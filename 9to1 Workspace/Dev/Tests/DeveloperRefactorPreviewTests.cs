using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>ED-BUILD-002: rename/refactor previews every affected file/range and applies against expected versions, including unopened files,
/// as one declared transaction. Proves a clean multi-file apply transforms every file, one changed expected version aborts the whole transaction
/// with no partial write, and unopened files are included in the preview.</summary>
public sealed class DeveloperRefactorPreviewTests
{
    private static readonly Guid Refactor = Guid.NewGuid();
    private static readonly Guid FileA = Guid.NewGuid();
    private static readonly Guid FileB = Guid.NewGuid();

    private static DeveloperRefactorEdit Edit(Guid id, string path, long version, string original, string replacement) =>
        new(id, path, version, original, replacement);

    private static DeveloperRefactorPreview Rename() => DeveloperRefactorPreview.Prepare(Refactor, "Rename Order to PurchaseOrder",
        [Edit(FileA, "src/Order.cs", 3, "class Order", "class PurchaseOrder"),
         Edit(FileB, "src/Service.cs", 7, "new Order()", "new PurchaseOrder()")]);

    private static Dictionary<Guid, DeveloperRefactorBuffer> Buffers(long versionA = 3, long versionB = 7) => new()
    {
        [FileA] = new(FileA, versionA, "class Order"),
        [FileB] = new(FileB, versionB, "new Order()"),
    };

    [Fact]
    public void CleanApplyTransformsEveryAffectedFile()
    {
        var result = DeveloperRefactorApply.Apply(Rename(), Buffers());
        Assert.True(result.Applied);
        Assert.Equal("class PurchaseOrder", result.RevisedTexts![FileA]);
        Assert.Equal("new PurchaseOrder()", result.RevisedTexts[FileB]);
    }

    [Fact]
    public void ChangedExpectedVersionAbortsWholeTransaction()
    {
        // FileB was edited concurrently (version advanced) after the preview.
        var result = DeveloperRefactorApply.Apply(Rename(), Buffers(versionA: 3, versionB: 8));
        Assert.False(result.Applied);
        Assert.Equal(FileB, result.MismatchedDocumentId);
        // No partial write: nothing is produced when any file mismatches.
        Assert.Null(result.RevisedTexts);
    }

    [Fact]
    public void ChangedContentAbortsEvenAtSameVersion()
    {
        var buffers = Buffers();
        buffers[FileA] = new DeveloperRefactorBuffer(FileA, 3, "class Order // edited");
        var result = DeveloperRefactorApply.Apply(Rename(), buffers);
        Assert.False(result.Applied);
        Assert.Equal(FileA, result.MismatchedDocumentId);
    }

    [Fact]
    public void MissingFileAbortsTransaction()
    {
        var buffers = Buffers();
        buffers.Remove(FileB);
        var result = DeveloperRefactorApply.Apply(Rename(), buffers);
        Assert.False(result.Applied);
        Assert.Equal(FileB, result.MismatchedDocumentId);
    }

    [Fact]
    public void PreviewIncludesUnopenedFiles()
    {
        // The preview lists every affected path regardless of open state.
        var paths = Rename().AffectedPaths();
        Assert.Contains("src/Order.cs", paths);
        Assert.Contains("src/Service.cs", paths);
    }

    [Fact]
    public void EmptyAndDuplicatePreviewsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => DeveloperRefactorPreview.Prepare(Refactor, "empty", []));
        Assert.Throws<InvalidOperationException>(() => DeveloperRefactorPreview.Prepare(Refactor, "dupe",
            [Edit(FileA, "a.cs", 1, "x", "y"), Edit(FileA, "a.cs", 2, "p", "q")]));
    }

    [Fact]
    public void InvalidEditIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperRefactorEdit(Guid.Empty, "a.cs", 1, "x", "y").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperRefactorEdit(FileA, " ", 1, "x", "y").Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperRefactorEdit(FileA, "a.cs", 0, "x", "y").Capture());
    }
}
