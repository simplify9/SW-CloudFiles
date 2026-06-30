using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.GC;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.Extensions;

internal class GoogleJsonCredentialsModel
{
    public string type { get; set; }
    public string project_id { get; set; }
    public string private_key_id { get; set; }
    public string private_key { get; set; }
    public string client_email { get; set; }
    public string client_id { get; set; }
    public string auth_uri { get; set; }
    public string token_uri { get; set; }
    public string auth_provider_x509_cert_url { get; set; }
    public string client_x509_cert_url { get; set; }
    public string universe_domain { get; set; }
}

/// <summary>ASP.NET Core DI extension methods for registering the Google Cloud Storage <see cref="ICloudFilesService"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Creates a <see cref="StorageClient"/> authenticated with the service account credentials in <paramref name="options"/>.</summary>
    public static StorageClient BuildGoogleCloudStorageClient(this GoogleCloudFilesOptions options)
    {
        var json = BuildCredentialJson(options);
        return new StorageClientBuilder { JsonCredentials = json }.Build();
    }

    /// <summary>
    /// Registers Google Cloud Storage as the <see cref="ICloudFilesService"/> implementation.
    /// On startup, ensures the bucket exists and — unless <see cref="GoogleCloudFilesOptions.DisableAutoLifecycle"/>
    /// is true — creates delete lifecycle rules for the temp1/, temp7/, temp30/, and temp365/ prefixes.
    /// </summary>
    public static IServiceCollection AddGoogleCloudFiles(this IServiceCollection serviceCollection,
        Action<GoogleCloudFilesOptions> configure = null)
    {
        var cloudFilesOptions = new GoogleCloudFilesOptions();
        if (configure != null) configure.Invoke(cloudFilesOptions);

        var serviceProvider = serviceCollection.BuildServiceProvider();
        serviceProvider.GetRequiredService<IConfiguration>()
            .GetSection(CloudFilesOptions.ConfigurationSection)
            .Bind(cloudFilesOptions);

        var client = cloudFilesOptions.BuildGoogleCloudStorageClient();

        EnsureBucketExists(client, cloudFilesOptions);

        if (!cloudFilesOptions.DisableAutoLifecycle)
            EnsureLifecycleRules(client, cloudFilesOptions.BucketName);

        var urlSigner = cloudFilesOptions.BuildUrlSigner();

        serviceCollection.AddScoped<ICloudFilesService, CloudFilesService>();
        serviceCollection.AddSingleton(cloudFilesOptions);
        serviceCollection.AddSingleton<CloudFilesOptions>(cloudFilesOptions);
        serviceCollection.AddSingleton(client);
        serviceCollection.AddSingleton(urlSigner);

        return serviceCollection;
    }

    private static void EnsureBucketExists(StorageClient client, GoogleCloudFilesOptions options)
    {
        try
        {
            client.GetBucket(options.BucketName);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            client.CreateBucket(options.ProjectId, options.BucketName);
        }
    }

    private static void EnsureLifecycleRules(StorageClient client, string bucketName)
    {
        var bucket = client.GetBucket(bucketName);
        bucket.Lifecycle ??= new Bucket.LifecycleData { Rule = new List<Bucket.LifecycleData.RuleData>() };
        bucket.Lifecycle.Rule ??= new List<Bucket.LifecycleData.RuleData>();

        var tempRules = new[]
        {
            ("temp1/",   1),
            ("temp7/",   7),
            ("temp30/",  30),
            ("temp365/", 365)
        };

        bool changed = false;
        foreach (var (prefix, days) in tempRules)
        {
            bool exists = bucket.Lifecycle.Rule.Any(r =>
                r.Action?.Type == "Delete" &&
                r.Condition?.MatchesPrefix != null &&
                r.Condition.MatchesPrefix.Contains(prefix));

            if (!exists)
            {
                bucket.Lifecycle.Rule.Add(new Bucket.LifecycleData.RuleData
                {
                    Action = new Bucket.LifecycleData.RuleData.ActionData { Type = "Delete" },
                    Condition = new Bucket.LifecycleData.RuleData.ConditionData
                    {
                        Age = days,
                        MatchesPrefix = new List<string> { prefix }
                    }
                });
                changed = true;
            }
        }

        if (changed)
            client.PatchBucket(new Bucket { Name = bucketName, Lifecycle = bucket.Lifecycle });
    }

    /// <summary>Creates a <see cref="UrlSigner"/> from the service account credentials in <paramref name="options"/>.</summary>
    public static UrlSigner BuildUrlSigner(this GoogleCloudFilesOptions options)
    {
        var json = BuildCredentialJson(options);
        var credential = GoogleCredential.FromJson(json).UnderlyingCredential as ServiceAccountCredential;
        return UrlSigner.FromCredential(credential);
    }

    private static string BuildCredentialJson(GoogleCloudFilesOptions options)
    {
        return JsonSerializer.Serialize(new GoogleJsonCredentialsModel
        {
            type = "service_account",
            project_id = options.ProjectId,
            private_key_id = options.PrivateKeyId,
            private_key = options.PrivateKey,
            client_email = options.ClientEmail,
            client_id = options.ClientId,
            auth_uri = options.AuthUri,
            token_uri = options.TokenUri,
            auth_provider_x509_cert_url = options.AuthProviderX509CertUrl,
            client_x509_cert_url = options.ClientX509CertUrl,
            universe_domain = options.UniverseDomain
        });
    }
}
