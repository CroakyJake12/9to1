using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;

namespace HavenOS.Apps.Data.Native.Tests;

public sealed class DataAppWorkflowTests
{
    [AvaloniaFact]
    public async Task Original_local_editor_creates_edits_saves_and_reopens_the_same_physical_workbook()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = new PhysicalWorkbooks();
        var host = fixture.CreateHost(fixture.Repository);
        await host.InitializeAsync(token);
        Assert.Null(host.Workbook);
        Assert.Empty(await fixture.Repository.ListAsync(token));

        var landing = Element<Button>(host, "Data.Landing.New");
        var intent = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        landing.KeyDown(intent); landing.KeyUp(intent);
        await WaitUntilAsync(() => host.Workbook is not null
            && Element<Button>(host, "Data.Workbook.New").GetValue(HavenProperties.Enabled), token);
        var id = host.Workbook!.Id;
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, Assert.Single(await fixture.Repository.ListAsync(token)).Id);
        Element<Input>(host, "Data.Workbook.Title").Text = "Local coursework";
        Element<Input>(host, "Data.Cell.Value").Text = "Ada";
        Assert.True(host.Page.IsDirty);
        Assert.Equal("Ada", host.Workbook.Sheets[0].GetCell(0, 0)?.Value);
        Assert.True(await host.TrySaveBeforeCloseAsync(token));
        Assert.False(host.Page.IsDirty);
        await host.DisposeAsync();
        Assert.True(host.IsDisposed);

        var reopened = fixture.CreateHost(new DataWorkbookRepository(fixture));
        Assert.True(await reopened.OpenWorkbookAsync(id, token));
        Assert.Equal(id, reopened.Workbook?.Id);
        Assert.Equal("Local coursework", reopened.Workbook?.Title);
        Assert.Equal("Ada", reopened.Workbook?.Sheets[0].GetCell(0, 0)?.Value);
        Assert.True(reopened.Workbook!.Version >= 2);
        Assert.True(File.Exists(fixture.CurrentPath(id)));
        Assert.Single(await fixture.Repository.ListAsync(token));
    }

    [AvaloniaFact]
    public async Task Save_refusal_retains_the_actual_dirty_editor_and_prior_file_until_explicit_retry()
    {
        var token = TestContext.Current.CancellationToken;
        await using var fixture = new PhysicalWorkbooks();
        var original = DataWorkbook.Create("Previously saved");
        original.Sheets[0].SetCell(0, 0, "Saved value");
        await fixture.Repository.SaveAsync(original, "Real fixture seed", token);
        var physicalBefore = await File.ReadAllBytesAsync(fixture.CurrentPath(original.Id), token);
        var refusingStore = new BeforeWriteRefusal(fixture.Repository);
        fixture.Refusals.Add(refusingStore);
        var host = fixture.CreateHost(refusingStore);
        Assert.True(await host.OpenWorkbookAsync(original.Id, token));
        var actualDraft = host.Workbook;
        Element<Input>(host, "Data.Cell.Value").Text = "Unsaved draft";
        Assert.True(host.Page.IsDirty);
        refusingStore.RejectSave = true;

        Assert.False(await host.TrySaveBeforeCloseAsync(token));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
        Assert.False(host.IsDisposed);
        Assert.Same(actualDraft, host.Workbook);
        Assert.True(host.Page.IsDirty);
        Assert.Equal("Unsaved draft", host.Workbook!.Sheets[0].GetCell(0, 0)?.Value);
        Assert.Equal(physicalBefore, await File.ReadAllBytesAsync(fixture.CurrentPath(original.Id), token));
        Assert.Equal("Saved value", (await fixture.Repository.LoadAsync(original.Id, token))?.Sheets[0].GetCell(0, 0)?.Value);

        refusingStore.RejectSave = false;
        Assert.True(await host.TrySaveBeforeCloseAsync(token));
        await host.DisposeAsync();
        var reopened = fixture.CreateHost(new DataWorkbookRepository(fixture));
        Assert.True(await reopened.OpenWorkbookAsync(original.Id, token));
        Assert.Equal(original.Id, reopened.Workbook?.Id);
        Assert.Equal("Unsaved draft", reopened.Workbook?.Sheets[0].GetCell(0, 0)?.Value);
        Assert.Single(await fixture.Repository.ListAsync(token));
    }

    private static T Element<T>(DataAppHost host, string name) where T : HavenElement
    {
        var scene = Assert.IsType<HavenSceneControl>(host.Page.Content);
        var root = Assert.IsAssignableFrom<HavenElement>(scene.Root);
        return Assert.Single(root.DescendantsAndSelf().OfType<T>(), item => item.Name == name);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The original Data create action did not complete.");
            await Task.Delay(10, token);
        }
    }

    private sealed class BeforeWriteRefusal(IDataWorkbookRepository original) : IDataWorkbookRepository
    {
        public bool RejectSave { get; set; }
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken token) => original.ListAsync(token);
        public Task<DataWorkbook?> LoadAsync(Guid id, CancellationToken token) => original.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => original.DeleteAsync(id, token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken token)
            => RejectSave ? Task.FromException<DataSaveResult>(new IOException("Test-owned refusal before any physical write."))
                : original.SaveAsync(workbook, reason, token);
    }

    private sealed class PhysicalWorkbooks : IAppPaths, IAsyncDisposable
    {
        private readonly List<DataAppHost> _hosts = [];
        public PhysicalWorkbooks()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-data-native-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            Repository = new(this);
        }
        public DataWorkbookRepository Repository { get; }
        public List<BeforeWriteRefusal> Refusals { get; } = [];
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "state.json");
        public string CurrentPath(Guid id) => Path.Combine(DataDirectory, "Data", "Workbooks", id.ToString("D"), "current.json");
        public DataAppHost CreateHost(IDataWorkbookRepository repository)
        {
            var host = DataAppHost.Create(repository, new DataXlsxFormatService(), new DataWorkbookQueryService());
            _hosts.Add(host); return host;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var refusal in Refusals) refusal.RejectSave = false;
            foreach (var host in _hosts) if (!host.IsDisposed) await host.DisposeAsync();
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
