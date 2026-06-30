# SW.CloudFiles

[![Build and Publish NuGet Package](https://github.com/simplify9/SW-CloudFiles/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/simplify9/SW-CloudFiles/actions/workflows/nuget-publish.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Contributions welcome](https://img.shields.io/badge/contributions-welcome-orange.svg)](CONTRIBUTING.md)

| **Package** | **Version** |
|-------------|-------------|
| SimplyWorks.CloudFiles.S3 | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.S3.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.S3/) |
| SimplyWorks.CloudFiles.S3.Extensions | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.S3.Extensions.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.S3.Extensions/) |
| SimplyWorks.CloudFiles.AS | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.AS.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.AS/) |
| SimplyWorks.CloudFiles.AS.Extensions | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.AS.Extensions.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.AS.Extensions/) |
| SimplyWorks.CloudFiles.GC | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.GC.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.GC/) |
| SimplyWorks.CloudFiles.GC.Extensions | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.GC.Extensions.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.GC.Extensions/) |
| SimplyWorks.CloudFiles.OC | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.OC.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.OC/) |
| SimplyWorks.CloudFiles.OC.Extensions | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.OC.Extensions.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.OC.Extensions/) |
| SimplyWorks.CloudFiles.LocalTests | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.LocalTests.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.LocalTests/) |
| SimplyWorks.CloudFiles.LocalTests.Extensions | [![NuGet](https://img.shields.io/nuget/v/SimplyWorks.CloudFiles.LocalTests.Extensions.svg)](https://www.nuget.org/packages/SimplyWorks.CloudFiles.LocalTests.Extensions/) |

## Introduction

**SW.CloudFiles** is a unified, multi-cloud storage abstraction library for .NET 8 that provides a consistent interface across different cloud storage providers. It simplifies cloud storage operations by offering a single API that works with multiple cloud providers without vendor lock-in.

### Supported Cloud Providers

- **🚀 S3-Compatible Storage** (AWS S3, DigitalOcean Spaces, MinIO, etc.)
- **☁️ Azure Blob Storage**
- **🌐 Google Cloud Storage**
- **🔶 Oracle Cloud Storage**
- **🧪 Local Filesystem** — for testing and local development only

The library provides both core functionality and ASP.NET Core dependency injection extensions for easy integration into web applications.

## Installation

Choose the appropriate package based on your cloud storage provider:

### S3-Compatible Storage (AWS S3, DigitalOcean Spaces, MinIO, etc.)
```bash
dotnet add package SimplyWorks.CloudFiles.S3.Extensions
```

### Azure Blob Storage
```bash
dotnet add package SimplyWorks.CloudFiles.AS.Extensions
```

### Google Cloud Storage
```bash
dotnet add package SimplyWorks.CloudFiles.GC.Extensions
```

### Oracle Cloud Storage
```bash
dotnet add package SimplyWorks.CloudFiles.OC.Extensions
```

### Local Filesystem (Testing / Local Development Only)
```bash
dotnet add package SimplyWorks.CloudFiles.LocalTests.Extensions
```

> Install the core package (e.g. `SimplyWorks.CloudFiles.S3`) only if you need to reference the service or options types directly without the DI extensions.

## Getting Started

All providers implement the same `ICloudFilesService` interface from [SimplyWorks.PrimitiveTypes](https://github.com/simplify9/PrimitiveTypes), ensuring a consistent API.

### Configuration

#### Option 1: appsettings.json

```json
{
  "CloudFiles": {
    "AccessKeyId": "your-access-key",
    "SecretAccessKey": "your-secret-key",
    "BucketName": "your-bucket-name",
    "ServiceUrl": "https://your-service-url"
  }
}
```

#### Option 2: Programmatic configuration

```csharp
// S3-Compatible Storage
services.AddS3CloudFiles(o => {
    o.AccessKeyId = "your-access-key";
    o.SecretAccessKey = "your-secret-key";
    o.ServiceUrl = "https://s3.amazonaws.com";
    o.BucketName = "your-bucket-name";
});

// Azure Blob Storage
services.AddAsCloudFiles(o => {
    o.AccessKeyId = "your-account-name";
    o.SecretAccessKey = "your-account-key";
    o.ServiceUrl = "https://youraccount.blob.core.windows.net";
    o.BucketName = "your-container-name";
});

// Google Cloud Storage
services.AddGoogleCloudFiles(o => {
    o.ProjectId = "your-project-id";
    o.BucketName = "your-bucket-name";
    o.ClientEmail = "service-account@project.iam.gserviceaccount.com";
    o.PrivateKey = "-----BEGIN RSA PRIVATE KEY-----\n...";
    // ... other service account fields
});

// Oracle Cloud Storage
services.AddOracleCloudFiles(o => {
    o.TenantId = "your-tenant-ocid";
    o.UserId = "your-user-ocid";
    o.FingerPrint = "xx:xx:xx:...";
    o.Region = "us-ashburn-1";
    o.BucketName = "your-bucket-name";
    o.NamespaceName = "your-namespace";
    o.RSAKey = "-----BEGIN RSA PRIVATE KEY-----\n...";
});

// Local Filesystem — testing and local development only
services.AddLocalTestsCloudFiles(o => {
    o.BucketName = "my-test-bucket";
    // StoragePath is optional — defaults to {GetTempPath()}/SW.CloudFiles.LocalTests/{BucketName}
});
```

### Dependency Injection

Inject `ICloudFilesService` into your controllers or services:

```csharp
public class FileController : ControllerBase
{
    private readonly ICloudFilesService _cloudFilesService;

    public FileController(ICloudFilesService cloudFilesService)
    {
        _cloudFilesService = cloudFilesService;
    }
}
```

## Usage Examples

### Upload a File

```csharp
var result = await _cloudFilesService.WriteAsync(fileStream, new WriteFileSettings
{
    Key = "uploads/document.pdf",
    ContentType = "application/pdf",
    Public = true,
    Metadata = new Dictionary<string, string>
    {
        ["UploadedBy"] = "user123"
    }
});
// result.Location contains the public URL or a 1-hour signed URL
```

### Upload Text Content

```csharp
var result = await _cloudFilesService.WriteTextAsync("Hello, World!", new WriteFileSettings
{
    Key = "messages/hello.txt",
    ContentType = "text/plain"
});
```

### Download a File

```csharp
using var stream = await _cloudFilesService.OpenReadAsync("uploads/document.pdf");
using var fileStream = File.Create("local-copy.pdf");
await stream.CopyToAsync(fileStream);
```

### List Files

```csharp
var files = await _cloudFilesService.ListAsync("uploads/");
foreach (var file in files)
    Console.WriteLine($"{file.Key}  {file.Size} bytes");
```

### Generate URLs

```csharp
// Permanent public URL
var publicUrl = _cloudFilesService.GetUrl(key);

// Time-limited signed URL
var signedUrl = _cloudFilesService.GetSignedUrl(key, TimeSpan.FromHours(2));
```

### Delete a File

```csharp
bool deleted = await _cloudFilesService.DeleteAsync("uploads/document.pdf");
```

### Get File Metadata

```csharp
var metadata = await _cloudFilesService.GetMetadataAsync(key);
// Always includes: ContentType, Hash, ContentLength
```

### ASP.NET Core Controller Example

```csharp
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly ICloudFilesService _cloudFilesService;

    public FilesController(ICloudFilesService cloudFilesService)
    {
        _cloudFilesService = cloudFilesService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded");

        var result = await _cloudFilesService.WriteAsync(file.OpenReadStream(), new WriteFileSettings
        {
            Key = $"uploads/{Guid.NewGuid()}/{file.FileName}",
            ContentType = file.ContentType,
            Public = true,
            CloseInputStream = false
        });

        return Ok(new { url = result.Location, size = result.Size, fileName = result.Name });
    }

    [HttpGet("download/{*filePath}")]
    public async Task<IActionResult> DownloadFile(string filePath)
    {
        try
        {
            var stream = await _cloudFilesService.OpenReadAsync(filePath);
            var metadata = await _cloudFilesService.GetMetadataAsync(filePath);
            return File(stream, metadata["ContentType"], Path.GetFileName(filePath));
        }
        catch
        {
            return NotFound();
        }
    }

    [HttpDelete("{*filePath}")]
    public async Task<IActionResult> DeleteFile(string filePath)
    {
        var success = await _cloudFilesService.DeleteAsync(filePath);
        return success ? Ok() : NotFound();
    }
}
```

## Automatic Lifecycle Management

Three of the four providers automatically create **delete lifecycle rules** for temp-prefix objects on startup. This is useful for objects that are only needed temporarily.

| Prefix | Expiry |
|--------|--------|
| `temp1/`   | 1 day   |
| `temp7/`   | 7 days  |
| `temp30/`  | 30 days |
| `temp365/` | 365 days |

Rules are only added if they don't already exist — existing rules are never modified or removed.

### Disabling Lifecycle Management

Set `DisableAutoLifecycle = true` in any provider's options to skip the automatic rule setup:

```csharp
services.AddS3CloudFiles(o => {
    // ... other config
    o.DisableAutoLifecycle = true;
});

services.AddGoogleCloudFiles(o => {
    // ... other config
    o.DisableAutoLifecycle = true;
});

services.AddOracleCloudFiles(o => {
    // ... other config
    o.DisableAutoLifecycle = true;
});
```

### Provider Support

| Provider | Lifecycle support | Notes |
|----------|------------------|-------|
| S3 | ✅ Automatic | Rules applied via S3 Lifecycle Configuration API |
| Google Cloud | ✅ Automatic | Rules applied via GCS Bucket Lifecycle API; bucket is created if it doesn't exist |
| Oracle Cloud | ✅ Automatic | Rules applied via OCI Object Lifecycle Policy API |
| Azure Blob | ⚠️ Manual | Azure lifecycle management requires the Azure Resource Manager (ARM) plane, which is separate from the data-plane SDK used by this library. Configure lifecycle rules via the [Azure Portal](https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview), Azure CLI (`az storage account management-policy create`), or ARM templates. |

## Signed URLs

All four providers support `GetSignedUrl(key, expiry)`:

| Provider | Mechanism |
|----------|-----------|
| S3 | Pre-signed URL (AWS Signature V4) |
| Google Cloud | Signed URL (V4 signing via service account credentials) |
| Azure (shared key) | Blob SAS token |
| Azure (managed identity) | User Delegation SAS token |
| Oracle Cloud | Pre-Authenticated Request (PAR) — creates a server-side resource on Oracle |

> **Note:** Oracle PARs are server-side objects. Each call to `GetSignedUrl` creates a new PAR on Oracle Cloud.

## Provider-Specific Notes

### S3-Compatible Storage
- Works with AWS S3, DigitalOcean Spaces, MinIO, and any S3-compatible service.
- Bucket is created automatically if it does not exist.

### Azure Blob Storage
- Container is created automatically if it does not exist.
- **Managed Identity**: Set `Managed = true`. Optionally set `ManagedIdentityClientId` for a user-assigned identity; omit it to use the system-assigned identity or ambient `DefaultAzureCredential`.
- **Public URL override**: If you connect via private link (`ServiceUrl`) but need public-facing URLs, set `PublicServiceUrl` to the standard public endpoint (e.g. `https://account.blob.core.windows.net`).

```csharp
// Managed Identity example
services.AddAsCloudFiles(o => {
    o.ServiceUrl = "https://youraccount.blob.core.windows.net";
    o.BucketName = "your-container";
    o.Managed = true;
    o.ManagedIdentityClientId = "your-client-id"; // omit for system-assigned
    o.PublicServiceUrl = "https://youraccount.blob.core.windows.net"; // optional
});
```

### Google Cloud Storage
- Requires a service account with Storage Object Admin role on the bucket.
- Bucket is created automatically if it does not exist (requires Storage Admin role on the project).
- `GetSignedUrl` uses V4 signing via the service account's private key.

### Oracle Cloud Storage
- OCI credentials (`UserId`, `TenantId`, `FingerPrint`, `RSAKey`) are written to temporary files on startup and used to authenticate via `ConfigFileAuthenticationDetailsProvider`.
- `GetSignedUrl` creates a Pre-Authenticated Request (PAR) with read-only access.

### Local Filesystem (Testing / Local Development Only)

> ⚠️ **Do not use this provider in production.** It is designed exclusively for unit and integration tests and local development workflows.

Files are stored on the local filesystem. The storage root is chosen using .NET's `Path.GetTempPath()` — so the correct OS temp directory is used automatically (`/tmp` on Linux/macOS, `%TEMP%` on Windows). The final path is `{GetTempPath()}/SW.CloudFiles.LocalTests/{BucketName}`.

**File cleanup** is the caller's responsibility. On Linux and macOS the OS typically clears temp files on reboot, but on Windows temp files persist indefinitely. Always call `Cleanup()` in test teardown:

```csharp
// Startup / DI registration (e.g. in TestStartup.cs)
services.AddLocalTestsCloudFiles(o => {
    o.BucketName = "my-test-bucket";
    // StoragePath is optional — you almost never need to set it
});

// MSTest example — inject the concrete type for teardown
[ClassInitialize]
public static void Init(TestContext ctx)
{
    // build _serviceProvider ...
}

[ClassCleanup]
public static void Cleanup()
{
    _serviceProvider.GetRequiredService<CloudFilesService>().Cleanup();
}
```

`GetUrl` and `GetSignedUrl` both return a `file://` URI. `GetSignedUrl` accepts the `expiry` parameter for interface compatibility but it has no effect.

`OpenWrite` throws `NotImplementedException` (same as Oracle and Azure).

**Overriding the storage path** — only needed for special scenarios such as Docker bind-mounts or CI artifact collection:

```csharp
services.AddLocalTestsCloudFiles(o => {
    o.BucketName = "my-test-bucket";
    o.StoragePath = "/mnt/ci-artifacts/cloudfiles"; // absolute path, any OS
});
```

## Interface Reference

```csharp
public interface ICloudFilesService
{
    Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings);
    Task<RemoteBlob> WriteTextAsync(string text, WriteFileSettings settings);
    Task<Stream> OpenReadAsync(string key);
    Task<IEnumerable<CloudFileInfo>> ListAsync(string prefix);
    Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(string key);
    Task<bool> DeleteAsync(string key);
    string GetUrl(string key);
    string GetSignedUrl(string key, TimeSpan expiry);
    WriteWrapper OpenWrite(WriteFileSettings settings);
}
```

## Requirements

- **.NET 8.0** or later
- Appropriate cloud provider account and credentials
- [SimplyWorks.PrimitiveTypes](https://github.com/simplify9/PrimitiveTypes)

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](.github/CONTRIBUTING.md) and [Code of Conduct](.github/CODE_OF_CONDUCT.md).

## License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

## About Simplify9

SW.CloudFiles is developed and maintained by [Simplify9](https://github.com/simplify9).
