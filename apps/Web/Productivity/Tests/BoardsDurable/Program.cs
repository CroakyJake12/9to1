using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

if (args.Length != 1 || args[0] is not ("seed" or "verify") ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed/verify with a fresh explicit isolated HAVEN_DATA_DIR.");
var paths = new AppPaths();
NotesRepository Repository() => new(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
var repository = Repository();
var boards = new BoardsWorkspaceService(repository);
var statePath = Path.Combine(paths.DataDirectory, "boards-state.json");
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
    var notebook = await boards.CreateNotebookAsync("Actual durable Boards", default);
    Check(notebook.Id != Guid.Empty && notebook.Version == 1 && notebook.Metadata[BoardsWorkspaceService.ProductKey] == "boards",
        "actual workspace creates canonical Boards notebook through real repository");
    var section = boards.AddSection(notebook, "Independent section");
    var page = boards.AddPage(notebook, section.Id, "Stable page");
    var block = boards.AddBlock(notebook, page.Id, NotesBlockKind.Paragraph, "Alpha beta gamma");
    Check(block.Id != Guid.Empty && page.Blocks.Contains(block), "actual service adds owned structured block to canonical page");
    var denied = false;
    try { boards.AddCanvasObject(notebook, page.Id, NotesCanvasObjectKind.Text, "Denied locked card", 10, 20); }
    catch (InvalidOperationException) { denied = true; }
    Check(denied && page.CanvasObjects.Count == 0, "actual default locked layout denies object placement without mutation");
    boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Unlocked);
    var card = boards.AddCanvasObject(notebook, page.Id, NotesCanvasObjectKind.Text, "Actual card", 10, 20, 300, 180);
    Check(boards.MoveCanvasObject(notebook, page.Id, card.Id, 140, 240) && card.X == 140 && card.Y == 240,
        "actual unlocked owner operation moves stable canvas object");
    Check(boards.UpdateCanvasObjectText(notebook, page.Id, card.Id, "Retained card content"), "actual owner updates card content");
    var placed = JsonSerializer.Serialize(page.CanvasObjects);
    boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Locked);
    Check(JsonSerializer.Serialize(page.CanvasObjects) == placed, "actual relocking retains exact canvas IDs/content/coordinates/dimensions");
    denied = false;
    try { boards.MoveCanvasObject(notebook, page.Id, card.Id, 999, 999); }
    catch (InvalidOperationException) { denied = true; }
    Check(denied && card.X == 140 && card.Y == 240, "actual locked layout denies repositioning existing card without moving it");
    Check(boards.UpdateCanvasObjectText(notebook, page.Id, card.Id, "Retained card content"),
        "actual locked layout continues to permit content editing in Edit mode");
    boards.SetLayoutMode(notebook, page.Id, BoardsPageLayoutMode.Unlocked);
    boards.SetEditMode(notebook, page.Id, BoardsPageEditMode.View);
    var before = JsonSerializer.Serialize(notebook.Sections);
    denied = false;
    try { boards.UpdateCanvasObjectText(notebook, page.Id, card.Id, "Denied view edit"); }
    catch (InvalidOperationException) { denied = true; }
    Check(denied && JsonSerializer.Serialize(notebook.Sections) == before,
        "actual owner View mode denies content mutation without changing structure; this is not backend ACL");
    boards.SetEditMode(notebook, page.Id, BoardsPageEditMode.Edit);
    await boards.SaveAsync(notebook, "Structured Boards durable fixture", default);
    var saved = (await Repository().LoadAsync(notebook.Id, default))!;
    Check(saved.Version == notebook.Version && JsonSerializer.Serialize(saved.Sections) == JsonSerializer.Serialize(notebook.Sections),
        "independent real repository readback retains all canonical sections/pages/blocks/cards and attributes");
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(notebook.Id, notebook.Version,
        JsonSerializer.Serialize(notebook.Sections), JsonSerializer.Serialize(notebook.Metadata), page.Id, card.Id)));
}
else
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    var notebook = await boards.OpenNotebookAsync(state.Id, default);
    Check(notebook is not null && notebook.Id == state.Id && notebook.Version == state.Version,
        "fresh process opens same canonical notebook ID and owner revision");
    Check(JsonSerializer.Serialize(notebook!.Sections) == state.Sections && JsonSerializer.Serialize(notebook.Metadata) == state.Metadata,
        "fresh process retains complete structure, all object IDs, geometry and mode metadata");
    Check(boards.GetLayoutMode(notebook, state.PageId) == BoardsPageLayoutMode.Unlocked &&
        boards.GetEditMode(notebook, state.PageId) == BoardsPageEditMode.Edit,
        "fresh process reads independently persisted layout and edit modes");
    var stale = (await Repository().LoadAsync(state.Id, default))!;
    Check(boards.MoveCanvasObject(notebook, state.PageId, state.CardId, 260, 360), "actual reopened owner moves same stable card");
    await boards.SaveAsync(notebook, "Canonical Boards writer", default);
    // Inspect the actual owner's physical locator for this backend assertion;
    // browser adapters must use repository APIs, never filesystem paths.
    var currentPath = (string)typeof(NotesRepository).GetMethod("CurrentPath",
        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(repository, [state.Id])!;
    var physicalHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    stale.Title = "Stale Boards writer";
    var conflict = false;
    try { await new BoardsWorkspaceService(Repository()).SaveAsync(stale, "Stale save", default); }
    catch (NotesRevisionConflictException error)
    { conflict = error.ExpectedVersion == state.Version && error.ActualVersion == notebook.Version; }
    Check(conflict, "actual notebook service exposes repository revision conflict instead of overwriting winner");
    var afterHash = SHA256.HashData(await File.ReadAllBytesAsync(currentPath));
    Check(physicalHash.SequenceEqual(afterHash), "actual stale writer rejection leaves canonical physical bytes unchanged");
    Check(await boards.DeleteNotebookAsync(state.Id, default) && await boards.OpenNotebookAsync(state.Id, default) is null,
        "actual owner moves notebook to recoverable trash and excludes normal open");
    Check((await boards.ListDeletedNotebooksAsync(default)).Any(item => item.Id == state.Id), "actual owner lists exact canonical ID in recoverable trash");
    Check(await boards.RestoreNotebookAsync(state.Id, default), "actual owner restores canonical notebook from trash");
    var restored = (await boards.OpenNotebookAsync(state.Id, default))!;
    var restoredCard = restored.Sections.SelectMany(section => section.Pages).Single(page => page.Id == state.PageId)
        .CanvasObjects.Single(card => card.Id == state.CardId);
    Check(restored.Id == state.Id && restored.Version > notebook.Version && restoredCard.X == 260 && restoredCard.Y == 360 &&
        restoredCard.Text == "Retained card content", "actual restore retains winning canonical ID/card content/geometry and advances owner revision");
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions,
    scope = "actual canonical Boards service/local NotesRepository durability; browser UI/donor/ACL not executed" }));
internal sealed record State(Guid Id, long Version, string Sections, string Metadata, Guid PageId, Guid CardId);
