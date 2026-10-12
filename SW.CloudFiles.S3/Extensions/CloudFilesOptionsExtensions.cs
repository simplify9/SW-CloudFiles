using Amazon.S3;
using Amazon;
using Amazon.Runtime;
using SW.PrimitiveTypes;
using System;

namespace SW.CloudFiles.S3;

/// <summary>Factory extensions for creating an <see cref="AmazonS3Client"/> from <see cref="CloudFilesOptions"/>.</summary>
public static class CloudFilesOptionsExtensions
{
    /// <summary>Creates a configured <see cref="AmazonS3Client"/> from the given options.</summary>
    public static AmazonS3Client CreateClient(this S3CloudFilesOptions cloudFilesOptions)
    {
        var clientConfig = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            ServiceURL = cloudFilesOptions.ServiceUrl,
            UseHttp = new Uri(cloudFilesOptions.ServiceUrl).Scheme.ToLower() == "http",
            ForcePathStyle = true,
            Timeout = TimeSpan.FromSeconds(cloudFilesOptions.TimeoutSeconds),
            MaxErrorRetry = cloudFilesOptions.MaxErrorRetry,
            // Since AWSSDK.S3 3.7.410 the default is WHEN_SUPPORTED, which sends PutObject bodies as
            // aws-chunked with a trailing CRC32. S3-compatible stores that don't understand that
            // (SeaweedFS, older MinIO) store the chunk framing as part of the object. Amazon S3 is
            // fine either way.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };

        return new AmazonS3Client(cloudFilesOptions.AccessKeyId, cloudFilesOptions.SecretAccessKey, clientConfig);
    }
}
