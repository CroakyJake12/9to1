using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Web.Write;

// Run seed and verify in separate processes with the same explicit isolated HAVEN_DATA_DIR.
// Production repository, diagnostics, validator, codec and editor are linked unchanged.
if (args.Length != 1 || args[0] is not ("seed" or "verify" or "subscriber" or "shell") ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed or verify and set an isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
var diagnostics = new ProductionDiagnostics(paths);
var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
var packages = new WriteNativeDocumentPackageStore();
var statePath = Path.Combine(paths.DataDirectory, "browser-write-test-state.json");
var packagePath = Path.Combine(paths.DataDirectory, "browser-write.9to1w");
var mediaStatePath = Path.Combine(paths.DataDirectory, "browser-write-media-state.json");
var mediaPackagePath = Path.Combine(paths.DataDirectory, "browser-write-media.9to1w");
var assertions = 0;
void Check(bool condition, string assertion)
{
    if (!condition) throw new InvalidOperationException(assertion);
    ++assertions;
    Console.WriteLine("ASSERT " + assertion);
}
NotesBlock First(NotesDocument document) => document.Sections.SelectMany(section => section.Pages)
    .SelectMany(page => page.Blocks).First();
string Text(NotesDocument document) => string.Concat(First(document).Runs.Select(run => run.Text));
void AssertFormatting(NotesDocument document)
{
    var runs = First(document).Runs;
    Check(Text(document) == "Alpha beta gamma", "exact document text");
    Check(runs.Count == 3 && runs[0].Text == "Alpha " && runs[1].Text == "beta" && runs[2].Text == " gamma",
        "range formatting preserves three text runs");
    Check(!runs[0].Bold && runs[1].Bold && !runs[2].Bold,
        "only beta is bold; adjacent text remains unchanged");
}

if (args[0] == "shell")
{
    var session = new WriteBrowserSession(repository, packages);
    Check((await session.CreateAsync("Contextual editor journey")).Succeeded, "create actual contextual editor document");
    var id = session.DocumentId!.Value;
    var block = First(session.GetDocumentSnapshot()!).Id;
    await session.InsertTextAsync("One two One");
    Check(session.TrySetValue("FindText", "One"), "find input accepts actual query");
    await session.DispatchAsync("FindNext", null);
    Check(session.Editor!.SelectionStart == 0 && session.Editor.SelectionEnd == 3, "first match selects actual document range");
    await session.DispatchAsync("FindNext", null);
    Check(session.Editor.SelectionStart == 8 && session.Editor.SelectionEnd == 11, "next match selects later actual document range");
    session.TrySetValue("ReplacementText", "Three");
    await session.DispatchAsync("ReplaceAll", null);
    Check(Text(session.GetDocumentSnapshot()!) == "Three two Three", "replace acts on canonical document text");
    await session.DispatchAsync("StyleHeading1", null);
    Check(First(session.GetDocumentSnapshot()!).StyleId == "heading-1", "style command applies persisted named style");
    await session.DispatchAsync("InsertTable", null);
    Check(session.Editor.SelectedBlock?.Kind == NotesBlockKind.Table && session.IsActionAvailable("BoldSelection") == false,
        "table selection excludes irrelevant text formatting");
    await session.DispatchAsync("AddTableRow", null);
    Check(session.Editor.SelectedBlock!.Table!.Rows.Count == 4, "contextual table action calls owning structured editor");
    var tableBlock = session.Editor.SelectedBlock;
    var firstCell = tableBlock.Table!.Rows[0].Cells[0];
    session.Editor.SelectTableCell(tableBlock.Id, firstCell.Id);
    Check(session.IsActionAvailable("MergeCell") == true && session.IsActionAvailable("SplitCell") == false,
        "table capabilities reflect whether selected cell can merge or split");
    await session.DispatchAsync("MergeCell", null);
    Check(firstCell.ColumnSpan == 2 && session.IsActionAvailable("SplitCell") == true, "merge persists actual span and enables split");
    await session.DispatchAsync("SplitCell", null);
    Check(firstCell.ColumnSpan == 1 && session.IsActionAvailable("SplitCell") == false, "split preserves table structure and disables inapplicable repeat");
    Check(session.TrySetValue("DocumentTitle", "Updated title while table selected"), "title remains editable outside text selection");
    await session.SelectAsync(block, 0, 0);
    Check(session.IsActionAvailable("AddTableRow") == false && session.IsActionAvailable("BoldSelection") == true,
        "text selection removes irrelevant table controls");
    await session.SaveAsync();
    var before = JsonSerializer.Serialize(session.GetDocumentSnapshot());
    session.SetViewportWidth(390);
    session.TryGetValue("ShowSidebar", out var sidebar);
    session.TryGetValue("WorkspaceColumns", out var columns);
    Check(sidebar is false && Equals(columns, "0 1fr") && !session.IsDirty, "narrow viewport collapses sidebar without changing content");
    await session.DispatchAsync("ToggleSidebar", null);
    session.TryGetValue("SidebarColumnSpan", out var span);
    Check(Equals(span, 2) && before == JsonSerializer.Serialize(session.GetDocumentSnapshot()), "mobile drawer overlays without document mutation");
    await session.CloseAsync();
    await session.OpenAsync(id);
    Check(before == JsonSerializer.Serialize(session.GetDocumentSnapshot()), "contextual edits reopen with exact canonical state");
    await session.CloseAsync();
    await session.OpenAsync(id, readOnly: true);
    session.TrySetValue("FindText", "Three");
    Check(session.IsActionAvailable("FindNext") == true && session.IsActionAvailable("ReplaceAll") == false,
        "read-only document permits search but rejects replacement");
    await session.CloseAsync(); await session.DisposeAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "actual owner editor and filesystem repository; no donor/browser acceptance" }));
    return;
}

if (args[0] == "subscriber")
{
    var session = new WriteBrowserSession(repository, packages);
    var sentinel = new ApplicationException("Intentional presentation subscriber failure");
    System.ComponentModel.PropertyChangedEventHandler listener = (_, _) =>
    {
        if (!session.IsBusy) throw sentinel;
    };
    session.PropertyChanged += listener;
    var observed = false;
    try { await session.CreateAsync("Subscriber failure fixture"); }
    catch (ApplicationException failure) when (ReferenceEquals(failure, sentinel)) { observed = true; }
    finally { session.PropertyChanged -= listener; }
    Check(observed && session.DocumentId is not null && !session.IsDirty,
        "presentation listener error is visible after real durable create");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    Check((await session.CloseAsync(timeout.Token)).Succeeded,
        "subscriber exception cannot strand command gate; subsequent operation completes");
    await session.DisposeAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "actual local durable owner/session" }));
    return;
}

if (args[0] == "seed")
{
    Check(!File.Exists(statePath), "fixture is fresh; never overwrite another test run");
    var session = new WriteBrowserSession(repository, packages);
    var created = await session.CreateAsync("Browser durable journey");
    Check(created.Succeeded && created.Value != Guid.Empty && session.DurableRevision > 0, "owner creates durable opaque document ID");
    var id = session.DocumentId!.Value;
    var blockId = First(session.GetDocumentSnapshot()!).Id;
    Check((await session.SelectAsync(blockId, 0, 0)).Succeeded, "owner selection addresses actual object ID");
    Check((await session.InsertTextAsync("Alpha beta gamma")).Succeeded, "owner structured editor inserts text");
    Check((await session.SelectAsync(blockId, 6, 10)).Succeeded, "select actual beta range");
    await session.DispatchAsync("BoldSelection", null);
    Check(session.ErrorCode is null && session.IsDirty, "same CUI action changes actual editor and marks dirty");
    AssertFormatting(session.GetDocumentSnapshot()!);
    var unsavedRevision = session.DurableRevision;
    Check((await session.UndoAsync()).Succeeded && Text(session.GetDocumentSnapshot()!) == "Alpha beta gamma" &&
        First(session.GetDocumentSnapshot()!).Runs.All(run => !run.Bold), "owner undo removes only range formatting");
    Check(session.DurableRevision == unsavedRevision, "undo does not alter durable base revision");
    Check((await session.RedoAsync()).Succeeded, "owner redo restores formatting");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check((await session.SaveAsync()).Succeeded && !session.IsDirty, "canonical save acknowledges clean state");
    var revision = session.DurableRevision;
    Check((await session.ExportNativeAsync(packagePath)).Succeeded, "actual native codec exports saved document");
    var exported = await packages.OpenAsync(packagePath);
    Check(exported.IsSuccess && exported.Value!.Id == id && exported.Value.Version == revision, "native package retains canonical identity and revision");
    AssertFormatting(exported.Value!);
    Check((await repository.GetVersionsAsync(id, default)).Count >= 2, "real repository publishes create and edit history");
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(id, blockId, revision,
        JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections))));
    Check((await session.CloseAsync()).Succeeded && session.DocumentId is null, "close releases saved editor");
    await session.DisposeAsync();

    // Genuine image journey against the canonical attachment owner. The fixture is
    // a real PNG file; no document content is fabricated to satisfy an assertion.
    Check(!File.Exists(mediaStatePath), "media fixture is fresh; never overwrite another test run");
    var attachments = new NotesAttachmentStore(paths, new ProductionDiagnostics(paths));
    var fixtureImage = Path.Combine(paths.DataDirectory, "media-fixture.png");
    File.WriteAllBytes(fixtureImage, PngFixture.Build(2, 2));
    var fixtureHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixtureImage))).ToLowerInvariant();
    var mediaSession = new WriteBrowserSession(repository, packages, null, null, new FixtureImageSource(fixtureImage), attachments);
    Check((await mediaSession.CreateAsync("Media journey")).Succeeded, "media session creates its own durable document");
    var mediaId = mediaSession.DocumentId!.Value;
    Check(mediaSession.IsActionAvailable("InsertImage") == true, "image action is available with a real picker and the canonical attachment store");
    Check((await mediaSession.SelectAsync(First(mediaSession.GetDocumentSnapshot()!).Id, 0, 0)).Succeeded, "select the block the image is inserted after");
    Check((await mediaSession.InsertImageAsync()).Succeeded, "picked image is imported through the canonical attachment store");
    var mediaBlock = mediaSession.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages)
        .SelectMany(page => page.Blocks).First(block => block.Kind == NotesBlockKind.Image);
    Check(mediaBlock.Media!.AttachmentId != Guid.Empty && !string.IsNullOrWhiteSpace(mediaBlock.Media.StoredPath),
        "the attachment owner assigned the identity and stored path, not the browser");
    Check(mediaBlock.Media.Sha256 == fixtureHash && mediaBlock.Media.SizeBytes == new FileInfo(fixtureImage).Length,
        "the stored attachment carries the exact picked bytes");
    Check(mediaBlock.Media.AltText == Path.GetFileNameWithoutExtension(fixtureImage),
        "the attachment owner derives alt text from the staged file name");
    Check((await mediaSession.UndoAsync()).Succeeded, "undo removes the media insert as the most recent admitted edit");
    Check(mediaSession.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages)
        .SelectMany(page => page.Blocks).All(block => block.Kind != NotesBlockKind.Image),
        "undo of the insert leaves no media block in the draft");
    Check((await mediaSession.RedoAsync()).Succeeded, "redo restores the media insert");
    var replayed = mediaSession.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages)
        .SelectMany(page => page.Blocks).First(block => block.Kind == NotesBlockKind.Image);
    Check(replayed.Id == mediaBlock.Id && replayed.Media!.AttachmentId == mediaBlock.Media.AttachmentId,
        "redo restores the same media block and the same stored attachment identity");
    Check((await mediaSession.SelectAsync(replayed.Id, 0, 0)).Succeeded, "select the restored image block before editing its properties");
    Check(mediaSession.TrySetValue("MediaAlt", "A test fixture image") &&
        mediaSession.TrySetValue("MediaCaption", "Fixture caption") && mediaSession.TrySetValue("MediaWrapping", "Square"),
        "media text properties write through the owning editor");
    var written = mediaSession.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages)
        .SelectMany(page => page.Blocks).First(block => block.Kind == NotesBlockKind.Image).Media!;
    Check(written.AltText == "A test fixture image" && written.Caption == "Fixture caption" && written.Wrapping == "Square",
        "each media property writes its own value without overwriting the others");
    Check(mediaSession.TrySetValue("MediaWidth", 640d) && mediaSession.TrySetValue("MediaHeight", 480d) &&
        mediaSession.TrySetValue("MediaRotation", 90d) && mediaSession.TrySetValue("MediaCropLeft", 0.1d) &&
        mediaSession.TrySetValue("MediaCropTop", 0.2d), "media geometry writes through the owning editor");
    Check(!mediaSession.TrySetValue("MediaCropLeft", 0.96d),
        "an out-of-range crop is rejected for validation instead of silently clamped");
    Check(mediaSession.IsDirty, "media edits are authored changes");
    Check((await mediaSession.UndoAsync()).Succeeded, "undo removes the last accepted media geometry edit");
    var rewound = mediaSession.GetDocumentSnapshot()!.Sections.SelectMany(section => section.Pages)
        .SelectMany(page => page.Blocks).First(block => block.Kind == NotesBlockKind.Image).Media!;
    Check(rewound.CropTop == 0d && rewound.CropLeft == 0.1d,
        "undo reverts only the last accepted crop write and leaves the earlier geometry");
    Check((await mediaSession.RedoAsync()).Succeeded, "redo reapplies the last accepted crop write");
    Check((await mediaSession.SaveAsync()).Succeeded && !mediaSession.IsDirty, "media document saves through the canonical repository");
    await File.WriteAllTextAsync(mediaStatePath, JsonSerializer.Serialize(new MediaState(mediaId, mediaBlock.Id,
        mediaBlock.Media.AttachmentId, fixtureHash)));
    Check((await mediaSession.ExportNativeAsync(mediaPackagePath)).Succeeded, "native codec exports the saved media document");
    Check((await mediaSession.CloseAsync()).Succeeded, "media session closes cleanly");
    await mediaSession.DisposeAsync();
}
else
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    var session = new WriteBrowserSession(repository, packages);
    Check((await session.OpenAsync(state.Id)).Succeeded, "fresh process opens same owner document ID");
    Check(session.DurableRevision == state.Revision && First(session.GetDocumentSnapshot()!).Id == state.BlockId,
        "fresh process retains revision and object ID");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check(JsonSerializer.Serialize(session.GetDocumentSnapshot()!.Sections) == state.StructuredSections,
        "fresh process retains entire section/page/block/run structure, IDs and attributes");

    await using (var readOnly = new WriteBrowserSession(repository, packages))
    {
        Check((await readOnly.OpenAsync(state.Id, readOnly: true)).Succeeded, "open explicit read-only presentation");
        var denied = await readOnly.InsertTextAsync("denied");
        Check(!denied.Succeeded && denied.Code == "ReadOnlyDocument" && !readOnly.IsDirty,
            "read-only session rejects editor mutation without dirtying");
        Check(readOnly.IsActionAvailable("BoldSelection") == false, "read-only formatting action disabled");
        Check((await repository.LoadAsync(state.Id, default))!.Version == state.Revision, "read-only rejection does not change durable revision");
    }

    var stale = new WriteBrowserSession(new NotesRepository(paths, new NotesDocumentValidator(), diagnostics), packages);
    Check((await stale.OpenAsync(state.Id)).Succeeded, "independent repository session opens same base revision");
    await session.SelectAsync(state.BlockId, 16, 16);
    await stale.SelectAsync(state.BlockId, 16, 16);
    await session.InsertTextAsync(" winner");
    await stale.InsertTextAsync(" stale");
    Check((await session.SaveAsync()).Succeeded, "first independent session commits next owner revision");
    var currentPath = Path.Combine(paths.DataDirectory, "Notes", "Documents", state.Id.ToString("D"), "current.haven-notes.json");
    var committedHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    var rejected = await stale.SaveAsync();
    Check(!rejected.Succeeded && rejected.Code == "RevisionConflict" && stale.IsDirty,
        "actual repository rejects stale expected revision and preserves draft");
    var afterRejectionHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    Check(committedHash.SequenceEqual(afterRejectionHash), "stale save leaves committed bytes unchanged");
    Check(Text(stale.GetDocumentSnapshot()!) == "Alpha beta gamma stale", "conflicting session retains its own exact unsaved text");
    var cannotClose = await stale.CloseAsync();
    Check(!cannotClose.Succeeded && stale.DocumentId == state.Id, "failed conflict save prevents close and draft loss");
    // Preserve the real conflict draft by exporting its snapshot with the production codec;
    // disposal remains deliberately blocked until a host supplies a conflict-resolution flow.
    Check((await packages.SaveAsync(stale.GetDocumentSnapshot()!, Path.Combine(paths.DataDirectory, "conflict-draft.9to1w"))).IsSuccess,
        "conflict draft can be preserved by actual native codec");

    Check((await session.UndoAsync()).Succeeded, "undo uses owner editor after save");
    Check((await session.SaveAsync()).Succeeded, "undo content saves against current durable revision");
    AssertFormatting((await repository.LoadAsync(state.Id, default))!);

    var revisionBeforeCopy = session.DurableRevision;
    var copy = await session.ImportNativeCopyAsync(packagePath);
    Check(copy.Succeeded && copy.Value != state.Id && session.DurableRevision == 1, "explicit native import creates independent durable document");
    AssertFormatting(session.GetDocumentSnapshot()!);
    Check((await repository.LoadAsync(state.Id, default))!.Version == revisionBeforeCopy, "independent import leaves original canonical artifact unchanged");

    var tamperedPath = Path.Combine(paths.DataDirectory, "tampered.9to1w");
    File.Copy(packagePath, tamperedPath);
    using (var archive = ZipFile.Open(tamperedPath, ZipArchiveMode.Update))
    {
        archive.GetEntry("document.json")!.Delete();
        var entry = archive.CreateEntry("document.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("{}");
    }
    var beforeImport = session.DocumentId;
    var tampered = await session.ImportNativeCopyAsync(tamperedPath);
    Check(!tampered.Succeeded && tampered.Code == "IntegrityCheckFailed" && session.DocumentId == beforeImport,
        "native integrity failure preserves currently open document");
    Check((await session.CloseAsync()).Succeeded, "independent imported document closes safely");

    // Stale is intentionally left as a recoverable unsaved session; IAsyncDisposable
    // correctly refuses silent data loss. Assert this guard, then avoid implicit disposal.
    var guarded = false;
    try { await stale.DisposeAsync(); } catch (InvalidOperationException) { guarded = true; }
    Check(guarded, "lifetime refuses silent dirty-document disposal");

    var queued = new WriteBrowserSession(repository, packages);
    Check((await queued.CreateAsync("Queued edit fixture")).Succeeded, "create separate durable queue fixture");
    var queuedId = queued.DocumentId!.Value;
    var queuedBlock = First(queued.GetDocumentSnapshot()!).Id;
    await queued.SelectAsync(queuedBlock, 0, 0);
    await queued.InsertTextAsync("before");
    var saving = queued.SaveAsync();
    var editing = queued.InsertTextAsync(" after");
    Check((await saving).Succeeded && (await editing).Succeeded, "queued mutation completes after actual repository save");
    Check(Text((await repository.LoadAsync(queuedId, default))!) == "before", "committed snapshot excludes queued later mutation");
    Check(queued.IsDirty && Text(queued.GetDocumentSnapshot()!) == "before after", "later edit remains visible and unsaved");
    var savedBase = queued.DurableRevision;
    Check((await queued.SaveAsync()).Succeeded && queued.DurableRevision == savedBase + 1, "remaining edit commits exactly next owner revision");

    await queued.InsertTextAsync(" retry");
    // Obstruct versions, keeping actual current present so the owner CAS still
    // validates before failing in real directory creation. Obstructing the entire
    // document directory correctly looks like a missing revision, not an IO gate.
    var directory = Path.Combine(paths.DataDirectory, "Notes", "Documents", queuedId.ToString("D"), "Versions");
    var backup = directory + ".isolated-fault-backup";
    Directory.Move(directory, backup);
    await File.WriteAllTextAsync(directory, "intentional fixture-only filesystem obstruction");
    try
    {
        var failed = await queued.SaveAsync();
        Check(!failed.Succeeded && failed.Code == "ProviderUnavailable" && queued.IsDirty,
            "real filesystem failure preserves unsaved draft");
        Check(queued.DurableRevision == savedBase + 1 && Text(queued.GetDocumentSnapshot()!) == "before after retry",
            "failed write retains durable base and exact draft text");
    }
    finally
    {
        File.Delete(directory);
        Directory.Move(backup, directory);
    }
    Check(Text((await repository.LoadAsync(queuedId, default))!) == "before after", "failed save left previous real durable content recoverable");
    Check((await queued.SaveAsync()).Succeeded && queued.DurableRevision == savedBase + 2, "retry commits once against preserved owner revision");
    Check((await queued.CloseAsync()).Succeeded, "recovered draft safely closes");
    await queued.DisposeAsync();
    await session.DisposeAsync();

    // The image journey is read back in this separate process through the same
    // canonical attachment owner, with nothing carried over from the seed process.
    var mediaState = JsonSerializer.Deserialize<MediaState>(await File.ReadAllTextAsync(mediaStatePath))!;
    var attachments = new NotesAttachmentStore(paths, new ProductionDiagnostics(paths));
    var reopened = (await repository.LoadAsync(mediaState.Id, default))!;
    var reopenedBlock = reopened.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
        .First(block => block.Kind == NotesBlockKind.Image);
    var media = reopenedBlock.Media!;
    Check(reopenedBlock.Id == mediaState.BlockId && media.AttachmentId == mediaState.AttachmentId,
        "fresh process addresses the same media block and canonical attachment");
    Check(media.AltText == "A test fixture image" && media.Caption == "Fixture caption" && media.Wrapping == "Square",
        "media text properties survive save and reopen in a fresh process");
    Check(media.Width == 640 && media.Height == 480 && media.Rotation == 90 && media.CropLeft == 0.1 && media.CropTop == 0.2,
        "media geometry survives save and reopen");
    var resolved = await attachments.ResolvePathAsync(media.AttachmentId, default);
    Check(File.Exists(resolved), "the canonical attachment store still resolves the reopened attachment");
    Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(resolved))).ToLowerInvariant() == mediaState.Sha256,
        "reopened attachment bytes still match the digest recorded on the media");
    Check(Path.GetFileName(resolved) == media.StoredPath, "stored path addresses the same canonical attachment file");
    var mediaPackage = await packages.OpenAsync(mediaPackagePath);
    Check(mediaPackage.IsSuccess && mediaPackage.Value!.Id == mediaState.Id,
        "the exported media package opens through the real codec with its canonical identity");
    Check(mediaPackage.Value!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
        .Any(block => block.Kind == NotesBlockKind.Image && block.Media!.AttachmentId == mediaState.AttachmentId),
        "the exported package carries the media block and its canonical attachment reference");

    await using (var readOnlyMedia = new WriteBrowserSession(repository, packages, null, null,
        new FixtureImageSource(Path.Combine(paths.DataDirectory, "media-fixture.png")), attachments))
    {
        Check((await readOnlyMedia.OpenAsync(mediaState.Id, readOnly: true)).Succeeded, "read-only presentation opens the media document");
        Check(readOnlyMedia.IsActionAvailable("InsertImage") == false, "read-only document reports the image action unavailable");
        var deniedImage = await readOnlyMedia.InsertImageAsync();
        Check(!deniedImage.Succeeded && deniedImage.Code == "ReadOnlyDocument" && !readOnlyMedia.IsDirty,
            "image insertion cannot bypass the read-only gate");
        Check(!readOnlyMedia.TrySetValue("MediaAlt", "denied"), "read-only document rejects a media property write");
        Check((await repository.LoadAsync(mediaState.Id, default))!.Version == reopened.Version,
            "read-only rejection does not change the durable revision or store a new attachment");
    }

    await using (var withoutStore = new WriteBrowserSession(repository, packages, null, null,
        new FixtureImageSource(Path.Combine(paths.DataDirectory, "media-fixture.png")), null))
    {
        Check((await withoutStore.CreateAsync("No attachment store")).Succeeded, "session without an attachment store still owns its document");
        Check(withoutStore.IsActionAvailable("InsertImage") == false,
            "the image action reports itself unavailable without the canonical attachment store");
        var refused = await withoutStore.InsertImageAsync();
        Check(!refused.Succeeded && refused.Code == "AttachmentStoreMissing",
            "the session never accepts an image it cannot store durably");
        Check((await withoutStore.CloseAsync()).Succeeded, "the unadmitted image session closes cleanly");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "local durable owner/session; browser GUI not executed" }));

internal sealed record State(Guid Id, Guid BlockId, long Revision, string StructuredSections);
internal sealed record MediaState(Guid Id, Guid BlockId, Guid AttachmentId, string Sha256);

/// <summary>Test fixture standing in for the platform chooser. It stages a real file and stores nothing itself.</summary>
internal sealed class FixtureImageSource(string sourcePath) : IWriteBrowserImageSource
{
    public Task<WritePickedImage?> PickImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The fixture image does not exist.", sourcePath);
        var name = Path.GetFileName(sourcePath);
        var directory = Path.Combine(Path.GetTempPath(), "nine-to-one-write-image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var stagedPath = Path.Combine(directory, name);
        try { File.Copy(sourcePath, stagedPath); }
        catch { Directory.Delete(directory, recursive: true); throw; }
        return Task.FromResult<WritePickedImage?>(new WritePickedImage(name, "image/png",
            new WriteStagedPackage(stagedPath, name, () => { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); return ValueTask.CompletedTask; })));
    }
}

/// <summary>Builds a structurally valid PNG so the fixture is a real image file rather than arbitrary bytes.</summary>
internal static class PngFixture
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Build(int width, int height)
    {
        var raw = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = y * (1 + width * 4) + 1 + x * 4;
                raw[offset] = 0x20; raw[offset + 1] = 0x80; raw[offset + 2] = 0x40; raw[offset + 3] = 0xFF;
            }

        byte[] compressed;
        using (var stream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(raw, 0, raw.Length);
            compressed = stream.ToArray();
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(output, "IHDR", [.. BigEndian((uint)width), .. BigEndian((uint)height), 8, 6, 0, 0, 0]);
        Chunk(output, "IDAT", compressed);
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(MemoryStream output, string name, byte[] data)
    {
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        output.Write(BigEndian((uint)data.Length));
        output.Write(nameBytes);
        output.Write(data);
        output.Write(BigEndian(Crc32(nameBytes, data)));
    }

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc32(byte[] name, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var byteValue in name.Concat(data)) crc = CrcTable[(crc ^ byteValue) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) == 1 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}