#if !ANDROID
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalAssistantGeneratedUiOriginOwner? _actualGeneratedUiOrigins;
    private CanonicalGeneratedUiInteractionOriginalOwner? _actualGeneratedUiInteractions;
    private HomeCanonicalGeneratedUiInteractionWriteSource? _actualGeneratedUiWrites;
    private HomeCanonicalGeneratedUiInteractionResourceResolver? _actualGeneratedUiResolver;
    private HomeCanonicalGeneratedUiInteractionActionPolicySource? _actualGeneratedUiPolicy;
    private Task? _actualGeneratedUiDrain;
    private void PrepareOriginalGeneratedUiInteractionPolicies()
    {
        _actualGeneratedUiResolver = new(() => _actualGeneratedUiWrites ?? throw new InvalidOperationException("The SAME original generated interaction Home source is not retained."));
        _actualGeneratedUiPolicy = new();
    }
    private bool HasOriginalGeneratedUiProvider(IServiceProvider actual) => _services is { } root &&
        (ReferenceEquals(actual, root) || ReferenceEquals(actual, root.GetRequiredService<IServiceProvider>()));
    private void ConfigureOriginalGeneratedUiInteractionRegistrations(IServiceCollection collection)
    {
        if (_actualWindowsHome is not { } home) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            Type[] owned = [typeof(CanonicalGeneratedUiInteractionOriginalOwner), typeof(ICanonicalGeneratedUiInteractionOriginalReadSource),
                typeof(ICanonicalGeneratedUiInteractionOriginalWriteSource), typeof(ICanonicalGeneratedUiInteractionOriginalProcessSource)];
            if (collection.Any(row => owned.Contains(row.ServiceType))) throw new InvalidOperationException("The original generated interaction owner cannot be replaced.");
            var repositoryRegistration = collection.Where(row => row.ServiceType == typeof(IGenUiAppRepository)).ToArray();
            if (repositoryRegistration.Length != 1 || repositoryRegistration[0].ImplementationType != typeof(GenUiAppRepository) || repositoryRegistration[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("Retain the maintained configured generated-app repository.");
            collection.AddSingleton<CanonicalGeneratedUiInteractionOriginalOwner>(provider =>
            {
                CanonicalGeneratedUiInteractionOriginalOwner? acquired = null;
                _originalAppWork.RunSynchronous(source => acquired = AcquireOriginalAppSynchronous(source, () =>
                {
                    if (!HasOriginalGeneratedUiProvider(provider)) throw new UnauthorizedAccessException("Use the SAME actual configured root provider.");
                    _ = provider.RequireOriginalWindowsHomeComponents(home);
                    if (_actualGeneratedUiOrigins is not null || _actualGeneratedUiInteractions is not null || _actualGeneratedUiWrites is not null)
                        throw new InvalidOperationException("The actual generated interaction acquisition already exists; retain partial originals.");
                    var factory = provider.GetRequiredService<HomePersonalDenFactory>();
                    var conversations = provider.GetRequiredService<IConversationRepository>();
                    var repository = provider.GetRequiredService<IGenUiAppRepository>() as GenUiAppRepository
                        ?? throw new UnauthorizedAccessException("Use the actual maintained generated-app repository.");
                    var origins = _actualGeneratedUiOrigins = new(factory, conversations, provider.GetRequiredService<ConversationRepository>(),
                        _actualAssistantSqliteStore!, _actualAssistantSqliteDatabase!, home.Ownership, provider.GetRequiredService<GenUiInstanceStore>());
                    var writer = _actualGeneratedUiInteractions = new(_actualAssistantSqliteStore!, _actualAssistantSqliteDatabase!, repository, home.Ownership, origins);
                    var writes = _actualGeneratedUiWrites = new(home.StateStore, home.Profiles, home.Resources, home.Broker, home.Permissions, writer);
                    writer.BindOriginalHomeWriteSource(writes); origins.BindOriginalInteractionStore(writer);
                    if (!ReferenceEquals(writer.OriginalHomeWriteSource, writes) || !ReferenceEquals(writer.OriginalMessageAuthority, origins) ||
                        !writes.HasOriginalComposition(home.StateStore, home.Profiles, writer) || !_actualGeneratedUiResolver!.IsBoundToOriginalOwner(writes))
                        throw new UnauthorizedAccessException("Retain the SAME configured origin/store/individual Home WRITE tuple.");
                    return writer;
                }));
                return acquired ?? throw new InvalidOperationException("The original generated interaction source was not captured.");
            });
            collection.AddSingleton<ICanonicalGeneratedUiInteractionOriginalReadSource>(provider => provider.GetRequiredService<CanonicalGeneratedUiInteractionOriginalOwner>());
            collection.AddSingleton<ICanonicalGeneratedUiInteractionOriginalWriteSource>(provider => provider.GetRequiredService<CanonicalGeneratedUiInteractionOriginalOwner>());
            collection.AddSingleton<ICanonicalGeneratedUiInteractionOriginalProcessSource>(provider => provider.GetRequiredService<CanonicalGeneratedUiInteractionOriginalOwner>());
            return true;
        }));
    }
    private void DemandOriginalGeneratedUiInteractionJoin()
    { _actualGeneratedUiInteractions?.DemandExternalOriginalJoin(); _actualGeneratedUiOrigins?.DemandExternalOriginalJoin(); _actualGeneratedUiWrites?.DemandExternalOriginalJoin(); }
    private void RequestOriginalGeneratedUiPendingWithdrawals(List<Exception> errors)
    {
        try { _originalAppWork.RunCloseCallback(() => _actualGeneratedUiInteractions?.RequestOriginalPendingReviewWithdrawals()); }
        catch (Exception cause) { AddAppCause(errors, cause); }
    }
    private Task JoinOriginalGeneratedUiInteractionsAfterViewsAsync()
    {
        DemandOriginalGeneratedUiInteractionJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_actualStartupAcquisitionGate)
        {
            if (_actualGeneratedUiDrain is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _actualGeneratedUiDrain = Drain(start.Task); }
            actual = _actualGeneratedUiDrain;
        }
        start?.SetResult(); return actual;
        async Task Drain(Task begin)
        {
            await begin; var errors = new List<Exception>(); RequestOriginalGeneratedUiPendingWithdrawals(errors);
            ThrowAppCauses(errors);
            OriginalAssistantsAcquisition[] acquisitions; lock (_actualStartupAcquisitionGate) acquisitions = _actualAssistantAcquisitions.ToArray();
            if (acquisitions.Any(item => item.OriginalGeneratedUiHost is { } host && host.OriginalClose?.IsCompletedSuccessfully != true))
                throw new InvalidOperationException("The actual generated presentation borrowers remain unresolved; retain process/Home/Den/store.");
            async Task Join(Func<Task> acquire)
            {
                Task? raw = null; try { _originalAppWork.RunCloseCallback(() => raw = acquire()); } catch (Exception cause) { AddAppCause(errors, cause); }
                if (raw is not null) await JoinOriginalAppTaskAsync(raw, errors); ThrowAppCauses(errors);
            }
            // Actual pending/accepted SQL and both original pin releases settle
            // before process provenance/document cleanup and downstream Home.
            if (_actualGeneratedUiInteractions is { } writer) await Join(writer.CloseAndDrainOriginalAsync);
            if (_actualGeneratedUiOrigins is { } origins) await Join(origins.CloseAndDrainOriginalAsync);
            if (_actualGeneratedUiWrites is { } writes) await Join(writes.CloseAndDrainOriginalAsync);
        }
    }
}
#endif
