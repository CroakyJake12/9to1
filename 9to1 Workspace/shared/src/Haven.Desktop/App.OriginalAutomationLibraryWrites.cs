#if !ANDROID
using Haven.Application.Automations;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private CanonicalAutomationDefinitionOriginalWriteOwner? _actualAutomationLibraryWriter;
    private HomeCanonicalAutomationDefinitionWriteSource? _actualAutomationLibraryWrites;
    private HomeCanonicalAutomationDefinitionResourceResolver? _actualAutomationLibraryWriteResolver;
    private HomeCanonicalAutomationDefinitionActionPolicySource? _actualAutomationLibraryWritePolicy;
    private Task? _actualAutomationLibraryWriteDrain;

    private void PrepareOriginalAutomationLibraryWritePolicies()
    {
        _actualAutomationLibraryWriteResolver = new(() => _actualAutomationLibraryWrites
            ?? throw new InvalidOperationException("The actual saved-automation Home review owner has not been captured."));
        _actualAutomationLibraryWritePolicy = new();
    }
    private void ConfigureOriginalAutomationLibraryWriteRegistrations(IServiceCollection collection,
        HomeNativeWindowsComposition home)
    {
        // Lazy construction pairs the SAME actual protected READ/store/repository.
        // Registration and construction perform no discovery, seed or schema IO.
        if (collection.Any(row => row.ServiceType == typeof(CanonicalAutomationDefinitionOriginalWriteOwner) ||
            row.ServiceType == typeof(ICanonicalAutomationDefinitionOriginalProcessSource)))
            throw new InvalidOperationException("Retain the actual saved-automation writer once.");
        collection.AddSingleton<CanonicalAutomationDefinitionOriginalWriteOwner>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
        {
            _ = provider.RequireOriginalWindowsHomeComponents(home);
            if (_actualAutomationLibraryWriter is not null || _actualAutomationLibraryWrites is not null)
                throw new InvalidOperationException("Retain the SAME automation writer and Home review close.");
            var library = provider.GetRequiredService<CanonicalAutomationLibraryOriginalReadOwner>();
            DemandOriginalAutomationLibraryComposition(provider, home, library);
            var actual = _actualAutomationLibraryWriter = new(library);
            var writes = _actualAutomationLibraryWrites = new(home.StateStore, home.Profiles,
                home.Resources, home.Broker, home.Permissions, actual);
            actual.BindOriginalHomeWriteSource(writes);
            DemandOriginalAutomationLibraryWriteComposition(provider, home, actual);
            return actual;
        }));
        collection.AddSingleton<ICanonicalAutomationDefinitionOriginalWriteSource>(provider =>
            provider.GetRequiredService<CanonicalAutomationDefinitionOriginalWriteOwner>());
        collection.AddSingleton<ICanonicalAutomationDefinitionOriginalProcessSource>(provider =>
            provider.GetRequiredService<CanonicalAutomationDefinitionOriginalWriteOwner>());
        collection.AddSingleton<HomeCanonicalAutomationDefinitionWriteSource>(provider => AcquireOriginalAssistantCapabilitySingleton(() =>
        {
            _ = provider.GetRequiredService<CanonicalAutomationDefinitionOriginalWriteOwner>();
            return _actualAutomationLibraryWrites ?? throw new InvalidOperationException("No SAME Home review owner was retained.");
        }));
        collection.AddSingleton<ICanonicalAutomationDefinitionOriginalHomeWriteSource>(provider =>
            provider.GetRequiredService<HomeCanonicalAutomationDefinitionWriteSource>());
        collection.AddSingleton<ICanonicalAutomationDefinitionOriginalHomeReviewWithdrawalSource>(provider =>
            provider.GetRequiredService<HomeCanonicalAutomationDefinitionWriteSource>());
    }
    private void DemandOriginalAutomationLibraryWriteComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, CanonicalAutomationDefinitionOriginalWriteOwner actual)
    {
        if (!HasOriginalAutomationLibraryProviderTuple(provider, _services) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(actual, _actualAutomationLibraryWriter) || actual.OriginalClose is not null ||
            !ReferenceEquals(actual.OriginalLibrary, _actualAutomationLibraryRead) ||
            !ReferenceEquals(actual.OriginalStore, _actualAssistantSqliteStore) ||
            !ReferenceEquals(actual.OriginalRepository, provider.GetRequiredService<Haven.Application.IAutomationRepository>()) ||
            _actualAutomationLibraryWrites is not { } writes || writes.OriginalClose is not null ||
            !ReferenceEquals(actual.OriginalHomeWriteSource, writes) ||
            !writes.HasOriginalComposition(home.StateStore, home.Profiles, actual) ||
            !_actualAutomationLibraryWriteResolver!.IsBoundToOriginalOwner(writes))
            throw new UnauthorizedAccessException("The actual protected automation library/store/repository/Home review pairing changed.");
        DemandOriginalAutomationLibraryComposition(provider, home, actual.OriginalLibrary);
    }
    private void DemandOriginalAutomationLibraryWriteJoin()
    {
        _actualAutomationLibraryWriter?.DemandExternalOriginalJoin();
        _actualAutomationLibraryWrites?.DemandExternalOriginalJoin();
    }
    private Task JoinOriginalAutomationLibraryWritesAfterViewsAsync()
    {
        DemandOriginalAutomationLibraryWriteJoin(); TaskCompletionSource start; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualAutomationLibraryWriteDrain is { } same) return same;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = _actualAutomationLibraryWriteDrain = JoinOriginalAutomationLibraryWritesCoreAsync(start.Task);
        }
        start.SetResult(); return actual;
    }
    private async Task JoinOriginalAutomationLibraryWritesCoreAsync(Task start)
    {
        await start;
        var failures = new List<Exception>();
        try { _originalAppWork.RunCloseCallback(() => _actualAutomationLibraryWriter?.RequestOriginalPendingReviewWithdrawals()); }
        catch (Exception cause) { AddAppCause(failures, cause); }
        async Task Join(Func<Task> acquire)
        {
            Task? raw = null;
            try { _originalAppWork.RunCloseCallback(() => raw = acquire()); }
            catch (Exception cause) { AddAppCause(failures, cause); }
            if (raw is not null) await JoinOriginalAppTaskAsync(raw, failures);
            ThrowAppCauses(failures);
        }
        // Deliveries retired with their views. The process withdraws only its OWN
        // unresolved reviews; accepted SQL and all unknown outcomes must settle.
        if (_actualAutomationLibraryWriter is { } actual) await Join(actual.CloseAndDrainOriginalAsync);
        if (_actualAutomationLibraryWrites is { } writes) await Join(writes.CloseAndDrainOriginalAsync);
        ThrowAppCauses(failures);
    }
}
#endif
