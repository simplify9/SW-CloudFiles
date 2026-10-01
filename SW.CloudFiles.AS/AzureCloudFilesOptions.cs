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
    /// When true, skips creating the temp-prefix delete rules (temp1/, temp7/, temp30/, temp365/) in the
    /// storage account's lifecycle management policy. Only applies once <see cref="SubscriptionId"/> and
    /// <see cref="ResourceGroupName"/> are set: the policy lives on the Azure Resource Manager plane, not
    /// the blob endpoint, so without them nothing can be created and nothing deletes temp files.
    /// </summary>
    public bool DisableAutoLifecycle { get; set; }

    /// <summary>
    /// Subscription of the storage account, for its lifecycle management policy. Leave unset and the
    /// policy is neither created nor read. The identity used — the managed identity, or a service principal
    /// from <c>AZURE_TENANT_ID</c> / <c>AZURE_CLIENT_ID</c> / <c>AZURE_CLIENT_SECRET</c> — needs
    /// <c>Microsoft.Storage/storageAccounts/managementPolicies/read</c> and <c>/write</c>, for example the
    /// Storage Account Contributor role; Storage Blob Data Contributor alone isn't enough.
    /// </summary>
    public string SubscriptionId { get; set; }

    /// <summary>Resource group of the storage account. Required alongside <see cref="SubscriptionId"/>.</summary>
    public string ResourceGroupName { get; set; }

    /// <summary>
    /// The storage account's name. Optional: taken from <see cref="CloudFilesOptions.AccessKeyId"/> with a
    /// shared key, or from the first part of the service URL's host with a managed identity. Set it when
    /// the service URL is a custom domain or a private link.
    /// </summary>
    public string StorageAccountName { get; set; }
}