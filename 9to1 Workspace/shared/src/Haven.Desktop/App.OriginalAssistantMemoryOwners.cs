#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private AssistantOriginalMemorySource? _actualAssistantMemorySource;
    private HomeCanonicalAssistantMemoryWriteSource? _actualAssistantMemoryWrites;
    private CanonicalSqliteOriginalStoreEvidenceProvider? _actualAssistantMemoryEvidence;
    private HomeCanonicalAssistantMemoryWriteResourceResolver? _actualAssistantMemoryWriteResolver;
    private HomeCanonicalAssistantMemoryWriteActionPolicy? _actualAssistantMemoryWritePolicy;
    private Task? _actualAssistantMemoryDrain;

    private void PrepareOriginalAssistantMemoryPolicies()
    {
        _actualAssistantMemoryWriteResolver = new(() => _actualAssistantMemoryWrites
            ?? throw new InvalidOperationException("The SAME original Assistant memory Home WRITE owner is not captured."));
        _actualAssistantMemoryWritePolicy = new();
    }

    private IHomeLocalStoreEvidenceProvider CreateOriginalAssistantMemoryStoreEvidence() =>
        _actualAssistantMemoryEvidence = new(_actualAssistantSqliteStore!, AssistantOriginalMemorySource.ResourceKind);

    private void ConfigureOriginalAssistantMemoryOwnerRegistrations(IServiceCollection collection)
    {
        ConfigureOriginalGeneratedUiInteractionRegistrations(collection);
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            Type[] owned = [typeof(AssistantOriginalMemorySource), typeof(IAssistantOriginalPersistentMemoryInputOwner),
                typeof(IChatOriginalPersistentMemorySource), typeof(ICanonicalAssistantMemoryWriteSource),
                typeof(HomeCanonicalAssistantMemoryWriteSource), typeof(ICanonicalAssistantMemoryHomeWriteSource)];
            if (collection.Any(row => owned.Contains(row.ServiceType)))
                throw new InvalidOperationException("The SAME original Assistant memory and Home WRITE owners cannot be replaced.");
            collection.AddSingleton<AssistantOriginalMemorySource>(provider =>
            {
                _ = provider.RequireOriginalWindowsHomeComponents(home);
                if (_actualAssistantMemorySource is not null || _actualAssistantMemoryWrites is not null)
                    throw new InvalidOperationException("The actual memory acquisition already exists; retain its original custody.");
                // Only the actual installed dependency/actor/Den acquisition resolves
                // this factory. Chat startup and ordinary App routes do not open Den.
                var factory = provider.GetRequiredService<HomePersonalDenFactory>();
                var conversations = provider.GetRequiredService<IConversationRepository>();
                var knowledge = provider.GetRequiredService<KnowledgeLibraryService>();
                if (!ReferenceEquals(provider.GetRequiredService<IKnowledgeLibrary>(), knowledge) ||
                    !ReferenceEquals(provider.GetRequiredService<IMemoryQuerySource>(), knowledge) ||
                    !knowledge.HasOriginalAssistantMemoryStoreOwner(_actualAssistantSqliteStore!))
                    throw new UnauthorizedAccessException("Use the SAME maintained Knowledge Library and canonical protected SQLite owner.");
                var legacyMemory = _actualAssistantLegacySource;
                if (legacyMemory is not null &&
                    !legacyMemory.IsOriginalSource(_actualAssistantSqliteStore!, home.Profiles))
                    throw new UnauthorizedAccessException("Use the SAME retained legacy source, canonical protected store and Home profile for migrated memory.");
                var memory = _actualAssistantMemorySource = new(factory, conversations,
                    provider.GetRequiredService<TaskExecutionCoordinator>(), knowledge,
                    _actualAssistantSqliteStore!, home.Profiles, home.Ownership,
                    actualLegacyMemorySource: legacyMemory);
                // Retain the actual partial source before constructing/binding WRITE,
                // import delivery or Chat. A later failure keeps exact owners for cleanup.
                BindOriginalAssistantMemoryImportSession(provider, home, memory);
                var writes = _actualAssistantMemoryWrites = new(home.StateStore, home.Profiles,
                    home.Resources, home.Broker, home.Permissions, memory);
                memory.BindOriginalHomeWriteSource(writes);
                var chat = provider.GetRequiredService<ChatSessionService>();
                chat.BindOriginalPersistentMemorySource(memory);
                if (!chat.HasOriginalPersistentMemorySource(memory) ||
                    !memory.HasOriginalHomeWriteSource(writes) ||
                    !writes.HasOriginalComposition(home.StateStore, home.Profiles, memory) ||
                    !_actualAssistantMemoryWriteResolver!.IsBoundToOriginalOwner(writes))
                    throw new UnauthorizedAccessException("The SAME actual Chat, memory input and Home WRITE composition is required.");
                return memory;
            });
            // Chat obtains this exact source ONLY through its one-time explicit
            // Bind after genuine Den acquisition. No optional-constructor DI alias.
            collection.AddSingleton<IAssistantOriginalPersistentMemoryInputOwner>(provider => provider.GetRequiredService<AssistantOriginalMemorySource>());
            collection.AddSingleton<ICanonicalAssistantMemoryWriteSource>(provider => provider.GetRequiredService<AssistantOriginalMemorySource>());
            collection.AddSingleton<HomeCanonicalAssistantMemoryWriteSource>(provider =>
            {
                _ = provider.GetRequiredService<AssistantOriginalMemorySource>();
                return _actualAssistantMemoryWrites
                    ?? throw new InvalidOperationException("The actual Assistant memory WRITE acquisition is unavailable.");
            });
            collection.AddSingleton<ICanonicalAssistantMemoryHomeWriteSource>(provider => provider.GetRequiredService<HomeCanonicalAssistantMemoryWriteSource>());
            return true;
        }));
    }

    private void DemandOriginalAssistantMemoryComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, HomePersonalDenFactory factory, AssistantOriginalMemorySource memory)
    {
        if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(memory, _actualAssistantMemorySource) || memory.OriginalClose is not null ||
            !memory.HasOriginalComposition(factory, provider.GetRequiredService<IConversationRepository>()) ||
            !ReferenceEquals(provider.GetRequiredService<IAssistantOriginalPersistentMemoryInputOwner>(), memory) ||
            !ReferenceEquals(provider.GetRequiredService<ICanonicalAssistantMemoryWriteSource>(), memory) ||
            !provider.GetRequiredService<ChatSessionService>().HasOriginalPersistentMemorySource(memory) ||
            _actualAssistantMemoryWrites is not { } writes ||
            !ReferenceEquals(provider.GetRequiredService<HomeCanonicalAssistantMemoryWriteSource>(), writes) ||
            !ReferenceEquals(provider.GetRequiredService<ICanonicalAssistantMemoryHomeWriteSource>(), writes) ||
            !writes.HasOriginalComposition(home.StateStore, home.Profiles, memory) ||
            !memory.HasOriginalHomeWriteSource(writes) ||
            !_actualAssistantMemoryWriteResolver!.IsBoundToOriginalOwner(writes) ||
            _actualAssistantMemoryImportSession is not { } imports || imports.OriginalClose is not null ||
            !memory.HasOriginalMemoryImportSession(imports) ||
            !imports.IsOriginalSource(memory, _actualAssistantMemoryEvidence!, home.Profiles, home.Ownership))
            throw new UnauthorizedAccessException("Retain the SAME actual App/Home/Den/Chat/memory/WRITE owners.");
    }

    private void DemandOriginalAssistantMemoryInputsJoin()
    {
        _actualAssistantMemorySource?.DemandExternalOriginalRetirementJoin();
        _actualAssistantMemoryWrites?.DemandExternalOriginalJoin();
        _actualAssistantMemoryImportSession?.DemandExternalOriginalRetirementJoin();
    }

    private Task JoinOriginalAssistantMemoryAfterBorrowersAsync()
    {
        DemandOriginalAssistantMemoryInputsJoin();
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantMemoryDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAssistantMemoryDrain = JoinOriginalAssistantMemoryAfterBorrowersCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }

    private async Task JoinOriginalAssistantMemoryAfterBorrowersCoreAsync(Task start)
    {
        await start;
        OriginalAssistantsAcquisition[] acquisitions;
        lock (_actualStartupAcquisitionGate) acquisitions = _actualAssistantAcquisitions.ToArray();
        // Failed view cleanup cannot lend permission to close its borrowed source.
        // The process business/Task and presentation cohorts have already joined.
        NativeAssistantsDesktopPage[] pages;
        lock (_actualStartupAcquisitionGate) pages = _actualAssistantPresentations.ToArray();
        if ((_actualAssistantConversationProcess is { } business && business.OriginalClose?.IsCompletedSuccessfully != true) ||
            pages.Any(page => page.OriginalClose?.IsCompletedSuccessfully != true) ||
            acquisitions.Any(actual => actual.OriginalController.OriginalClose?.IsCompletedSuccessfully != true ||
                (actual.OriginalMemoryManagement is { } memory && memory.OriginalClose?.IsCompletedSuccessfully != true)))
            throw new InvalidOperationException("The actual Assistant memory presentation borrowers have not settled; retain the SAME memory/WRITE/store/Home owners.");
        var failures = new List<Exception>();
        async Task Join(Func<Task> acquire)
        {
            Task? original = null;
            try { _originalAppWork.RunCloseCallback(() => original = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (original is not null) await JoinOriginalAppTaskAsync(original, failures);
            ThrowAppCauses(failures); // Failed memory source keeps WRITE/SQLite/Home live.
        }
        if (_actualAssistantMemorySource is { } source) await Join(source.CloseAndDrainAsync);
        if (_actualAssistantMemoryWrites is { } writes) await Join(writes.CloseAndDrainOriginalAsync);
        // Borrowers and source drivers settled before the process import session.
        // It retains its own review/audit originals across presentation reopen.
        if (_actualAssistantMemoryImportSession is { } imports) await Join(imports.CloseAndDrainAsync);
    }
}
#endif
