using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Bounded Windows owner port over the SAME atomic-write operation. The original
    /// staged file stays exclusively held through asynchronous stage, final handle rename and
    /// cleanup. Path names/hashes alone never authorise deletion or identify the staged file.
    /// This is not a target-version CAS against arbitrary external filesystem writers.</summary>
    private async Task WriteOriginalWindowsAsync(string root, string relativePath, string content,
        Invocation invocation, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(root, relativePath);
        var effect = await invocation.PrepareWriteAsync(root, path, content, cancellationToken).ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The original mutation requires the retained Windows file-handle rename/disposition owner.");
        var digest = WorkspaceToolOriginalDigest.Text(content);
        var temporary = path + ".haven.tmp." + Guid.NewGuid().ToString("N");
        var errors = new List<Exception>();
        Task<IAsyncDisposable?>? originalAcquire = null;
        Task? originalStage = null;
        IAsyncDisposable? originalPin = null;
        SafeFileHandle? originalHandle = null;
        FileStream? originalStream = null;
        var admitted = false;
        var renamed = false;
        try
        {
            originalAcquire = invocation.Fence.AcquireOriginalCommitPinAsync(cancellationToken).AsTask();
            originalPin = await originalAcquire.ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual original task staging pin is unavailable.");
            cancellationToken.ThrowIfCancellationRequested();
            invocation.DemandIssued();
            invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            invocation.EnsureOriginalWriteParent(path, directory =>
                invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    admitted = true;
                    Directory.CreateDirectory(directory);
                    return true;
                }));
            originalHandle = invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                admitted = true;
                return CreateOriginalStagedHandle(temporary);
            });
            originalStream = new FileStream(originalHandle, FileAccess.Write, 4096, isAsync: true);
            originalStage = originalStream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken).AsTask();
            await originalStage.ConfigureAwait(false);
            originalStage = originalStream.FlushAsync(cancellationToken);
            await originalStage.ConfigureAwait(false);
            // Fresh call/issuer/preimage checks after stage; neither the pin nor earlier policy
            // permission overrides revocation. No I/O/await/observer runs in the policy gate.
            await invocation.RevalidateWriteAsync(path, cancellationToken).ConfigureAwait(false);
            invocation.DemandIssued();
            invocation.Fence.DemandOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest);
            invocation.Fence.RunOriginalEffect(invocation.Root, WorkspaceToolEffectKind.AtomicWrite, path, digest, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RenameOriginalStagedHandle(originalHandle, path);
                renamed = true;
                return true;
            });
        }
        catch (Exception error)
        {
            AddOriginalErrors(errors, originalAcquire?.IsCompleted == true && !originalAcquire.IsCompletedSuccessfully ? originalAcquire :
                originalStage?.IsCompleted == true && !originalStage.IsCompletedSuccessfully ? originalStage : null, error);
        }
        finally
        {
            // Disposition applies to the SAME held handle, never a path that another writer
            // could replace. A created parent or failed stage remains partial/unknown evidence.
            if (!renamed && originalHandle is { IsInvalid: false, IsClosed: false })
                try { DeleteOriginalStagedHandleOnClose(originalHandle); }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
            Task? originalClose = null;
            try
            {
                if (originalStream is not null)
                {
                    originalClose = originalStream.DisposeAsync().AsTask();
                    await originalClose.ConfigureAwait(false);
                }
                else originalHandle?.Dispose();
            }
            catch (Exception error) { AddOriginalErrors(errors, originalClose, error); }
            try { originalHandle?.Dispose(); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            Task? originalPinClose = null;
            try
            {
                if (originalPin is not null)
                {
                    originalPinClose = originalPin.DisposeAsync().AsTask();
                    await originalPinClose.ConfigureAwait(false);
                }
            }
            catch (Exception error) { AddOriginalErrors(errors, originalPinClose, error); }
            invocation.FinishEffect(effect, admitted, renamed, renamed, eligible: renamed && errors.Count == 0);
        }
        ThrowOriginalErrors(errors, originalAcquire?.IsCanceled == true || originalStage?.IsCanceled == true);
    }

    private static SafeFileHandle CreateOriginalStagedHandle(string temporary)
    {
        // CREATE_NEW + share-none + DELETE access: another opener cannot swap/remove this
        // original file while its owner stages, renames or sets disposition on the handle.
        var handle = CreateFileW(temporary, 0x40000000U | 0x00010000U, 0, IntPtr.Zero, 1, 0x40000000U | 0x80U, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw new Win32Exception(error, "The original staged file could not be created.");
    }

    private static void RenameOriginalStagedHandle(SafeFileHandle original, string canonicalTarget)
    {
        var name = Encoding.Unicode.GetBytes(canonicalTarget);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(uint);
        var payload = new byte[nameOffset + name.Length];
        payload[0] = 1; // FILE_RENAME_INFO.ReplaceIfExists; original target replacement behaviour.
        BitConverter.GetBytes((uint)name.Length).CopyTo(payload, lengthOffset);
        name.CopyTo(payload, nameOffset);
        if (!SetFileInformationByHandle(original, 3, payload, (uint)payload.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original staged file could not be renamed by its retained handle.");
    }

    private static void DeleteOriginalStagedHandleOnClose(SafeFileHandle original)
    {
        if (!SetFileInformationByHandle(original, 4, [1], 1)) // FILE_DISPOSITION_INFO.DeleteFile.
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original staged file disposition could not be set.");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
        [In] byte[] information, uint bufferSize);
}
