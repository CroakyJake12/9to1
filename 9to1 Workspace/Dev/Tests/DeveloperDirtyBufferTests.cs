using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>ED-BUILD-001: dirty buffers retain encoding, line endings, selection and undo separately from saved disk revisions.
/// Proves a buffer becomes dirty on edit while the saved revision is unchanged, MarkSaved clears dirtiness and advances the saved revision,
/// and encoding/line-endings/selection survive both transitions.</summary>
public sealed class DeveloperDirtyBufferTests
{
    private static readonly Guid Doc = Guid.NewGuid();

    [Fact]
    public void OpenStartsClean()
    {
        var buffer = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8", DeveloperLineEnding.Lf);
        Assert.False(buffer.IsDirty);
        Assert.Equal(5, buffer.SavedDiskRevision);
    }

    [Fact]
    public void EditMakesDirtyWithoutChangingSavedRevision()
    {
        var buffer = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8", DeveloperLineEnding.Lf).Edit("class B");
        Assert.True(buffer.IsDirty);
        // The saved disk revision is untouched by editing.
        Assert.Equal(5, buffer.SavedDiskRevision);
    }

    [Fact]
    public void MarkSavedClearsDirtinessAndAdvancesSavedRevision()
    {
        var dirty = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8", DeveloperLineEnding.Lf).Edit("class B");
        var saved = dirty.MarkSaved(6);

        Assert.False(saved.IsDirty);
        Assert.Equal(6, saved.SavedDiskRevision);
        Assert.Equal("class B", saved.DraftText);
    }

    [Fact]
    public void PresentationAndSelectionSurviveEditAndSave()
    {
        var buffer = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8-bom", DeveloperLineEnding.CrLf)
            .SetPresentation("iso-8859-1", DeveloperLineEnding.Cr)
            .SetSelection(2, 7);

        var edited = buffer.Edit("class AB");
        Assert.Equal("iso-8859-1", edited.EncodingId);
        Assert.Equal(DeveloperLineEnding.Cr, edited.LineEnding);
        Assert.Equal(2, edited.Selection.Anchor);
        Assert.Equal(7, edited.Selection.Caret);

        var saved = edited.MarkSaved(6);
        Assert.Equal("iso-8859-1", saved.EncodingId);
        Assert.Equal(DeveloperLineEnding.Cr, saved.LineEnding);
        Assert.Equal(2, saved.Selection.Anchor);
        Assert.Equal(7, saved.Selection.Caret);
    }

    [Fact]
    public void RevertingToSavedSnapshotClearsDirtiness()
    {
        var buffer = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8", DeveloperLineEnding.Lf)
            .Edit("class B")
            .Edit("class A"); // typed back to the saved content

        Assert.False(buffer.IsDirty);
    }

    [Fact]
    public void SavedRevisionCannotMoveBackwards()
    {
        var buffer = DeveloperDirtyBuffer.Open(Doc, 5, "class A", "utf-8", DeveloperLineEnding.Lf).Edit("class B");
        Assert.Throws<InvalidOperationException>(() => buffer.MarkSaved(4));
    }

    [Fact]
    public void InvalidBuffersAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperDirtyBuffer(Guid.Empty, 1, "a", "a", "utf-8", DeveloperLineEnding.Lf, new DeveloperBufferSelection(0, 0)).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperDirtyBuffer(Doc, 0, "a", "a", "utf-8", DeveloperLineEnding.Lf, new DeveloperBufferSelection(0, 0)).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperDirtyBuffer(Doc, 1, "a", "a", " ", DeveloperLineEnding.Lf, new DeveloperBufferSelection(0, 0)).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperBufferSelection(-1, 0).Capture());
    }
}
