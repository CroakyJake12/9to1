using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// D04 requires the transcript to be rendered as one canonical ordered event stream.
/// These cover the projection owner, which is where that ordering must be imposed.
/// </summary>
public sealed class ChatSpaceProjectionChronologyTests
{
    [Fact]
    public void Messages_are_projected_in_recorded_chronology_not_repository_array_order()
    {
        var result = ChatSpaceProjection.Project(Conversation(
            Message(MessageRole.User, "request", At(0)),
            Message(MessageRole.Assistant, "work", At(1)),
            Message(MessageRole.Tool, "tool result", At(2)),
            Message(MessageRole.Assistant, "final result", At(3))));

        Assert.Equal(
            ["request", "work", "tool result", "final result"],
            result.Messages.Select(message => message.Content));
    }

    [Fact]
    public void Work_returned_after_the_result_it_concludes_is_reordered_before_it()
    {
        // An array that yields the result before its own work must not make the transcript
        // read as if the answer preceded the work that produced it.
        var result = ChatSpaceProjection.Project(Conversation(
            Message(MessageRole.User, "request", At(0)),
            Message(MessageRole.Assistant, "final result", At(5)),
            Message(MessageRole.Assistant, "work", At(2))));

        Assert.Equal(
            ["request", "work", "final result"],
            result.Messages.Select(message => message.Content));
    }

    [Fact]
    public void Events_recorded_at_the_same_instant_keep_their_recorded_order()
    {
        var shared = At(0);
        var result = ChatSpaceProjection.Project(Conversation(
            Message(MessageRole.User, "first", shared),
            Message(MessageRole.Assistant, "second", shared),
            Message(MessageRole.Assistant, "third", shared)));

        Assert.Equal(
            ["first", "second", "third"],
            result.Messages.Select(message => message.Content));
    }

    [Fact]
    public void Chronological_ordering_preserves_canonical_message_identity()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var result = ChatSpaceProjection.Project(Conversation(
            new ChatMessage(ids[0], ConversationId, MessageRole.User, "request", null, null, null, At(3)),
            new ChatMessage(ids[1], ConversationId, MessageRole.Assistant, "work", null, null, null, At(1)),
            new ChatMessage(ids[2], ConversationId, MessageRole.Assistant, "result", null, null, null, At(2))));

        // Reordering for display must never rewrite identity: the same IDs survive, re-ranked.
        Assert.Equal([ids[1], ids[2], ids[0]], result.Messages.Select(message => message.Id));
    }

    private static readonly Guid ConversationId = Guid.Parse("b1000000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int seconds) => T0 + TimeSpan.FromSeconds(seconds);

    private static ChatMessage Message(MessageRole role, string content, DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), ConversationId, role, content, null, null, null, createdAt);

    private static ChatSpaceConversationData Conversation(params ChatMessage[] messages) => new(
        new Conversation(
            ConversationId,
            HavenMode.Chat,
            ConversationKind.Chat,
            "Chronology",
            null,
            null,
            false,
            false,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch),
        messages,
        [],
        [],
        null);
}