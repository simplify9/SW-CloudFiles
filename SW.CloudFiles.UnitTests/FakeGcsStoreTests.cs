using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Cloud.Storage.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.GC;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.UnitTests;

/// <summary>
/// The Google provider against fake-gcs-server in Docker. Signed URLs need a service account key, so
/// these tests write with <see cref="WriteFileSettings.Public"/> set (the returned location is then the
/// plain URL) and never sign. fake-gcs-server 1.52 doesn't keep the content type of an SDK upload
/// (the SDK's own <c>UploadObjectAsync(bucket, name, contentType, …)</c> comes back as
/// <c>application/octet-stream</c> too), so content type isn't asserted here.
/// </summary>
[TestClass]
[TestCategory("Docker")]
public class FakeGcsStoreTests
{
    private const int Port = 4443;
    private const string Bucket = "cloudfiles-tests";

    private static IContainer gcs;
    private static StorageClient client;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Resumable uploads are redirected to the server's external URL, so it must know the host port:
        // pick a free one and bind it explicitly instead of letting Docker choose.
        var hostPort = FreePort();
        gcs = new ContainerBuilder()
            .WithImage("fsouza/fake-gcs-server:1.52.2")
            .WithCommand("-scheme", "http", "-port", Port.ToString(), "-external-url", $"http://localhost:{hostPort}")
            .WithPortBinding(hostPort, Port)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("server started"))
            .Build();
        await gcs.StartAsync();

        client = await new StorageClientBuilder
        {
            BaseUri = $"http://localhost:{hostPort}/storage/v1/",
            UnauthenticatedAccess = true,
        }.BuildAsync();
        await client.CreateBucketAsync("test-project", Bucket);
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task ClassCleanup()
    {
        client?.Dispose();
        if (gcs != null) await gcs.DisposeAsync();
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static CloudFilesService Service() =>
        new(new GoogleCloudFilesOptions { BucketName = Bucket }, client, urlSigner: null);

    private static WriteFileSettings Settings(string key, string contentType = null,
        Dictionary<string, string> metadata = null) =>
        new() { Key = key, Public = true, ContentType = contentType, Metadata = metadata ?? new Dictionary<string, string>() };

    [TestMethod]
    public async Task Writes_keep_metadata_and_metadata_reports_the_system_values()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteTextAsync("hello", Settings("meta/item.txt", "text/plain",
            new Dictionary<string, string> { ["kind"] = "greeting" }));

        var metadata = await cloudFiles.GetMetadataAsync("meta/item.txt");
        Assert.AreEqual("greeting", metadata["Kind"]);
        Assert.IsTrue(metadata.ContainsKey("ContentType"));
        Assert.AreEqual("5", metadata["ContentLength"]);
        Assert.IsTrue(metadata.ContainsKey("Hash"));
    }

    [TestMethod]
    public async Task Copy_keeps_content_and_metadata()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteAsync(new MemoryStream(Encoding.UTF8.GetBytes("{}")), Settings("copy/source.json",
            "application/json", new Dictionary<string, string> { ["origin"] = "copy-test" }));

        await cloudFiles.CopyAsync("copy/source.json", "copy/destination.json");

        var copied = await cloudFiles.GetPropertiesAsync("copy/destination.json");
        Assert.IsNotNull(copied);
        Assert.AreEqual(2, copied.ContentLength);
        Assert.AreEqual("copy-test", copied.Metadata["origin"]);
    }

    [TestMethod]
    public async Task Properties_are_null_for_a_missing_key_and_never_match_by_prefix()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteTextAsync("abc", Settings("props/item-long.txt"));

        Assert.AreEqual(3, (await cloudFiles.GetPropertiesAsync("props/item-long.txt"))!.ContentLength);
        Assert.IsNull(await cloudFiles.GetPropertiesAsync("props/item"));
        Assert.IsFalse(await cloudFiles.ExistsAsync("props/missing.txt"));
    }

    [TestMethod]
    public void OpenWrite_is_not_supported()
    {
        Assert.ThrowsException<System.NotImplementedException>(() => Service().OpenWrite(Settings("x")));
    }
}
