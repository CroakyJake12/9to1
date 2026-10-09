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
        Task<IAsyncDisposable?>? actualAcquire = null; Task? actualStage = null;
        var errors = new List<Exception>(); var admitted = false; var linked = false; var renamed = false;
        var stageName = ".haven-anonymous-" + Guid.NewGuid().ToString("N"); var leaf = Path.GetFileName(path);
        var digest = WorkspaceToolOriginalDigest.Text(content);
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

            actualAcquire = invocation.Fence.AcquireOriginalCommitPinAsync(cancellationToken).AsTask();
            pin = await actualAcquire.ConfigureAwait(false) ?? throw new UnauthorizedAccessException("The actual original Linux staging pin is unavailable.");
            invocation.DemandOriginalLinuxRoot(); invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            original = invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
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
            invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (LinuxLinkAt(LinuxDescriptor(original), "", LinuxDescriptor(parent), stageName, 0x1000) != 0)
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original anonymous inode could not be linked.");
                linked = true;
                if (LinuxRenameAt2(LinuxDescriptor(parent), stageName, LinuxDescriptor(parent), leaf, 0) != 0)
                    throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original Linux target swap failed.");
                renamed = true;
                return true;
            });
            if (LinuxFsync(LinuxDescriptor(parent)) != 0)
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError(), "The original Linux parent commit flush failed.");
            invocation.DemandOriginalLinuxRoot(); DemandLinuxDescriptorPath(parent, Path.GetDirectoryName(path)!);
            DemandLinuxDescriptorPath(original, path);
            SafeFileHandle? observed = null; var observedErrors = new List<Exception>();
            try
            {
                observed = OpenLinuxAt(parent, leaf, LinuxCloseOnExec | LinuxNonBlocking);
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
            if (effect is not null) invocation.FinishEffect(effect, admitted, renamed, renamed, eligible: renamed && errors.Count == 0);
        }
        ThrowOriginalErrors(errors, actualAcquire?.IsCanceled == true || actualStage?.IsCanceled == true);
    }
}
