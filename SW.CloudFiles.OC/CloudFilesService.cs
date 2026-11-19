using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oci.Common.Auth;
using Oci.Common.Model;
using Oci.ObjectstorageService;
using Oci.ObjectstorageService.Models;
using Oci.ObjectstorageService.Requests;
using Oci.ObjectstorageService.Transfer;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.OC
{
    /// <summary>
    /// Oracle Cloud Infrastructure Object Storage implementation of <see cref="ICloudFilesService"/>,
    /// <see cref="ICloudFilesLifecycle"/>, <see cref="ICloudFilesCopy"/> and <see cref="ICloudFilesProperties"/>.
    /// </summary>
    public class CloudFilesService : ICloudFilesService, ICloudFilesLifecycle, ICloudFilesCopy, ICloudFilesProperties, IDisposable
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
                MimeType = mimeType,
                Name = settings.Key,
                Size = inputStream.CanSeek ? (int)inputStream.Length : 0
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
                MimeType = mimeType,
                Name = settings.Key,
                Size = byteArray.Length
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
            // ListObjects returns one page (1,000 objects by default); NextStartWith names the first
            // object of the next page and is null once the listing is complete.
            var result = new List<CloudFileInfo>();
            string start = null;

            do
            {
                var response = await _client.ListObjects(new ListObjectsRequest
                {
                    BucketName = _cloudFilesOptions.BucketName,
                    NamespaceName = _cloudFilesOptions.NamespaceName,
                    Prefix = prefix,
                    Start = start,
                    Fields = "size"
                });

                result.AddRange(response.ListObjects.Objects.Select(x => new CloudFileInfo
                {
                    Key = x.Name,
                    Size = x.Size ?? 0,
                }));

                start = response.ListObjects.NextStartWith;
            } while (!string.IsNullOrEmpty(start));

            return result;
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
            result.TryAdd("Hash", headObject.ETag?.Trim('"'));
            result.TryAdd("ContentType", headObject.ContentType);
            result.TryAdd("ContentLength", (headObject.ContentLength ?? 0).ToString());
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
        /// <remarks>
        /// Oracle copies asynchronously through a work request; this waits for it, checking every
        /// second, until it completes, fails or <paramref name="cancellationToken"/> is cancelled.
        /// Content type and metadata are copied. Not covered by tests (they need a live tenancy).
        /// </remarks>
        public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken cancellationToken = default)
        {
            var response = await _client.CopyObject(new CopyObjectRequest
            {
                NamespaceName = _cloudFilesOptions.NamespaceName,
                BucketName = _cloudFilesOptions.BucketName,
                CopyObjectDetails = new CopyObjectDetails
                {
                    SourceObjectName = sourceKey,
                    DestinationRegion = _cloudFilesOptions.Region,
                    DestinationNamespace = _cloudFilesOptions.NamespaceName,
                    DestinationBucket = _cloudFilesOptions.BucketName,
                    DestinationObjectName = destinationKey,
                }
            }, cancellationToken: cancellationToken);

            while (true)
            {
                var workRequest = (await _client.GetWorkRequest(new GetWorkRequestRequest
                {
                    WorkRequestId = response.OpcWorkRequestId
                }, cancellationToken: cancellationToken)).WorkRequest;

                switch (workRequest.Status)
                {
                    case WorkRequest.StatusEnum.Completed:
                        return;
                    case WorkRequest.StatusEnum.Failed:
                    case WorkRequest.StatusEnum.Canceled:
                        throw new InvalidOperationException(
                            $"Copying '{sourceKey}' to '{destinationKey}' ended as {workRequest.Status} (work request {response.OpcWorkRequestId}).");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        /// <inheritdoc/>
        public async Task<CloudFileProperties> GetPropertiesAsync(string key, CancellationToken cancellationToken = default)
        {
            Oci.ObjectstorageService.Responses.HeadObjectResponse headObject;
            try
            {
                headObject = await _client.HeadObject(new HeadObjectRequest
                {
                    BucketName = _cloudFilesOptions.BucketName,
                    NamespaceName = _cloudFilesOptions.NamespaceName,
                    ObjectName = key
                }, cancellationToken: cancellationToken);
            }
            catch (OciException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            return new CloudFileProperties
            {
                Key = key,
                ContentLength = headObject.ContentLength ?? 0,
                ContentType = headObject.ContentType,
                ETag = headObject.ETag?.Trim('"'),
                LastModified = headObject.LastModified,
                Metadata = new Dictionary<string, string>(headObject.OpcMeta ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
            };
        }

        /// <inheritdoc/>
        public async Task<CloudFilesLifecycle> GetLifecycleAsync(CancellationToken cancellationToken = default)
        {
            IEnumerable<ObjectLifecycleRule> rules;
            try
            {
                var response = await _client.GetObjectLifecyclePolicy(new GetObjectLifecyclePolicyRequest
                {
                    BucketName = _cloudFilesOptions.BucketName,
                    NamespaceName = _cloudFilesOptions.NamespaceName
                }, cancellationToken: cancellationToken);
                rules = response.ObjectLifecyclePolicy?.Items ?? new List<ObjectLifecycleRule>();
            }
            catch (OciException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // A bucket that never had a policy has none to return.
                rules = new List<ObjectLifecycleRule>();
            }

            return new CloudFilesLifecycle
            {
                Provider = "Oracle",
                Bucket = _cloudFilesOptions.BucketName,
                Rules = OracleLifecycleRules.ToDeletionRules(rules)
            };
        }

        /// <inheritdoc/>
        public void Dispose() => _client?.Dispose();
    }
}