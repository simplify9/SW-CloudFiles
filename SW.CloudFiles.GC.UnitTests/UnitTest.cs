using SW.CloudFiles.Extensions;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.GC.UnitTests;

[TestClass]
public class UnitTest
{
    [TestMethod]
    public async Task TestBasic()
    {
        var options = new GoogleCloudFilesOptions();
        var storageClient = options.BuildGoogleCloudStorageClient();
        Assert.IsNotNull(storageClient);
        var urlSigner = options.BuildUrlSigner();
        var cloudFilesService = new CloudFilesService(options, storageClient, urlSigner);
        Assert.IsNotNull(cloudFilesService);
        var key = $"test{Guid.NewGuid():N}.txt";
        await cloudFilesService.WriteTextAsync("test", new WriteFileSettings
        {
            Key = key,
            ContentType = "text/plain"
        });
    }
}
