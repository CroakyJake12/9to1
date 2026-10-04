using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
using HavenOS.Images;
using NineToOne.Web;
using NineToOne.Web.Media;
using NineToOne.Web.Picture;

// Actual feature/session/codec/native Bitmap controls. Controlled media is a fault boundary,
// never evidence of browser IndexedDB, permission, provider or native device acceptance.
if (args.Length < 1) throw new ArgumentException("An explicit task-owned output directory is required.");
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var sourcePath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "Fixtures", "Picture8x6.png");
var source = File.ReadAllBytes(sourcePath);
if (!string.Equals(Convert.ToHexString(SHA256.HashData(source)), "A17E304D0A333065202CF77C656D7BC86ABDD37237B407A0A5CB870D33870297", StringComparison.Ordinal))
    throw new InvalidDataException("The reviewed actual 8x6 alpha PNG fixture changed.");
using var markupResource = typeof(ControlledMedia).Assembly.GetManifestResourceStream("NineToOne.Picture.Admission.Tests.Picture.cui")
    ?? throw new InvalidDataException("The actual Picture CUI resource is unavailable.");
using var markupReader = new StreamReader(markupResource);
var markup = args.Length > 2 ? File.ReadAllText(args[2]) : markupReader.ReadToEnd();
var tests = new List<(string Name, Func<Task> Run)>(); var results = new List<object>(); var receipts = new List<object>();
var assertions = 0; var failed = false; var passedCount = 0; var failedCount = 0; var executedCount = 0; var timedOut = false; string? prerequisite = null;
void Check(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
string Canonical(PictureBrowserSession s) => Encoding.UTF8.GetString(s.Document!.Serialize());
HomeFeatureNavigationRequest Request(string? id = null) => new("app.picture", EntityType: id is null ? null : "PictureDocument", EntityId: id);
void Refuses(Action action) { var refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Check(refused, "Stale native candidate is refused."); }
byte[] Pixels(Bitmap bitmap)
{
    using var destination = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
    using var frame = destination.Lock(); bitmap.CopyPixels(frame);
    var data = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
    for (var y = 0; y < bitmap.PixelSize.Height; y++) Marshal.Copy(frame.Address + y * frame.RowBytes, data, y * bitmap.PixelSize.Width * 4, bitmap.PixelSize.Width * 4);
    return data;
}
Loaded Load(PictureBrowserFeature feature, HomeFeatureViewState state)
{
    var surface = feature.Render(state); var loader = new CuiControlLoader(surface.ControlRegistry!);
    try
    {
        loader.SetBindingContext(surface.Bindings); loader.SetActionDispatcher(surface.Actions);
        var (root, diagnostics) = loader.TryLoad(surface.Document);
        Check(root is not null && !diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error), "Actual Picture CUI loads with its actual raster renderer.");
        loader.WireBindings(root!); var image = root!.GetLogicalDescendants().OfType<Image>().Single();
        return new(surface, loader, image);
    }
    catch { loader.Dispose(); surface.Lifetime?.Dispose(); surface.Admission?.Reject(); throw; }
}
async Task<Loaded> Open(PictureBrowserFeature feature, string? id = null, bool accept = true)
{
    var result = await feature.OpenAsync(Request(id)); Check(result.Succeeded && result.ViewState is not null, "Real feature prepares supported canonical route.");
    var loaded = Load(feature, result.ViewState!); if (accept) loaded.Surface.Admission?.Accept(); return loaded;
}
async Task<Fixture> New()
{
    var media = new ControlledMedia(source); string a, b;
    using (var s = new PictureBrowserSession(media)) { Check(await s.ImportAsync(), "Owner native source import seed A."); a = s.Document!.DocumentId.ToString(); }
    using (var s = new PictureBrowserSession(media)) { Check(await s.ImportAsync() && await s.EditAsync(d => d.Rotate()), "Owner native import/rotation seed B."); b = s.Document!.DocumentId.ToString(); }
    var feature = new PictureBrowserFeature(media, markup); var active = await Open(feature, a);
    return new(feature, media, active, a, b);
}
void Receipt(string name, Fixture f)
{
    receipts.Add(new { name, f.A, f.B, f.Media.ReleaseCalls, f.Media.DirtyWrites, f.Media.CommitCalls,
        saved = f.Media.Saved.ToDictionary(x => x.Key, x => x.Value.GetRawText()),
        activeDocument = f.Active.Session.Document is null ? null : Canonical(f.Active.Session),
        activeDirty = f.Active.Session.IsDirty, f.Feature.HasUnsavedChanges });
}

tests.Add(("independent-target-prepare-and-reject-preserve-active-bitmap-and-canonical", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); var bitmap = (Bitmap)f.Active.Image.Source!;
    var pixels = Pixels(bitmap); var releases = f.Media.ReleaseCalls; var dirtyWrites = f.Media.DirtyWrites;
    using var candidate = await Open(f.Feature, f.B, accept: false);
    Check(Canonical(f.Active.Session) == json && ReferenceEquals(f.Active.Image.Source, bitmap), "Preparing another document never mutates the active canonical session or tears down its real Bitmap.");
    Check(!ReferenceEquals(candidate.Session, f.Active.Session) && candidate.Session.Document!.DocumentId.ToString() == f.B, "Candidate is a separate real canonical owner session.");
    candidate.Reject(); Check(Canonical(f.Active.Session) == json && Pixels(bitmap).SequenceEqual(pixels), "Rejected candidate preserves usable exact native pixels and canonical bytes.");
    Check(f.Media.ReleaseCalls == releases && f.Media.DirtyWrites == dirtyWrites, "Unadmitted cleanup never releases shared device media or changes the current dirty marker."); Receipt("reject", f);
}));
tests.Add(("invalid-route-refused-before-owner-or-device-effects", async () =>
{
    using var f = await New(); var calls = f.Media.Calls; var json = Canonical(f.Active.Session);
    foreach (var request in new[] { Request(f.B) with { DeepLink = "" }, Request(f.B) with { Action = "unsupported" }, Request("not-a-guid") })
    { var result = await f.Feature.OpenAsync(request); Check(!result.Succeeded && result.Code == "InvalidArgument" && ReferenceEquals(result.Request, request), "Unsupported original route is rejected with original request."); }
    Check(f.Media.Calls == calls && Canonical(f.Active.Session) == json && f.Active.Image.Source is Bitmap, "No side effects before route validation."); Receipt("invalid", f);
}));
tests.Add(("controlled-target-read-failure-preserves-active-owner", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); var pixels = Pixels((Bitmap)f.Active.Image.Source!); var releases = f.Media.ReleaseCalls;
    f.Media.FailRead = true; var result = await f.Feature.OpenAsync(Request(f.B));
    Check(!result.Succeeded && Canonical(f.Active.Session) == json && Pixels((Bitmap)f.Active.Image.Source!).SequenceEqual(pixels), "Target read failure retains active owner and real preview.");
    Check(f.Media.ReleaseCalls == releases, "Failed temporary session cannot release actual active device media."); Receipt("read-refusal", f);
}));
tests.Add(("typed-input-before-navigation-and-close-is-retained", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); var reads = f.Media.OpenCalls;
    Check(f.Active.Session.TrySetValue("CropX", "1"), "Real two-way binding accepts input.");
    var prepared = await f.Feature.PrepareToCloseAsync(); var route = await f.Feature.OpenAsync(Request(f.B));
    Check(!prepared.Succeeded && prepared.Code == "UnsavedInput" && !route.Succeeded && route.Code == "UnsavedInput" && f.Feature.HasUnsavedChanges, "Navigation and close cannot discard unapplied typed values.");
    Check(Canonical(f.Active.Session) == json && f.Media.OpenCalls == reads && f.Active.Session.TryGetValue("CropX", out var x) && Equals(x, "1"), "Typed input and prior canonical package stay unchanged.");
    Check(f.Active.Session.TrySetValue("CropX", "0") && (await f.Feature.PrepareToCloseAsync()).Succeeded, "Restoring exact prior input clears the typed draft without inventing an edit."); Receipt("input", f);
}));
tests.Add(("typed-input-after-render-fences-native-admission", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); using var candidate = await Open(f.Feature, f.B, false);
    f.Active.Session.TrySetValue("CropX", "1"); Check(candidate.Surface.Admission is not null, "Actual owner supplies the shared admission contract.");
    Refuses(() => candidate.Surface.Admission!.Accept()); candidate.Reject();
    Check(Canonical(f.Active.Session) == json && f.Feature.HasUnsavedChanges && f.Active.Session.TryGetValue("CropX", out var x) && Equals(x, "1"), "Late input is fenced and never discarded."); Receipt("late-input", f);
}));
tests.Add(("acknowledged-owner-edit-after-render-fences-target", async () =>
{
    using var f = await New(); using var candidate = await Open(f.Feature, f.B, false);
    Check(await f.Active.Session.EditAsync(d => d.Rotate()), "Actual canonical owner edit acknowledges."); var edited = Canonical(f.Active.Session);
    Check(candidate.Surface.Admission is not null, "Actual candidate has admission."); Refuses(() => candidate.Surface.Admission!.Accept()); candidate.Reject();
    Check(Canonical(f.Active.Session) == edited && f.Active.Session.Document!.Revision == 1 && !f.Active.Session.IsDirty && f.Media.Saved[f.A].GetProperty("revision").GetInt64() == 1, "Late real owner revision and canonical save preserved."); Receipt("late-edit", f);
}));
tests.Add(("forged-view-state-and-superseded-offer-have-no-authority", async () =>
{
    using var f = await New(); var result = await f.Feature.OpenAsync(Request(f.B)); Check(result.Succeeded, "Target prepared.");
    Refuses(() => f.Feature.Render(result.ViewState! with { })); using var candidate = Load(f.Feature, result.ViewState!);
    using var sameOwner = await Open(f.Feature); Check(candidate.Surface.Admission is not null, "Real old offer exists."); Refuses(() => candidate.Surface.Admission!.Accept()); candidate.Reject();
    Check(sameOwner.Session.Document!.DocumentId.ToString() == f.A, "App-root keeps actual current canonical document, not an empty substitute."); Receipt("state-fence", f);
}));
tests.Add(("cancelled-late-read-and-pending-close-preserve-owner", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); var releases = f.Media.ReleaseCalls; using var cancellation = new CancellationTokenSource(); var hold = f.Media.HoldRead();
    var pending = f.Feature.OpenAsync(Request(f.B), cancellation.Token);
    try
    {
        await Task.WhenAny(hold.Entered.Task, pending); Check(hold.Entered.Task.IsCompleted, "Actual read reached the controlled late-completion boundary.");
        var close = await f.Feature.PrepareToCloseAsync(); Check(!close.Succeeded && close.Code == "OperationBusy" && f.Feature.HasUnsavedChanges, "Pending native candidate preparation prevents teardown.");
        cancellation.Cancel(); hold.Continue.TrySetResult(); var cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && Canonical(f.Active.Session) == json && f.Media.ReleaseCalls == releases && f.Active.Image.Source is Bitmap, "Late ignored-token read cannot admit or tear down old view."); Receipt("cancelled-read", f);
    }
    finally
    {
        cancellation.Cancel(); hold.Continue.TrySetResult();
        try { await pending; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}));
tests.Add(("disposal-during-read-fences-late-candidate-without-extra-release", async () =>
{
    using var f = await New(); var releases = f.Media.ReleaseCalls; var hold = f.Media.HoldRead(); var pending = f.Feature.OpenAsync(Request(f.B));
    try
    {
        await Task.WhenAny(hold.Entered.Task, pending); Check(hold.Entered.Task.IsCompleted, "Actual read reached the controlled disposal boundary.");
        f.Feature.Dispose(); hold.Continue.TrySetResult(); var result = await pending;
        Check(!result.Succeeded && f.Active.Session.Document is null && f.Active.Image.Source is null && f.Media.ReleaseCalls == releases + 1, "Disposed candidate never reexposes model or releases media after the one active terminal cleanup.");
    }
    finally { hold.Continue.TrySetResult(); await pending; }
}));
tests.Add(("candidate-preview-does-not-write-native-cache-before-accept", async () =>
{
    using var f = await New(); var canonical = PictureDocument.Deserialize(Encoding.UTF8.GetBytes(f.Media.Saved[f.B].GetProperty("documentJson").GetString()!));
    var bytes = File.ReadAllBytes(canonical.SourcePath!); File.Delete(canonical.SourcePath!);
    using var candidate = await Open(f.Feature, f.B, false);
    Check(!File.Exists(canonical.SourcePath) && candidate.Image.Source is Bitmap bitmap && bitmap.PixelSize == new PixelSize(6, 8), "Actual unchanged owner renders retained stream with no preadmission source cache rewrite.");
    Check(candidate.Surface.Admission is not null, "Real candidate admission."); candidate.Surface.Admission!.Accept();
    Check(File.ReadAllBytes(canonical.SourcePath!).SequenceEqual(bytes) && candidate.Session.Document!.SourcePath == canonical.SourcePath && candidate.Session.Document.Revision == 1 && candidate.Session.Document.FileId is null, "Acceptance materializes original cache without changing canonical source or hosted identity/revision."); Receipt("cache-admission", f);
}));
tests.Add(("changed-native-cache-refuses-admission-without-overwrite", async () =>
{
    using var f = await New(); var json = Canonical(f.Active.Session); using var candidate = await Open(f.Feature, f.B, false);
    var path = candidate.Session.Document!.SourcePath!; var bytes = File.ReadAllBytes(path); var changed = bytes.ToArray(); changed[^1] ^= 1; File.WriteAllBytes(path, changed);
    try
    {
        var refused = false; try { candidate.Surface.Admission!.Accept(); } catch (PictureBrowserException e) { refused = e.Code == "SourceChanged"; }
        Check(refused && File.ReadAllBytes(path).SequenceEqual(changed), "Refused admission cannot overwrite changed canonical source bytes."); candidate.Reject();
        Check(Canonical(f.Active.Session) == json && f.Active.Image.Source is Bitmap, "Old valid canonical owner and Bitmap remain intact."); Receipt("source-refusal", f);
    }
    finally { File.WriteAllBytes(path, bytes); }
}));
tests.Add(("old-retirement-cannot-clear-new-real-canonical-dirty-edit", async () =>
{
    using var f = await New(); using var candidate = await Open(f.Feature, f.B, false); var releases = f.Media.ReleaseCalls;
    Check(candidate.Surface.Admission is not null, "Actual admission."); candidate.Surface.Admission!.Accept();
    var hold = f.Media.HoldCommit(); var edit = candidate.Session.EditAsync(d => d.Rotate());
    try
    {
        await Task.WhenAny(hold.Entered.Task, edit); Check(hold.Entered.Task.IsCompleted, "Actual edit reached the controlled commit boundary."); await Task.Yield();
        Check(candidate.Session.IsDirty && f.Media.Dirty && f.Media.ReleaseCalls == releases && candidate.Session.Document!.Revision == 2, "Retired old owner cannot clear current actual pending canonical edit or global warning.");
        hold.Continue.TrySetResult(); Check(await edit && !candidate.Session.IsDirty && (await f.Feature.PrepareToCloseAsync()).Succeeded, "Only new canonical revision acknowledges and readiness is real.");
    }
    finally { hold.Continue.TrySetResult(); await edit; }
}));
tests.Add(("actual-raster-crop-history-save-close-reopen-and-pixel-export", async () =>
{
    var media = new ControlledMedia(source); using var feature = new PictureBrowserFeature(media, markup); using var first = await Open(feature);
    Check(await first.Session.ImportAsync(), "Actual original PNG/native owner create."); var original = first.Session.Document!; var sourcePath = original.SourcePath!; var originalBytes = File.ReadAllBytes(sourcePath);
    first.Session.TrySetValue("CropX", "1"); first.Session.TrySetValue("CropY", "2"); first.Session.TrySetValue("CropWidth", "5"); first.Session.TrySetValue("CropHeight", "3");
    await first.Session.DispatchAsync("Crop", null); Check(first.Session.Document!.Revision == 1 && first.Session.Document.Operations.Single() == new CropOperation(1, 2, 5, 3) && !first.Session.IsDirty && !feature.HasUnsavedChanges, "Canonical crop consumes real input, preserves identity and acknowledges once.");
    Check(await first.Session.UndoAsync() && first.Session.Document!.Revision == 2 && first.Session.Document.Operations.Count == 0, "Undo restores actual codec graph with owner-compatible current+1 revision.");
    Check(await first.Session.UndoAsync(true) && first.Session.Document!.Revision == 3 && first.Session.Document.Operations.Single() == new CropOperation(1, 2, 5, 3), "Redo restores canonical typed operation at revision3.");
    var saved = Canonical(first.Session); var id = original.DocumentId.ToString(); var commits = media.CommitCalls;
    Check(await first.Session.SaveAsync() && await first.Session.CloseAsync() && media.CommitCalls == commits, "Clean save/close cannot replay a revision.");
    using var reopened = await Open(feature, id); Check(Canonical(reopened.Session) == saved && reopened.Session.Document!.SourcePath == sourcePath && File.ReadAllBytes(sourcePath).SequenceEqual(originalBytes), "Actual feature reopen preserves canonical ID/revision/operations/original bytes.");
    var before = Canonical(reopened.Session); Check(await reopened.Session.ExportAsync(PictureMetadataExportMode.RemoveAll), "Owner PNG privacy export into actual controlled download bytes.");
    var downloaded = media.Downloads.Single(); using var rendered = new Bitmap(new MemoryStream(downloaded)); using var originalBitmap = new Bitmap(new MemoryStream(source));
    var inputPixels = Pixels(originalBitmap); var expected = new byte[5 * 3 * 4];
    for (var row = 0; row < 3; row++) Array.Copy(inputPixels, ((row + 2) * 8 + 1) * 4, expected, row * 5 * 4, 5 * 4);
    Check(rendered.PixelSize == new PixelSize(5, 3) && Pixels(rendered).SequenceEqual(expected), "Actual exported PNG equals strict independent crop RGBA bytes including alpha.");
    var exportPath = Path.Combine(output, "actual-crop-remove-all.png"); File.WriteAllBytes(exportPath, downloaded); var metadata = RawPngMetadata.Read(exportPath);
    Check(metadata.XmpPackets == 0 && !metadata.Chunks.Any(x => x is "tEXt" or "zTXt" or "iTXt" or "eXIf"), "Actual export has no removable text/XMP/EXIF chunks, using maintained strict CRC validator.");
    Check(Canonical(reopened.Session) == before && reopened.Session.Document.DocumentId == original.DocumentId && reopened.Session.Document.FileId is null && media.Saved[id].GetProperty("revision").GetInt64() == 3 && File.ReadAllBytes(sourcePath).SequenceEqual(originalBytes), "Export leaves canonical model/source and acknowledged revision intact; no invented hosted Files identity.");
    receipts.Add(new { name = "actual-raster", documentId = id, canonical = before, sourceSHA256 = Convert.ToHexString(SHA256.HashData(originalBytes)), rgbaSHA256 = Convert.ToHexString(SHA256.HashData(expected)), media.CommitCalls });
}));

tests.Add(("candidate-publication-cannot-write-detached-previous-input", async () =>
{
    using var f = await New(); var before = Canonical(f.Active.Session); using var candidate = await Open(f.Feature, f.B, false);
    var called = false; var oldAcceptedInput = false; var currentAcceptedInput = false;
    candidate.Session.PropertyChanged += (_, _) =>
    {
        if (called) return; called = true;
        oldAcceptedInput = f.Active.Session.TrySetValue("CropX", "1");
        currentAcceptedInput = candidate.Session.TrySetValue("CropX", "1");
    };
    Check(candidate.Surface.Admission is not null, "Real candidate admission contract."); candidate.Surface.Admission!.Accept();
    Check(called && !oldAcceptedInput && !f.Active.Session.HasPendingInput && Canonical(f.Active.Session) == before,
        "Real synchronous candidate observer cannot write a soon-retired previous owner.");
    Check(currentAcceptedInput && candidate.Session.HasPendingInput && f.Feature.HasUnsavedChanges
        && candidate.Session.Document!.DocumentId.ToString() == f.B
        && candidate.Session.TryGetValue("CropX", out var value) && Equals(value, "1"),
        "Observer writes target the coherent current canonical owner and remain retained as pending input.");
    var close = await f.Feature.PrepareToCloseAsync(); Check(!close.Succeeded && close.Code == "UnsavedInput",
        "Reentrant current input cannot be silently discarded during close.");
}));
tests.Add(("candidate-observer-reentrant-navigation-waits-coherent-retirement", async () =>
{
    using var f = await New(); using var candidate = await Open(f.Feature, f.B, false); var releases = f.Media.ReleaseCalls;
    Task<HomeFeatureNavigationResult>? reentered = null; var called = false;
    candidate.Session.PropertyChanged += (_, _) =>
    {
        if (called) return; called = true; reentered = f.Feature.OpenAsync(Request(f.A));
    };
    Check(candidate.Surface.Admission is not null, "Real candidate admission contract."); candidate.Surface.Admission!.Accept();
    Check(called && reentered is not null && !reentered.IsCompleted
        && candidate.Session.Document!.DocumentId.ToString() == f.B,
        "Reentrant route sees published candidate and waits actual old-session retirement; it cannot reject an unpublished candidate.");
    var result = await reentered!; Check(result.Succeeded && result.ViewState is not null, "Reentered actual target prepares after retirement.");
    using var returned = Load(f.Feature, result.ViewState!); returned.Surface.Admission!.Accept();
    var ready = await f.Feature.PrepareToCloseAsync();
    Check(returned.Session.Document!.DocumentId.ToString() == f.A && f.Media.ReleaseCalls == releases
        && ready.Succeeded && !f.Feature.HasUnsavedChanges, "Both real canonical transfers preserve coherent source ownership and do not release newer device media.");
}));

var exit = 1;
try
{
    AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    AvaloniaSynchronizationContext.InstallIfNeeded(); using var stop = new CancellationTokenSource();
    Dispatcher.UIThread.Post(async () =>
    {
        try
        {
            foreach (var test in tests)
            {
                Console.WriteLine("START " + test.Name); var before = assertions;
                ++executedCount;
                try { await test.Run().WaitAsync(TimeSpan.FromSeconds(30)); ++passedCount; results.Add(new { test.Name, state = "PASS", assertions = assertions - before }); Console.WriteLine("PASS " + test.Name); }
                catch (Exception e)
                {
                    failed = true; ++failedCount; results.Add(new { test.Name, state = "FAIL", assertions = assertions - before, failure = e.ToString() }); Console.WriteLine("FAIL " + test.Name);
                    if (e is TimeoutException) { timedOut = true; break; } // WaitAsync cannot cancel the original operation; outer owned-family bound must retire it.
                }
            }
            exit = failed ? 1 : 0;
        }
        finally { stop.Cancel(); }
    });
    Dispatcher.UIThread.MainLoop(stop.Token);
}
catch (Exception e) { failed = true; prerequisite = e.ToString(); }
finally
{
    foreach (var test in tests.Skip(executedCount)) results.Add(new { test.Name, state = "NOT_RUN", reason = timedOut ? "Prior operation timed out; original task not cancelled by WaitAsync" : prerequisite ?? "Native prerequisite did not complete" });
    File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { scope = "Controlled native feature/CUI/codec/raster admission and fault boundary only; no browser IDB/provider/permission/Files/complete Picture acceptance", discovered = tests.Count, executed = executedCount, passed = passedCount, failed = failedCount, notRun = tests.Count - executedCount, timedOut, prerequisite, assertions, results, receipts, exitCode = exit }, new JsonSerializerOptions { WriteIndented = true }));
}
return exit;

sealed class Loaded(BrowserCuiSurface surface, CuiControlLoader loader, Image image) : IDisposable
{
    public BrowserCuiSurface Surface { get; } = surface;
    public PictureBrowserSession Session => (PictureBrowserSession)Surface.Bindings;
    public Image Image { get; } = image;
    public void Reject() { try { Dispose(); } finally { Surface.Admission?.Reject(); } }
    public void Dispose() { try { loader.Dispose(); } finally { Surface.Lifetime?.Dispose(); } }
}
sealed class Fixture(PictureBrowserFeature feature, ControlledMedia media, Loaded active, string a, string b) : IDisposable
{
    public PictureBrowserFeature Feature { get; } = feature; public ControlledMedia Media { get; } = media;
    public Loaded Active { get; } = active; public string A { get; } = a; public string B { get; } = b;
    public void Dispose() { try { Active.Dispose(); } finally { Feature.Dispose(); } }
}
sealed class Hold
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
sealed class ControlledMedia(byte[] source) : IPictureBrowserMedia
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Dictionary<string, JsonElement> Saved { get; } = new(); public List<byte[]> Downloads { get; } = [];
    public bool FailRead, Dirty; public int Calls, OpenCalls, CommitCalls, ReleaseCalls, DirtyWrites;
    private Hold? _read, _commit;
    public Hold HoldRead() => _read = new(); public Hold HoldCommit() => _commit = new();
    private static string Ok(object? value) => JsonSerializer.Serialize(new { ok = true, value }, Json);
    private static string Refused(string code) => JsonSerializer.Serialize(new { ok = false, code, message = "Controlled native boundary " + code }, Json);
    public async Task<string> InvokeAsync(string action, string arguments, CancellationToken cancellationToken = default)
    {
        Calls++; using var parsed = JsonDocument.Parse(arguments); var args = parsed.RootElement;
        switch (action)
        {
            case "pick": return Ok(new { name = "Public real owner 8x6 alpha PNG", base64 = Convert.ToBase64String(source) });
            case "list": return Ok(Saved.Values.Select(x => new { documentId = x.GetProperty("documentId").GetString(), name = x.GetProperty("name").GetString(), revision = x.GetProperty("revision").GetInt64() }).ToArray());
            case "open":
                OpenCalls++; if (_read is { } read) { _read = null; read.Entered.TrySetResult(); await read.Continue.Task; } // Deliberately late ignored-token controlled completion.
                return FailRead ? Refused("StorageFailed") : Ok(Saved[args.GetProperty("documentId").GetString()!]);
            case "commit":
                CommitCalls++; var bundle = args.GetProperty("bundle").Clone(); var id = bundle.GetProperty("documentId").GetString()!;
                if (_commit is { } commit) { _commit = null; commit.Entered.TrySetResult(); await commit.Continue.Task; }
                if ((Saved.TryGetValue(id, out var prior) ? prior.GetProperty("revision").GetInt64() : -1) != args.GetProperty("expectedRevision").GetInt64()) return Refused("RevisionConflict");
                Saved[id] = bundle; return Ok(new { revision = bundle.GetProperty("revision").GetInt64() });
            case "download": Downloads.Add(Convert.FromBase64String(args.GetProperty("base64").GetString()!)); return Ok(new { initiated = true });
            default: throw new InvalidOperationException("Unknown controlled boundary action.");
        }
    }
    public void SetDirty(bool dirty) { Dirty = dirty; DirtyWrites++; }
    public void Release() { ReleaseCalls++; Dirty = false; }
}
