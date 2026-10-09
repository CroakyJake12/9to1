#if !ANDROID
using System.Collections;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Controls;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Actual_branch_find_reads_saved_history_jumps_to_original_rendered_message_and_preserves_independent_drafts_after_reopen() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional history search" });
        var start = DateTimeOffset.UtcNow; var messages = new List<ChatMessage>();
        for (var index = 0; index < 30; index++)
        {
            var text = index == 0 ? "Original branch point [.*] stays saved."
                : index == 10 ? "Compacted KESTREL [.*] note remains part of saved history."
                : index == 29 ? "Distant KESTREL [.*] meeting remains in the complete history."
                : $"Saved history entry {index}. " + new string('x', 240);
            var message = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User, text, null, null, null, start.AddSeconds(index), IsCompacted: index == 10);
            messages.Add(message); var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
        }
        Guid main = Guid.Empty, branch = Guid.Empty;
        const string mainDraft = "Unsent original draft must survive finding messages.", branchDraft = "Independent child draft.";
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            main = Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id;
            Assert.True(surface.Bindings.TrySetValue("Prompt", mainDraft)); Assert.True(surface.HasUnsavedChanges);
            await FindActualSavedBranchText(window, surface, "[.*]", 3); // Literal text, never a regex.
            Assert.Equal(30, controller.Snapshot.Conversation!.Messages.Count);
            Assert.Equal(mainDraft, surface.Bindings.Prompt); Assert.True(surface.HasUnsavedChanges);
            var result = Assert.Single(ConversationSearchRows(surface), value => value.Id == messages[^1].Id);
            var actualMessage = Assert.Single(BranchMessageRows(surface), value => value.Id == result.Id);
            Control? brought = null;
            EventHandler<RequestBringIntoViewEventArgs> observe = (_, request) =>
            {
                if (request.TargetObject is Control target && target.DataContext is ICuiBindingContext context &&
                    context.TryGetValue("message", out var value) && ReferenceEquals(value, actualMessage)) brought = target;
            };
            window.AddHandler(Control.RequestBringIntoViewEvent, observe, RoutingStrategies.Bubble, handledEventsToo: true);
            try
            {
                var copied = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(result, null)!;
                await surface.Bindings.DispatchAsync("assistants.conversation.find.show", copied, Token);
                Assert.Null(brought);
                await ClickCanonicalCaptureControl(window, surface, ConversationSearchButton(window, result), () => brought is not null);
                var target = Assert.IsAssignableFrom<Control>(brought);
                Assert.Contains(target.GetVisualAncestors(), value => ReferenceEquals(value, window));
                var scroller = target.GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.True(scroller.Offset.Y > 0, "The real message scroller must move to the saved message beyond its initial viewport.");
                Assert.Equal(mainDraft, surface.Bindings.Prompt);
            }
            finally { window.RemoveHandler(Control.RequestBringIntoViewEvent, observe); }
            await FindActualSavedBranchText(window, surface, "kestrel", 2);
            Assert.False(surface.Bindings.TryGetItemValue(result, "Snippet", out _));
            var previousBranchResult = Assert.Single(ConversationSearchRows(surface), value => value.Id == messages[10].Id);
            var compacted = Assert.Single(BranchMessageRows(surface), value => value.Id == messages[10].Id);
            Assert.Contains("summarized", compacted.Activity, StringComparison.Ordinal);
            Assert.True(Assert.Single(controller.Snapshot.Conversation!.Messages, value => value.Id == compacted.Id).IsCompacted);
            var jumpedCompacted = false;
            EventHandler<RequestBringIntoViewEventArgs> observeCompacted = (_, request) =>
            {
                if (request.TargetObject is Control target && target.DataContext is ICuiBindingContext context &&
                    context.TryGetValue("message", out var value) && ReferenceEquals(value, compacted)) jumpedCompacted = true;
            };
            window.AddHandler(Control.RequestBringIntoViewEvent, observeCompacted, RoutingStrategies.Bubble, handledEventsToo: true);
            try { await ClickCanonicalCaptureControl(window, surface, ConversationSearchButton(window, previousBranchResult), () => jumpedCompacted); }
            finally { window.RemoveHandler(Control.RequestBringIntoViewEvent, observeCompacted); }
            Assert.True(Assert.Single(controller.Snapshot.Conversation!.Messages, value => value.Id == compacted.Id).IsCompacted);
            var point = Assert.Single(BranchMessageRows(surface), value => value.Id == messages[0].Id);
            await ClickCanonicalCaptureControl(window, surface, BranchActionButton(window, "message", point), () =>
                controller.Snapshot.Conversation?.Branches.Count == 2 && surface.IsOriginalClosePrepared);
            branch = Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id;
            Assert.Empty(ConversationSearchRows(surface)); Assert.False(surface.Bindings.TryGetItemValue(previousBranchResult, "Snippet", out _));
            await surface.Bindings.DispatchAsync("assistants.conversation.find.show", previousBranchResult, Token);
            Assert.Equal(branch, Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id);
            Assert.True(surface.Bindings.TrySetValue("Prompt", branchDraft));
            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => surface.IsOriginalClosePrepared);
            Assert.Null(controller.Snapshot.Work!.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            Assert.Equal(branchDraft, surface.Bindings.Prompt);
            await FindActualSavedBranchText(window, surface, "kestrel", 0);
            Assert.Equal(branchDraft, surface.Bindings.Prompt); Assert.Single(controller.Snapshot.Conversation!.Messages);
            await ClickCanonicalCaptureControl(window, surface, BranchActionButton(window, "branch", Assert.Single(BranchRows(surface), value => value.Id == main)), () =>
                controller.Snapshot.Conversation?.Branches.SingleOrDefault(value => value.IsCurrent)?.Id == main && surface.IsOriginalClosePrepared);
            Assert.Equal(mainDraft, surface.Bindings.Prompt);
            await FindActualSavedBranchText(window, surface, "[.*]", 3);
            Assert.Equal(messages.Select(value => value.Id), controller.Snapshot.Conversation!.Messages.Select(value => value.Id));
            Assert.Equal(original.Definition.Identity, controller.Snapshot.SelectedAssistant!.Identity);
            Assert.Equal(original.Definition.Revision, controller.Snapshot.SelectedAssistant!.Revision);
            Assert.Equal(mainDraft, surface.Bindings.Prompt); Assert.False(surface.HasUnsavedChanges);
        });
    }, importMemory: false);

    [AvaloniaFact]
    public Task Actual_find_refreshes_new_saved_history_and_revokes_previous_results_without_saving_the_draft() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional fresh history" });
        var first = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User, "Searchable current note", null, null, null, DateTimeOffset.UtcNow);
        var write = rig.OriginalConversations.AddMessageAsync(first, Token); rig.Retain(write); await write;
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            await FindActualSavedBranchText(window, surface, "searchable", 1);
            var old = Assert.Single(ConversationSearchRows(surface));
            var newer = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User, "Another searchable saved note", null, null, null, first.CreatedAt.AddSeconds(1));
            var actualWrite = rig.OriginalConversations.AddMessageAsync(newer, Token); rig.Retain(actualWrite); await actualWrite;
            Assert.Single(controller.Snapshot.Conversation!.Messages);
            const string draft = "Do not send or silently save this draft.";
            Assert.True(surface.Bindings.TrySetValue("Prompt", draft));
            await FindActualSavedBranchText(window, surface, "searchable", 2);
            Assert.False(surface.Bindings.TryGetItemValue(old, "Snippet", out _));
            Assert.Equal(2, BranchMessageRows(surface).Length); Assert.Equal(draft, surface.Bindings.Prompt);
            Assert.True(surface.HasUnsavedChanges); Assert.Null(controller.Snapshot.Conversation!.Draft);
            await ClickCanonicalCaptureControl(window, surface, "conversation-find-clear", () =>
                surface.Bindings.TryGetValue("ShowConversationSearch", out var shown) && shown is false);
            Assert.Empty(ConversationSearchRows(surface)); Assert.Equal(draft, surface.Bindings.Prompt);
            await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => surface.IsOriginalClosePrepared);
        });
    }, importMemory: false);

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_compacted_generated_declaration_is_found_as_saved_text_and_jumps_without_activating_the_protected_host() =>
        RunProtectedGeneratedControl(async (rig, graphs) =>
        {
            var binding = await rig.CreateAsync(new() { Name = "Fictional summarized declaration", Memory = new(false) });
            var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User,
                ProtectedGeneratedPayload, null, null, null, DateTimeOffset.UtcNow, IsCompacted: true);
            var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
            var graph = new ProtectedGeneratedGraph(rig); graphs.Add(graph);
            var registrations = 0;
            EventHandler<GenUiDocument> observe = (_, _) => registrations++;
            graph.Instances.DocumentChanged += observe;
            try
            {
                await WithProtectedGeneratedView(rig, graph, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    Assert.Empty(BranchMessageRows(surface));
                    Assert.Empty(surface.OriginalGeneratedUiRenderTasks);
                    await FindActualSavedBranchText(window, surface, "2 + 3", 1);
                    var row = Assert.Single(ConversationSearchRows(surface));
                    var rendered = Assert.Single(BranchMessageRows(surface));
                    Assert.Equal(message.Id, row.Id); Assert.Equal(message.Content, rendered.Content);
                    Assert.Contains("summarized", rendered.Activity, StringComparison.Ordinal);
                    var markdown = Assert.Single(window.GetVisualDescendants().OfType<CuiMarkdownView>(), view =>
                        view.DataContext is ICuiBindingContext context && context.TryGetValue("message", out var actual) && ReferenceEquals(actual, rendered));
                    Assert.Equal(message.Content, markdown.Text);
                    Assert.Contains(markdown.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("2 + 3", StringComparison.Ordinal) == true);
                    var jumped = false;
                    EventHandler<RequestBringIntoViewEventArgs> capture = (_, request) =>
                    {
                        if (request.TargetObject is Control target && target.DataContext is ICuiBindingContext context &&
                            context.TryGetValue("message", out var actual) && ReferenceEquals(actual, rendered) &&
                            target.GetVisualDescendants().Contains(markdown)) jumped = true;
                    };
                    window.AddHandler(Control.RequestBringIntoViewEvent, capture, RoutingStrategies.Bubble, handledEventsToo: true);
                    try { await ClickCanonicalCaptureControl(window, surface, ConversationSearchButton(window, row), () => jumped); }
                    finally { window.RemoveHandler(Control.RequestBringIntoViewEvent, capture); }
                    Assert.Empty(surface.OriginalGeneratedUiRenderTasks);
                    Assert.Empty(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                    Assert.Empty(graph.Instances.GetForThread(binding.Conversation.Id)); Assert.Equal(0, registrations);
                    Assert.Empty(host.OriginalInteractionCommands); Assert.Empty(host.OriginalInteractionObservations);
                    Assert.Equal(message, Assert.Single(controller.Snapshot.Conversation!.Messages));
                    Assert.Null(controller.Snapshot.Work!.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
                    Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                    Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                    Assert.True(surface.IsOriginalClosePrepared);
                });
                var close = graph.CloseAsync(); graph.Retain(close); await close;
                Assert.Same(close, graph.CloseAsync()); Assert.True(close.IsCompletedSuccessfully);
            }
            finally { graph.Instances.DocumentChanged -= observe; }
        });

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_find_replaces_a_previously_mounted_message_with_summarized_text_without_reactivating_its_host() =>
        RunProtectedGeneratedControl(async (rig, graphs) =>
        {
            var binding = await rig.CreateAsync(new() { Name = "Fictional newly summarized declaration", Memory = new(false) });
            var original = await AddProtectedGeneratedMessage(rig, binding);
            var graph = new ProtectedGeneratedGraph(rig); graphs.Add(graph);
            await WithProtectedGeneratedView(rig, graph, async (window, surface, controller, host) =>
            {
                await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                var mounted = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                var document = Assert.IsType<GenUiDocument>(mounted.Document);
                var renders = surface.OriginalGeneratedUiRenderTasks.ToArray(); Assert.NotEmpty(renders);
                var compact = rig.OriginalConversations.MarkMessagesCompactedAsync(binding.Conversation.Id, [original.Id], Token);
                graph.Retain(compact); await compact;
                Assert.False(Assert.Single(controller.Snapshot.Conversation!.Messages).IsCompacted);
                await FindActualSavedBranchText(window, surface, "2 + 3", 1);
                Assert.Empty(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                Assert.Equal(renders, surface.OriginalGeneratedUiRenderTasks);
                var current = Assert.Single(BranchMessageRows(surface));
                Assert.Equal(original.Id, current.Id); Assert.Equal(original.Content, current.Content);
                Assert.Contains("summarized", current.Activity, StringComparison.Ordinal);
                var markdown = Assert.Single(window.GetVisualDescendants().OfType<CuiMarkdownView>(), view =>
                    view.DataContext is ICuiBindingContext context && context.TryGetValue("message", out var actual) && ReferenceEquals(actual, current));
                Assert.Equal(original.Content, markdown.Text);
                Assert.Same(document, graph.Instances.TryGet(document.Origin.InstanceId)); // Retired by the actual existing host close.
                Assert.True(Assert.Single(controller.Snapshot.Conversation!.Messages).IsCompacted);
                Assert.Empty(host.OriginalInteractionCommands); Assert.Empty(host.OriginalInteractionObservations);
                Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                Assert.True(surface.IsOriginalClosePrepared);
            });
            var close = graph.CloseAsync(); graph.Retain(close); await close;
            Assert.Same(close, graph.CloseAsync()); Assert.True(close.IsCompletedSuccessfully);
        });

    private static AssistantsCuiBindings.ConversationSearchRow[] ConversationSearchRows(AssistantsNativeCuiSurface surface)
    {
        Assert.True(surface.Bindings.TryGetValue("ConversationSearchResults", out var rows));
        return Assert.IsAssignableFrom<IEnumerable>(rows).Cast<AssistantsCuiBindings.ConversationSearchRow>().ToArray();
    }
    private static async Task FindActualSavedBranchText(Window window, AssistantsNativeCuiSurface surface, string query, int expected)
    {
        await ClickCanonicalCaptureControl(window, surface, "conversation-find-open", () =>
            surface.Bindings.TryGetValue("ShowConversationSearch", out var shown) && shown is true);
        var input = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), value => value.IsEffectivelyVisible &&
            AutomationProperties.GetName(value) == "Find text in this branch");
        input.Text = query;
        Assert.True(surface.Bindings.TryGetValue("ConversationSearchQuery", out var actual)); Assert.Equal(query, actual);
        await ClickCanonicalCaptureControl(window, surface, "conversation-find", () =>
            surface.Bindings.TryGetValue("CanFindConversation", out var canFind) && canFind is true &&
            ConversationSearchRows(surface).Length == expected &&
            surface.Bindings.TryGetValue("ConversationSearchStatus", out var status) && status is string text &&
            text.Contains(expected == 0 ? "No matching messages" : "matching message", StringComparison.Ordinal));
        await FlushNativeMemoryUi(window);
    }
    private static Button ConversationSearchButton(Window window, AssistantsCuiBindings.ConversationSearchRow actual) => Assert.Single(
        window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
            button.DataContext is ICuiBindingContext binding && binding.TryGetValue("match", out var row) && ReferenceEquals(row, actual));
}
#endif
