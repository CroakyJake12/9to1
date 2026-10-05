using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Core.Forms;
using HavenOS.Forms;
using HavenOS.Home.Core;
using NineToOne.Web;
using NineToOne.Web.Forms;

if (args.Length != 2 || args[0] is not ("suite" or "seed" or "verify")) throw new ArgumentException("Pass suite|seed|verify and an isolated absolute evidence root.");
var mode = args[0]; var root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
if (mode != "suite")
{
    Console.WriteLine($"EXPECTED=1 mode={mode}; actualOS/Home/settings/Forms; browser=NOT_RUN; HTTP=NOT_RUN");
    try { if (mode == "seed") await Seed(root); else await Verify(root); Console.WriteLine($"RESULT mode={mode} expected=1 executed=1 passed=1 failed=0 assertions={Checks.Count}"); return 0; }
    catch (Exception error) { Console.WriteLine($"FAIL {mode}: {error}"); Console.WriteLine($"RESULT mode={mode} expected=1 executed=1 passed=0 failed=1 assertions={Checks.Count}"); return 1; }
}
var tests = new (string Name, Func<string, Task> Run)[]
{
    ("canonical-create-field-edit-save-close-reopen", Lifecycle),
    ("actual-cas-conflict-retains-inspector-and-source", Conflict),
    ("actual-home-revocation-clears-private-inspector", Revocation),
    ("actual-store-replacement-denies-retained-session", RootReplacement),
    ("missing-owner-and-unsupported-context-are-not-substituted", MissingOwner),
    ("presentation-admission-and-reject-retain-original-draft", Admission),
    ("pending-navigation-cancellation-does-not-revoke-consumed-owner", Cancellation),
    ("canonical-choice-table-and-page-identities-survive-edits", TypedFields),
    ("actual-owner-preview-does-not-write-responses", Preview),
    ("typed-unknown-commit-blocks-repeat-until-explicit-recovery", UnknownCommit),
    ("unsupported-canonical-source-remains-byte-identical", UnsupportedSource),
    ("presentation-failure-cannot-lock-original-workspace", PresentationFailure),
    ("independent-native-seed-and-verify-processes", IndependentProcesses)
};
Console.WriteLine($"EXPECTED={tests.Length}; actualOS/Home/settings/Forms/CUI; browser=NOT_RUN; HTTP=NOT_RUN; publicSubmission=NOT_RUN");
var passed = 0;
foreach (var test in tests)
{
    try { await test.Run(Path.Combine(root, test.Name)); passed++; Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"RESULT expected={tests.Length} executed={tests.Length} passed={passed} failed={tests.Length - passed} assertions={Checks.Count}");
return passed == tests.Length ? 0 : 1;

static void Check(bool condition, string message) => Checks.That(condition, message);
static object? Value(FormsBrowserSession view, string path) { Check(view.TryGetValue(path, out var value), "Binding missing: " + path); return value; }
static async Task<FormsBrowserSession> Open(FormsBrowserRoute route, Guid? id = null)
{
    var request = id is null ? new HomeFeatureNavigationRequest(FormsBrowserRoute.Id) : new(FormsBrowserRoute.Id, "form", id.Value.ToString());
    var result = await route.OpenAsync(request); Check(result.Succeeded && result.ViewState is not null, "Canonical route did not open: " + result.Code);
    var surface = route.CreateSurface(result.ViewState!); Check(surface.Admission is not null && surface.Bindings is FormsBrowserSession && surface.Actions == surface.Bindings, "Original workspace adapter not returned.");
    surface.Admission!.Accept(); surface.Lifetime!.Dispose(); return (FormsBrowserSession)surface.Bindings;
}
static async Task<Guid> Create(FormsBrowserSession view)
{
    Check(view.TrySetValue("Title", "Canonical Forms browser draft"), "Title not editable."); await view.DispatchAsync("9to1.Forms.Create", null);
    Check(view.FormID is { } id && id != Guid.Empty, "Actual owner create did not return FormID."); return view.FormID!.Value;
}
static async Task AddText(FormsBrowserSession view, string text)
{
    await view.DispatchAsync("9to1.Forms.AddText", null); Check(view.TrySetValue("Label", text), "Field inspector not writable.");
    Check(view.TrySetValue("Help", "Structured help"), "Help not writable."); await view.DispatchAsync("9to1.Forms.SaveField", null);
}
static async Task Lifecycle(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); FormPublication original;
    using (var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true))
    {
        var view = await Open(route); var id = await Create(view); await AddText(view, "Question before closing");
        original = await fixture.PublicationAsync(id); var project = NativeFormsFixture.Project(original);
        Check(project.Fields.Single().Label == "Question before closing" && project.Fields.Single().Help == "Structured help" && project.Pages.Single().Children.Single().ID == project.Fields.Single().FieldID, "Original engine lost field/page identity or content.");
        Check((await route.PrepareToCloseAsync()).Succeeded && !route.HasUnsavedChanges, "Saved inspector was marked dirty.");
        Check(view.IsActionAvailable("9to1.Forms.Publish") == false && view.IsActionAvailable("9to1.Forms.Respond") == false, "Unavailable public services were enabled.");
    }
    var restarted = await NativeFormsFixture.CreateAsync(root, bindNew: false); using var reopened = new FormsBrowserRoute(restarted.OpenOwnerAsync, () => true);
    var restored = await Open(reopened, original.FormID); var actual = await restarted.PublicationAsync(original.FormID);
    Check(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(original) && restored.FormID == original.FormID && Value(restored, "Label") as string == "Question before closing", "Fresh owner changed full canonical publication/schema/revision/IDs/content.");
}
static async Task Conflict(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var id = await Create(view); await AddText(view, "Original");
    Check(view.TrySetValue("Label", "Retained inspector draft"), "Draft unavailable."); var old = await fixture.PublicationAsync(id); var field = NativeFormsFixture.Project(old).Fields.Single();
    Check((await fixture.Authoring.UpdateFieldAsync(id, old.Revision, field with { Label = "Actual concurrent edit" })).Success, "Actual concurrent author edit failed.");
    try { await view.DispatchAsync("9to1.Forms.SaveField", null); throw new Exception("Stale save accepted."); } catch (InvalidOperationException error) when (error.Message == "RevisionConflict") { }
    Check(Value(view, "Label") as string == "Retained inspector draft" && route.HasUnsavedChanges && !(await route.PrepareToCloseAsync()).Succeeded, "Conflict discarded draft or allowed destructive close.");
    Check(NativeFormsFixture.Project(await fixture.PublicationAsync(id)).Fields.Single().Label == "Actual concurrent edit", "Stale editor overwrote actual source.");
}
static async Task Revocation(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var id = await Create(view); await AddText(view, "Private source"); view.TrySetValue("Label", "Private draft");
    await fixture.RevokeActualHomeBindingAsync(); var before = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"));
    try { await view.DispatchAsync("9to1.Forms.SaveField", null); throw new Exception("Revoked owner wrote."); } catch (InvalidOperationException error) when (error.Message == "PermissionDenied") { }
    Check(view.FormID is null && Value(view, "Label") as string == "" && Value(view, "FieldNames") is string[] names && names.Length == 0 && view.IsActionAvailable("9to1.Forms.Create") == false, "Actual denial retained private presentation.");
    var after = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")); Check(before.SequenceEqual(after), "Denied edit changed actual durable source.");
    Check((await fixture.Publications.ReadAsync(id)).Code == "PermissionDenied", "New service bypassed actual Home revocation.");
}
static async Task RootReplacement(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); await Create(view); await AddText(view, "Original store");
    var original = await fixture.Settings.GetStoreIdentityAsync(default); var file = Path.Combine(root, "settings.json"); var text = await File.ReadAllTextAsync(file);
    Check(text.Contains(original.StoreId.ToString("D"), StringComparison.OrdinalIgnoreCase), "Actual durable StoreID missing.");
    await File.WriteAllTextAsync(file, text.Replace(original.StoreId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.OrdinalIgnoreCase)); var replaced = await File.ReadAllBytesAsync(file);
    try { await view.DispatchAsync("9to1.Forms.AddText", null); throw new Exception("Replacement root accepted."); } catch (UnauthorizedAccessException) { }
    var after = await File.ReadAllBytesAsync(file); Check(view.FormID is null && Value(view, "Label") as string == "" && replaced.SequenceEqual(after), "Retained session read/wrote replacement root or kept private source.");
}
static async Task MissingOwner(string root)
{
    using var missing = new FormsBrowserRoute(null, () => true); var request = new HomeFeatureNavigationRequest(FormsBrowserRoute.Id);
    var unavailable = await missing.OpenAsync(request); Check(!unavailable.Succeeded && unavailable.Code == "HomeServiceUnavailable" && unavailable.Request == request, "Missing owner replaced by private engine.");
    var fixture = await NativeFormsFixture.CreateAsync(root, bindNew: false); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true);
    foreach (var wrong in new[] { new HomeFeatureNavigationRequest(FormsBrowserRoute.Id, "response", Guid.NewGuid().ToString()), new(FormsBrowserRoute.Id, Action: "submit"), new(FormsBrowserRoute.Id, DeepLink: "?private"), new(FormsBrowserRoute.Id, ModelPickerTarget: new("forms", null, "data", null)) })
    { var result = await route.OpenAsync(wrong); Check(!result.Succeeded && result.Request == wrong, "Unsupported target silently ignored."); }
    var view = await Open(route); try { await Create(view); throw new Exception("Unowned actual Forms store granted creation."); } catch (InvalidOperationException error) when (error.Message == "PermissionDenied") { }
    Check(!(await fixture.Settings.ExportAsync(default)).Settings.Keys.Any(key => key.StartsWith("forms.publication.v1.", StringComparison.Ordinal)), "Unowned creation persisted private data.");
    var owned = await NativeFormsFixture.CreateAsync(Path.Combine(root, "actual-owned-negative-control"));
    Check(await owned.VerifyPrivateActorsRejectedAsync(), "Actual local Forms authority did not reject explicit account/organisation actor inputs with a valid local ownership control.");
}
static async Task Admission(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true);
    var pending = await route.OpenAsync(new(FormsBrowserRoute.Id)); var candidate = route.CreateSurface(pending.ViewState!); var view = (FormsBrowserSession)candidate.Bindings;
    Check(view.IsActionAvailable("9to1.Forms.Create") == false && !view.TrySetValue("Title", "Premature write"), "Prepared CUI granted write before admission."); candidate.Admission!.Accept(); candidate.Lifetime!.Dispose();
    var id = await Create(view); await AddText(view, "Source"); view.TrySetValue("Label", "Private retained draft");
    Check(view.IsActionAvailable("9to1.Forms.Create") == false, "Creating another form could discard the current private inspector.");
    var next = await route.OpenAsync(new(FormsBrowserRoute.Id, "form", id.ToString())); var rejected = route.CreateSurface(next.ViewState!); rejected.Admission!.Reject(); rejected.Lifetime!.Dispose();
    Check(view.FormID == id && Value(view, "Label") as string == "Private retained draft" && route.HasUnsavedChanges, "Rejected CUI destroyed original owner draft.");
    var other = await route.OpenAsync(new(FormsBrowserRoute.Id, "form", Guid.NewGuid().ToString())); Check(!other.Succeeded && other.Code == "FormsUnsavedChanges", "Other target discarded original unsaved inspector.");
    view.TrySetValue("Label", "Source"); view.TrySetValue("Help", "Structured help");
    Check((await route.PrepareToCloseAsync()).Succeeded, "Restoring original inspector text did not clear actual owner dirty state.");
    var originalTitle = Value(view, "Title"); view.TrySetValue("Title", "Uncreated next form title");
    Check(!(await route.PrepareToCloseAsync()).Succeeded, "Uncreated owner title draft was discarded by close preparation.");
    view.TrySetValue("Title", originalTitle); Check((await route.PrepareToCloseAsync()).Succeeded, "Restoring original title did not clear its presentation draft.");
}
static async Task Cancellation(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true);
    using var pendingToken = new CancellationTokenSource(); var pending = await route.OpenAsync(new(FormsBrowserRoute.Id), pendingToken.Token); pendingToken.Cancel();
    try { route.CreateSurface(pending.ViewState!); throw new Exception("Cancelled candidate stayed renderable."); } catch (InvalidOperationException) { }
    using var consumedToken = new CancellationTokenSource(); var prepared = await route.OpenAsync(new(FormsBrowserRoute.Id), consumedToken.Token); var surface = route.CreateSurface(prepared.ViewState!); surface.Admission!.Accept(); consumedToken.Cancel();
    var view = (FormsBrowserSession)surface.Bindings; var id = await Create(view); Check((await fixture.PublicationAsync(id)).FormID == id, "Consumed navigation token revoked actual owner.");
    try { route.CreateSurface(prepared.ViewState!); throw new Exception("Consumed surface was reusable."); } catch (InvalidOperationException) { }
}
static async Task TypedFields(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var id = await Create(view);
    var palette = (string[])Value(view, "PaletteNames")!; Check(view.TrySetValue("SelectedPaletteIndex", Array.IndexOf(palette, "Single choice")), "Choice palette unavailable."); await view.DispatchAsync("9to1.Forms.AddField", null);
    var choiceBefore = NativeFormsFixture.Project(await fixture.PublicationAsync(id)).Fields.Single(); Check(view.TrySetValue("OptionLabel", "Canonical option"), "Choice draft unavailable."); await view.DispatchAsync("9to1.Forms.UpdateChoice", null); await view.DispatchAsync("9to1.Forms.AddChoice", null);
    Check(view.TrySetValue("SelectedPaletteIndex", Array.IndexOf(palette, "Table input")), "Table palette unavailable."); await view.DispatchAsync("9to1.Forms.AddField", null);
    Check(view.TrySetValue("SelectedColumnTypeIndex", 1), "Typed column palette unavailable."); await view.DispatchAsync("9to1.Forms.AddColumn", null); await view.DispatchAsync("9to1.Forms.AddFixedRow", null); await view.DispatchAsync("9to1.Forms.AddPage", null);
    var current = NativeFormsFixture.Project(await fixture.PublicationAsync(id)); var choice = current.Fields.Single(field => field.FieldID == choiceBefore.FieldID); var table = current.Fields.Single(field => field.Kind == FormFieldKind.TableInput).Table!;
    Check(choice.Options!.Count == 3 && choice.Options[0].OptionID == choiceBefore.Options![0].OptionID && choice.Options[0].Label == "Canonical option", "Choice editor duplicated or replaced original option identity.");
    Check(table.Columns.Count == 2 && table.Columns.Last().Type == FormTableCellType.Number && table.FixedRowIDs!.Count == 1 && current.Pages.Count == 2, "Actual TableInput lost typed columns/rows/pages.");
    Check(FormProjectCodec.Encode(FormProjectCodec.Decode(FormProjectCodec.Encode(current))).SequenceEqual(FormProjectCodec.Encode(current)), "Canonical typed project roundtrip changed identities/content.");
}
static async Task Preview(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); var previewOpened = false;
    using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true, async (preview, ct) =>
    {
        previewOpened = true; _ = preview.CreateDocument(); await preview.DispatchAsync("9to1.Forms.Preview.Viewport.Mobile", null, ct); Check(preview.PreviewWidth == 390, "Actual mobile preview mode unavailable.");
    });
    var view = await Open(route); await Create(view); await AddText(view, "Preview source"); var before = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"));
    await view.DispatchAsync("9to1.Forms.Preview", null); var after = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json"));
    Check(previewOpened && before.SequenceEqual(after) && !(await fixture.Settings.ExportAsync(default)).Settings.Keys.Any(key => key.StartsWith("forms.response", StringComparison.Ordinal)), "Actual author preview created durable respondent/submission state.");
}
static async Task UnknownCommit(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var id = await Create(view); await AddText(view, "Before lost reply");
    var before = await fixture.PublicationAsync(id); view.TrySetValue("Label", "After actual commit"); fixture.Settings.LoseNextCommittedReply = true;
    try { await view.DispatchAsync("9to1.Forms.SaveField", null); throw new Exception("Lost commit reply accepted as success."); } catch (InvalidOperationException error) when (error.Message == "StorageUnavailable") { }
    Check(view.IsActionAvailable("9to1.Forms.SaveField") == false && !view.TrySetValue("Label", "Changed uncertain intent") && !(await route.PrepareToCloseAsync()).Succeeded, "Unknown mutation allowed repeated save/intent change or destructive close.");
    var persisted = await fixture.PublicationAsync(id); Check(persisted.Revision == before.Revision + 1 && NativeFormsFixture.Project(persisted).Fields.Single().Label == "After actual commit", "Actual lost response control did not commit exactly once.");
    try { await view.DispatchAsync("9to1.Forms.SaveField", null); throw new Exception("Disabled save executed."); } catch (InvalidOperationException error) when (error.Message == "Forms action is unavailable.") { }
    Check((await fixture.PublicationAsync(id)).Revision == persisted.Revision, "Unknown outcome replayed mutation.");
    await view.DispatchAsync("9to1.Forms.Open", null); Check(view.IsActionAvailable("9to1.Forms.SaveField") == false && Value(view, "Label") as string == "After actual commit", "Owner reload silently discarded pending inspector or re-enabled replay.");
    await view.DispatchAsync("9to1.Forms.DiscardInspector", null); Check((await route.PrepareToCloseAsync()).Succeeded && Value(view, "Label") as string == "After actual commit", "Explicit recovery lost actual saved source.");
}
static async Task UnsupportedSource(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); Guid id;
    using (var original = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true)) { var view = await Open(original); id = await Create(view); }
    var publication = await fixture.PublicationAsync(id); var node = JsonNode.Parse(publication.Draft.GetRawText())!.AsObject(); node["FutureRequiredContent"] = new JsonObject { ["DoNotDrop"] = true };
    await fixture.Settings.SetAsync("forms.publication.v1." + id.ToString("N"), publication with { Draft = JsonSerializer.SerializeToElement(node) }, default);
    var before = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var result = await route.OpenAsync(new(FormsBrowserRoute.Id, "form", id.ToString()));
    var after = await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")); Check(!result.Succeeded && before.SequenceEqual(after), "Unsupported required source was opened/reset/rewritten instead of preserved.");
}
static async Task PresentationFailure(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var failures = 0; var delivered = 0;
    view.PropertyChanged += (_, _) => { failures++; throw new InvalidOperationException("Induced presentation observer failure."); }; view.PropertyChanged += (_, _) => delivered++;
    var id = await Create(view); await AddText(view, "After observer failures"); var source = await fixture.PublicationAsync(id);
    Check(failures > 0 && delivered > 0 && source.Revision == 3 && !route.HasUnsavedChanges, "Observer failure locked unchanged workspace, suppressed listeners, repeated commits or lost success.");
}
static async Task IndependentProcesses(string root) { Directory.CreateDirectory(root); await Child("seed", root); await Child("verify", root); }
static async Task Child(string mode, string root)
{
    var executable = Environment.ProcessPath ?? throw new Exception("Actual executable path missing."); var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(executable) == "dotnet") start.ArgumentList.Add(typeof(Checks).Assembly.Location); start.ArgumentList.Add(mode); start.ArgumentList.Add(root);
    using var child = Process.Start(start) ?? throw new Exception("Independent process failed to start."); var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); try { await child.WaitForExitAsync(deadline.Token); } catch { child.Kill(entireProcessTree: true); throw; }
    var output = await stdout; var errors = await stderr; await File.WriteAllTextAsync(Path.Combine(root, mode + ".log"), output + errors); await File.WriteAllTextAsync(Path.Combine(root, mode + ".exit"), child.ExitCode.ToString()); Console.Write(output); Console.Write(errors); Check(child.ExitCode == 0, "Independent " + mode + " failed.");
}
static async Task Seed(string root)
{
    var fixture = await NativeFormsFixture.CreateAsync(root); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true); var view = await Open(route); var id = await Create(view); await AddText(view, "Independent native restart <oracle> & content");
    var publication = await fixture.PublicationAsync(id); var identity = await fixture.Settings.GetStoreIdentityAsync(default); var actor = await fixture.Actors.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException();
    Check((await route.PrepareToCloseAsync()).Succeeded, "Seed retained unsaved inspector.");
    await File.WriteAllTextAsync(Path.Combine(root, "canonical-oracle.json"), JsonSerializer.Serialize(new Oracle(identity.StoreId, actor.ProfileId, publication, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")))).ToLowerInvariant())));
}
static async Task Verify(string root)
{
    var oracle = JsonSerializer.Deserialize<Oracle>(await File.ReadAllTextAsync(Path.Combine(root, "canonical-oracle.json")))!; var fixture = await NativeFormsFixture.CreateAsync(root, bindNew: false); using var route = new FormsBrowserRoute(fixture.OpenOwnerAsync, () => true);
    var view = await Open(route, oracle.Publication.FormID); var publication = await fixture.PublicationAsync(oracle.Publication.FormID); var identity = await fixture.Settings.GetStoreIdentityAsync(default); var actor = await fixture.Actors.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException();
    Check(identity.StoreId == oracle.StoreID && actor.ProfileId == oracle.ProfileID, "Independent restart changed original settings/profile identity.");
    Check(JsonSerializer.Serialize(publication) == JsonSerializer.Serialize(oracle.Publication) && view.FormID == oracle.Publication.FormID, "Independent process changed full canonical publication/schema/project/pages/fields/revisions.");
    Check(Value(view, "Label") as string == "Independent native restart <oracle> & content", "Independent editor lost source text.");
    Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "settings.json")))).ToLowerInvariant() == oracle.SettingsHash, "Read-only owner restart changed durable settings bytes.");
}
sealed record Oracle(Guid StoreID, string ProfileID, FormPublication Publication, string SettingsHash);
static class Checks { public static int Count { get; private set; } public static void That(bool condition, string message) { Count++; if (!condition) throw new Exception(message); } }
