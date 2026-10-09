using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.Canonical;

// Presentation custody over a source-issued setup delivery. The actual SQL/Home
// initializer is process owned and is never retired by this bridge.
public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalCapabilityInitializationOwner
{
    private readonly object _configurationInitializationGate = new();
    // The original ledger roots each unresolved delivery through its exact closer.
    // Issuer lookup adds no second permanent strong cohort after healthy close.
    private readonly ConditionalWeakTable<AssistantOriginalCapabilityInitializationObservation, object> _configurationInitializationDeliveries = new();
    private readonly ConditionalWeakTable<AssistantOriginalCapabilityInitializationObservation, InitializationRetirement> _configurationInitializationRetirementRequests = new();
    private sealed class InitializationRetirement { internal bool Requested; internal Task? Failure; }
    private bool _configurationInitializationRetiring;
    [ThreadStatic] private static DenAssistantCanonicalBridge? _configurationInitializationPhysical;

    public Task<AssistantOriginalCapabilityInitializationIntent> PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Guid operationId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            var owner = DemandOriginalConfigurationInitializationOwner();
            var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
            return await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
                owner.PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(identity,
                    expectedDefinitionRevision, operationId, callbacks.Scope, callbacks.Retain, token),
                callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        });

    public bool IsIssuedOriginalConfigurationInitializationIntent(AssistantOriginalCapabilityInitializationIntent sameActual) =>
        TryOriginalConfigurationInitializationOwner(out var owner) && owner.IsIssuedOriginalConfigurationInitializationIntent(sameActual);

    public Task RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(
        AssistantOriginalCapabilityInitializationIntent sameActual, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => _originals.Admit(async () =>
        {
            var owner = DemandOriginalConfigurationInitializationOwner();
            var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
            await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
                owner.RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(sameActual,
                    callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
            return true;
        });

    public Task<AssistantOriginalCapabilityInitializationObservation> StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(
        AssistantOriginalCapabilityInitializationIntent sameActual, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => _originals.Admit(async () =>
        {
            var owner = DemandOriginalConfigurationInitializationOwner();
            var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
            _originals.Invoke(() => { _originals.DemandObservationCapacity(); return true; });
            Task<AssistantOriginalCapabilityInitializationObservation>? acquisition = null;
            AssistantOriginalCapabilityInitializationObservation? actual = null;
            Exception? failure = null;
            try
            {
                actual = await CaptureOriginalConfigurationCapabilitySourceAsync(() => acquisition =
                    owner.StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(sameActual,
                        callbacks.Scope, callbacks.Retain, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
            }
            catch (Exception cause) { failure = cause; }
            finally
            {
                // A rejected retainer/postguard cannot orphan a late successful
                // acquisition. Keep and join its exact delivery before reporting it.
                if (acquisition is { IsCompletedSuccessfully: true }) actual = acquisition.Result;
                if (actual is not null)
                {
                    var captured = actual;
                    _originals.Observe(captured.CloseAndDrainOriginalAsync, () => captured.OriginalClose);
                    bool retire;
                    lock (_configurationInitializationGate)
                    {
                        _configurationInitializationDeliveries.GetValue(captured, static _ => new object());
                        retire = _configurationInitializationRetiring;
                    }
                    if (retire || failure is not null) RequestOriginalConfigurationInitializationDelivery(captured);
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return _originals.Invoke(() => actual is not null &&
                owner.IsIssuedOriginalConfigurationInitializationObservation(actual) &&
                ReferenceEquals(actual.OriginalIntent, sameActual)
                    ? actual : throw new UnauthorizedAccessException("The SAME configured setup owner did not issue this delivery observation."));
        });

    public bool IsIssuedOriginalConfigurationInitializationObservation(AssistantOriginalCapabilityInitializationObservation sameActual) =>
        TryOriginalConfigurationInitializationOwner(out var owner) && owner.IsIssuedOriginalConfigurationInitializationObservation(sameActual);

    /// <summary>Actual source composition only; preparing/starting still requires fresh Home validation.</summary>
    public bool HasOriginalConfiguredCapabilityInitializationSource =>
        TryOriginalConfigurationInitializationOwner(out var owner) && owner is AssistantOriginalCapabilityOwner actual &&
        actual.OriginalInitializationSource is not null;

    private bool TryOriginalConfigurationInitializationOwner(out IAssistantOriginalCapabilityInitializationOwner owner)
    {
        if (_capabilities is AssistantOriginalCapabilityOwner actual &&
            ReferenceEquals(actual.OriginalHomeDenFactory, _home) &&
            ReferenceEquals(actual.OriginalConversations, _conversations) && ReferenceEquals(actual.OriginalChat, _chat) &&
            actual is IAssistantOriginalCapabilityInitializationOwner source)
        { owner = source; return true; }
        owner = null!; return false;
    }
    private IAssistantOriginalCapabilityInitializationOwner DemandOriginalConfigurationInitializationOwner() =>
        TryOriginalConfigurationInitializationOwner(out var owner) ? owner :
            throw new AssistantCommandRefusedException("The SAME saved-definition capability setup owner is unavailable.");

    private void DemandExternalOriginalConfigurationInitializationJoin()
    {
        if (ReferenceEquals(_configurationInitializationPhysical, this))
            throw new InvalidOperationException("A setup delivery retirement callback cannot join its own presentation.");
    }
    private void RequestOriginalConfigurationInitializationDeliveryRetirement()
    {
        AssistantOriginalCapabilityInitializationObservation[] deliveries;
        lock (_configurationInitializationGate)
        {
            _configurationInitializationRetiring = true;
            deliveries = _configurationInitializationDeliveries.Select(value => value.Key).ToArray();
        }
        foreach (var delivery in deliveries) RequestOriginalConfigurationInitializationDelivery(delivery);
    }
    private void RequestOriginalConfigurationInitializationDelivery(AssistantOriginalCapabilityInitializationObservation delivery)
    {
        InitializationRetirement occurrence;
        lock (_configurationInitializationGate)
        {
            occurrence = _configurationInitializationRetirementRequests.GetValue(delivery, static _ => new());
            if (occurrence.Requested) return;
            occurrence.Requested = true;
        }
        var prior = _configurationInitializationPhysical; _configurationInitializationPhysical = this;
        try { delivery.RequestOriginalRetirement(); }
        catch (Exception cause)
        {
            // Qualification belongs to the actual delivery invocation. Two distinct
            // callbacks reusing one exception object still produce two retained raw
            // occurrences; repeated retirement of this SAME delivery is once only.
            var failure = Task.FromException(cause);
            lock (_configurationInitializationGate) occurrence.Failure = failure;
            _originals.Retain(failure);
        }
        finally { _configurationInitializationPhysical = prior; }
    }
}
