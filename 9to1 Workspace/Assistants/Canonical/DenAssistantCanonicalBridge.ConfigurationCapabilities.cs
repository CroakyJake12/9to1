using System.Runtime.ExceptionServices;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.Canonical;

// Optional saved-definition catalogue only. The actual configured capability owner
// validates Home/Den/store lineage before returning display rows; preferences add no
// capability/resource/action grant and require fresh runtime admission later.
public sealed partial class DenAssistantCanonicalBridge : IAssistantOriginalConfigurationCapabilityCatalogueOwner
{
    public Task<AssistantOriginalConfigurationCapabilityCatalogue> ReadOriginalConfigurationCatalogueWithinSourceAsync(
        AssistantIdentity identity, long expectedDefinitionRevision, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token) =>
        ForwardConfigurationCapabilitySourceAsync((owner, scope, retain) =>
            owner.ReadOriginalConfigurationCatalogueWithinSourceAsync(identity, expectedDefinitionRevision, scope, retain, token),
            originalSynchronousScope, retainOriginalTask);

    public bool IsIssuedOriginalConfigurationCatalogue(AssistantOriginalConfigurationCapabilityCatalogue sameActual) =>
        TryOriginalConfigurationCapabilityOwner(out var owner) && owner.IsIssuedOriginalConfigurationCatalogue(sameActual);

    public Task RevalidateOriginalConfigurationCatalogueWithinSourceAsync(AssistantOriginalConfigurationCapabilityCatalogue sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        ForwardConfigurationCapabilitySourceAsync((owner, scope, retain) =>
            owner.RevalidateOriginalConfigurationCatalogueWithinSourceAsync(sameActual, scope, retain, token),
            originalSynchronousScope, retainOriginalTask);

    public Task<AssistantOriginalConfigurationCapabilityChoice> SelectOriginalConfigurationCapabilityWithinSourceAsync(
        AssistantOriginalConfigurationCapabilityCatalogue sameActual, Guid observedCapabilityId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        ForwardConfigurationCapabilitySourceAsync((owner, scope, retain) =>
            owner.SelectOriginalConfigurationCapabilityWithinSourceAsync(sameActual, observedCapabilityId, scope, retain, token),
            originalSynchronousScope, retainOriginalTask);

    public bool IsIssuedOriginalConfigurationChoice(AssistantOriginalConfigurationCapabilityChoice sameActual) =>
        TryOriginalConfigurationCapabilityOwner(out var owner) && owner.IsIssuedOriginalConfigurationChoice(sameActual);

    public Task RevalidateOriginalConfigurationChoiceWithinSourceAsync(AssistantOriginalConfigurationCapabilityChoice sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        ForwardConfigurationCapabilitySourceAsync((owner, scope, retain) =>
            owner.RevalidateOriginalConfigurationChoiceWithinSourceAsync(sameActual, scope, retain, token),
            originalSynchronousScope, retainOriginalTask);

    private bool TryOriginalConfigurationCapabilityOwner(out IAssistantOriginalConfigurationCapabilityCatalogueOwner owner)
    {
        // Pure SAME configured refs, not a resource or execution permission.
        if (_capabilities is AssistantOriginalCapabilityOwner actual &&
            ReferenceEquals(actual.OriginalHomeDenFactory, _home) &&
            ReferenceEquals(actual.OriginalConversations, _conversations) && ReferenceEquals(actual.OriginalChat, _chat) &&
            actual is IAssistantOriginalConfigurationCapabilityCatalogueOwner configured)
        { owner = configured; return true; }
        owner = null!; return false;
    }

    private Task<T> ForwardConfigurationCapabilitySourceAsync<T>(
        Func<IAssistantOriginalConfigurationCapabilityCatalogueOwner, Action<Action>, Action<Task>, Task<T>> factory,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask) => _originals.Admit(async () =>
        {
            var owner = DemandOriginalConfigurationCapabilityOwner();
            var callbacks = CaptureConfigurationCapabilityCallbacks(originalSynchronousScope, retainOriginalTask);
            return await CaptureOriginalConfigurationCapabilitySourceAsync(() => factory(owner, callbacks.Scope, callbacks.Retain),
                callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        });

    private Task ForwardConfigurationCapabilitySourceAsync(
        Func<IAssistantOriginalConfigurationCapabilityCatalogueOwner, Action<Action>, Action<Task>, Task> factory,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask) => _originals.Admit(async () =>
        {
            var owner = DemandOriginalConfigurationCapabilityOwner();
            var callbacks = CaptureConfigurationCapabilityCallbacks(originalSynchronousScope, retainOriginalTask);
            await CaptureOriginalConfigurationCapabilitySourceAsync(() => factory(owner, callbacks.Scope, callbacks.Retain),
                callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
            return true;
        });

    private IAssistantOriginalConfigurationCapabilityCatalogueOwner DemandOriginalConfigurationCapabilityOwner() =>
        TryOriginalConfigurationCapabilityOwner(out var owner) ? owner :
            throw new AssistantCommandRefusedException("The SAME configured saved-definition capability catalogue owner is unavailable.");

    private (Action<Action> Scope, Action<Task> Retain) CaptureConfigurationCapabilityCallbacks(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        void Scope(Action body) => _originals.Invoke(() => { originalSynchronousScope(body); return true; });
        void Retain(Task actual)
        {
            // Capture the SAME raw occurrence before the parent's retainer can reject.
            _originals.Retain(actual); retainOriginalTask(actual);
        }
        return (Scope, Retain);
    }

    internal static Task<T> CaptureOriginalConfigurationCapabilitySourceAsync<T>(Func<Task<T>> factory,
        Action<Action> scope, Action<Task> retain) => CaptureOriginalConfigurationCapabilitySourceCoreAsync(
            () => factory(), actual => ((Task<T>)actual).GetAwaiter().GetResult(), scope, retain);

    internal static Task CaptureOriginalConfigurationCapabilitySourceAsync(Func<Task> factory,
        Action<Action> scope, Action<Task> retain) => CaptureOriginalConfigurationCapabilitySourceCoreAsync(
            factory, static _ => true, scope, retain);

    private static async Task<T> CaptureOriginalConfigurationCapabilitySourceCoreAsync<T>(Func<Task> factory,
        Func<Task, T> readResult, Action<Action> scope, Action<Task> retain)
    {
        ArgumentNullException.ThrowIfNull(factory); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        Task? actual = null; Exception? bodyFailure = null; Exception? protocolFailure = null; Exception? scopeFailure = null;
        var phase = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref phase) == 0 || thread != Environment.CurrentManagedThreadId ||
                    Interlocked.CompareExchange(ref used, 1, 0) != 0)
                {
                    var cause = new InvalidOperationException("The original configuration source callback expired, repeated or moved threads.");
                    Interlocked.CompareExchange(ref protocolFailure, cause, null); throw cause;
                }
                try { actual = factory() ?? throw new InvalidOperationException("No actual configuration source Task returned."); retain(actual); }
                catch (Exception cause) { bodyFailure = cause; throw; }
            });
            if (Volatile.Read(ref used) != 1 && bodyFailure is null && protocolFailure is null)
                throw new InvalidOperationException("The actual configuration source callback was not invoked.");
        }
        catch (Exception cause) { scopeFailure = cause; }
        finally { Volatile.Write(ref phase, 0); }
        var errors = new List<Exception>();
        void Keep(Exception? cause) { if (cause is not null && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
        Keep(bodyFailure); Keep(protocolFailure); Keep(scopeFailure);
        T? result = default;
        if (actual is not null)
        {
            // Every accepted raw source settles independently before any rejected
            // publication reports. Direct foreign aggregates remain opaque causes.
            try { await actual.ConfigureAwait(false); if (errors.Count == 0) result = readResult(actual); }
            catch (Exception cause)
            {
                if (errors.Count == 0 && actual.IsCanceled) ExceptionDispatchInfo.Capture(cause).Throw();
                foreach (var original in actual.Exception?.InnerExceptions.ToArray() ?? [cause]) Keep(original);
            }
        }
        else if (errors.Count == 0) Keep(new InvalidOperationException("No actual configuration source was acquired."));
        if (errors.Count == 0) return result!;
        if (errors.Count == 1 && errors[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        throw new AggregateException("Original configuration source/callback custody failed.", errors);
    }
}
