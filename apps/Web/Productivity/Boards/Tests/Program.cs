using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using NineToOne.Web.Productivity.Boards;

if (args.Length != 1 || args[0] is not ("seed" or "verify" or "unknown") || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")))
    throw new ArgumentException("Choose seed/verify/unknown in an explicitly isolated actual repository fixture.");
var paths = new AppPaths();
var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
var core = new BoardsBrowserCore(repository);
var statePath = Path.Combine(paths.DataDirectory, "browser-boards-fixture.json");
var assertions = 0;
void Check(bool ok, string name)
{ if (!ok) throw new InvalidOperationException(name); ++assertions; Console.WriteLine("ASSERT " + name); }
string Canonical(NotesDocument document)
{
    var value = JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document))!;
    value.Recovery = new(); // Owner storage/recovery inspection metadata is not authored notebook content.
    return JsonSerializer.Serialize(value);
}
if (args[0] == "seed")
{
    Check((await repository.ListAsync(default)).Count == 0, "actual canonical repository fixture is fresh");
    var created = await core.CreateAsync("Browser Boards actual core");
    var notebook = core.GetDocumentSnapshot()!;
    Check(created.Succeeded && notebook.Id != Guid.Empty && core.DurableRevision == 1 && !core.IsDirty &&
        BoardsWorkspaceService.IsBoardsNotebook(notebook) && core.LastAcknowledgedSave!.DocumentId == notebook.Id,
        "unchanged owner workspace creates actual Boards canonical ID and acknowledged durable receipt");
    var sectionId = (await core.AddSectionAsync("Independent canonical section")).Value;
    var pageId = (await core.AddPageAsync(sectionId, "Independent page")).Value;
    var blockId = (await core.AddParagraphAsync(pageId, "Alpha beta gamma")).Value;
    Check(sectionId != Guid.Empty && pageId != Guid.Empty && blockId != Guid.Empty && core.IsDirty && core.DurableRevision == 1,
        "typed adapter calls original owner section/page/block operations with stable IDs while durable revision stays pinned");
    var before = Canonical(core.GetDocumentSnapshot()!);
    var denied = await core.AddCanvasTextAsync(pageId, "Denied locked placement", 10, 20);
    Check(!denied.Succeeded && denied.Code == "OperationDenied" && Canonical(core.GetDocumentSnapshot()!) == before,
        "actual locked layout denies placement without mutating canonical draft");
    await core.SetLayoutModeAsync(pageId, BoardsPageLayoutMode.Unlocked);
    var cardId = (await core.AddCanvasTextAsync(pageId, "Actual card", 10, 20)).Value;
    Check(cardId != Guid.Empty && (await core.MoveCanvasObjectAsync(pageId, cardId, 140, 240)).Succeeded,
        "actual unlocked owner operation creates and moves same canonical card");
    var placed = JsonSerializer.Serialize(core.GetDocumentSnapshot()!.Sections.SelectMany(s => s.Pages).Single(p => p.Id == pageId).CanvasObjects);
    await core.SetLayoutModeAsync(pageId, BoardsPageLayoutMode.Locked);
    Check(placed == JsonSerializer.Serialize(core.GetDocumentSnapshot()!.Sections.SelectMany(s => s.Pages).Single(p => p.Id == pageId).CanvasObjects),
        "original owner relock preserves exact IDs/content/geometry/dimensions");
    Check((await core.UpdateCanvasTextAsync(pageId, cardId, "Content remains editable while locked")).Succeeded,
        "original locked layout permits content change independently of placement");
    before = Canonical(core.GetDocumentSnapshot()!);
    Check(!(await core.MoveCanvasObjectAsync(pageId, cardId, 999, 999)).Succeeded && Canonical(core.GetDocumentSnapshot()!) == before,
        "actual locked movement denial preserves full draft");
    await core.SetEditModeAsync(pageId, BoardsPageEditMode.View);
    before = Canonical(core.GetDocumentSnapshot()!);
    Check(!(await core.UpdateCanvasTextAsync(pageId, cardId, "Denied view edit")).Succeeded && Canonical(core.GetDocumentSnapshot()!) == before,
        "actual owner View mode denies content mutation with unchanged structure; no backend ACL claim");
    await core.SetEditModeAsync(pageId, BoardsPageEditMode.Edit);
    Check((await core.SaveAsync()).Succeeded && !core.IsDirty && core.DurableRevision == 2,
        "original owner save publishes actual acknowledged revision without a browser CAS counter");
    var saved = (await repository.LoadAsync(notebook.Id, default))!;
    Check(Canonical(saved) == Canonical(core.GetDocumentSnapshot()!), "independent actual repository read confirms whole canonical notebook attributes and IDs");
    await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new State(saved.Id, saved.Version, Canonical(saved), pageId, cardId)));
    await core.CloseAsync(); await core.DisposeAsync();
}
else if (args[0] == "verify")
{
    var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(statePath))!;
    Check((await core.OpenAsync(state.Id)).Succeeded && core.DurableRevision == state.Version && Canonical(core.GetDocumentSnapshot()!) == state.Canonical,
        "fresh process reopens same canonical ID/revision/full notebook content and all object identities");
    var candidate = new BoardsBrowserCore(repository); candidate.PrepareUnadmittedPresentation();
    Check((await candidate.OpenAsync(state.Id)).Succeeded && !candidate.IsPresentationAdmitted && !candidate.IsDirty,
        "real owner snapshot prepares clean without authoring admission");
    var before = Canonical(candidate.GetDocumentSnapshot()!);
    Check(!(await candidate.CreateAsync()).Succeeded && !(await candidate.SaveAsync()).Succeeded &&
        !(await candidate.AddSectionAsync()).Succeeded && !(await candidate.SetLayoutModeAsync(state.PageId, BoardsPageLayoutMode.Unlocked)).Succeeded &&
        Canonical(candidate.GetDocumentSnapshot()!) == before && (await repository.ListAsync(default)).Count == 1,
        "prepared candidate denies direct create/save/structure/layout effects without duplicate document or content changes");
    candidate.AbandonUnadmittedPresentation();
    var readOnly = new BoardsBrowserCore(repository); await readOnly.OpenAsync(state.Id, readOnly: true);
    Check(!(await readOnly.AddSectionAsync()).Succeeded && !(await readOnly.SaveAsync()).Succeeded && !readOnly.IsDirty,
        "presentation read-only denies typed mutation without asserting backend permission authority");
    await readOnly.CloseAsync(); await readOnly.DisposeAsync();
    await core.UpdateCanvasTextAsync(state.PageId, state.CardId, "Preserved conflicting draft");
    var draft = Canonical(core.GetDocumentSnapshot()!);
    var winner = (await repository.LoadAsync(state.Id, default))!; winner.Title = "Actual independent winner";
    var winnerReceipt = await repository.SaveAsync(winner, "Real independent writer", default);
    var physical = SHA256.HashData(await File.ReadAllBytesAsync(winnerReceipt.CurrentPath));
    var conflict = await core.SaveAsync();
    var after = SHA256.HashData(await File.ReadAllBytesAsync(winnerReceipt.CurrentPath));
    Check(!conflict.Succeeded && conflict.Code == "RevisionConflict" && core.DurableRevision == state.Version && core.IsDirty &&
        Canonical(core.GetDocumentSnapshot()!) == draft && physical.SequenceEqual(after),
        "actual canonical CAS rejects stale save and preserves draft plus exact winning durable bytes");
    Check(!(await core.PrepareToCloseAsync()).Succeeded && core.DocumentId == state.Id && core.IsDirty,
        "failed actual close preparation preserves unresolved canonical draft");
}
else
{
    Check((await repository.ListAsync(default)).Count == 0, "unknown-commit actual repository fixture is fresh");
    var loss = new LostReceipt(repository);
    var uncertain = new BoardsBrowserCore(loss, unknownOutcome: error => ReferenceEquals(error, loss.Error));
    loss.Lose = true;
    var create = await uncertain.CreateAsync("Lost actual Boards receipt");
    var id = loss.Receipt!.DocumentId;
    Check(!create.Succeeded && create.Code == "CommitOutcomeUnknown" && uncertain.HasUnknownCommitOutcome && uncertain.IsDirty &&
        uncertain.DocumentId == id && uncertain.DurableRevision == 0 && (await repository.LoadAsync(id, default))!.Version == 1,
        "transparent owner-create payload observation preserves real canonical uncertain identity and stored commit without fake receipt");
    Check(!(await uncertain.CreateAsync()).Succeeded && !(await uncertain.SaveAsync()).Succeeded && !(await uncertain.CloseAsync()).Succeeded &&
        (await repository.ListAsync(default)).Count == 1, "unknown create blocks blind replay and draft discard without duplicate document");
    Check((await uncertain.InspectDurableStateAsync()).Succeeded && !uncertain.HasUnknownCommitOutcome && !uncertain.IsDirty && uncertain.DurableRevision == 1,
        "explicit actual-owner durable inspection confirms same complete intended snapshot and actual committed revision");
    var page = uncertain.GetDocumentSnapshot()!.Sections[0].Pages[0];
    await uncertain.AddParagraphAsync(page.Id, "Actual next Boards draft"); loss.Lose = true;
    Check(!(await uncertain.SaveAsync()).Succeeded && uncertain.HasUnknownCommitOutcome && uncertain.DurableRevision == 1 &&
        (await repository.LoadAsync(id, default))!.Version == 2, "genuine second save receipt loss keeps acknowledged base separate from actual durable revision");
    Check((await uncertain.InspectDurableStateAsync()).Succeeded && !uncertain.IsDirty && uncertain.DurableRevision == 2 &&
        (await repository.LoadAsync(id, default))!.Version == 2, "explicit matching inspection resolves second save without another commit");
    await uncertain.CloseAsync(); await uncertain.DisposeAsync();
}
Console.WriteLine(JsonSerializer.Serialize(new { mode = args[0], assertions, scope = "Actual owner Boards service/NotesRepository portable core; native renderer/browser/donor/ACL not executed" }));
internal sealed record State(Guid Id, long Version, string Canonical, Guid PageId, Guid CardId);
sealed class LostReceipt(INotesRepository actual) : INotesRepository
{
    public bool Lose { get; set; }
    public IOException Error { get; } = new("Controlled loss after genuine canonical owner commit");
    public NotesSaveResult? Receipt { get; private set; }
    public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
    { Receipt = await actual.SaveAsync(document, reason, token); if (Lose) { Lose = false; throw Error; } return Receipt; }
    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => actual.ListAsync(token);
    public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => actual.LoadAsync(id, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => actual.GetVersionsAsync(id, token);
    public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => actual.LoadVersionAsync(id, version, token);
    public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => actual.RecoverLatestAsync(id, token);
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => actual.SearchAsync(query, token);
}
