# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

SW.CloudFiles is a multi-cloud file storage abstraction library for .NET 8. It provides a unified `ICloudFilesService` interface (defined in `SimplyWorks.PrimitiveTypes`) implemented across four cloud providers — AWS S3, Azure Blob Storage, Google Cloud Storage, and Oracle Cloud Object Storage — plus a local filesystem provider intended for testing and local development.

## Build & Test Commands

```bash
# Build the entire solution
dotnet build SW.CloudFiles.sln

# Run all tests
dotnet test SW.CloudFiles.sln

# Run tests for a specific provider
dotnet test SW.CloudFiles.UnitTests/SW.CloudFiles.UnitTests.csproj
dotnet test SW.CloudFiles.AS.UnitTest/SW.CloudFiles.AS.UnitTest.csproj
dotnet test SW.CloudFiles.GC.UnitTests/SW.CloudFiles.GC.UnitTests.csproj
dotnet test SW.CloudFiles.OC.UnitTest/SW.CloudFiles.OC.UnitTest.csproj

# Pack a NuGet package
dotnet pack SW.CloudFiles.S3/SW.CloudFiles.S3.csproj -c Release
```

NuGet publishing is handled by CI on push to `main` (see `.github/workflows/nuget-publish.yml`). Tests are currently disabled in CI (`run-tests: 'false'`).

## Project Structure

Each cloud provider is split into two packages:

| Core Project | Extensions Project | NuGet Package Prefix |
|---|---|---|
| `SW.CloudFiles.S3/` | `SW.CloudFiles.S3.Extensions/` | `SimplyWorks.CloudFiles.S3` |
| `SW.CloudFiles.AS/` | `SW.CloudFiles.AS.Extensions/` | `SimplyWorks.CloudFiles.AS` |
| `SW.CloudFiles.GC/` | `SW.CloudFiles.GC.Extensions/` | `SimplyWorks.CloudFiles.GC` |
| `SW.CloudFiles.OC/` | `SW.CloudFiles.OC.Extensions/` | `SimplyWorks.CloudFiles.OC` |
| `SW.CloudFiles.LocalTests/` | `SW.CloudFiles.LocalTests.Extensions/` | `SimplyWorks.CloudFiles.LocalTests` *(testing only)* |

- **Core projects** contain `CloudFilesService` (implements `ICloudFilesService`) and provider-specific `Options` class.
- **Extensions projects** contain `IServiceCollectionExtensions` for ASP.NET Core DI registration and helper extension methods for creating SDK clients from options.

## Architecture

### Interface Contract

All providers implement `ICloudFilesService` from `SimplyWorks.PrimitiveTypes`:

```csharp
WriteAsync(Stream, WriteFileSettings) -> RemoteBlob
WriteTextAsync(string, WriteFileSettings) -> RemoteBlob
OpenReadAsync(string key) -> Stream
ListAsync(string prefix) -> IEnumerable<CloudFileInfo>
GetMetadataAsync(string key) -> IReadOnlyDictionary<string,string>
DeleteAsync(string key) -> bool
GetUrl(string key) -> string
GetSignedUrl(string key, TimeSpan expiry) -> string
OpenWrite(WriteFileSettings) -> WriteWrapper
```

Note: `GetSignedUrl` throws `NotImplementedException` in Azure and Oracle providers.

### Options Hierarchy

`CloudFilesOptions` (base, from PrimitiveTypes) holds `AccessKeyId`, `SecretAccessKey`, `BucketName`, `ServiceUrl`. Provider-specific subclasses add their own fields:

- `AzureCloudFilesOptions` — adds `Managed` (bool), `ManagedIdentityClientId`, `PublicServiceUrl`
- `GoogleCloudFilesOptions` — adds full service account JSON fields (`ProjectId`, `PrivateKey`, `ClientEmail`, etc.)
- `OracleCloudFilesOptions` — adds `TenantId`, `UserId`, `FingerPrint`, `RSAKey`, `Region`, `NamespaceName`; also provides `GetFileUrl()` helper

### DI Registration Pattern

Each Extensions project exposes a single method on `IServiceCollection`:

```csharp
services.AddS3CloudFiles(options => { ... });       // or bind from IConfiguration
services.AddAsCloudFiles(options => { ... });
services.AddGoogleCloudFiles(options => { ... });
services.AddOracleCloudFiles(options => { ... });
```

Configuration is read from the `"CloudFiles"` section in `appsettings.json` (`CloudFilesOptions.ConfigurationSection`), with optional programmatic override via the `Action<TOptions>` delegate.

### Automatic Lifecycle Rules

Three providers automatically create delete rules for temp-prefix objects at DI registration time. All three check for existing rules and only add missing ones.

| Provider | Mechanism | Opt-out |
|----------|-----------|---------|
| S3 | S3 Lifecycle Configuration API | `S3CloudFilesOptions.DisableAutoLifecycle = true` |
| GCS | GCS Bucket Lifecycle API (`PatchBucket`) | `GoogleCloudFilesOptions.DisableAutoLifecycle = true` |
| Oracle | OCI Object Lifecycle Policy API (`PutObjectLifecyclePolicy`) | `OracleCloudFilesOptions.DisableAutoLifecycle = true` |
| Azure | **Not automatic** — ARM plane required | N/A (property exists but is no-op) |
| LocalTests | N/A — call `CloudFilesService.Cleanup()` in test teardown | N/A |

Lifecycle prefixes: `temp1/` (1 day), `temp7/` (7 days), `temp30/` (30 days), `temp365/` (365 days).

S3 and GCS also auto-create the bucket if it does not exist.

### GCS: `StorageClient` and `UrlSigner` via DI

`CloudFilesService` (GCS) takes `GoogleCloudFilesOptions`, `StorageClient`, and `UrlSigner` via constructor injection. `AddGoogleCloudFiles` registers all three as singletons. `BuildGoogleCloudStorageClient()` and `BuildUrlSigner()` are public extension methods on `GoogleCloudFilesOptions` in the Extensions project.

### Oracle-Specific: Config File Auth

`AddOracleCloudFiles` writes PEM and OCI config files to the entry assembly directory, sets `OracleCloudFilesOptions.ConfigPath`, then creates a temporary `ObjectStorageClient` for lifecycle setup if enabled. The service constructor also creates its own `ObjectStorageClient` from `ConfigPath`.

### LocalTests Provider

`AddLocalTestsCloudFiles` registers `CloudFilesService` (concrete) as a singleton **in addition to** the `ICloudFilesService` interface alias, so test classes can inject `CloudFilesService` directly and call `Cleanup()` in teardown. Storage root defaults to `Path.GetTempPath()/SW.CloudFiles.LocalTests/{BucketName}` via `LocalTestsCloudFilesOptions.ResolvedStoragePath`. Metadata is persisted as sidecar `.meta.json` files alongside each stored blob. `GetSignedUrl` returns the same `file://` URI as `GetUrl`. `OpenWrite` throws `NotImplementedException`.

## Known Issues / DI Lifetime Audit (2026-08-25)

A production incident on 2026-08-25 (invoice-attachment and shipment-label uploads hanging ~1 minute then failing) traced back to the S3 provider building a brand-new `AmazonS3Client` on every DI resolution (`ICloudFilesService` was `AddTransient`) with no explicit timeout, so any latency to the storage endpoint compounded through the AWS SDK's default ~100s timeout and several backoff retries. Fixed in 8.1.11 (S3 provider): `ICloudFilesService` is now `AddSingleton`, and `S3CloudFilesOptions.TimeoutSeconds`/`MaxErrorRetry` (defaults 15/2) are configurable via the `CloudFiles` config section or the `AddS3CloudFiles(options => ...)` delegate.

Prompted by that incident, the other three network-backed providers were audited for the same class of bug (fresh client construction on every resolution instead of reusing a pooled, thread-safe SDK client):

- **Azure (`SW.CloudFiles.AS`) — same bug, and worse. Fixed alongside S3.** `ICloudFilesService` was also `AddTransient`, and `CloudFilesService`'s constructor called `cloudFilesOptions.CreateClient()` itself rather than using the `BlobContainerClient` the DI extension registered as a singleton — that singleton registration was dead code, never actually injected anywhere. Worse than the S3 case: in the non-managed-identity (shared-key) auth path, `CreateClient()` does a **synchronous, blocking `GetBlobContainers()` list call** plus an existence check — a real network round-trip, not just a fresh TCP/TLS handshake — on every single construction. Fixed: `CloudFilesService` now takes `BlobContainerClient` as a constructor parameter (reusing the singleton), and `ICloudFilesService` is registered `AddSingleton`.
- **Oracle (`SW.CloudFiles.OC`) — milder version, fixed alongside S3.** Was `AddScoped` (so only once per HTTP request rather than per resolution, unlike S3/Azure's `AddTransient`), but the constructor still built a fresh `ObjectStorageClient` + `UploadManager` per request scope, including a `ConfigFileAuthenticationDetailsProvider` re-reading the PEM/config file from disk each time. Fixed: registered `AddSingleton` instead, so the client is built once for the process lifetime.
- **Google Cloud (`SW.CloudFiles.GC`) — already correct, no change needed.** `ICloudFilesService` is `AddScoped`, but the actual `StorageClient`/`UrlSigner` doing the I/O are `AddSingleton` and injected in — no repeated client construction regardless of the wrapper service's lifetime. This is the reference-correct pattern the other providers now follow.
- **LocalTests** — file-based, no network client involved, not applicable.

None of Azure/GCS/Oracle are referenced by any Traxis service (only the S3 provider is, via `AddS3CloudFiles()` in all 8 backend microservices) — this was pure library-quality debt with no live-incident urgency, unlike the S3 fix.

Note: `SW.CloudFiles.AS.UnitTest` and `SW.CloudFiles.OC.UnitTest` fail locally regardless of these changes (confirmed by reverting and re-running) — they appear to be integration tests requiring live cloud credentials, consistent with CI's `run-tests: 'false'` in `nuget-publish.yml`. Not a regression from this audit.

## Key Dependencies

- `SimplyWorks.PrimitiveTypes` v8.1.3 — shared interface and base types (all providers)
- `AWSSDK.S3` — S3 provider
- `Azure.Storage.Blobs` + `Azure.Identity` — Azure provider (Managed Identity support)
- `Google.Cloud.Storage.V1` — GCP provider
- `Oci.ObjectstorageService` — Oracle provider
