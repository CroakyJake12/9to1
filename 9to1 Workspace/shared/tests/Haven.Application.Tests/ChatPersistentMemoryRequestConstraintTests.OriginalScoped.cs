using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

/// <summary>Actual maintained Chat/source consumer controls with controlled scoped source
/// originals. Real Home/Den/SQLite memory authority belongs to the owning integration suite.</summary>
public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    private static GenerationOptions ScopedOptions(ScopedMemorySource source, bool allow = true) => new()
    {
        RequestedRoutingConstraints = new(false, false),
        RequestedContextConstraints = new(allow) { RequireOriginalPersistentMemoryInput = true },
        OriginalPersistentMemoryInput = source.Input
    };

    [Fact]
    public async Task Scoped_memory_opt_out_ignores_even_actual_input_without_reading_either_source()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy, scoped);
        await rig.SendOwnedAsync(ScopedOptions(scoped, false));
        Assert.Equal(0, legacy.Reads); Assert.Equal(0, scoped.Reads + scoped.Validations);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.DoesNotContain(ScopedMemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(MemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Required_missing_memory_input_refuses_before_legacy_read_or_model_dispatch()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy, scoped);
        var options = ScopedOptions(scoped) with { OriginalPersistentMemoryInput = null };
        var refused = await Record.ExceptionAsync(() => rig.SendOwnedAsync(options));
        Assert.True(HasMessage(refused, "Persistent-memory opt-in requires the SAME live scoped source and original Chat producer."));
        Assert.Equal(0, legacy.Reads); Assert.Equal(0, scoped.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Foreign_scoped_memory_input_cannot_fall_back_to_global_reader()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var foreign = new ScopedMemorySource();
        var rig = new Rig(legacy, scoped);
        var refused = await Record.ExceptionAsync(() => rig.SendOwnedAsync(ScopedOptions(foreign)));
        Assert.True(HasMessage(refused, "The scoped memory input was not issued by this SAME configured source."));
        Assert.Equal(0, legacy.Reads); Assert.Equal(0, scoped.Reads + foreign.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Same_scoped_memory_feeds_existing_injection_and_revalidates_before_dispatch()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy, scoped);
        await rig.SendOwnedAsync(ScopedOptions(scoped));
        Assert.Equal(0, legacy.Reads); Assert.Equal(1, scoped.Reads); Assert.True(scoped.Validations >= 2);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.Contains(ScopedMemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(MemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.Same(scoped.Input, request.Options!.OriginalPersistentMemoryInput);
    }

    [Fact]
    public async Task Durable_memory_requirement_survives_without_serializing_live_permission()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy, scoped);
        var payload = JsonSerializer.Serialize(ScopedOptions(scoped));
        Assert.DoesNotContain(nameof(GenerationOptions.OriginalPersistentMemoryInput), payload, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<GenerationOptions>(payload)!;
        Assert.True(restored.RequestedContextConstraints!.RequireOriginalPersistentMemoryInput);
        Assert.Null(restored.OriginalPersistentMemoryInput);
        var refused = await Record.ExceptionAsync(() => rig.SendOwnedAsync(restored));
        Assert.True(HasMessage(refused, "Persistent-memory opt-in requires the SAME live scoped source and original Chat producer."));
        Assert.Equal(0, legacy.Reads); Assert.Equal(0, scoped.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Scoped_post_callback_fault_retains_actual_held_child_and_its_independent_failure()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource { HoldChild = true }; var rig = new Rig(legacy, scoped);
        var postScope = new IOException("Actual memory scope post-callback failure");
        var childFault = new IOException("Actual accepted memory child independent failure");
        var failed = false;
        var actual = rig.SendOwnedAsync(ScopedOptions(scoped), callback =>
        {
            callback();
            if (scoped.ChildStarted.Task.IsCompleted && !failed) { failed = true; throw postScope; }
        });
        Exception? observed = null;
        try
        {
            await Task.WhenAny(scoped.ChildStarted.Task, actual).WaitAsync(TestContext.Current.CancellationToken);
            if (actual.IsCompleted) await actual;
            await scoped.ChildStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(actual.IsCompleted); Assert.False(scoped.Child.Task.IsCompleted);
            Assert.Empty(rig.Provider.Requests); Assert.Equal(0, legacy.Reads);
            scoped.Child.SetException(childFault);
            observed = await Record.ExceptionAsync(() => actual);
            Assert.NotNull(observed); Assert.True(HasOriginal(observed, postScope)); Assert.True(HasOriginal(observed, childFault));
            Assert.True(scoped.Child.Task.IsFaulted); Assert.Empty(rig.Provider.Requests);
        }
        finally
        {
            scoped.Child.TrySetResult();
            try { await actual; } catch when (observed is not null) { }
        }
    }

    [Fact]
    public async Task Scoped_post_callback_refusal_keeps_inline_faulted_task_and_foreign_empty_cause()
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource(); var rig = new Rig(legacy, scoped);
        var postScope = new IOException("Actual inline-memory scope post-callback failure");
        var rawFailure = new IOException("Actual inline-memory raw task failure");
        var foreignEmpty = new AggregateException("Foreign opaque empty memory cause");
        var raw = new TaskCompletionSource<IReadOnlyList<KnowledgeRecord>>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([rawFailure, foreignEmpty]); scoped.InlineRead = raw.Task;
        var rejected = false;
        var observed = await Record.ExceptionAsync(() => rig.SendOwnedAsync(ScopedOptions(scoped), callback =>
        {
            callback();
            if (scoped.Reads == 1 && !rejected) { rejected = true; throw postScope; }
        }));
        Assert.True(rejected); Assert.True(raw.Task.IsFaulted);
        Assert.True(HasOriginal(observed, postScope)); Assert.True(HasOriginal(observed, rawFailure));
        Assert.True(HasOriginal(observed, foreignEmpty)); Assert.Empty(foreignEmpty.InnerExceptions);
        Assert.Equal(0, legacy.Reads); Assert.Empty(rig.Provider.Requests);
    }

    private static bool HasMessage(Exception? actual, string expected) => actual?.Message == expected ||
        actual is AggregateException group && group.InnerExceptions.Any(child => HasMessage(child, expected));

    private static bool HasOriginal(Exception? actual, Exception expected) => ReferenceEquals(actual, expected) ||
        actual is AggregateException group && group.InnerExceptions.Any(child => HasOriginal(child, expected));

    private sealed class ScopedMemorySource : IChatOriginalPersistentMemorySource
    {
        private sealed class Marker(ScopedMemorySource owner) : IChatOriginalPersistentMemoryInput
        { internal ScopedMemorySource Owner => owner; }
        internal const string Sentinel = "same-source-scoped-private-memory";
        internal readonly TaskCompletionSource ChildStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Child = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly IChatOriginalPersistentMemoryInput Input;
        internal bool HoldChild;
        internal Task<IReadOnlyList<KnowledgeRecord>>? InlineRead;
        internal int Reads, Validations;
        internal ScopedMemorySource() => Input = new Marker(this);
        public bool IsIssuedOriginalInput(IChatOriginalPersistentMemoryInput actual) =>
            actual is Marker marker && ReferenceEquals(marker.Owner, this) && ReferenceEquals(Input, actual);
        public Task<IReadOnlyList<KnowledgeRecord>> ReadOriginalWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
            Conversation conversation, ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            if (!IsIssuedOriginalInput(input)) throw new UnauthorizedAccessException("Controlled source issuer refuses foreign input.");
            // The enclosing actual source factory already runs in its physical scope.
            // Return the SAME faulted original so the caller's post-scope guard can race it.
            if (InlineRead is not null) { Reads++; return InlineRead; }
            scope(() =>
            {
                token.ThrowIfCancellationRequested(); Reads++;
                if (HoldChild) { retain(Child.Task); ChildStarted.SetResult(); }
            });
            var now = DateTimeOffset.UtcNow;
            KnowledgeRecord record = new(Guid.NewGuid(), KnowledgeCategory.LearnMe, "scoped", Sentinel,
                Sentinel, KnowledgePrivacyClass.Normal, 1, true, now, now, null, "controlled scoped source", []);
            return Task.FromResult<IReadOnlyList<KnowledgeRecord>>([record]);
        }
        public Task ValidateOriginalWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
            Conversation conversation, ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!IsIssuedOriginalInput(input)) throw new UnauthorizedAccessException("Actual scoped input changed.");
                Validations++;
            });
            return Task.CompletedTask;
        }
    }
}
