using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Storage.V1;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.GC;

/// <summary>
/// Google Cloud Storage implementation of <see cref="ICloudFilesService"/>, <see cref="ICloudFilesLifecycle"/>,
/// <see cref="ICloudFilesCopy"/> and <see cref="ICloudFilesProperties"/>.
/// </summary>
public class CloudFilesService(GoogleCloudFilesOptions options, StorageClient storageClient, UrlSigner urlSigner)
    : ICloudFilesService, ICloudFilesLifecycle, ICloudFilesCopy, ICloudFilesProperties
{
    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings)
    {
        var obj = await storageClient.UploadObjectAsync(new Google.Apis.Storage.v1.Data.Object
        {
            Bucket = options.BucketName,
            Name = settings.Key,
            ContentType = settings.ContentType,
            Metadata = settings.Metadata is { Count: > 0 } ? new Dictionary<string, string>(settings.Metadata) : null,
        }, inputStream);

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
    /// <remarks>
    /// Not supported, as on Azure and Oracle. It used to send a PUT to a URL signed for GET, which
    /// Google Cloud Storage refuses because the method is part of the signature. Use
    /// <see cref="WriteAsync"/>.
    /// </remarks>
    public WriteWrapper OpenWrite(WriteFileSettings settings) =>
        throw new NotImplementedException(
            "OpenWrite is not supported by the Google Cloud Storage provider; use WriteAsync.");

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
        var result = new Dictionary<string, string>(obj.Metadata ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        // The same system values the S3 and Azure providers return.
        result.TryAdd("Hash", obj.ETag?.Trim('"'));
        result.TryAdd("ContentType", obj.ContentType);
        result.TryAdd("ContentLength", (obj.Size ?? 0).ToString());
        return new ReadOnlyDictionary<string, string>(result);
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

    /// <inheritdoc/>
    /// <remarks>One CopyObject request within the bucket; content type and metadata are copied.</remarks>
    public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken cancellationToken = default)
    {
        await storageClient.CopyObjectAsync(options.BucketName, sourceKey, options.BucketName, destinationKey,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CloudFileProperties> GetPropertiesAsync(string key, CancellationToken cancellationToken = default)
    {
        Google.Apis.Storage.v1.Data.Object obj;
        try
        {
            obj = await storageClient.GetObjectAsync(options.BucketName, key, cancellationToken: cancellationToken);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return new CloudFileProperties
        {
            Key = key,
            ContentLength = (long)(obj.Size ?? 0),
            ContentType = obj.ContentType,
            ETag = obj.ETag?.Trim('"'),
            // Parsed from the raw RFC 3339 text: the SDK's own getter throws on some valid variants
            // (different fractional-second precision), and a timestamp mustn't fail the whole call.
            LastModified = DateTimeOffset.TryParse(obj.UpdatedRaw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var updated) ? updated : null,
            Metadata = new Dictionary<string, string>(obj.Metadata ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <inheritdoc/>
    public async Task<CloudFilesLifecycle> GetLifecycleAsync(CancellationToken cancellationToken = default)
    {
        var bucket = await storageClient.GetBucketAsync(options.BucketName, cancellationToken: cancellationToken);

        return new CloudFilesLifecycle
        {
            Provider = "Google",
            Bucket = options.BucketName,
            Rules = GoogleLifecycleRules.ToDeletionRules(bucket.Lifecycle?.Rule)
        };
    }
}
