using SW.PrimitiveTypes;

namespace SW.CloudFiles.S3;

/// <summary>S3-specific options extending <see cref="CloudFilesOptions"/>.</summary>
public class S3CloudFilesOptions : CloudFilesOptions
{
    /// <summary>
    /// When true, skips automatic creation of temp-prefix lifecycle deletion rules on the bucket.
    /// Lifecycle rules are: temp1/ (1 day), temp7/ (7 days), temp30/ (30 days), temp365/ (365 days).
    /// </summary>
    public bool DisableAutoLifecycle { get; set; }

    /// <summary>Per-request timeout, in seconds, for the underlying <see cref="Amazon.S3.AmazonS3Client"/>. Defaults to 15.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Max automatic retries for the underlying <see cref="Amazon.S3.AmazonS3Client"/>. Defaults to 2.</summary>
    public int MaxErrorRetry { get; set; } = 2;
}
