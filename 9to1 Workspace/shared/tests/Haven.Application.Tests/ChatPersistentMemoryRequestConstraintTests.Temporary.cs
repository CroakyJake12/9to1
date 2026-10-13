using System.Reflection;
using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_memory_has_no_read_without_a_fresh_live_choice(bool? existingPreference)
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        await CollectTemporary(TemporarySend(rig, new GenerationOptions
            { RequestedContextConstraints = new(existingPreference) }, send.Token));
        Assert.Equal(0, source.Reads);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.False(request.Options!.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.DoesNotContain(MemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Temporary_allow_reads_once_and_does_not_expose_live_choice_to_provider_options()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var choice = rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true);
        await CollectTemporary(TemporarySend(rig, new GenerationOptions { TemporaryMemoryReadChoice = choice }, send.Token));
        Assert.Equal(1, source.Reads);
        var request = Assert.Single(rig.Provider.Requests);
        Assert.Contains(MemorySource.Sentinel, request.SystemPrompt ?? "", StringComparison.Ordinal);
        Assert.Null(request.Options!.TemporaryMemoryReadChoice);
        Assert.True(request.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
    }

    [Fact]
    public async Task Temporary_choice_retains_original_send_identity_when_enumeration_adds_cancellation()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource();
        var stream = TemporarySend(rig, new GenerationOptions { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) }, send.Token);
        await foreach (var _ in stream.WithCancellation(enumeration.Token)) { }
        Assert.Equal(1, source.Reads);
        Assert.Contains(MemorySource.Sentinel, Assert.Single(rig.Provider.Requests).SystemPrompt ?? "", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectTemporary(stream));
        Assert.Equal(1, source.Reads);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Temporary_choice_intersects_existing_memory_restrictions(bool choiceAllows, bool sourcePreferenceAllows)
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var choice = rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, choiceAllows);
        await CollectTemporary(TemporarySend(rig, new GenerationOptions
            { TemporaryMemoryReadChoice = choice, RequestedContextConstraints = new(sourcePreferenceAllows) }, send.Token));
        Assert.Equal(0, source.Reads);
        Assert.False(Assert.Single(rig.Provider.Requests).Options!.RequestedContextConstraints!.AllowPersistentMemoryRead);
    }

    [Fact]
    public async Task Temporary_choice_cannot_be_reused_for_later_send_but_a_new_choice_can_read()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var options = new GenerationOptions { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) };
        await CollectTemporary(TemporarySend(rig, options, send.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectTemporary(TemporarySend(rig, options, send.Token)));
        Assert.Equal(1, source.Reads); Assert.Single(rig.Provider.Requests);
        using var nextSend = new CancellationTokenSource();
        var next = options with { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", nextSend.Token, true) };
        await CollectTemporary(TemporarySend(rig, next, nextSend.Token));
        Assert.Equal(2, source.Reads); Assert.Equal(2, rig.Provider.Requests.Count);
    }

    [Fact]
    public async Task Reenumerating_the_same_returned_stream_does_not_reuse_temporary_memory_choice()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var stream = TemporarySend(rig, new GenerationOptions { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) }, send.Token);
        await CollectTemporary(stream);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectTemporary(stream));
        Assert.Equal(1, source.Reads); Assert.Single(rig.Provider.Requests);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("conversation")]
    [InlineData("prompt")]
    [InlineData("token")]
    [InlineData("copied")]
    [InlineData("saved")]
    public async Task Temporary_choice_refuses_foreign_stale_or_copied_request_before_any_memory_or_provider_work(string change)
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource(); using var otherSend = new CancellationTokenSource();
        var choice = rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true);
        if (change == "copied") choice = Assert.IsType<TemporaryChatMemoryReadChoice>(typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(choice, null));
        var chat = change == "service" ? new Rig(source, temporary: true).OriginalChat : rig.OriginalChat;
        var conversation = change switch
        {
            "conversation" => rig.OriginalConversation with { },
            "saved" => rig.OriginalConversation with { IsTemporary = false },
            _ => rig.OriginalConversation
        };
        var stream = chat.SendAsync(conversation, change == "prompt" ? "Changed" : "Hello", rig.Model,
            EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null,
            change == "token" ? otherSend.Token : send.Token,
            generationOptions: new GenerationOptions { TemporaryMemoryReadChoice = choice });
        await Assert.ThrowsAsync<InvalidOperationException>(() => CollectTemporary(stream));
        Assert.Equal(0, source.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Cancelled_temporary_send_preserves_original_cancellation_before_memory_or_provider_work()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var options = new GenerationOptions { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) };
        var stream = TemporarySend(rig, options, send.Token);
        send.Cancel();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectTemporary(stream));
        Assert.Equal(send.Token, cancelled.CancellationToken);
        Assert.Equal(0, source.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public async Task Serialized_temporary_options_cannot_restore_a_memory_choice()
    {
        var source = new MemorySource(); var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var options = new GenerationOptions { RequestedContextConstraints = new(true), TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) };
        var json = JsonSerializer.Serialize(options);
        Assert.DoesNotContain(nameof(GenerationOptions.TemporaryMemoryReadChoice), json, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<GenerationOptions>(json)!;
        Assert.Null(restored.TemporaryMemoryReadChoice);
        await CollectTemporary(TemporarySend(rig, restored, send.Token));
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task Temporary_allow_preserves_exact_actual_memory_source_refusal()
    {
        var refusal = new UnauthorizedAccessException("Actual temporary source refuses access.");
        var source = new MemorySource { Refusal = refusal }; var rig = new Rig(source, temporary: true);
        using var send = new CancellationTokenSource();
        var options = new GenerationOptions { TemporaryMemoryReadChoice =
            rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", send.Token, true) };
        var observed = await Record.ExceptionAsync(() => CollectTemporary(TemporarySend(rig, options, send.Token)));
        Assert.Same(refusal, observed); Assert.Equal(1, source.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_absent_or_denied_choice_skips_the_scoped_source_and_legacy_reader(bool issueDenial)
    {
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource();
        var rig = new Rig(legacy, scoped, temporary: true);
        var options = ScopedOptions(scoped) with { TemporaryMemoryReadChoice = issueDenial
            ? rig.OriginalChat.RecordTemporaryMemoryReadChoice(rig.OriginalConversation, "Hello", TestContext.Current.CancellationToken, false)
            : null };
        await rig.SendOwnedAsync(options);
        Assert.Equal(0, scoped.Reads); Assert.Equal(0, scoped.Validations); Assert.Equal(0, legacy.Reads);
        Assert.Single(rig.Provider.Requests);
    }

    [Fact]
    public async Task Temporary_allow_cannot_fall_back_around_an_original_scoped_source_refusal()
    {
        var refusal = new UnauthorizedAccessException("Actual scoped temporary source refuses access.");
        var legacy = new MemorySource(); var scoped = new ScopedMemorySource
            { InlineRead = Task.FromException<IReadOnlyList<KnowledgeRecord>>(refusal) };
        var rig = new Rig(legacy, scoped, temporary: true);
        var options = ScopedOptions(scoped) with { TemporaryMemoryReadChoice = rig.OriginalChat.RecordTemporaryMemoryReadChoice(
            rig.OriginalConversation, "Hello", TestContext.Current.CancellationToken, true) };
        var observed = await Record.ExceptionAsync(() => rig.SendOwnedAsync(options));
        Assert.True(HasOriginal(observed, refusal));
        Assert.Equal(1, scoped.Reads); Assert.Equal(0, legacy.Reads); Assert.Empty(rig.Provider.Requests);
    }

    [Fact]
    public void Temporary_choice_requires_an_actual_active_cancellable_send_lifetime()
    {
        var rig = new Rig(new MemorySource(), temporary: true);
#pragma warning disable xUnit1051 // This negative control must supply an uncancellable lifetime.
        Assert.Throws<ArgumentException>(() => rig.OriginalChat.RecordTemporaryMemoryReadChoice(
            rig.OriginalConversation, "Hello", default, true));
#pragma warning restore xUnit1051
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => rig.OriginalChat.RecordTemporaryMemoryReadChoice(
            rig.OriginalConversation, "Hello", cancelled.Token, true));
    }

    private static IAsyncEnumerable<ChatStreamEvent> TemporarySend(Rig rig, GenerationOptions options, CancellationToken token) =>
        rig.OriginalChat.SendAsync(rig.OriginalConversation, "Hello", rig.Model, EffortLevel.Medium,
            [], "Controlled", "", DuoMode.Solo, null, null, null, null, token, generationOptions: options);

    private static async Task CollectTemporary(IAsyncEnumerable<ChatStreamEvent> stream)
    { await foreach (var _ in stream) { } }
}
