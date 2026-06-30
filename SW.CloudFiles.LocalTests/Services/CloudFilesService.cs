using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.LocalTests;

/// <summary>
/// Local filesystem implementation of <see cref="ICloudFilesService"/>.
/// </summary>
/// <remarks>
/// <b>For testing and local development only.</b> Files are stored on the local
/// filesystem under <see cref="LocalTestsCloudFilesOptions.StoragePath"/> (or a
/// subfolder of <see cref="Path.GetTempPath"/> when not overridden). Unlike Linux and
/// macOS, Windows does not automatically clear the temp directory — call
/// <see cref="Cleanup"/> in your test teardown to remove all files written during the
/// test run.
/// </remarks>
public class CloudFilesService(LocalTestsCloudFilesOptions options) : ICloudFilesService, IDisposable
{
    private readonly string _root = options.ResolvedStoragePath;

    private string ToAbsolutePath(string key) =>
        Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    private string ToMetaPath(string key) => ToAbsolutePath(key) + ".meta.json";

    private static void EnsureParentDirectory(string filePath) =>
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteAsync(Stream inputStream, WriteFileSettings settings)
    {
        var path = ToAbsolutePath(settings.Key);
        EnsureParentDirectory(path);

        await using var fs = File.Create(path);
        await inputStream.CopyToAsync(fs);
        await fs.FlushAsync();

        if (settings.Metadata?.Count > 0)
            await File.WriteAllTextAsync(ToMetaPath(settings.Key),
                JsonSerializer.Serialize(settings.Metadata));

        return new RemoteBlob
        {
            Location = GetUrl(settings.Key),
            Name = settings.Key,
            MimeType = settings.ContentType,
            Size = (int)new FileInfo(path).Length
        };
    }

    /// <inheritdoc/>
    public async Task<RemoteBlob> WriteTextAsync(string text, WriteFileSettings settings)
    {
        var path = ToAbsolutePath(settings.Key);
        EnsureParentDirectory(path);

        // Use BOM-free UTF-8 so byte count matches file size exactly
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));

        if (settings.Metadata?.Count > 0)
            await File.WriteAllTextAsync(ToMetaPath(settings.Key),
                JsonSerializer.Serialize(settings.Metadata));

        var byteCount = Encoding.UTF8.GetByteCount(text);
        return new RemoteBlob
        {
            Location = GetUrl(settings.Key),
            Name = settings.Key,
            MimeType = settings.ContentType ?? "text/plain",
            Size = byteCount
        };
    }

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string key) =>
        Task.FromResult<Stream>(File.OpenRead(ToAbsolutePath(key)));

    /// <inheritdoc/>
    public Task<IEnumerable<CloudFileInfo>> ListAsync(string prefix)
    {
        if (!Directory.Exists(_root))
            return Task.FromResult(Enumerable.Empty<CloudFileInfo>());

        var results = Directory
            .EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var key = f[(_root.Length + 1)..]
                    .Replace(Path.DirectorySeparatorChar, '/');
                return (key, fi: new FileInfo(f));
            })
            .Where(x => x.key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(x => new CloudFileInfo { Key = x.key, Size = x.fi.Length });

        return Task.FromResult<IEnumerable<CloudFileInfo>>(results);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(string key)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var fi = new FileInfo(ToAbsolutePath(key));
        if (fi.Exists)
        {
            result["ContentLength"] = fi.Length.ToString();
            result["LastModified"] = fi.LastWriteTimeUtc.ToString("O");
        }

        var metaPath = ToMetaPath(key);
        if (File.Exists(metaPath))
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await File.ReadAllTextAsync(metaPath));
            if (stored is not null)
                foreach (var kv in stored)
                    result[kv.Key] = kv.Value;
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key)
    {
        var path = ToAbsolutePath(key);
        if (File.Exists(path)) File.Delete(path);

        var metaPath = ToMetaPath(key);
        if (File.Exists(metaPath)) File.Delete(metaPath);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public string GetUrl(string key) => new Uri(ToAbsolutePath(key)).AbsoluteUri;

    /// <summary>
    /// Returns a <c>file://</c> URI for <paramref name="key"/>. The <paramref name="expiry"/>
    /// parameter is accepted for interface compatibility but has no effect in this local provider.
    /// </summary>
    public string GetSignedUrl(string key, TimeSpan expiry) => GetUrl(key);

    /// <summary>Not supported by the local filesystem provider.</summary>
    /// <exception cref="NotImplementedException">Always thrown.</exception>
    public WriteWrapper OpenWrite(WriteFileSettings settings) =>
        throw new NotImplementedException("OpenWrite is not supported by the local filesystem provider.");

    /// <summary>
    /// Deletes all files and subdirectories under the storage root directory.
    /// </summary>
    /// <remarks>
    /// Call this in your test teardown — for example in an <c>[ClassCleanup]</c> method or
    /// <c>IAsyncLifetime.DisposeAsync</c> — to remove all files written during the test run.
    /// This is especially important on Windows, where the temp directory is not cleared
    /// automatically by the operating system.
    /// </remarks>
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <inheritdoc/>
    public void Dispose() { }
}
