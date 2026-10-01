using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx.Synchronous;
using SW.PrimitiveTypes;
using System;
using SW.CloudFiles.S3;

namespace SW.CloudFiles.Extensions;

/// <summary>ASP.NET Core DI extension methods for registering the S3-compatible <see cref="ICloudFilesService"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers an S3-compatible provider as the <see cref="ICloudFilesService"/> implementation.
    /// On startup, creates the bucket if it does not exist and — unless
    /// <see cref="S3CloudFilesOptions.DisableAutoLifecycle"/> is true — ensures delete lifecycle rules
    /// exist for the temp1/, temp7/, temp30/, and temp365/ prefixes (1, 7, 30, and 365 days respectively),
    /// keeping every other rule already on the bucket. Also registers <see cref="ICloudFilesLifecycle"/>,
    /// which reads the bucket's rules back.
    /// </summary>
    public static IServiceCollection AddS3CloudFiles(this IServiceCollection serviceCollection,
        Action<S3CloudFilesOptions> configure = null)
    {
        var cloudFilesOptions = new S3CloudFilesOptions();
        if (configure != null) configure.Invoke(cloudFilesOptions);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        serviceProvider.GetRequiredService<IConfiguration>().GetSection(CloudFilesOptions.ConfigurationSection)
            .Bind(cloudFilesOptions);

        using (var client = cloudFilesOptions.CreateClient())
        {
            if (!AmazonS3Util.DoesS3BucketExistV2Async(client, cloudFilesOptions.BucketName).WaitAndUnwrapException())
            {
                var putBucketRequest = new PutBucketRequest
                {
                    BucketName = cloudFilesOptions.BucketName,
                    CannedACL = S3CannedACL.Private
                };
                client.PutBucketAsync(putBucketRequest).WaitAndUnwrapException();
            }

            if (!cloudFilesOptions.DisableAutoLifecycle)
            {
                var config = client.GetLifecycleConfigurationAsync(new GetLifecycleConfigurationRequest
                {
                    BucketName = cloudFilesOptions.BucketName
                }).WaitAndUnwrapException().Configuration;

                // The whole rule set goes back, not just the missing temp rules: S3 replaces the entire
                // configuration on every write, so sending only the additions deleted every other rule.
                var rules = S3LifecycleRules.WithTempRules(config?.Rules);
                if (rules != null)
                {
                    client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
                    {
                        BucketName = cloudFilesOptions.BucketName,
                        Configuration = new LifecycleConfiguration { Rules = rules }
                    }).WaitAndUnwrapException();
                }
            }
        }

        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesService>();
        serviceCollection.AddSingleton<ICloudFilesService>(sp => sp.GetRequiredService<CloudFilesService>());
        serviceCollection.AddSingleton<ICloudFilesLifecycle>(sp => sp.GetRequiredService<CloudFilesService>());

        return serviceCollection;
    }
}
