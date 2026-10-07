using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI.Components;

namespace HavenOS.Apps.Write.Tests;

public sealed class WriteAppWorkflowTests
{
    [AvaloniaFact]
    public async Task Close_persists_real_local_document_and_a_new_host_reopens_its_identity()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Before close");
        await services.Repository.SaveAsync(document, "Seed original identity", default);

        await using (var host = WriteAppHost.Create(services.Repository, services.Formats, document.Id))
        {
            await host.InitializeAsync();
            Title(host).Text = "Saved by standalone Write";
            Assert.True(host.Page.IsDirty);
            Assert.True(await host.TrySaveBeforeCloseAsync());
            Assert.False(host.Page.IsDirty);
        }

        var reopenedServices = new DocumentServices(paths);
        await using var reopened = WriteAppHost.Create(reopenedServices.Repository, reopenedServices.Formats, document.Id);
        await reopened.InitializeAsync();
        Assert.Equal(document.Id, reopened.Document!.Id);
        Assert.Equal("Saved by standalone Write", reopened.Document.Title);
        Assert.Equal(2, reopened.Document.Version);
    }

    [AvaloniaFact]
    public async Task Storage_refusal_preserves_draft_and_retry_saves_before_close()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Stored title");
        await services.Repository.SaveAsync(document, "Seed original identity", default);
        var repository = new ControlledNotesRepository(services.Repository);
        await using var host = WriteAppHost.Create(repository, services.Formats, document.Id);
        await host.InitializeAsync();
        Title(host).Text = "Keep this draft";
        repository.FailSaves = true;

        Assert.False(await host.TrySaveBeforeCloseAsync());
        Assert.True(host.Page.IsDirty);
        Assert.Equal(document.Id, host.Document!.Id);
        Assert.Equal("Keep this draft", host.Document.Title);
        Assert.Equal("Stored title", (await services.Repository.LoadAsync(document.Id, default))!.Title);

        repository.FailSaves = false;
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.Equal("Keep this draft", (await services.Repository.LoadAsync(document.Id, default))!.Title);
    }

    [AvaloniaFact]
    public async Task Edit_during_actual_save_remains_dirty_and_is_saved_by_later_close()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Stored title");
        await services.Repository.SaveAsync(document, "Seed original identity", default);
        var repository = new ControlledNotesRepository(services.Repository);
        await using var host = WriteAppHost.Create(repository, services.Formats, document.Id);
        await host.InitializeAsync();
        Title(host).Text = "Submitted title";
        repository.DelayNextSave();
        var save = host.Page.SaveAsync("Save submitted title");
        await repository.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Title(host).Text = "Later unsaved title";
            Assert.False(await host.TrySaveBeforeCloseAsync());
            Assert.Equal(document.Id, host.Document!.Id);
            Assert.Equal("Later unsaved title", host.Document.Title);
        }
        finally { repository.ReleaseSave.TrySetResult(); }
        Assert.False(await save);

        Assert.True(host.Page.IsDirty);
        Assert.Equal("Later unsaved title", host.Document!.Title);
        Assert.Equal("Submitted title", (await services.Repository.LoadAsync(document.Id, default))!.Title);
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.Equal("Later unsaved title", (await services.Repository.LoadAsync(document.Id, default))!.Title);
        Assert.Equal(3, (await services.Repository.LoadAsync(document.Id, default))!.Version);
    }

    [AvaloniaFact]
    public async Task Close_refuses_while_actual_initial_library_read_is_pending()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var repository = new ControlledNotesRepository(services.Repository);
        repository.DelayNextList();
        await using var host = WriteAppHost.Create(repository, services.Formats);
        var initialize = host.InitializeAsync();
        await repository.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Null(host.Document);
            Assert.False(await host.TrySaveBeforeCloseAsync());
        }
        finally { repository.ReleaseList.TrySetResult(); }
        await initialize;
        Assert.True(await host.TrySaveBeforeCloseAsync());
    }

    private static Input Title(WriteAppHost host) => Assert.Single(
        Assert.IsType<HavenSceneControl>(host.Page.Content).Root!.DescendantsAndSelf().OfType<Input>(),
        element => element.Name == "Write.Word.Title");

    private sealed class DocumentServices
    {
        public DocumentServices(IAppPaths paths)
        {
            var validator = new NotesDocumentValidator();
            var diagnostics = new ProductionDiagnostics(paths);
            Repository = new NotesRepository(paths, validator, diagnostics);
            Formats = new NotesImportExportService(validator, diagnostics);
        }
        public NotesRepository Repository { get; }
        public NotesImportExportService Formats { get; }
    }

    private sealed class ControlledNotesRepository(INotesRepository inner) : INotesRepository
    {
        private bool _delaySave, _delayList;
        public bool FailSaves { get; set; }
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void DelayNextSave() => _delaySave = true;
        public void DelayNextList() => _delayList = true;

        public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token)
        {
            if (_delayList)
            {
                _delayList = false; ListEntered.TrySetResult();
                await ReleaseList.Task.WaitAsync(token);
            }
            return await inner.ListAsync(token);
        }
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        {
            if (FailSaves) throw new IOException("Controlled refusal before original repository publication.");
            if (_delaySave)
            {
                _delaySave = false; SaveEntered.TrySetResult();
                await ReleaseSave.Task.WaitAsync(token);
            }
            return await inner.SaveAsync(document, reason, token);
        }
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => inner.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string versionId, CancellationToken token) => inner.LoadVersionAsync(id, versionId, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => inner.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => inner.SearchAsync(query, token);
    }

    private sealed class TemporaryWritePaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-write-app-tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
