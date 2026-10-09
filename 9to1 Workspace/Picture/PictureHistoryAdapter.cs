using System.Globalization;
using Haven.Application;

namespace HavenOS.Images;

/// <summary>Owning Picture validation for the existing shared snapshot envelope.
/// File identities and hashes here describe retained state, never access grants.</summary>
internal static class PictureHistoryAdapter
{
    internal const string OwnerFormat = "9to1.Picture.Document";
    internal static string Revision(long revision) => revision.ToString(CultureInfo.InvariantCulture);

    internal static void Validate(PictureDocument document)
    {
        var history = document.SemanticHistory ?? throw new InvalidDataException("The Picture history is unavailable.");
        history.Validate(OwnerFormat, document.DocumentId, Revision(document.Revision), document.SerializeSnapshot(), bytes =>
        {
            var frame = ReadFrame(bytes, document);
            return (frame.DocumentId, Revision(frame.Revision));
        });
    }

    internal static PictureDocument ReadFrame(ReadOnlyMemory<byte> bytes, PictureDocument current)
    {
        var frame = PictureDocument.DeserializeSnapshot(bytes);
        if (frame.DocumentId != current.DocumentId || frame.FileId != current.FileId || frame.SourcePath != current.SourcePath ||
            frame.SourceRevision != current.SourceRevision || frame.InitialCanvasWidth != current.InitialCanvasWidth ||
            frame.InitialCanvasHeight != current.InitialCanvasHeight || frame.Revision > current.Revision ||
            frame.InitialCanvasWidth is not { } width || frame.InitialCanvasHeight is not { } height)
            throw new InvalidDataException("A retained Picture history frame does not belong to this original document, source and revision scope.");
        try
        {
            // Validate every raster dependency without source IO or a rewritten
            // document. The original frame and canonical graph stay unchanged.
            var replayed = PictureOperationEditor.Replay(frame.WithRevision(0), frame.Operations, width, height);
            if (replayed.CanvasWidth != frame.CanvasWidth || replayed.CanvasHeight != frame.CanvasHeight)
                throw new InvalidDataException("A history frame's raster dependencies disagree with its canvas.");
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { throw new InvalidDataException("A retained Picture raster history frame is invalid.", error); }
        return frame;
    }
}
