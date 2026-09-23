using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace HavenOS.Apps.Present.Tests;

public sealed class PresentAppCloseTests
{
    [AvaloniaFact]
    public async Task Close_save_persists_dirty_presentation_for_the_next_app_host()
    {
        using var paths = new TemporaryPresentPaths();
        var repository = new ControllablePresentRepository(new PresentRepository(paths));
        using var host = PresentAppHost.Create(repository, new NoOpPresentExporter());
        await host.InitializeAsync();

        PresentDocument document = Assert.IsType<PresentDocument>(host.Document);
        var proposal = PresentAiEdits.CreateProposal(
            new PresentEditor(document),
            "Rename before close",
            [new PresentEditOperation(PresentEditOperationKind.SetDocumentTitle, Text: "Saved on close")]);
        host.Page.ApplyAiProposal(proposal);

        Assert.True(host.Page.IsDirty);
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.False(host.Page.IsDirty);

        using var reopenedHost = PresentAppHost.Create(repository, new NoOpPresentExporter());
        await reopenedHost.InitializeAsync();

        Assert.Equal(document.Id, reopenedHost.Document!.Id);
        Assert.Equal("Saved on close", reopenedHost.Document.Title);
    }

    [AvaloniaFact]
    public async Task Failed_close_save_keeps_the_host_dirty_and_allows_a_retry()
    {
        using var paths = new TemporaryPresentPaths();
        var repository = new ControllablePresentRepository(new PresentRepository(paths));
        using var host = PresentAppHost.Create(repository, new NoOpPresentExporter());
        await host.InitializeAsync();

        PresentDocument document = Assert.IsType<PresentDocument>(host.Document);
        var proposal = PresentAiEdits.CreateProposal(
            new PresentEditor(document),
            "Rename before close",
            [new PresentEditOperation(PresentEditOperationKind.SetDocumentTitle, Text: "Retry close")]);
        host.Page.ApplyAiProposal(proposal);
        repository.FailSaves = true;

        Assert.False(await host.TrySaveBeforeCloseAsync());
        Assert.True(host.Page.IsDirty);
        Assert.Equal("Retry close", host.Document!.Title);

        repository.FailSaves = false;
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.False(host.Page.IsDirty);
    }

    private sealed class NoOpPresentExporter : IPresentExportService
    {
        public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];

        public Task<string> ExportAsync(
            PresentDocument document,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(destinationPath);
        }
    }

    private sealed class ControllablePresentRepository(IPresentRepository inner) : IPresentRepository
    {
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken) =>
            inner.LoadAsync(documentId, cancellationToken);

        public Task<PresentSaveResult> SaveAsync(
            PresentDocument document,
            string reason,
            CancellationToken cancellationToken) =>
            FailSaves
                ? Task.FromException<PresentSaveResult>(new IOException("Simulated presentation storage failure."))
                : inner.SaveAsync(document, reason, cancellationToken);

        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(documentId, cancellationToken);
    }

    private sealed class TemporaryPresentPaths : IAppPaths, IDisposable
    {
        public TemporaryPresentPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-present-close-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DataDirectory))
                {
                    Directory.Delete(DataDirectory, recursive: true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
