using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oci.Common.Auth;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Models;
using Oci.ObjectstorageService.Requests;
using Oci.ObjectstorageService.Transfer;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.OC
{
    /// <summary>Oracle Cloud Infrastructure Object Storage implementation of <see cref="ICloudFilesService"/>.</summary>
    public class CloudFilesService : ICloudFilesService, IDisposable
    {
        private readonly OracleCloudFilesOptions _cloudFilesOptions;
        private readonly UploadManager _uploadManager;
        private readonly ObjectStorageClient _client;
        private readonly ILogger<CloudFilesService> _logger;

        /// <summary>Initialises the service and configures the OCI Object Storage client.</summary>
        public CloudFilesService(OracleCloudFilesOptions cloudFilesOptions, ILogger<CloudFilesService> logger)
        {
            _cloudFilesOptions = cloudFilesOptions;
            _logger = logger;
            var provider = new ConfigFileAuthenticationDetailsProvider(_cloudFilesOptions.ConfigPath, "DEFAULT");
            _client = new ObjectStorageClient(provider);

            _uploadManager = new UploadManager(_client, new UploadConfiguration());
        }

        /// <inheritdoc/>
        public async Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings)
        {
            var mimeType = settings.ContentType ?? "application/octet-stream";
            var metadata = settings.Metadata != null ? new Dictionary<string, string>(settings.Metadata) : null;
            var request = new PutObjectRequest
            {
                NamespaceName = _cloudFilesOptions.NamespaceName,
                ContentType = mimeType,
                BucketName = _cloudFilesOptions.BucketName,
                ObjectName = settings.Key,
                OpcMeta = metadata,
                PutObjectBody = inputStream,
            };

            await _uploadManager.Upload(new UploadManager.UploadRequest(request) { AllowOverwrite = true });

            return new RemoteBlob
            {
                Location = _cloudFilesOptions.GetFileUrl(settings.Key),
                MimeType = mimeType
            };
        }

        /// <inheritdoc/>
        public async Task<RemoteBlob> WriteTextAsync(string text, WriteFileSettings settings)
        {
            var byteArray = Encoding.UTF8.GetBytes(text);
            using var stream = new MemoryStream(byteArray);

            var mimeType = settings.ContentType ?? "text/plain";
            var metadata = settings.Metadata != null ? new Dictionary<string, string>(settings.Metadata) : null;
            var request = new PutObjectRequest
            {
                ContentType = mimeType,
                BucketName = _cloudFilesOptions.BucketName,
                ObjectName = settings.Key,
                OpcMeta = metadata,
                PutObjectBody = stream,
                NamespaceName = _cloudFilesOptions.NamespaceName
            };
            await _uploadManager.Upload(new UploadManager.UploadRequest(request) { AllowOverwrite = true });
            return new RemoteBlob
            {
                Location = _cloudFilesOptions.GetFileUrl(settings.Key),
                MimeType = mimeType
            };
        }

        /// <inheritdoc/>
        public string GetSignedUrl(string key, TimeSpan expiry)
        {
            var response = _client.CreatePreauthenticatedRequest(new CreatePreauthenticatedRequestRequest
            {
                BucketName = _cloudFilesOptions.BucketName,
                NamespaceName = _cloudFilesOptions.NamespaceName,
                CreatePreauthenticatedRequestDetails = new CreatePreauthenticatedRequestDetails
                {
                    Name = $"par-{Guid.NewGuid():N}",
                    ObjectName = key,
                    AccessType = CreatePreauthenticatedRequestDetails.AccessTypeEnum.ObjectRead,
                    TimeExpires = DateTime.UtcNow.Add(expiry)
                }
            }).GetAwaiter().GetResult();

            return $"https://objectstorage.{_cloudFilesOptions.Region}.oraclecloud.com{response.PreauthenticatedRequest.AccessUri}";
        }

        /// <inheritdoc/>
        public string GetUrl(string key) => _cloudFilesOptions.GetFileUrl(key);

        /// <inheritdoc/>
        public WriteWrapper OpenWrite(WriteFileSettings settings)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public async Task<Stream> OpenReadAsync(string key)
        {
            var getObjectObjectRequest = new GetObjectRequest()
            {
                BucketName = _cloudFilesOptions.BucketName,
                NamespaceName = _cloudFilesOptions.NamespaceName,
                ObjectName = key,
            };
            var response = await _client.GetObject(getObjectObjectRequest);
            return response.InputStream;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<CloudFileInfo>> ListAsync(string prefix)
        {
            var response = await _client.ListObjects(new ListObjectsRequest
            {
                BucketName = _cloudFilesOptions.BucketName,
                NamespaceName = _cloudFilesOptions.NamespaceName,
                Prefix = prefix,
                Fields = "size"
            });

            return response.ListObjects.Objects.Select(x => new CloudFileInfo
            {
                Key = x.Name,
                Size = x.Size ?? 0,
            });
        }
        /// <inheritdoc/>
        public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(string key)
        {
            var headObject = await _client.HeadObject(new HeadObjectRequest()
            {
                BucketName = _cloudFilesOptions.BucketName,
                NamespaceName = _cloudFilesOptions.NamespaceName,
                ObjectName = key
            });
 
 
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headObject.OpcMeta != null)
            {
                foreach (var kvp in headObject.OpcMeta)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            result.TryAdd("Hash", headObject.ETag);
            result.TryAdd("ContentType", headObject.ContentType);
            return result;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteAsync(string key)
        {
            try
            {
                await _client.DeleteObject(new DeleteObjectRequest
                {
                    BucketName = _cloudFilesOptions.BucketName,
                    NamespaceName = _cloudFilesOptions.NamespaceName,
                    ObjectName = key
                });
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to delete file");
                return false;
            }
        }

        /// <inheritdoc/>
        public void Dispose() => _client?.Dispose();
    }
}