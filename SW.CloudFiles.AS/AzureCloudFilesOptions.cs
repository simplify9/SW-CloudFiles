using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

public class AzureCloudFilesOptions : CloudFilesOptions
{
    public bool Managed { get; set; }

    /// <summary>
    /// Client ID of a user-assigned managed identity. Leave null to use the system-assigned
    /// managed identity (or any ambient credential resolved by DefaultAzureCredential).
    /// Only relevant when <see cref="Managed"/> is true.
    /// </summary>
    public string ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Publicly reachable blob endpoint used by GetUrl(). When the backend connects via a
    /// private link ServiceUrl, set this to the standard public endpoint so that URLs
    /// returned to external clients are accessible (e.g. https://account.blob.core.windows.net/).
    /// Falls back to ServiceUrl when not set.
    /// </summary>
    public string PublicServiceUrl { get; set; }
}