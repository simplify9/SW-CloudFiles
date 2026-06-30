using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.GC;

/// <summary>Google Cloud Storage implementation of <see cref="ICloudFilesService"/>.</summary>
public class CloudFilesService(GoogleCloudFilesOptions options, StorageClient storageClient, UrlSigner urlSigner)
    : ICloudFilesService
{
    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings)
    {
        var obj = await storageClient.UploadObjectAsync(options.BucketName, settings.Key, settings.ContentType, inputStream);

        return new RemoteBlob
        {
            Location = settings.Public ? GetUrl(settings.Key) : GetSignedUrl(settings.Key, TimeSpan.FromHours(1)),
            MimeType = settings.ContentType,
            Name = settings.Key,
            Size = (int)(obj.Size ?? 0)
        };
    }

    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteTextAsync(string text, WriteFileSettings settings)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return await WriteAsync(stream, settings);
    }

    /// <inheritdoc/>
    public string GetSignedUrl(string key, TimeSpan expiry)
    {
        return urlSigner.Sign(options.BucketName, key, expiry);
    }

    /// <inheritdoc/>
    public string GetUrl(string key)
    {
        return $"https://storage.googleapis.com/{options.BucketName}/{key}";
    }

    /// <inheritdoc/>
    public WriteWrapper OpenWrite(WriteFileSettings settings)
    {
        var url = GetSignedUrl(settings.Key, TimeSpan.FromHours(1));

        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "PUT";
        request.ContentType = settings.ContentType;

        foreach (var metadata in settings.Metadata)
            request.Headers[metadata.Key] = metadata.Value;

        return new WriteWrapper(request, this, settings);
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string key)
    {
        var stream = new MemoryStream();
        await storageClient.DownloadObjectAsync(options.BucketName, key, stream);
        stream.Position = 0;
        return stream;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<CloudFileInfo>> ListAsync(string prefix)
    {
        var fileList = new List<CloudFileInfo>();

        await foreach (var obj in storageClient.ListObjectsAsync(options.BucketName, prefix))
        {
            fileList.Add(new CloudFileInfo
            {
                Key = obj.Name,
                Size = (long)(obj.Size ?? 0),
                Signature = obj.Md5Hash != null
                    ? Convert.ToBase64String(Convert.FromBase64String(obj.Md5Hash))
                    : string.Empty
            });
        }

        return fileList;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(string key)
    {
        var obj = await storageClient.GetObjectAsync(options.BucketName, key);
        return new ReadOnlyDictionary<string, string>(obj.Metadata ?? new Dictionary<string, string>());
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            await storageClient.DeleteObjectAsync(options.BucketName, key);
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
