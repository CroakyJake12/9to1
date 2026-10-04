using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

if (args.Length != 1 || args[0] is not ("seed" or "verify") ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed/verify with a fresh explicit isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
var repository = new PresentRepository(paths);
var statePath = Path.Combine(paths.DataDirectory, "present-state.json");
var packagePath = Path.Combine(paths.DataDirectory, "present-native.9to1p");
var assertions = 0;
void Check(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException(name);
    ++assertions;
    Console.WriteLine("ASSERT " + name);
}
if (args[0] == "seed")
{
    Check(!File.Exists(statePath), "fixture is fresh");
    var document = PresentDocument.Create("Durable actual Present");
    var editor = new PresentEditor(document);
    var slide = editor.SelectedSlide;
    var text = editor.AddText(slide.Id, "Alpha beta gamma");
    var shape = editor.AddShape(slide.Id);
    editor.SelectElements([shape.Id]);
    Check(editor.MoveSelection(0.1, 0.2), "actual PresentEditor moves selected shape");
    var duplicate = editor.DuplicateSlide(slide.Id);
    Check(duplicate.Id != slide.Id && duplicate.Elements.Count == slide.Elements.Count &&
        !duplicate.Elements.Select(element => element.Id).Intersect(slide.Elements.Select(element => element.Id)).Any(),
        "actual duplicated slide has independent stable object identities");
    var saved = await repository.SaveAsync(editor.Document, "Actual typed editor fixture", default);
    Check(saved.DocumentId == document.Id && saved.Version == 1, "actual PresentRepository commits initial opaque ID and revision");
    var physical = await repository.LoadAsync(document.Id, default);
    Check(physical is not null && physical.Slides[0].Elements.Single(element => element.Id == text.Id).Text == "Alpha beta gamma",
        "independent real backend readback retains edited element");
    await PresentPackageCodec.ExportAsync(physical!, packagePath, default);
    var imported = await PresentPackageCodec.ImportAsync(packagePath, paths.DataDirectory, default);
    Check(imported.Id != document.Id && imported.Version == 0 && imported.Metadata["importedFromPresentationId"] == document.Id.ToString("D"),
        "actual native package import is an explicit independent copy, not canonical replacement");
    Check(JsonSerializer.Serialize(imported.Slides) == JsonSerializer.Serialize(physical!.Slides),
        "native codec retains complete slides/object IDs/content/styles/geometry");
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(document.Id, saved.Version,
        JsonSerializer.Serialize(physical.Slides))));
}
else
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    var document = await repository.LoadAsync(state.Id, default);
    Check(document is not null && document.Id == state.Id && document.Version == state.Version,
        "fresh process loads actual canonical presentation ID and revision");
    Check(JsonSerializer.Serialize(document!.Slides) == state.Slides,
        "fresh process preserves entire slide/object structure, attributes and identities");
    var stale = await new PresentRepository(paths).LoadAsync(state.Id, default);
    var editor = new PresentEditor(document);
    editor.SetDocumentTitle("Canonical winner");
    var committed = await repository.SaveAsync(editor.Document, "First writer", default);
    var currentHash = SHA256.HashData(await File.ReadAllBytesAsync(committed.CurrentPath));
    stale!.Title = "Stale loser";
    var rejected = false;
    try { await new PresentRepository(paths).SaveAsync(stale, "Stale writer", default); }
    catch (PresentRevisionConflictException conflict)
    { rejected = conflict.DocumentId == state.Id && conflict.ExpectedVersion == state.Version && conflict.ActualVersion == committed.Version; }
    Check(rejected, "actual sequential stale CAS rejects exact expected and actual revisions");
    var afterRejected = SHA256.HashData(await File.ReadAllBytesAsync(committed.CurrentPath));
    Check(currentHash.SequenceEqual(afterRejected), "rejected stale save leaves physical canonical bytes unchanged");
    Check((await repository.LoadAsync(state.Id, default))!.Title == "Canonical winner", "independent readback confirms winner content");
    // Corrupt only this isolated fixture's current. Actual owner recovery must
    // discover the previous valid canonical file and acknowledge its own revision.
    await File.WriteAllTextAsync(committed.CurrentPath, "intentional isolated fixture corruption");
    var recovered = await repository.LoadAsync(state.Id, default);
    Check(recovered is not null && recovered.Recovery.RecoveredFromBackup && recovered.Version == state.Version,
        "actual repository recovers previous validated revision from physical backup");
    Check(JsonSerializer.Serialize(recovered!.Slides) == state.Slides, "actual backup recovery retains exact structured slides");
    var recoverySave = await repository.SaveAsync(recovered, "Acknowledged real backup recovery", default);
    Check(recoverySave.Version == state.Version + 1 && (await repository.LoadAsync(state.Id, default))!.Version == recoverySave.Version,
        "owner recovery saves acknowledged revision instead of silent last-writer overwrite");
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "actual local Present domain/repository/native package; browser/donor acceptance not executed" }));
internal sealed record State(Guid Id, int Version, string Slides);
