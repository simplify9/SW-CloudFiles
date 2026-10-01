using System;
using Azure;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.AS;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS.Extensions;

/// <summary>ASP.NET Core DI extension methods for registering the Azure Blob Storage <see cref="ICloudFilesService"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Blob Storage as the <see cref="ICloudFilesService"/> implementation, and
    /// <see cref="ICloudFilesLifecycle"/>, which reads the storage account's deletion rules back.
    /// On startup, creates the container if it does not exist.
    /// <para>
    /// Once <see cref="AzureCloudFilesOptions.SubscriptionId"/> and
    /// <see cref="AzureCloudFilesOptions.ResourceGroupName"/> are set, and unless
    /// <see cref="AzureCloudFilesOptions.DisableAutoLifecycle"/> is true, also ensures the account's lifecycle
    /// management policy deletes this container's blobs under temp1/, temp7/, temp30/ and temp365/ after 1, 7,
    /// 30 and 365 days, keeping every other rule in the policy. The policy lives on the Azure Resource Manager
    /// plane, so the identity needs a role that can manage it; see <see cref="AzureCloudFilesOptions.SubscriptionId"/>.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAsCloudFiles(this IServiceCollection serviceCollection, Action<AzureCloudFilesOptions> configure = null)
    {
        var cloudFilesOptions = new AzureCloudFilesOptions();
        if (configure != null) configure.Invoke(cloudFilesOptions);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.GetRequiredService<IConfiguration>().GetSection(CloudFilesOptions.ConfigurationSection)
            .Bind(cloudFilesOptions);

        var blobContainerClient = cloudFilesOptions.CreateClient();

        if (!cloudFilesOptions.DisableAutoLifecycle && cloudFilesOptions.CanManage())
            EnsureLifecycleRules(cloudFilesOptions);

        serviceCollection.AddSingleton(blobContainerClient);
        serviceCollection.AddSingleton(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesService>();
        serviceCollection.AddSingleton<ICloudFilesService>(sp => sp.GetRequiredService<CloudFilesService>());
        serviceCollection.AddSingleton<ICloudFilesLifecycle>(sp => sp.GetRequiredService<CloudFilesService>());
        return serviceCollection;
    }

    private static void EnsureLifecycleRules(AzureCloudFilesOptions options)
    {
        var policy = options.ManagementPolicy();

        StorageAccountManagementPolicyData data;
        try
        {
            data = policy.Get().Value.Data;
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "ManagementPolicyNotFound")
        {
            data = new StorageAccountManagementPolicyData();
        }

        // The policy is written whole, so everything already in it goes back with the additions.
        if (AzureLifecycleRules.AddTempRules(data, options.BucketName))
            policy.CreateOrUpdate(WaitUntil.Completed, data);
    }
}