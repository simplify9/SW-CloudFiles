using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.S3;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.UnitTests;

/// <summary>
/// Runs against a real SeaweedFS in Docker, a store that does not decode aws-chunked bodies and so
/// keeps the chunk framing in the object when the SDK sends one.
/// </summary>
[TestClass]
[TestCategory("Docker")]
public class S3CompatibleStoreTests
{
    private const int S3Port = 8333;
    private const string Bucket = "cloudfiles-tests";
    private const string AccessKey = "cloudfiles";
    private const string SecretKey = "cloudfiles-secret";

    // SeaweedFS rejects signed requests unless at least one identity is configured.
    private const string S3Config = $$"""
        {"identities":[{"name":"tests","credentials":[{"accessKey":"{{AccessKey}}","secretKey":"{{SecretKey}}"}],
          "actions":["Admin","Read","Write","List","Tagging"]}]}
        """;

    private static IContainer seaweed;
    private static S3CloudFilesOptions options;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        seaweed = new ContainerBuilder()
            .WithImage("chrislusf/seaweedfs:3.97")
            .WithResourceMapping(Encoding.UTF8.GetBytes(S3Config), "/etc/seaweedfs/s3.json")
            // Small volumes and a low free-space floor, so a nearly full Docker disk doesn't turn
            // the volume server read-only and fail every write with an internal error.
            .WithCommand("server", "-s3", "-s3.config=/etc/seaweedfs/s3.json",
                "-master.volumeSizeLimitMB=32", "-volume.minFreeSpace=10MiB")
            .WithPortBinding(S3Port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Start Seaweed S3 API Server"))
            .Build();
        await seaweed.StartAsync();

        options = new S3CloudFilesOptions
        {
            AccessKeyId = AccessKey,
            SecretAccessKey = SecretKey,
            BucketName = Bucket,
            ServiceUrl = $"http://{seaweed.Hostname}:{seaweed.GetMappedPublicPort(S3Port)}",
            DisableAutoLifecycle = true
        };

        // The S3 gateway logs "started" a moment before the filer accepts writes.
        using var client = options.CreateClient();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket });
                break;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(1000);
            }
        }
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task ClassCleanup()
    {
        if (seaweed != null) await seaweed.DisposeAsync();
    }

    [TestMethod]
    public async Task Uploaded_stream_is_stored_byte_for_byte()
    {
        var json = Encoding.UTF8.GetBytes("{ \"labId\": 42, \"name\": \"chunk framing check\" }");
        using var cloudFiles = new CloudFilesService(options);

        await cloudFiles.WriteAsync(new MemoryStream(json), new WriteFileSettings
        {
            Key = "checksum/stream.json",
            ContentType = "application/json"
        });

        AssertStored(json, await DownloadRaw(cloudFiles, "checksum/stream.json"));
    }

    [TestMethod]
    public async Task Uploaded_text_is_stored_byte_for_byte()
    {
        const string json = "{ \"labId\": 43 }";
        using var cloudFiles = new CloudFilesService(options);

        await cloudFiles.WriteTextAsync(json, new WriteFileSettings
        {
            Key = "checksum/text.json",
            ContentType = "application/json"
        });

        AssertStored(Encoding.UTF8.GetBytes(json), await DownloadRaw(cloudFiles, "checksum/text.json"));
    }

    private static void AssertStored(byte[] uploaded, byte[] stored) =>
        CollectionAssert.AreEqual(uploaded, stored, $"Stored object was: {Encoding.UTF8.GetString(stored)}");

    // Plain HTTP on a signed URL, so the SDK's response handling plays no part in what we compare.
    // The SDK signs URLs as https whatever the endpoint; the scheme isn't part of the signature.
    private static async Task<byte[]> DownloadRaw(CloudFilesService cloudFiles, string key)
    {
        var url = new UriBuilder(cloudFiles.GetSignedUrl(key, TimeSpan.FromMinutes(5))) { Scheme = "http" };
        using var http = new HttpClient();
        return await http.GetByteArrayAsync(url.Uri);
    }
}
