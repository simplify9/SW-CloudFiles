using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nito.AsyncEx.Synchronous;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using SW.CloudFiles.S3;

namespace SW.CloudFiles.Extensions;

/// <summary>ASP.NET Core DI extension methods for registering the S3-compatible <see cref="ICloudFilesService"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers an S3-compatible provider as the <see cref="ICloudFilesService"/> implementation.
    /// On startup, creates the bucket if it does not exist and — unless
    /// <see cref="S3CloudFilesOptions.DisableAutoLifecycle"/> is true — ensures delete lifecycle rules
    /// exist for the temp1/, temp7/, temp30/, and temp365/ prefixes (1, 7, 30, and 365 days respectively).
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

                var newRules = new List<LifecycleRule> { };

                if (config.Rules?.FirstOrDefault(r => r.Id == "temp1") == null ||
                    config.Rules?.FirstOrDefault(r => r.Id == "temp1")?.Status != LifecycleRuleStatus.Enabled)
                    newRules.Add(new LifecycleRule
                    {
                        Id = "temp1",
                        Expiration = new LifecycleRuleExpiration { Days = 1 },
                        Filter = new LifecycleFilter()
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate()
                            {
                                Prefix = "temp1/"
                            }
                        },
                        Status = LifecycleRuleStatus.Enabled
                    });

                if (config.Rules?.FirstOrDefault(r => r.Id == "temp7") == null ||
                    config.Rules?.FirstOrDefault(r => r.Id == "temp7")?.Status != LifecycleRuleStatus.Enabled)
                    newRules.Add(new LifecycleRule
                    {
                        Id = "temp7",
                        Expiration = new LifecycleRuleExpiration { Days = 7 },
                        Filter = new LifecycleFilter()
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate()
                            {
                                Prefix = "temp7/"
                            }
                        },
                        Status = LifecycleRuleStatus.Enabled
                    });

                if (config.Rules?.FirstOrDefault(r => r.Id == "temp30") == null ||
                    config.Rules?.FirstOrDefault(r => r.Id == "temp30")?.Status != LifecycleRuleStatus.Enabled)
                    newRules.Add(new LifecycleRule
                    {
                        Id = "temp30",
                        Expiration = new LifecycleRuleExpiration { Days = 30 },
                        Filter = new LifecycleFilter
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate
                            {
                                Prefix = "temp30/"
                            }
                        },
                        Status = LifecycleRuleStatus.Enabled
                    });

                if (config.Rules?.FirstOrDefault(r => r.Id == "temp365") == null ||
                    config.Rules?.FirstOrDefault(r => r.Id == "temp365")?.Status != LifecycleRuleStatus.Enabled)
                    newRules.Add(new LifecycleRule
                    {
                        Id = "temp365",
                        Expiration = new LifecycleRuleExpiration { Days = 365 },
                        Filter = new LifecycleFilter()
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate()
                            {
                                Prefix = "temp365/"
                            }
                        },
                        Status = LifecycleRuleStatus.Enabled
                    });

                if (newRules.Count > 0)
                {
                    client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
                    {
                        BucketName = cloudFilesOptions.BucketName,
                        Configuration = new LifecycleConfiguration { Rules = newRules }
                    }).WaitAndUnwrapException();
                }
            }
        }

        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddTransient<ICloudFilesService, CloudFilesService>();

        return serviceCollection;
    }
}
