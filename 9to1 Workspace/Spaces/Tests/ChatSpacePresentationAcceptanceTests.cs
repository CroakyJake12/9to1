using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class ChatSpacePresentationAcceptanceTests
{
    [Theory]
    [InlineData(390, 844)]
    [InlineData(768, 1024)]
    [InlineData(1280, 800)]
    public async Task Actual_authored_chat_keeps_composer_inside_viewport_and_scrolls_markdown_transcript(int width, int height)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch(async () =>
        {
            var state = State();
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            bindings.UpdateViewportWidth(width);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var (tree, diagnostics) = loader.LoadMarkup(Source());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);
            var window = new Window { Width = width, Height = height, Content = root };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.Measure(new Size(width, height)); window.Arrange(new Rect(0, 0, width, height));
                for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); }
                var input = root.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "composer-input");
                var position = input.TranslatePoint(default, root);
                Assert.NotNull(position);
                Assert.True(input.Bounds.Height > 0);
                Assert.InRange(position.Value.Y + input.Bounds.Height, 1, height);
                Assert.InRange(position.Value.X + input.Bounds.Width, 1, width);
                Assert.True(input.AcceptsReturn);
                var markdown = root.GetVisualDescendants().OfType<CuiMarkdownView>().ToArray();
                Assert.Equal(state.Conversation!.Messages.Count, markdown.Length);
                Assert.Contains(markdown, view => view.Text.Contains("**formatted**", StringComparison.Ordinal));
                var scrolls = root.GetVisualDescendants().OfType<ScrollViewer>().ToArray();
                Assert.True(scrolls.Any(scroll => scroll.Extent.Height > scroll.Viewport.Height && scroll.Viewport.Height > 0),
                    string.Join(" | ", scrolls.Select(scroll => $"extent={scroll.Extent} viewport={scroll.Viewport} bounds={scroll.Bounds}")));

                var models = root.GetVisualDescendants().OfType<ComboBox>().Single(box => AutomationProperties.GetAutomationId(box) == "model-choice");
                Assert.Equal(2, models.ItemCount);
            }
            finally { window.Close(); loader.Dispose(); await loader.WhenActionsIdleAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void Selectors_resolve_published_stable_identities_and_refuse_stale_collection_writes()
    {
        var state = State();
        var commands = new List<(string Command, object? Value)>();
        var bindings = new ChatSpaceCuiBindings(() => state, (command, value, _) => { commands.Add((command, value)); return ValueTask.CompletedTask; });
        Assert.True(bindings.TrySetValue("Composer.SelectedModelIndex", 1));
        Assert.Equal(("chat.select-model", "model-b"), commands.Single());
        commands.Clear();
        Assert.True(bindings.TrySetValue("SelectedBranchIndex", 1));
        Assert.Equal(("chat.switch-branch", state.Conversation!.Branches[1].Id), commands.Single());
        state = state with { Models = state.Models.Reverse().ToArray() };
        Assert.False(bindings.TrySetValue("Composer.SelectedModelIndex", 1));
        Assert.False(bindings.IsActionAvailable("chat.send"));
        Assert.True(bindings.TryGetValue("Conversation.Messages", out var hidden));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<ChatSpaceMessage>>(hidden));
    }

    [Fact]
    public void Search_filters_recent_chats_and_reasoning_edit_fields_bind()
    {
        var state = State();
        var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
        Assert.True(bindings.TrySetValue("RecentSearchQuery", "SECOND"));
        Assert.True(bindings.TryGetValue("RecentChats", out var recent));
        Assert.Equal("Second chat", Assert.Single(Assert.IsAssignableFrom<IEnumerable<ChatSpaceRecentChat>>(recent)).Title);
        var message = state.Conversation!.Messages[0];
        Assert.True(bindings.TryGetItemValue(message, "HasThinking", out var thinking)); Assert.Equal(true, thinking);
        Assert.True(bindings.TryGetItemValue(message, "CanEdit", out var edit)); Assert.Equal(true, edit);
        state = state with { IsSafetyLocked = true }; bindings.Refresh();
        Assert.False(bindings.IsActionAvailable("chat.send")); Assert.False(bindings.IsActionAvailable("chat.attach"));
        Assert.True(bindings.IsActionAvailable("chat.copy")); Assert.True(bindings.IsActionAvailable("chat.delete"));
    }

    private static ChatSpaceViewState State()
    {
        var id = Guid.NewGuid(); var branch = Guid.NewGuid();
        return ChatSpaceViewState.Empty with
        {
            RecentChats = [new(id, "First chat", "now", DateTimeOffset.UtcNow, true), new(Guid.NewGuid(), "Second chat", "now", DateTimeOffset.UtcNow, false)],
            Conversation = new(id, "Conversation", Enumerable.Range(0, 24).Select(index => new ChatSpaceMessage(Guid.NewGuid(), MessageRole.Assistant,
                $"Message {index}: **formatted** text.\n\nA paragraph which must wrap within this conversation viewport.", "Haven", "now", "model-a", [], [], [], true,
                Thinking: "Reasoning is present", CanEdit: true)).ToArray(),
                [new(branch, "Main", ConversationBranchReason.Root, true), new(Guid.NewGuid(), "Another branch", ConversationBranchReason.Manual, false)], branch),
            Models = [new("model-a", "Model A", "", new HashSet<ToolCapability>()), new("model-b", "Model B", "", new HashSet<ToolCapability>())],
            Composer = new("ready draft", [], "model-a", false, 0),
            SelectedBranchIndex = 0
        };
    }
    private static string Source()
    {
        using var stream = typeof(ChatSpaceCuiBindings).Assembly.GetManifestResourceStream(ChatSpaceCuiDocument.ResourceName)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
