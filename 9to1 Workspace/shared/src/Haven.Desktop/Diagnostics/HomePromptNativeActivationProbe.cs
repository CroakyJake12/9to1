#if ASTRA_HOME_NATIVE_PROBE
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Validation;

/// <summary>Validation-only original native host observer. The default product excludes this entry point.
/// It displays a synthetic request against the actual local actor and never decides, trusts or executes it.
/// Whole native host lifecycle and original managed/process drains require separate caller evidence.</summary>
internal static class HomePromptNativeActivationProbe
{
    private static Task? observation;

    [STAThread]
    internal static int Main(string[] args)
    {
        if (args.Length != 0) throw new ArgumentException("The isolated native Home observer takes no product arguments.");
        var profile = NativeProbeProfileWitness.RequireFreshOriginalProducerProfile();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var exit = Program.BuildAvaloniaApp().AfterSetup(_ =>
            Dispatcher.UIThread.Post(() => observation = ObserveAsync(deadline.Token, profile), DispatcherPriority.Background))
            .StartWithClassicDesktopLifetime([]);
        // This is the actual observed asynchronous operation, not a fire-and-forget success marker.
        if (observation is null) throw new InvalidOperationException("The original native UI observer never started.");
        if (!observation.IsCompleted)
            throw new InvalidOperationException("The original classic loop exited before its observer settled; original outer drains are required.");
        observation.GetAwaiter().GetResult();
        profile.RequireCurrentOriginalProducerProfile();
        NativeProbeOriginalDesktopShutdown.RequireSettledOriginalTask();
        if (exit != 0) throw new InvalidOperationException("The original native lifetime returned a nonzero exit.");
        Console.WriteLine("ASTRA_HOME_ORIGINAL_CLASSIC_VISIBLE_PENDING_OBSERVATION_AND_EXIT_TASK_SETTLED_DRAINS_REQUIRED");
        return 0;
    }

    private static async Task ObserveAsync(CancellationToken originalToken, NativeProbeProfileWitness profile)
    {
        Dispatcher.UIThread.VerifyAccess();
        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var window = desktop?.MainWindow;
        using var observerLifetime = CancellationTokenSource.CreateLinkedTokenSource(originalToken);
        var token = observerLifetime.Token;
        using var reviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var allowOriginalClose = false;
        Exception? closingFailure = null;
        void DeferOriginalClose(object? sender, WindowClosingEventArgs args)
        {
            if (allowOriginalClose) return;
            args.Cancel = true;
            // Ordinary close keeps the actual UI loop alive until its original task settles.
            try { observerLifetime.Cancel(); }
            catch (Exception error) { closingFailure = closingFailure is null ? error : new AggregateException(closingFailure, error); }
        }
        if (window is not null) window.Closing += DeferOriginalClose;
        Task<bool>? review = null;
        Exception? primary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (Avalonia.Application.Current is not App || desktop is null || window is null)
                throw new InvalidOperationException("The original App/classic native lifetime/MainWindow is missing.");
            var services = App.Services ?? throw new InvalidOperationException("The original App did not initialize its DI provider.");
            profile.RequireInitializedAppDataDirectory(services.GetRequiredService<IAppPaths>().DataDirectory);
            if (!window.IsVisible || !window.IsEffectivelyVisible || window.DataContext is not MainView shell || shell.IsDisposed)
                throw new InvalidOperationException("The original MainWindow/MainView is not actually visible and current.");
            var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            var actor = await actors.GetCurrentAsync(token) ?? throw new UnauthorizedAccessException("The actual local OS actor is unavailable.");
            var permissions = services.GetRequiredService<HomePermissionTrustService>();
            if (!ReferenceEquals(services.GetRequiredService<HomeAppAiServices>().Permissions, permissions))
                throw new InvalidOperationException("The original App is not using the shared Home broker.");
            var target = new HomeObjectReference("home.profile-model-routes", actor.ProfileId);
            var pending = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Original native Home validation request", actor.ProfileId, actor.AuthenticationRevision, true),
                "actual-original-native-home-" + Guid.NewGuid().ToString("N"),
                new(HomeModelPickerFeatureProvider.AppId, "models.routes.update", [target]),
                new([target.ObjectType], 1, [target], false, "Observe this exact native prompt without approving or executing it")), token);
            if (pending.State != HomePermissionRequestState.PendingApproval || pending.IsAllowed || pending.TrustLevel is not null)
                throw new InvalidOperationException("A fresh isolated actual pending request is required.");
            var presenter = services.GetRequiredService<IHomeApprovalPromptPresenter>();
            if (presenter is not NativeHomeApprovalPromptPresenter)
                throw new InvalidOperationException("The actual registered native Home presenter is missing.");
            review = presenter.ShowPendingRequestAsync(pending.RequestId, reviewLifetime.Token).AsTask();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(App.Services, services) || await actors.GetCurrentAsync(token) != actor)
                    throw new UnauthorizedAccessException("The original native Home provider or whole local actor changed.");
                var snapshot = await permissions.GetSnapshotAsync(cancellationToken: token);
                if (snapshot.RecentAuditEvents.Any(item => item.RequestId == pending.RequestId && item.Kind == HomePermissionAuditKind.ApprovalPromptShown))
                    break;
                if (review.IsCompleted)
                {
                    await review;
                    throw new InvalidOperationException("The actual presenter finished before the exact native display acknowledgement.");
                }
                await Task.Delay(25, token);
            }
            window.UpdateLayout();
            var actual = window.GetVisualDescendants().OfType<HomeApprovalCuiSurface>()
                .Where(surface => surface.IsEffectivelyVisible && surface.Bounds.Width > 0 && surface.Bounds.Height > 0 &&
                    ReferenceEquals(TopLevel.GetTopLevel(surface), window)).ToArray();
            if (actual.Length != 1 || !window.IsVisible)
                throw new InvalidOperationException("The actual visible original native Home control is missing or ambiguous.");
            using (var source = typeof(HomeApprovalCuiSurface).Assembly.GetManifestResourceStream("HavenOS.Home.NativeUI.Resources.Cui.HomeApprovals.cui")
                ?? throw new InvalidDataException("The actual embedded Home approval scene is missing."))
            {
                var digest = await SHA256.HashDataAsync(source, token);
                if (Convert.ToHexString(digest).ToLowerInvariant() != "98000a602764c3eda03386f49327c067f9b7c20f987260b68eeb21d1902ab266")
                    throw new InvalidDataException("The actual native scene differs from the reviewed complete CUI document.");
            }
            var authorization = await permissions.GetAuthorizationAsync(pending.RequestId, token);
            var observed = await permissions.GetSnapshotAsync(cancellationToken: token);
            if (authorization.State != HomePermissionRequestState.PendingApproval || authorization.IsAllowed || authorization.TrustLevel is not null ||
                !observed.PendingRequests.Any(item => item.RequestId == pending.RequestId) ||
                observed.RecentAuditEvents.Any(item => item.RequestId == pending.RequestId &&
                    item.Kind is HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted))
                throw new InvalidOperationException("Native visibility incorrectly became a decision, trust or execution grant.");
            if (!ReferenceEquals(App.Services, services) || await actors.GetCurrentAsync(token) != actor)
                throw new UnauthorizedAccessException("The original native Home observation actor/provider changed.");
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Exception? cleanupFailure = closingFailure;
            void RetainCleanup(Exception error) => cleanupFailure = cleanupFailure is null ? error : new AggregateException(cleanupFailure, error);
            // Each original cleanup action runs even when a cancellation callback throws.
            try { reviewLifetime.Cancel(); }
            catch (Exception error) { RetainCleanup(error); }
            try
            {
                if (review is not null)
                {
                    try { await review; }
                    catch (OperationCanceledException error) when (error.CancellationToken == reviewLifetime.Token && reviewLifetime.IsCancellationRequested) { }
                }
            }
            catch (Exception error) { RetainCleanup(error); }
            try
            {
                allowOriginalClose = true;
                if (window is not null)
                {
                    window.Closing -= DeferOriginalClose;
                    window.Close();
                }
                else desktop?.Shutdown(1);
            }
            catch (Exception error) { RetainCleanup(error); }
            if (cleanupFailure is not null)
                throw primary is null ? cleanupFailure : new AggregateException(primary, cleanupFailure);
        }
    }
}
#endif
