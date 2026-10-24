using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace AvaloniaHome;

internal sealed partial class HomeApp
{
    private AuthenticatedResourceActor? _originalLibraryActor;
    private FilesNativeBrowserService? _originalLibraryFilesBrowser;
    private FilesCompatibilityPackageOpenCoordinator? _originalLibraryFilesPackages;
    private readonly object _originalLibraryFilesSync = new();
    private readonly List<OriginalLibraryFilesWindow> _originalLibraryFilesWindows = [];
    private volatile bool _originalLibraryFilesRetiring;
    private Task? _originalLibraryFilesClose;

    // Native adapter composition only. All canonical state/profile/ownership/resolution owners
    // come from this SAME original Home. No new store, setup, import or permission is created.
    private bool BindOriginalLibraryFiles()
    {
        var home = _nativeComposition;
        if (home.Services.GetService(typeof(NativeFilesWorkspaceService)) is not NativeFilesWorkspaceService files ||
            home.Services.GetService(typeof(NativeFilesWorkspaceAuthority)) is not NativeFilesWorkspaceAuthority authority ||
            home.Services.GetService(typeof(FilesArtifactResourceResolver)) is not FilesArtifactResourceResolver resolver)
            return false;
        if (!files.IsBoundToOriginalLocalComposition(home.Profiles, home.Ownership) ||
            !authority.IsBoundToOriginalComposition(files, home.Profiles, home.Ownership) ||
            !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
            throw new UnauthorizedAccessException("The standalone Files Library needs its original Home owner tuple.");
        var content = home.Services.GetService(typeof(ICompatibilityPackageContentSource)) as ICompatibilityPackageContentSource
            ?? new FilesCompatibilityPackageContentSource(authority, home.Profiles, home.Resources);
        var browser = home.Services.GetService(typeof(FilesNativeBrowserService)) as FilesNativeBrowserService
            ?? new FilesNativeBrowserService(authority, home.Profiles, home.Resources, content,
                new FilesOriginalChildFolderReadSource(resolver, home.Resources), home.Broker);
        if (!browser.IsBoundToOriginalComposition(authority, home.Profiles, home.Resources, content))
            throw new UnauthorizedAccessException("The original standalone Files browser belongs to a different composition.");
        _originalLibraryFilesBrowser = browser;
        // Inspection exists ONLY when the actual host registered its real handler.
        var handler = home.Services.GetService(typeof(ICompatibilityPackageOpenHandler)) as ICompatibilityPackageOpenHandler;
        _originalLibraryFilesPackages = handler is null ? null : new(content, home.Profiles, handler);
        return true;
    }

    private bool OriginalLibraryHomeIsAlive() => !_libraryRetiring && !_originalLibraryFilesRetiring &&
        _originalLifetime.OriginalShutdown is null && _nativeComposition.OriginalCloseTask is null &&
        _nativeComposition.OriginalProcessRetirementRequestTask is null;

    private bool OriginalLibraryRequiredServicesCurrent(HomeCoreStateSnapshot snapshot) =>
        OriginalLibraryHomeIsAlive() && _nativeComposition.OriginalStartTask is { IsCompletedSuccessfully: true } &&
        new[] { "home.core", "home.state", "permissions.trust" }.All(id =>
            snapshot.Services.Any(service => service.ServiceId == id &&
                new HomeServiceRequirement(id, 1).Accepts(service.ContractVersion) &&
                service.IsAvailable && service.State == HomeServiceLifecycleState.Ready));

    private async Task<bool> ObserveOriginalLibraryOwnerAsync(CancellationToken token)
    {
        var original = new CloudflareOriginalTaskLedger(); original.BindOriginalOwner(this);
        return await original.RunToOriginalSettlementAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var before = _nativeComposition.Runtime.Current;
            if (_originalLibraryActor is not { } actor || !OriginalLibraryRequiredServicesCurrent(before)) return false;
            var read = await original.AwaitAsync(original.Invoke(() => _nativeComposition.StateStore.ReadAsync(token)))
                .ConfigureAwait(false);
            if (!read.IsSuccess || read.State is null)
                throw new InvalidDataException("The original Home Library state needs recovery; it was preserved.");
            void Own(Action body) => original.Invoke(() => { body(); return true; });
            void Retain(Task task) => original.Track(task);
            var current = await original.AwaitAsync(original.Invoke(() => _nativeComposition.Profiles.CheckAsync(
                read.State, actor, HomeStateCommitPhase.Publication, Own, Retain, token).AsTask())).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var after = _nativeComposition.Runtime.Current;
            return current && after.Revision == before.Revision && OriginalLibraryRequiredServicesCurrent(after);
        }).ConfigureAwait(false);
    }

    private sealed class StandaloneLibraryAppRouter(HomeApp owner, IHomeDeepLinkRouter? borrowed) : IHomeDeepLinkRouter
    {
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken token = default)
        {
            if (link.OwnerApp == "files" && link.TargetRouteId == "files.folder" && link.Action == "open")
                return owner.OpenOriginalLibraryFilesFolderAsync(link, token);
            return borrowed?.OpenAsync(link, token) ?? Task.FromResult(new HomeFeatureNavigationResult(false,
                "OwningAppUnavailable", "This standalone host has no real connection to the selected owning app.",
                new(link.TargetRouteId!, link.OwnerApp, link.ObjectId, link.Action)));
        }
    }

    private async Task<HomeFeatureNavigationResult> OpenOriginalLibraryFilesFolderAsync(HomeDeepLink link, CancellationToken token)
    {
        var ids = link.ObjectId.Split('/');
        if (ids.Length != 2 || !Guid.TryParseExact(ids[0], "D", out var store) || store == Guid.Empty ||
            !Guid.TryParseExact(ids[1], "D", out var folder) || folder == Guid.Empty ||
            _originalLibraryFilesBrowser is null || _originalLibraryActor is null)
            throw new UnauthorizedAccessException("Retain the current original Files Library folder target.");
        var original = new CloudflareOriginalTaskLedger(); original.BindOriginalOwner(this);
        OriginalLibraryFilesWindow? acquired = null;
        try
        {
            return await original.RunToOriginalSettlementAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (!await original.AwaitAsync(original.Invoke(() => ObserveOriginalLibraryOwnerAsync(token))).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Home profile cannot open this Files folder.");
                // Shared Library has fresh source Get/CanRead/CanOpen. The destination independently
                // lists/revalidates its own original Files page; no stale index grants access.
                var dispatch = original.Invoke(() => Dispatcher.UIThread.InvokeAsync<OriginalLibraryFilesWindow>(() =>
                    acquired = BeginOriginalLibraryFilesWindow(store, folder, token)).GetTask());
                var window = await original.AwaitAsync(dispatch).ConfigureAwait(false);
                await original.AwaitAsync(window.Open!).ConfigureAwait(false);
                if (!await original.AwaitAsync(original.Invoke(() => ObserveOriginalLibraryOwnerAsync(token))).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Home profile changed during Files opening.");
                var finalPublication = original.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    if (!window.IsAlive || window.View is null)
                        throw new UnauthorizedAccessException("The original Files window retired during opening.");
                    window.View.CheckOriginalPublicationAlive();
                    return window.View.RevalidateOriginalOwnerAsync(token);
                }).GetTask());
                var finalOwner = await original.AwaitAsync(finalPublication).ConfigureAwait(false);
                await original.AwaitAsync(finalOwner).ConfigureAwait(false);
                return new HomeFeatureNavigationResult(true, "Opened", "Opened the original folder in Files.",
                    new(link.TargetRouteId!, link.OwnerApp, link.ObjectId, link.Action));
            }).ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            // A failed final publication/current-profile check must not leave an exposed window.
            // Custody is captured INSIDE the UI callback, before its return can itself fail.
            if (acquired is not null)
            {
                try
                {
                    BeginOriginalLibraryFilesClose(acquired);
                    await acquired.Close!.ConfigureAwait(false);
                }
                catch (Exception cleanup) { throw new AggregateException("Original Files opening and retirement failed.", primary, cleanup); }
            }
            ExceptionDispatchInfo.Capture(primary).Throw(); throw;
        }
    }

    private OriginalLibraryFilesWindow BeginOriginalLibraryFilesWindow(Guid store, Guid folder, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess(); caller.ThrowIfCancellationRequested();
        if (!OriginalLibraryHomeIsAlive()) throw new ObjectDisposedException("The original Home Library is closing.");
        var record = new OriginalLibraryFilesWindow(this, store, folder);
        lock (_originalLibraryFilesSync)
        {
            _originalLibraryFilesWindows.RemoveAll(item => item.Close is { IsCompletedSuccessfully: true });
            if (_originalLibraryFilesWindows.Count >= 64)
                throw new InvalidOperationException("Close existing Files windows before opening another.");
            _originalLibraryFilesWindows.Add(record); // Custody before any native callback.
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        record.Open = OpenWindowAsync(start.Task);
        start.SetResult();
        return record;
        async Task OpenWindowAsync(Task admission)
        {
            await admission;
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, record.Lifetime.Token);
                if (!record.IsAlive) throw new ObjectDisposedException("The original Files window is closing.");
                record.View = originals.Invoke(() => new FilesNativeBrowserSurface(_originalLibraryFilesBrowser!,
                    _originalLibraryFilesPackages, _originalLibraryActor!, new OriginalLibraryFilesReadiness(record), record.Lifetime.Token));
                await originals.AwaitAsync(originals.Invoke(() => record.View.InitializeAsync(linked.Token)));
                await originals.AwaitAsync(originals.Invoke(() => record.View.OpenOriginalLocationAsync(store, folder, linked.Token)));
                await originals.AwaitAsync(originals.Invoke(() => record.View.RevalidateOriginalOwnerAsync(linked.Token)));
                if (!await originals.AwaitAsync(originals.Invoke(() => ObserveOriginalLibraryOwnerAsync(linked.Token))))
                    throw new UnauthorizedAccessException("The original Home profile changed before Files publication.");
                originals.Invoke(() =>
                {
                    if (!record.IsAlive) throw new ObjectDisposedException("The original Files window is closing.");
                    record.View.CheckOriginalPublicationAlive();
                    if (!record.View.TryGetOriginalLocation(out var actualStore, out var actualFolder, out var title) ||
                        actualStore != store || actualFolder != folder)
                        throw new UnauthorizedAccessException("The original Files destination changed before publication.");
                    record.Window = new Window { Title = "Files — " + title, Width = 1100, Height = 760, Content = record.View };
                    record.Window.Closing += (_, args) =>
                    {
                        if (record.NativeClosed) return;
                        args.Cancel = true;
                        if (record.Close is not null || record.View.CanPrepareOriginalClose()) BeginOriginalLibraryFilesClose(record);
                    };
                    record.Window.Show();
                    if (!record.IsAlive) throw new ObjectDisposedException("The original Files window retired while being shown.");
                    record.View.CheckOriginalPublicationAlive();
                    return true;
                });
            });
        }
    }

    // Test observation of real windows only; it grants no access and creates no UI.
    internal IReadOnlyList<Window> OriginalLibraryFilesWindows
    {
        get { lock (_originalLibraryFilesSync) return _originalLibraryFilesWindows.Where(item => item.Window is not null)
            .Select(item => item.Window!).ToArray(); }
    }

    private sealed class OriginalLibraryFilesWindow(HomeApp owner, Guid store, Guid folder)
    {
        internal readonly HomeApp Owner = owner;
        internal readonly Guid Store = store, Folder = folder;
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly List<Task> Checks = [];
        internal FilesNativeBrowserSurface? View;
        internal Window? Window;
        internal Task? Open, Close;
        internal volatile bool Closing, NativeClosed;
        internal bool IsAlive => !Closing && !Lifetime.IsCancellationRequested && Owner.OriginalLibraryHomeIsAlive();
    }
    private sealed class OriginalLibraryFilesReadiness(OriginalLibraryFilesWindow window) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var owner = window.Owner;
            lock (owner._originalLibraryFilesSync)
            {
                token.ThrowIfCancellationRequested();
                if (!window.IsAlive) return new(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                    "HomeClosing", "The original Home/Files window is closing."));
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var actual = CheckCoreAsync(start.Task);
                window.Checks.RemoveAll(item => item.IsCompletedSuccessfully);
                window.Checks.Add(actual); start.SetResult(); return new(actual);
            }
            async Task<CuiSceneAvailability> CheckCoreAsync(Task admission)
            {
                await admission.ConfigureAwait(false);
                var current = await owner.ObserveOriginalLibraryOwnerAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return current && window.IsAlive
                    ? new(CuiSceneAvailabilityState.Ready, "Ready", "The original Home services and full profile are current.")
                    : new(CuiSceneAvailabilityState.Unavailable, "HomeChanged", "The original Home services or profile changed.");
            }
        }
    }

    private void PrepareOriginalLibraryFilesRetirement()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_originalLibraryFilesSync)
        {
            if (_originalLibraryFilesClose is not null) return;
            _originalLibraryFilesRetiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalLibraryFilesClose = CloseAllAsync(start.Task);
            start.SetResult();
        }
        async Task CloseAllAsync(Task admission)
        {
            await admission.ConfigureAwait(false);
            OriginalLibraryFilesWindow[] records;
            lock (_originalLibraryFilesSync) records = _originalLibraryFilesWindows.ToArray();
            var failures = new List<Exception>();
            foreach (var record in records)
            {
                try { BeginOriginalLibraryFilesClose(record); }
                catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, cause); }
            }
            foreach (var record in records)
                if (record.Close is { } close)
                    try { await close.ConfigureAwait(false); }
                    catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, close.Exception ?? cause); }
            ThrowOriginalLibraryFilesFailures(failures);
        }
    }
    private Task CloseOriginalLibraryFilesAsync()
    { PrepareOriginalLibraryFilesRetirement(); return _originalLibraryFilesClose!; }

    private void BeginOriginalLibraryFilesClose(OriginalLibraryFilesWindow record)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_originalLibraryFilesSync)
        {
            if (record.Close is not null) return;
            record.Closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            record.Close = CloseWindowAsync(start.Task);
            start.SetResult();
        }
        async Task CloseWindowAsync(Task admission)
        {
            await admission.ConfigureAwait(false);
            var failures = new List<Exception>();
            try { record.Lifetime.Cancel(); } catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, cause); }
            if (record.Open is { } open)
                try { await open.ConfigureAwait(false); }
                catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, open.Exception ?? cause); }
            if (record.View is { } view)
                try { await view.CloseAndDrainAsync().ConfigureAwait(false); }
                catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, cause); }
            Task[] checks; lock (_originalLibraryFilesSync) checks = record.Checks.ToArray();
            foreach (var check in checks)
                try { await check.ConfigureAwait(false); }
                catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, check.Exception ?? cause); }
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    record.NativeClosed = true;
                    if (record.Window is { } window) { window.Content = null; window.Close(); }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, cause); }
            try { record.Lifetime.Dispose(); } catch (Exception cause) { AddOriginalLibraryFilesFailure(failures, cause); }
            ThrowOriginalLibraryFilesFailures(failures);
        }
    }
    private static void AddOriginalLibraryFilesFailure(List<Exception> failures, Exception cause)
    { if (!failures.Any(original => ReferenceEquals(original, cause))) failures.Add(cause); }
    private static void ThrowOriginalLibraryFilesFailures(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original standalone Files work/window retirement failed.", failures);
    }
}
