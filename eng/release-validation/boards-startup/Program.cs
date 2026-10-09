using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Apps.Boards.App;
using CakeOS.Apps.Boards.Contract;

// A source-linked contract control, not an alternative app or renderer. The
// caller supplies a fresh, exclusively owned fixture profile via XDG_DATA_HOME.
if (args.Length != 2) throw new ArgumentException("Expected case and controlled profile directory.");
var scenario = args[0];
var profile = Path.GetFullPath(args[1]);
var path = ContractSessionAdapter.DefaultBoardPath();
var assertions = 0;
Check(Path.GetFullPath(path).StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.Ordinal),
    "The actual platform default path must remain inside the controlled profile before any fixture write.");
using var store = new JsonFileHavenBoardStore(Path.Combine(profile, "store"));
try
{
    switch (scenario)
    {
        case "fresh-default":
        {
            Check(!File.Exists(path) && !File.Exists(path + ".bak"), "Profile must start without primary or backup.");
            Guid identity;
            await using (var board = await ContractSessionAdapter.OpenAsync(store, null))
            {
                Check(board.FilePath == path && File.Exists(path), "Default startup must return a durably saved real board.");
                var actual = await store.LoadDocumentAtPathAsync(path);
                Check(actual is { RichNotes: not null } && actual.DocumentId != Guid.Empty, "The canonical store must load a real stable document.");
                identity = actual!.DocumentId;
                Check(actual.SchemaVersion == HavenBoardDocument.CurrentSchemaVersion, "Canonical schema must be used.");
                Check(board.Document.Sections.Count > 0 && board.Document.Sections[0].Pages.Count > 0, "Canonical editable section/page must exist.");
                board.Document.Title = "Fresh profile saved title";
                board.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph").Text = "Saved through actual adapter";
                board.MarkDirty(); await board.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, null);
            var persisted = await store.LoadDocumentAtPathAsync(path);
            Check(persisted!.DocumentId == identity, "Save/dispose/reopen must preserve Board identity.");
            Check(reopened.Document.Title == "Fresh profile saved title", "Actual title edit must survive reopen.");
            Check(reopened.Document.Sections[0].Pages[0].Blocks.Any(b => b.Text == "Saved through actual adapter"), "Actual content must survive reopen.");
            break;
        }
        case "existing-default":
        {
            var identity = await SeedAsync(); var before = await File.ReadAllBytesAsync(path);
            await using (var board = await ContractSessionAdapter.OpenAsync(store, null))
            {
                Check(board.Document.Title == "Existing canonical board", "Existing title must not be replaced by a new default.");
                Check((await store.LoadDocumentAtPathAsync(path))!.DocumentId == identity, "Existing stable identity must remain unchanged.");
            }
            Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(path)), "Opening/disposal without edits must preserve exact existing bytes.");
            break;
        }
        case "backup-only":
        {
            var identity = await SeedAsync(); File.Move(path, path + ".bak");
            var backup = await File.ReadAllBytesAsync(path + ".bak");
            await using (var board = await ContractSessionAdapter.OpenAsync(store, null))
            {
                Check(store.LastLoadDisposition == HavenBoardLoadDisposition.RecoveredFromBackup, "Missing primary must use existing backup recovery.");
                Check(board.Document.Title == "Existing canonical board", "Backup content must not be replaced by a new board.");
                Check(!File.Exists(path), "Opening backup must not invent an immediate primary write.");
                Check(Enumerable.SequenceEqual(backup, await File.ReadAllBytesAsync(path + ".bak")), "Opening must preserve backup bytes.");
                await board.SaveAsync();
            }
            Check((await store.LoadDocumentAtPathAsync(path))!.DocumentId == identity, "Explicit recovery save must retain canonical identity.");
            Check(Enumerable.SequenceEqual(backup, await File.ReadAllBytesAsync(path + ".bak")), "Explicit recovery save must preserve the last good backup.");
            break;
        }
        case "corrupt-primary-good-backup":
        {
            var identity = await SeedAsync(); File.Copy(path, path + ".bak");
            var backup = await File.ReadAllBytesAsync(path + ".bak");
            var corrupt = "controlled invalid primary"u8.ToArray(); await File.WriteAllBytesAsync(path, corrupt);
            await using (var board = await ContractSessionAdapter.OpenAsync(store, null))
            {
                Check(store.LastLoadDisposition == HavenBoardLoadDisposition.RecoveredFromBackup, "Corrupt primary must recover the canonical backup.");
                Check(Enumerable.SequenceEqual(corrupt, await File.ReadAllBytesAsync(path)), "Opening must not replace corrupt original before explicit save.");
                await board.SaveAsync();
            }
            Check(Enumerable.SequenceEqual(backup, await File.ReadAllBytesAsync(path + ".bak")), "Recovery save must not overwrite known-good backup.");
            Check((await store.LoadDocumentAtPathAsync(path))!.DocumentId == identity, "Recovery must preserve real Board identity.");
            var quarantined = Directory.GetFiles(Path.GetDirectoryName(path)!, "Board.9to1board.corrupt-*.bak");
            Check(quarantined.Length == 1 && Enumerable.SequenceEqual(corrupt, await File.ReadAllBytesAsync(quarantined[0])), "Canonical quarantine must retain original corrupt bytes.");
            break;
        }
        case "corrupt-default-refuses":
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var primary = "controlled invalid primary"u8.ToArray(); var backup = "controlled invalid backup"u8.ToArray();
            await File.WriteAllBytesAsync(path, primary); await File.WriteAllBytesAsync(path + ".bak", backup);
            await RefusesAsync<InvalidDataException>(() => ContractSessionAdapter.OpenAsync(store, null));
            Check(Enumerable.SequenceEqual(primary, await File.ReadAllBytesAsync(path)), "Invalid primary must remain unchanged.");
            Check(Enumerable.SequenceEqual(backup, await File.ReadAllBytesAsync(path + ".bak")), "Invalid backup must remain unchanged.");
            break;
        }
        case "future-default-refuses":
        {
            await SeedAsync(); File.Copy(path, path + ".bak"); var backup = await File.ReadAllBytesAsync(path + ".bak");
            var future = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            future["schemaVersion"] = HavenBoardDocument.CurrentSchemaVersion + 1;
            await File.WriteAllTextAsync(path, future.ToJsonString()); var original = await File.ReadAllBytesAsync(path);
            await RefusesAsync<UnsupportedHavenBoardDocumentVersionException>(() => ContractSessionAdapter.OpenAsync(store, null));
            Check(Enumerable.SequenceEqual(original, await File.ReadAllBytesAsync(path)), "Future primary must not be silently downgraded or replaced.");
            Check(Enumerable.SequenceEqual(backup, await File.ReadAllBytesAsync(path + ".bak")), "Backup must remain unchanged after future-version refusal.");
            break;
        }
        case "cancelled-fresh-default":
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await RefusesAsync<OperationCanceledException>(() => ContractSessionAdapter.OpenAsync(store, null, cancelled.Token));
            Check(!File.Exists(path) && !File.Exists(path + ".bak"), "Cancelled startup must not create a primary or backup.");
            Check(!Directory.Exists(Path.Combine(profile, "store")), "Cancelled startup must not create alternate persisted state.");
            break;
        }
        case "default-path-is-directory":
        {
            Directory.CreateDirectory(path); var occupant = Path.Combine(path, "preserve.txt");
            await File.WriteAllTextAsync(occupant, "controlled existing directory content");
            await RefusesAsync<IOException>(() => ContractSessionAdapter.OpenAsync(store, null));
            Check(Directory.Exists(path) && await File.ReadAllTextAsync(occupant) == "controlled existing directory content", "A conflicting directory must never be replaced.");
            break;
        }
        case "explicit-open-preserved":
        {
            var explicitPath = Path.Combine(profile, "Explicit board");
            await using (var created = await ContractSessionAdapter.OpenAsync(store, explicitPath))
            {
                Check(created.FilePath == explicitPath + ".9to1board", "Existing explicit-path extension policy must remain unchanged.");
                created.Document.Title = "Explicit retained"; created.MarkDirty(); await created.SaveAsync();
            }
            var before = await File.ReadAllBytesAsync(explicitPath + ".9to1board");
            await using (var opened = await ContractSessionAdapter.OpenAsync(store, explicitPath))
                Check(opened.Document.Title == "Explicit retained", "Bare-name reopen must resolve existing sibling, not create another board.");
            Check(Enumerable.SequenceEqual(before, await File.ReadAllBytesAsync(explicitPath + ".9to1board")), "Explicit existing-file open/dispose must preserve exact bytes.");
            Check(!File.Exists(path), "Explicit open must not create the default board.");
            break;
        }
        default: throw new ArgumentException("Unknown startup control.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { scenario, status = "PASS", assertions, platform = Environment.OSVersion.Platform.ToString(), qualification = "Actual source-linked adapter/contract persistence; no graphical app/native Windows acceptance." }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { scenario, status = "FAIL", assertions, type = error.GetType().FullName, message = error.Message }));
    Console.Error.WriteLine(error.StackTrace);
    return 1;
}

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    assertions++;
}
async Task RefusesAsync<T>(Func<Task<ContractSessionAdapter>> operation) where T : Exception
{
    try { await using var unexpected = await operation(); }
    catch (T) { assertions++; return; }
    throw new InvalidOperationException("Required refusal was not observed: " + typeof(T).Name);
}
async Task<Guid> SeedAsync()
{
    await using var real = await RichBoardSession.CreateNewAsync(store, "Existing canonical board");
    await real.SaveAsAsync(path);
    return real.Document.DocumentId;
}
