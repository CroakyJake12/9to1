#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Attachments;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Apps.Assistants.MiniComputer;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalAssistantConversationProcessOwner? _actualAssistantConversationProcess;
    private readonly List<OriginalAssistantsAcquisition> _actualAssistantAcquisitions = [];
    private readonly HashSet<Task> _actualAssistantAcquisitionSources = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _assistantControllerAcquisitions = new(1, 1);
    private OriginalAssistantsAcquisition? _currentAssistantAcquisition;
    private NativeAssistantsDesktopPage? _currentAssistantPresentation;
    private readonly List<NativeAssistantsDesktopPage> _actualAssistantPresentations = [];
    private bool _assistantOwnersTransferred;

    internal sealed record OriginalAssistantsAcquisition(
        OriginalAssistantPersonalDenHost OriginalDenHost,
        HomePersonalDenFactory OriginalDenFactory,
        HomePersonalDenSession OriginalOpenedDen,
        AssistantsWorkspaceController OriginalController)
    {
        internal IAssistantGeneratedUiHost? OriginalGeneratedUiHost { get; set; }
        internal ILegacyAgentMigrationController? OriginalMigration { get; set; }
        internal IAssistantMemoryManagementController? OriginalMemoryManagement { get; set; }
        internal IAssistantMiniComputerController? OriginalMiniComputerManagement { get; set; }
    }

    private OriginalAssistantConversationProcessOwner RetainOriginalAssistantConversationProcess()
    {
        if (_actualAssistantConversationProcess is not null)
            throw new InvalidOperationException("The actual Assistants process owner was already retained.");
        // Its constructor captures a real App-owned business CTS and the SAME
        // ordinary host, but performs no Chat/Den/profile/DI acquisition.
        return _actualAssistantConversationProcess = new();
    }

    internal Task<OriginalAssistantsAcquisition?> CreateOriginalAssistantsControllerAsync(
        IServiceProvider provider, HomeNativeWindowsComposition home, CancellationToken caller) =>
        _originalAppWork.RunAsync<OriginalAssistantsAcquisition?>(async original =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
            var token = lifetime.Token;
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => _assistantControllerAcquisitions.WaitAsync(token)));
            try
            {
                void DemandCurrent()
                {
                    caller.ThrowIfCancellationRequested(); original.DemandPublication();
                    if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
                        home.OriginalStartTask?.IsCompletedSuccessfully != true || home.OriginalCloseTask is not null ||
                        home.OriginalProcessRetirementRequestTask is not null)
                        throw new UnauthorizedAccessException("Use the SAME started original Windows Home and App provider.");
                    WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
                }
                DemandCurrent();
                var dependency = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                    () => ObserveOriginalAssistantSpacesDependencyAsync(original, provider, home, token)));
                DemandCurrent();
                if (!dependency.IsObserved) return null; // Genuine pre-effect setup outcome; no Den/controller was acquired.
                var host = _actualAssistantPersonalDen
                    ?? throw new InvalidOperationException("The actual personal Den owner is unavailable.");
                var ordinary = _actualAssistantConversationProcess
                    ?? throw new InvalidOperationException("The actual ordinary Chat process owner is unavailable.");
                void Scope(Action source) => AcquireOriginalAppSynchronous(original, () =>
                { DemandCurrent(); token.ThrowIfCancellationRequested(); source(); return true; });
                void Retain(Task actual)
                {
                    ArgumentNullException.ThrowIfNull(actual);
                    lock (_actualStartupAcquisitionGate) _actualAssistantAcquisitionSources.Add(actual);
                }
                var actorTask = AcquireOriginalAppSynchronous(original,
                    () => home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask());
                var actor = await original.AwaitAsync(actorTask)
                    ?? throw new UnauthorizedAccessException("A current actual personal Home actor is required.");
                DemandCurrent();
                if (actor.AccountId is not null || actor.OrganisationId is not null)
                    throw new UnauthorizedAccessException("Native personal Den setup does not infer account or organisation ownership.");

                // Authorized owning App setup may create only the actually missing
                // configured empty location. Existing manifests use genuine Open;
                // existing unbound/corrupt stores remain refused for Home recovery/import.
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                    () => host.OpenOrCreateMissingOwnedOriginalAsync(actor, home.LocalStoreOwnership, token)));
                DemandCurrent();
                var factory = AcquireOriginalAppSynchronous(original, provider.GetRequiredService<HomePersonalDenFactory>);
                var actualOpen = AcquireOriginalAppSynchronous(original, () => factory.OpenWithinOriginalSourceAsync(Scope, Retain, token));
                var opened = await original.AwaitAsync(actualOpen);
                DemandCurrent();
                var currentTask = AcquireOriginalAppSynchronous(original,
                    () => home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask());
                if (await original.AwaitAsync(currentTask) != opened.Actor || opened.Actor != actor)
                    throw new UnauthorizedAccessException("The actual Home actor changed during personal Den acquisition.");
                DemandCurrent();
                return AcquireOriginalAppSynchronous(original, () =>
                {
                    DemandCurrent();
                    if (!ReferenceEquals(provider.GetRequiredService<OriginalAssistantPersonalDenHost>(), host) ||
                        !ReferenceEquals(provider.GetRequiredService<OriginalAssistantConversationProcessOwner>(), ordinary) ||
                        !ReferenceEquals(provider.GetRequiredService<HavenOS.Apps.Assistants.Canonical.AssistantOriginalConversationHost>(), ordinary.OriginalHost))
                        throw new UnauthorizedAccessException("Use the SAME retained original Assistants App owners.");
                    OriginalAssistantsAcquisition? previous;
                    lock (_actualStartupAcquisitionGate) previous = _currentAssistantAcquisition;
                    if (previous is not null)
                    {
                        NativeAssistantsDesktopPage? previousPage;
                        lock (_actualStartupAcquisitionGate) previousPage = _currentAssistantPresentation;
                        var previousPageClose = previousPage?.OriginalClose;
                        if (previousPageClose is not null && !previousPageClose.IsCompletedSuccessfully)
                            throw new InvalidOperationException("The SAME native Assistant page close remains pending or failed. Preserve the actual page and acquisition.");
                        var previousClose = previous.OriginalController.OriginalClose;
                        var previousMiniComputerClose = previous.OriginalMiniComputerManagement?.OriginalClose;
                        if (previousMiniComputerClose is not null &&
                            (!previousMiniComputerClose.IsCompletedSuccessfully || previousClose?.IsCompletedSuccessfully != true))
                            throw new InvalidOperationException("The SAME original Mini Computer/Core acquisition has not settled; preserve its actual owners.");
                        var previousMemoryClose = previous.OriginalMemoryManagement?.OriginalClose;
                        if (previousMemoryClose is not null &&
                            (!previousMemoryClose.IsCompletedSuccessfully || previousClose?.IsCompletedSuccessfully != true))
                            throw new InvalidOperationException("The SAME original memory/Core acquisition has not both settled; preserve its actual owners.");
                        var previousMigrationClose = previous.OriginalMigration?.OriginalClose;
                        if (previousMigrationClose is not null &&
                            (!previousMigrationClose.IsCompletedSuccessfully || previousClose?.IsCompletedSuccessfully != true))
                            throw new InvalidOperationException("The SAME original migration/Core acquisition has not both settled; preserve its actual owners.");
                        if (previousPage is not null &&
                            ((previousPageClose is null && previousClose is not null) ||
                             (previousPageClose?.IsCompletedSuccessfully == true && previousClose?.IsCompletedSuccessfully != true)))
                            throw new InvalidOperationException("The actual native Assistant page and controller have not both settled their SAME close originals.");
                        if (previousClose is null)
                        {
                            if (previous.OriginalController.Snapshot.IsRetiring ||
                                !ReferenceEquals(previous.OriginalDenHost, host) ||
                                !ReferenceEquals(previous.OriginalDenFactory, factory) ||
                                previous.OriginalOpenedDen.Actor != opened.Actor ||
                                previous.OriginalOpenedDen.DenId != opened.DenId)
                                throw new UnauthorizedAccessException("The original Assistant presentation acquisition is no longer current.");
                            // Revalidated actual Home/Den authority above, then reuse
                            // the exact prior tuple/controller held by the live native page.
                            DemandCurrent();
                            return previous;
                        }
                        if (!previousClose.IsCompletedSuccessfully)
                            throw new InvalidOperationException("The original Assistant controller close remains pending or failed. Inspect the SAME actual owner before reopening.");
                    }
                    var memory = provider.GetRequiredService<AssistantOriginalMemorySource>();
                    DemandOriginalAssistantMemoryComposition(provider, home, factory, memory);
                    var capabilities = provider.GetRequiredService<AssistantOriginalCapabilityOwner>();
                    DemandOriginalAssistantCapabilityComposition(provider, home, factory, capabilities);
                    if (!ReferenceEquals(provider.GetRequiredService<IAssistantOriginalCapabilityOwner>(), capabilities))
                        throw new UnauthorizedAccessException("Retain the SAME actual capability source interface and concrete owner.");
                    var attachments = provider.GetRequiredService<AssistantOriginalAttachmentSource>();
                    DemandOriginalAssistantAttachmentComposition(provider, home, factory, attachments);
                    if (!ReferenceEquals(provider.GetRequiredService<IAssistantOriginalAttachmentOwner>(), attachments) ||
                        !ReferenceEquals(provider.GetRequiredService<IAssistantOriginalAttachmentCommandSource>(), attachments))
                        throw new UnauthorizedAccessException("Use the SAME configured original attachment owner and command issuer.");
                    var controller = AssistantsWorkspaceFactory.Create(factory,
                        provider.GetRequiredService<IConversationRepository>(),
                        provider.GetRequiredService<IConversationProductionRepository>(),
                        provider.GetRequiredService<ChatSessionService>(),
                        provider.GetRequiredService<TaskExecutionCoordinator>(), ordinary.OriginalHost,
                        provider.GetRequiredService<IAssistantOriginalModelSelectionOwner>(),
                        attachments,
                        provider.GetService<IAssistantOriginalDevelopmentOwner>(),
                        capabilities, memory);
                    var acquisition = new OriginalAssistantsAcquisition(host, factory, opened, controller);
                    // Retain the SAME actual scoped controller before any final
                    // freshness check or page/window construction can fail.
                    lock (_actualStartupAcquisitionGate)
                    {
                        _actualAssistantAcquisitions.Add(acquisition);
                        _currentAssistantAcquisition = acquisition;
                        _currentAssistantPresentation = null;
                    }
                    acquisition.OriginalGeneratedUiHost = AcquireOriginalAssistantGeneratedUiHost(provider, controller,
                        actual => acquisition.OriginalGeneratedUiHost = actual);
                    acquisition.OriginalMemoryManagement = new AssistantMemoryManagementController(memory, controller.OriginalCanonicalBridge);
                    acquisition.OriginalMiniComputerManagement = AcquireOriginalAssistantMiniComputerManagement(provider,
                        home, factory, controller); // Actual tuple retained before any page/native callbacks.
                    if (_actualAssistantLegacySource is { } legacy)
                    {
                        var imports = AcquireOriginalAssistantLegacyImportSession(provider, home, legacy);
                        acquisition.OriginalMigration = new LegacyAgentMigrationController(legacy, factory, home.Ownership,
                            controller.OriginalCanonicalBridge, imports); // SAME controller tuple was retained before any migration/page callback.
                    }
                    DemandCurrent();
                    return acquisition;
                });
            }
            finally { AcquireOriginalAppSynchronous(original, () => { _assistantControllerAcquisitions.Release(); return true; }); }
        });

    internal void RetainOriginalAssistantsPresentation(AssistantsWorkspaceController controller,
        NativeAssistantsDesktopPage actualPage)
    {
        ArgumentNullException.ThrowIfNull(controller); ArgumentNullException.ThrowIfNull(actualPage);
        lock (_actualStartupAcquisitionGate)
        {
            if (_currentAssistantAcquisition is not { } actual ||
                !ReferenceEquals(actual.OriginalController, controller) ||
                !ReferenceEquals(actualPage.OriginalController, controller) ||
                !ReferenceEquals(actualPage.OriginalGeneratedUiHost, actual.OriginalGeneratedUiHost) ||
                !ReferenceEquals(actualPage.OriginalMiniComputerManagementController, actual.OriginalMiniComputerManagement) ||
                !ReferenceEquals(actualPage.OriginalMemoryManagementController, actual.OriginalMemoryManagement) ||
                !ReferenceEquals(actualPage.OriginalMigration, actual.OriginalMigration))
                throw new UnauthorizedAccessException("Retain the actual page issued for the SAME App-owned Assistant acquisition.");
            if (_currentAssistantPresentation is { } previous && !ReferenceEquals(previous, actualPage) &&
                previous.OriginalClose?.IsCompletedSuccessfully != true)
                throw new InvalidOperationException("The actual prior Assistant page has not settled its SAME original close.");
            if (!_actualAssistantPresentations.Any(previous => ReferenceEquals(previous, actualPage)))
                _actualAssistantPresentations.Add(actualPage);
            _currentAssistantPresentation = actualPage;
            // Existing accepted capture cannot be dropped merely because App
            // admission changed. No new page, authority or source is acquired here.
        }
    }

    private void DemandOriginalAssistantRetirementJoin()
    {
        _actualAssistantConversationProcess?.DemandExternalOriginalRetirementJoin();
        _actualAssistantSpacesDependency?.DemandExternalOriginalRetirementJoin();
        DemandOriginalAssistantPersonalDenRetirementJoin();
        OriginalAssistantsAcquisition[] actuals;
        NativeAssistantsDesktopPage[] pages;
        lock (_actualStartupAcquisitionGate)
        { actuals = _actualAssistantAcquisitions.ToArray(); pages = _actualAssistantPresentations.ToArray(); }
        foreach (var page in pages) page.DemandExternalOriginalRetirementJoin();
        foreach (var actual in actuals)
        {
            actual.OriginalGeneratedUiHost?.DemandExternalOriginalRetirementJoin();
            actual.OriginalMiniComputerManagement?.DemandExternalOriginalRetirementJoin();
            actual.OriginalMemoryManagement?.DemandExternalOriginalRetirementJoin();
            actual.OriginalMigration?.DemandExternalOriginalRetirementJoin();
            actual.OriginalController.DemandExternalOriginalRetirementJoin();
        }
    }

    private void RequestOriginalAssistantScopeRetirement(List<Exception> failures)
    {
        RequestOriginalHomeApplicationLaunchRetirement(failures);
        RequestOriginalAssistantMiniComputerPendingReviewWithdrawals(failures);
        RequestOriginalAssistantAttachmentPendingWithdrawals(failures);
        RequestOriginalGeneratedUiPendingWithdrawals(failures);
        OriginalAssistantsAcquisition[] actuals;
        NativeAssistantsDesktopPage[] pages;
        lock (_actualStartupAcquisitionGate)
        { actuals = _actualAssistantAcquisitions.ToArray(); pages = _actualAssistantPresentations.ToArray(); }
        foreach (var page in pages)
            try { page.RequestRetirement(); }
            catch (Exception cause) { AddAppCause(failures, cause); }
        void Request(Action request)
        {
            try { _originalAppWork.RunCloseCallback(request); }
            catch (Exception cause) { AddAppCause(failures, cause); }
        }
        foreach (var actual in actuals)
        {
            if (actual.OriginalGeneratedUiHost is { } generated) Request(generated.RequestRetirement);
            if (actual.OriginalMiniComputerManagement is { } mini) Request(mini.RequestRetirement);
            if (actual.OriginalMemoryManagement is { } memory) Request(memory.RequestRetirement);
            if (actual.OriginalMigration is { } migration) Request(migration.RequestRetirement);
            Request(actual.OriginalController.RequestRetirement);
            // Core retains its borrowed bridge until actual CloseAndDrain. Memory
            // and migration each join before that close, including partial pages.
        }
    }

    private async Task JoinOriginalAssistantScopesAfterPresentationsAsync()
    {
        var pendingFailures = new List<Exception>();
        RequestOriginalAssistantMiniComputerPendingReviewWithdrawals(pendingFailures);
        RequestOriginalAssistantAttachmentPendingWithdrawals(pendingFailures);
        RequestOriginalGeneratedUiPendingWithdrawals(pendingFailures);
        OriginalAssistantsAcquisition[] actuals;
        NativeAssistantsDesktopPage[] pages;
        Task[] sources;
        lock (_actualStartupAcquisitionGate)
        {
            actuals = _actualAssistantAcquisitions.ToArray();
            pages = _actualAssistantPresentations.ToArray();
            sources = _actualAssistantAcquisitionSources.ToArray();
        }
        var failures = pendingFailures; var originals = new List<Task>();
        foreach (var actual in sources) await JoinOriginalAppTaskAsync(actual, failures);
        foreach (var page in pages)
            try { originals.Add(page.CloseAndDrainAsync()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
        foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        originals.Clear();
        foreach (var actual in actuals)
        {
            var borrowerHealthy = true;
            if (actual.OriginalGeneratedUiHost is { } generated)
            {
                Task? sameGeneratedClose = null;
                try { _originalAppWork.RunCloseCallback(() => sameGeneratedClose = generated.CloseAndDrainAsync());
                    if (sameGeneratedClose is not null) lock (_actualStartupAcquisitionGate) _actualAssistantAcquisitionSources.Add(sameGeneratedClose); }
                catch (Exception cause) { AddAppCause(failures, cause); }
                if (sameGeneratedClose is not null) await JoinOriginalAppTaskAsync(sameGeneratedClose, failures);
                borrowerHealthy &= sameGeneratedClose?.IsCompletedSuccessfully == true;
            }
            if (actual.OriginalMiniComputerManagement is { } mini)
            {
                Task? sameMiniClose = null;
                try { _originalAppWork.RunCloseCallback(() => sameMiniClose = mini.CloseAndDrainAsync()); }
                catch (Exception cause) { AddAppCause(failures, cause); }
                if (sameMiniClose is not null) await JoinOriginalAppTaskAsync(sameMiniClose, failures);
                borrowerHealthy &= sameMiniClose?.IsCompletedSuccessfully == true;
            }
            if (actual.OriginalMemoryManagement is { } memory)
            {
                Task? sameMemoryClose = null;
                try { _originalAppWork.RunCloseCallback(() => sameMemoryClose = memory.CloseAndDrainAsync()); }
                catch (Exception cause) { AddAppCause(failures, cause); }
                if (sameMemoryClose is not null) await JoinOriginalAppTaskAsync(sameMemoryClose, failures);
                borrowerHealthy &= sameMemoryClose?.IsCompletedSuccessfully == true;
            }
            if (actual.OriginalMigration is { } migration)
            {
                Task? sameMigrationClose = null;
                try { sameMigrationClose = migration.CloseAndDrainAsync(); }
                catch (Exception cause) { AddAppCause(failures, cause); }
                if (sameMigrationClose is not null) await JoinOriginalAppTaskAsync(sameMigrationClose, failures);
                borrowerHealthy &= sameMigrationClose?.IsCompletedSuccessfully == true;
            }
            if (!borrowerHealthy) continue; // SAME Core remains alive on any unresolved presentation borrower.
            try { originals.Add(actual.OriginalController.CloseAndDrainAsync()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
        }
        foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures);
    }

    private Task JoinOriginalUntransferredAssistantBusinessAsync()
    {
        bool transferred;
        OriginalAssistantConversationProcessOwner? actual;
        lock (_actualStartupAcquisitionGate)
        { transferred = _assistantOwnersTransferred; actual = _actualAssistantConversationProcess; }
        // Actual token callbacks execute outside ALL App acquisition locks.
        return transferred ? Task.CompletedTask : actual?.CloseAndDrainAsync() ?? Task.CompletedTask;
    }

    private Task JoinOriginalUntransferredAssistantScopesAsync()
    {
        bool transferred;
        lock (_actualStartupAcquisitionGate) transferred = _assistantOwnersTransferred;
        return transferred ? Task.CompletedTask : JoinOriginalAssistantScopesAfterPresentationsAsync();
    }

    private Task JoinOriginalUntransferredAssistantPersonalDenAsync()
    {
        bool transferred;
        lock (_actualStartupAcquisitionGate) transferred = _assistantOwnersTransferred;
        return transferred ? Task.CompletedTask : JoinOriginalModelCataloguesThenDenAsync();
    }
}
#endif
