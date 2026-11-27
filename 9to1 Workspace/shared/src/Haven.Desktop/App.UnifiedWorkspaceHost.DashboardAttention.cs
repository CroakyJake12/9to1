#if !ANDROID
using Avalonia.Threading;
using Haven.Application;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        private Task<bool> ReviewDashboardAttentionAsync(string requestId,
            AuthenticatedResourceActor expectedActor, Func<bool> isPublicationCurrent, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
            ArgumentNullException.ThrowIfNull(expectedActor);
            ArgumentNullException.ThrowIfNull(isPublicationCurrent);
            return RunObservation(async ledger =>
            {
                // This principal read belongs to the host/window. A shorter page lifetime
                // may withdraw publication, but its ordinary retirement cannot orphan or
                // cancel the host's original read and leave a spurious close failure.
                var actor = await ledger.AwaitAsync(ledger.Invoke(() => ObserveActorAsync(_windowLifetime))).ConfigureAwait(false);
                if (actor != expectedActor)
                    throw new UnauthorizedAccessException("Reopen Home to review requests for the current profile.");
                var dispatch = ledger.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task?>(() =>
                {
                    DemandCurrent();
                    var approvals = _services.GetRequiredService<HomeNativeApprovalWindowOwner>();
                    if (_actor != expectedActor || !approvals.IsBoundToOriginalComposition(_home))
                        throw new UnauthorizedAccessException("The original Home review owner changed.");
                    // Check the actual Dashboard publication on its UI thread immediately
                    // before acquiring the exact existing review-window original. Leaving
                    // the page cannot create a review for a superseded banner selection.
                    if (token.IsCancellationRequested || !isPublicationCurrent()) return null;
                    return ledger.Invoke(() => approvals.OpenOriginalAsync(
                        requestId, _windowLifetime, expectedActor: expectedActor));
                }).GetTask());
                var original = await ledger.AwaitAsync(dispatch).ConfigureAwait(false);
                if (original is null) return false; // Superseded before any review was acquired.
                // Once acquired, the review belongs to the existing window lifetime. The
                // original task remains joined even if the shorter Dashboard view retires.
                await ledger.AwaitAsync(original).ConfigureAwait(false);
                var result = ledger.Invoke(() => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DemandCurrent();
                    return _actor == expectedActor && !token.IsCancellationRequested && isPublicationCurrent();
                }).GetTask());
                return await ledger.AwaitAsync(result).ConfigureAwait(false);
            });
        }
    }
}
#endif
