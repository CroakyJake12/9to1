using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    /// <summary>Opens a saved manual (no-AI) project through the SAME compiled-CUI workbench in a
    /// real shell tab. It resolves the REAL workspace-store identity (no Task/Run/context/model,
    /// no fabricated context), creates the manual workbench through the same factory as the
    /// 9to1://dev session, retains it before publication, and presents it in a new tab.</summary>
    internal Task OpenOriginalManualDevelopmentAsync(DeveloperProjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return _originalShellWork.RunAsync(original => OpenOriginalManualDevelopmentCoreAsync(original, reference));
    }

    private async Task OpenOriginalManualDevelopmentCoreAsync(
        DesktopOriginalWorkLifetime.Original original, DeveloperProjectReference reference)
    {
        original.BindPublicationGuard(() => !IsDisposed);
        original.DemandPublication();
        var workspaces = _manualWorkspaces ?? throw new InvalidOperationException("The original manual workspace store is not configured.");
        var development = _manualDevelopment ?? throw new InvalidOperationException("The original manual Dev owner is not configured.");
        var factory = _manualWorkbenchFactory ?? throw new InvalidOperationException("The original manual workbench factory is not configured.");
        var homeReadiness = _manualHomeReadiness ?? throw new InvalidOperationException("The original manual Home readiness is not configured.");

        // Genuine resolution of the SAME saved identity (same store/dev owners as 9to1://dev).
        var resolvedResult = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => development.ResolveAsync(reference, original.Token)));
        if (!resolvedResult.Succeeded || resolvedResult.Value is null)
            throw new UnauthorizedAccessException(resolvedResult.Error?.Message ?? "The saved manual project could not be resolved.");

        DeveloperProjectWorkbenchPage? retained = null;
        void CapturePartial(DeveloperProjectWorkbenchPage actual)
        {
            RetainOriginalCanonicalPage(actual); // Before constructor host/document/native publication.
            if (retained is not null && !ReferenceEquals(retained, actual))
                throw new InvalidOperationException("The native manual workbench factory acquired multiple different partial pages.");
            retained = actual;
        }
        var page = AcquireOriginalShellSynchronous(original, () => factory.CreateManual(resolvedResult.Value,
            workbench => new ShellManualWorkbenchReadiness(workbench, homeReadiness), CapturePartial, original.Token));
        if (!ReferenceEquals(retained, page))
        {
            RetainOriginalCanonicalPage(page);
            throw new InvalidOperationException("The SAME actual manual workbench page was not retained before publication.");
        }
        original.DemandPublication();
        await PublishOriginalCanonicalTabAsync(original, () =>
            AddOrSelectTab("dev-manual-" + Guid.NewGuid().ToString("N"), "Dev", page, true, HavenSurface.Studio, forceNewTab: true));
    }

    /// <summary>Genuine readiness for an exact manual workbench page. It observes the borrowed native
    /// Home readiness and the page's real presentation/generation currency, and owns a finite
    /// retirement port independent of the page/shell (no delegation back, no self-join).</summary>
    private sealed class ShellManualWorkbenchReadiness : ICuiSceneReadiness,
        IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        private readonly DeveloperProjectWorkbenchPage _page;
        private readonly CuiSceneHost _host;
        private readonly ICuiSceneReadiness _borrowedHome;
        private readonly DesktopOriginalWorkLifetime _work = new(
            static () => Task.CompletedTask, static () => Task.CompletedTask);
        [ThreadStatic] private static List<ShellManualWorkbenchReadiness>? _sourceCallbacks;

        internal ShellManualWorkbenchReadiness(DeveloperProjectWorkbenchPage page, ICuiSceneReadiness borrowedHome)
        {
            ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(borrowedHome);
            _page = page; _host = page.OriginalHost; _borrowedHome = borrowedHome;
        }

        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(_work.RunAsync(original => CheckOriginalAsync(original, cancellationToken)));

        private async Task<CuiSceneAvailability> CheckOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
        {
            var generation = _page.OriginalReadinessGeneration;
            original.BindPublicationGuard(() => _page.IsOriginalReadinessPresentationCurrent(_host, generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller);
            var token = scope.Token;
            DemandOriginal(original, token);
            // Genuine borrowed Home readiness first; never a fabricated receipt.
            var home = await original.AwaitAsync(InvokeOriginalSource(() => _borrowedHome.CheckAsync(token).AsTask()));
            DemandOriginal(original, token);
            if (!Enum.IsDefined(home.State) || string.IsNullOrWhiteSpace(home.Code) || string.IsNullOrWhiteSpace(home.Message))
                throw new InvalidDataException("The borrowed Home readiness returned invalid observation metadata.");
            if (home.State != CuiSceneAvailabilityState.Ready)
                return home;
            // Real page presentation/generation currency is also required.
            if (!_page.IsOriginalReadinessPresentationCurrent(_host, generation))
                return new(CuiSceneAvailabilityState.Unavailable, "ManualWorkbenchNotCurrent", "The manual workbench presentation is not current.");
            return new(CuiSceneAvailabilityState.Ready, "ManualWorkbenchReady", "The manual workbench source presentation is current.");
        }

        private void DemandOriginal(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); original.DemandPublication();
            if (!ReferenceEquals(_host, _page.OriginalHost)) throw new InvalidOperationException("The manual workbench host was replaced.");
            token.ThrowIfCancellationRequested(); original.DemandPublication();
        }

        private T InvokeOriginalSource<T>(Func<T> callback)
        {
            var stack = _sourceCallbacks ??= []; stack.Add(this);
            try { return callback(); }
            catch (Exception error)
            {
                if (error is OperationCanceledException) throw new AggregateException("An actual manual readiness callback faulted.", error);
                throw;
            }
            finally { stack.RemoveAt(stack.Count - 1); }
        }

        public void DemandExternalOriginalRetirementJoin()
        {
            if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
                throw new InvalidOperationException("An actual manual readiness callback cannot join its encompassing retirement.");
            _work.DemandExternalClose();
        }
        public void RequestRetirement() => _work.RequestRetirement();
        public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    }
}
