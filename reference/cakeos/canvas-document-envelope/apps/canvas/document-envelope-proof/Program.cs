using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CakeOS.Canvas.Document;

var firstBoardId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var secondBoardId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var document = BuildDocument(firstBoardId, secondBoardId);

using var packageStream = new MemoryStream();
CanvasDocumentPackageCodec.Write(packageStream, document);
var packageBytes = packageStream.ToArray();
if (packageBytes.Length < 500) throw new InvalidOperationException("Canvas document envelope was unexpectedly small.");

packageStream.Position = 0;
var restored = CanvasDocumentPackageCodec.Read(packageStream);
if (restored.Boards.Count != 2 || restored.ActiveBoardId != secondBoardId)
    throw new InvalidOperationException("Canvas multi-board identity did not survive the package round trip.");

var restoredFirst = restored.Boards.Single(board => board.Id == firstBoardId);
var restoredSecond = restored.Boards.Single(board => board.Id == secondBoardId);
var point = restoredFirst.State.InkStrokes.Single().Points[1];
if (Math.Abs(point.Pressure - 0.72) > 0.0001
    || Math.Abs(point.TiltX - 18) > 0.0001
    || Math.Abs(point.TiltY + 11) > 0.0001
    || point.TimestampMilliseconds != 1042)
{
    throw new InvalidOperationException("Cake canonical pressure/tilt/timestamp data did not survive the package round trip.");
}

if (restoredFirst.State.StructuredObjects.Single().GetProperty("kind").GetString() != "connector"
    || restoredFirst.State.GhostLayers.Single().GetProperty("name").GetString() != "Answer"
    || restoredFirst.State.Extensions["cake.generative-ui"].GetProperty("version").GetInt32() != 1)
{
    throw new InvalidOperationException("Cake structured/ghost/generative extension state did not survive the package round trip.");
}

if (restoredFirst.EnginePayload is null
    || restoredSecond.EnginePayload is null
    || !restoredFirst.EnginePayload.SequenceEqual(document.Boards[0].EnginePayload!)
    || !restoredSecond.EnginePayload.SequenceEqual(document.Boards[1].EnginePayload!))
{
    throw new InvalidOperationException("Opaque per-board Rnote engine snapshots did not survive the package round trip.");
}

using (var archiveCheck = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
{
    var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "manifest.json",
        $"boards/{firstBoardId:N}/board.json",
        $"boards/{firstBoardId:N}/engine.rnote",
        $"boards/{secondBoardId:N}/board.json",
        $"boards/{secondBoardId:N}/engine.rnote",
    };
    if (!expectedPaths.SetEquals(archiveCheck.Entries.Select(entry => entry.FullName)))
        throw new InvalidOperationException("Canvas v1 package entries did not match the fixed manifest/board/engine layout.");
}

var tamperRejected = RejectTamperedEngine(packageBytes, firstBoardId);
var duplicateRejected = RejectDuplicateBoardId(document);

if (args.Length > 0)
{
    var outputPath = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllBytes(outputPath, packageBytes);
}

Console.WriteLine(
    $"CANVAS_DOCUMENT_ENVELOPE_READY boards={restored.Boards.Count} active={restored.ActiveBoardId:N} tilt=1 engineDigest={(tamperRejected ? 1 : 0)} duplicateIds={(duplicateRejected ? 1 : 0)} bytes={packageBytes.Length}");

static CanvasDocumentPackage BuildDocument(Guid firstBoardId, Guid secondBoardId)
{
    var created = new DateTimeOffset(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);
    var document = new CanvasDocumentPackage
    {
        DocumentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Title = "Rnote migration envelope proof",
        CreatedAt = created,
        UpdatedAt = created.AddMinutes(1),
        ActiveBoardId = secondBoardId,
    };
    document.Extensions["cake.migration"] = Json("""{"source":"notes-canvas","version":1}""");

    var first = new CanvasBoardPackage
    {
        Id = firstBoardId,
        Title = "Ideas",
        EnginePayload = Encoding.UTF8.GetBytes("fixture-rnote-engine-snapshot-board-one"),
        EngineUpstreamRelease = "v0.14.2",
        State = new CanvasBoardState
        {
            Viewport = new CanvasViewportState { CenterX = 180, CenterY = 140, Zoom = 1.75 },
            Infinite = true,
        },
    };
    first.State.InkStrokes.Add(new CanvasInkStroke
    {
        Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Tool = "pen",
        Colour = "#FF2F80ED",
        BaseWidth = 3,
        Opacity = 1,
        Points =
        [
            new CanvasInkPoint { X = 100, Y = 120, Pressure = 0.31, TiltX = 12, TiltY = -8, TimestampMilliseconds = 1000 },
            new CanvasInkPoint { X = 140, Y = 155, Pressure = 0.72, TiltX = 18, TiltY = -11, TimestampMilliseconds = 1042 },
        ],
    });
    first.State.StructuredObjects.Add(Json("""{"id":"44444444-4444-4444-4444-444444444444","kind":"connector","from":"a","to":"b","label":"flow"}"""));
    first.State.GhostLayers.Add(Json("""{"id":"55555555-5555-5555-5555-555555555555","name":"Answer","revealed":false}"""));
    first.State.Extensions["cake.generative-ui"] = Json("""{"version":1,"prompt":"group these ideas"}""");

    var second = new CanvasBoardPackage
    {
        Id = secondBoardId,
        Title = "Plan",
        EnginePayload = Encoding.UTF8.GetBytes("fixture-rnote-engine-snapshot-board-two"),
        EngineUpstreamRelease = "v0.14.2",
        State = new CanvasBoardState
        {
            Viewport = new CanvasViewportState { CenterX = -40, CenterY = 220, Zoom = 2.25 },
            Infinite = true,
        },
    };

    document.Boards.Add(first);
    document.Boards.Add(second);
    return document;
}

static JsonElement Json(string json)
{
    using var parsed = JsonDocument.Parse(json);
    return parsed.RootElement.Clone();
}

static bool RejectTamperedEngine(byte[] packageBytes, Guid boardId)
{
    using var tampered = new MemoryStream();
    tampered.Write(packageBytes);
    tampered.Position = 0;
    using (var archive = new ZipArchive(tampered, ZipArchiveMode.Update, leaveOpen: true))
    {
        var path = $"boards/{boardId:N}/engine.rnote";
        var original = archive.GetEntry(path) ?? throw new InvalidOperationException("Engine fixture was missing before tamper test.");
        original.Delete();
        var replacement = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var stream = replacement.Open();
        stream.Write(Encoding.UTF8.GetBytes("tampered-rnote-engine-snapshot-board-one"));
    }

    tampered.Position = 0;
    try
    {
        _ = CanvasDocumentPackageCodec.Read(tampered);
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}

static bool RejectDuplicateBoardId(CanvasDocumentPackage source)
{
    var duplicate = BuildDocument(source.Boards[0].Id, source.Boards[0].Id);
    duplicate.ActiveBoardId = source.Boards[0].Id;
    try
    {
        using var stream = new MemoryStream();
        CanvasDocumentPackageCodec.Write(stream, duplicate);
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}
