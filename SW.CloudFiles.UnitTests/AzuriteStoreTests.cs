using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.CloudFiles.AS;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.UnitTests;

/// <summary>
/// The Azure provider against Azurite (Microsoft's storage emulator) in Docker, with a shared key,
/// as an account outside Azure would be reached.
/// </summary>
[TestClass]
[TestCategory("Docker")]
public class AzuriteStoreTests
{
    private const int BlobPort = 10000;
    private const string Account = "devstoreaccount1";

    // Azurite's well-known development key, published in its documentation; not a secret.
    private const string AccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private static IContainer azurite;
    private static AzureCloudFilesOptions options;
    private static BlobContainerClient container;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        azurite = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.33.0")
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck", "--loose")
            .WithPortBinding(BlobPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Azurite Blob service successfully listens"))
            .Build();
        await azurite.StartAsync();

        options = new AzureCloudFilesOptions
        {
            AccessKeyId = Account,
            SecretAccessKey = AccountKey,
            BucketName = "cloudfiles-tests",
            ServiceUrl = $"http://{azurite.Hostname}:{azurite.GetMappedPublicPort(BlobPort)}/{Account}",
        };
        container = options.CreateClient();
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task ClassCleanup()
    {
        if (azurite != null) await azurite.DisposeAsync();
    }

    private static CloudFilesService Service() => new(options, container);

    [TestMethod]
    public async Task Copy_keeps_content_type_and_metadata()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteAsync(new MemoryStream(Encoding.UTF8.GetBytes("<a/>")), new WriteFileSettings
        {
            Key = "copy/source.xml",
            ContentType = "application/xml",
            Metadata = new Dictionary<string, string> { ["origin"] = "copy-test" }
        });

        await cloudFiles.CopyAsync("copy/source.xml", "copy/destination.xml");

        var copied = await cloudFiles.GetPropertiesAsync("copy/destination.xml");
        Assert.IsNotNull(copied);
        Assert.AreEqual(4, copied.ContentLength);
        Assert.AreEqual("application/xml", copied.ContentType);
        Assert.AreEqual("copy-test", copied.Metadata["ORIGIN"]);

        using var reader = new StreamReader(await cloudFiles.OpenReadAsync("copy/destination.xml"));
        Assert.AreEqual("<a/>", await reader.ReadToEndAsync());
    }

    [TestMethod]
    public async Task Properties_are_null_for_a_missing_key_and_never_match_by_prefix()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteTextAsync("abc", new WriteFileSettings { Key = "props/item-long.txt" });

        var found = await cloudFiles.GetPropertiesAsync("props/item-long.txt");
        Assert.IsNotNull(found);
        Assert.AreEqual(3, found.ContentLength);
        Assert.IsFalse(found.ETag.Contains('"'));
        Assert.IsNotNull(found.LastModified);

        Assert.IsNull(await cloudFiles.GetPropertiesAsync("props/item"));
        Assert.IsFalse(await cloudFiles.ExistsAsync("props/missing.txt"));
    }

    [TestMethod]
    public async Task Deleting_a_missing_blob_returns_false_instead_of_throwing()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteTextAsync("x", new WriteFileSettings { Key = "delete/present.txt" });

        Assert.IsTrue(await cloudFiles.DeleteAsync("delete/present.txt"));
        Assert.IsFalse(await cloudFiles.DeleteAsync("delete/present.txt"));
    }

    [TestMethod]
    public async Task Metadata_reads_case_insensitively_with_the_system_values()
    {
        var cloudFiles = Service();
        await cloudFiles.WriteTextAsync("hi", new WriteFileSettings
        {
            Key = "meta/item.txt",
            Metadata = new Dictionary<string, string> { ["kind"] = "greeting" }
        });

        var metadata = await cloudFiles.GetMetadataAsync("meta/item.txt");
        Assert.AreEqual("greeting", metadata["Kind"]);
        Assert.AreEqual("2", metadata["contentlength"]);
        Assert.IsTrue(metadata.ContainsKey("Hash"));
    }
}
