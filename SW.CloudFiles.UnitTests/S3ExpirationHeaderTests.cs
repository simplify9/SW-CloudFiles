using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.S3;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.UnitTests;

/// <summary>
/// A local fake S3 that answers every request successfully but with a malformed x-amz-expiration
/// (12 Oct 2026 is a Monday, not a Thursday), as one provider was seen to send.
/// </summary>
[TestClass]
public class S3ExpirationHeaderTests
{
    private const string Expiration = "expiry-date=\"Thu, 12 Oct 2026 02:51:59 GMT\", rule-id=\"r\"";
    private static readonly byte[] storedBody = Encoding.UTF8.GetBytes("{ \"labId\": 7 }");

    private static HttpListener listener;
    private static CancellationTokenSource stop;
    private static S3CloudFilesOptions options;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        var port = FreePort();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        stop = new CancellationTokenSource();
        _ = Task.Run(Serve);

        options = new S3CloudFilesOptions
        {
            AccessKeyId = "any",
            SecretAccessKey = "any",
            BucketName = "bucket",
            ServiceUrl = $"http://127.0.0.1:{port}",
            MaxErrorRetry = 0
        };
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        stop.Cancel();
        listener.Close();
    }

    [TestMethod]
    public async Task Upload_succeeds_despite_a_malformed_expiration_header()
    {
        using var cloudFiles = new CloudFilesService(options);

        var blob = await cloudFiles.WriteAsync(new MemoryStream(storedBody), new WriteFileSettings
        {
            Key = "labs/7.json",
            ContentType = "application/json"
        });

        Assert.AreEqual("labs/7.json", blob.Name);
    }

    [TestMethod]
    public async Task Download_succeeds_despite_a_malformed_expiration_header()
    {
        using var cloudFiles = new CloudFilesService(options);

        await using var stream = await cloudFiles.OpenReadAsync("labs/7.json");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        CollectionAssert.AreEqual(storedBody, copy.ToArray());
    }

    private static async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) when (stop.IsCancellationRequested || !listener.IsListening) { return; }

            await context.Request.InputStream.CopyToAsync(Stream.Null);

            var response = context.Response;
            response.StatusCode = 200;
            response.Headers["x-amz-request-id"] = "test";
            // The SDK checks a GET body against an MD5 ETag, so it has to be the real one.
            response.Headers["ETag"] = $"\"{Convert.ToHexStringLower(MD5.HashData(storedBody))}\"";
            response.Headers["x-amz-expiration"] = Expiration;

            if (context.Request.HttpMethod == "GET")
            {
                response.ContentType = "application/json";
                response.ContentLength64 = storedBody.Length;
                await response.OutputStream.WriteAsync(storedBody);
            }
            else
            {
                response.ContentLength64 = 0;
            }

            response.Close();
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
