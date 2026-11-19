# Changelog

All packages in this repository (`SimplyWorks.CloudFiles.*`) are released together with the same
version. `main` publishes the **10.0.x** line (net10.0). The **8.1.x** line (net8.0) ended at 8.1.13.

Entries marked **Behaviour change** alter what callers observe; read them before upgrading.

## 10.0.4

- **Behaviour change. S3 and Oracle: `ListAsync` returns every object under the prefix.** Both
  providers used to return only the first page of results (1,000 keys) and silently drop the rest,
  while Azure and Google already returned everything. S3 now follows `NextContinuationToken` and
  Oracle `NextStartWith`. Code that lists prefixes holding more than 1,000 objects now sees all of
  them, and takes one request per 1,000 keys. Covered by
  `S3CompatibleStoreTests.List_returns_every_object_beyond_the_first_page` (1,050 objects against
  SeaweedFS; fails on 10.0.3).
- Docs: [provider behaviour](docs/provider-behavior.md), [S3-compatible stores](docs/s3-compatible-stores.md),
  [evolving the contract](docs/evolving-the-contract.md), and this changelog.

## 10.0.3

- Packages are published through nuget.org trusted publishing. No code change.

## 10.0.2

- **S3: uploads to S3-compatible stores are no longer corrupted.** AWSSDK.S3 3.7.410+ sends `PutObject`
  bodies as `aws-chunked` with a trailing checksum by default; stores that don't decode it
  (SeaweedFS, older MinIO) saved the chunk framing inside the object. The client now computes
  checksums only when an operation requires one.
- **S3: a malformed `x-amz-expiration` response header no longer fails a successful request.** The
  SDK threw `FormatException` while parsing it after the request had succeeded; the header is now
  dropped before parsing.

## 10.0.1

- New `ICloudFilesLifecycle` (in PrimitiveTypes 10.0.1), registered by every provider: reads the
  bucket's age-based deletion rules back.
- Azure: the temp-prefix lifecycle rules are created through the storage account's management
  policy (Azure Resource Manager) when `SubscriptionId` and `ResourceGroupName` are set.
- **Behaviour change. S3: registering no longer deletes the bucket's other lifecycle rules.** The S3
  lifecycle API replaces the whole configuration; `AddS3CloudFiles` used to write only the missing temp
  rules and so removed every other rule. It now writes the merged set.

## 10.0.0

- Targets .NET 10 and depends on PrimitiveTypes 10.x. No API change from 8.1.13.

## 8.1.13 (last net8.0 release)

- Azure and Oracle: one SDK client per process (`ICloudFilesService` registered as a singleton)
  instead of one per resolution or request. Azure no longer makes a blocking container-listing call
  every time the service is resolved.

## 8.1.11

- S3: one `AmazonS3Client` per process (singleton) instead of one per resolution, with
  `TimeoutSeconds` (default 15) and `MaxErrorRetry` (default 2). Previously each resolution built a
  new client with the SDK's ~100-second timeout and its retries, so a slow endpoint made uploads hang
  for about a minute before failing.

## 8.1.6

- New `SW.CloudFiles.LocalTests` provider: a filesystem store for tests and local development.

## 8.1.4 – 8.1.5

- Automatic temp-prefix lifecycle rules (`temp1/`, `temp7/`, `temp30/`, `temp365/`) for S3, Google and
  Oracle, with `DisableAutoLifecycle` to opt out.

## 8.1.2

- Azure: managed identity support (`Managed`, `ManagedIdentityClientId`) and `PublicServiceUrl`.
