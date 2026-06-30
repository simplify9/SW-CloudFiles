using System.Text.Json;
using Google.Cloud.Storage.V1;

namespace SW.CloudFiles.GC;

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

/// <summary>Factory extensions for building a <see cref="StorageClient"/> from <see cref="GoogleCloudFilesOptions"/>.</summary>
public static class GoogleCloudFileOptionsExtensions
{
    /// <summary>Creates a <see cref="StorageClient"/> authenticated with the service account credentials in <paramref name="options"/>.</summary>
    public static StorageClient BuildGoogleCloudStorageClient(this GoogleCloudFilesOptions options)
    {
        var model = new GoogleJsonCredentialsModel
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
        };

        var json = JsonSerializer.Serialize(model);
        return new StorageClientBuilder { JsonCredentials = json }.Build();
    }
}
