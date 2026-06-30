using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.AS;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS.Extensions;

/// <summary>ASP.NET Core DI extension methods for registering the Azure Blob Storage <see cref="ICloudFilesService"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure Blob Storage as the <see cref="ICloudFilesService"/> implementation.
    /// On startup, creates the container if it does not exist.
    /// <para>
    /// Note: Azure lifecycle management policies require the Azure Resource Manager (ARM) plane and
    /// are not configured automatically. Set them via the Azure Portal, Azure CLI
    /// (<c>az storage account management-policy create</c>), or ARM templates.
    /// <see cref="AzureCloudFilesOptions.DisableAutoLifecycle"/> is accepted but has no effect.
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

        serviceCollection.AddSingleton(blobContainerClient);
        serviceCollection.AddSingleton(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddTransient<ICloudFilesService, CloudFilesService>();
        return serviceCollection;
    }
}