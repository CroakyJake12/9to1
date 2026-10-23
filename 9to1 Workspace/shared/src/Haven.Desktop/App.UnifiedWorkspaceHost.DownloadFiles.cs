#if !ANDROID
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost : IFilesOriginalBrowserDownloadNavigator
    {
        private FilesNativeBrowserService.BrowserDownloadFilesOwner? _originalDownloadFiles;
        private FilesNativeBrowserService? _originalDownloadBrowser;
        private Task? _originalDownloadFilesClose;
        private readonly HashSet<Task> _originalDownloadNavigations = new(ReferenceEqualityComparer.Instance);

        // One Browser-owned presentation borrows the existing Files/Home tuple.
        // No Downloads path is adopted as a Files folder, and no provider, actor,
        // queue, approval workflow or original Files surface is manufactured here.
        private IBrowserOriginalDownloadFilesService? CaptureOriginalDownloadFilesService()
        {
            Dispatcher.UIThread.VerifyAccess(); DemandCurrent();
            if (!OperatingSystem.IsLinux()) return null;
            if (_originalDownloadFiles is not null)
                throw new InvalidOperationException("The original Browser Files owner was already captured.");
            _ = _services.RequireOriginalWindowsHomeComponents(_home);
            var physical = _services.GetRequiredService<IBrowserOriginalNativeDownloadPhysicalOwner>();
            var destinations = _services.GetRequiredService<IBrowserOriginalDownloadFilesPhysicalOwner>();
            var transport = _services.GetRequiredService<BrowserDownloadTransport>();
            var browser = _services.GetRequiredService<FilesNativeBrowserService>();
            var reads = _services.GetRequiredService<FilesArtifactResourceResolver>();
            var workspaces = _services.GetRequiredService<NativeFilesWorkspaceService>();
            var authority = _services.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var packages = _services.GetRequiredService<Haven.Application.Compatibility.ICompatibilityPackageContentSource>();
            if (physical is not Haven.Infrastructure.Native.Linux.BrowserOriginalNativeDownloadPhysicalOwner ||
                !ReferenceEquals(physical, destinations) || !ReferenceEquals(transport.OriginalPhysicalOwner, physical) ||
                !ReferenceEquals(physical.OriginalTransportSource, transport) ||
                !ReferenceEquals(_home.Services.GetService(typeof(FilesArtifactResourceResolver)), reads) ||
                !workspaces.IsBoundToOriginalLocalComposition(_home.Profiles, _home.Ownership) ||
                !authority.IsBoundToOriginalComposition(workspaces, _home.Profiles, _home.Ownership) ||
                !browser.HasNativeMutationOwner ||
                !browser.IsBoundToOriginalComposition(authority, _home.Profiles, _home.Resources, packages))
                throw new UnauthorizedAccessException("Retain the same genuine Linux physical and Files/Home owners.");
            var presenter = _services.GetRequiredService<IFilesOriginalBrowserDownloadReviewPresenter>();
            // Retain the actual acquisition before later workspace/scene callbacks.
            // The physical owner is process-owned and is disposed by the existing
            // provider only AFTER every original host and Files borrower drains.
            _originalDownloadBrowser = browser;
            _originalDownloadFiles = browser.CreateOriginalBrowserDownloadFilesOwner(
                physical, destinations, reads, this, presenter);
            return _originalDownloadFiles;
        }

        Task IFilesOriginalBrowserDownloadNavigator.RevealOriginalRegisteredDownloadWithinSourceAsync(
            FilesNativeBrowserService sameBrowser, FilesNativeBrowserPage originalPage,
            HostedItemMetadata originalRow, AuthenticatedResourceActor originalActor,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(originalPage); ArgumentNullException.ThrowIfNull(originalRow);
            ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(retain);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task actual;
            lock (_gate)
            {
                DemandCurrent(); token.ThrowIfCancellationRequested();
                _originalDownloadNavigations.RemoveWhere(task => task.IsCompletedSuccessfully);
                if (_originalDownloadNavigations.Count >= 64)
                    throw new InvalidOperationException("Settle the original Files reveals before opening more.");
                actual = RevealOriginalDownloadCoreAsync(start.Task, sameBrowser, originalPage,
                    originalRow, originalActor, scope, retain, token);
                _originalDownloadNavigations.Add(actual); _sources.Add(actual);
            }
            start.SetResult(); return actual;
        }

        private async Task RevealOriginalDownloadCoreAsync(Task start, FilesNativeBrowserService sameBrowser,
            FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
            AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
            CancellationToken caller)
        {
            await start.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var homePhase = CloudflareOriginalExecutionGuard.EnterOriginal(_home);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, _appLifetime, _windowLifetime);
            var token = lifetime.Token;
            var originals = new CloudflareOriginalTaskLedger();
            originals.BindOriginalOwner(this); originals.BindOriginalCallerCallback(scope);
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                Demand();
                await ReadActorAsync().ConfigureAwait(false);
                // Validate the privately issued page BEFORE using even its metadata
                // to form a route. Public store/folder IDs confer no read authority.
                var current = await originals.AwaitAsync(StartRead(() =>
                    sameBrowser.CaptureOriginalBrowserDownloadPageReadCheckWithinSourceAsync(
                        originalPage, originalActor, () => IsCurrent, Own, Retain, token))).ConfigureAwait(false);
                await RequireCurrentPageAsync().ConfigureAwait(false);
                var dispatch = StartRead(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    // Capture before an external scope/retainer can refuse publication.
                    return StartTask(RevealOnUiAsync);
                }).GetTask());
                var nested = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
                await originals.AwaitAsync(nested).ConfigureAwait(false);
                await RequireCurrentPageAsync().ConfigureAwait(false);
                await ReadActorAsync().ConfigureAwait(false);
                Demand();

                async Task RequireCurrentPageAsync()
                {
                    if (!await originals.AwaitAsync(StartRead(() => current(token).AsTask())).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The original registered Files page changed.");
                }
                async Task RevealOnUiAsync()
                {
                    Dispatcher.UIThread.VerifyAccess(); Demand();
                    await ReadActorAsync();
                    var chrome = _chrome ?? throw new InvalidOperationException("The original Browse owner is absent.");
                    var source = chrome.State.SelectedTab;
                    if (source.Privacy != Haven.Core.BrowserTabPrivacy.Standard || source.ProfileId != originalActor.ProfileId)
                        throw new UnauthorizedAccessException("Open registered Files from the same ordinary Home profile.");
                    var address = FilesBrowseAppContentAddress.For(originalPage.StoreID, originalPage.ParentID);
                    // The normal registry creates the SAME original Files app/session.
                    // New-tab navigation preserves the originating browser page.
                    var opened = await originals.AwaitAsync(StartRead(() => chrome.OpenContentAsync(address,
                        openInNewTab: true, sourceTabId: source.Id, cancellationToken: token)));
                    Demand(); await ReadActorAsync();
                    var tabId = opened.SelectedTabId;
                    if (chrome.ObserveOriginalAppContent(tabId) is not IBrowseNativeAppContentSession session ||
                        !session.IsPublicationCurrent || session.Address != address ||
                        session.OriginalNativeView is not FilesNativeBrowserSurface surface)
                        throw new InvalidOperationException("The genuine configured Files tab did not publish its original view.");
                    // This real surface retains its own original reveal and current
                    // route readiness. Its registry/session remains its sole close owner.
                    await originals.AwaitAsync(StartTask(() => surface.RevealOriginalRegisteredPageWithinSourceAsync(
                        sameBrowser, originalPage, originalRow, originalActor, Own, Retain, token)));
                    Demand(); await ReadActorAsync();
                    if (!ReferenceEquals(chrome.ObserveOriginalAppContent(tabId), session) ||
                        !session.IsPublicationCurrent || !ReferenceEquals(session.OriginalNativeView, surface) ||
                        session.Address != address)
                        throw new UnauthorizedAccessException("The original Files tab changed during registered-item reveal.");
                }
            }).ConfigureAwait(false);

            void Demand()
            {
                token.ThrowIfCancellationRequested(); DemandCurrent();
                if (originals.OriginalErrors.Count != 0)
                    throw new InvalidOperationException("An original source refusal prevents Files publication.");
                if (!ReferenceEquals(sameBrowser, _originalDownloadBrowser) ||
                    !ReferenceEquals(_services.GetRequiredService<FilesNativeBrowserService>(), sameBrowser) ||
                    !ReferenceEquals(_originalDownloadFiles?.OriginalBrowser, sameBrowser) ||
                    originalActor != _actor || originalActor.AccountId is not null || originalActor.OrganisationId is not null ||
                    originalRow.Kind != HostedItemKind.File || originalRow.CurrentRevisionId is null ||
                    originalRow.ParentId?.Value != originalPage.ParentID ||
                    !originalPage.Items.Any(row => ReferenceEquals(row, originalRow)))
                    throw new UnauthorizedAccessException("Select the same original registered Files page, item and Home actor.");
            }
            void Own(Action body) => originals.Invoke(() => { Demand(); body(); return true; });
            void Retain(Task actual)
            {
                _ = originals.Track(actual); // Local custody precedes borrowed callbacks.
                lock (_gate) _sources.Add(actual);
                retain(actual);
            }
            Task<T> StartRead<T>(Func<Task<T>> factory) => originals.Invoke(() =>
            {
                Demand(); var actual = factory(); Retain(actual); return actual;
            });
            Task StartTask(Func<Task> factory) => originals.Invoke(() =>
            {
                Demand(); var actual = factory(); Retain(actual); return actual;
            });
            async Task ReadActorAsync()
            {
                var actor = await originals.AwaitAsync(StartRead(() =>
                    _home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask())).ConfigureAwait(false);
                if (actor != originalActor)
                    throw new UnauthorizedAccessException("The original Home profile changed before Files publication.");
                Demand();
            }
        }

        private async Task JoinOriginalDownloadFilesAsync(List<Exception> failures)
        {
            if (_originalDownloadFiles is not { } owner) return;
            // A pure external preflight precedes any irreversible retirement.
            try { owner.DemandExternalOriginalRetirementJoin(); }
            catch (Exception error) { AddAppCause(failures, error); return; }
            try { owner.RequestRetirement(); }
            catch (Exception error) { AddAppCause(failures, error); }
            // Request may have acquired a close task before a callback refused.
            if (owner.OriginalClose is { } retained) _originalDownloadFilesClose = retained;
            try { _originalDownloadFilesClose = owner.CloseAndDrainAsync(); }
            catch (Exception error)
            {
                AddAppCause(failures, error);
                _originalDownloadFilesClose ??= owner.OriginalClose;
            }
            if (_originalDownloadFilesClose is { } close)
            {
                Track(close);
                await JoinOriginalAppTaskAsync(close, failures);
            }
            else AddAppCause(failures, new InvalidOperationException("The actual Browser Files owner supplied no retirement Task."));
        }
    }
}
#endif
