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

## Key Dependencies

- `SimplyWorks.PrimitiveTypes` v8.1.3 — shared interface and base types (all providers)
- `AWSSDK.S3` — S3 provider
- `Azure.Storage.Blobs` + `Azure.Identity` — Azure provider (Managed Identity support)
- `Google.Cloud.Storage.V1` — GCP provider
- `Oci.ObjectstorageService` — Oracle provider
