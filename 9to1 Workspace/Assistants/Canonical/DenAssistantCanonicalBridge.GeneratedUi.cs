using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using System.Runtime.CompilerServices;
using HavenOS.Home.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalBindingEvidence, GeneratedUiBindingEvidence> _generatedUiBindings = new();
    private sealed class GeneratedUiBindingEvidence(DenAssistantCanonicalBridge owner,
        AssistantConversationBinding binding, AuthenticatedResourceActor actor, VerifiedResourceStoreOwnership receipt)
        : ICanonicalGeneratedUiOriginalBindingEvidence
    {
        internal readonly DenAssistantCanonicalBridge Owner = owner;
        internal readonly AssistantConversationBinding Binding = binding;
        public AuthenticatedResourceActor HomeActor { get; } = actor;
        public Guid ConversationId => Binding.Conversation.Id;
        public VerifiedResourceStoreOwnership OriginalDenOwnership { get; } = receipt;
    }
    public bool HasOriginalGeneratedUiMessageComposition(HomePersonalDenFactory sameHome, IConversationRepository sameConversations) =>
        ReferenceEquals(_home, sameHome) && ReferenceEquals(_conversations, sameConversations);
    public bool IsIssuedOriginalGeneratedUiBindingEvidence(ICanonicalGeneratedUiOriginalBindingEvidence same) =>
        same is GeneratedUiBindingEvidence actual && ReferenceEquals(actual.Owner, this) &&
        _generatedUiBindings.TryGetValue(same, out var issued) && ReferenceEquals(actual, issued);
    public Task<ICanonicalGeneratedUiOriginalBindingEvidence> ObserveOriginalGeneratedUiBindingWithinSourceAsync(
        AssistantConversationBinding sameBinding, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit<ICanonicalGeneratedUiOriginalBindingEvidence>(async () =>
        {
            var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
            var current = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
                ValidateOriginalGeneratedUiDenWithinSourceAsync(sameBinding, callbacks.Scope, callbacks.Retain, token),
                callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
            var home = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
                _membership.OpenHomeWithinSourceAsync(callbacks.Scope, callbacks.Retain, token),
                callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
            return await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            {
                if (current.Actor != home.Actor || !_home.TryObserveOriginalDenOwnership(home, out var receipt) || receipt is null)
                    throw new AssistantCommandRefusedException("The SAME live original Home/Den session ownership is unavailable.");
                var evidence = new GeneratedUiBindingEvidence(this, sameBinding, home.Actor, receipt);
                _generatedUiBindings.Add(evidence, evidence);
                return Task.FromResult<ICanonicalGeneratedUiOriginalBindingEvidence>(evidence);
            }, callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        });
    public Task RevalidateOriginalGeneratedUiBindingEvidenceWithinSourceAsync(ICanonicalGeneratedUiOriginalBindingEvidence same,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit<bool>(async () =>
        {
            var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
            await _originals.Source(() => RevalidateOriginalGeneratedUiBindingEvidenceForProcessWithinSourceAsync(
                same, callbacks.Scope, callbacks.Retain, token)).ConfigureAwait(false);
            return true;
        });
    // Only evidence issued while a genuine live view binding was validated enters
    // this finite process borrower path. The retired view ledger is never revived.
    public async Task RevalidateOriginalGeneratedUiBindingEvidenceForProcessWithinSourceAsync(ICanonicalGeneratedUiOriginalBindingEvidence same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            if (!IsIssuedOriginalGeneratedUiBindingEvidence(same)) throw new AssistantCommandRefusedException("Only SAME bridge-issued generated membership evidence is accepted.");
            return Task.FromResult((GeneratedUiBindingEvidence)same);
        }, scope, retain).ConfigureAwait(false);
        var current = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            ValidateOriginalGeneratedUiDenWithinSourceAsync(actual.Binding, scope, retain, token), scope, retain).ConfigureAwait(false);
        var home = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.OpenHomeWithinSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            if (actual.HomeActor != current.Actor || current.Actor != home.Actor ||
                !_home.TryObserveOriginalDenOwnership(home, out var receipt) || receipt != actual.OriginalDenOwnership)
                throw new AssistantCommandRefusedException("The actual original Home actor/Den receipt changed.");
            return Task.FromResult(true);
        }, scope, retain).ConfigureAwait(false);
    }
    public Conversation ObserveOriginalGeneratedUiExpectedConversation(ICanonicalGeneratedUiOriginalBindingEvidence same)
    {
        if (!IsIssuedOriginalGeneratedUiBindingEvidence(same)) throw new UnauthorizedAccessException("Only SAME private generated membership evidence is accepted.");
        return ((GeneratedUiBindingEvidence)same).Binding.Conversation;
    }
    public async Task<ICanonicalGeneratedUiOriginalSourcePin> AcquireOriginalGeneratedUiEvidencePinWithinSourceAsync(
        ICanonicalGeneratedUiOriginalBindingEvidence sameEvidence, ICanonicalGeneratedUiOriginalSnapshot sameSnapshot,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalGeneratedUiOriginalSourcePin> capturePin, CancellationToken token)
    {
        await RevalidateOriginalGeneratedUiBindingEvidenceForProcessWithinSourceAsync(sameEvidence, scope, retain, token).ConfigureAwait(false);
        var actual = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            Task.FromResult((GeneratedUiBindingEvidence)sameEvidence), scope, retain).ConfigureAwait(false);
        return await AcquireOriginalGeneratedUiBindingPinCoreAsync(actual.Binding, sameSnapshot, scope, retain, capturePin, token).ConfigureAwait(false);
    }

    /// <summary>Finite acquisition only. The process interaction producer owns the
    /// returned child pin through SQL settlement, independently of view retirement.</summary>
    public Task<ICanonicalGeneratedUiOriginalSourcePin> AcquireOriginalGeneratedUiBindingPinWithinSourceAsync(
        AssistantConversationBinding sameBinding, ICanonicalGeneratedUiOriginalSnapshot sameSnapshot,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalGeneratedUiOriginalSourcePin> capturePin,
        CancellationToken token) => _originals.Admit<ICanonicalGeneratedUiOriginalSourcePin>(() =>
    {
        var callbacks = CaptureConfigurationCapabilityCallbacks(scope, retain);
        return AcquireOriginalGeneratedUiBindingPinCoreAsync(sameBinding, sameSnapshot, callbacks.Scope, callbacks.Retain, capturePin, token);
    });
    private async Task<ICanonicalGeneratedUiOriginalSourcePin> AcquireOriginalGeneratedUiBindingPinCoreAsync(
        AssistantConversationBinding sameBinding, ICanonicalGeneratedUiOriginalSnapshot sameSnapshot,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalGeneratedUiOriginalSourcePin> capturePin, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(capturePin);
        var callbacks = (Scope: scope, Retain: retain);
        var current = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            ValidateOriginalGeneratedUiDenWithinSourceAsync(sameBinding, callbacks.Scope, callbacks.Retain, token),
            callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var home = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.OpenHomeWithinSourceAsync(callbacks.Scope, callbacks.Retain, token),
            callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            if (sameSnapshot.HomeActor != current.Actor || sameSnapshot.ConversationId != current.Conversation.Id ||
                home.Actor != current.Actor || !_home.TryObserveOriginalDenOwnership(home, out var denReceipt) ||
                denReceipt != sameSnapshot.OriginalDenOwnership)
                throw new AssistantCommandRefusedException("The original message source no longer matches the same current Home binding/Den receipt.");
            return Task.FromResult(true);
        }, callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var definition = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.DefinitionWithinSourceAsync(home, sameBinding.Definition.Identity, callbacks.Scope, callbacks.Retain, token),
            callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        var session = await CaptureOriginalConfigurationCapabilitySourceAsync(() => home.Den.GetAsync<SessionRecord>(
            sameBinding.Definition.Identity.NamespaceId, sameBinding.DenSessionId, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            if (session is null || definition.Revision != sameBinding.Definition.Revision || session.Revision != sameBinding.DenSessionRevision)
                throw new AssistantCommandRefusedException("The exact original definition/membership revisions changed before pinning.");
            return Task.FromResult(true);
        }, callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        GeneratedUiBindingPin? captured = null; var failures = new List<Exception>();
        try
        {
            await CaptureOriginalConfigurationCapabilitySourceAsync(() => home.Den.Store.PinOriginalAssistantRevisionsAsync(
                definition, session!, home.Den.AccessPolicy, home.Den.PrincipalId, callbacks.Scope, callbacks.Retain,
                raw =>
                {
                    captured = new(sameSnapshot, sameBinding, raw, scope, retain);
                    // Actual child is locally captured before any external retainer or
                    // post-scope currentness callback can reject its successful birth.
                    captured.InvokeOriginalCapture(() => capturePin(captured));
                }, token), callbacks.Scope, callbacks.Retain).ConfigureAwait(false);
        }
        catch (Exception cause) { failures.Add(cause); }
        if (failures.Count != 0 && captured is not null)
        {
            Task? close = null;
            try { close = captured.CloseAndDrainOriginalAsync(); await close.ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Original generated UI Den acquisition and cleanup failed.", failures);
        return captured ?? throw new InvalidOperationException("No same original generated UI revision pin was captured.");
     }

    // Fresh Den membership precedes canonical SQL. The process reader proves
    // the actual canonical conversation under its own Home-authorized native lease.
    private sealed record GeneratedUiDenObservation(AuthenticatedResourceActor Actor, Conversation Conversation);
    private async Task<GeneratedUiDenObservation> ValidateOriginalGeneratedUiDenWithinSourceAsync(
        AssistantConversationBinding binding, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            if (!_membership.IsIssuedOriginalBinding(binding))
                throw new AssistantCommandRefusedException("Only this actual Den source's privately issued binding is accepted.");
            return Task.FromResult(true);
        }, scope, retain).ConfigureAwait(false);
        var home = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.OpenHomeWithinSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        var definition = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.DefinitionWithinSourceAsync(home, binding.Definition.Identity, scope, retain, token), scope, retain).ConfigureAwait(false);
        var session = await CaptureOriginalConfigurationCapabilitySourceAsync(() => home.Den.GetAsync<SessionRecord>(
            binding.Definition.Identity.NamespaceId, binding.DenSessionId, token), scope, retain).ConfigureAwait(false);
        var current = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
        {
            var metadata = session is null ? null : AssistantCanonicalMembershipSource.ReadMetadata<AssistantCanonicalMembershipSource.MembershipMetadata>(
                session, AssistantCanonicalMembershipSource.SessionKey);
            if (session is null || metadata is not { Schema: 1, Publication: "ready" } ||
                definition.Revision != binding.Definition.Revision || session.Revision != binding.DenSessionRevision ||
                metadata.DefinitionId != binding.Definition.Identity.DefinitionId || session.NamespaceId != binding.Definition.Identity.NamespaceId ||
                session.Id != AssistantCanonicalMembershipSource.SessionId(binding.Conversation.Id) ||
                !Guid.TryParse(session.ConversationId, out var id) || id != binding.Conversation.Id ||
                id != metadata.OriginalConversation.Id || !AssistantCanonicalMembershipSource.MatchesMembership(binding.Conversation, metadata))
                throw new AssistantCommandRefusedException("The exact current Den definition/session membership changed before protected canonical READ.");
            return Task.FromResult(new GeneratedUiDenObservation(home.Actor, metadata.OriginalConversation));
        }, scope, retain).ConfigureAwait(false);
        var fresh = await CaptureOriginalConfigurationCapabilitySourceAsync(() =>
            _membership.OpenHomeWithinSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        if (fresh.Actor != current.Actor) throw new AssistantCommandRefusedException("The actual Home actor changed during Den membership observation.");
        return current;
    }

    private sealed class GeneratedUiBindingPin : ICanonicalGeneratedUiOriginalSourcePin
    {
        private readonly AssistantConversationBinding _binding;
        private readonly DenStore.AssistantRevisionPin _actual;
        private readonly Action<Action> _scope;
        private readonly Action<Task> _retain;
        private readonly object _gate = new();
        private readonly AsyncLocal<int> _logical = new();
        [ThreadStatic] private static HashSet<GeneratedUiBindingPin>? _physical;
        private readonly List<Task> _raw = [];
        private readonly List<Exception> _callbackErrors = [];
        private Task? _close;
        private Task? _rawClose;
        private bool _rawCloseJoined;
        internal GeneratedUiBindingPin(ICanonicalGeneratedUiOriginalSnapshot snapshot, AssistantConversationBinding binding,
            DenStore.AssistantRevisionPin actual, Action<Action> scope, Action<Task> retain)
        { OriginalSnapshot = snapshot; _binding = binding; _actual = actual; _scope = scope; _retain = retain; }
        public ICanonicalGeneratedUiOriginalSnapshot OriginalSnapshot { get; }
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public bool IsOwnedOriginalHealthyClose(Task sameClose)
        {
            lock (_gate) return ReferenceEquals(sameClose, _close) && sameClose.IsCompletedSuccessfully &&
                _rawCloseJoined && _rawClose is { IsCompletedSuccessfully: true } &&
                _callbackErrors.Count == 0;
        }
        public void DemandOriginalCurrent()
        {
            lock (_gate)
            {
                if (_close is not null || _actual.DefinitionId != _binding.Definition.Identity.DefinitionId ||
                    _actual.DefinitionRevision != _binding.Definition.Revision || _actual.SessionId != _binding.DenSessionId ||
                    _actual.SessionRevision != _binding.DenSessionRevision)
                    throw new ObjectDisposedException(nameof(GeneratedUiBindingPin));
                _actual.DemandOriginalPinnedRevisions();
            }
        }
        public void DemandExternalOriginalJoin()
        {
            if (_logical.Value != 0 || _physical?.Contains(this) == true)
                throw new InvalidOperationException("The same child pin callback cannot synchronously join its own close.");
        }
        internal void InvokeOriginalCapture(Action body)
        {
            var marks = _physical ??= new(ReferenceEqualityComparer.Instance); var added = marks.Add(this);
            try { body(); } finally { if (added) marks.Remove(this); }
        }
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); Task actual; TaskCompletionSource? start = null;
            lock (_gate)
            {
                if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
                actual = _close;
            }
            start?.SetResult(); return actual;
        }
        private async Task Close(Task begin)
        {
            await begin.ConfigureAwait(false); var before = _logical.Value; _logical.Value = before + 1;
            Task? raw = null; var errors = new List<Exception>();
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            void Remember(Exception cause) { lock (_gate) _callbackErrors.Add(cause); }
            void Body()
            {
                if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId || Interlocked.Exchange(ref used, 1) != 0)
                { var cause = new InvalidOperationException("The original child pin close callback is inactive, repeated or foreign-thread."); Remember(cause); throw cause; }
                try
                {
                    InvokeOriginalCapture(() =>
                    {
                        raw = _actual.CloseAndDrainAsync(); lock (_gate) { _raw.Add(raw); _rawClose = raw; }
                        _retain(raw); // Private same raw custody precedes a failing external retainer.
                    });
                }
                catch (Exception cause) { Remember(cause); throw; }
            }
            try
            {
                try { _scope(Body); } catch (Exception cause) { errors.Add(cause); }
                Volatile.Write(ref active, 0); // The borrowed synchronous callback ended before any wait/fallback.
                if (Volatile.Read(ref used) == 0) errors.Add(new InvalidOperationException("The actual child pin close callback was not invoked."));
                // If no factory entered, cleanup is still necessary and allowed under
                // this child's own physical guard. Never replay an entered close.
                if (raw is null && Volatile.Read(ref used) == 0)
                    try { InvokeOriginalCapture(() => { raw = _actual.CloseAndDrainAsync(); lock (_gate) { _raw.Add(raw); _rawClose = raw; } }); }
                    catch (Exception cause) { errors.Add(cause); }
                if (raw is not null)
                    try { await raw.ConfigureAwait(false); lock (_gate) _rawCloseJoined = true; }
                    catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
                lock (_gate) errors.AddRange(_callbackErrors);
                if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
                if (errors.Count != 0) throw new AggregateException("The same actual child pin close and callback originals failed.", errors);
            }
            finally { Volatile.Write(ref active, 0); _logical.Value = before; }
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
}
