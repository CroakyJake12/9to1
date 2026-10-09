using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsMemoryCuiBindingsTests
{
    [Fact]
    public async Task Missing_owner_refuses_memory_effects_and_revocation_hides_private_fields()
    {
        var calls = 0;
        var bindings = new AssistantsMemoryCuiBindings(false, "Import the actual memory store through Home.",
            (_, _, _) => { calls++; return ValueTask.CompletedTask; }, body => body(), () => true);
        Assert.True(bindings.TryGetValue("MemoryStatus", out var status)); Assert.Contains("Home", Assert.IsType<string>(status));
        foreach (var action in new[] { "review", "confirm", "refresh", "revise", "status" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.memory." + action, null).AsTask());
        Assert.False(bindings.TrySetValue("MemoryDraftSummary", "Unpermitted draft")); Assert.Equal(0, calls);
        bindings.Revoke(); Assert.True(bindings.TryGetValue("MemoryStatus", out status)); Assert.Null(status);
        Assert.True(bindings.TryGetValue("CanMemoryConfirm", out var canConfirm)); Assert.Equal(false, canConfirm);
    }

    [Fact]
    public void Pending_and_unknown_write_keep_same_preview_operation_and_local_draft()
    {
        var bindings = Available(); var preview = Review(bindings);
        bindings.BeginCommit(); bindings.SetBusy(true);
        bindings.Observe(new(preview.OperationId, AssistantMemoryWriteState.HomeReview, "actual-home-review", "Home review pending"));
        Assert.Same(preview, bindings.OriginalPreview); Assert.Equal(preview.OperationId, bindings.OriginalOperationId);
        Assert.False(bindings.TrySetValue("MemoryDraftSummary", "Different effect"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.discard")); Assert.False(bindings.IsActionAvailable("assistants.memory.revise"));
        Assert.True(bindings.IsActionAvailable("assistants.memory.status")); Assert.True(bindings.HasUnconfirmedChanges);
        bindings.MarkUnconfirmed("Original source failed after submission"); bindings.SetBusy(false);
        Assert.Equal("Use concrete examples", bindings.OriginalSummary);
        Assert.False(bindings.IsActionAvailable("assistants.memory.confirm")); Assert.False(bindings.IsActionAvailable("assistants.memory.discard"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.back")); Assert.Same(preview, bindings.OriginalPreview);
    }

    [Fact]
    public void Exact_decline_preserves_draft_until_explicit_revision_creates_new_operation()
    {
        var bindings = Available(); var preview = Review(bindings); bindings.BeginCommit();
        var foreign = new AssistantMemoryWritePreview(new object(), new DisplayIntent(preview.Candidate, preview.OperationId));
        Assert.Throws<InvalidOperationException>(() => bindings.Acknowledge(foreign, new(false, null, "foreign")));
        bindings.Acknowledge(preview, new(false, null, "Home declined before persistence"));
        Assert.Equal("Use concrete examples", bindings.OriginalSummary); Assert.Equal(preview.OperationId, bindings.OriginalOperationId);
        Assert.True(bindings.HasUnconfirmedChanges); Assert.False(bindings.IsActionAvailable("assistants.memory.confirm"));
        bindings.ReviseDraft(); Assert.NotEqual(preview.OperationId, bindings.OriginalOperationId);
        Assert.Equal("Use concrete examples", bindings.OriginalSummary); Assert.True(bindings.TrySetValue("MemoryDraftTitle", "Revised preference"));
        bindings.DiscardDraft(); Assert.False(bindings.HasUnconfirmedChanges); Assert.Null(bindings.OriginalPreview);
    }

    [Fact]
    public void Authored_memory_scene_parses_with_its_real_scene_registration() => Assert.NotNull(AssistantsCuiScenes.ReadDocument(AssistantsCuiScene.Memory));

    private static AssistantsMemoryCuiBindings Available()
    {
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fictional helper" }, []);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Memory draft fixture", null, null, false, false, now, now);
        var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1, conversation);
        var bindings = new AssistantsMemoryCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        // Detached UI fixtures only. These issuers/markers are never presented as
        // actual source authority; the real Home/Den/SQLite controls own that proof.
        bindings.SetView(new(new object(), binding, new DisplayInput(), [], "Selected permitted fixture records")); return bindings;
    }
    private static AssistantMemoryWritePreview Review(AssistantsMemoryCuiBindings bindings)
    {
        Assert.True(bindings.TrySetValue("MemoryDraftTitle", "Examples")); Assert.True(bindings.TrySetValue("MemoryDraftSummary", "Use concrete examples"));
        var now = DateTimeOffset.UtcNow;
        var record = new KnowledgeRecord(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Preference", "Examples", "Use concrete examples",
            KnowledgePrivacyClass.Private, 1, false, now, now, null, "Display fixture", []);
        var preview = new AssistantMemoryWritePreview(new object(), new DisplayIntent(record, bindings.OriginalOperationId));
        bindings.SetPreview(preview); return preview;
    }
    private sealed class DisplayInput : IChatOriginalPersistentMemoryInput { }
    private sealed record DisplayIntent(KnowledgeRecord Candidate, Guid OperationId) : ICanonicalAssistantMemoryWriteIntent
    {
        public AuthenticatedResourceActor Actor => throw new NotSupportedException();
        public ResourceStoreIdentity OriginalStoreIdentity => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalDenOwnership => throw new NotSupportedException();
        public string DenId => throw new NotSupportedException();
        public string NamespaceId => throw new NotSupportedException();
        public string DefinitionId => throw new NotSupportedException();
        public long DefinitionRevision => throw new NotSupportedException();
        public string OriginalStorageScope => throw new NotSupportedException();
        public IChatOriginalPersistentMemoryInput OriginalProductInput => throw new NotSupportedException();
        public IChatOriginalPersistentMemorySource OriginalProductSource => throw new NotSupportedException();
        public Conversation OriginalConversation => throw new NotSupportedException();
        public ProviderExecutionContext? OriginalCanonicalContext => throw new NotSupportedException();
    }
}
