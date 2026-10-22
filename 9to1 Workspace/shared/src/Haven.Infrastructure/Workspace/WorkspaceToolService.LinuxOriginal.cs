using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Stages a SAME anonymous inode, then commits relative to the held original parent.
    /// Participating preimage checks are explicit; no external-writer CAS/share-delete promise.
    /// After an uncertain linked-stage failure, never unlink a pathname that could be another
    /// writer's file. Keep its exact residue/identity diagnostic and refuse receipt/replay.</summary>
    private async Task WriteOriginalLinuxAsync(string root, string relativePath, string content,
        Invocation invocation, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(root, relativePath);
        SafeFileHandle? parent = null; SafeFileHandle? original = null; SafeFileHandle? beforeTarget = null; FileStream? stream = null;
        LinuxIdentity? beforeIdentity = null;
        OriginalEffect? effect = null; IAsyncDisposable? pin = null;
        IAsyncDisposable? homeEntry = null;
        Task<IAsyncDisposable?>? actualAcquire = null; Task? actualStage = null;
        var errors = new List<Exception>(); var admitted = false; var linked = false; var renamed = false;
        var stageName = ".haven-anonymous-" + Guid.NewGuid().ToString("N"); var leaf = Path.GetFileName(path);
        var digest = WorkspaceToolOriginalDigest.Text(content);
        var manualFence = invocation.Fence as IWorkspaceManualEditFinalFence;
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); invocation.DemandOriginalLinuxRoot();
            parent = invocation.OpenOriginalLinuxParent(path); // existing parent only, before staging
            effect = await invocation.PrepareWriteAsync(root, path, content, cancellationToken).ConfigureAwait(false);
            try
            {
                beforeTarget = invocation.OpenOriginalLinuxTarget(path); beforeIdentity = ReadLinuxIdentity(beforeTarget);
                if ((beforeIdentity.Value.Mode & 0xe00) != 0)
                    throw new PlatformNotSupportedException("A privileged/sticky file mode requires its explicit metadata owner; no staging occurred.");
            }
            catch (FileNotFoundException) { } // a genuine original absence may create a private 0600 file
            if (manualFence?.RequiresAbsentTarget == true && beforeTarget is not null)
                throw new UnauthorizedAccessException("The reviewed CREATE target already exists; an empty file is not absence.");

            actualAcquire = invocation.Fence.AcquireOriginalCommitPinAsync(cancellationToken).AsTask();
            pin = await actualAcquire.ConfigureAwait(false) ?? throw new UnauthorizedAccessException("The actual original Linux staging pin is unavailable.");
            invocation.DemandOriginalLinuxRoot();
            // The anonymous O_TMPFILE stage is private pre-commit work: the cheap pin permits it,
            // but it never commits the reviewed file into place. The final swap below requires the
            // real held Home entry.
            if (manualFence is not null) manualFence.DemandOriginalStage(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            else invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            original = manualFence is not null
                ? manualFence.RunOriginalStage(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested(); admitted = true;
                    return OpenLinuxAt(parent, ".", LinuxAnonymousWrite, 0x180); // 0600, O_TMPFILE
                })
                : invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested(); admitted = true;
                    return OpenLinuxAt(parent, ".", LinuxAnonymousWrite, 0x180); // 0600, O_TMPFILE
                });
            // Existing ordinary permission bits remain part of the SAME original file metadata;
            // a new file stays private 0600. Never manufacture privileged mode/ownership.
            if (beforeIdentity is { } metadata && LinuxFchmod(LinuxDescriptor(original), (uint)(metadata.Mode & 0x1ff)) != 0)
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The staged original mode could not be preserved.");
            var originalIdentity = ReadLinuxIdentity(original);
            if (!originalIdentity.IsRegular || originalIdentity.Links != 0) throw new IOException("The Linux staged inode is not the original anonymous regular file.");
            stream = new FileStream(original, FileAccess.ReadWrite, 4096, isAsync: false);
            var bytes = Encoding.UTF8.GetBytes(content);
            actualStage = stream.WriteAsync(bytes, cancellationToken).AsTask(); await actualStage.ConfigureAwait(false);
            actualStage = stream.FlushAsync(cancellationToken); await actualStage.ConfigureAwait(false);
            stream.Flush(flushToDisk: true); // stage/flush completes before the finite native swap
            await invocation.RevalidateWriteAsync(path, cancellationToken).ConfigureAwait(false);
            invocation.DemandOriginalLinuxRoot(); DemandLinuxDescriptorPath(parent, Path.GetDirectoryName(path)!);
            if (beforeTarget is not null && beforeIdentity is { } preimage)
            {
                DemandLinuxDescriptorPath(beforeTarget, path);
                if (!ReadLinuxIdentity(beforeTarget).SameReadVersion(preimage))
                    throw new IOException("The original Linux target metadata changed while staging.");
            }
            // The genuine Home edit entry is acquired freshly only immediately before the final native
            // swap — never held across asynchronous staging. If the reviewed BEFORE/target/scope or the
            // actual Home claim has diverted, this refuses with zero effects.
            if (manualFence is not null)
            {
                homeEntry = await manualFence.AcquireOriginalHomeCommitEntryAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The original Home edit entry is unavailable immediately before the native swap.");
            }
            // The finite committed swap is guarded by the SAME held Home entry: the final effect gate
            // refuses with zero effects when no real entry is held. Cheap pins never grant commit.
            invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (LinuxLinkAt(LinuxDescriptor(original), "", LinuxDescriptor(parent), stageName, 0x1000) != 0)
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original anonymous inode could not be linked.");
                linked = true;
                if (LinuxRenameAt2(LinuxDescriptor(parent), stageName, LinuxDescriptor(parent), leaf,
                    manualFence?.RequiresAbsentTarget == true ? 1U : 0U) != 0) // RENAME_NOREPLACE for CREATE only.
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original Linux target swap failed.");
                renamed = true;
                return true;
            });
            if (LinuxFsync(LinuxDescriptor(parent)) != 0)
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original Linux parent commit flush failed.");
            invocation.DemandOriginalLinuxRoot(); DemandLinuxDescriptorPath(parent, Path.GetDirectoryName(path)!);
            // The anonymous O_TMPFILE staged hold was linked+renamed into the published path; its
            // original fd still resolves to an anonymous dentry name, so no pathname assertion is
            // valid on it after commit. Verify the intended path on the freshly reopened PUBLISHED
            // leaf and require the SAME staged inode whose bytes were flushed above.
            SafeFileHandle? observed = null; var observedErrors = new List<Exception>();
            try
            {
                observed = OpenLinuxAt(parent, leaf, LinuxCloseOnExec | LinuxNonBlocking);
                DemandLinuxDescriptorPath(observed, path);
                if (!ReadLinuxIdentity(observed).SameFile(originalIdentity)) throw new IOException("The Linux committed target is not the same staged inode.");
            }
            catch (Exception error) { AddOriginalErrors(observedErrors, null, error); }
            finally { try { observed?.Dispose(); } catch (Exception error) { AddOriginalErrors(observedErrors, null, error); } }
            ThrowOriginalErrors(observedErrors);
            if (stream.Length != bytes.Length) throw new IOException("The Linux staged content length changed before acknowledgment.");
            stream.Position = 0; var retainedBytes = new byte[bytes.Length];
            actualStage = stream.ReadExactlyAsync(retainedBytes, cancellationToken).AsTask(); await actualStage.ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(retainedBytes), SHA256.HashData(bytes)))
                throw new IOException("The actual staged Linux content changed before acknowledgment.");
        }
        catch (Exception error)
        {
            AddOriginalErrors(errors, actualAcquire?.IsFaulted == true || actualAcquire?.IsCanceled == true ? actualAcquire :
                actualStage?.IsFaulted == true || actualStage?.IsCanceled == true ? actualStage : null, error);
        }
        finally
        {
            if (linked && !renamed)
                errors.Add(new IOException("Original Linux linked-stage outcome requires inspection; no pathname cleanup was attempted: " + Path.Combine(Path.GetDirectoryName(path)!, stageName)));
            Task? actualClose = null;
            try { if (stream is not null) { actualClose = stream.DisposeAsync().AsTask(); await actualClose.ConfigureAwait(false); } }
            catch (Exception error) { AddOriginalErrors(errors, actualClose, error); }
            try { original?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { beforeTarget?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { parent?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            Task? actualPinClose = null;
            try { if (pin is not null) { actualPinClose = pin.DisposeAsync().AsTask(); await actualPinClose.ConfigureAwait(false); } }
            catch (Exception error) { AddOriginalErrors(errors, actualPinClose, error); }
            Task? actualHomeClose = null;
            try { if (homeEntry is not null) { actualHomeClose = homeEntry.DisposeAsync().AsTask(); await actualHomeClose.ConfigureAwait(false); } }
            catch (Exception error) { AddOriginalErrors(errors, actualHomeClose, error); }
            if (effect is not null) invocation.FinishEffect(effect, admitted, renamed, renamed, eligible: renamed && errors.Count == 0);
        }
        ThrowOriginalErrors(errors, actualAcquire?.IsCanceled == true || actualStage?.IsCanceled == true);
    }
}
