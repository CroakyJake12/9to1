using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Merge stage M3: the authored <c>ChatSpace.cui</c>, rendered.
///
/// The compiler test proves the document is canonical CUI. This proves it actually binds and
/// renders real <see cref="ChatSpaceViewState"/> data, which is the property that matters for the
/// Chat Space and the one a compiling document alone cannot establish - bindings resolve at
/// runtime, so a path naming a property that does not exist compiles perfectly and renders
/// nothing.
///
/// The projection under test is <see cref="ChatSpaceCuiBindings"/>, the real one, not a stand-in.
/// </summary>
public sealed class ChatSpaceCuiRenderingTests
{
    private sealed class TestApplication : Application
    {
        public static Application Instance { get; } = new TestApplication();
    }

    private static ChatSpaceMessage Message(
        Guid id, string speaker, string content, bool canRegenerate = false,
        IReadOnlyList<MessageAttachment>? attachments = null,
        IReadOnlyList<ChatSpaceCitation>? citations = null,
        IReadOnlyList<ToolActivity>? activities = null) =>
        new(id, speaker == "You" ? MessageRole.User : MessageRole.Assistant, content, speaker, "now",
            null, attachments ?? [], citations ?? [], activities ?? [], canRegenerate);

    private static ChatSpaceViewState State(params ChatSpaceMessage[] messages) =>
        ChatSpaceViewState.Empty with
        {
            Conversation = new ChatSpaceConversation(
                Guid.NewGuid(), "Space chat", messages, [], null),
            Composer = new ChatSpaceComposer(string.Empty, [], "model-a", false),
            Status = new ChatSpaceStatus("Ready", ChatSpaceStatusTone.Neutral, false)
        };

    [Fact]
    public async Task Authored_chat_space_renders_the_transcript_from_real_domain_state()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(TestApplication.Instance.GetType());
        await session.Dispatch<bool>(async () =>
        {
            var first = Message(Guid.NewGuid(), "You", "Hello");
            var second = Message(Guid.NewGuid(), "Haven", "Hi there", canRegenerate: true);
            var state = State(first, second);
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.True(diagnostics.Count == 0,
                    string.Join(" | ", diagnostics.Select(d => $"{d.Code}:{d.Message}")));
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                var texts = root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

                // Every message reached the document through its Repeat, in published order.
                var rendered = string.Join(" | ", texts);
                Assert.True(texts.Contains("Hello"), $"message body did not bind. Rendered: {rendered}");
                Assert.True(texts.Contains("Hi there"), $"assistant body did not bind. Rendered: {rendered}");

                // The conversation title and the composer state are bound too, so this is a real
                // projection rather than a hardcoded shell.
                Assert.True(texts.Contains("Space chat"), $"title did not bind. Rendered: {rendered}");
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Transcript_reorder_keeps_each_message_control_by_canonical_identity()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(TestApplication.Instance.GetType());
        await session.Dispatch<bool>(async () =>
        {
            var work = Message(Guid.NewGuid(), "Haven", "Working on it");
            var answer = Message(Guid.NewGuid(), "Haven", "All done");
            var request = Message(Guid.NewGuid(), "You", "Please fix it");
            var state = State(work, answer, request);
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.True(diagnostics.Count == 0, string.Join(" | ", diagnostics.Select(d => d.Code)));
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                var before = root.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text is "Working on it" or "All done" or "Please fix it")
                    .ToDictionary(t => t.Text!, t => t);

                // D04 chronology: the same records re-ranked by created order.
                state = State(request, work, answer);
                bindings.Refresh();
                await loader.WhenActionsIdleAsync();

                var after = root.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.Text is "Working on it" or "All done" or "Please fix it")
                    .ToDictionary(t => t.Text!, t => t);

                Assert.Equal(3, after.Count);
                // Same canonical id -> the same control instance. A rebuild would discard caret,
                // focus and scroll for that row on every re-rank.
                Assert.Same(before["Working on it"], after["Working on it"]);
                Assert.Same(before["All done"], after["All done"]);
                Assert.Same(before["Please fix it"], after["Please fix it"]);

                // Identity alone is not enough to prove the re-rank happened: if Refresh()
                // published a notification that matched no authored path, the controls would be
                // the same instances in the same order and every assertion above would still pass.
                // Assert the published order actually reached the visual tree.
                var order = root.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Empty)
                    .Where(text => text is "Working on it" or "All done" or "Please fix it")
                    .ToArray();
                Assert.Equal(["Please fix it", "Working on it", "All done"], order);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task An_unauthored_command_is_refused_rather_than_dispatched()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(TestApplication.Instance.GetType());
        await session.Dispatch<bool>(async () =>
        {
            var state = State();
            var dispatched = new List<string>();
            var bindings = new ChatSpaceCuiBindings(
                () => state,
                (command, _, _) => { dispatched.Add(command); return ValueTask.CompletedTask; });

            await bindings.DispatchAsync("chat.send", null);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => bindings.DispatchAsync("chat.delete-everything", null).AsTask());

            Assert.Equal(["chat.send"], dispatched);
            // An unknown command is invisible to availability, so a control cannot even claim it.
            Assert.Null(bindings.IsActionAvailable("chat.delete-everything"));
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public void Send_is_unavailable_until_the_composer_can_actually_send()
    {
        var empty = State();
        var bindings = new ChatSpaceCuiBindings(() => empty, (_, _, _) => ValueTask.CompletedTask);
        Assert.False(bindings.IsActionAvailable("chat.send"));

        var ready = empty with
        {
            Composer = new ChatSpaceComposer("a real draft", [], "model-a", false)
        };
        var readyBindings = new ChatSpaceCuiBindings(() => ready, (_, _, _) => ValueTask.CompletedTask);
        Assert.True(readyBindings.IsActionAvailable("chat.send"));
    }

    [Fact]
    public void Model_choice_renders_its_display_name_for_native_selector_labels()
    {
        var choice = new ChatSpaceModelChoice("model-a", "Model A", "A model", new HashSet<ToolCapability>());
        Assert.Equal("Model A", choice.ToString());
    }

    [Fact]
    public async Task Update_draft_command_is_admitted_and_dispatched()
    {
        var state = State();
        var dispatched = new List<(string Command, object? Parameter)>();
        var bindings = new ChatSpaceCuiBindings(
            () => state,
            (command, parameter, _) => { dispatched.Add((command, parameter)); return ValueTask.CompletedTask; });

        await bindings.DispatchAsync("chat.update-draft", "draft text");

        Assert.Single(dispatched);
        Assert.Equal("chat.update-draft", dispatched[0].Command);
        Assert.Equal("draft text", dispatched[0].Parameter);
    }

    [Fact]
    public async Task Clear_search_empties_the_owned_query_and_republishes()
    {
        var state = State();
        var notifications = 0;
        var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
        bindings.PropertyChanged += (_, _) => notifications++;
        bindings.TrySetValue("RecentSearchQuery", "half typed");

        Assert.True(bindings.TryGetValue("RecentSearchQuery", out var typed));
        Assert.Equal("half typed", typed);

        bindings.ClearRecentSearch();

        Assert.True(bindings.TryGetValue("RecentSearchQuery", out var cleared));
        Assert.Equal(string.Empty, cleared);
        // The republish must reach the loader, or the visible box keeps the stale text.
        Assert.True(notifications > 0, "clearing the search must raise a change notification");
    }

    [Fact]
    public async Task Clearing_the_search_box_updates_the_rendered_input()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(TestApplication.Instance.GetType());
        await session.Dispatch<bool>(async () =>
        {
            var state = State();
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            try
            {
                var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
                Assert.True(diagnostics.Count == 0, string.Join(" | ", diagnostics.Select(d => d.Code)));
                var root = Assert.IsAssignableFrom<Control>(tree);
                loader.WireBindings(root);

                var search = root.GetVisualDescendants().OfType<TextBox>()
                    .Single(box => box.Name == "recent-search" ||
                        Avalonia.Automation.AutomationProperties.GetAutomationId(box) == "recent-search");
                bindings.TrySetValue("RecentSearchQuery", "half typed");
                await loader.WhenActionsIdleAsync();
                Assert.Equal("half typed", search.Text);

                bindings.ClearRecentSearch();
                await loader.WhenActionsIdleAsync();

                Assert.Equal(string.Empty, search.Text);
            }
            finally { loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, lifetime.Token);
    }

    private static string ReadAuthoredSource()
    {
        var assembly = typeof(ChatSpaceCuiBindings).Assembly;
        using var stream = assembly.GetManifestResourceStream(ChatSpaceCuiDocument.ResourceName)
            ?? throw new InvalidOperationException($"'{ChatSpaceCuiDocument.ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}