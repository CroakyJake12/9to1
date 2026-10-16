using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using NineToOne.Dulche.Context;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

[Collection("Assistants native UI")]
public sealed class AssistantsUcmlMemoryTests
{
    [Fact]
    public void Full_219_record_review_preserves_metadata_and_body_without_enabling_categories()
    {
        var (bindings, view) = Create();
        var documents = Enumerable.Range(1, 219).Select(index => Document("Fact " + index)).ToArray();
        bindings.SetUcmlReview(view, documents.Select((document, index) => new AssistantUcmlFile(index + ".ucml", UcmlCodec.Serialize(document))).ToArray());
        Assert.Equal(219, bindings.OriginalUcmlRows.Count);
        for (var i = 0; i < documents.Length; i++)
        {
            Assert.Equal(UcmlCodec.Serialize(documents[i]), bindings.OriginalUcmlRows[i].Serialized);
            Assert.Equal(AssistantsMemoryCuiBindings.UcmlImportState.Ready, bindings.OriginalUcmlRows[i].State);
        }
        Assert.Empty(view.Definition.Configuration.Memory.UcmlCategories);
        Assert.True(bindings.IsActionAvailable("assistants.memory.ucml.confirm"));
        Assert.False(bindings.TrySetValue("MemoryDraftTitle", "Cannot replace reviewed import"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.back"));
        bindings.ClearUcmlReview(); Assert.False(bindings.HasUnconfirmedChanges);
    }

    [Fact]
    public async Task Unsupported_consent_and_duplicate_ids_remain_visible_as_individual_denials()
    {
        var calls = 0; var (bindings, view) = Create((_, _, _) => { calls++; return ValueTask.CompletedTask; });
        var document = Document("Supported");
        var unsupported = Document("Needs consent");
        unsupported = unsupported with { Metadata = unsupported.Metadata with { Consent = unsupported.Metadata.Consent with { Status = "review-required" } } };
        bindings.SetUcmlReview(view, [new("valid.ucml", UcmlCodec.Serialize(document)),
            new("duplicate.ucml", UcmlCodec.Serialize(document)), new("review.ucml", UcmlCodec.Serialize(unsupported)), new("broken.ucml", "not UCML")]);
        Assert.Single(bindings.OriginalUcmlRows, row => row.State == AssistantsMemoryCuiBindings.UcmlImportState.Ready);
        Assert.Equal(3, bindings.OriginalUcmlRows.Count(row => row.State == AssistantsMemoryCuiBindings.UcmlImportState.Denied));
        Assert.Contains("Duplicate", bindings.OriginalUcmlRows[1].Result);
        Assert.Contains("explicit", bindings.OriginalUcmlRows[2].Result);
        var foreign = new AssistantsMemoryCuiBindings.UcmlImportRow("foreign", document, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("assistants.memory.ucml.inspect", foreign).AsTask());
        Assert.Equal(0, calls);
        await bindings.DispatchAsync("assistants.memory.ucml.inspect", bindings.OriginalUcmlRows[2]); Assert.Equal(1, calls);
        bindings.Revoke(); Assert.False(bindings.TryGetItemValue(bindings.OriginalUcmlRows[0], "Title", out _));
    }

    [Fact]
    public void Pending_and_unconfirmed_original_cannot_be_cleared_retried_or_navigated_away()
    {
        var (bindings, view) = Create(); var document = Document("Original");
        bindings.SetUcmlReview(view, [new("original.ucml", UcmlCodec.Serialize(document))]);
        var row = Assert.Single(bindings.OriginalUcmlRows); bindings.BeginUcmlImport(row);
        var preview = Preview(row); bindings.SetUcmlPreview(row, preview);
        bindings.ObserveUcml(row, new(row.OperationId, AssistantMemoryWriteState.HomeReview, "real-request", "Waiting in Home"));
        Assert.True(bindings.TryGetValue("UcmlApprovalRequest", out var request)); Assert.Equal("real-request", request);
        Assert.Throws<InvalidOperationException>(bindings.ClearUcmlReview);
        bindings.MarkUcmlUnconfirmed(row, "Original commit did not settle");
        Assert.Same(preview, row.Preview); Assert.Equal(UcmlCodec.Serialize(document), row.Serialized);
        Assert.False(bindings.IsActionAvailable("assistants.memory.ucml.confirm"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.back")); Assert.True(bindings.HasUnconfirmedChanges);
    }

    [Fact]
    public void Decline_is_not_approval_and_saved_result_requires_the_exact_record()
    {
        var (bindings, view) = Create();
        bindings.SetUcmlReview(view, [new("one.ucml", UcmlCodec.Serialize(Document("First"))), new("two.ucml", UcmlCodec.Serialize(Document("Second")))]);
        var first = bindings.OriginalUcmlRows[0]; bindings.BeginUcmlImport(first); bindings.SetUcmlPreview(first, Preview(first));
        bindings.AcknowledgeUcml(first, new(false, null, "Home declined"));
        Assert.Equal(AssistantsMemoryCuiBindings.UcmlImportState.Declined, first.State);
        Assert.Throws<InvalidOperationException>(() => bindings.BeginUcmlImport(first));
        var second = bindings.OriginalUcmlRows[1]; bindings.BeginUcmlImport(second); var preview = Preview(second); bindings.SetUcmlPreview(second, preview);
        Assert.Throws<InvalidOperationException>(() => bindings.AcknowledgeUcml(second, new(true, preview.Candidate with { Id = Guid.NewGuid() }, "Foreign")));
        bindings.AcknowledgeUcml(second, new(true, preview.Candidate, "Saved by same original"));
        Assert.False(bindings.HasUnconfirmedChanges); Assert.True(bindings.IsActionAvailable("assistants.memory.back"));
        Assert.False(bindings.IsActionAvailable("assistants.memory.ucml.confirm"));
    }

    [Fact]
    public void Categories_and_expiry_are_explicit_validated_draft_changes_with_revocation()
    {
        var draft = new AssistantConfigurationDraft(); Assert.Empty(draft.Configuration.Memory.UcmlCategories);
        Assert.True(draft.TrySetText("DraftUcmlCategories", "communication\nstudy"));
        Assert.Equal(new[] { "communication", "study" }, draft.CaptureSubmission().Configuration.Memory.UcmlCategories);
        Assert.True(draft.TrySetText("DraftUcmlExpiry", "2027-01-01T00:00:00Z"));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), draft.CaptureSubmission().Configuration.Memory.UcmlAccessExpiresAt);
        Assert.True(draft.TrySetText("DraftUcmlCategories", "*")); Assert.NotEmpty(draft.UcmlPreferenceError);
        Assert.Throws<InvalidOperationException>(() => draft.CaptureSubmission());
        Assert.True(draft.TrySetText("DraftUcmlCategories", "")); Assert.Empty(draft.CaptureSubmission().Configuration.Memory.UcmlCategories);
        Assert.True(draft.TrySetText("DraftUcmlExpiry", "2027-01")); Assert.Equal("2027-01", draft.UcmlExpiryText);
        Assert.Throws<InvalidOperationException>(() => draft.CaptureSubmission());
        Assert.True(draft.TrySetText("DraftUcmlExpiry", "")); Assert.Null(draft.CaptureSubmission().Configuration.Memory.UcmlAccessExpiresAt);
        draft.DiscardChanges(); Assert.Empty(draft.UcmlCategoryText); Assert.Empty(draft.UcmlExpiryText);
    }

    [Fact]
    public void Allow_and_ask_are_separate_disjoint_saved_categories_and_neither_means_deny()
    {
        var draft = new AssistantConfigurationDraft();
        Assert.Empty(draft.Configuration.Memory.UcmlCategories); Assert.Empty(draft.Configuration.Memory.UcmlAskCategories);
        Assert.True(draft.TrySetText("DraftUcmlCategories", "communication"));
        Assert.True(draft.TrySetText("DraftUcmlAskCategories", "study"));
        var submitted = draft.CaptureSubmission().Configuration.Memory;
        Assert.Equal(new[] { "communication" }, submitted.UcmlCategories); Assert.Equal(new[] { "study" }, submitted.UcmlAskCategories);
        Assert.True(draft.TrySetText("DraftUcmlAskCategories", "communication"));
        Assert.NotEmpty(draft.UcmlPreferenceError); Assert.Throws<InvalidOperationException>(() => draft.CaptureSubmission());
        Assert.True(draft.TrySetText("DraftUcmlAskCategories", "study,study"));
        Assert.Throws<InvalidOperationException>(() => draft.CaptureSubmission());
        Assert.True(draft.TrySetText("DraftUcmlAskCategories", "")); Assert.True(draft.TrySetText("DraftUcmlCategories", ""));
        Assert.Empty(draft.CaptureSubmission().Configuration.Memory.UcmlCategories);
        Assert.Empty(draft.CaptureSubmission().Configuration.Memory.UcmlAskCategories);
        Assert.Equal(new[] { "study" }, submitted.UcmlAskCategories); // Earlier reviewed submission stays immutable.
    }

    [Fact]
    public async Task Export_replaces_only_selected_local_file_with_private_permissions_and_no_temporary_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ucml-export-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "memory.ucml"); await File.WriteAllTextAsync(path, "previous export");
            var text = UcmlCodec.Serialize(Document("Unicode café"));
            await AssistantUcmlFilePicker.WritePrivateLocalFileAsync(path, text, CancellationToken.None);
            Assert.Equal(text, await File.ReadAllTextAsync(path)); Assert.Single(Directory.GetFiles(directory));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AssistantUcmlFilePicker.WritePrivateLocalFileAsync(path, "replacement", canceled.Token));
            Assert.Equal(text, await File.ReadAllTextAsync(path)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Actual_authored_memory_scene_renders_policy_denials_and_dispatches_the_exact_review_row()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(AssistantsTestApplication));
        Assert.True(await native.Dispatch<bool>(async () =>
        {
            object? selected = null;
            var (bindings, view) = Create((command, row, _) => { Assert.Equal("assistants.memory.ucml.inspect", command); selected = row; return ValueTask.CompletedTask; });
            bindings.SetUcmlReview(view, [new("first.ucml", UcmlCodec.Serialize(Document("Visible UCML"))), new("bad.ucml", null, "Cannot read this selected file")]);
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            var window = new Window { Content = loader.Load(AssistantsCuiScenes.ReadDocument(AssistantsCuiScene.Memory)) };
            try
            {
                window.Show(); window.UpdateLayout();
                var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
                Assert.Contains("Visible UCML", texts); Assert.Contains(texts, text => text?.Contains("local-model") == true);
                Assert.Contains(texts, text => text?.Contains("Cannot read this selected file") == true);
                var buttons = window.GetLogicalDescendants().OfType<Button>().Where(button => button.Content as string == "Inspect full record").ToArray();
                Assert.Equal(2, buttons.Length); buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await loader.WhenActionsIdleAsync();
                Assert.Same(bindings.OriginalUcmlRows[1], selected);
                bindings.BeginUcmlImport(bindings.OriginalUcmlRows[0]); window.UpdateLayout();
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Pending: Preparing this exact file", StringComparison.Ordinal) == true);
                bindings.MarkUcmlUnconfirmed(bindings.OriginalUcmlRows[0], "test status"); window.UpdateLayout();
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Outcome unconfirmed") == true);
            }
            finally { await loader.WhenActionsIdleAsync(); window.Close(); }
            return true;
        }, CancellationToken.None));
    }

    private static (AssistantsMemoryCuiBindings, AssistantMemoryView) Create(Func<string, object?, CancellationToken, ValueTask>? dispatch = null)
    {
        var definition = new AssistantDefinitionSnapshot(new("fixture-den", "personal", "fixture-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Fictional helper" }, []);
        var now = DateTimeOffset.UtcNow;
        var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1,
            new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "UI fixture", null, null, false, false, now, now));
        var view = new AssistantMemoryView(new object(), binding, new DisplayInput(), [], "UI-only fixture");
        var bindings = new AssistantsMemoryCuiBindings(true, "", dispatch ?? ((_, _, _) => ValueTask.CompletedTask), action => action(), () => true);
        bindings.SetView(view); bindings.SetUcmlSupport(true); return (bindings, view);
    }
    private static UcmlDocument Document(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new(new UcmlMetadata { SchemaVersion = 1, Id = Guid.NewGuid(), Revision = 1, Title = title,
            OwnerId = "fixture-owner", Scope = new("personal", "fixture-owner"), Category = "communication", Classification = "private",
            CreatedAt = now, UpdatedAt = now, Retention = "until-revoked", Confidence = .92,
            Consent = new("explicit", ["9to1.assistants"], ["personalization"], ["local-model"]),
            Sources = [new("fixture-source", "Owner statement", "user-message", null, now)] }, "Exact body with Unicode café.\nSource and qualification preserved.");
    }
    private static AssistantMemoryWritePreview Preview(AssistantsMemoryCuiBindings.UcmlImportRow row)
    {
        var m = row.Document!.Metadata;
        var record = new KnowledgeRecord(m.Id, KnowledgeCategory.LearnMe, m.Category, m.Title, row.Document.Markdown,
            KnowledgePrivacyClass.Private, m.Confidence, false, m.CreatedAt, m.UpdatedAt, null, "ucml.v1:" + row.Metadata, []);
        return new(new object(), new DisplayIntent(record, row.OperationId));
    }
    private sealed class DisplayInput : IChatOriginalPersistentMemoryInput { }
    private sealed record DisplayIntent(KnowledgeRecord Candidate, Guid OperationId) : ICanonicalAssistantMemoryWriteIntent
    {
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
