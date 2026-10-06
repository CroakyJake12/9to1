using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Development;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    // Root supplies the original graph before routing. No issuer, actor, native Ready,
    // project, conversation, Task or Run is manufactured by this presentation binding.
    private CanonicalSpaceRoutes? _canonicalSpaceRoutes;
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> _canonicalSpaceTaskPages = new();
    private InvalidOperationException? _canonicalPageCapacityRefusal;

    internal void ConfigureCanonicalSpaceTaskRoutes(SpaceRegistry originalSpaces,
        TaskExecutionCoordinator originalCanonical,
        Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness>? bindOriginalNativeTaskFrame,
        DeveloperTaskWorkspaceService? originalDeveloper = null,
        Func<SpaceDeveloperView, DeveloperProjectWorkbenchPage>? createOriginalDevPage = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(originalSpaces);
        ArgumentNullException.ThrowIfNull(originalCanonical);
        if (_canonicalSpaceRoutes is not null)
            throw new InvalidOperationException("The original canonical Space composition is already bound.");
        if (_spaceRegistry is not null && !ReferenceEquals(_spaceRegistry, originalSpaces))
            throw new InvalidOperationException("The shell already uses a different original Space registry.");
        if (originalDeveloper is null && createOriginalDevPage is not null)
            throw new ArgumentException("A native Dev factory requires its SAME original Dev service.");
        var source = new SpaceTaskWorkspaceService(originalSpaces, _conversations, originalCanonical);
        var development = originalDeveloper is null ? null : new SpaceDevelopmentWorkspace(originalSpaces, source, originalDeveloper);
        _spaceRegistry = originalSpaces;
        _canonicalSpaceRoutes = new(source, originalCanonical, development, bindOriginalNativeTaskFrame, createOriginalDevPage);
    }

    private void OpenOriginalCanonicalTaskDashboard() =>
        StartOriginalShellEvent(OpenOriginalCanonicalTaskDashboardAsync);

    private Task OpenOriginalCanonicalTaskDashboardAsync(DesktopOriginalWorkLifetime.Original original) =>
        OpenOriginalCanonicalTaskDashboardAsync(original, SpaceRegistry.TasksSpaceId);

    private async Task OpenOriginalCanonicalTaskDashboardAsync(DesktopOriginalWorkLifetime.Original original, Guid actualSpaceId)
    {
        var routes = _canonicalSpaceRoutes ?? throw new InvalidOperationException("The original Space task services are unconfigured.");
        original.BindPublicationGuard(() => !IsDisposed);
        original.DemandPublication();
        // A maintained built-in ID selects an existing stored Space, never a ContextId.
        var actualSpace = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => SpacesRegistry.ReadExistingAsync(actualSpaceId, original.Token)));
        if (actualSpace is null || actualSpace.IsArchived)
            throw new InvalidOperationException("The actual stored Tasks Space is unavailable; initialize it through its existing owner.");
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            var key = $"space-tasks-{actualSpace.Id:N}";
            var existing = OpenTabs.FirstOrDefault(tab => tab.Key == key);
            if (existing is not null) { SelectedTab = existing; return; }
            DemandCanonicalPageAcquisition();
            var page = new SpaceTasksDashboardPage(routes.Source, routes.Canonical, actualSpace.Id,
                observation => OpenOriginalCanonicalTaskAsync(observation),
                routes.Canonical.HasAttemptAuthority ? instruction => OpenOriginalExplicitTaskAsync(instruction) : null,
                () => OpenOriginalExplicitTaskAsync(null));
            RetainOriginalCanonicalPage(page); // Before any tab/property/activation callback.
            original.DemandPublication();
            AddOrSelectTab(key, "Haven Tasks", page, true, HavenSurface.Tasks, forceNewTab: true);
            // ApplySelectedTab owns the one actual ActivateAsync; never activate twice.
        });
    }

    private Task OpenOriginalCanonicalTaskAsync(SpaceTaskObservation captured) =>
        _originalShellWork.RunAsync(original => OpenOriginalCanonicalTaskAsync(original, captured));

    private async Task OpenOriginalCanonicalTaskAsync(DesktopOriginalWorkLifetime.Original original, SpaceTaskObservation captured)
    {
        var routes = _canonicalSpaceRoutes ?? throw new InvalidOperationException("The original Space task services are unconfigured.");
        original.BindPublicationGuard(() => !IsDisposed);
        original.DemandPublication();
        void OwnRawSource(Action callback) => AcquireOriginalShellSynchronous(original, () => { callback(); return true; });
        var current = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => routes.Source.ReadAsync(captured.SpaceId, captured.Conversation.Id, original.Token, OwnRawSource)));
        if (current.Snapshot?.TaskId != captured.Snapshot?.TaskId || current.Snapshot?.ExecutionId != captured.Snapshot?.ExecutionId)
            throw new InvalidOperationException("The acknowledged Task/Run changed. Reopen the current observation rather than replace work.");
        var actualSpace = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
            () => SpacesRegistry.ReadExistingAsync(current.SpaceId, original.Token)));
        if (actualSpace is null || actualSpace.IsArchived || actualSpace.Revision != current.SpaceRevision)
            throw new InvalidOperationException("The original Space changed during task open.");
        var projectReferences = (actualSpace.ContextReferences ?? [])
            .Where(reference => reference.Kind == SpaceContextReferenceKind.ConnectedEntity && reference.OwnerAppId == "dev")
            .Where(reference => SpaceDevelopmentWorkspace.ReadLink(reference).ConversationId == current.Conversation.Id).ToArray();
        if (projectReferences.Length > 1)
            throw new InvalidDataException("The original task has ambiguous Dev project references; select one through the owning Space editor.");
        SpaceTaskWidgetPage? acquiredPage = null;
        OriginalTaskFrameReadiness? acquiredReadiness = null;
        Task<NativeCanonicalTaskSceneReadiness>? actualReadinessAcquisition = null;
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            var key = $"space-task-{current.Conversation.Id:N}-{current.Snapshot?.TaskId:N}-{current.Snapshot?.ExecutionId:N}";
            var existing = OpenTabs.FirstOrDefault(tab => tab.Key == key);
            if (existing is not null) { SelectedTab = existing; return; }
            var bindFrame = routes.BindOriginalFrame ?? throw new InvalidOperationException("The genuine native Task frame/readiness owner is unconfigured.");
            DemandCanonicalPageAcquisition();
            var readiness = new OriginalTaskFrameReadiness();
            SpaceTaskWidgetPage? page = null;
            page = new SpaceTaskWidgetPage(routes.Source, routes.Canonical, current.SpaceId, current.Conversation.Id,
                readiness, routes.Development, projectReferences.SingleOrDefault()?.ContextId,
                routes.CreateOriginalDevPage is null && _captureOriginalCanonicalDevPage is null ? null :
                    view => OpenOriginalCanonicalDevAsync(page!, view),
                current.Snapshot?.TaskId, current.Snapshot?.ExecutionId);
            RetainOriginalCanonicalPage(page); // Late acquisition stays in the shell cohort even if binding seals it.
            acquiredPage = page; acquiredReadiness = readiness;
            actualReadinessAcquisition = page.AcquireOriginalNativeReadinessAsync(bindFrame);
        });
        if (actualReadinessAcquisition is null || acquiredPage is null || acquiredReadiness is null)
            return; // Existing tab selection acquired no new product/source.
        var actualReadiness = await original.AwaitAsync(actualReadinessAcquisition);
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            acquiredReadiness.Bind(actualReadiness);
            original.DemandPublication();
            var key = $"space-task-{current.Conversation.Id:N}-{current.Snapshot?.TaskId:N}-{current.Snapshot?.ExecutionId:N}";
            AddOrSelectTab(key, current.Conversation.Title, acquiredPage, true, HavenSurface.Tasks, forceNewTab: true);
        });
    }

    private Task OpenOriginalCanonicalTaskSpaceAsync(Guid actualSpaceId) =>
        _originalShellWork.RunAsync(original => OpenOriginalCanonicalTaskDashboardAsync(original, actualSpaceId));

    private Task OpenOriginalSpaceTaskConversationAsync(Conversation conversation) =>
        _originalShellWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !IsDisposed);
            var routes = _canonicalSpaceRoutes ?? throw new InvalidOperationException("Canonical Space services are unconfigured.");
            if (conversation.Mode != HavenMode.Tasks || conversation.SpaceId is not { } spaceId)
                throw new InvalidOperationException("An existing Tasks conversation and its actual Space are required.");
            void OwnRawSource(Action callback) => AcquireOriginalShellSynchronous(original, () => { callback(); return true; });
            var actual = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
                () => routes.Source.ReadAsync(spaceId, conversation.Id, original.Token, OwnRawSource)));
            // Reopen only; this path never calls ConfigureTaskMode, Submit or Begin.
            await OpenOriginalCanonicalTaskAsync(original, actual);
        });

    private Task OpenOriginalCanonicalDevAsync(SpaceDeveloperView captured) =>
        OpenOriginalCanonicalDevAsync(null, captured); // Exact old Func caller remains source compatible.

    private Task OpenOriginalCanonicalDevAsync(SpaceTaskWidgetPage? originalTaskPage, SpaceDeveloperView captured) =>
        _originalShellWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !IsDisposed);
            var routes = _canonicalSpaceRoutes ?? throw new InvalidOperationException("Canonical Space services are unconfigured.");
            var development = routes.Development ?? throw new InvalidOperationException("The SAME original Dev owner is unconfigured.");
            var factory = routes.CreateOriginalDevPage;
            if (factory is null && _captureOriginalCanonicalDevPage is null)
                throw new InvalidOperationException("The genuine native Dev document/frame factory is unconfigured.");
            void OwnRawSource(Action callback) => AcquireOriginalShellSynchronous(original, () => { callback(); return true; });
            var current = await original.AwaitAsync(AcquireOriginalShellSynchronous(original,
                () => development.OpenAsync(captured.Space.Id, captured.ContextReferenceId, original.Token, OwnRawSource)));
            if (current.Link != captured.Link || current.Project.Reference != captured.Project.Reference)
                throw new InvalidOperationException("The original embedded project reference changed; refresh before opening it.");
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                var key = $"space-dev-{current.Space.Id:N}-{current.ContextReferenceId:N}-{current.Task.Snapshot!.ExecutionId:N}";
                var existing = OpenTabs.FirstOrDefault(tab => tab.Key == key);
                if (existing is not null) { SelectedTab = existing; return; }
                DemandCanonicalPageAcquisition();
                DeveloperProjectWorkbenchPage page;
                if (_captureOriginalCanonicalDevPage is { } captureFactory)
                {
                    var actualOrigin = originalTaskPage ?? throw new InvalidOperationException("A genuine originating native Task binder is required for Dev child readiness.");
                    var issuer = actualOrigin.GetOriginalDevelopmentReadinessIssuer();
                    DeveloperProjectWorkbenchPage? retained = null;
                    void CapturePartial(DeveloperProjectWorkbenchPage actual)
                    {
                        RetainOriginalCanonicalPage(actual); // Before any constructor publication or later refusal.
                        if (retained is not null && !ReferenceEquals(retained, actual))
                            throw new InvalidOperationException("The native factory returned multiple different partial Dev pages.");
                        retained = actual;
                    }
                    page = AcquireOriginalShellSynchronous(original, () => captureFactory(current,
                        actual => AcquireOriginalShellSynchronous(original, () => issuer.BindOriginalDevelopment(actual)),
                        CapturePartial, CancellationToken.None));
                    if (!ReferenceEquals(retained, page))
                    {
                        if (page is not null) RetainOriginalCanonicalPage(page);
                        throw new InvalidOperationException("The SAME actual partial Dev page was not captured before constructor publication.");
                    }
                }
                else
                {
                    page = AcquireOriginalShellSynchronous(original, () => factory!(current));
                    RetainOriginalCanonicalPage(page); // Maintained old Func path has its original constructor prerequisite.
                }
                original.DemandPublication();
                AddOrSelectTab(key, current.Project.Project.Name, page, true, HavenSurface.Studio, forceNewTab: true);
                // The factory keeps the original explicit business token, never original.Token.
            });
        });

    private Task OpenOriginalExplicitTaskAsync(string? instruction) =>
        _originalShellWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !IsDisposed);
            NewChatPage? capturedPage = null;
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                DemandCanonicalPageAcquisition();
                var page = CreateNewChatPage();
                capturedPage = page;
                RetainOriginalCanonicalPage(page);
                original.DemandPublication(); page.ConfigureTaskMode(); original.DemandPublication();
            });
            // All later callbacks operate on the SAME captured page, not mutable CurrentPage.
            var page = capturedPage ?? throw new InvalidOperationException("No original explicit task page was acquired.");
            await ConfigureOriginalCanonicalTaskCatalogueAsync(original, page);
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                AddOrSelectTab("task-run-" + Guid.NewGuid().ToString("N")[..8], "Run Task",
                    page, true, HavenSurface.Tasks, forceNewTab: true);
                original.DemandPublication(); ApplyShellVisualState(); original.DemandPublication();
                if (instruction is null) page.FocusComposer();
                else page.Submit(instruction); // Explicit NEW work; actual Page/Chat own its canonical iterator.
            });
        });

    private async Task ConfigureOriginalCanonicalTaskCatalogueAsync(DesktopOriginalWorkLifetime.Original original, NewChatPage page)
    {
        // Same catalogue/domain inputs as the maintained ConfigureAddMenuAsync, with
        // every independently acquired raw Task joined even when a sibling fails.
        Task<IReadOnlyList<AgentDefinition>>? agents = null;
        Task<IReadOnlyList<CapabilityDefinition>>? capabilities = null;
        Task<IReadOnlyList<PromptDefinition>>? instructions = null;
        Task<IReadOnlyList<ModeDefinition>>? apps = null;
        try
        {
            original.DemandPublication(); agents = AcquireOriginalShellSynchronous(original, () => _catalog.GetAgentsAsync(CancellationToken.None));
            original.DemandPublication(); capabilities = AcquireOriginalShellSynchronous(original, () => _capabilityRegistry.DiscoverAsync(CurrentCapabilityPlatform, CancellationToken.None));
            original.DemandPublication(); instructions = AcquireOriginalShellSynchronous(original, () => _catalog.GetPromptsAsync(CancellationToken.None));
            original.DemandPublication(); apps = AcquireOriginalShellSynchronous(original, () => _modeRegistry.GetModesAsync(CancellationToken.None));
        }
        catch (Exception error) { original.Retain(error); }
        foreach (var actual in new Task?[] { agents, capabilities, instructions, apps }.OfType<Task>())
            try { await original.AwaitAsync(actual); } catch (Exception error) { original.Capture(actual, error); }
        original.ThrowRetained();
        if (agents is null || capabilities is null || instructions is null || apps is null)
            throw new InvalidOperationException("No complete original catalogue was acquired.");
        IReadOnlyList<ModeDefinition> actualApps = apps.Result;
#if ANDROID
        var installed = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, GetInstalledAndroidAppDefinitionsAsync));
        actualApps = actualApps.Concat(installed).ToArray();
#endif
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            original.DemandPublication(); _availableCapabilities = capabilities.Result;
            original.DemandPublication(); page.SetAddCatalogue(agents.Result, capabilities.Result, instructions.Result, actualApps);
            original.DemandPublication(); RefreshContextualActions(); original.DemandPublication();
        });
    }

    private void DemandCanonicalPageAcquisition()
    {
        Dispatcher.UIThread.VerifyAccess();
        _originalShellWork.DemandAdmission();
        if (_canonicalPageCapacityRefusal is not null) throw _canonicalPageCapacityRefusal;
        if (_canonicalSpaceTaskPages.Count >= 128)
            throw _canonicalPageCapacityRefusal = new InvalidOperationException("The canonical page cohort requires external shell retirement at its finite bound.");
    }
    private void RetainOriginalCanonicalPage(object page)
    {
        ArgumentNullException.ThrowIfNull(page);
        // Capture first, even if a constructor/factory callback has just requested retirement.
        _canonicalSpaceTaskPages.Enqueue(page);
    }
    private Task PublishOriginalCanonicalTabAsync(DesktopOriginalWorkLifetime.Original original, Action callback) =>
        original.AwaitAsync(AcquireOriginalShellSynchronous(original, () => Dispatcher.UIThread.InvokeAsync(() =>
            AcquireOriginalShellSynchronous(original, () => { original.DemandPublication(); callback(); original.DemandPublication(); return true; })).GetTask()));

    private sealed record CanonicalSpaceRoutes(SpaceTaskWorkspaceService Source, TaskExecutionCoordinator Canonical,
        SpaceDevelopmentWorkspace? Development, Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness>? BindOriginalFrame,
        Func<SpaceDeveloperView, DeveloperProjectWorkbenchPage>? CreateOriginalDevPage);
    private sealed class OriginalTaskFrameReadiness : ICuiSceneReadiness
    {
        private ICuiSceneReadiness? _original;
        public void Bind(ICuiSceneReadiness actual) => _original = actual ?? throw new InvalidOperationException("The actual native readiness owner returned no original.");
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            (_original ?? throw new InvalidOperationException("The original native frame is unbound.")).CheckAsync(token);
    }
}
