using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class WriteSaveSnapshotTests
{
    [AvaloniaFact]
    public async Task Actual_physical_save_retains_captured_content_and_native_later_edits_remain_dirty_until_their_own_save()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var initial = NotesDocument.Create("Initial document"); await actual.SaveAsync(initial, "Seed actual physical document", token);
            var held = new HeldRepository(actual);
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, held, new NotesImportExportService(new NotesDocumentValidator(), diagnostics), initialDocumentId: initial.Id);
            await page.InitializeAsync(token);
            page.Route.TitleInput.Text = "Captured title";
            var live = page.Document!; var blockID = live.Sections[0].Pages[0].Blocks[0].Id;
            var save = page.SaveAsync("Actual first snapshot", token);
            try
            {
                await held.Entered.Task.WaitAsync(token);
                page.Route.TitleInput.Text = "Later native edit";
                Assert.Equal("Later native edit", page.Document!.Title);
                held.Release.TrySetResult(); Assert.True(await save);
            }
            finally { held.Release.TrySetResult(); }
            var first = await actual.LoadAsync(initial.Id, token);
            Assert.Equal("Captured title", first!.Title); Assert.Equal(blockID, first.Sections[0].Pages[0].Blocks[0].Id);
            Assert.Equal("Later native edit", page.Document!.Title); Assert.True(page.IsDirty);
            Assert.True(await page.SaveAsync("Actual later edit", token));
            var final = await actual.LoadAsync(initial.Id, token);
            Assert.Equal(initial.Id, final!.Id); Assert.Equal("Later native edit", final.Title);
            Assert.Equal(first.Version + 1, final.Version); Assert.False(page.IsDirty);
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acknowledged_physical_save_is_preserved_after_close_or_a_later_library_refresh_fault(bool close)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var initial = NotesDocument.Create("Actual saved snapshot"); await actual.SaveAsync(initial, "Seed", token);
            var held = new HeldRepository(actual) { FaultRefreshAfterSave = !close };
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, held, new NotesImportExportService(new NotesDocumentValidator(), diagnostics), initialDocumentId: initial.Id);
            await page.InitializeAsync(token); page.Route.TitleInput.Text = "Retained acknowledged title";
            var save = page.SaveAsync("Actual acknowledged snapshot", token);
            try
            {
                await held.Entered.Task.WaitAsync(token);
                if (close) page.Dispose();
                var statusAtClose = page.Route.StatusText.Content;
                held.Release.TrySetResult(); Assert.True(await save);
                if (close) Assert.Equal(statusAtClose, page.Route.StatusText.Content);
            }
            finally { held.Release.TrySetResult(); }
            var stored = await actual.LoadAsync(initial.Id, token);
            Assert.Equal("Retained acknowledged title", stored!.Title); Assert.Equal(initial.Version + 1, stored.Version);
            if (!close) Assert.False(page.IsDirty);
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [AvaloniaFact]
    public async Task Actual_create_after_save_cannot_discard_a_newer_native_edit_while_the_earlier_snapshot_commits()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var initial = NotesDocument.Create("Original canonical identity"); await actual.SaveAsync(initial, "Seed", token);
            var held = new HeldRepository(actual);
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, held, new NotesImportExportService(new NotesDocumentValidator(), diagnostics), initialDocumentId: initial.Id);
            await page.InitializeAsync(token); page.Route.TitleInput.Text = "Earlier captured edit";
            var creation = page.CreateDocumentAsync(token); // Same task awaited by actual native New command.
            try
            {
                await held.Entered.Task.WaitAsync(token);
                page.Route.TitleInput.Text = "Newer pending edit";
                held.Release.TrySetResult(); await creation;
                Assert.Equal(initial.Id, page.Document!.Id); Assert.Equal("Newer pending edit", page.Document.Title);
                Assert.True(page.IsDirty); Assert.Single(await actual.ListAsync(token));
                Assert.Equal("Earlier captured edit", (await actual.LoadAsync(initial.Id, token))!.Title);
            }
            finally { held.Release.TrySetResult(); }
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [AvaloniaFact]
    public async Task Actual_independent_editor_revision_conflict_preserves_foreign_commit_and_original_unsaved_native_edit()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var initial = NotesDocument.Create("Original revision"); await actual.SaveAsync(initial, "Seed", token);
            var held = new HeldRepository(actual);
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, held, new NotesImportExportService(new NotesDocumentValidator(), diagnostics), initialDocumentId: initial.Id);
            await page.InitializeAsync(token); page.Route.TitleInput.Text = "Original native unsaved edit";
            var save = page.SaveAsync("Held stale save", token);
            try
            {
                await held.Entered.Task.WaitAsync(token);
                var independent = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
                var changed = await independent.LoadAsync(initial.Id, token) ?? throw new InvalidOperationException("Actual document required.");
                changed.Title = "Independent actual editor commit";
                var committed = await independent.SaveAsync(changed, "Independent current CAS", token);
                var bytes = await File.ReadAllBytesAsync(committed.CurrentPath, token);
                held.Release.TrySetResult(); Assert.False(await save);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(committed.CurrentPath, token));
                Assert.Equal("Original native unsaved edit", page.Document!.Title); Assert.True(page.IsDirty);
                Assert.Equal("Independent actual editor commit", (await independent.LoadAsync(initial.Id, token))!.Title);
            }
            finally { held.Release.TrySetResult(); }
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [AvaloniaFact]
    public async Task Actual_native_package_import_creates_new_local_identity_preserving_structured_content_and_original_revision()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var source = NotesDocument.Create("Original package document");
            var original = await actual.SaveAsync(source, "Actual original", token);
            var originalBytes = await File.ReadAllBytesAsync(original.CurrentPath, token);
            var packages = new WriteNativeDocumentPackageStore();
            var packagePath = Path.Combine(paths.DataDirectory, "import.9to1w");
            Assert.True((await packages.SaveAsync(source, packagePath, token)).IsSuccess);
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, actual, new NotesImportExportService(new NotesDocumentValidator(), diagnostics),
                initialDocumentId: source.Id, nativePackageStore: packages);
            await page.InitializeAsync(token);
            Assert.True(await page.ImportFromPathAsync(packagePath, token));
            Assert.NotEqual(source.Id, page.Document!.Id); Assert.Equal(1, page.Document.Version);
            Assert.Equal(source.Sections[0].Pages[0].Blocks[0].Id, page.Document.Sections[0].Pages[0].Blocks[0].Id);
            Assert.Equal(source.Title, page.Document.Title); Assert.Equal(2, (await actual.ListAsync(token)).Count);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(original.CurrentPath, token));
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_current_commit_retains_version_and_native_success_after_ancillary_diagnostics_return_loss_or_cancel(bool cancel)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = lifetime.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var fault = new LostDiagnosticsReturn(diagnostics, lifetime, cancel);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), fault);
            var source = NotesDocument.Create("Original actual current"); await actual.SaveAsync(source, "Seed", token);
            using var bus = new HavenEventBus();
            using var page = new WritePage(bus, actual, new NotesImportExportService(new NotesDocumentValidator(), diagnostics),
                initialDocumentId: source.Id);
            await page.InitializeAsync(token); page.Route.TitleInput.Text = "Committed native edit";
            fault.Armed = true;
            Assert.True(await page.SaveAsync("Actual known current", token));
            Assert.Equal(2, page.Document!.Version); Assert.False(page.IsDirty);
            var reopened = await new NotesRepository(paths, new NotesDocumentValidator(), diagnostics).LoadAsync(source.Id, CancellationToken.None);
            Assert.Equal(2, reopened!.Version); Assert.Equal("Committed native edit", reopened.Title);
            Assert.Contains("diagnostics", (page.Route.StatusText.Content?.ToString() ?? string.Empty).ToLowerInvariant());
            var versions = await actual.GetVersionsAsync(source.Id, CancellationToken.None);
            Assert.Contains(versions, version => version.Version == 2);
            Assert.All(versions, version => Assert.True(version.Version <= 2));
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    private sealed class LostDiagnosticsReturn(IProductionDiagnostics actual, CancellationTokenSource lifetime, bool cancel) : IProductionDiagnostics
    {
        public bool Armed;
        public async ValueTask WriteAsync(ReliabilitySeverity severity, string component, string eventName, string message,
            IReadOnlyDictionary<string, string>? data = null, string? correlationId = null, CancellationToken cancellationToken = default)
        {
            await actual.WriteAsync(severity, component, eventName, message, data, correlationId, cancellationToken);
            if (!Armed || eventName != "document-saved") return;
            Armed = false;
            if (cancel) { lifetime.Cancel(); throw new OperationCanceledException(lifetime.Token); }
            throw new IOException("Actual local diagnostics returned after writing; acknowledgement lost.");
        }
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken token) => actual.ReadRecentAsync(limit, token);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class HeldRepository(INotesRepository actual) : INotesRepository
    {
        private bool _held;
        private bool _faultList;
        public bool FaultRefreshAfterSave { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        {
            if (!_held) { _held = true; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            var result = await actual.SaveAsync(document, reason, token);
            _faultList = FaultRefreshAfterSave;
            return result;
        }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token)
        {
            if (_faultList) { _faultList = false; throw new IOException("Actual save acknowledged; library read fails afterward."); }
            return actual.ListAsync(token);
        }
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => actual.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => actual.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => actual.LoadVersionAsync(id, version, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => actual.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => actual.SearchAsync(query, token);
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-write-save-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
