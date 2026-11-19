# S3-compatible stores

`SW.CloudFiles.S3` is one provider for every store that speaks the S3 API: Amazon S3, DigitalOcean
Spaces, MinIO, SeaweedFS, Cloudflare R2, Wasabi and others. You choose the store with `ServiceUrl`;
nothing else changes in your code.

"S3-compatible" doesn't mean feature parity, though. Each store implements a different subset of
S3, and SDK defaults that suit Amazon can break other stores. This page lists what the provider
relies on and what is known to differ.

## How the client is built

`CloudFilesOptionsExtensions.CreateClient()` configures `AmazonS3Client` for the widest compatibility:

| Setting | Value | Why |
|---|---|---|
| `ServiceURL` | `CloudFiles:ServiceUrl` | selects the store |
| `RegionEndpoint` | `us-east-1` | most non-AWS stores accept it as the signing region |
| `ForcePathStyle` | `true` | `{endpoint}/{bucket}/{key}`: no wildcard DNS or certificate per bucket needed |
| `UseHttp` | when `ServiceUrl` is `http://` | local MinIO or SeaweedFS |
| `Timeout` / `MaxErrorRetry` | `TimeoutSeconds` (15) / `MaxErrorRetry` (2) | fail fast instead of the SDK's ~100 s timeout with several retries |
| `RequestChecksumCalculation` / `ResponseChecksumValidation` | `WHEN_REQUIRED` | see below |

It also returns a `CloudFilesS3Client` subclass, which strips the `x-amz-expiration` response header
before the SDK parses it (see below).

### Fixes for non-AWS stores (10.0.2)

- **aws-chunked uploads.** Since AWSSDK.S3 3.7.410 the default is to send `PutObject` bodies as
  `aws-chunked` with a trailing CRC32. Stores that don't decode that encoding (SeaweedFS, older MinIO)
  saved the chunk framing as part of the object, so files read back with extra bytes around the
  content. The client now computes checksums only when an operation requires one.
  `S3CompatibleStoreTests` checks that uploads round-trip byte for byte against SeaweedFS.
- **Malformed `x-amz-expiration`.** When an object falls under a lifecycle rule, the store returns
  an `x-amz-expiration` header. Some stores format its date in a way the SDK can't parse, and the SDK
  then threw a `FormatException` *after* the request had already succeeded, so a successful write
  looked like a failure. The header is now dropped, because nothing in this library reads it.
  `S3ExpirationHeaderTests` covers it with a local `HttpListener`.

## What happens at startup

`AddS3CloudFiles()` calls the store **synchronously, during DI registration**:

1. It checks whether the bucket exists and creates it if not (`PutBucket`).
2. Unless `DisableAutoLifecycle = true`, it reads the bucket's lifecycle configuration and writes it
   back with the `temp1/`, `temp7/`, `temp30/` and `temp365/` expiration rules merged in. Every other
   rule is kept; before 10.0.1 the other rules were deleted.

Neither step catches errors. If the store is unreachable, or the key lacks permission to check or
create buckets or to read and write lifecycle configuration, **the application fails to start**.
For a key scoped to a single object prefix, or a store without a lifecycle API, set
`DisableAutoLifecycle = true` and create the bucket yourself.

## Store notes

| | Amazon S3 | DigitalOcean Spaces | MinIO | SeaweedFS |
|---|---|---|---|---|
| Basic object API (put, get, head, list v2, delete) | ✅ | ✅ | ✅ | ✅ |
| `ListObjectsV2` paging (used by `ListAsync`) | ✅ | ✅ | ✅ | ✅ (tested) |
| Presigned GET and PUT (`GetSignedUrl`, `OpenWrite`) | ✅ | ✅ | ✅ | ✅ (GET tested) |
| Canned ACL `public-read` (`WriteFileSettings.Public`) | ✅ only if the bucket allows ACLs (new buckets default to "bucket owner enforced", which rejects ACLs) | ✅ (`private` and `public-read` only) | no object ACLs; public access comes from a bucket policy | accepted; public access comes from configuration |
| Lifecycle expiration (temp rules) | ✅ | ✅ | ✅ | partial, depending on version: disable it if startup fails |
| aws-chunked uploads | ✅ | ✅ | recent versions | ❌ (fixed by `WHEN_REQUIRED`) |

Check the store's own compatibility documentation before relying on anything beyond the basic object
API, and pin the store version you test against.

### Making files public

`WriteFileSettings.Public = true` sends the `public-read` canned ACL. That only works where object
ACLs work. On Amazon S3 buckets with ACLs disabled the upload is **rejected**, and on MinIO the ACL
has no effect. For a store like that, make a prefix public with a bucket policy, write with
`Public = false`, and use `GetUrl` for links to that prefix.

### Local development and tests

- MinIO or SeaweedFS in Docker gives a real S3 API. `S3CompatibleStoreTests` shows a Testcontainers
  setup for SeaweedFS (it needs an identity configured before it accepts signed requests).
- Presigned URLs are always signed as `https`. Against a plain-`http` local store, swap the scheme
  before using the URL; the scheme isn't part of the signature.
- `SW.CloudFiles.LocalTests` (filesystem) is faster for unit tests, but it doesn't exercise S3
  behaviour: listing, metadata casing, ACLs and errors all differ (see
  [provider behaviour](provider-behavior.md)).
