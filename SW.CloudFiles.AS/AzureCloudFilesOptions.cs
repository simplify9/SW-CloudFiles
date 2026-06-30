using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS;

/// <summary>Configuration options for the Azure Blob Storage provider.</summary>
public class AzureCloudFilesOptions : CloudFilesOptions
{
    /// <summary>
    /// When true, authenticates using Azure managed identity (or <c>DefaultAzureCredential</c>)
    /// instead of a storage account key.
    /// </summary>
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

    /// <summary>
    /// Reserved for future use. Azure Blob Storage lifecycle management policies require the
    /// Azure Resource Manager (ARM) plane and cannot be configured automatically through
    /// the data-plane SDK. Configure lifecycle rules via the Azure Portal, Azure CLI
    /// (<c>az storage account management-policy create</c>), or ARM templates.
    /// </summary>
    public bool DisableAutoLifecycle { get; set; }
}