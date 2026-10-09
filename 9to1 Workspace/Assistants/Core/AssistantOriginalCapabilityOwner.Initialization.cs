using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantOriginalCapabilityOwner : IAssistantOriginalCapabilityInitializationOwner
{
    private readonly object _initializationIssuer = new(), _initializationGate = new();
    private readonly ConditionalWeakTable<AssistantOriginalCapabilityInitializationIntent, InitializationIntent> _initializationIntents = new();
    private readonly ConditionalWeakTable<AssistantOriginalCapabilityInitializationObservation, InitializationDelivery> _initializationObservations = new();
    private readonly List<InitializationDelivery> _initializationDeliveries = [];
    private sealed record InitializationIntent(AssistantCanonicalMembershipSource Definitions,
        AssistantDefinitionSnapshot Definition, string DefinitionJson,
        ICapabilityOriginalInitializationIntent Original);
    private sealed class InitializationDelivery(InitializationIntent intent,
        ICapabilityOriginalInitializationObservation original)
    {
        internal readonly InitializationIntent Intent = intent;
        internal readonly ICapabilityOriginalInitializationObservation Original = original;
        internal AssistantOriginalCapabilityInitializationObservation? Public;
    }
    private ICapabilityOriginalInitializationProcessSource RequireOriginalInitializationSource() =>
        _initialization ?? throw new AssistantCommandRefusedException("Actual individual Home capability setup is not configured on this host.");

    public bool IsIssuedOriginalConfigurationInitializationIntent(AssistantOriginalCapabilityInitializationIntent actual) =>
        Volatile.Read(ref _retired) == 0 && actual is not null && ReferenceEquals(actual.Issuer, _initializationIssuer) &&
        _initializationIntents.TryGetValue(actual, out var issued) && ReferenceEquals(actual.Original, issued) &&
        ReferenceEquals(actual.Definition, issued.Definition) && actual.Actor == issued.Original.Actor &&
        actual.OperationId == issued.Original.OperationId;
    public bool IsIssuedOriginalConfigurationInitializationObservation(AssistantOriginalCapabilityInitializationObservation actual) =>
        actual is not null && ReferenceEquals(actual.Issuer, _initializationIssuer) &&
        _initializationObservations.TryGetValue(actual, out var issued) && ReferenceEquals(actual.Original, issued) &&
        ReferenceEquals(issued.Public, actual);

    public Task<AssistantOriginalCapabilityInitializationIntent> PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantIdentity identity, long expectedRevision, Guid operationId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
    {
        var source = Invoke(scope, RequireOriginalInitializationSource);
        var definitions = Invoke(scope, OriginalConfigurationDefinitionSource);
        var before = await ReadConfigurationDefinitionAsync(definitions, identity, expectedRevision, scope, retain, token).ConfigureAwait(false);
        var observed = await ReadOriginalInitializationSourceAsync(source, (sourceScope, sourceRetain) =>
            source.PrepareOriginalInitializationWithinSourceAsync(before.Actor, operationId, sourceScope, sourceRetain, token), scope, retain).ConfigureAwait(false);
        var after = await ReadConfigurationDefinitionAsync(definitions, identity, expectedRevision, scope, retain, token).ConfigureAwait(false);
        return Invoke(scope, () =>
        {
            if (before.Actor != after.Actor || DefinitionFingerprint(before.Definition) != DefinitionFingerprint(after.Definition) ||
                !source.IsIssuedOriginalInitializationIntent(observed) || observed.Actor != before.Actor || observed.OperationId != operationId)
                throw new AssistantCommandRefusedException("The actual saved definition, actor or setup source changed. Refresh before setup.");
            var original = new InitializationIntent(definitions, before.Definition, DefinitionFingerprint(before.Definition), observed);
            var result = new AssistantOriginalCapabilityInitializationIntent(_initializationIssuer, original,
                before.Definition, before.Actor, operationId, Array.AsReadOnly(observed.MissingDefinitions.ToArray()), observed.IsRecoveredOperation);
            _initializationIntents.Add(result, original); return result;
        });
    });
    private InitializationIntent RequireOriginalInitializationIntent(AssistantOriginalCapabilityInitializationIntent actual) =>
        IsIssuedOriginalConfigurationInitializationIntent(actual) && _initializationIntents.TryGetValue(actual, out var issued)
            ? issued : throw new UnauthorizedAccessException("The SAME live saved-definition setup source did not issue this intent.");

    public Task RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(AssistantOriginalCapabilityInitializationIntent actual,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
    {
        var source = Invoke(scope, RequireOriginalInitializationSource);
        var issued = Invoke(scope, () => RequireOriginalInitializationIntent(actual));
        await DemandOriginalInitializationDefinitionAsync(issued, scope, retain, token).ConfigureAwait(false);
        await Read(() => source.RevalidateOriginalInitializationIntentWithinSourceAsync(issued.Original,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        await DemandOriginalInitializationDefinitionAsync(issued, scope, retain, token).ConfigureAwait(false);
        Invoke(scope, () => source.IsIssuedOriginalInitializationIntent(issued.Original) ? true :
            throw new UnauthorizedAccessException("The actual configured process owner no longer recognizes this original intent."));
        return true;
    });
    private async Task DemandOriginalInitializationDefinitionAsync(InitializationIntent issued,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var fresh = await ReadConfigurationDefinitionAsync(issued.Definitions, issued.Definition.Identity,
            issued.Definition.Revision, scope, retain, token).ConfigureAwait(false);
        Invoke(scope, () => fresh.Actor == issued.Original.Actor && DefinitionFingerprint(fresh.Definition) == issued.DefinitionJson
            ? true : throw new AssistantCommandRefusedException("The actual saved definition or Home actor changed before setup."));
    }

    public Task<AssistantOriginalCapabilityInitializationObservation> StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantOriginalCapabilityInitializationIntent actual, Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
    {
        var source = Invoke(scope, RequireOriginalInitializationSource);
        var issued = Invoke(scope, () => RequireOriginalInitializationIntent(actual));
        await Read(() => RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(actual,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        Invoke(scope, () => { _originals.DemandObservationCapacity(); return true; });
        Task<ICapabilityOriginalInitializationObservation>? acquisition = null;
        ICapabilityOriginalInitializationObservation? original = null;
        InitializationDelivery? delivery = null;
        var errors = new List<Exception>();
        try
        {
            original = await Read(() => acquisition = source.StartOriginalInitializationProcessWithinSourceAsync(issued.Original,
                body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        }
        catch (Exception cause) { Add(errors, cause); }
        finally
        {
            // Capture the SAME late successful delivery even if a caller retainer/postguard
            // rejects after acquisition. Its finite close remains owned before publication.
            if (acquisition is { IsCompletedSuccessfully: true }) original = acquisition.Result;
            if (original is not null)
            {
                delivery = new(issued, original);
                lock (_initializationGate)
                {
                    _initializationDeliveries.RemoveAll(item => item.Original.OriginalClose is { IsCompletedSuccessfully: true });
                    _initializationDeliveries.Add(delivery);
                }
                var retainedOriginal = original;
                _originals.Observe(retainedOriginal.CloseAndDrainOriginalAsync, () => retainedOriginal.OriginalClose);
            }
        }
        if (errors.Count != 0 && original is not null)
        {
            original.RequestOriginalRetirement();
            try { await original.CloseAndDrainOriginalAsync().ConfigureAwait(false); } catch (Exception cause) { Add(errors, cause); }
        }
        Throw(errors);
        var captured = delivery ?? throw new InvalidOperationException("No actual setup delivery observation was captured.");
        try { return Invoke(scope, () =>
        {
            if (!source.IsIssuedOriginalInitializationObservation(captured.Original) ||
                !ReferenceEquals(captured.Original.OriginalIntent, issued.Original))
                throw new UnauthorizedAccessException("The SAME configured process source did not issue this setup delivery.");
            var result = new AssistantOriginalCapabilityInitializationObservation(_initializationIssuer, captured, actual,
                waitToken => WaitOriginalInitializationDeliveryAsync(captured, waitToken),
                captured.Original.RequestOriginalRetirement, captured.Original.DemandExternalOriginalJoin,
                captured.Original.CloseAndDrainOriginalAsync, () => captured.Original.OriginalClose);
            captured.Public = result; _initializationObservations.Add(result, captured); return result;
        }); }
        catch (Exception cause)
        {
            var publicationErrors = new List<Exception> { cause };
            captured.Original.RequestOriginalRetirement();
            try { await captured.Original.CloseAndDrainOriginalAsync().ConfigureAwait(false); } catch (Exception cleanup) { Add(publicationErrors, cleanup); }
            Throw(publicationErrors); throw;
        }
    });
    private Task<AssistantOriginalCapabilityInitializationCompletion> WaitOriginalInitializationDeliveryAsync(InitializationDelivery delivery,
        CancellationToken token) => _originals.Admit(async () =>
    {
        var original = await Read(() => delivery.Original.WaitOriginalCompletionAsync(token), body => body(), _ => { }).ConfigureAwait(false);
        return _originals.Invoke(() =>
        {
            if (!delivery.Original.IsIssuedOriginalCompletion(original) || !ReferenceEquals(original.OriginalObservation, delivery.Original) ||
                delivery.Public is not { } actual)
                throw new UnauthorizedAccessException("The actual setup delivery did not issue this completion.");
            var acknowledgment = original.Acknowledgment;
            if (original.Kind == CapabilityOriginalInitializationCompletionKind.Initialized &&
                (acknowledgment is null || !ReferenceEquals(acknowledgment.OriginalIntent, delivery.Intent.Original)))
                throw new UnauthorizedAccessException("The original setup completion has no SAME intent acknowledgment.");
            return new AssistantOriginalCapabilityInitializationCompletion(actual, original.Kind,
                Array.AsReadOnly(acknowledgment?.InsertedDefinitionIds.ToArray() ?? []), acknowledgment?.IsRecoveredOperation ?? false);
        });
    });
    private async Task<T> ReadOriginalInitializationSourceAsync<T>(ICapabilityOriginalInitializationProcessSource source,
        Func<Action<Action>, Action<Task>, Task<T>> factory, Action<Action> scope, Action<Task> retain)
    {
        var actuals = new List<Task>(); var sourceGate = new object(); Task<T>? original = null; var protocolFailed = 0;
        void Scoped(Action body)
        {
            try { Run(scope, body); } catch { Interlocked.Exchange(ref protocolFailed, 1); throw; }
        }
        void Keep(Task actual)
        {
            lock (sourceGate) actuals.Add(actual);
            try { Retain(retain, actual); } catch { Interlocked.Exchange(ref protocolFailed, 1); throw; }
        }
        try { return await Read(() => original = factory(Scoped, Keep), Scoped, Keep).ConfigureAwait(false); }
        catch (Exception observed)
        {
            Task[] retained; lock (sourceGate) retained = actuals.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            var completeKnown = Volatile.Read(ref protocolFailed) == 0 &&
                original is { IsFaulted: true, Exception: { InnerExceptions.Count: 1 } payload } &&
                ReferenceEquals(payload.InnerExceptions[0], observed);
            foreach (var actual in retained.Where(value => value.IsFaulted))
            {
                var acknowledged = _originals.AcknowledgeOriginalExternalPreEffectRefusal(actual,
                    source.IsAcknowledgedOriginalInitializationSourceRefusal);
                completeKnown &= acknowledged;
            }
            if (completeKnown && original is not null && retained.Any(value => ReferenceEquals(value, original)))
                throw new AssistantCommandRefusedException("The actual capability setup source declined before any effect. Complete Home setup or refresh the saved intent.");
            throw;
        }
    }
    private void RetireOriginalConfigurationInitializationDeliveries()
    {
        InitializationDelivery[] snapshot; lock (_initializationGate) snapshot = _initializationDeliveries.ToArray();
        foreach (var delivery in snapshot) delivery.Original.RequestOriginalRetirement();
    }
}
