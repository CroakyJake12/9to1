using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed class DeveloperWindowsMetadataIncompleteException(bool published) : IOException(
        published ? "The exact Windows metadata name was published, but its complete durability/readback/cleanup outcome is unknown."
                  : "Windows native metadata staging occurred, but its create-only publication outcome is incomplete or unknown.")
    {
        public bool OriginalNamePublished => published;
        public bool OriginalOutcomeUnknown => true;
    }
    private sealed partial class DeveloperCaptureSource
    {
        private async Task ObserveWindowsMetadataCleanup(WorkspaceMetadataOperation operation, Func<Task> fixedOwnedCleanup)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { InvokeSavedRootOwnedCleanup(() => { actual = fixedOwnedCleanup() ?? throw new InvalidOperationException("Owned Windows stream cleanup returned no actual Task."); RetainMetadata(operation, actual); return true; }); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            ThrowOriginalErrors(errors);
        }
        private static void DemandWindowsWorkspaceMetadata(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation)
        {
            preparation.WindowsRoot!.DemandCurrent(); var sid = preparation.WindowsSid!;
            DemandDeveloperWindowsOwner(preparation.AncestorHandle!, sid);
            if (preparation.WindowsAncestorIdentity is not { } ancestor || !ReadDeveloperWindowsIdentity(preparation.AncestorHandle!).SameFile(ancestor))
                throw new IOException("The actual original Windows metadata ancestor changed.");
            var path = preparation.Ancestor;
            for (var i = 0; i < operation.Parents.Count; i++)
            {
                if (i != 0) path = Path.Combine(path, preparation.Components[i - 1]);
                DemandDeveloperWindowsPath(operation.Parents[i], path, true); DemandDeveloperWindowsCase(operation.Parents[i]);
                DemandDeveloperWindowsOwner(operation.Parents[i], sid);
                var identity = ReadDeveloperWindowsIdentity(operation.Parents[i]);
                if (!identity.IsDirectory || identity.Volume != ancestor.Volume) throw new UnauthorizedAccessException("The held original Windows metadata parent is unavailable or on another volume.");
            }
            if (operation.LockHandle is not null)
            {
                DemandDeveloperWindowsPath(operation.LockHandle, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".lock"), false);
                DemandDeveloperWindowsOwner(operation.LockHandle, sid); var identity = ReadDeveloperWindowsIdentity(operation.LockHandle);
                if (!identity.IsRegular || identity.Links != 1 || identity.Volume != ancestor.Volume) throw new UnauthorizedAccessException("The participating actual Windows metadata lock changed.");
            }
        }
        private async Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> WriteWindowsWorkspaceMetadata(
            WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation, CancellationToken token)
        {
            var errors = new List<Exception>(); WorkspaceMetadataObservation? observation = null;
            try
            {
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandWorkspaceMetadata(preparation, operation, token);
                    using var security = new DeveloperWindowsPrivateDescriptor(preparation.WindowsSid!);
                    var parent = preparation.WindowsRoot!.OpenDirectory(preparation.Ancestor, mutable: true); operation.Parents.Add(parent);
                    // A successful actual directory flush is required before any namespace
                    // effect. A filesystem that rejects it has no admitted durability port.
                    FlushDeveloperWindowsNative(parent);
                    var path = preparation.Ancestor;
                    foreach (var leaf in preparation.Components)
                    {
                        DemandWorkspaceMetadata(preparation, operation, token); SafeFileHandle? child = null;
                        try { child = OpenDeveloperWindowsAt(parent, leaf, DeveloperWindowsMutableDirectoryAccess, 3, 1, true); }
                        catch (FileNotFoundException)
                        {
                            operation.WindowsNamespaceAttempted = true;
                            child = OpenDeveloperWindowsAt(parent, leaf, DeveloperWindowsMutableDirectoryAccess, 3, 2, true, security.Pointer); // FILE_CREATE only
                            // Custody precedes the original post-create sync, which may fail.
                            operation.Parents.Add(child); FlushDeveloperWindowsNative(parent);
                        }
                        if (!operation.Parents.Contains(child)) operation.Parents.Add(child);
                        path = Path.Combine(path, leaf); DemandDeveloperWindowsPath(child, path, true);
                        DemandDeveloperWindowsCase(child); DemandDeveloperWindowsOwner(child, preparation.WindowsSid!);
                        if (ReadDeveloperWindowsIdentity(child).Volume != preparation.WindowsAncestorIdentity!.Value.Volume)
                            throw new UnauthorizedAccessException("The original Windows metadata component crossed its physical volume.");
                        parent = child;
                    }
                    DemandWorkspaceMetadata(preparation, operation, token); FlushDeveloperWindowsNative(parent);
                    operation.WindowsNamespaceAttempted = true;
                    operation.LockHandle = OpenDeveloperWindowsAt(parent, preparation.Intent.WorkspaceId.ToString("N") + ".lock",
                        DeveloperWindowsReadControl | DeveloperWindowsAttributes | DeveloperWindowsRead | 2, 0, 3, false, security.Pointer); // actual share-none FILE_OPEN_IF lock
                    DemandWorkspaceMetadata(preparation, operation, token);
                    var stageName = ".dev-workspace-stage-" + Guid.NewGuid().ToString("N");
                    operation.StageHandle = OpenDeveloperWindowsAt(parent, stageName,
                        DeveloperWindowsReadControl | DeveloperWindowsAttributes | DeveloperWindowsRead | 2 | 0x100 | 0x10000, 0, 2, false, security.Pointer);
                    DemandDeveloperWindowsPath(operation.StageHandle, Path.Combine(preparation.DirectoryPath, stageName), false);
                    DemandDeveloperWindowsOwner(operation.StageHandle, preparation.WindowsSid!);
                    var identity = ReadDeveloperWindowsIdentity(operation.StageHandle);
                    if (!identity.IsRegular || identity.Links != 1 || identity.Volume != preparation.WindowsAncestorIdentity!.Value.Volume)
                        throw new UnauthorizedAccessException("No genuine exclusive same-volume Windows staging file exists.");
                    RetainDeveloperWindowsStageTimestamps(operation.StageHandle);
                    operation.BorrowedStageHandle = new SafeFileHandle(operation.StageHandle.DangerousGetHandle(), ownsHandle: false);
                    operation.Output = new FileStream(operation.BorrowedStageHandle, FileAccess.Write, 16 * 1024, isAsync: false);
                });
                await ObserveMetadata(preparation, operation, () => operation.Output!.WriteAsync(operation.Document.AsMemory(), token).AsTask()).ConfigureAwait(false);
                await ObserveMetadata(preparation, operation, () => operation.Output!.FlushAsync(token)).ConfigureAwait(false);
                MetadataScope(preparation, operation, () => operation.Output!.Flush(flushToDisk: true));
                await ObserveWindowsMetadataCleanup(operation, () => operation.Output!.DisposeAsync().AsTask()).ConfigureAwait(false); operation.Output = null;
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandWorkspaceMetadata(preparation, operation, token); var stage = operation.StageHandle!; var parent = operation.Parents[^1];
                    DemandDeveloperWindowsOwner(stage, preparation.WindowsSid!); var staged = ReadDeveloperWindowsIdentity(stage);
                    if (!staged.IsRegular || staged.Links != 1 || staged.Size != (ulong)operation.Document.Length)
                        throw new InvalidDataException("The SAME exclusive Windows staging handle no longer contains the complete original document.");
                    var leaf = preparation.Intent.WorkspaceId.ToString("N") + ".json";
                    RenameDeveloperWindowsCreateOnly(stage, parent, leaf); operation.WindowsPublished = true;
                    // Namespace synchronization is deliberately distinct from stage flush.
                    // Both SAME original objects must report successful native completion.
                    FlushDeveloperWindowsNative(stage); FlushDeveloperWindowsNative(parent);
                    DemandDeveloperWindowsPath(stage, Path.Combine(preparation.DirectoryPath, leaf), false);
                    var committed = ReadDeveloperWindowsIdentity(stage);
                    if (!committed.SameFile(staged) || !committed.IsRegular || committed.Links != 1 || committed.Size != (ulong)operation.Document.Length)
                        throw new IOException("The original create-only Windows publication differs from the held staged file.");
                    operation.WindowsCommittedIdentity = committed;
                    // Share-none remains in force across publication. A nonowning wrapper
                    // reads the SAME committed file, avoiding a weaker reopen/share window.
                    operation.InputHandle = new SafeFileHandle(stage.DangerousGetHandle(), ownsHandle: false);
                    operation.Input = new FileStream(operation.InputHandle, FileAccess.Read, 16 * 1024, isAsync: false); operation.Input.Position = 0;
                });
                var observed = new byte[operation.Document.Length]; var offset = 0;
                while (offset < observed.Length)
                {
                    var count = await ObserveMetadata(preparation, operation, () => operation.Input!.ReadAsync(observed.AsMemory(offset), token).AsTask()).ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException("Original Windows workspace readback is incomplete."); offset += count;
                }
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandDeveloperWindowsPath(operation.StageHandle!, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json"), false);
                    DemandDeveloperWindowsOwner(operation.StageHandle!, preparation.WindowsSid!); var actual = ReadDeveloperWindowsIdentity(operation.StageHandle!);
                    if (operation.WindowsCommittedIdentity is not { } committed || !actual.SameReadVersion(committed) || actual.Size != (ulong)observed.Length ||
                        !observed.AsSpan().SequenceEqual(operation.Document)) throw new IOException("The complete original Windows workspace readback differs from its exact file/version/bytes.");
                    observation = new(preparation, operation, Encoding.UTF8.GetString(observed), Convert.ToHexString(SHA256.HashData(observed)).ToLowerInvariant(), default, actual);
                });
            }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            finally
            {
                if (operation.Input is not null) try { await ObserveWindowsMetadataCleanup(operation, () => operation.Input.DisposeAsync().AsTask()).ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (operation.Output is not null) try { await ObserveWindowsMetadataCleanup(operation, () => operation.Output.DisposeAsync().AsTask()).ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                // Only actual fixed handles are closed. No stage/target deletion by name or
                // retry hides an uncertain native publication or directory-create effect.
                foreach (var handle in new[] { operation.InputHandle, operation.BorrowedStageHandle, operation.StageHandle, operation.LockHandle }.Concat(operation.Parents.AsEnumerable().Reverse()))
                    if (handle is not null) try { InvokeSavedRootOwnedCleanup(() => { handle.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                operation.Cleaned = errors.Count == 0;
            }
            if (errors.Count != 0 && operation.WindowsNamespaceAttempted)
                errors.Add(new DeveloperWindowsMetadataIncompleteException(operation.WindowsPublished));
            ThrowOriginalErrors(errors, operation.OriginalCanceledTask?.IsCanceled == true &&
                errors.Count != 0 && errors.All(error => ReferenceEquals(error, operation.OriginalCancellationCause)));
            if (observation is null) throw new InvalidOperationException("No private original Windows metadata readback was issued.");
            lock (_gate) { operation.Observation = observation; operation.Confirmed = true; } return observation;
        }
        private async Task ValidateWindowsCommittedWorkspaceDocument(WorkspaceMetadataPreparation preparation,
            WorkspaceMetadataObservation observation, CancellationToken token)
        {
            DeveloperWindowsRootLease? root = null; SafeFileHandle? ancestor = null, handle = null; FileStream? input = null;
            var errors = new List<Exception>(); Task? actual = null;
            var original = _executing.Value ?? throw new InvalidOperationException("No actual Windows metadata validation original exists.");
            try
            {
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(_originalWorkspaceStore?.Invoke(), preparation.Store)) throw new UnauthorizedAccessException("The actual configured workspace store changed before Windows metadata acknowledgment.");
                    root = new DeveloperWindowsRootLease(preparation.Ancestor); DemandDeveloperWindowsSid(preparation.WindowsSid!);
                    ancestor = root.OpenDirectory(preparation.Ancestor); DemandDeveloperWindowsOwner(ancestor, preparation.WindowsSid!);
                    if (preparation.WindowsAncestorIdentity is not { } identity || !ReadDeveloperWindowsIdentity(ancestor).SameFile(identity)) throw new IOException("The actual Windows metadata ancestor changed after original commit.");
                    handle = root.OpenRead(Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json")); DemandDeveloperWindowsOwner(handle, preparation.WindowsSid!);
                    if (observation.WindowsIdentity is not { } committed || !ReadDeveloperWindowsIdentity(handle).SameReadVersion(committed)) throw new IOException("The actual Windows workspace record changed after original commit.");
                    input = new FileStream(handle, FileAccess.Read, 16 * 1024, isAsync: false); return true;
                });
                var bytes = new byte[observation.Operation.Document.Length]; var offset = 0;
                while (offset < bytes.Length)
                {
                    Task<int>? read = null;
                    Invoke(() => { read = input!.ReadAsync(bytes.AsMemory(offset), token).AsTask(); lock (_gate) original.Sources.Add(read); return true; });
                    actual = read; var count = await read!.ConfigureAwait(false); if (count == 0) throw new EndOfStreamException("The actual Windows metadata acknowledgment read is incomplete."); offset += count;
                }
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); root!.DemandCurrent(); DemandDeveloperWindowsOwner(handle!, preparation.WindowsSid!);
                    DemandDeveloperWindowsPath(handle!, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json"), false);
                    if (observation.WindowsIdentity is not { } committed || !ReadDeveloperWindowsIdentity(handle!).SameReadVersion(committed) || !bytes.AsSpan().SequenceEqual(observation.Operation.Document))
                        throw new IOException("The complete current Windows metadata does not match the original exact physical file/document."); return true;
                });
            }
            catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            finally
            {
                Task? close = null;
                try { InvokeSavedRootOwnedCleanup(() => { if (input is not null) { close = input.DisposeAsync().AsTask(); lock (_gate) original.Sources.Add(close); } return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                foreach (var owned in new IDisposable?[] { handle, ancestor, root }) if (owned is not null) try { InvokeSavedRootOwnedCleanup(() => { owned.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors);
        }
    }
}
