using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureOperationEditorTests
{
    [Fact]
    public void RemovingRotationRecalculatesCanvasAndPreservesSourceIdentity()
    {
        var document = PictureDocument.Create(640, 480, "files:picture", "source-revision", "/images/example.png")
            .Crop(20, 10, 300, 200).Rotate().Flip(true);

        var edited = PictureOperationEditor.RemoveAt(document, 1, 640, 480);

        Assert.Equal((300, 200), (edited.CanvasWidth, edited.CanvasHeight));
        Assert.Equal(document.DocumentId, edited.DocumentId);
        Assert.Equal(document.SchemaVersion, edited.SchemaVersion);
        Assert.Equal(document.DisplayName, edited.DisplayName);
        Assert.Equal(document.FileId, edited.FileId);
        Assert.Equal(document.SourcePath, edited.SourcePath);
        Assert.Equal(document.SourceRevision, edited.SourceRevision);
        Assert.Equal(document.InitialCanvasWidth, edited.InitialCanvasWidth);
        Assert.Equal(document.InitialCanvasHeight, edited.InitialCanvasHeight);
        Assert.Equal(document.Revision + 1, edited.Revision);
        Assert.Equal(new PictureOperation[] { document.Operations[0], document.Operations[2] }, edited.Operations);
        Assert.Equal((200, 300), (document.CanvasWidth, document.CanvasHeight));
        Assert.Equal(3, document.Operations.Count);
    }

    [Fact]
    public void MovingResizeBeforeCropReplaysLaterDimensionsInTheNewOrder()
    {
        var document = PictureDocument.Create(100, 80).Crop(10, 5, 80, 60).Resize(400, 300).Rotate();

        var edited = PictureOperationEditor.Move(document, 1, 0, 100, 80);

        Assert.Equal((60, 80), (edited.CanvasWidth, edited.CanvasHeight));
        Assert.Equal(new PictureOperation[] { document.Operations[1], document.Operations[0], document.Operations[2] }, edited.Operations);
        Assert.Equal(document.Revision + 1, edited.Revision);
        var movedBack = PictureOperationEditor.Move(edited, 0, 1, 100, 80);
        Assert.Equal(document.Operations, movedBack.Operations);
        Assert.Equal((document.CanvasWidth, document.CanvasHeight), (movedBack.CanvasWidth, movedBack.CanvasHeight));
    }

    [Fact]
    public void InvalidReorderDoesNotChangeThePriorDocument()
    {
        var document = PictureDocument.Create(100, 80).Resize(400, 300).Crop(300, 200, 80, 70);
        var before = document.Serialize();

        Assert.Throws<ArgumentOutOfRangeException>(() => PictureOperationEditor.Move(document, 1, 0, 100, 80));

        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void ReplacingAnEarlierResizeValidatesDependentCropBounds()
    {
        var document = PictureDocument.Create(100, 80).Resize(400, 300).Crop(300, 200, 80, 70);
        var before = document.Serialize();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PictureOperationEditor.ReplaceAt(document, 0, new ResizeOperation(200, 100), 100, 80));

        Assert.Equal(before, document.Serialize());
        var edited = PictureOperationEditor.ReplaceAt(document, 1, new CropOperation(10, 20, 150, 100), 100, 80);
        Assert.Equal((150, 100), (edited.CanvasWidth, edited.CanvasHeight));
        Assert.Equal(document.Revision + 1, edited.Revision);
    }

    [Fact]
    public void ReplayKeepsAnExplicitResizeWhenItBecomesANoOpAndSnapshotsTheInput()
    {
        var document = PictureDocument.Create(100, 80).Resize(200, 160).Resize(100, 80);
        var operations = new List<PictureOperation> { document.Operations[1] };

        var edited = PictureOperationEditor.Replay(document, operations, 100, 80);
        operations.Clear();

        Assert.Equal(new ResizeOperation(100, 80), Assert.Single(edited.Operations));
        Assert.Equal((100, 80), (edited.CanvasWidth, edited.CanvasHeight));
        Assert.Equal(document.Revision + 1, edited.Revision);
        Assert.Equal(edited.Operations, PictureDocument.Deserialize(edited.Serialize()).Operations);
    }

    [Fact]
    public void RemovingTheLastOperationRestoresOriginalCanvas()
    {
        var document = PictureDocument.Create(100, 80).Crop(10, 20, 30, 40);

        var edited = PictureOperationEditor.RemoveAt(document, 0, 100, 80);

        Assert.Empty(edited.Operations);
        Assert.Equal((100, 80), (edited.CanvasWidth, edited.CanvasHeight));
        Assert.Equal(document.DocumentId, edited.DocumentId);
        Assert.Equal(document.Revision + 1, edited.Revision);
    }

    [Fact]
    public void ReplayRejectsDimensionsThatDoNotMatchTheSource()
    {
        var document = PictureDocument.Create(100, 80).Rotate();

        Assert.Throws<ArgumentException>(() => PictureOperationEditor.Replay(document, [], 80, 100));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    public void ReplayRejectsNonCanonicalRotations(int turns)
    {
        var document = PictureDocument.Create(100, 80);

        Assert.Throws<InvalidDataException>(() =>
            PictureOperationEditor.Replay(document, [new RotateOperation(turns)], 100, 80));
    }

    [Fact]
    public void ReplayRejectsRevisionOverflow()
    {
        var document = new PictureDocument
        {
            CanvasWidth = 100, CanvasHeight = 80,
            InitialCanvasWidth = 100, InitialCanvasHeight = 80, Revision = long.MaxValue,
        };

        Assert.Throws<OverflowException>(() => PictureOperationEditor.Replay(document, [], 100, 80));
    }
}
