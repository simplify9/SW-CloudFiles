using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Models;
using Oci.ObjectstorageService.Requests;
using SW.CloudFiles.OC;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.Extensions;

/// <summary>ASP.NET Core DI extension methods for registering the Oracle Cloud <see cref="ICloudFilesService"/>.</summary>
public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Registers Oracle Cloud Infrastructure Object Storage as the <see cref="ICloudFilesService"/> implementation.
    /// On startup, writes OCI credentials to temporary files and — unless
    /// <see cref="OracleCloudFilesOptions.DisableAutoLifecycle"/> is true — creates delete lifecycle rules
    /// for the temp1/, temp7/, temp30/, and temp365/ prefixes on the bucket.
    /// </summary>
    public static IServiceCollection AddOracleCloudFiles(this IServiceCollection serviceCollection,
        Action<OracleCloudFilesOptions> configure = null)
    {
        var cloudFilesOptions = new OracleCloudFilesOptions();
        if (configure != null) configure.Invoke(cloudFilesOptions);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.GetRequiredService<IConfiguration>().GetSection(CloudFilesOptions.ConfigurationSection)
            .Bind(cloudFilesOptions);

        var directory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        var pemPath = Path.Combine(directory, $"{Guid.NewGuid():N}.pem");
        File.WriteAllText(pemPath, cloudFilesOptions.RSAKey);

        var configPath = Path.Combine(directory, $"{Guid.NewGuid():N}.config");
        File.WriteAllText(configPath, $@"[DEFAULT]
user={cloudFilesOptions.UserId}
fingerprint={cloudFilesOptions.FingerPrint}
tenancy={cloudFilesOptions.TenantId}
region={cloudFilesOptions.Region}
key_file={pemPath}");

        cloudFilesOptions.ConfigPath = configPath;

        if (!cloudFilesOptions.DisableAutoLifecycle)
            EnsureLifecycleRules(cloudFilesOptions);

        serviceCollection.AddScoped<ICloudFilesService, CloudFilesService>();
        serviceCollection.AddSingleton(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        return serviceCollection;
    }

    private static void EnsureLifecycleRules(OracleCloudFilesOptions options)
    {
        var provider = new ConfigFileAuthenticationDetailsProvider(options.ConfigPath, "DEFAULT");
        using var client = new ObjectStorageClient(provider);

        IList<ObjectLifecycleRule> existingRules = new List<ObjectLifecycleRule>();
        try
        {
            var policyResponse = client.GetObjectLifecyclePolicy(new GetObjectLifecyclePolicyRequest
            {
                BucketName = options.BucketName,
                NamespaceName = options.NamespaceName
            }).GetAwaiter().GetResult();

            existingRules = policyResponse.ObjectLifecyclePolicy?.Items
                ?? new List<ObjectLifecycleRule>();
        }
        catch { /* no existing policy */ }

        var rules = existingRules.ToList();

        var tempRules = new[]
        {
            ("delete-temp1",   "temp1/",   1L),
            ("delete-temp7",   "temp7/",   7L),
            ("delete-temp30",  "temp30/",  30L),
            ("delete-temp365", "temp365/", 365L)
        };

        bool changed = false;
        foreach (var (ruleName, prefix, days) in tempRules)
        {
            if (rules.Any(r => r.Name == ruleName && r.IsEnabled == true))
                continue;

            rules.RemoveAll(r => r.Name == ruleName);
            rules.Add(new ObjectLifecycleRule
            {
                Name = ruleName,
                Action = "DELETE",
                TimeAmount = days,
                TimeUnit = ObjectLifecycleRule.TimeUnitEnum.Days,
                ObjectNameFilter = new ObjectNameFilter
                {
                    InclusionPrefixes = new List<string> { prefix }
                },
                IsEnabled = true,
                Target = "objects"
            });
            changed = true;
        }

        if (changed)
        {
            client.PutObjectLifecyclePolicy(new PutObjectLifecyclePolicyRequest
            {
                BucketName = options.BucketName,
                NamespaceName = options.NamespaceName,
                PutObjectLifecyclePolicyDetails = new PutObjectLifecyclePolicyDetails
                {
                    Items = rules
                }
            }).GetAwaiter().GetResult();
        }
    }
}
