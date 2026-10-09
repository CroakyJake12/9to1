using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI.Components;

namespace HavenOS.Apps.Present.Tests;

public sealed class PresentCloseGenerationTests
{
    [AvaloniaFact]
    public async Task Later_retained_edit_is_not_in_the_submitted_save_and_must_be_saved_before_close()
    {
        using var paths = new TemporaryPresentPaths();
        var original = new PresentRepository(paths);
        var controlled = new ControlledPresentRepository(original);
        using var host = PresentAppHost.Create(controlled, new PresentPptxExportService());
        await host.InitializeAsync();
        var id = host.Document!.Id;
        Title(host).Text = "Submitted presentation title";
        controlled.DelayNextSave();
        var save = host.Page.SaveAsync("Save original submitted edit");
        await controlled.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Title(host).Text = "Later retained presentation edit";
            Assert.False(await host.TrySaveBeforeCloseAsync());
            Assert.Equal(id, host.Document!.Id);
        }
        finally { controlled.ReleaseSave.TrySetResult(); }
        Assert.False(await save);

        Assert.True(host.Page.IsDirty);
        Assert.Equal("Later retained presentation edit", host.Document!.Title);
        Assert.Equal("Submitted presentation title", (await original.LoadAsync(id, default))!.Title);
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.Equal("Later retained presentation edit", (await original.LoadAsync(id, default))!.Title);
        Assert.Equal(3, (await original.LoadAsync(id, default))!.Version);

        using var reopened = PresentAppHost.Create(new PresentRepository(paths), new PresentPptxExportService());
        await reopened.InitializeAsync();
        Assert.Equal(id, reopened.Document!.Id);
        Assert.Equal("Later retained presentation edit", reopened.Document.Title);
        Assert.Equal(3, reopened.Document.Version);
    }

    [AvaloniaFact]
    public async Task Close_refuses_the_pending_initial_owner_read_before_any_document_exists()
    {
        using var paths = new TemporaryPresentPaths();
        var controlled = new ControlledPresentRepository(new PresentRepository(paths));
        controlled.DelayNextList();
        using var host = PresentAppHost.Create(controlled, new PresentPptxExportService());
        var initialize = host.InitializeAsync();
        await controlled.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Null(host.Document);
            Assert.False(await host.TrySaveBeforeCloseAsync());
        }
        finally { controlled.ReleaseList.TrySetResult(); }
        await initialize;
        Assert.NotNull(host.Document);
        Assert.True(await host.TrySaveBeforeCloseAsync());
    }

    private static Input Title(PresentAppHost host) => Assert.Single(
        Assert.IsType<HavenSceneControl>(host.Page.Content).Root!.DescendantsAndSelf().OfType<Input>(),
        element => element.Name == "Present.Deck.Title");

    private sealed class ControlledPresentRepository(IPresentRepository inner) : IPresentRepository
    {
        private bool _delaySave, _delayList;
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void DelayNextSave() => _delaySave = true;
        public void DelayNextList() => _delayList = true;
        public async Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken token)
        {
            if (_delayList)
            {
                _delayList = false; ListEntered.TrySetResult();
                await ReleaseList.Task.WaitAsync(token);
            }
            return await inner.ListAsync(token);
        }
        public async Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken token)
        {
            if (_delaySave)
            {
                _delaySave = false; SaveEntered.TrySetResult();
                await ReleaseSave.Task.WaitAsync(token);
            }
            return await inner.SaveAsync(document, reason, token);
        }
        public Task<PresentDocument?> LoadAsync(Guid id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
    }

    private sealed class TemporaryPresentPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-present-generation-tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
