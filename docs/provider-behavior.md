# Provider behaviour reference

`ICloudFilesService` gives every provider the same method signatures, but not always the same
behaviour. This page records, method by method, what each provider actually does as of **10.0.4**,
read from the code in this repository. Write code against the "portable" column; anything outside it
works on some providers only.

Providers: **S3** (`SW.CloudFiles.S3`, any S3-compatible store), **Azure** (`SW.CloudFiles.AS`),
**Google** (`SW.CloudFiles.GC`), **Oracle** (`SW.CloudFiles.OC`), **Local** (`SW.CloudFiles.LocalTests`,
tests and local development only).

## Summary

| Method | S3 | Azure | Google | Oracle | Local | Portable? |
|---|---|---|---|---|---|---|
| `WriteAsync` | ✅ | ✅ | ✅ metadata and `Public` ignored | ✅ | ✅ | Yes, but see `Public` and `Metadata` |
| `WriteTextAsync` | ✅ metadata ignored | ✅ | ✅ metadata and `Public` ignored | ✅ | ✅ | Yes, but see `Metadata` |
| `OpenReadAsync` | network stream | network stream | **whole object buffered in memory** | network stream | file stream | Yes |
| `ListAsync` | all pages (since 10.0.4) | all pages | all pages | all pages (since 10.0.4) | all files | Yes |
| `GetMetadataAsync` | user + `Hash`, `ContentType`, `ContentLength` | user + `Hash`, `ContentType`, `ContentLength` | **user metadata only** | user + `Hash`, `ContentType` | user + `ContentLength`, `LastModified` | Only user metadata is portable |
| `DeleteAsync` | `true`, even when missing | `true`; **throws** when missing | `true`; `false` when missing | `true`; `false` on **any** error | `true`, even when missing | Don't rely on the return value |
| `GetUrl` | path-style URL | container URL | `storage.googleapis.com` URL | Object Storage URL | `file://` URI | Yes; works only for public objects |
| `GetSignedUrl` | presigned GET, local | SAS; **network call** with managed identity | V4 signed GET, local | **creates a PAR on every call** | `file://` URI, expiry ignored | Yes, but cost differs |
| `OpenWrite` | presigned PUT | **throws** | ⚠️ expected to fail (see below) | **throws** | **throws** | **No: S3 only** |
| `ICloudFilesLifecycle.GetLifecycleAsync` | ✅ | ✅ once ARM is configured | ✅ | ✅ | no rules | Yes |

## Writing

### `WriteAsync(Stream, WriteFileSettings)` and `WriteTextAsync(string, WriteFileSettings)`

Both overwrite an existing object with the same key on every provider.

| Setting | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| `ContentType` default | `application/octet-stream` (stream), `text/plain` (text) | same as S3 | none sent (the store decides) | same as S3 | not stored |
| `Public = true` | canned ACL `public-read` (stores without ACLs ignore it, see [S3-compatible stores](s3-compatible-stores.md)) | ignored: access is set on the container | ignored: access is set on the bucket | ignored: access is set on the bucket | ignored |
| `Metadata` on `WriteAsync` | stored as `x-amz-meta-*` | stored | **ignored** | stored as `opc-meta-*` | sidecar `.meta.json` |
| `Metadata` on `WriteTextAsync` | **ignored** | stored | **ignored** | stored | sidecar `.meta.json` |
| `CloseInputStream` | honoured | ignored (stream left open) | ignored | ignored | ignored |

The returned `RemoteBlob` also differs:

| Field | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| `Location` | `GetUrl` when `Public`, else a 1-hour signed URL | always the plain blob URL | `GetUrl` when `Public`, else a 1-hour signed URL | always `GetUrl` | `file://` URI |
| `Name` | key | key | key | **not set** | key |
| `Size` | `inputStream.Length` / **`text.Length` in characters** | `inputStream.Length` / not set for text | object size reported by the store | **not set** | bytes on disk |

Consequences:

- **Non-seekable streams.** S3 reads `inputStream.Length` before the upload and Azure reads it after,
  so a stream that can't report its length (a network or compressed stream) throws on both. Copy it
  into a `MemoryStream` or a temp file first.
- **`RemoteBlob.Size` is an `int`.** Objects over 2 GiB overflow it. `CloudFileInfo.Size` (from
  `ListAsync`) is a `long` and is correct.
- **Don't store `RemoteBlob.Location` for private files.** A signed URL expires after one hour, and
  Azure and Oracle return an unsigned URL that a private bucket refuses. Store the key and generate a
  URL when it is needed.

### `OpenWrite(WriteFileSettings)`

Returns a `WriteWrapper` that streams the body of a single HTTP PUT. Only S3 implements it properly
(a presigned PUT URL, with `x-amz-acl: public-read` when `Public`). Azure, Oracle and Local throw
`NotImplementedException`.

Google builds the wrapper from `GetSignedUrl`, which signs a **GET** URL, then sends a PUT to it.
Google Cloud Storage checks the signed method, so this is expected to fail with a signature
mismatch. No test covers it. Treat `OpenWrite` as S3-only and prefer `WriteAsync`.

`WriteWrapper` is built on the obsolete `HttpWebRequest` and lives in PrimitiveTypes, so it is a poor
base for new code.

## Reading

### `OpenReadAsync(string key)`

Returns a stream the caller must dispose. S3, Azure and Oracle return the live response stream, so
large objects are streamed. **Google downloads the whole object into a `MemoryStream` first**, so a
1 GB object costs 1 GB of memory on that provider.

A missing key throws, with a different exception on each provider: `AmazonS3Exception` (404,
`NoSuchKey`), `Azure.RequestFailedException` (404), `Google.GoogleApiException` (404),
`Oci.Common.Model.OciException` (404) and `FileNotFoundException` (Local). There is no common
"not found" exception, so portable code can't catch it reliably. Check first, or catch `Exception`.

### `ListAsync(string prefix)`

Returns every object whose key starts with `prefix`, recursively: there is no delimiter, so nested
"folders" are included.

- **Paging.** Every provider returns the complete listing. Before **10.0.4**, S3 and Oracle returned
  only the first page (1,000 keys) and silently dropped the rest, while Azure and Google walked every
  page. Code that ran on S3 or Oracle with an older version and listed big prefixes may have been
  working on partial data.
- **Cost.** A full listing is one request per 1,000 keys, and the result is materialised in memory.
  Listing a bucket root with millions of objects is slow and memory-hungry. Keep prefixes narrow.
- **Folder placeholders.** Consoles and some tools create zero-byte objects whose key ends in `/`. S3
  and Google return them like any other object, so filter out keys ending in `/` (or `Size == 0`) when
  you only want files.
- **`CloudFileInfo.Signature`:** S3 returns the ETag (with its quotes), Azure the ETag, Google the
  base64 MD5 (empty for composite objects), and Oracle and Local return `null`. Treat it as an opaque
  change marker, not a checksum: an S3 multipart ETag is not an MD5 of the content.
- **Existence checks.** `ListAsync(key).Any()` is a common way to ask "does this key exist?", but it
  also matches any key that merely starts with `key` (`a/b` matches `a/b.json`). Compare keys exactly:
  `(await files.ListAsync(key)).Any(f => f.Key == key)`.

### `GetMetadataAsync(string key)`

Returns user metadata plus some system values. Only user metadata is portable:

| Key | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| user metadata | ✅ (prefix stripped) | ✅ | ✅ | ✅ | ✅ |
| `Hash` (ETag, quotes removed) | ✅ | ✅ | ❌ | ✅ | ❌ |
| `ContentType` | ✅ | ✅ | ❌ | ✅ | ❌ |
| `ContentLength` | ✅ | ✅ | ❌ | ❌ | ✅ |
| `LastModified` | ❌ | ❌ | ❌ | ❌ | ✅ |
| case-insensitive keys | ✅ | ❌ | ❌ | ✅ | ✅ |

S3 and some compatible stores lowercase user-metadata keys on the way in, so read them
case-insensitively, or write them lowercase in the first place. A missing key throws, as for
`OpenReadAsync`.

## Deleting

### `DeleteAsync(string key)`

| Case | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| object existed | `true` | `true` | `true` | `true` | `true` |
| object missing | `true` (S3 answers 204 either way) | **throws** `RequestFailedException` 404 | `false` | `false` | `true` |
| other failure | throws | throws | throws | **`false`, error logged** | throws |

The return value doesn't mean the same thing everywhere. If you need to know whether the object
existed, check before deleting. If you need delete-if-present without errors, wrap the call so that
it also tolerates Azure's 404.

## URLs

### `GetUrl(string key)`

Builds the object's permanent address without calling the store. It works only if the object or
bucket is publicly readable.

| Provider | Shape |
|---|---|
| S3 | `{ServiceUrl}/{bucket}/{key}` (path style, built by presigning and stripping the query) |
| Azure | `{PublicServiceUrl}/{container}/{key}`, or the container URL when `PublicServiceUrl` is not set |
| Google | `https://storage.googleapis.com/{bucket}/{key}` |
| Oracle | `https://objectstorage.{Region}.oraclecloud.com/n/{namespace}/b/{bucket}/o/{escaped key}` |
| Local | `file://` URI of the stored file |

### `GetSignedUrl(string key, TimeSpan expiry)`

Every provider returns a read-only (GET) URL, but at very different cost:

| Provider | Mechanism | Cost per call |
|---|---|---|
| S3 | SigV4 presigned URL | local computation |
| Azure, shared key | service SAS | local computation |
| Azure, managed identity | user delegation SAS | a **blocking network call** for a delegation key each time |
| Google | V4 signed URL | local computation |
| Oracle | pre-authenticated request (PAR) | a **blocking network call** that creates a server-side PAR each time; PARs remain until they expire |
| Local | `file://` URI | expiry ignored |

On Oracle, and on Azure with managed identity, avoid generating signed URLs in loops or per list
row. Generate them when a user actually opens the file.

The S3 SDK always signs `https` URLs, even when `ServiceUrl` is `http` (a local MinIO or SeaweedFS).
Swap the scheme yourself in local tests, as `S3CompatibleStoreTests.DownloadRaw` does.

## Lifetimes and threading

| Provider | `ICloudFilesService` lifetime | Underlying client |
|---|---|---|
| S3 | singleton | one `AmazonS3Client`, timeout `TimeoutSeconds` (15 s), `MaxErrorRetry` (2) |
| Azure | singleton | one `BlobContainerClient` |
| Google | scoped | singleton `StorageClient` and `UrlSigner` |
| Oracle | singleton | one `ObjectStorageClient` |
| Local | singleton | none |

All the SDK clients are thread-safe. Creating `CloudFilesService` yourself (outside DI, as an adapter
with per-run credentials might) is fine, but dispose it: S3 and Oracle hold an HTTP client.

## Not in the contract

These operations are **not** available through `ICloudFilesService` on any provider: server-side
copy or move, existence or properties without a listing, range reads, conditional writes, batch
delete, paginated or streamed listing, versions, tags, and `CancellationToken`s. Code that needs one
today has to use the provider's SDK directly, which ties it to that provider. See
[Evolving the contract](evolving-the-contract.md) for which of these are planned and how they will be
added without breaking existing implementations.
