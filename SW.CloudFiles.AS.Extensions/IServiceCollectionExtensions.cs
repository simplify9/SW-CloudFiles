using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.AS;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.AS.Extensions;

public static class IServiceCollectionExtensions
{
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