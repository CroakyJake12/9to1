using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Canonical;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    /// <summary>Source composition only, never setup completion or an execution/resource grant.</summary>
    public bool HasOriginalConfiguredCapabilityInitializationSource =>
        _bridge is DenAssistantCanonicalBridge actual && actual.HasOriginalConfiguredCapabilityInitializationSource;

    public Task CloseOriginalObservationAsync(AssistantOriginalCapabilityInitializationObservation actual) => DetachObservation(actual);
    public Task<AssistantOriginalCapabilityInitializationIntent> PrepareOriginalConfigurationCapabilityInitializationAsync(
        Guid operationId, CancellationToken token = default)
    {
        var definition = DemandSelected();
        return CommandAsync(() =>
        {
            var owner = DemandConfigurationInitializationOwner();
            var sources = CaptureConfigurationCapabilitySources();
            return ObserveSourceAsync(() => owner.PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(
                definition.Identity, definition.Revision, operationId, sources.Scope, sources.Retain, token));
        }, false, token);
    }

    public Task<AssistantOriginalCapabilityInitializationObservation> StartOriginalConfigurationCapabilityInitializationAsync(
        AssistantOriginalCapabilityInitializationIntent actualIntent, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualIntent); var definition = DemandSelected();
        DemandObservationCapacity();
        return CommandAsync(async () =>
        {
            var owner = DemandConfigurationInitializationOwner();
            using (EnterSynchronousSource())
            {
                if (!owner.IsIssuedOriginalConfigurationInitializationIntent(actualIntent) || actualIntent.Definition.Identity != definition.Identity)
                    throw new UnauthorizedAccessException("The current saved-definition owner did not issue this setup intent.");
                if (actualIntent.Definition.Revision != definition.Revision)
                    throw IssueLocalRefusal("The Assistant configuration changed. Review its current setup intent.");
            }
            var sources = CaptureConfigurationCapabilitySources();
            var actual = await ObserveSourceAsync(() => owner.StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(
                actualIntent, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            using (EnterSynchronousSource())
                if (!owner.IsIssuedOriginalConfigurationInitializationObservation(actual) || !ReferenceEquals(actual.OriginalIntent, actualIntent))
                    throw new UnauthorizedAccessException("The SAME setup source did not issue this original delivery.");
            // RequestRetirement already requests every retained delivery BEFORE
            // controller command joins. It detaches delivery only, never SQL/Home.
            RetainObservation(new(actual, actual.RequestOriginalRetirement, actual.DemandExternalOriginalJoin,
                actual.CloseAndDrainOriginalAsync));
            return actual;
        }, false, token);
    }

    public Task<AssistantOriginalCapabilityInitializationCompletion> WaitOriginalConfigurationCapabilityInitializationAsync(
        AssistantOriginalCapabilityInitializationObservation actual, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actual);
        return ObserveConfigurationInitializationDeliveryAsync(async () =>
        {
            using (EnterSynchronousSource())
            {
                if (!DemandConfigurationInitializationOwner().IsIssuedOriginalConfigurationInitializationObservation(actual))
                    throw new UnauthorizedAccessException("Only the SAME setup source's original delivery is accepted.");
                lock (_gate)
                    if (!_observations.Any(value => ReferenceEquals(value.Original, actual)))
                        throw IssueLocalRefusal("Use this presentation's retained setup observation.");
            }
            var completion = await ObserveSourceAsync(() => actual.WaitOriginalCompletionAsync(token)).ConfigureAwait(false);
            if (!ReferenceEquals(completion.OriginalObservation, actual))
                throw new UnauthorizedAccessException("The setup completion belongs to another delivery.");
            return completion;
        });
    }

    private Task<T> ObserveConfigurationInitializationDeliveryAsync<T>(Func<Task<T>> body)
    {
        Original original; TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            DemandAdmission();
            _originals.RemoveAll(value => value.Task.IsCompletedSuccessfully || (value.Task.IsCompleted && value.KnownRefusal));
            if (_originals.Count >= MaximumRetainedOriginals)
                throw new InvalidOperationException("Assistants delivery custody is full. Preserve unresolved originals before new observation.");
            original = new() { Parent = _executing.Value };
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DriveConfigurationInitializationDeliveryAsync(start.Task, original, body);
            original.Task = actual; _originals.Add(original);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<T> DriveConfigurationInitializationDeliveryAsync<T>(Task start, Original original, Func<Task<T>> body)
    {
        await start.ConfigureAwait(false);
        var prior = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        // The SAME existing Original/Sources cohort owns this delivery wait. It
        // holds no command semaphore, so a pending Home review cannot block saving
        // drafts or navigating while the process operation remains independently owned.
        try { return await body().ConfigureAwait(false); }
        catch (Exception error)
        {
            // The SAME issuer/local-refusal proof and healthy actual source cohort
            // used by ExecuteAsync apply here. Unknown/mixed raw failures remain
            // failures; this delivery path grants no cancellation waiver.
            lock (_gate) original.KnownRefusal = IsObservedRefusal(original, error) &&
                original.Sources.All(value => value.IsCompletedSuccessfully || IsKnownSourceRefusal(value));
            throw;
        }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = prior; }
    }

    private IAssistantOriginalCapabilityInitializationOwner DemandConfigurationInitializationOwner() =>
        _bridge as IAssistantOriginalCapabilityInitializationOwner ??
        throw IssueLocalRefusal("Individual Home capability setup is unavailable on this host.");
}
