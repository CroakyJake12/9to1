using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Hosting;
using HavenOS.Apps.Sites.Infrastructure;
using HavenOS.Apps.Sites.Runtime;
using HavenOS.Home.Core;
using NineToOne.Web.Sites;

if (args.Length != 2 || args[0] is not ("suite" or "seed" or "verify")) throw new ArgumentException("Pass suite|seed|verify and an isolated absolute evidence directory.");
var mode = args[0]; var dataRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(dataRoot);
if (mode != "suite")
{
    Console.WriteLine($"EXPECTED=1 mode={mode}; actual native OS/Home/Files/Sites; browser=NOT_RUN; publishing=NOT_RUN");
    try
    {
        if (mode == "seed") await Seed(dataRoot); else await Verify(dataRoot);
        Console.WriteLine($"RESULT mode={mode} expected=1 executed=1 passed=1 failed=0 assertions={Checks.Count}"); return 0;
    }
    catch (Exception error) { Console.WriteLine($"FAIL {mode}: {error}"); Console.WriteLine($"RESULT mode={mode} expected=1 executed=1 passed=0 failed=1 assertions={Checks.Count}"); return 1; }
}
var tests = new (string Name, Func<string, Task> Run)[]
{
    ("actual-native-create-page-edit-close-reopen", Lifecycle),
    ("actual-stale-revision-keeps-text-draft", Conflict),
    ("duplicate-page-route-preserves-index-and-draft", DuplicatePage),
    ("lost-native-reply-does-not-repeat-mutation", LostReply),
    ("foreign-owner-receipt-does-not-acknowledge-edit", ForeignReceipt),
    ("actual-text-commit-rejects-text-receipt", root => TextReceipt(root, "text")),
    ("actual-text-commit-rejects-missing-receipt", root => TextReceipt(root, "missing")),
    ("actual-text-commit-rejects-revision-receipt", root => TextReceipt(root, "revision")),
    ("actual-text-commit-rejects-type-receipt", root => TextReceipt(root, "type")),
    ("actual-text-commit-rejects-duplicate-receipt", root => TextReceipt(root, "duplicate")),
    ("actual-files-binding-revocation-clears-private-draft", Revocation),
    ("canonical-html-import-json-roundtrip-render-build", FormatRoundtrip),
    ("unknown-owner-schema-preserves-source-bytes", UnsupportedSchema),
    ("actual-cui-owner-route-cancel-type-and-dispose", Route),
    ("real-owner-denies-foreign-id-and-stale-scope", AuthBoundaries),
    ("presentation-listener-failure-does-not-lock-owner-actions", PresentationFailure),
    ("throwing-owner-cancellation-still-clears-private-view", CancellationTeardown),
    ("independent-native-seed-and-verify-processes", IndependentProcesses),
};
Console.WriteLine($"EXPECTED={tests.Length}; actual native OS/Home/Files/Sites; browser=NOT_RUN; HTTP=NOT_RUN; publishing=NOT_RUN");
var passed = 0;
foreach (var test in tests)
{
    try { await test.Run(Path.Combine(dataRoot, test.Name)); passed++; Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { Console.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"RESULT expected={tests.Length} executed={tests.Length} passed={passed} failed={tests.Length - passed} assertions={Checks.Count}");
return passed == tests.Length ? 0 : 1;

static void Check(bool condition, string message) => Checks.That(condition, message);
static object? Value(SitesBrowserController view, string path) { Check(view.TryGetValue(path, out var value), "Unknown binding: " + path); return value; }
static async Task<SiteProject> CreateWebsite(SitesBrowserController view)
{
    await view.DispatchAsync("NewProject", null); Check(view.TrySetValue("Name", "Canonical browser website"), "Project name not writable.");
    await view.DispatchAsync("Save", null); Check(Value(view, "Status") as string == "Saved.", "Owner create did not save: " + Value(view, "Status"));
    return view.CurrentProject ?? throw new Exception("Owner did not return canonical project.");
}
static async Task<SiteProject> AddPage(SitesBrowserController view)
{
    await view.DispatchAsync("NewPage", null); view.TrySetValue("Name", "Home"); view.TrySetValue("Path", "/"); await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") as string == "Saved.", "Page create did not save: " + Value(view, "Status"));
    return view.CurrentProject!;
}
static async Task<SiteProject> AddText(SitesBrowserController view, string text)
{
    await view.DispatchAsync("AddParagraph", null); view.TrySetValue("Text", text); await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") as string == "Saved.", "Paragraph did not save: " + Value(view, "Status")); return view.CurrentProject!;
}
static async Task SelectText(SitesBrowserController view)
{
    var rows = (SiteComponentRow[])Value(view, "Components")!; await view.DispatchAsync("SelectComponent", rows.Single());
    Check(view.IsActionAvailable("EditText") == true, "Canonical paragraph was not editable.");
}
static async Task<SiteProject> EditText(SitesBrowserController view, string text)
{
    await SelectText(view); await view.DispatchAsync("EditText", null); view.TrySetValue("Text", text); await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") as string == "Saved.", "Text edit did not save: " + Value(view, "Status")); return view.CurrentProject!;
}
static async Task Lifecycle(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root);
    var binding = await fixture.BindingAsync(); SiteProject original;
    using (var view = new SitesBrowserController(fixture.Operations))
    {
        await view.InitializeAsync(null, default); original = await CreateWebsite(view); await AddPage(view); await AddText(view, "Original text");
        var saved = await EditText(view, "Saved <text> & content");
        Check(saved.SiteId == original.SiteId && saved.ProjectId == original.ProjectId && saved.Source.FilesDirectoryId == binding.FilesFolderId && saved.Revision == 4,
            "Canonical identity/source/revision changed incorrectly.");
    }
    using var reopened = new SitesBrowserController(fixture.Operations); await reopened.InitializeAsync(original.SiteId, default);
    Check(reopened.CurrentProject is { } persisted && persisted.Revision == 4 && persisted.Components.Single().Properties["text"].GetString() == "Saved <text> & content", "Close/reopen lost saved text.");
}
static async Task Conflict(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); await CreateWebsite(view); await AddPage(view); var project = await AddText(view, "Original");
    await SelectText(view); await view.DispatchAsync("EditText", null); view.TrySetValue("Text", "My retained draft");
    var node = project.Components.Single(); var external = await fixture.Operations.SetText(project.SiteId, project.Revision, node.ComponentId, "External committed edit", default);
    Check(external.IsSuccess, "Actual concurrent owner update failed."); await view.DispatchAsync("Save", null);
    Check(Value(view, "Text") as string == "My retained draft" && Value(view, "Status") is string status && status.StartsWith("RevisionConflict"), "Stale save discarded draft or hid actual conflict.");
    var current = await fixture.Operations.Open(project.SiteId, default);
    Check(current.Value?.Components.Single().Properties["text"].GetString() == "External committed edit", "Stale update overwrote owner state.");
}
static async Task DuplicatePage(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); await CreateWebsite(view); await AddPage(view);
    var index = Path.Combine((await fixture.BindingAsync()).RootDirectory, ".9to1-sites-index.json"); var original = await File.ReadAllBytesAsync(index);
    await view.DispatchAsync("NewPage", null); view.TrySetValue("Name", "Duplicate draft"); view.TrySetValue("Path", "/"); await view.DispatchAsync("Save", null);
    Check(Value(view, "Name") as string == "Duplicate draft" && Value(view, "Status") is string status && status.StartsWith("InvalidInput"), "Duplicate route did not retain rejected draft.");
    var rejectedBytes = await File.ReadAllBytesAsync(index);
    Check(original.SequenceEqual(rejectedBytes), "Rejected route altered canonical index bytes.");
}
static async Task LostReply(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); await CreateWebsite(view); var project = await AddPage(view);
    await view.DispatchAsync("AddParagraph", null); view.TrySetValue("Text", "Committed once"); fixture.LoseNextMutationReply = true; await view.DispatchAsync("Save", null);
    Check(Value(view, "Status") is string status && status.Contains("outcome is unknown") && view.IsActionAvailable("Save") == false && !view.TrySetValue("Text", "Another intent"), "Unknown native effect could be repeated.");
    await view.DispatchAsync("Save", null); var owner = await fixture.Operations.Open(project.SiteId, default);
    Check(owner.Value is { } actual && actual.Revision == 3 && actual.Components.Count == 1, "Lost reply repeated canonical mutation.");
    await view.DispatchAsync("Reload", null); Check(view.CurrentProject?.Revision == 3 && Value(view, "Editing") is false, "Explicit owner reload failed to recover actual save.");
}
static async Task ForeignReceipt(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); var project = await CreateWebsite(view); await AddPage(view);
    await view.DispatchAsync("AddParagraph", null); view.TrySetValue("Text", "Real owner text"); fixture.ForeignNextMutationReceipt = true; await view.DispatchAsync("Save", null);
    Check(view.CurrentProject is { } retained && retained.SiteId == project.SiteId && retained.Revision == 2 && Value(view, "Status") is string status && status.Contains("outcome is unknown"), "Foreign receipt acknowledged another project.");
    await view.DispatchAsync("Reload", null); Check(view.CurrentProject is { } recovered && recovered.SiteId == project.SiteId && recovered.Revision == 3, "Foreign receipt reload did not restore original canonical project.");
}
static async Task TextReceipt(string root, string variant)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var actual = fixture.Operations; var mutationCalls = 0;
    var replies = actual with { SetText = async (site, revision, component, text, ct) =>
    {
        mutationCalls++;
        var committed = await actual.SetText(site, revision, component, text, ct);
        if (!committed.IsSuccess) return committed;
        var receiptProject = committed.Value!; var target = receiptProject.Components.Single(node => node.ComponentId == component);
        // Only the reply changes AFTER the actual canonical/Home-authorized commit. Storage is genuine.
        var changed = variant switch
        {
            "text" => receiptProject.Components.Select(node => node.ComponentId == component ? node with
            {
                Properties = node.Properties.ToDictionary(property => property.Key,
                    property => property.Key == "text" ? JsonSerializer.SerializeToElement("Reply-only altered text") : property.Value.Clone())
            } : node).ToArray(),
            "missing" => receiptProject.Components.Where(node => node.ComponentId != component).ToArray(),
            "revision" => receiptProject.Components.Select(node => node.ComponentId == component ? node with { Revision = checked(node.Revision + 1) } : node).ToArray(),
            "type" => receiptProject.Components.Select(node => node.ComponentId == component ? node with { ComponentType = "heading" } : node).ToArray(),
            "duplicate" => receiptProject.Components.Append(target).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        return SiteApiResult<SiteProject>.Success(receiptProject with { Components = changed });
    } };
    using var view = new SitesBrowserController(replies); await view.InitializeAsync(null, default);
    await CreateWebsite(view); await AddPage(view); var before = await AddText(view, "Original canonical text");
    var originalTarget = before.Components.Single(); const string intendedText = "Actual durable typed edit <content> & source";
    await SelectText(view); await view.DispatchAsync("EditText", null); Check(view.TrySetValue("Text", intendedText), "Typed edit was unavailable.");
    await view.DispatchAsync("Save", null);
    Check(view.CurrentProject is { } retained && retained.SiteId == before.SiteId && retained.Revision == before.Revision
        && Value(view, "Text") as string == intendedText && Value(view, "Editing") is true
        && Value(view, "Status") is string status && status.Contains("outcome is unknown"), "Contradictory text receipt acknowledged an incompatible edit: " + variant);
    Check(view.IsActionAvailable("Save") == false && !view.TrySetValue("Text", "Changed intent") && mutationCalls == 1,
        "Contradictory text receipt admitted another mutation: " + variant);
    var binding = await fixture.BindingAsync(); var index = Path.Combine(binding.RootDirectory, ".9to1-sites-index.json");
    var committedBytes = await File.ReadAllBytesAsync(index);
    var saved = await actual.Open(before.SiteId, default);
    Check(saved.IsSuccess && saved.Value is { } project && project.Source == before.Source && project.ProjectId == before.ProjectId
        && project.Revision == before.Revision + 1 && project.Components.Single().ComponentId == originalTarget.ComponentId
        && project.Components.Single().Revision == originalTarget.Revision + 1 && project.Components.Single().ComponentType == originalTarget.ComponentType
        && project.Components.Single().Properties["text"].GetString() == intendedText, "Reply mutation changed actual canonical commit: " + variant);
    await view.DispatchAsync("Save", null);
    var afterRepeatedSaveBytes = await File.ReadAllBytesAsync(index);
    Check(mutationCalls == 1 && committedBytes.SequenceEqual(afterRepeatedSaveBytes), "Unknown text receipt repeated actual native commit: " + variant);
    await view.DispatchAsync("Reload", null);
    Check(view.CurrentProject is { } reopened && JsonSerializer.Serialize(reopened) == JsonSerializer.Serialize(saved.Value)
        && Value(view, "Editing") is false && mutationCalls == 1, "Explicit original-source reload did not recover exact canonical graph: " + variant);
    var afterExplicitReloadBytes = await File.ReadAllBytesAsync(index);
    Check(committedBytes.SequenceEqual(afterExplicitReloadBytes), "Read-only recovery changed canonical index bytes: " + variant);
}
static async Task Revocation(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); await CreateWebsite(view); await AddPage(view); await AddText(view, "Private source");
    await SelectText(view); await view.DispatchAsync("EditText", null); view.TrySetValue("Text", "Private draft");
    await fixture.DeleteActualSitesFolderAsync(); await view.DispatchAsync("Save", null);
    Check(view.CurrentProject is null && view.CurrentPage is null && Value(view, "Text") as string == "" && ((SiteProjectRow[])Value(view, "Projects")!).Length == 0
        && view.IsActionAvailable("Save") == false && Value(view, "Status") is string status && status.Contains("access was denied"), "Actual Files revocation retained private Sites state.");
}
static async Task FormatRoundtrip(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); var project = await CreateWebsite(view);
    var projects = await fixture.ProjectsAsync(); var imported = await new SiteHtmlContentImporter(projects).ImportPageAsync(project.SiteId, project.Revision, "Imported", "/",
        "<main><h1>A &amp; B</h1><p>Rich <strong>bold</strong> and <em>emphasis</em><br>line two</p><ul><li>One</li></ul><table><tbody><tr><th>Heading</th><td>Cell</td></tr></tbody></table></main>");
    Check(imported.Project is not null && !imported.Issues.Any(issue => issue.BlocksImport), "Canonical HTML importer blocked supported source.");
    var saved = imported.Project!; var renderer = new SiteDocumentRenderer(); var preview = renderer.Render(saved, saved.Pages.Single().PageId, SiteRenderContext.PagePreview);
    Check(preview.Diagnostics.Count == 0 && preview.Document.Contains("<strong") && preview.Document.Contains("<table") && preview.Document.Contains("A &amp; B"), "Canonical render lost imported semantic content.");
    var exportedPath = Path.Combine(root, "site.project.json"); await File.WriteAllTextAsync(exportedPath, JsonSerializer.Serialize(saved));
    var exported = JsonSerializer.Deserialize<SiteProject>(await File.ReadAllTextAsync(exportedPath))!;
    Check(JsonSerializer.Serialize(exported) == JsonSerializer.Serialize(saved) && renderer.Render(exported, exported.Pages.Single().PageId, SiteRenderContext.PagePreview).Document == preview.Document,
        "Canonical SiteProject JSON roundtrip changed identity, graph or rendered content.");
    var artifact = await new SiteArtifactAuthoringService(projects, Path.Combine((await fixture.BindingAsync()).RootDirectory, ".9to1-site-builds")).BuildAsync(saved.SiteId, saved.Revision, "local-test-config");
    Check(!artifact.SecretScanPassed, "Absent secret scanner was incorrectly reported passing.");
    Check(await File.ReadAllTextAsync(Path.Combine(artifact.ArtifactReference, "index.html")) == preview.Document, "Real build did not share preview renderer.");
    var pipeline = new NativeSiteBuildPipeline(Path.Combine(root, "validation")); Check((await pipeline.ValidateArtifactAsync(artifact, default)).IsValid, "Real build manifest invalid.");
    await File.AppendAllTextAsync(Path.Combine(artifact.ArtifactReference, "index.html"), "tampered"); Check(!(await pipeline.ValidateArtifactAsync(artifact, default)).IsValid, "Tampered real artifact accepted.");
    var originalBytes = await File.ReadAllBytesAsync(Path.Combine((await fixture.BindingAsync()).RootDirectory, ".9to1-sites-index.json"));
    var blocked = await new SiteHtmlContentImporter(projects).ImportPageAsync(saved.SiteId, saved.Revision, "Unsupported", "/unsupported", "<script>console.log('source must not silently vanish')</script>");
    Check(blocked.Project is null && blocked.Issues.Any(issue => issue.BlocksImport), "Unsupported source was silently imported.");
    var blockedBytes = await File.ReadAllBytesAsync(Path.Combine((await fixture.BindingAsync()).RootDirectory, ".9to1-sites-index.json"));
    Check(originalBytes.SequenceEqual(blockedBytes), "Blocked import changed canonical source.");
}
static async Task UnsupportedSchema(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); var project = await CreateWebsite(view); var binding = await fixture.BindingAsync(); var path = Path.Combine(binding.RootDirectory, ".9to1-sites-index.json");
    var snapshot = JsonSerializer.Deserialize<SiteWorkspaceSnapshot>(await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot with { SchemaVersion = 999 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var unknown = await File.ReadAllBytesAsync(path); var result = await new SiteProjectService(new FileSiteWorkspaceStore(binding.RootDirectory)).GetProjectAsync(project.SiteId);
    Check(result.Error?.Code == "SitesSchemaUnsupported", "Actual owner silently accepted unknown schema.");
    var rereadBytes = await File.ReadAllBytesAsync(path);
    Check(unknown.SequenceEqual(rereadBytes), "Unknown source was reset or overwritten.");
}
static CuiDocument Document()
{
    var parser = new CuiRichParser(); var document = parser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sites.cui")), "Sites.cui");
    Check(!parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error), "Actual Sites CUI parser diagnostics: " + string.Join("; ", parser.Diagnostics.Diagnostics)); return document;
}
static async Task Route(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var route = new SitesBrowserRoute(fixture.Operations, Document());
    Check(!(await route.OpenAsync(new(SitesBrowserRoute.Id, "deployment", Guid.NewGuid().ToString()))).Succeeded, "Hosted deployment was substituted for authoring.");
    foreach (var request in new[] { new HomeFeatureNavigationRequest(SitesBrowserRoute.Id, DeepLink: "?private"), new(SitesBrowserRoute.Id, ModelPickerTarget: new("sites", null, "text", null)) })
    { var rejected = await route.OpenAsync(request); Check(!rejected.Succeeded && rejected.Request == request, "Unsupported context ignored or replaced."); }
    var result = await route.OpenAsync(new(SitesBrowserRoute.Id)); Check(result.Succeeded && result.ViewState is not null, "Actual Sites route did not open.");
    var surface = route.CreateSurface(result.ViewState!); var view = (SitesBrowserController)surface.Bindings; var project = await CreateWebsite(view); surface.Lifetime!.Dispose();
    Check(view.CurrentProject is null && view.IsActionAvailable("NewProject") == false, "Disposed private route retained source/writes.");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await route.OpenAsync(new(SitesBrowserRoute.Id), cancelled.Token); throw new Exception("Cancelled route returned a view."); } catch (OperationCanceledException) { }
    using var pendingCancellation = new CancellationTokenSource();
    var pending = await route.OpenAsync(new(SitesBrowserRoute.Id, "site", project.SiteId.ToString()), pendingCancellation.Token);
    Check(pending.Succeeded && pending.ViewState is not null, "Pending navigation did not open."); pendingCancellation.Cancel();
    try { route.CreateSurface(pending.ViewState!); throw new Exception("Cancelled pending private surface remained renderable."); } catch (InvalidOperationException) { }
    using var consumedCancellation = new CancellationTokenSource();
    var consumed = await route.OpenAsync(new(SitesBrowserRoute.Id), consumedCancellation.Token);
    var consumedSurface = route.CreateSurface(consumed.ViewState!); using var lifetime = consumedSurface.Lifetime; consumedCancellation.Cancel();
    Check(consumedSurface.Actions is SitesBrowserController live && live.IsActionAvailable("NewProject") == true, "Consumed lease retained navigation cancellation.");
    try { route.CreateSurface(consumed.ViewState!); throw new Exception("Consumed private surface was reusable."); } catch (InvalidOperationException) { }
}
static async Task AuthBoundaries(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations); await view.InitializeAsync(null, default); var project = await CreateWebsite(view); await AddPage(view);
    Check(await fixture.Resources.AuthorizeAsync("sites.project.read", [new("sites.project", Guid.NewGuid().ToString(), "1", ResourceAccess.Read)]) is null, "Foreign project scope authorised.");
    Check(await fixture.Resources.AuthorizeAsync("sites.project.save", [new("sites.project", project.SiteId.ToString(), "1", ResourceAccess.Write)]) is null, "Stale project scope authorised.");
    var binding = await fixture.BindingAsync(); Check(await fixture.Resources.AuthorizeAsync("sites.project.create", [new("files.item", binding.FilesFolderId.ToString(), binding.FolderRevision, ResourceAccess.Read)]) is null, "Read access promoted to write.");
    Check((await fixture.Operations.Open(Guid.NewGuid(), default)).Error?.Code == "SiteNotFound", "Absent actual project returned success.");
}
static async Task PresentationFailure(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations); await view.InitializeAsync(null, default);
    var calls = 0; view.PropertyChanged += (_, _) => { if (++calls == 1) throw new InvalidOperationException("Induced listener failure."); };
    await view.DispatchAsync("Reload", null); Check(view.IsActionAvailable("NewProject") == true, "Listener failure permanently locked presentation."); var project = await CreateWebsite(view);
    var postCommitFailures = 0; var laterObserverCalls = 0;
    view.PropertyChanged += (_, _) => { if (Value(view, "Status") as string == "Saved.") { postCommitFailures++; throw new InvalidOperationException("Induced postcommit observer failure."); } };
    view.PropertyChanged += (_, _) => laterObserverCalls++;
    await AddPage(view);
    Check(postCommitFailures > 0 && laterObserverCalls > 0 && Value(view, "Status") as string == "Saved.", "Postcommit observer altered known success or suppressed later observers.");
    await view.DispatchAsync("Save", null); var saved = await fixture.Operations.Open(project.SiteId, default);
    Check(saved.IsSuccess && saved.Value is { } committed && committed.Revision == 2 && committed.Pages.Count == 1, "Presentation failure replayed actual owner write.");
    await view.DispatchAsync("Reload", null); Check(view.IsActionAvailable("NewPage") == true, "Postcommit observer locked later commands.");
}
static async Task CancellationTeardown(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); var actual = fixture.Operations;
    var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancellationCalls = 0;
    var bounded = actual with { SetText = async (site, revision, component, text, ct) =>
    {
        var result = await actual.SetText(site, revision, component, text, ct); if (!result.IsSuccess) return result;
        using var registration = ct.Register(() => { cancellationCalls++; throw new InvalidOperationException("Induced owner cancellation callback failure."); });
        committed.SetResult(); await Task.Delay(Timeout.Infinite, ct); return result;
    } };
    using var view = new SitesBrowserController(bounded); await view.InitializeAsync(null, default); var project = await CreateWebsite(view); await AddPage(view); await AddText(view, "Private source");
    await SelectText(view); await view.DispatchAsync("EditText", null); view.TrySetValue("Text", "Private in-flight edit");
    var inFlight = view.DispatchAsync("Save", null).AsTask(); await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    view.Dispose(); await inFlight.WaitAsync(TimeSpan.FromSeconds(10));
    Check(cancellationCalls == 1 && view.CurrentProject is null && view.CurrentPage is null && Value(view, "Projects") is IReadOnlyList<SiteProjectRow> rows && rows.Count == 0 && Value(view, "Text") as string == "" && Value(view, "Editing") is false && view.IsActionAvailable("NewProject") == false,
        "Throwing owner cancellation callback prevented private project/draft teardown.");
    var persisted = await actual.Open(project.SiteId, default);
    Check(persisted.IsSuccess && persisted.Value is { } saved && saved.Revision == 4 && saved.Components.Single().Properties["text"].GetString() == "Private in-flight edit", "Teardown lost or repeated real owner commit.");
    Console.WriteLine("INJECTED_OWNER_CANCEL_FAILURE actual owner commit retained; private teardown completed.");
}
static async Task IndependentProcesses(string root)
{
    Directory.CreateDirectory(root); await Child("seed", root); await Child("verify", root);
}
static async Task Child(string mode, string root)
{
    var executable = Environment.ProcessPath ?? throw new Exception("Executable path unavailable.");
    var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(executable) == "dotnet") info.ArgumentList.Add(typeof(Checks).Assembly.Location);
    info.ArgumentList.Add(mode); info.ArgumentList.Add(root);
    using var child = Process.Start(info) ?? throw new Exception("Independent process could not start.");
    var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try { await child.WaitForExitAsync(deadline.Token); } catch { child.Kill(entireProcessTree: true); throw; }
    var output = await stdout; var errors = await stderr; await File.WriteAllTextAsync(Path.Combine(root, mode + ".log"), output + errors);
    await File.WriteAllTextAsync(Path.Combine(root, mode + ".exit"), child.ExitCode.ToString()); Console.Write(output); Console.Write(errors);
    Check(child.ExitCode == 0, "Independent " + mode + " process failed.");
}
static async Task Seed(string root)
{
    var fixture = await NativeSitesFixture.CreateAsync(root); using var view = new SitesBrowserController(fixture.Operations);
    await view.InitializeAsync(null, default); await CreateWebsite(view); await AddPage(view); await AddText(view, "Original"); var project = await EditText(view, "Independent restart <oracle> & content");
    var binding = await fixture.BindingAsync(); var workspace = await fixture.Files.GetCurrentAsync(default) ?? throw new Exception("Actual Files workspace absent.");
    var renderer = new SiteDocumentRenderer(); var rendered = renderer.Render(project, project.Pages.Single().PageId, SiteRenderContext.PagePreview); Check(rendered.Diagnostics.Count == 0, "Seed source has render diagnostics.");
    var artifact = await new SiteArtifactAuthoringService(await fixture.ProjectsAsync(), Path.Combine(binding.RootDirectory, ".9to1-site-builds")).BuildAsync(project.SiteId, project.Revision, "restart-config");
    Check(await File.ReadAllTextAsync(Path.Combine(artifact.ArtifactReference, "index.html")) == rendered.Document, "Seed artifact renderer mismatch.");
    var oracle = new Oracle(workspace.Configuration.StoreId, workspace.Configuration.LocationId.Value, binding.ProfileId, binding.FilesFolderId, binding.FolderRevision, project,
        Hash(await File.ReadAllBytesAsync(Path.Combine(binding.RootDirectory, ".9to1-sites-index.json"))), rendered.Document, artifact);
    await File.WriteAllTextAsync(Path.Combine(root, "canonical-oracle.json"), JsonSerializer.Serialize(oracle));
}
static async Task Verify(string root)
{
    var oracle = JsonSerializer.Deserialize<Oracle>(await File.ReadAllTextAsync(Path.Combine(root, "canonical-oracle.json")))!;
    var fixture = await NativeSitesFixture.CreateAsync(root, initialize: false); var binding = await fixture.BindingAsync();
    var workspace = await fixture.Files.GetCurrentAsync(default) ?? throw new Exception("Restarted owner workspace absent.");
    Check(workspace.Configuration.StoreId == oracle.StoreID && workspace.Configuration.LocationId.Value == oracle.LocationID && binding.ProfileId == oracle.ProfileID
        && binding.FilesFolderId == oracle.FolderID && binding.FolderRevision == oracle.FolderRevision, "Independent restart changed original Files/profile/source identity.");
    using var view = new SitesBrowserController(fixture.Operations); await view.InitializeAsync(oracle.Project.SiteId, default); var reopened = view.CurrentProject!;
    Check(JsonSerializer.Serialize(reopened) == JsonSerializer.Serialize(oracle.Project), "Independent process changed complete canonical project/schema/revisions/content graph.");
    Check(Hash(await File.ReadAllBytesAsync(Path.Combine(binding.RootDirectory, ".9to1-sites-index.json"))) == oracle.IndexHash, "Read-only restart altered Sites index bytes.");
    var rendered = new SiteDocumentRenderer().Render(reopened, reopened.Pages.Single().PageId, SiteRenderContext.PagePreview);
    Check(rendered.Document == oracle.RenderedDocument && rendered.Diagnostics.Count == 0, "Restart rendering changed saved source.");
    Check((await new NativeSiteBuildPipeline(Path.Combine(root, "validation")).ValidateArtifactAsync(oracle.Artifact, default)).IsValid && !oracle.Artifact.SecretScanPassed, "Restart artifact identity/integrity or truthful secret gate failed.");
    Check(await File.ReadAllTextAsync(Path.Combine(oracle.Artifact.ArtifactReference, "index.html")) == rendered.Document, "Restart artifact differs from actual renderer.");
}
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
sealed record Oracle(Guid StoreID, Guid LocationID, string ProfileID, Guid FolderID, string FolderRevision, SiteProject Project, string IndexHash, string RenderedDocument, SiteBuildArtifact Artifact);
static class Checks
{
    public static int Count { get; private set; }
    public static void That(bool condition, string message) { Count++; if (!condition) throw new Exception(message); }
}
