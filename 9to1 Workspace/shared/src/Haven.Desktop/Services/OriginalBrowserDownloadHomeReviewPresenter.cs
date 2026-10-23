using Avalonia.Threading;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

// Finite borrowed presentation of an already-issued Home request. This owns no
// window/store/profile and has no approval/Begin/Files execution authority.
internal sealed class OriginalBrowserDownloadHomeReviewPresenter(IServiceProvider originalProvider,
    HomeNativeWindowsComposition originalHome, FilesNativeBrowserService originalBrowser,
    HomeNativeApprovalWindowOwner originalApprovals) : IFilesOriginalBrowserDownloadReviewPresenter
{
    public Task OpenOriginalRegistrationReviewWithinSourceAsync(FilesNativeBrowserService sameBrowser,
        FilesNativeBrowserService.BrowserDownloadFilesOwner sameOwner,
        IBrowserOriginalDownloadFilesRegistration originalRegistration,
        AuthenticatedResourceActor originalActor, string originalRequestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalRequestId);
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this); originals.BindOriginalCallerCallback(scope);
        return originals.RunToOriginalSettlementAsync(async () =>
        {
            DemandPairing();
            await ReadOriginalActorAsync().ConfigureAwait(false);
            // Retain both actual dispatcher acquisition and the actual Home open
            // task, including post-callback refusal. No fire-and-forget UI work.
            var dispatch = StartRead(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
                StartTask(() =>
                {
                    DemandPairing();
                    // Home owns this shared window's lifetime. Canceling a
                    // browser command must not retire another Home review.
                    return originalApprovals.OpenOriginalAsync(originalRequestId, CancellationToken.None);
                })).GetTask());
            var actualOpen = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            await originals.AwaitAsync(actualOpen).ConfigureAwait(false);
            await ReadOriginalActorAsync().ConfigureAwait(false);
            DemandPairing();
        });

        void DemandPairing()
        {
            token.ThrowIfCancellationRequested();
            if (originals.OriginalErrors.Count != 0)
                throw new InvalidOperationException("A prior original source refusal prevents further Home presentation.");
            _ = originalProvider.RequireOriginalWindowsHomeComponents(originalHome);
            if (!ReferenceEquals(sameBrowser, originalBrowser) ||
                !ReferenceEquals(originalProvider.GetRequiredService<FilesNativeBrowserService>(), originalBrowser) ||
                !ReferenceEquals(sameOwner.OriginalBrowser, originalBrowser) ||
                !sameOwner.IsOriginalPendingRegistrationReview(originalRegistration, originalActor, originalRequestId) ||
                !originalApprovals.IsBoundToOriginalComposition(originalHome))
                throw new UnauthorizedAccessException("Only the same current Files registration may select its original Home review.");
        }
        void Own(Action body) => originals.Invoke(() => { DemandPairing(); body(); return true; });
        void Retain(Task actual) { _ = originals.Track(actual); retain(actual); }
        Task<T> StartRead<T>(Func<Task<T>> factory) => originals.Invoke(() =>
        {
            DemandPairing();
            var actual = factory();
            Retain(actual); // Internal custody precedes a failing external retainer.
            return actual;
        });
        Task StartTask(Func<Task> factory) => originals.Invoke(() =>
        {
            DemandPairing();
            var actual = factory(); Retain(actual); return actual;
        });
        async Task ReadOriginalActorAsync()
        {
            var actor = await originals.AwaitAsync(StartRead(() => originalHome.Profiles.GetCurrentAsync(
                Own, Retain, token).AsTask())).ConfigureAwait(false);
            if (actor != originalActor)
                throw new UnauthorizedAccessException("The original Home profile changed before browser download review publication.");
        }
    }
}
