#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Development;
using Haven.Desktop.Views.Pages.Assistants;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly Dictionary<AssistantsNativeCuiSurface, OriginalAssistantDevelopmentSource> _assistantDevelopmentSources =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, OriginalAssistantWorkbench> _assistantDevelopmentPages = new(StringComparer.Ordinal);

    // Created only by this SAME shell's actual Assistant route. The page retains this
    // callback before surface construction; configuration follows real tab publication.
    private OriginalAssistantNativeDevelopmentRoute? CreateOriginalAssistantDevelopmentRoute(
        IServiceProvider provider, HomeNativeWindowsComposition home,
        HavenOS.Apps.Assistants.Core.AssistantsWorkspaceController controller,
        CancellationToken appLifetime, CancellationToken windowLifetime, Window window)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        if (provider.GetService<IAssistantOriginalDevelopmentOwner>() is null) return null;
        if (!ReferenceEquals(provider, global::Haven.Desktop.App.Services) ||
            controller.OriginalCanonicalBridge is not DenAssistantCanonicalBridge bridge ||
            provider.GetService<IAssistantOriginalDevelopmentOwner>() is not DenAssistantOriginalDevelopmentOwner owner ||
            !ReferenceEquals(bridge.OriginalDevelopmentOwner, owner))
            throw new UnauthorizedAccessException("The actual configured canonical Assistant Dev issuer differs.");
        DemandOriginalAssistantProjectStoreComposition(provider, home, owner);
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
        appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
        return new(this, provider, home, controller, appLifetime, windowLifetime, window);
    }

    private static void DemandOriginalAssistantProjectStoreComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, DenAssistantOriginalDevelopmentOwner owner)
    {
        // Exact configured components are a composition check, never a store READ
        // grant. Every candidate/handoff still obtains fresh source-owned receipts.
        if (provider.GetService<ICanonicalProjectContextStoreReadSource>() is not CanonicalProjectContextStoreReadOwner contexts ||
            provider.GetRequiredService<IConversationRepository>() is not UsageTrackingConversationRepository conversations ||
            !conversations.IsBoundToOriginalRepository(provider.GetRequiredService<ConversationRepository>()) ||
            provider.GetRequiredService<IContainerRepository>() is not ContainerRepository containers ||
            !ReferenceEquals(owner.OriginalProjectContextStoreReadOwner, contexts) ||
            !ReferenceEquals(owner.OriginalConversationOwner, conversations) ||
            !ReferenceEquals(owner.OriginalContainerOwner, containers) ||
            !contexts.HasOriginalComposition(provider.GetRequiredService<CanonicalSqliteOriginalStoreOwner>(),
                provider.GetRequiredService<ConversationRepository>(), containers,
                provider.GetRequiredService<HomeResourceStoreOwnershipAuthority>()) ||
            !contexts.OriginalStoreOwner.HasOriginalComposition(provider.GetRequiredService<SqliteDatabase>(),
                provider.GetRequiredService<IAppPaths>(), home.Profiles))
            throw new UnauthorizedAccessException("The SAME sealed canonical project store READ composition is unavailable.");
    }

    private sealed class OriginalAssistantNativeDevelopmentRoute(MainView shell,
        IServiceProvider provider, HomeNativeWindowsComposition home,
        HavenOS.Apps.Assistants.Core.AssistantsWorkspaceController controller,
        CancellationToken appLifetime, CancellationToken windowLifetime, Window window)
        : IAssistantsNativeOriginalDevelopmentRoute
    {
        private NativeAssistantsDesktopPage? _page;
        internal void CaptureOriginalPage(NativeAssistantsDesktopPage page)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_page is not null && !ReferenceEquals(_page, page))
                throw new UnauthorizedAccessException("Retain the SAME actual Assistant partial page for its Dev callback.");
            _page = page; // Before the actual page/surface can publish native callbacks.
        }
        internal bool IsOriginalComposition(MainView sameShell, NativeAssistantsDesktopPage samePage,
            IServiceProvider sameProvider, HomeNativeWindowsComposition sameHome,
            HavenOS.Apps.Assistants.Core.AssistantsWorkspaceController sameController,
            CancellationToken sameAppLifetime, CancellationToken sameWindowLifetime, Window sameWindow) =>
            ReferenceEquals(shell, sameShell) && ReferenceEquals(_page, samePage) &&
            ReferenceEquals(provider, sameProvider) && ReferenceEquals(home, sameHome) &&
            ReferenceEquals(controller, sameController) && appLifetime == sameAppLifetime &&
            windowLifetime == sameWindowLifetime && ReferenceEquals(window, sameWindow);

        public Task OpenOriginalAsync(AssistantsNativeCuiSurface actualOrigin,
            AssistantDevelopmentBinding actualBinding, long originalPresentationGeneration, CancellationToken token)
        {
            // Native owns the SAME returned shell task. No extra Dev factory, scope,
            // Task, project or authority DTO is created by this callback adapter.
            var page = _page ?? throw new InvalidOperationException("No actual Assistant page was captured for this callback.");
            if (!ReferenceEquals(actualOrigin.OriginalDevelopmentRoute, this) ||
                !ReferenceEquals(page.OriginalDevelopmentRoute, this) ||
                !ReferenceEquals(page.OriginalSurface, actualOrigin) ||
                !ReferenceEquals(actualOrigin.OriginalController, controller))
                throw new UnauthorizedAccessException("The callback belongs to another actual Assistant surface/page.");
            return shell.OpenOriginalAssistantDevelopmentAsync(actualOrigin, actualBinding,
                originalPresentationGeneration, token);
        }
    }

    // Root-only actual constructor graph. No caller IDs, resource path or ready flag can configure this source.
    internal void ConfigureOriginalAssistantDevelopmentSource(AssistantsNativeCuiSurface surface,
        NativeAssistantsDesktopPage actualOwningPage, IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken actualAppLifetime, CancellationToken actualWindowLifetime, Window actualWindow)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(surface); ArgumentNullException.ThrowIfNull(actualOwningPage);
        ArgumentNullException.ThrowIfNull(actualWindow);
        _originalShellWork.RunSynchronous(original => AcquireOriginalShellSynchronous(original, () =>
        {
            original.DemandPublication();
            if (!ReferenceEquals(provider, global::Haven.Desktop.App.Services))
                throw new UnauthorizedAccessException("Retain the original configured Assistant provider.");
            var controller = surface.OriginalController;
            if (surface.OriginalDevelopmentRoute is not OriginalAssistantNativeDevelopmentRoute route ||
                !ReferenceEquals(actualOwningPage.OriginalDevelopmentRoute, route) ||
                !route.IsOriginalComposition(this, actualOwningPage, provider, home, controller,
                    actualAppLifetime, actualWindowLifetime, actualWindow))
                throw new UnauthorizedAccessException("The SAME source-created native Dev callback was not retained before surface publication.");
            if (controller.OriginalCanonicalBridge is not DenAssistantCanonicalBridge bridge)
                throw new UnauthorizedAccessException("The actual sealed canonical Assistant bridge is required.");
            if (provider.GetService<IAssistantOriginalDevelopmentOwner>() is not DenAssistantOriginalDevelopmentOwner owner)
                throw new InvalidOperationException("The sealed actual canonical Assistant project READ owner is not configured.");
            DemandOriginalAssistantProjectStoreComposition(provider, home, owner);
            var tasks = provider.GetRequiredService<Haven.Application.TaskExecutionCoordinator>();
            var development = provider.GetRequiredService<DeveloperTaskWorkspaceService>();
            var files = provider.GetRequiredService<FilesNativeBrowserService>();
            var factory = provider.GetRequiredService<DeveloperProjectWorkbenchPageFactory>();
            var actors = provider.GetRequiredService<HostLocalTaskActorSource>();
            var den = provider.GetRequiredService<HomePersonalDenFactory>();
            if (!ReferenceEquals(bridge.OriginalTaskOwner, tasks) || !ReferenceEquals(bridge.OriginalDevelopmentOwner, owner) ||
                !ReferenceEquals(bridge.OriginalHomeDenFactory, den) || !development.IsBoundToOriginalCanonicalOwner(tasks) ||
                !factory.IsBoundToOriginalComposition(development, tasks, files) ||
                !ReferenceEquals(owner.OriginalHomeDenFactory, den) || !ReferenceEquals(owner.OriginalTaskOwner, tasks) ||
                !ReferenceEquals(owner.OriginalDeveloperOwner, development) ||
                !ReferenceEquals(owner.OriginalTaskActorOwner, actors) ||
                !ReferenceEquals(owner.OriginalProjectReadOwner, provider.GetRequiredService<HomeColdProjectReadReconciliation>()) ||
                !ReferenceEquals(actualOwningPage.OriginalController, controller) ||
                !ReferenceEquals(actualOwningPage.OriginalSurface, surface) ||
                !actualOwningPage.IsOriginalComposition(provider, home) ||
                !actualOwningPage.IsOriginalWindow(actualWindow, actualAppLifetime, actualWindowLifetime) ||
                actualOwningPage.OriginalInitialization?.IsCompletedSuccessfully != true || actualOwningPage.OriginalClose is not null ||
                !actualWindow.IsVisible || !ReferenceEquals(TopLevel.GetTopLevel(this), actualWindow) ||
                !ReferenceEquals(TopLevel.GetTopLevel(actualOwningPage), actualWindow))
                throw new UnauthorizedAccessException("The actual Assistant/Task/Dev/Files/Home composition differs.");
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
            if (!actualAppLifetime.CanBeCanceled || !actualWindowLifetime.CanBeCanceled)
                throw new ArgumentException("Retain the actual App and owning native window lifetimes.");
            actualAppLifetime.ThrowIfCancellationRequested(); actualWindowLifetime.ThrowIfCancellationRequested();
            foreach (var ended in _assistantDevelopmentSources.Where(pair => pair.Value.NativePage.OriginalClose?.IsCompletedSuccessfully == true).Select(pair => pair.Key).ToArray())
                _assistantDevelopmentSources.Remove(ended);
            if (_assistantDevelopmentSources.ContainsKey(surface) || _assistantDevelopmentSources.Count >= 16)
                throw new InvalidOperationException("The original native Assistant source is already bound or its cohort is full.");
            _assistantDevelopmentSources.Add(surface, new(surface, actualOwningPage, provider, home,
                bridge, owner, factory, tasks, development, files, actors, den, actualAppLifetime, actualWindowLifetime, actualWindow));
            original.DemandPublication();
            return true;
        }));
    }

    internal Task OpenOriginalAssistantDevelopmentAsync(AssistantsNativeCuiSurface actualOrigin,
        AssistantDevelopmentBinding actualBinding, long originalPresentationGeneration,
        CancellationToken callerCancellation) => _originalShellWork.RunAsync(async original =>
    {
        original.BindPublicationGuard(() => !IsDisposed);
        OriginalAssistantDevelopmentSource? source = null;
        WorkspaceTabViewModel? originTab = null;
        var originGeneration = originalPresentationGeneration;
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            if (!_assistantDevelopmentSources.TryGetValue(actualOrigin, out source))
                throw new InvalidOperationException("The original native Assistant development source is unconfigured.");
            originTab = OpenTabs.SingleOrDefault(tab => ReferenceEquals(tab.Page, source.NativePage))
                ?? throw new InvalidOperationException("The actual retained Assistant tab is unavailable.");
            callerCancellation.ThrowIfCancellationRequested();
            DemandOriginalAssistantOrigin(source, originTab, actualBinding, originGeneration);
            DemandCanonicalPageAcquisition();
        });
        var retainedSource = source!;
        var retainedTab = originTab!;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(original.Token,
            retainedSource.AppLifetime, retainedSource.WindowLifetime, callerCancellation);
        var token = lifetime.Token;
        void DemandOrigin()
        {
            token.ThrowIfCancellationRequested(); original.DemandPublication();
            DemandOriginalAssistantOrigin(retainedSource, retainedTab, actualBinding, originGeneration);
        }
        void Scope(Action callback) => AcquireOriginalShellSynchronous(original, () =>
        {
            // Backend source callbacks can resume off the UI thread. They retain accepted
            // work while the actual surface binding remains current; only dispatcher
            // publication reads the owning selected-tab cohort.
            token.ThrowIfCancellationRequested(); original.DemandPublication();
            if (!retainedSource.Surface.IsPresentationCurrent(actualBinding.Conversation, originGeneration))
                throw new InvalidOperationException("The original Assistant conversation presentation changed.");
            callback(); return true;
        });
        void Retain(Task actual) { _ = original.AwaitAsync(actual); }
        var reference = actualBinding.Project.Reference;
        var identity = actualBinding.Conversation.Definition;
        var key = $"assistant-dev-{identity.Identity.DefinitionId}-{identity.Revision}-{actualBinding.CanonicalTask.ContextId:N}-" +
            $"{actualBinding.CanonicalTask.TaskId:N}-{actualBinding.CanonicalTask.ExecutionId:N}-{reference.WorkspaceId:N}-" +
            $"{reference.WorkspaceRevision}-{reference.ProjectId:N}-{reference.ProjectRevision}-{reference.RootId:N}-{reference.RepositoryBindingId}";
        OriginalAssistantWorkbench? existing = null;
        var detachedPrevious = false;
        await PublishOriginalCanonicalTabAsync(original, () =>
        {
            DemandOrigin();
            _assistantDevelopmentPages.TryGetValue(key, out existing);
            var mapped = existing;
            detachedPrevious = mapped is not null &&
                !OpenTabs.Any(value => value.Key == key && ReferenceEquals(value.Page, mapped.Page));
        });
        if (existing is not null && detachedPrevious)
        {
            // Absence from the tab collection is not a cleanup receipt. Join this
            // SAME real page before releasing its map entry or opening a new view.
            var previous = existing;
            await original.AwaitAsync(AcquireOriginalShellSynchronous(original, previous.Page.CloseAndDrainAsync));
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                DemandOrigin();
                if (!_assistantDevelopmentPages.TryGetValue(key, out var same) || !ReferenceEquals(same, previous) ||
                    OpenTabs.Any(value => value.Key == key && ReferenceEquals(value.Page, previous.Page)))
                    throw new InvalidOperationException("The original previous Dev tab changed during its independent drain.");
                _assistantDevelopmentPages.Remove(key);
            });
            existing = null;
        }
        if (existing is { } existingWorkbench)
        {
            var observed = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () =>
                retainedSource.Owner.ValidateOriginalBindingWithinSourceAsync(existingWorkbench.Binding, existingWorkbench.Custody, Scope, Retain, token)));
            DemandAssistantDevelopmentObservation(existingWorkbench.Binding, observed);
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                DemandOrigin();
                var tab = OpenTabs.SingleOrDefault(value => value.Key == key && ReferenceEquals(value.Page, existingWorkbench.Page));
                if (tab is null) throw new InvalidOperationException("The actual original Dev tab retired; reopen through a new native acquisition.");
                SelectedTab = tab;
            });
            return; // Re-select the same actual host; no new project, Task or effect is started.
        }

        AssistantNativeTaskSceneReadiness? child = null;
        IAssistantOriginalDevelopmentCustody? custody = null;
        DeveloperProjectWorkbenchPage? retainedPage = null;
        try
        {
            // Parent custody observes the actual transfer before any post-await publication guard.
            custody = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () =>
                retainedSource.Owner.TransferOriginalWithinSourceAsync(actualBinding, Scope, Retain, token)));
            child = AcquireOriginalShellSynchronous(original, () => AssistantNativeTaskSceneReadiness.CaptureOriginalSameProcess(
                retainedSource.Owner, actualBinding, custody, retainedSource.Provider, retainedSource.Home,
                retainedSource.Actors, retainedSource.AppLifetime, retainedSource.WindowLifetime,
                RetainOriginalCanonicalPage)); // Exact child retained even after parent retirement, before source callbacks.
            if (!AcquireOriginalShellSynchronous(original, () => retainedSource.Owner.IsIssuedOriginalCustody(actualBinding, custody)))
                throw new UnauthorizedAccessException("The actual source did not issue the retained transferred Dev custody.");
            var observed = await original.AwaitAsync(AcquireOriginalShellSynchronous(original, () =>
                retainedSource.Owner.ValidateOriginalBindingWithinSourceAsync(actualBinding, custody, Scope, Retain, token)));
            DemandAssistantDevelopmentObservation(actualBinding, observed);
            await PublishOriginalCanonicalTabAsync(original, () =>
            {
                DemandOrigin(); DemandCanonicalPageAcquisition();
                void CapturePage(DeveloperProjectWorkbenchPage actual)
                {
                    RetainOriginalCanonicalPage(actual);
                    if (retainedPage is not null && !ReferenceEquals(retainedPage, actual))
                        throw new InvalidOperationException("The actual factory captured multiple different Dev partials.");
                    retainedPage = actual;
                    // Retain the actual partial under this identity before any
                    // subsequent original guard or native constructor callback.
                    if (!_assistantDevelopmentPages.TryGetValue(key, out var mapped))
                        _assistantDevelopmentPages.Add(key, new(actualBinding, custody, actual));
                    else if (!ReferenceEquals(mapped.Page, actual))
                        throw new InvalidOperationException("This identity already retains a different actual Dev partial.");
                    DemandOrigin();
                }
                var page = AcquireOriginalShellSynchronous(original, () => retainedSource.Factory.CreateOriginal(
                    observed.Project, observed.CanonicalTask,
                    actual => AcquireOriginalShellSynchronous(original, () => child.BindOriginalDevelopment(actual)),
                    CapturePage, CancellationToken.None)); // Business cancellation remains explicit and independent of this view.
                if (!ReferenceEquals(page, retainedPage))
                {
                    RetainOriginalCanonicalPage(page);
                    throw new InvalidOperationException("The same original partial Dev page was not captured before publication.");
                }
                DemandOrigin();
                if (!_assistantDevelopmentPages.TryGetValue(key, out var retainedEntry) || !ReferenceEquals(retainedEntry.Page, page))
                    throw new InvalidOperationException("The SAME actual partial Dev page was not retained under its original identity.");
                AddOrSelectTab(key, observed.Project.Project.Name, page, true, HavenSurface.Studio, false);
                // Selecting Dev legitimately deactivates the origin. The transferred child
                // now follows its own actual host and independent backend, never origin generation.
                original.DemandPublication();
                if (SelectedTab is not { } published || published.Key != key || !ReferenceEquals(published.Page, page))
                    throw new InvalidOperationException("The actual Dev tab changed during native publication.");
            });
        }
        catch (Exception primary)
        {
            original.Retain(primary);
            // Keep the exact partial in the identity map. A later reopen must
            // independently join its actual close; failed/unknown custody refuses.
            var closes = new List<Task>();
            if (retainedPage is not null)
            {
                try { closes.Add(AcquireOriginalShellSynchronous(original, retainedPage.CloseAndDrainAsync)); }
                catch (Exception failure) { original.Retain(failure); }
            }
            try
            {
                if (child is not null) closes.Add(AcquireOriginalShellSynchronous(original, child.CloseAndDrainAsync));
                else if (custody is not null) closes.Add(AcquireOriginalShellSynchronous(original, custody.CloseAndDrainAsync));
            }
            catch (Exception failure) { original.Retain(failure); }
            foreach (var close in closes.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await original.AwaitAsync(close); }
                catch (Exception failure) { original.Retain(close.Exception ?? failure); }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
    });

    private void DemandOriginalAssistantOrigin(OriginalAssistantDevelopmentSource source, WorkspaceTabViewModel tab,
        AssistantDevelopmentBinding binding, long generation)
    {
        Dispatcher.UIThread.VerifyAccess(); _originalShellWork.DemandAdmission();
        source.AppLifetime.ThrowIfCancellationRequested(); source.WindowLifetime.ThrowIfCancellationRequested();
        var controller = source.Surface.OriginalController;
        var ownsSurface = ReferenceEquals(source.NativePage.OriginalSurface, source.Surface) &&
            ReferenceEquals(source.NativePage.Content, source.Surface) &&
            ReferenceEquals(source.NativePage.OriginalController, controller) &&
            source.NativePage.OriginalInitialization?.IsCompletedSuccessfully == true &&
            source.NativePage.OriginalClose is null &&
            source.NativePage.IsOriginalComposition(source.Provider, source.Home) &&
            source.NativePage.IsOriginalWindow(source.Window, source.AppLifetime, source.WindowLifetime);
        if (!_assistantDevelopmentSources.TryGetValue(source.Surface, out var current) || !ReferenceEquals(source, current) ||
            !ReferenceEquals(source.Provider, global::Haven.Desktop.App.Services) ||
            !ReferenceEquals(controller.OriginalCanonicalBridge, source.Bridge) ||
            !ReferenceEquals(source.Bridge.OriginalTaskOwner, source.Tasks) ||
            !ReferenceEquals(source.Bridge.OriginalDevelopmentOwner, source.Owner) ||
            !ReferenceEquals(source.Bridge.OriginalHomeDenFactory, source.Den) || !ownsSurface ||
            !ReferenceEquals(source.Provider.GetService<IAssistantOriginalDevelopmentOwner>(), source.Owner) ||
            !ReferenceEquals(source.Provider.GetRequiredService<HostLocalTaskActorSource>(), source.Actors) ||
            !ReferenceEquals(tab.Page, source.NativePage) ||
            !OpenTabs.Contains(tab) || !source.Window.IsVisible ||
            !ReferenceEquals(TopLevel.GetTopLevel(this), source.Window) ||
            !source.Surface.IsPresentationCurrent(binding.Conversation, generation) ||
            !source.Bridge.IsIssuedOriginalBinding(binding.Conversation) || !source.Owner.IsIssuedOriginalBinding(binding) ||
            !source.Factory.IsBoundToOriginalComposition(source.Development, source.Tasks, source.Files))
            throw new UnauthorizedAccessException("The actual Assistant native source, membership or owning tab changed.");
        DemandOriginalAssistantProjectStoreComposition(source.Provider, source.Home, source.Owner);
        if (!ReferenceEquals(source.NativePage.OriginalDevelopmentRoute, source.Surface.OriginalDevelopmentRoute) ||
            source.Surface.OriginalDevelopmentRoute is not OriginalAssistantNativeDevelopmentRoute route ||
            !route.IsOriginalComposition(this, source.NativePage, source.Provider, source.Home, controller,
                source.AppLifetime, source.WindowLifetime, source.Window))
            throw new UnauthorizedAccessException("The actual native callback composition changed.");
        source.NativePage.DemandOriginalDevelopmentPresentation(source.Surface, binding.Conversation, generation);
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(source.Provider, source.Home);
    }

    private static void DemandAssistantDevelopmentObservation(AssistantDevelopmentBinding binding,
        AssistantDevelopmentCurrentObservation observed)
    {
        if (observed.Project.Reference != binding.Project.Reference ||
            observed.CanonicalTask.TaskId != binding.CanonicalTask.TaskId ||
            observed.CanonicalTask.ExecutionId != binding.CanonicalTask.ExecutionId ||
            observed.CanonicalTask.ContextId != binding.CanonicalTask.ContextId ||
            observed.CanonicalTask.ContextId != binding.Conversation.Conversation.Id)
            throw new UnauthorizedAccessException("The actual source returned a different canonical Task or project.");
    }

    private sealed record OriginalAssistantDevelopmentSource(AssistantsNativeCuiSurface Surface, NativeAssistantsDesktopPage NativePage,
        IServiceProvider Provider, HomeNativeWindowsComposition Home, DenAssistantCanonicalBridge Bridge,
        DenAssistantOriginalDevelopmentOwner Owner, DeveloperProjectWorkbenchPageFactory Factory,
        Haven.Application.TaskExecutionCoordinator Tasks, DeveloperTaskWorkspaceService Development,
        FilesNativeBrowserService Files, HostLocalTaskActorSource Actors, HomePersonalDenFactory Den,
        CancellationToken AppLifetime, CancellationToken WindowLifetime, Window Window);
    private sealed record OriginalAssistantWorkbench(AssistantDevelopmentBinding Binding,
        IAssistantOriginalDevelopmentCustody Custody, DeveloperProjectWorkbenchPage Page);
}

#endif
