using Haven.Core;

namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private static bool UsesOriginalPersistentMemory(GenerationOptions? options) =>
        options?.OriginalPersistentMemoryInput is not null ||
        options?.RequestedContextConstraints?.RequireOriginalPersistentMemoryInput == true;

    /// <summary>Pure SAME configured source observation; never authorization.</summary>
    public bool HasOriginalPersistentMemorySource(IChatOriginalPersistentMemorySource source) =>
        ReferenceEquals(OriginalPersistentMemorySource, source);

    private void DemandOriginalPersistentMemoryRequest(GenerationOptions? options,
        ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical)
    {
        if (options is null || !UsesOriginalPersistentMemory(options) || options.RequestedContextConstraints?.AllowPersistentMemoryRead == false) return;
        var actualOwner = OriginalPersistentMemorySource;
        if (actualOwner is null || options.OriginalPersistentMemoryInput is not { } input ||
            (ordinary is null && canonical?.OriginalProcessProducer is null))
            throw new InvalidOperationException("Persistent-memory opt-in requires the SAME live scoped source and original Chat producer.");
        var source = new OriginalMemorySourceScope(ordinary, canonical);
        if (!source.Observe(() => actualOwner.IsIssuedOriginalInput(input)))
            throw new InvalidOperationException("The scoped memory input was not issued by this SAME configured source.");
        if (actualOwner is IChatOriginalPersistentMemoryColdSource lineageSource &&
            !source.Observe(() => options.RequestedPersistentMemoryLineage is { Schema: 1 } expected &&
                expected.ConversationId != Guid.Empty && lineageSource.ObserveOriginalLineage(input) == expected))
            throw new InvalidOperationException("The SAME live memory source did not issue the requested durable lineage.");
    }

    private async Task<IReadOnlyList<KnowledgeRecord>> ReadOriginalPersistentMemoryAsync(
        GenerationOptions options, Conversation actualConversation, ProviderExecutionContext? actualContext,
        ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical, CancellationToken token)
    {
        DemandOriginalPersistentMemoryRequest(options, ordinary, canonical);
        var owner = OriginalPersistentMemorySource!;
        var input = options.OriginalPersistentMemoryInput!;
        var source = new OriginalMemorySourceScope(ordinary, canonical);
        var records = await source.Read(() => owner.ReadOriginalWithinSourceAsync(input,
            actualConversation, actualContext, source.Run, source.Retain, token)).ConfigureAwait(false);
        var snapshot = source.Observe(() => Array.AsReadOnly(records.ToArray()));
        await source.Read(() => owner.ValidateOriginalWithinSourceAsync(input,
            actualConversation, actualContext, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!source.Observe(() => owner.IsIssuedOriginalInput(input)))
            throw new InvalidOperationException("The SAME scoped memory input changed before context selection.");
        return snapshot;
    }

    private async Task ValidateOriginalPersistentMemoryAsync(GenerationOptions? options,
        Conversation actualConversation, ProviderExecutionContext? actualContext,
        ChatOrdinaryOriginalInvocation? ordinary, TaskRunInvocationCustody? canonical, CancellationToken token)
    {
        if (options is null || !UsesOriginalPersistentMemory(options) || options.RequestedContextConstraints?.AllowPersistentMemoryRead == false) return;
        DemandOriginalPersistentMemoryRequest(options, ordinary, canonical);
        var owner = OriginalPersistentMemorySource!;
        var input = options.OriginalPersistentMemoryInput!;
        var source = new OriginalMemorySourceScope(ordinary, canonical);
        await source.Read(() => owner.ValidateOriginalWithinSourceAsync(input,
            actualConversation, actualContext, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (!source.Observe(() => owner.IsIssuedOriginalInput(input)))
            throw new InvalidOperationException("The SAME scoped memory input changed before actual provider dispatch.");
    }

    /// <summary>Actual child Tasks are retained before source post-checks and independently
    /// joined before any returned memory can enter a provider request. Raw child roles are
    /// conservatively unknown: cancellation never waives an accepted cleanup/write sibling.</summary>
    private sealed class OriginalMemorySourceScope(ChatOrdinaryOriginalInvocation? ordinary,
        TaskRunInvocationCustody? canonical)
    {
        private readonly TaskRunColdOriginalSourceScope? _coldSource;
        private readonly Action<Task>? _coldRetain;
        internal OriginalMemorySourceScope(TaskRunColdOriginalSourceScope sameColdSource, Action<Task> sameRetainer)
            : this((ChatOrdinaryOriginalInvocation?)null, (TaskRunInvocationCustody?)null) { _coldSource = sameColdSource; _coldRetain = sameRetainer; }
        private readonly object _gate = new();
        private readonly List<Task> _tasks = [];
        private const int MaximumActualChildren = 256;
        internal void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            lock (_gate)
            {
                if (_tasks.Any(prior => ReferenceEquals(prior, actual))) return;
                _tasks.Add(actual); // Always retain an already accepted raw original.
            }
            if (_coldRetain is not null) _coldRetain(actual);
            else if (ordinary is not null) ordinary.RetainUnknownMemoryOriginal(actual);
            else (canonical ?? throw new InvalidOperationException("No original memory source custody exists."))
                .RetainAdditionalOriginal("memory.scoped-source", actual);
        }
        internal T Observe<T>(Func<T> operation)
        {
            T result = default!;
            Run(() => result = operation());
            return result;
        }
        internal void Run(Action operation)
        {
            var sourceThread = Environment.CurrentManagedThreadId;
            var active = true; var claimed = false;
            void Once()
            {
                lock (_gate)
                {
                    if (!active || claimed || sourceThread != Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("Acquire each original memory source synchronously once.");
                    if (_tasks.Count >= MaximumActualChildren)
                        throw new InvalidOperationException("Original memory source child custody requires settlement before another acquisition.");
                    claimed = true;
                }
                operation();
            }
            try
            {
                if (_coldSource is not null) _coldSource.Invoke(() => { Once(); return true; });
                else if (ordinary is not null) ordinary.InvokeOriginal(Once);
                else (canonical?.OriginalProcessProducer ?? throw new InvalidOperationException("The SAME original Task producer is unavailable."))
                    .InvokeOriginalCallback(Once);
                if (!claimed) throw new InvalidOperationException("The actual memory source scope did not acquire its original.");
            }
            finally { lock (_gate) active = false; }
        }
        internal async Task<T> Read<T>(Func<Task<T>> operation)
        {
            Task<T>? actual = null; T result = default!; var failures = new List<Exception>();
            try { Run(() => { actual = operation(); Retain(actual); }); }
            catch (Exception scopeFailure) { failures.Add(scopeFailure); }
            // A post-callback scope refusal is an independent original cause. It must not
            // be replaced by the accepted Task's exception, even if that Task faulted inline.
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); }
                catch (Exception failure) { AddOriginalFailure(failures, actual, failure); }
            await JoinChildren(failures).ConfigureAwait(false);
            Throw(failures); return result;
        }
        internal async Task Read(Func<Task> operation)
        {
            Task? actual = null; var failures = new List<Exception>();
            try { Run(() => { actual = operation(); Retain(actual); }); }
            catch (Exception scopeFailure) { failures.Add(scopeFailure); }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); }
                catch (Exception failure) { AddOriginalFailure(failures, actual, failure); }
            await JoinChildren(failures).ConfigureAwait(false);
            Throw(failures);
        }
        private async Task JoinChildren(List<Exception> failures)
        {
            Task[] originals; lock (_gate) originals = _tasks.ToArray();
            foreach (var actual in originals)
                try { await actual.ConfigureAwait(false); }
                catch (Exception failure) { AddOriginalFailure(failures, actual, failure); }
            // Remove only independently joined successful originals. Unknown outcomes remain.
            lock (_gate)
                foreach (var actual in originals)
                    if (actual.IsCompletedSuccessfully) _tasks.Remove(actual);
        }
        private static void AddOriginalFailure(List<Exception> failures, Task? actual, Exception observed)
        {
            if (actual?.Exception is { } wrapper) failures.AddRange(wrapper.InnerExceptions);
            else failures.Add(observed);
        }
        private static void Throw(List<Exception> failures)
        {
            if (failures.Count != 0)
                throw new AggregateException("The actual scoped memory originals did not settle cleanly.",
                    failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
        }
    }
}
