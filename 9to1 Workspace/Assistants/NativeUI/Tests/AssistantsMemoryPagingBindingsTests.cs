using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsMemoryPagingBindingsTests
{
    [Fact]
    public void Query_edit_invalidates_old_page_and_search_cannot_discard_an_unsaved_memory_draft()
    {
        var bindings = new AssistantsMemoryCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        bindings.SetPagingSupport(true);
        var now = DateTimeOffset.UtcNow;
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fictional helper" }, []);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Display fixture", null, null, false, false, now, now);
        var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1, conversation);
        var continuation = new AssistantMemoryPageContinuation(new object(), new object());
        var view = new AssistantMemoryView(new object(), binding, new DisplayInput(), [], "Display only", "", continuation);
        bindings.SetView(view); Assert.True(bindings.IsActionAvailable("assistants.memory.older"));
        var revision = bindings.SearchRevision;
        Assert.True(bindings.TrySetValue("MemorySearch", "examples")); Assert.True(bindings.SearchRevision > revision);
        Assert.Null(bindings.OriginalView); Assert.False(bindings.IsActionAvailable("assistants.memory.older"));
        Assert.True(bindings.IsActionAvailable("assistants.memory.search"));
        bindings.SetView(new(new object(), binding, new DisplayInput(), [], "Display only", "examples", continuation));
        Assert.True(bindings.TrySetValue("MemoryDraftTitle", "My draft"));
        var operation = bindings.OriginalOperationId;
        Assert.False(bindings.TrySetValue("MemorySearch", "different")); Assert.False(bindings.IsActionAvailable("assistants.memory.older"));
        Assert.Equal(operation, bindings.OriginalOperationId); Assert.Equal("My draft", bindings.OriginalTitle);
        bindings.DiscardDraft(); Assert.True(bindings.IsActionAvailable("assistants.memory.older"));
        bindings.SetBusy(true); Assert.False(bindings.TrySetValue("MemorySearch", "held"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.search"));
    }
    private sealed class DisplayInput : IChatOriginalPersistentMemoryInput { }
}
