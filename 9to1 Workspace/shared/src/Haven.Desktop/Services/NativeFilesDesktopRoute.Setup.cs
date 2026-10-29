using Avalonia.Threading;
using Haven.Application;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class NativeFilesDesktopRoute
{
    // Setup is owned by the real in-process Home composition. Installed IPC routes
    // continue to require their existing authenticated domain operation.
    internal bool SupportsOriginalWorkspaceSetup => _sameProcessHome is not null;
    internal bool IsOriginalRouteAlive
    {
        get
        {
            lock (_sync) return !_closing && !_originalConnectionLifetime.IsCancellationRequested
                && !_windowLifetime.IsCancellationRequested;
        }
    }

    internal Task<NativeFilesWorkspaceConfiguration?> ReadOriginalSetupConfigurationAsync(CancellationToken token) =>
        RunOriginalSetupAsync(async cancellation =>
            await OriginalSetupFiles().GetConfigurationAsync(cancellation), token);

    internal Task<FilesWorkspaceRootObservation> ObserveOriginalWorkspaceRootAsync(CancellationToken token) =>
        RunOriginalSetupAsync(async cancellation =>
            await OriginalSetupFiles().ObserveWorkspaceRootAsync(cancellation), token);

    internal Task<NativeFilesWorkspace> ReplaceOriginalMissingWorkspaceRootAsync(CancellationToken token) =>
        RunOriginalSetupAsync(async cancellation =>
        {
            var home = _sameProcessHome ?? throw new InvalidOperationException("The owning Home is unavailable.");
            return await OriginalSetupFiles().ReplaceMissingWorkspaceRootAsync(home.LocalStoreOwnership, cancellation);
        }, token);

    internal Task<NativeFilesWorkspace> RelocateOriginalWorkspaceRootAsync(string relocatedRoot, CancellationToken token) =>
        RunOriginalSetupAsync(async cancellation =>
        {
            var home = _sameProcessHome ?? throw new InvalidOperationException("The owning Home is unavailable.");
            return await OriginalSetupFiles().ReLocateWorkspaceRootAsync(relocatedRoot, home.LocalStoreOwnership, cancellation);
        }, token);

    internal Task<NativeFilesWorkspaceConfiguration> ConfigureOriginalWorkspaceAsync(string selectedEmptyDirectory,
        CancellationToken token) => RunOriginalSetupAsync(async cancellation =>
        {
            var home = _sameProcessHome ?? throw new InvalidOperationException("The owning Home is unavailable.");
            var workspace = await OriginalSetupFiles().ConfigureNewAsync(selectedEmptyDirectory,
                home.LocalStoreOwnership, cancellation);
            return workspace.Configuration;
        }, token);

    private NativeFilesWorkspaceService OriginalSetupFiles()
    {
        var home = _sameProcessHome ?? throw new NotSupportedException("Files setup needs its owning Home process.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_originalProvider, home);
        var files = _originalProvider.GetRequiredService<NativeFilesWorkspaceService>();
        if (!ReferenceEquals(home.Services.GetService(typeof(NativeFilesWorkspaceService)), files))
            throw new UnauthorizedAccessException("Retain the original Files workspace service.");
        return files;
    }

    private Task<T> RunOriginalSetupAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = RunOriginalSetupCoreAsync(start.Task, body, token);
            _originalPublications.RemoveWhere(row => row.IsCompletedSuccessfully);
            _originalPublications.Add(actual); // Before any Home/store callback or I/O.
            start.SetResult();
            return actual;
        }
    }

    private async Task<T> RunOriginalSetupCoreAsync<T>(Task start, Func<CancellationToken, Task<T>> body,
        CancellationToken caller)
    {
        await start;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
        var token = linked.Token;
        try
        {
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            if (_originalActor is null || await ReadOriginalRouteActorAsync(token) != _originalActor)
                throw new UnauthorizedAccessException("The original Files setup profile changed.");
            await RequireReadyAsync(token);
            var result = await body(token);
            if (await ReadOriginalRouteActorAsync(token) != _originalActor)
                throw new UnauthorizedAccessException("The original Files setup profile changed.");
            await RequireReadyAsync(token);
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error)
        {
            // Preserve actual failures for external retirement; never erase a
            // partly-created workspace or claim that setup recovered it.
            lock (_sync) Add(_publicationFailures, error);
            throw;
        }
    }
}
