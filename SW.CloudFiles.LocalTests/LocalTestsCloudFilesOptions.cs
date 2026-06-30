using System.IO;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.LocalTests;

/// <summary>
/// Configuration options for the local filesystem provider.
/// </summary>
/// <remarks>
/// <b>This provider is intended for testing and local development only.</b>
/// Do not use it in production. Files are stored on the local filesystem and are not
/// removed automatically on Windows — call <see cref="CloudFilesService.Cleanup"/> in
/// your test teardown to delete all stored files.
/// </remarks>
public class LocalTestsCloudFilesOptions : CloudFilesOptions
{
    /// <summary>
    /// Optional override for the root storage directory.
    /// </summary>
    /// <remarks>
    /// When not set, files are stored inside the OS temporary directory returned by
    /// <see cref="Path.GetTempPath"/> — typically <c>/tmp</c> on Linux and macOS, and
    /// <c>%TEMP%</c> (e.g. <c>C:\Users\&lt;user&gt;\AppData\Local\Temp</c>) on Windows.
    /// The actual path used is <c>{GetTempPath()}/SW.CloudFiles.LocalTests/{BucketName}</c>.
    /// <para>
    /// For most test scenarios you do not need to set this property; the default keeps
    /// files isolated per bucket and well away from your project tree. Override only when
    /// you need a deterministic, well-known path — for example in Docker bind-mount or CI
    /// artifact collection scenarios.
    /// </para>
    /// </remarks>
    public string StoragePath { get; set; }

    /// <summary>
    /// Returns the effective storage root: <see cref="StoragePath"/> when set, otherwise
    /// <c>{Path.GetTempPath()}/SW.CloudFiles.LocalTests/{BucketName}</c>.
    /// </summary>
    public string ResolvedStoragePath =>
        !string.IsNullOrWhiteSpace(StoragePath)
            ? StoragePath
            : Path.Combine(Path.GetTempPath(), "SW.CloudFiles.LocalTests", BucketName ?? "default");
}
