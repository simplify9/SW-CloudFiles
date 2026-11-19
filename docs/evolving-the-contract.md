# Evolving the contract

How to add capabilities to SW.CloudFiles without breaking the applications that use it, and which
capabilities are worth adding. Written in November 2026, after a review of the interface against
the S3, Azure, Google and Oracle APIs and of what consuming applications actually call.

## Where the contract lives

`ICloudFilesService`, `ICloudFilesLifecycle`, `WriteFileSettings`, `CloudFileInfo`, `RemoteBlob`,
`WriteWrapper` and `CloudFilesOptions` are defined in **SimplyWorks.PrimitiveTypes**
(`SW.PrimitiveTypes/Contracts/CloudFiles/`), not in this repository. The interface is implemented by:

- the five providers here (S3, Azure, Google, Oracle, Local);
- **any application** that writes its own implementation: test fakes, "not implemented" stubs for
  hosts without storage, decorators that add logging or caching.

So a new member on `ICloudFilesService` is a **breaking change for every implementation outside
this repository**, even though it looks additive. Those applications stop compiling (or throw
`TypeLoadException` at runtime with an older binary) the day they take the new PrimitiveTypes.

## Rules

1. **Never change or remove an existing member** of `ICloudFilesService` or the shared models. No
   renames, no signature changes, no new required parameters.
2. **Add capabilities as separate interfaces**, one per capability, implemented by the provider's
   `CloudFilesService` and registered in DI next to `ICloudFilesService`. `ICloudFilesLifecycle` (10.0.1)
   is the model:

   ```csharp
   // PrimitiveTypes
   public interface ICloudFilesCopy
   {
       Task CopyAsync(string sourceKey, string destinationKey, CancellationToken cancellationToken = default);
   }

   // each provider's Add*CloudFiles()
   serviceCollection.AddSingleton<ICloudFilesCopy>(sp => sp.GetRequiredService<CloudFilesService>());
   ```

   A consumer that needs the capability injects `ICloudFilesCopy`. If the configured provider lacks
   it, the app fails at startup with a clear DI error instead of a `NotSupportedException` halfway
   through a job, and implementations that don't care are untouched.
3. **New fields on models are additive and optional.** Add a new property rather than changing a
   type: for example a `long ContentLength` next to `RemoteBlob.Size` (an `int`), with `Size` marked
   `[Obsolete]` but still filled where it fits.
4. **Every provider ships the capability, or says why not.** If a provider can't support it, it
   doesn't register the interface, and [provider behaviour](provider-behavior.md) records that.
5. **Release order:** PrimitiveTypes first, then this repository (bump the PrimitiveTypes reference,
   implement, test), then consumers. `main` publishes the 10.0.x line (net10.0) only. The 8.1.x line
   (net8.0) ended at 8.1.13 and has no branch: a net8 application gets new fixes only by moving to
   net10, or by someone cutting a release branch from the `8.1.13` tag and backporting.
6. **Behaviour fixes inside a provider need no contract change.** Make them in the provider, note
   them in the [changelog](../CHANGELOG.md), and update the behaviour page. Where a fix changes what
   callers observe (as the `ListAsync` paging fix did), say so in the changelog.

## What to add, in order

Ranked by how often consuming applications already work around the gap.

| # | Capability | Why | Mapping |
|---|---|---|---|
| 1 | **Behaviour fixes** (no contract change) | Same call, different result per provider: see the list below | per provider |
| 2 | **Server-side copy** (`ICloudFilesCopy`) | Applications download and re-upload, or call an SDK directly (tying the code to one provider), to copy or "move" files. A move is copy + delete, never atomic | S3 `CopyObject` (≤5 GB in one call, multipart copy above); Azure `SyncCopyFromUri`/`StartCopyFromUri`; Google `CopyObject`/`RewriteObject`; Oracle `CopyObject` (asynchronous work request); Local file copy |
| 3 | **Properties and existence** (`ICloudFilesProperties`: `GetPropertiesAsync` returning `null` when missing) | Applications call `ListAsync(key).Any()` to test existence, which lists by prefix and so false-matches longer keys. It also gives a portable "not found" without provider exceptions | S3/Oracle `HeadObject`, Azure `GetProperties`, Google `GetObject` (metadata only), Local `FileInfo` |
| 4 | **`CancellationToken`** | No call can be cancelled today; long downloads outlive the request that started them | overloads on the new interfaces; existing members stay |
| 5 | **Size as `long`** | `RemoteBlob.Size` overflows above 2 GiB | additive `long` property |
| 6 | **Upload URLs** (presigned PUT) | Lets clients upload directly to storage. `OpenWrite` partly covers this on S3 only | S3 presigned PUT, Azure SAS with write, Google signed PUT, Oracle PAR `ObjectWrite` |

Add these only when an application needs them:

- **Range reads**: partial downloads, resumable transfers, media. All four stores support `Range`.
- **Conditional writes** (create-if-absent, replace-if-unchanged): optimistic concurrency on shared
  objects. Use an opaque version token, because Google uses generations rather than ETags.
- **Batch delete**: one request per 1,000 keys on S3 and Oracle, 256 on Azure. It isn't atomic, so
  return a result per key, not a single `bool`.
- **Streamed listing** (`IAsyncEnumerable<CloudFileInfo>`, or paged with a continuation token): for
  prefixes too large to hold in memory.
- **Versions**: list, read and delete a specific version, where bucket versioning is enabled.

### Behaviour fixes worth making first

Each one is a provider-only change (see [provider behaviour](provider-behavior.md) for details):

- Google: honour `Metadata` and `Public` on writes; stream `OpenReadAsync` instead of buffering it;
  sign `OpenWrite` URLs for PUT (or throw, like Azure and Oracle).
- S3: store `Metadata` on `WriteTextAsync`; report `Size` in bytes, not characters.
- Oracle: fill `RemoteBlob.Name` and `Size`; stop turning every delete error into `false`.
- `GetMetadataAsync`: return `Hash`, `ContentType` and `ContentLength` on every provider, with
  case-insensitive keys.
- `DeleteAsync`: agree on one meaning for a missing key (returning `false` is the most useful).
- `WriteAsync`: accept streams that can't report their length.

## What not to add

Provider administration features have no common semantics across the four stores, and an
application that needs one is better served by the provider's SDK or a separate provider-specific
package than by a lowest-common-denominator interface:

- bulk job services (S3 Batch Operations, Google Storage Batch Operations), S3 Inventory and Storage
  Lens;
- Azure blob index tags and Find Blobs by Tags, blob leases, change feed, snapshots;
- Google object compose, Oracle native rename, archive-tier restore;
- retention, legal hold and object lock policies, replication, event notifications.

**Searching files by business attributes** (customer, document type, status) belongs in the
application's database, not in storage tags: keep an index row per file with its key, and let
storage hold only the bytes. That works the same on every provider and is queryable.
