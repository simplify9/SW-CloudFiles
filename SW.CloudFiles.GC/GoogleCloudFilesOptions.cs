using SW.PrimitiveTypes;

namespace SW.CloudFiles.GC;

/// <summary>Configuration options for the Google Cloud Storage provider.</summary>
public class GoogleCloudFilesOptions : CloudFilesOptions
{
    /// <summary>GCP project ID that owns the bucket.</summary>
    public string ProjectId { get; set; }

    /// <summary>Private key ID from the service account JSON.</summary>
    public string PrivateKeyId { get; set; }

    /// <summary>RSA private key from the service account JSON (PEM format).</summary>
    public string PrivateKey { get; set; }

    /// <summary>Service account email address.</summary>
    public string ClientEmail { get; set; }

    /// <summary>Service account client ID.</summary>
    public string ClientId { get; set; }

    /// <summary>OAuth2 authorization endpoint. Defaults to the standard Google endpoint.</summary>
    public string AuthUri { get; set; } = "https://accounts.google.com/o/oauth2/auth";

    /// <summary>OAuth2 token endpoint. Defaults to the standard Google endpoint.</summary>
    public string TokenUri { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>URL of the public x509 certificate for the auth provider.</summary>
    public string AuthProviderX509CertUrl { get; set; } = "https://www.googleapis.com/oauth2/v1/certs";

    /// <summary>URL of the public x509 certificate for the service account client.</summary>
    public string ClientX509CertUrl { get; set; }

    /// <summary>Universe domain. Defaults to <c>googleapis.com</c>.</summary>
    public string UniverseDomain { get; set; } = "googleapis.com";

    /// <summary>
    /// When true, skips automatic creation of temp-prefix lifecycle deletion rules on the bucket.
    /// Lifecycle rules are: temp1/ (1 day), temp7/ (7 days), temp30/ (30 days), temp365/ (365 days).
    /// </summary>
    public bool DisableAutoLifecycle { get; set; }
}
