using Amazon.S3;
using Amazon;
using SW.PrimitiveTypes;
using System;

namespace SW.CloudFiles.S3;

/// <summary>Factory extensions for creating an <see cref="AmazonS3Client"/> from <see cref="CloudFilesOptions"/>.</summary>
public static class CloudFilesOptionsExtensions
{
    /// <summary>Creates a configured <see cref="AmazonS3Client"/> from the given options.</summary>
    public static AmazonS3Client CreateClient(this CloudFilesOptions cloudFilesOptions)
    {
        var clientConfig = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            ServiceURL = cloudFilesOptions.ServiceUrl,
            UseHttp = new Uri(cloudFilesOptions.ServiceUrl).Scheme.ToLower() == "http",
            ForcePathStyle = true
        };

        return new AmazonS3Client(cloudFilesOptions.AccessKeyId, cloudFilesOptions.SecretAccessKey, clientConfig);
    }
}
