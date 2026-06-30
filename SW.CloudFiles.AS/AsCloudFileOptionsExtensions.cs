using System;
using System.Linq;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

/// <summary>Factory extensions for creating a <see cref="BlobContainerClient"/> from <see cref="AzureCloudFilesOptions"/>.</summary>
public static class AsCloudFileOptionsExtensions
{
    /// <summary>
    /// Creates and returns a <see cref="BlobContainerClient"/> authenticated according to
    /// <see cref="AzureCloudFilesOptions.Managed"/>. The container is created if it does not exist.
    /// </summary>
    public static BlobContainerClient CreateClient(this AzureCloudFilesOptions cloudFilesOptions)
    {
        if (cloudFilesOptions.Managed)
        {
            var credential = cloudFilesOptions.ManagedIdentityClientId is not null
                ? new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = cloudFilesOptions.ManagedIdentityClientId
                })
                : new DefaultAzureCredential();

            var blobServiceClient = new BlobServiceClient(new Uri(cloudFilesOptions.ServiceUrl), credential);
            return blobServiceClient.GetBlobContainerClient(cloudFilesOptions.BucketName);
        }
        else
        {
            var blobServiceClient = new BlobServiceClient(new Uri(cloudFilesOptions.ServiceUrl),
                new StorageSharedKeyCredential(cloudFilesOptions.AccessKeyId, cloudFilesOptions.SecretAccessKey));

            var containers = blobServiceClient.GetBlobContainers();

            if (containers.All(c => c.Name != cloudFilesOptions.BucketName))
                return blobServiceClient.CreateBlobContainer(cloudFilesOptions.BucketName, PublicAccessType.BlobContainer);

            return blobServiceClient.GetBlobContainerClient(cloudFilesOptions.BucketName);
        }
    }
}