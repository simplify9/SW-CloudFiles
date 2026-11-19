# Provider behaviour reference

`ICloudFilesService` gives every provider the same method signatures, but not always the same
behaviour. This page records, method by method, what each provider actually does as of **10.0.5**,
read from the code in this repository. Write code against the "portable" column; anything outside it
works on some providers only. The [changelog](../CHANGELOG.md) lists what changed in each version.

Providers: **S3** (`SW.CloudFiles.S3`, any S3-compatible store), **Azure** (`SW.CloudFiles.AS`),
**Google** (`SW.CloudFiles.GC`), **Oracle** (`SW.CloudFiles.OC`), **Local** (`SW.CloudFiles.LocalTests`,
tests and local development only).

## Summary

| Method | S3 | Azure | Google | Oracle | Local | Portable? |
|---|---|---|---|---|---|---|
| `WriteAsync` | ✅ | ✅ | ✅ `Public` ignored | ✅ | ✅ | Yes, but see `Public` |
| `WriteTextAsync` | ✅ | ✅ | ✅ `Public` ignored | ✅ | ✅ | Yes, but see `Public` |
| `OpenReadAsync` | network stream | network stream | **whole object buffered in memory** | network stream | file stream | Yes |
| `ListAsync` | all pages | all pages | all pages | all pages | all files | Yes |
| `GetMetadataAsync` | user + `Hash`, `ContentType`, `ContentLength` | same | same | same | user + `ContentLength`, `LastModified` | Yes, except Local's system keys |
| `DeleteAsync` | `true`, **even when missing** | `false` when missing | `false` when missing | `false` on **any** error | `false` when missing | Mostly: S3 can't tell a missing key |
| `GetUrl` | path-style URL | container URL | `storage.googleapis.com` URL | Object Storage URL | `file://` URI | Yes; works only for public objects |
| `GetSignedUrl` | presigned GET, local | SAS; **network call** with managed identity | V4 signed GET, local | **creates a PAR on every call** | `file://` URI, expiry ignored | Yes, but cost differs |
| `OpenWrite` | presigned PUT | throws | throws | throws | throws | **No: S3 only** |
| `ICloudFilesCopy.CopyAsync` | CopyObject (≤ 5 GB), copy is private | copy within the account | CopyObject | work request, polled | file copy | Yes |
| `ICloudFilesProperties.GetPropertiesAsync` | HEAD | Get Properties | object metadata | HEAD | file info, no content type | Yes |
| `ICloudFilesLifecycle.GetLifecycleAsync` | ✅ | ✅ once ARM is configured | ✅ | ✅ | no rules | Yes |

Every provider registers `ICloudFilesService`, `ICloudFilesLifecycle`, `ICloudFilesCopy` and
`ICloudFilesProperties` in DI, all resolving to the same `CloudFilesService` instance.

## Writing

### `WriteAsync(Stream, WriteFileSettings)` and `WriteTextAsync(string, WriteFileSettings)`

Both overwrite an existing object with the same key on every provider.

| Setting | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| `ContentType` default | `application/octet-stream` (stream), `text/plain` (text) | same as S3 | none sent (the store decides) | same as S3 | not stored |
| `Public = true` | canned ACL `public-read` (stores without ACLs ignore it, see [S3-compatible stores](s3-compatible-stores.md)) | ignored: access is set on the container | ignored: access is set on the bucket | ignored: access is set on the bucket | ignored |
| `Metadata` | stored as `x-amz-meta-*` | stored | stored | stored as `opc-meta-*` | sidecar `.meta.json` |
| `CloseInputStream` | honoured | ignored (stream left open) | ignored | ignored | ignored |

Before 10.0.5, Google ignored `Metadata` on every write, and S3 ignored it on `WriteTextAsync`.

The returned `RemoteBlob` also differs:

| Field | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| `Location` | `GetUrl` when `Public`, else a 1-hour signed URL | always the plain blob URL | `GetUrl` when `Public`, else a 1-hour signed URL | always `GetUrl` | `file://` URI |
| `Name` | key | key | key | key | key |
| `Size` | bytes | bytes (stream) / not set (text) | bytes reported by the store | bytes (text, seekable stream) / 0 | bytes on disk |

Consequences:

- **Non-seekable streams.** S3 reads `inputStream.Length` before the upload and Azure reads it after,
  so a stream that can't report its length (a network or compressed stream) throws on both. Copy it
  into a `MemoryStream` or a temp file first.
- **`RemoteBlob.Size` is an `int`.** Objects over 2 GiB overflow it. `CloudFileInfo.Size` (from
  `ListAsync`) and `CloudFileProperties.ContentLength` are `long` and are correct.
- **Don't store `RemoteBlob.Location` for private files.** A signed URL expires after one hour, and
  Azure and Oracle return an unsigned URL that a private bucket refuses. Store the key and generate a
  URL when it is needed.

### `OpenWrite(WriteFileSettings)`

Returns a `WriteWrapper` that streams the body of a single HTTP PUT. Only S3 implements it (a
presigned PUT URL, with `x-amz-acl: public-read` when `Public`). Azure, Google, Oracle and Local throw
`NotImplementedException`. Before 10.0.5, Google sent the PUT to a URL signed for GET, which Google
Cloud Storage refuses. `WriteWrapper` is built on the obsolete `HttpWebRequest`, so prefer
`WriteAsync`.

## Reading

### `OpenReadAsync(string key)`

Returns a stream the caller must dispose. S3, Azure and Oracle return the live response stream, so
large objects are streamed. **Google downloads the whole object into a `MemoryStream` first**, so a
1 GB object costs 1 GB of memory on that provider.

A missing key throws, with a different exception on each provider: `AmazonS3Exception` (404,
`NoSuchKey`), `Azure.RequestFailedException` (404), `Google.GoogleApiException` (404),
`Oci.Common.Model.OciException` (404) and `FileNotFoundException` (Local). There is no common
"not found" exception. To check first, use `ICloudFilesProperties` (below), which returns `null` for
a missing key on every provider.

### `ListAsync(string prefix)`

Returns every object whose key starts with `prefix`, recursively: there is no delimiter, so nested
"folders" are included.

- **Paging.** Every provider returns the complete listing. Before **10.0.4**, S3 and Oracle returned
  only the first page (1,000 keys) and silently dropped the rest.
- **Cost.** A full listing is one request per 1,000 keys, and the result is held in memory. Listing a
  bucket root with millions of objects is slow and memory-hungry. Keep prefixes narrow.
- **Folder placeholders.** Consoles and some tools create zero-byte objects whose key ends in `/`. S3
  and Google return them like any other object, so filter out keys ending in `/` (or `Size == 0`) when
  you only want files.
- **`CloudFileInfo.Signature`:** S3 returns the ETag (with its quotes), Azure the ETag, Google the
  base64 MD5 (empty for composite objects), and Oracle and Local return `null`. Treat it as an opaque
  change marker, not a checksum: an S3 multipart ETag is not an MD5 of the content.
- **Not an existence check.** `ListAsync(key).Any()` also matches any key that merely starts with
  `key` (`a/b` matches `a/b.json`). Use `ICloudFilesProperties.ExistsAsync(key)`, or compare keys
  exactly.

### `GetMetadataAsync(string key)`

Returns user metadata plus system values:

| Key | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| user metadata | ✅ (prefix stripped) | ✅ | ✅ | ✅ | ✅ |
| `Hash` (ETag, quotes removed) | ✅ | ✅ | ✅ | ✅ | ❌ |
| `ContentType` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `ContentLength` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `LastModified` | ❌ | ❌ | ❌ | ❌ | ✅ |
| case-insensitive keys | ✅ | ✅ | ✅ | ✅ | ✅ |

Before 10.0.5, Google returned user metadata only, Oracle had no `ContentLength`, and Azure and Google
keys were case-sensitive. S3 and some compatible stores lowercase user-metadata keys on the way in,
so read them case-insensitively. A missing key throws, as for `OpenReadAsync`.

### `ICloudFilesProperties.GetPropertiesAsync(string key, CancellationToken)`

One request, whatever the object's size. Returns `null` when no object has **exactly** that key, on
every provider, so it works both as a portable "not found" and as an existence check
(`ExistsAsync`).

| Field | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| `ContentLength` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `ContentType` | ✅ | ✅ | ✅ | ✅ | `null` (not stored) |
| `ETag` (quotes removed) | ✅ | ✅ | ✅ | ✅ | from size and write time |
| `LastModified` | ✅ | ✅ | ✅ (`null` if unparseable) | ✅ | ✅ |
| `Metadata`, case-insensitive | ✅ | ✅ | ✅ | ✅ | ✅ |

## Copying

### `ICloudFilesCopy.CopyAsync(string sourceKey, string destinationKey, CancellationToken)`

Copies inside the bucket on the store itself; no bytes pass through the application. Replaces the
destination if it exists. A missing source throws, as `OpenReadAsync` does.

| | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| Mechanism | one `CopyObject` request | `StartCopyFromUri`, then waits | one `CopyObject` request | `CopyObject` work request, polled every second | `File.Copy` |
| Content type and metadata | copied | copied | copied | copied | metadata sidecar copied |
| Visibility | **private**, whatever the source's ACL | the container's | the bucket's | the bucket's | n/a |
| Size limit | **5 GB** (larger needs multipart copy, not implemented) | none | none | none | none |
| Tested | SeaweedFS | Azurite | fake-gcs-server | **not tested** (needs a live tenancy) | yes |

A move is `CopyAsync` followed by `DeleteAsync`. It is never atomic: if the delete fails, both copies
exist.

## Deleting

### `DeleteAsync(string key)`

| Case | S3 | Azure | Google | Oracle | Local |
|---|---|---|---|---|---|
| object existed | `true` | `true` | `true` | `true` | `true` |
| object missing | `true` (S3 answers 204 either way) | `false` | `false` | `false` | `false` |
| other failure | throws | throws | throws | **`false`, error logged** | throws |

Before 10.0.5, Azure threw on a missing blob and Local returned `true`. S3 still can't tell a missing
key from a deleted one without an extra request; call `ExistsAsync` first if it matters.

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

On Oracle, and on Azure with a managed identity, avoid generating signed URLs in loops or per list
row. Generate them when a user actually opens the file.

The S3 SDK always signs `https` URLs, even when `ServiceUrl` is `http` (a local MinIO or SeaweedFS).
Swap the scheme yourself in local tests, as `S3CompatibleStoreTests.DownloadRaw` does.

## Lifetimes and threading

| Provider | Lifetime of all four interfaces | Underlying client |
|---|---|---|
| S3 | singleton | one `AmazonS3Client`, timeout `TimeoutSeconds` (15 s), `MaxErrorRetry` (2) |
| Azure | singleton | one `BlobContainerClient` |
| Google | scoped | singleton `StorageClient` and `UrlSigner` |
| Oracle | singleton | one `ObjectStorageClient` |
| Local | singleton | none |

All the SDK clients are thread-safe. Creating `CloudFilesService` yourself (outside DI, as an adapter
with per-run credentials might) is fine, but dispose it: S3 and Oracle hold an HTTP client.

## Not in the contract

These operations are still **not** available on any provider: range reads, conditional writes,
batch delete, streamed or paged listing, upload URLs, versions and tags. `CancellationToken` exists
only on the newer interfaces (`ICloudFilesLifecycle`, `ICloudFilesCopy`, `ICloudFilesProperties`).
See [Evolving the contract](evolving-the-contract.md) for what is planned and how it will be added
without breaking existing implementations.
