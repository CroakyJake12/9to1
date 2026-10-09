#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private SqliteDatabase? _actualAssistantSqliteDatabase;
    private ConversationRepository? _actualAssistantSqliteConversations;
    private ContainerRepository? _actualAssistantSqliteContainers;
    private CanonicalSqliteOriginalStoreOwner? _actualAssistantSqliteStore;
    private CanonicalSqliteOriginalStoreEvidenceProvider? _actualAssistantSqliteEvidence;
    private CanonicalProjectContextStoreReadOwner? _actualAssistantProjectContexts;
    private LegacySavedAgentSqliteSource? _actualAssistantLegacySource;
    private CanonicalProjectTaskContextCreateOwner? _actualAssistantTaskContextCreator;
    private HomeCompatibleTaskContextWriteSource? _actualAssistantTaskContextWrites;
    private DenAssistantOriginalDevelopmentOwner? _actualAssistantDevelopmentOwner;
    private HomeCompatibleTaskContextWriteResourceResolver? _actualAssistantTaskContextWriteResolver;
    private HomeCompatibleTaskContextWriteActionPolicy? _actualAssistantTaskContextWritePolicy;
    private Task? _actualAssistantDevelopmentInputDrain;
    private Task? _actualAssistantSqliteDrain;

    // Trusted App construction only: replace precisely the maintained uninstantiated
    // descriptors with SAME actual instances before Home evidence construction. The
    // maintained usage-tracking conversation wrapper and its aliases stay in place.
    private void PrepareOriginalAssistantSqliteComposition(IServiceCollection collection, AppPaths paths)
    {
        ServiceDescriptor Demand(Type service, Type implementation)
        {
            var rows = collection.Where(row => row.ServiceType == service).Take(2).ToArray();
            if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton ||
                rows[0].ImplementationType != implementation)
                throw new InvalidOperationException("Use the maintained uninstantiated canonical owner: " + service.Name);
            return rows[0];
        }
        var database = Demand(typeof(SqliteDatabase), typeof(SqliteDatabase));
        var conversations = Demand(typeof(ConversationRepository), typeof(ConversationRepository));
        var containers = Demand(typeof(IContainerRepository), typeof(ContainerRepository));
        if (collection.Any(row => row.ServiceType == typeof(ContainerRepository)))
            throw new InvalidOperationException("A concrete canonical container owner already exists.");
        _actualAssistantSqliteDatabase = new(paths);
        _actualAssistantSqliteConversations = new(_actualAssistantSqliteDatabase);
        _actualAssistantSqliteContainers = new(_actualAssistantSqliteDatabase, paths);
        collection.Remove(database); collection.AddSingleton(_actualAssistantSqliteDatabase);
        collection.Remove(conversations); collection.AddSingleton(_actualAssistantSqliteConversations);
        collection.Remove(containers); collection.AddSingleton(_actualAssistantSqliteContainers);
        collection.AddSingleton<IContainerRepository>(_actualAssistantSqliteContainers);
        _actualAssistantTaskContextWriteResolver = new(() => _actualAssistantTaskContextWrites
            ?? throw new InvalidOperationException("The SAME original Tasks-context Home WRITE owner is not captured."));
        _actualAssistantTaskContextWritePolicy = new();
        PrepareOriginalAssistantMemoryPolicies();
        PrepareOriginalAssistantAttachmentPolicies();
        PrepareOriginalAssistantCapabilityInitializationPolicies();
        PrepareOriginalAutomationLibraryWritePolicies();
        PrepareOriginalGeneratedUiInteractionPolicies();
    }

    private IHomeLocalStoreEvidenceProvider[] CreateOriginalAssistantSqliteEvidence(HomeNativeWindowsIdentityComponents identity, AppPaths paths)
    {
        if (_actualAssistantSqliteStore is not null)
            throw new InvalidOperationException("The actual canonical SQLite owner was already constructed.");
        _actualAssistantSqliteStore = new(_actualAssistantSqliteDatabase!, paths, identity.Profiles);
        _actualAssistantSqliteEvidence = new(_actualAssistantSqliteStore, CanonicalProjectContextStoreReadOwner.CanonicalResourceKind);
        _actualAssistantLegacySource = new(_actualAssistantSqliteStore, identity.Profiles);
        // These separate resource kinds share physical identity, never a Home grant.
        return [_actualAssistantSqliteEvidence, _actualAssistantLegacySource, CreateOriginalAssistantMemoryStoreEvidence()];
    }

    private CanonicalProjectContextStoreReadOwner CreateOriginalAssistantProjectContextReadOwner(HomeNativeWindowsOwnershipComponents ownership)
    {
        if (_actualAssistantProjectContexts is not null)
            throw new InvalidOperationException("The actual canonical metadata READ owner was already constructed.");
        return _actualAssistantProjectContexts = new(_actualAssistantSqliteStore!, _actualAssistantSqliteDatabase!,
            _actualAssistantSqliteConversations!, _actualAssistantSqliteContainers!, ownership.Ownership);
    }

    private void RegisterOriginalAssistantSqliteOwners(IServiceCollection collection)
    {
        collection.AddSingleton(_actualAssistantSqliteStore!);
        collection.AddSingleton(_actualAssistantProjectContexts!);
        collection.AddSingleton<ICanonicalProjectContextStoreReadSource>(_actualAssistantProjectContexts!);
        collection.AddSingleton(_actualAssistantLegacySource!);
    }

    private void ConfigureOriginalAssistantDevelopmentOwnerRegistrations(IServiceCollection collection)
    {
        ConfigureOriginalAssistantAttachmentOwnerRegistrations(collection);
        if (_actualWindowsHome is not { } home || !WindowsColdProjectRequested) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            Type[] owned = [typeof(CanonicalProjectTaskContextCreateOwner), typeof(ICanonicalProjectTaskContextCreateSource),
                typeof(HomeCompatibleTaskContextWriteSource), typeof(ICanonicalProjectTaskContextHomeWriteSource),
                typeof(DenAssistantOriginalDevelopmentOwner), typeof(IAssistantOriginalDevelopmentOwner),
                typeof(ICanonicalProjectTaskContextResumeCreateSource), typeof(ICanonicalProjectTaskContextResumeSelectionSource)];
            if (collection.Any(row => owned.Contains(row.ServiceType)))
                throw new InvalidOperationException("The original Assistant project creation/development owners cannot be replaced.");
            collection.AddSingleton<CanonicalProjectTaskContextCreateOwner>(provider =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                return _actualAssistantTaskContextCreator = new(provider.GetRequiredService<CanonicalProjectContextStoreReadOwner>(),
                    provider.GetRequiredService<CanonicalSqliteOriginalStoreOwner>(), provider.GetRequiredService<HomeColdProjectReadReconciliation>());
            });
            collection.AddSingleton<ICanonicalProjectTaskContextCreateSource>(provider => provider.GetRequiredService<CanonicalProjectTaskContextCreateOwner>());
            collection.AddSingleton<ICanonicalProjectTaskContextResumeCreateSource>(provider => provider.GetRequiredService<CanonicalProjectTaskContextCreateOwner>());
            collection.AddSingleton<HomeCompatibleTaskContextWriteSource>(provider =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                var creator = provider.GetRequiredService<CanonicalProjectTaskContextCreateOwner>();
                var writes = _actualAssistantTaskContextWrites = new(home.StateStore, home.Profiles, home.Resources,
                    home.Broker, home.Permissions, provider.GetRequiredService<HomeColdProjectReadReconciliation>(), creator);
                creator.BindOriginalHomeWriteSource(writes);
                if (!_actualAssistantTaskContextWriteResolver!.IsBoundToOriginalOwner(writes))
                    throw new InvalidOperationException("The actual preconfigured Home WRITE resolver changed.");
                return writes;
            });
            collection.AddSingleton<ICanonicalProjectTaskContextHomeWriteSource>(provider => provider.GetRequiredService<HomeCompatibleTaskContextWriteSource>());
            collection.AddSingleton<DenAssistantOriginalDevelopmentOwner>(provider =>
            {
                _ = provider.GetRequiredService<HomeCompatibleTaskContextWriteSource>();
                var creator = provider.GetRequiredService<CanonicalProjectTaskContextCreateOwner>();
                var actual = _actualAssistantDevelopmentOwner = new DenAssistantOriginalDevelopmentOwner(provider.GetRequiredService<HomePersonalDenFactory>(),
                    provider.GetRequiredService<IConversationRepository>(), provider.GetRequiredService<IContainerRepository>(),
                    provider.GetRequiredService<TaskExecutionCoordinator>(), provider.GetRequiredService<DeveloperTaskWorkspaceService>(),
                    provider.GetRequiredService<HostLocalTaskActorSource>(), provider.GetRequiredService<HomeColdProjectReadReconciliation>(),
                    provider.GetRequiredService<ITaskRunColdRecoveryJournal>(), provider.GetRequiredService<ITaskRunColdContextAuthority>(),
                    provider.GetRequiredService<ICanonicalProjectContextStoreReadSource>(), creator);
                // Pair the independent process issuer only after genuine Den setup.
                // Creator construction never resolves the development issuer eagerly.
                creator.BindOriginalResumeSelectionSource(actual);
                if (!ReferenceEquals(creator.OriginalResumeSelectionSource, actual) ||
                    !ReferenceEquals(actual.OriginalCompatibleTaskContextCreateOwner, creator))
                    throw new InvalidOperationException("The SAME process development issuer and canonical creator resume source are required.");
                return actual;
            });
            collection.AddSingleton<IAssistantOriginalDevelopmentOwner>(provider => provider.GetRequiredService<DenAssistantOriginalDevelopmentOwner>());
            collection.AddSingleton<ICanonicalProjectTaskContextResumeSelectionSource>(provider => provider.GetRequiredService<DenAssistantOriginalDevelopmentOwner>());
            return true;
        }));
    }

    private void CaptureOriginalAssistantDevelopmentOwners(IServiceProvider provider)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            _ = provider.RequireOriginalWindowsHomeComponents(home); original.DemandPublication();
            if (!ReferenceEquals(provider.GetRequiredService<SqliteDatabase>(), _actualAssistantSqliteDatabase) ||
                !ReferenceEquals(provider.GetRequiredService<ConversationRepository>(), _actualAssistantSqliteConversations) ||
                provider.GetRequiredService<IConversationRepository>() is not UsageTrackingConversationRepository conversations ||
                !conversations.IsBoundToOriginalRepository(_actualAssistantSqliteConversations!) ||
                !ReferenceEquals(provider.GetRequiredService<IContainerRepository>(), _actualAssistantSqliteContainers) ||
                !ReferenceEquals(provider.GetRequiredService<ICanonicalProjectContextStoreReadSource>(), _actualAssistantProjectContexts) ||
                !_actualAssistantSqliteStore!.HasOriginalComposition(_actualAssistantSqliteDatabase!, provider.GetRequiredService<IAppPaths>(), home.Profiles))
                throw new UnauthorizedAccessException("The SAME actual canonical App/Home/store/repository composition is required.");
            if (WindowsColdProjectRequested)
            {
                // Source constructors perform no Den open. The development issuer is
                // deliberately still lazy: its SAME HomePersonalDenFactory cannot be
                // resolved until the real dependency/actor/store acquisition below.
                var creator = provider.GetRequiredService<CanonicalProjectTaskContextCreateOwner>();
                var writes = provider.GetRequiredService<HomeCompatibleTaskContextWriteSource>();
                if (!ReferenceEquals(creator, _actualAssistantTaskContextCreator) ||
                    !ReferenceEquals(creator.OriginalContextReadOwner, _actualAssistantProjectContexts) ||
                    !ReferenceEquals(creator.OriginalProjectReadSource, _windowsColdProjects) ||
                    !ReferenceEquals(creator.OriginalHomeWriteSource, writes) ||
                    !ReferenceEquals(writes, _actualAssistantTaskContextWrites))
                    throw new UnauthorizedAccessException("The SAME actual Assistant project/READ/creation/WRITE issuers are required.");
            }
            original.DemandPublication(); return true;
        }));
    }

    private void DemandOriginalAssistantDevelopmentInputsJoin()
    {
        DemandOriginalAutomationLibraryWriteJoin();
        DemandOriginalGeneratedUiInteractionJoin();
        DemandOriginalAssistantMiniComputerInputsJoin();
        DemandOriginalAssistantMemoryInputsJoin();
        DemandOriginalAssistantAttachmentRetirementJoin();
        DemandOriginalAssistantCapabilityInputsJoin();
        _actualAssistantLegacyImportSession?.DemandExternalOriginalRetirementJoin();
        _actualAssistantDevelopmentOwner?.DemandExternalOriginalRetirementJoin();
        _actualAssistantTaskContextCreator?.DemandExternalOriginalJoin();
        _actualAssistantTaskContextWrites?.DemandExternalOriginalJoin();
        _actualAssistantSqliteStore?.DemandExternalOriginalRetirementJoin();
    }

    private Task JoinOriginalAssistantDevelopmentInputsAsync()
    {
        DemandOriginalAssistantDevelopmentInputsJoin();
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantDevelopmentInputDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAssistantDevelopmentInputDrain = JoinOriginalAssistantDevelopmentInputsCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }
    private async Task JoinOriginalAssistantDevelopmentInputsCoreAsync(Task start)
    {
        await start;
        // Business, native Dev pages and all presentation commands have already joined.
        // Accepted creator commands settle their SAME claims before global WRITE seals.
        var failures = new List<Exception>();
        async Task Join(Func<Task> acquire)
        {
            Task? original = null;
            try { _originalAppWork.RunCloseCallback(() => original = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (original is not null) await JoinOriginalAppTaskAsync(original, failures);
        }
        // Finite capability queries borrow the SAME Chat/project input. They must
        // settle before the independent input issuer or protected store can retire.
        if (_actualAssistantCapabilityOwner is { } capabilities) await Join(capabilities.CloseAndDrainAsync);
        ThrowAppCauses(failures);
        // Setup owns process SQL/Home, not the retired presentation delivery.
        // Withdraw only its unresolved reviews and join any accepted commit
        // before borrowed protected-store/Home owners can retire.
        await JoinOriginalAssistantCapabilityInitializationAfterBorrowersAsync();
        await JoinOriginalGeneratedUiInteractionsAfterViewsAsync();
        await JoinOriginalAutomationLibraryWritesAfterViewsAsync();
        await JoinOriginalAssistantMiniComputerAfterBorrowersAsync();
        if (_actualAssistantDevelopmentOwner is { } development) await Join(development.CloseAndDrainAsync);
        if (_actualAssistantTaskContextCreator is { } creator) await Join(creator.CloseAndDrainOriginalAsync);
        if (_actualAssistantTaskContextWrites is { } writes) await Join(writes.CloseAndDrainOriginalAsync);
        ThrowAppCauses(failures); // Unknowns remain failures; downstream READ/SQLite/Home stays live.
        await JoinOriginalAssistantAttachmentsAfterBorrowersAsync();
        await JoinOriginalAssistantMemoryAfterBorrowersAsync();
    }

    private Task JoinOriginalAssistantSqliteAfterBorrowersAsync()
    {
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantSqliteDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAssistantSqliteDrain = JoinOriginalAssistantSqliteAfterBorrowersCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }
    private async Task JoinOriginalAssistantSqliteAfterBorrowersCoreAsync(Task start)
    {
        await start;
        // All business/Dev/creator/WRITE/current project inputs joined successfully.
        // A failure retains downstream SQLite/Home custody instead of claiming a close.
        var failures = new List<Exception>();
        async Task Join(Func<Task> acquire)
        {
            Task? original = null;
            try { _originalAppWork.RunCloseCallback(() => original = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (original is not null) await JoinOriginalAppTaskAsync(original, failures);
            ThrowAppCauses(failures); // A failed borrower keeps the SAME downstream store/Home alive.
        }
        // All scoped migration commands/views have joined. The process session
        // may span view reopen, and must settle its SAME Home originals before
        // its borrowed legacy source or protected store can retire.
        if (_actualAssistantLegacyImportSession is { } imports) await Join(imports.CloseAndDrainAsync);
        if (_actualAssistantLegacySource is { } legacy) await Join(legacy.CloseAndDrainAsync);
        if (_actualAssistantSqliteStore is { } store) await Join(store.CloseAndDrainAsync);
    }
}
#endif
