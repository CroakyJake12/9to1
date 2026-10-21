using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>
/// Genuine manual (no-AI) source-edit owner over a resolved saved project. It composes the REAL
/// configured Files selection/capture, the genuine Home READ admission, the genuine Home WRITE
/// admission (broker-approved, private-issuer-recognized) and the kernel owner. Applying a
/// reviewed edit:
/// <list type="number">
/// <item>selects the SAME existing project (strictly beneath the configured Files root) and
/// captures it under the kernel owner;</item>
/// <item>acquires the actual Home READ admission (used to read the exact current file revision)
/// and the actual Home WRITE admission for that exact file/revision;</item>
/// <item>calls
/// <see cref="IDeveloperProjectOriginalPhysicalCaptureSource.ApplyOriginalCapturedFileEditAsync"/>,
/// which revalidates BOTH admissions, refuses unless the current captured hash equals the reviewed
/// expected revision, applies the reviewed replacement through the SHARED atomic engine under the
/// WRITE admission, then re-reads the committed inode and verifies the after hash.</item>
/// </list>
/// No process execution, restore/build, terminal or inference occurs here; a READ admission alone
/// is never treated as WRITE permission.
/// </summary>
public sealed class DeveloperManualSourceEditOwner(
    NativeFilesWorkspaceService files,
    FilesDeveloperOriginalSourceSelection selections,
    IDeveloperProjectOriginalReadAdmissionSource reads,
    IDeveloperProjectOriginalEditAdmissionSource edits,
    IDeveloperProjectOriginalPhysicalCaptureSource capture)
{
    /// <summary>Reads one exact captured source file's complete text through the genuine Home/Files/
    /// kernel owner (never raw path IO) and returns it with its current SHA-256 revision. The captured
    /// read itself refuses any stale/mutated source, so the returned hash is the exact current revision.</summary>
    public async Task<(string Text, string Sha256)> ReadAsync(DeveloperResolvedProject project, string relativePath, CancellationToken token)
    {
        DemandSafeProjectRelativePath(relativePath);
        await using var lease = await AcquireReadAsync(project, token).ConfigureAwait(false);
        var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.ReadAdmission, relativePath, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The requested source file is not part of the genuine saved-project capture.", relativePath);
        return (text, Sha256(text));
    }

    /// <summary>Applies one exactly reviewed source replacement. It selects/captures the SAME project,
    /// acquires genuine READ + WRITE admissions over the exact file/revision, and routes the reviewed
    /// mutation through the kernel's shared atomic engine. It returns the post-write SHA-256 after the
    /// kernel re-reads the committed inode and verifies it. READ approval never substitutes for the
    /// required WRITE approval.</summary>
    public async Task<string> ApplyAsync(DeveloperResolvedProject project, string relativePath,
        string expectedSha256, string newText, CancellationToken token)
    {
        DemandSafeProjectRelativePath(relativePath);
        if (!IsHash(expectedSha256)) throw new ArgumentException("The exact reviewed current SHA-256 revision is required.", nameof(expectedSha256));
        var configuration = await files.GetConfigurationAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
        var root = project.Root.Location ?? throw new InvalidOperationException("The resolved project has no local root location.");
        var selection = await selections.SelectOriginalExistingProjectAsync(configuration.StoreId, root, token).ConfigureAwait(false);
        var readAdmission = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
        try
        {
            var captured = await selections.CaptureOriginalAsync(selection, readAdmission, token).ConfigureAwait(false);
            var edit = await edits.AcquireOriginalEditAsync(selection, captured, relativePath, expectedSha256, newText, token).ConfigureAwait(false);
            try
            {
                return await capture.ApplyOriginalCapturedFileEditAsync(captured, readAdmission, edit,
                    relativePath, expectedSha256, newText, token).ConfigureAwait(false);
            }
            finally { await edit.DisposeAsync().ConfigureAwait(false); }
        }
        finally { await readAdmission.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task<ReadLease> AcquireReadAsync(DeveloperResolvedProject project, CancellationToken token)
    {
        var configuration = await files.GetConfigurationAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
        var root = project.Root.Location ?? throw new InvalidOperationException("The resolved project has no local root location.");
        var selection = await selections.SelectOriginalExistingProjectAsync(configuration.StoreId, root, token).ConfigureAwait(false);
        var readAdmission = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
        try
        {
            var captureValue = await selections.CaptureOriginalAsync(selection, readAdmission, token).ConfigureAwait(false);
            return new(selection, readAdmission, captureValue);
        }
        catch (Exception error)
        {
            try { await readAdmission.DisposeAsync().ConfigureAwait(false); }
            catch (Exception disposeError) { throw new AggregateException("Manual source read acquisition failed and its genuine READ admission did not close cleanly.", error, disposeError); }
            throw;
        }
    }

    private sealed record ReadLease(IDeveloperProjectOriginalReadSelection Selection,
        IDeveloperProjectOriginalReadAdmission ReadAdmission, IDeveloperProjectOriginalExistingSourceCapture Capture)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ReadAdmission.DisposeAsync();
    }

    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void DemandSafeProjectRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Length > 2048 || path.IndexOf('\0') >= 0 ||
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
            throw new ArgumentException("A safe project-relative source path is required.", nameof(path));
    }
}
