#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private CanonicalCapabilityCatalogueInitializationOwner? _actualAssistantCapabilityInitialization;
    private HomeCapabilityCatalogueInitializationWriteSource? _actualAssistantCapabilityInitializationWrites;
    private HomeCapabilityCatalogueInitializationResourceResolver? _actualAssistantCapabilityInitializationResolver;
    private HomeCapabilityCatalogueInitializationActionPolicySource? _actualAssistantCapabilityInitializationPolicy;
    private Task? _actualAssistantCapabilityInitializationDrain;

    private void PrepareOriginalAssistantCapabilityInitializationPolicies()
    {
        _actualAssistantCapabilityInitializationResolver = new(() => _actualAssistantCapabilityInitializationWrites
            ?? throw new InvalidOperationException("The SAME capability setup Home WRITE owner has not been captured."));
        _actualAssistantCapabilityInitializationPolicy = new();
    }

    private void ConfigureOriginalAssistantCapabilityInitializationRegistrations(IServiceCollection collection,
        HomeNativeWindowsComposition home)
    {
        // Construction is lazy and has no READ, discovery, schema, seed or Den IO.
        // The actual saved-definition acquisition resolves this SAME process owner.
        collection.AddSingleton<CanonicalCapabilityCatalogueInitializationOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
        {
            _ = provider.RequireOriginalWindowsHomeComponents(home);
            if (_actualAssistantCapabilityInitialization is not null || _actualAssistantCapabilityInitializationWrites is not null)
                throw new InvalidOperationException("Retain the actual setup source and its SAME original Home WRITE close.");
            var catalogue = provider.GetRequiredService<CanonicalCapabilityCatalogueReadOwner>();
            var actual = _actualAssistantCapabilityInitialization = new(catalogue);
            // Retain each partial owner before the binding or any postguard can fail.
            var writes = _actualAssistantCapabilityInitializationWrites = new(home.StateStore, home.Profiles,
                home.Resources, home.Broker, home.Permissions, actual);
            actual.BindOriginalHomeWriteSource(writes);
            DemandOriginalAssistantCapabilityInitializationComposition(provider, home, catalogue, actual);
            return actual;
        }));
        collection.AddSingleton<ICapabilityOriginalInitializationSource>(provider =>
            provider.GetRequiredService<CanonicalCapabilityCatalogueInitializationOwner>());
        collection.AddSingleton<ICapabilityOriginalInitializationProcessSource>(provider =>
            provider.GetRequiredService<CanonicalCapabilityCatalogueInitializationOwner>());
        collection.AddSingleton<HomeCapabilityCatalogueInitializationWriteSource>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
        {
            _ = provider.GetRequiredService<CanonicalCapabilityCatalogueInitializationOwner>();
            return _actualAssistantCapabilityInitializationWrites ??
                throw new InvalidOperationException("The actual setup Home WRITE source was not retained.");
        }));
        collection.AddSingleton<ICapabilityOriginalInitializationHomeWriteSource>(provider =>
            provider.GetRequiredService<HomeCapabilityCatalogueInitializationWriteSource>());
        collection.AddSingleton<ICapabilityOriginalInitializationHomeReviewWithdrawalSource>(provider =>
            provider.GetRequiredService<HomeCapabilityCatalogueInitializationWriteSource>());
    }

    private void DemandOriginalAssistantCapabilityInitializationComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, CanonicalCapabilityCatalogueReadOwner catalogue,
        CanonicalCapabilityCatalogueInitializationOwner actual)
    {
        if (!ReferenceEquals(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(actual, _actualAssistantCapabilityInitialization) || actual.OriginalClose is not null ||
            !ReferenceEquals(catalogue, _actualAssistantCapabilityCatalogue) ||
            !ReferenceEquals(actual.OriginalCatalogue, catalogue) ||
            !ReferenceEquals(actual.OriginalStore, _actualAssistantSqliteStore) ||
            !ReferenceEquals(actual.OriginalRepository, provider.GetRequiredService<ICapabilityRepository>()) ||
            _actualAssistantCapabilityInitializationWrites is not { } writes || writes.OriginalClose is not null ||
            !ReferenceEquals(actual.OriginalHomeWriteSource, writes) ||
            !writes.HasOriginalComposition(home.StateStore, home.Profiles, actual) ||
            !_actualAssistantCapabilityInitializationResolver!.IsBoundToOriginalOwner(writes))
            throw new UnauthorizedAccessException("Use the SAME actual protected catalogue/store/repository/Home setup owners.");
        // During the producer factory, aliases cannot be recursively resolved.
        // The actual Core consumer compares its process-source reference after return.
    }

    private Task JoinOriginalAssistantCapabilityInitializationAfterBorrowersAsync()
    {
        _actualAssistantCapabilityInitialization?.DemandExternalOriginalJoin();
        _actualAssistantCapabilityInitializationWrites?.DemandExternalOriginalJoin();
        TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAssistantCapabilityInitializationDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAssistantCapabilityInitializationDrain = JoinOriginalAssistantCapabilityInitializationCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }

    private async Task JoinOriginalAssistantCapabilityInitializationCoreAsync(Task start)
    {
        await start;
        var failures = new List<Exception>();
        // Only the process issuer chooses which pending individual Home reviews
        // may be withdrawn. Claimed SQL is never treated as a canceled observation.
        try { _originalAppWork.RunCloseCallback(() => _actualAssistantCapabilityInitialization?.RequestOriginalPendingReviewWithdrawals()); }
        catch (Exception cause) { AddAppCause(failures, cause); }
        async Task Join(Func<Task> acquire)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() => raw = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (raw is not null) await JoinOriginalAppTaskAsync(raw, failures);
            ThrowAppCauses(failures); // Unknown source/SQL/claim failures retain downstream owners.
        }
        if (_actualAssistantCapabilityInitialization is { } actual) await Join(actual.CloseAndDrainOriginalAsync);
        if (_actualAssistantCapabilityInitializationWrites is { } writes) await Join(writes.CloseAndDrainOriginalAsync);
        ThrowAppCauses(failures);
    }
}
#endif
