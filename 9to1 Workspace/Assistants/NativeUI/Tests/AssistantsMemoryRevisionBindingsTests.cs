using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsMemoryRevisionBindingsTests
{
    [Fact]
    public void Revision_uses_exact_presented_record_and_preserves_correction_after_decline()
    {
        var (bindings, record) = Create();
        Assert.Throws<InvalidOperationException>(() => bindings.StartRevision(new(record with { }), CanonicalAssistantMemoryMutationKind.Correct));
        bindings.StartRevision(CurrentRow(bindings), CanonicalAssistantMemoryMutationKind.Correct);
        Assert.Same(record, bindings.OriginalSelectedRecord);
        Assert.True(bindings.TrySetValue("MemoryDraftSummary", "Corrected preference"));
        var operation = bindings.OriginalOperationId;
        var candidate = record with { Id = Guid.NewGuid(), Summary = "Corrected preference", SupersedesId = record.Id, Status = KnowledgeRecordStatus.Corrected };
        var preview = new AssistantMemoryWritePreview(new object(), new DisplayRevision(candidate, operation, record, CanonicalAssistantMemoryMutationKind.Correct));
        bindings.SetPreview(preview); bindings.BeginCommit();
        Assert.False(bindings.IsActionAvailable("assistants.memory.reject"));
        bindings.Acknowledge(preview, new(false, null, "Actual Home declined before persistence"));
        Assert.Same(preview, bindings.OriginalPreview); Assert.Same(record, bindings.OriginalSelectedRecord);
        Assert.Equal(operation, bindings.OriginalOperationId); Assert.Equal("Corrected preference", bindings.OriginalSummary);
        bindings.ReviseDraft(); Assert.NotEqual(operation, bindings.OriginalOperationId); Assert.Same(record, bindings.OriginalSelectedRecord);
        bindings.DiscardDraft(); Assert.Null(bindings.OriginalSelectedRecord); Assert.Equal(CanonicalAssistantMemoryMutationKind.Create, bindings.OriginalMutationKind);
    }

    [Fact]
    public void Rejection_review_cannot_edit_text_or_accept_a_create_or_foreign_record_preview()
    {
        var (bindings, record) = Create(); bindings.StartRevision(CurrentRow(bindings), CanonicalAssistantMemoryMutationKind.Reject);
        Assert.False(bindings.TrySetValue("MemoryDraftSummary", "replacement"));
        Assert.Equal(record.Summary, bindings.OriginalSummary);
        var foreign = new AssistantMemoryWritePreview(new object(), new DisplayRevision(record, bindings.OriginalOperationId,
            record with { Id = Guid.NewGuid() }, CanonicalAssistantMemoryMutationKind.Reject));
        Assert.Throws<InvalidOperationException>(() => bindings.SetPreview(foreign));
        var preview = new AssistantMemoryWritePreview(new object(), new DisplayRevision(record with { Status = KnowledgeRecordStatus.Rejected },
            bindings.OriginalOperationId, record, CanonicalAssistantMemoryMutationKind.Reject));
        bindings.SetPreview(preview); Assert.Contains("retained", preview.Explanation);
        bindings.BeginCommit(); bindings.MarkUnconfirmed("Actual original did not settle");
        Assert.False(bindings.IsActionAvailable("assistants.memory.discard")); Assert.False(bindings.IsActionAvailable("assistants.memory.confirm"));
        Assert.Same(preview, bindings.OriginalPreview); Assert.Same(record, bindings.OriginalSelectedRecord);
    }

    private static AssistantsMemoryCuiBindings.MemoryRow CurrentRow(AssistantsMemoryCuiBindings bindings)
    {
        Assert.True(bindings.TryGetValue("MemoryRecords", out var rows));
        return Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AssistantsMemoryCuiBindings.MemoryRow>>(rows));
    }

    private static (AssistantsMemoryCuiBindings, KnowledgeRecord) Create()
    {
        var now = DateTimeOffset.UtcNow;
        var record = new KnowledgeRecord(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Preference", "Examples", "Use concrete examples",
            KnowledgePrivacyClass.Private, 1, false, now, now, null, "Display-only fixture", []);
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fictional helper" }, []);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Display fixture", null, null, false, false, now, now);
        var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1, conversation);
        var bindings = new AssistantsMemoryCuiBindings(true, "", (_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        bindings.SetRevisionSupport(true); bindings.SetView(new(new object(), binding, new DisplayInput(), [record], "Display-only record"));
        return (bindings, record);
    }
    private sealed class DisplayInput : IChatOriginalPersistentMemoryInput { }
    // UI fixture implements observation only; never passed to production Memory/Home.
    private sealed record DisplayRevision(KnowledgeRecord Candidate, Guid OperationId, KnowledgeRecord? OriginalRecord,
        CanonicalAssistantMemoryMutationKind MutationKind) : ICanonicalAssistantMemoryRevisionWriteIntent
    {
        public string? ExpectedOriginalRecordSha256 => throw new NotSupportedException();
        public AuthenticatedResourceActor Actor => throw new NotSupportedException();
        public ResourceStoreIdentity OriginalStoreIdentity => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => throw new NotSupportedException();
        public VerifiedResourceStoreOwnership OriginalDenOwnership => throw new NotSupportedException();
        public string DenId => throw new NotSupportedException(); public string NamespaceId => throw new NotSupportedException();
        public string DefinitionId => throw new NotSupportedException(); public long DefinitionRevision => throw new NotSupportedException();
        public string OriginalStorageScope => throw new NotSupportedException();
        public IChatOriginalPersistentMemoryInput OriginalProductInput => throw new NotSupportedException();
        public IChatOriginalPersistentMemorySource OriginalProductSource => throw new NotSupportedException();
        public Conversation OriginalConversation => throw new NotSupportedException();
        public ProviderExecutionContext? OriginalCanonicalContext => throw new NotSupportedException();
    }
}
