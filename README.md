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

## Introduction 

**SW.CloudFiles** is a unified, multi-cloud storage abstraction library for .NET 8 that provides a consistent interface across different cloud storage providers. It simplifies cloud storage operations by offering a single API that works with multiple cloud providers without vendor lock-in.

### Supported Cloud Providers

- **🚀 S3-Compatible Storage** (AWS S3, DigitalOcean Spaces, MinIO, etc.)
- **☁️ Azure Blob Storage** 
- **🌐 Google Cloud Storage**
- **🔶 Oracle Cloud Storage**

The library provides both core functionality and ASP.NET Core dependency injection extensions for easy integration into web applications.

## Installation

Choose the appropriate package based on your cloud storage provider:

### S3-Compatible Storage (AWS S3, DigitalOcean Spaces, MinIO, etc.)
```bash
# Core library
dotnet add package SimplyWorks.CloudFiles.S3

# With ASP.NET Core DI extensions
dotnet add package SimplyWorks.CloudFiles.S3.Extensions
```

### Azure Blob Storage
```bash
# Core library  
dotnet add package SimplyWorks.CloudFiles.AS

# With ASP.NET Core DI extensions
dotnet add package SimplyWorks.CloudFiles.AS.Extensions
```

### Google Cloud Storage
```bash
# Core library
dotnet add package SimplyWorks.CloudFiles.GC

# With ASP.NET Core DI extensions  
dotnet add package SimplyWorks.CloudFiles.GC.Extensions
```

### Oracle Cloud Storage
```bash
# Core library
dotnet add package SimplyWorks.CloudFiles.OC

# With ASP.NET Core DI extensions
dotnet add package SimplyWorks.CloudFiles.OC.Extensions
```

## Getting Started

All providers implement the same `ICloudFilesService` interface from [SimplyWorks.PrimitiveTypes](https://github.com/simplify9/PrimitiveTypes), ensuring a consistent API across different cloud providers.

### Configuration

#### Option 1: appsettings.json Configuration
Add your cloud storage configuration to `appsettings.json`:

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

#### Option 2: Programmatic Configuration
Configure directly in your startup:

```csharp
// S3-Compatible Storage
services.AddS3CloudFiles(config => {
    config.AccessKeyId = "your-access-key";
    config.SecretAccessKey = "your-secret-key";
    config.ServiceUrl = "https://s3.amazonaws.com"; // or your S3-compatible endpoint
    config.BucketName = "your-bucket-name";
});

// Azure Blob Storage  
services.AddAsCloudFiles(config => {
    config.AccessKeyId = "your-account-name";
    config.SecretAccessKey = "your-account-key";
    config.ServiceUrl = "https://youraccount.blob.core.windows.net";
    config.BucketName = "your-container-name";
});

// Google Cloud Storage
services.AddGoogleCloudFiles(config => {
    config.ProjectId = "your-project-id";
    config.BucketName = "your-bucket-name";
    config.ClientEmail = "service-account@project.iam.gserviceaccount.com";
    config.PrivateKey = "your-private-key";
    // ... other Google Cloud credentials
});

// Oracle Cloud Storage
services.AddOracleCloudFiles(config => {
    config.TenantId = "your-tenant-id";
    config.UserId = "your-user-id"; 
    config.FingerPrint = "your-fingerprint";
    config.Region = "your-region";
    config.BucketName = "your-bucket-name";
    config.NamespaceName = "your-namespace";
    config.RSAKey = "your-rsa-private-key";
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
    
    // Your file operations here...
}

## Usage Examples

### Basic File Operations

#### Upload a File
```csharp
public async Task<RemoteBlob> UploadFileAsync(Stream fileStream)
{
    var result = await _cloudFilesService.WriteAsync(fileStream, new WriteFileSettings
    {
        Key = "uploads/document.pdf",
        ContentType = "application/pdf",
        Public = true,
        Metadata = new Dictionary<string, string>
        {
            ["UploadedBy"] = "user123",
            ["UploadDate"] = DateTime.UtcNow.ToString()
        }
    });
    
    return result; // Contains Location, Name, Size, MimeType
}
```

#### Upload Text Content
```csharp
public async Task<RemoteBlob> UploadTextAsync()
{
    var result = await _cloudFilesService.WriteTextAsync("Hello, World!", new WriteFileSettings
    {
        Key = "messages/hello.txt",
        ContentType = "text/plain",
        Public = false
    });
    
    return result;
}
```

#### Download a File
```csharp
public async Task DownloadFileAsync(string key)
{
    using var stream = await _cloudFilesService.OpenReadAsync(key);
    using var fileStream = File.Create("downloaded-file.txt");
    await stream.CopyToAsync(fileStream);
}
```

#### List Files with Prefix
```csharp
public async Task<IEnumerable<CloudFileInfo>> ListFilesAsync()
{
    var files = await _cloudFilesService.ListAsync("uploads/");
    
    foreach (var file in files)
    {
        Console.WriteLine($"File: {file.Key}, Size: {file.Size}, Signature: {file.Signature}");
    }
    
    return files;
}
```

#### Get File Metadata
```csharp
public async Task<IReadOnlyDictionary<string, string>> GetFileInfoAsync(string key)
{
    var metadata = await _cloudFilesService.GetMetadataAsync(key);
    
    Console.WriteLine($"Content Type: {metadata["ContentType"]}");
    Console.WriteLine($"File Hash: {metadata["Hash"]}");
    Console.WriteLine($"Content Length: {metadata["ContentLength"]}");
    
    return metadata;
}
```

#### Generate URLs
```csharp
public string GetFileUrls(string key)
{
    // Public URL (permanent, for public files)
    var publicUrl = _cloudFilesService.GetUrl(key);
    
    // Signed URL (temporary, with expiration)  
    var signedUrl = _cloudFilesService.GetSignedUrl(key, TimeSpan.FromHours(2));
    
    return signedUrl;
}
```

#### Delete a File
```csharp
public async Task<bool> DeleteFileAsync(string key)
{
    var success = await _cloudFilesService.DeleteAsync(key);
    return success;
}
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

        return Ok(new { 
            url = result.Location,
            size = result.Size,
            fileName = result.Name 
        });
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

## Features

### Automatic Lifecycle Management (S3 Provider)
The S3 provider automatically sets up lifecycle rules for temporary file prefixes:

- `temp1/` - Files expire after 1 day
- `temp7/` - Files expire after 7 days  
- `temp30/` - Files expire after 30 days
- `temp365/` - Files expire after 365 days

### Common Interface
All providers implement the same `ICloudFilesService` interface:

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

## Provider-Specific Notes

### S3-Compatible Storage
- Works with AWS S3, DigitalOcean Spaces, MinIO, and other S3-compatible services
- Supports custom service URLs for S3-compatible providers
- Automatic lifecycle rule setup for temporary file management

### Azure Blob Storage  
- Uses Azure.Storage.Blobs SDK
- Supports Azure Storage Account authentication
- Container is created automatically if it doesn't exist

### Google Cloud Storage
- Requires service account JSON credentials  
- Supports all Google Cloud Storage features
- Uses Google.Cloud.Storage.V1 client library

### Oracle Cloud Storage
- Uses Oracle Cloud Infrastructure (OCI) SDK
- Requires OCI configuration file and RSA private key
- Supports Oracle Cloud tenancy and namespace configuration

## Requirements

- **.NET 8.0** or later
- Appropriate cloud provider account and credentials
- [SimplyWorks.PrimitiveTypes](https://github.com/simplify9/PrimitiveTypes) for common interfaces

## Contributing

Contributions are welcome! Please read our [Contributing Guidelines](.github/CONTRIBUTING.md) and [Code of Conduct](.github/CODE_OF_CONDUCT.md).

## License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

## Support

If you encounter any bugs or have feature requests, please [submit an issue](https://github.com/simplify9/SW-CloudFiles/issues).

## About Simplify9

SW.CloudFiles is developed and maintained by [Simplify9](https://github.com/simplify9). We create tools and libraries that simplify cloud development. 







