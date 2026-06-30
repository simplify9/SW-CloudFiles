using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

/// <summary>Azure Blob Storage implementation of <see cref="ICloudFilesService"/>.</summary>
public class CloudFilesService(AzureCloudFilesOptions cloudFilesOptions) : IDisposable, ICloudFilesService
{
    private readonly AzureCloudFilesOptions cloudFilesOptions = cloudFilesOptions;
    private readonly BlobContainerClient blobContainerClient = cloudFilesOptions.CreateClient();

    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings)
    {
        var blobClient = blobContainerClient.GetBlobClient(settings.Key);

        await blobClient.UploadAsync(inputStream, new BlobUploadOptions
        {
            Metadata = settings.Metadata ?? new Dictionary<string, string>(),
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = settings.ContentType ??
                              "application/octet-stream"
            }
        });

        return new RemoteBlob
        {
            Location = $"{blobContainerClient.Uri}/{settings.Key}",
            Name = settings.Key,
            MimeType = settings.ContentType,
            Size = (int)inputStream.Length
        };
    }

    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteTextAsync(string text, WriteFileSettings settings)
    {
        var blobClient = blobContainerClient.GetBlobClient(settings.Key);


        var content = Encoding.UTF8.GetBytes(text);
        await using var ms = new MemoryStream(content);
        var contentType = settings.ContentType ?? "text/plain";
        await blobClient.UploadAsync(ms, new BlobHttpHeaders
        {
            ContentType = contentType
        }, settings.Metadata);

        return new RemoteBlob
        {
            Location = $"{blobContainerClient.Uri}/{settings.Key}",
            Name = settings.Key,
            MimeType = contentType
        };
    }

    /// <inheritdoc/>
    public string GetSignedUrl(string key, TimeSpan expiry)
    {
        var expiresOn = DateTimeOffset.UtcNow.Add(expiry);

        if (cloudFilesOptions.Managed)
        {
            var credential = cloudFilesOptions.ManagedIdentityClientId is not null
                ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = cloudFilesOptions.ManagedIdentityClientId
                })
                : new DefaultAzureCredential();

            var serviceClient = new BlobServiceClient(new Uri(cloudFilesOptions.ServiceUrl), credential);
            var delegationKey = serviceClient.GetUserDelegationKey(DateTimeOffset.UtcNow, expiresOn);

            var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
            {
                BlobContainerName = cloudFilesOptions.BucketName,
                BlobName = key,
            };

            return new BlobUriBuilder(blobContainerClient.GetBlobClient(key).Uri)
            {
                Sas = sasBuilder.ToSasQueryParameters(delegationKey.Value, serviceClient.AccountName)
            }.ToString();
        }
        else
        {
            var blobClient = blobContainerClient.GetBlobClient(key);
            return blobClient.GenerateSasUri(BlobSasPermissions.Read, expiresOn).ToString();
        }
    }

    /// <inheritdoc/>
    public string GetUrl(string key)
    {
        var baseUrl = !string.IsNullOrEmpty(cloudFilesOptions.PublicServiceUrl)
            ? $"{cloudFilesOptions.PublicServiceUrl.TrimEnd('/')}/{cloudFilesOptions.BucketName}"
            : blobContainerClient.Uri.ToString().TrimEnd('/');
        return $"{baseUrl}/{key}";
    }

    /// <inheritdoc/>
    public WriteWrapper OpenWrite(WriteFileSettings settings)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string key)
    {
        var blob = blobContainerClient.GetBlobClient(key);

        var result = await blob.DownloadAsync();

        return result.Value.Content;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<CloudFileInfo>> ListAsync(string prefix)
    {
        var blobHierarchyItems =
            blobContainerClient.GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, null, $"{prefix}");

        var files = new List<CloudFileInfo>();

        await foreach (var blobHierarchyItem in blobHierarchyItems)
        {
            if (!blobHierarchyItem.IsPrefix)
                files.Add(new CloudFileInfo
                {
                    Key = blobHierarchyItem.Blob.Name,
                    Signature = blobHierarchyItem.Blob.Properties.ETag?.ToString(),
                    Size = blobHierarchyItem.Blob.Properties.ContentLength ?? 0
                });
        }

        return files;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(string key)
    {
        var blob = blobContainerClient.GetBlobClient(key);
        var properties = await blob.GetPropertiesAsync();
        var data = new Dictionary<string, string>(properties.Value.Metadata);

        if (!data.TryGetValue("ContentType", out var _))
        {
            data.Add("ContentType", properties.Value.ContentType);
        }

        if (!data.TryGetValue("Hash", out var _))
        {
            data.Add("Hash", properties.Value.ETag.ToString().Replace("\"", ""));
        }

        if (!data.TryGetValue("ContentLength", out var _))
        {
            data.Add("ContentLength", properties.Value.ContentLength.ToString());
        }


        return data;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key)
    {
        var blob = blobContainerClient.GetBlobClient(key);
        await blob.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {

    }
}