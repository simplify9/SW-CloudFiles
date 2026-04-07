using System;
using System.Linq;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

public static class AsCloudFileOptionsExtensions
{
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