using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SW.CloudFiles.Extensions;
using SW.CloudFiles.LocalTests;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.LocalTests.UnitTests;

[TestClass]
public class Tests
{
    private static ServiceProvider _services = null!;
    private static ICloudFilesService _cloudFiles = null!;
    private static CloudFilesService _concreteService = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddLocalTestsCloudFiles(o =>
        {
            o.BucketName = $"unit-tests-{Guid.NewGuid():N}";
        });

        _services = services.BuildServiceProvider();
        _cloudFiles = _services.GetRequiredService<ICloudFilesService>();
        _concreteService = _services.GetRequiredService<CloudFilesService>();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _concreteService.Cleanup();
        _services.Dispose();
    }

    [TestMethod]
    public async Task WriteAsync_StoresStreamAndReturnsBlob()
    {
        var bytes = Encoding.UTF8.GetBytes("hello from WriteAsync");
        using var stream = new MemoryStream(bytes);

        var blob = await _cloudFiles.WriteAsync(stream, new WriteFileSettings
        {
            Key = "write/hello.txt",
            ContentType = "text/plain"
        });

        Assert.IsNotNull(blob);
        Assert.AreEqual("write/hello.txt", blob.Name);
        Assert.AreEqual("text/plain", blob.MimeType);
        Assert.AreEqual(bytes.Length, blob.Size);
        Assert.IsTrue(blob.Location.StartsWith("file://"), $"Expected file:// URI, got: {blob.Location}");
    }

    [TestMethod]
    public async Task WriteTextAsync_StoresTextAndReturnsBlob()
    {
        var text = "hello from WriteTextAsync";

        var blob = await _cloudFiles.WriteTextAsync(text, new WriteFileSettings
        {
            Key = "write/hello-text.txt",
            ContentType = "text/plain"
        });

        Assert.IsNotNull(blob);
        Assert.AreEqual("write/hello-text.txt", blob.Name);
        Assert.AreEqual("text/plain", blob.MimeType);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(text), blob.Size);
    }

    [TestMethod]
    public async Task OpenReadAsync_ReturnsCorrectContent()
    {
        var key = "read/roundtrip.txt";
        var content = "roundtrip content";
        await _cloudFiles.WriteTextAsync(content, new WriteFileSettings { Key = key, ContentType = "text/plain" });

        await using var stream = await _cloudFiles.OpenReadAsync(key);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var read = await reader.ReadToEndAsync();

        Assert.AreEqual(content, read);
    }

    [TestMethod]
    public async Task ListAsync_ReturnsMatchingKeys()
    {
        var prefix = $"list/{Guid.NewGuid():N}/";
        await _cloudFiles.WriteTextAsync("a", new WriteFileSettings { Key = $"{prefix}a.txt", ContentType = "text/plain" });
        await _cloudFiles.WriteTextAsync("b", new WriteFileSettings { Key = $"{prefix}b.txt", ContentType = "text/plain" });
        await _cloudFiles.WriteTextAsync("other", new WriteFileSettings { Key = "list/other/c.txt", ContentType = "text/plain" });

        var files = (await _cloudFiles.ListAsync(prefix)).ToList();

        Assert.AreEqual(2, files.Count);
        CollectionAssert.Contains(files.Select(f => f.Key).ToList(), $"{prefix}a.txt");
        CollectionAssert.Contains(files.Select(f => f.Key).ToList(), $"{prefix}b.txt");
    }

    [TestMethod]
    public async Task ListAsync_EmptyWhenPrefixHasNoMatches()
    {
        var files = await _cloudFiles.ListAsync("nonexistent-prefix/");
        Assert.AreEqual(0, files.Count());
    }

    [TestMethod]
    public async Task GetMetadataAsync_ReturnsContentLengthAndCustomMetadata()
    {
        var key = "meta/file.txt";
        var text = "metadata test";
        await _cloudFiles.WriteTextAsync(text, new WriteFileSettings
        {
            Key = key,
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["Author"] = "UnitTest" }
        });

        var meta = await _cloudFiles.GetMetadataAsync(key);

        Assert.IsTrue(meta.ContainsKey("ContentLength"), "Expected ContentLength");
        Assert.AreEqual(Encoding.UTF8.GetByteCount(text).ToString(), meta["ContentLength"]);
        Assert.IsTrue(meta.ContainsKey("Author"), "Expected Author metadata");
        Assert.AreEqual("UnitTest", meta["Author"]);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesFile()
    {
        var key = "delete/file.txt";
        await _cloudFiles.WriteTextAsync("to be deleted", new WriteFileSettings { Key = key, ContentType = "text/plain" });

        var deleted = await _cloudFiles.DeleteAsync(key);

        Assert.IsTrue(deleted);

        var files = await _cloudFiles.ListAsync("delete/");
        Assert.IsFalse(files.Any(f => f.Key == key));
    }

    [TestMethod]
    public async Task DeleteAsync_ReturnsTrueForNonExistentKey()
    {
        var deleted = await _cloudFiles.DeleteAsync("does/not/exist.txt");
        Assert.IsTrue(deleted);
    }

    [TestMethod]
    public async Task GetUrl_ReturnsFileUri()
    {
        var key = "urls/file.txt";
        await _cloudFiles.WriteTextAsync("url test", new WriteFileSettings { Key = key, ContentType = "text/plain" });

        var url = _cloudFiles.GetUrl(key);

        Assert.IsTrue(url.StartsWith("file://"), $"Expected file:// URI, got: {url}");
        Assert.IsTrue(url.Contains("urls") && url.Contains("file.txt"));
    }

    [TestMethod]
    public async Task GetSignedUrl_ReturnsSameAsGetUrl()
    {
        var key = "urls/signed.txt";
        await _cloudFiles.WriteTextAsync("signed url test", new WriteFileSettings { Key = key, ContentType = "text/plain" });

        var url = _cloudFiles.GetUrl(key);
        var signed = _cloudFiles.GetSignedUrl(key, TimeSpan.FromHours(1));

        Assert.AreEqual(url, signed);
    }

    [TestMethod]
    public async Task WriteAsync_WithMetadata_StoresAndReturnsMetadata()
    {
        var bytes = Encoding.UTF8.GetBytes("metadata stream");
        using var stream = new MemoryStream(bytes);

        await _cloudFiles.WriteAsync(stream, new WriteFileSettings
        {
            Key = "meta/stream.bin",
            ContentType = "application/octet-stream",
            Metadata = new Dictionary<string, string>
            {
                ["UploadedBy"] = "TestUser",
                ["Tag"] = "integration"
            }
        });

        var meta = await _cloudFiles.GetMetadataAsync("meta/stream.bin");

        Assert.AreEqual("TestUser", meta["UploadedBy"]);
        Assert.AreEqual("integration", meta["Tag"]);
    }

    [TestMethod]
    public void OpenWrite_ThrowsNotImplementedException()
    {
        Assert.ThrowsException<NotImplementedException>(() =>
            _cloudFiles.OpenWrite(new WriteFileSettings { Key = "any/key.bin" }));
    }

    [TestMethod]
    public void Cleanup_DeletesStorageRoot()
    {
        // Use an isolated service so we don't destroy the shared test data
        var options = new LocalTestsCloudFilesOptions { BucketName = $"cleanup-test-{Guid.NewGuid():N}" };
        var svc = new CloudFilesService(options);

        svc.WriteTextAsync("temp", new WriteFileSettings { Key = "temp.txt", ContentType = "text/plain" }).GetAwaiter().GetResult();

        var root = options.ResolvedStoragePath;
        Assert.IsTrue(Directory.Exists(root), "Storage root should exist after write");

        svc.Cleanup();

        Assert.IsFalse(Directory.Exists(root), "Storage root should be deleted after Cleanup()");
    }

    [TestMethod]
    public void StoragePath_DefaultsToOsTempSubdirectory()
    {
        var options = new LocalTestsCloudFilesOptions { BucketName = "my-bucket" };
        var root = options.ResolvedStoragePath;

        Assert.IsTrue(root.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)),
            $"Expected path under GetTempPath(), got: {root}");
        Assert.IsTrue(root.Contains("SW.CloudFiles.LocalTests"));
        Assert.IsTrue(root.EndsWith("my-bucket"));
    }

    [TestMethod]
    public void StoragePath_OverrideIsRespected()
    {
        var custom = Path.Combine(Path.GetTempPath(), "my-custom-path");
        var options = new LocalTestsCloudFilesOptions { BucketName = "ignored", StoragePath = custom };

        Assert.AreEqual(custom, options.ResolvedStoragePath);
    }

    [TestMethod]
    public void DI_RegistersBothInterfaceAndConcreteType()
    {
        var svc = _services.GetRequiredService<ICloudFilesService>();
        var concrete = _services.GetRequiredService<CloudFilesService>();

        Assert.IsNotNull(svc);
        Assert.IsNotNull(concrete);
        Assert.AreSame(svc, concrete, "ICloudFilesService and CloudFilesService should resolve to the same singleton");
    }
}
