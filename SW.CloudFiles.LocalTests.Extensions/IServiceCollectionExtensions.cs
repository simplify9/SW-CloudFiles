using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.LocalTests;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.Extensions;

/// <summary>
/// ASP.NET Core DI extension methods for registering the local filesystem <see cref="ICloudFilesService"/>.
/// </summary>
/// <remarks>
/// <b>For testing and local development only.</b> Do not reference this package in
/// production applications.
/// </remarks>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers the local filesystem provider as the <see cref="ICloudFilesService"/> implementation.
    /// </summary>
    /// <remarks>
    /// The concrete <see cref="CloudFilesService"/> is registered as a singleton in addition to the
    /// <see cref="ICloudFilesService"/> interface, so tests can inject it directly and call
    /// <see cref="CloudFilesService.Cleanup"/> during teardown to delete all files written during
    /// the test run. This is especially important on Windows, where temp files are not cleared
    /// automatically.
    /// <para>
    /// Example test teardown (MSTest):
    /// <code>
    /// [ClassCleanup]
    /// public static void Cleanup()
    /// {
    ///     _serviceProvider.GetRequiredService&lt;CloudFilesService&gt;().Cleanup();
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="serviceCollection">The service collection to add services to.</param>
    /// <param name="configure">
    /// Optional delegate to configure <see cref="LocalTestsCloudFilesOptions"/> programmatically.
    /// Settings are merged with the <c>CloudFiles</c> section in <c>appsettings.json</c>.
    /// </param>
    public static IServiceCollection AddLocalTestsCloudFiles(this IServiceCollection serviceCollection,
        Action<LocalTestsCloudFilesOptions> configure = null)
    {
        var cloudFilesOptions = new LocalTestsCloudFilesOptions();
        if (configure != null) configure.Invoke(cloudFilesOptions);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.GetRequiredService<IConfiguration>()
            .GetSection(CloudFilesOptions.ConfigurationSection)
            .Bind(cloudFilesOptions);

        serviceCollection.AddSingleton(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesService>();
        serviceCollection.AddSingleton<ICloudFilesService>(sp => sp.GetRequiredService<CloudFilesService>());

        return serviceCollection;
    }
}
