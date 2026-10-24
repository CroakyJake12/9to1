using System.Reflection;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Authored NOT RUN. Real source-review/domain owners reuse the existing synthetic tool/
// issuer fixture. No native Home/Files, permission, GUI or installed acceptance is claimed.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Source_editor_refocus_uses_host_filesystem_case_semantics()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var draft = review.UpdateDraft(opened.EditorId, opened.DraftRevision, "unsaved retained draft");
        var caseVariantDocument = f.Document with
        {
            FileId = OperatingSystem.IsWindows() ? f.Document.FileId : Guid.NewGuid(),
            CanonicalResourceId = OperatingSystem.IsWindows() ? f.Document.CanonicalResourceId : "file-case-distinct",
            RelativePath = "SRC/CODE.CS"
        };

        var refocused = (await review.OpenAsync(f.Reference, f.Context(), caseVariantDocument, OriginalTestBodyToken)).Value!;

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(opened.EditorId, refocused.EditorId);
            Assert.Equal(draft.DraftText, refocused.DraftText);
            Assert.Equal(draft.DraftRevision, refocused.DraftRevision);
        }
        else
        {
            Assert.NotEqual(opened.EditorId, refocused.EditorId);
            Assert.Equal(f.Document.RelativePath, opened.Document.RelativePath);
            Assert.Equal(caseVariantDocument.RelativePath, refocused.Document.RelativePath);
        }
        Assert.Equal(2, f.Tools.ReadCalls); // Both focus attempts use a fresh authorised read.
        Assert.Equal(0, f.Tools.WriteCalls);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Explicit_recovery_refreshes_clean_basis_while_normal_open_still_refuses_changed_source()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var original = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        f.Tools.TextValue = "another person's changed saved source";
        var prepared = (await review.PrepareOpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var ordinary = review.AcceptPreparedOpen(prepared);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, ordinary.Error!.Code);
        Assert.Same(original, review.GetSnapshot(original.EditorId));
        var current = review.AcceptPreparedRecoveryOpen(prepared, original).Value!;
        Assert.Equal(original.EditorId, current.EditorId);
        Assert.Equal(f.Tools.TextValue, current.OriginalText); Assert.False(current.IsDirty);
        Assert.Equal(prepared.Snapshot.OriginalObservation, current.OriginalObservation);
        Assert.Null(current.PreparedChangeSetId); Assert.Null(current.LastAppliedChangeSetId);
        var draft = review.UpdateDraft(current.EditorId, current.DraftRevision, "the complete retained protected draft");
        Assert.True(draft.IsDirty); Assert.Equal("the complete retained protected draft", draft.DraftText);
        Assert.Equal("another person's changed saved source", draft.OriginalText);
        Assert.Equal(0, f.Tools.WriteCalls); Assert.Empty(review.GetPasses());
        await f.Dev.CloseAndDrainAsync();
    }
    [Fact]
    public async Task Explicit_recovery_preserves_a_dirty_live_editor_and_its_original_text()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var original = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var dirty = review.UpdateDraft(original.EditorId, original.DraftRevision, "new live typing not in the recovery snapshot");
        f.Tools.TextValue = "external saved source";
        var prepared = (await review.PrepareOpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedRecoveryOpen(prepared, dirty));
        Assert.Same(dirty, review.GetSnapshot(original.EditorId));
        Assert.Equal("new live typing not in the recovery snapshot", dirty.DraftText);
        Assert.Equal(original.OriginalText, dirty.OriginalText); Assert.Equal(0, f.Tools.WriteCalls);
        review.AbandonPreparedOpen(prepared); await f.Dev.CloseAndDrainAsync();
    }
    [Fact]
    public async Task Copied_or_stale_clean_observations_and_hidden_editor_replacement_are_refused()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var original = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        f.Tools.TextValue = "fresh changed source";
        var prepared = (await review.PrepareOpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var copiedPreparation = (DeveloperPreparedSourceOpen)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(prepared, null)!;
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedRecoveryOpen(copiedPreparation, original));
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedRecoveryOpen(prepared, original with { }));
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedRecoveryOpen(prepared, null));
        var dirty = review.UpdateDraft(original.EditorId, original.DraftRevision, "temporarily edited");
        var laterClean = review.UpdateDraft(dirty.EditorId, dirty.DraftRevision, original.OriginalText);
        Assert.False(laterClean.IsDirty);
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedRecoveryOpen(prepared, original));
        Assert.Same(laterClean, review.GetSnapshot(original.EditorId)); Assert.Equal(0, f.Tools.WriteCalls);
        review.AbandonPreparedOpen(prepared); await f.Dev.CloseAndDrainAsync();
    }
}
