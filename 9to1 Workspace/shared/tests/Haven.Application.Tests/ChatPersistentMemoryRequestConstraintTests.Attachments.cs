using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

// Controlled source/transport custody tests only. Genuine Files/Home/SQLite
// provenance and available models are separate owning integration requirements.
public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    private static readonly List<object[]> RetainedAttachmentConsumerControls = [];
    [Fact]
    public async Task Controlled_attachment_input_uses_user_context_and_exact_accepted_message_write_before_dispatch()
    {
        var rig = new Rig(new MemorySource()); var source = new AttachmentConsumerSource(rig.OriginalChat);
        rig.OriginalChat.BindOriginalAttachmentInputSource(source);
        RetainedAttachmentConsumerControls.Add([rig, source]);
        await RunAttachmentConsumer(rig, source, source.Options);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.Contains(request.Messages, row => row.Role == "user" && row.Content.Contains(AttachmentConsumerSource.Text, StringComparison.Ordinal));
        Assert.DoesNotContain(AttachmentConsumerSource.Text, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.Equal(1, source.Reads); Assert.True(source.AcceptedValidations >= 2);
        var accepted = Assert.IsType<ChatOriginalAttachmentAcceptance>(source.Acceptance);
        Assert.Equal("Inspect the selected document", accepted.OriginalMessage.Content);
        Assert.Equal(source.Conversation.Id, accepted.OriginalMessage.ConversationId);
        Assert.True(rig.OriginalChat.IsIssuedOriginalAttachmentRequest(source.FirstRequest!, source.Input, source));
        Assert.Same(accepted, rig.OriginalChat.ObserveOriginalAttachmentAcceptance(source.FirstRequest!, source.Input, source));
    }

    [Fact]
    public async Task Controlled_attachment_input_refuses_another_invocation_and_restored_lineage_never_restores_source()
    {
        var rig = new Rig(new MemorySource()); var source = new AttachmentConsumerSource(rig.OriginalChat);
        rig.OriginalChat.BindOriginalAttachmentInputSource(source); RetainedAttachmentConsumerControls.Add([rig, source]);
        await RunAttachmentConsumer(rig, source, source.Options);
        var replay = await Record.ExceptionAsync(() => RunAttachmentConsumer(rig, source, source.Options));
        AssertOnlyAttachmentCause(replay, source.ReplayFailure);
        Assert.Single(rig.Provider.Requests);
        var payload = JsonSerializer.Serialize(source.Options);
        Assert.DoesNotContain(nameof(GenerationOptions.OriginalAttachmentInput), payload, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<GenerationOptions>(payload)!;
        Assert.Null(restored.OriginalAttachmentInput); Assert.NotNull(restored.RequestedOriginalAttachmentLineage);
        var refused = await Record.ExceptionAsync(() => RunAttachmentConsumer(rig, source, restored));
        Assert.True(HasMessage(refused, "Attachment input requires its live approved source and local model route. Restored IDs cannot authorize it."));
        Assert.Single(rig.Provider.Requests); Assert.Equal(1, source.Reads);
    }

    [Fact]
    public async Task Controlled_attachment_raw_and_post_callback_faults_are_both_retained_without_provider_dispatch()
    {
        var rig = new Rig(new MemorySource()); var source = new AttachmentConsumerSource(rig.OriginalChat);
        rig.OriginalChat.BindOriginalAttachmentInputSource(source); RetainedAttachmentConsumerControls.Add([rig, source]);
        var rawFailure = new IOException("Exact attachment materialization failure");
        var foreignEmpty = new AggregateException("Opaque foreign empty attachment failure");
        var post = new IOException("Exact attachment callback postguard failure");
        var raw = new TaskCompletionSource<AttachmentPromptContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([rawFailure, foreignEmpty]); source.Raw = raw.Task;
        var thrown = false;
        var observed = await Record.ExceptionAsync(() => RunAttachmentConsumer(rig, source, source.Options, body =>
        {
            body();
            if (source.Reads == 1 && !thrown) { thrown = true; throw post; }
        }));
        Assert.True(thrown); Assert.True(HasOriginal(observed, rawFailure)); Assert.True(HasOriginal(observed, foreignEmpty));
        Assert.True(HasOriginal(observed, post)); AssertOnlyAttachmentCause(observed, rawFailure, foreignEmpty, post);
        Assert.Empty(rig.Provider.Requests); Assert.Null(source.Acceptance); Assert.True(raw.Task.IsFaulted);
    }

    [Fact]
    public async Task Controlled_attachment_expected_selection_is_enumerated_only_inside_the_original_scope()
    {
        var rig = new Rig(new MemorySource()); var source = new AttachmentConsumerSource(rig.OriginalChat);
        rig.OriginalChat.BindOriginalAttachmentInputSource(source); RetainedAttachmentConsumerControls.Add([rig, source]);
        var depth = 0; var reads = 0; var options = source.Options;
        var lineage = options.RequestedOriginalAttachmentLineage!;
        var guarded = new GuardedAttachmentIds(lineage.AttachmentIds, () =>
        {
            Assert.True(depth > 0, "The caller-provided selection escaped the actual original synchronous scope."); reads++;
        });
        options = options with { RequestedOriginalAttachmentLineage = lineage with { AttachmentIds = guarded } };
        await RunAttachmentConsumer(rig, source, options, body =>
        {
            depth++; try { body(); } finally { depth--; }
        });
        Assert.True(reads > 0); Assert.Single(rig.Provider.Requests);
    }
    private sealed class GuardedAttachmentIds(IReadOnlyList<Guid> actual, Action demand) : IReadOnlyList<Guid>
    {
        public int Count { get { demand(); return actual.Count; } }
        public Guid this[int index] { get { demand(); return actual[index]; } }
        public IEnumerator<Guid> GetEnumerator()
        {
            demand();
            for (var index = 0; index < actual.Count; index++) { demand(); yield return actual[index]; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static async Task RunAttachmentConsumer(Rig rig, AttachmentConsumerSource source, GenerationOptions options, Action<Action>? scope = null)
    {
        var invocation = rig.OriginalChat.CreateOriginalOrdinaryConversation(source.Conversation, "Inspect the selected document",
            rig.Model, EffortLevel.Medium, "Controlled attachment transport", "", options,
            TestContext.Current.CancellationToken, scope ?? (body => body()));
        var originals = new List<Task>(); var errors = new List<Exception>();
        RetainedAttachmentConsumerControls.Add([rig, source, invocation, originals, errors]);
        try { await foreach (var _ in invocation.ConsumeOriginal()) { } }
        catch (Exception cause) { errors.Add(cause); }
        Task? close = null;
        try { close = invocation.JoinOriginalSourcesAsync(); originals.Add(close); await close; }
        catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Original controlled attachment consumer and independent join.", errors);
    }
    private static void AssertOnlyAttachmentCause(Exception? actual, params Exception[] expected)
    {
        Assert.NotNull(actual);
        if (expected.Any(value => ReferenceEquals(actual, value))) return;
        var group = Assert.IsAssignableFrom<AggregateException>(actual);
        Assert.NotEmpty(group.InnerExceptions);
        foreach (var cause in group.InnerExceptions) AssertOnlyAttachmentCause(cause, expected);
    }
    private sealed class AttachmentConsumerSource(ChatSessionService chat) : IChatOriginalAttachmentInputSource
    {
        private sealed class Marker : IChatOriginalAttachmentInput { }
        internal const string Text = "Controlled attached text stays in user content.";
        internal readonly IChatOriginalAttachmentInput Input = new Marker();
        internal readonly Conversation Conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Controlled attachment consumer", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        private readonly Guid _id = Guid.NewGuid();
        internal readonly Exception ReplayFailure = new UnauthorizedAccessException("Exact controlled original-input replay refusal");
        internal ChatOriginalAttachmentRequest? FirstRequest;
        internal ChatOriginalAttachmentAcceptance? Acceptance;
        internal int Reads, AcceptedValidations;
        internal Task<AttachmentPromptContext>? Raw = null;
        internal GenerationOptions Options => new()
        {
            RequestedRoutingConstraints = new(false, false), RequestedContextConstraints = new(false),
            OriginalAttachmentInput = Input, RequestedOriginalAttachmentLineage = ObserveOriginalAttachmentLineage(Input)
        };
        public bool IsIssuedOriginalAttachmentInput(IChatOriginalAttachmentInput input) => ReferenceEquals(input, Input);
        public ChatOriginalAttachmentLineage ObserveOriginalAttachmentLineage(IChatOriginalAttachmentInput input) =>
            IsIssuedOriginalAttachmentInput(input) ? new(1, Conversation.Id, null, "controlled-source-observation", Array.AsReadOnly(new[] { _id }))
                : throw new UnauthorizedAccessException("Foreign controlled input.");
        private void Demand(IChatOriginalAttachmentInput input, ChatOriginalAttachmentRequest request)
        {
            Assert.True(chat.IsIssuedOriginalAttachmentRequest(request, input, this));
            if (FirstRequest is not null && !chat.IsSameOriginalAttachmentRequest(FirstRequest, request, input, this)) throw ReplayFailure;
            FirstRequest ??= request;
            Acceptance = chat.ObserveOriginalAttachmentAcceptance(request, input, this);
        }
        public Task<AttachmentPromptContext> ReadOriginalAttachmentInputWithinSourceAsync(IChatOriginalAttachmentInput input,
            ChatOriginalAttachmentRequest request, ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() => Demand(input, request)); Reads++;
            if (Raw is not null) return Raw;
            var now = DateTimeOffset.UtcNow;
            MessageAttachment row = new(_id, Conversation.Id, null, null, "controlled.txt", "", "text/plain", MessageAttachmentKind.PlainText,
                Text.Length, "controlled", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, Text, "{}", now, now);
            return Task.FromResult(new AttachmentPromptContext([], Text, [], [row]));
        }
        public Task ValidateOriginalAttachmentInputWithinSourceAsync(IChatOriginalAttachmentInput input,
            ChatOriginalAttachmentRequest request, ProviderExecutionContext? context, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() => { Demand(input, request); if (Acceptance is not null) AcceptedValidations++; }); return Task.CompletedTask;
        }
    }
}
