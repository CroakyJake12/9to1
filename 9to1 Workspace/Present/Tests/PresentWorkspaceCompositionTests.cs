using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace HavenOS.Apps.Present.Tests;

public sealed class PresentWorkspaceCompositionTests
{
    [AvaloniaFact]
    public async Task Mounted_workspace_menu_edits_save_and_library_open_reuse_the_real_local_document()
    {
        using var paths = new TemporaryPresentPaths();
        var repository = new PresentRepository(paths);
        Guid documentId;
        using (var host = PresentAppHost.Create(repository, new PresentPptxExportService(), new PresentPptxImportService(paths)))
        {
            await host.InitializeAsync();
            var root = Assert.IsType<HavenSceneControl>(host.Page.Content).Root!;
            var firstId = Assert.IsType<PresentDocument>(host.Document).Id;
            AssertVisible(Element(root, "Present.MenuBar"));
            AssertVisible(CanonicalWorkspace(root));
            AssertCollapsed(Element(root, "Present.Library"));

            InvokeFileAction(root, "New presentation");
            await WaitUntilAsync(() => host.Document is { } document && document.Id != firstId);
            documentId = host.Document!.Id;
            Assert.Single(host.Document.Slides);
            Assert.Equal(2, (await repository.ListAsync(CancellationToken.None)).Count);

            Assert.IsType<Input>(Element(root, "Present.Deck.Title")).Text = "Workspace composition control";
            Assert.IsType<Input>(Element(root, "Present.Slide.Notes")).Text = "Persisted speaker notes";
            Invoke(Assert.IsType<HavenButton>(Element(root, "Present.Slide.Add")));
            Assert.Equal(2, host.Document.Slides.Count);
            Assert.True(host.Page.IsDirty);
            InvokeFileAction(root, "Save");
            await WaitUntilAsync(() => !host.Page.IsDirty);

            var persisted = Assert.IsType<PresentDocument>(await repository.LoadAsync(documentId, CancellationToken.None));
            Assert.Equal("Workspace composition control", persisted.Title);
            Assert.Equal("Persisted speaker notes", persisted.Slides[0].SpeakerNotes);
            Assert.Equal(2, persisted.Slides.Count);

            InvokeFileAction(root, "Back to presentations");
            await WaitUntilAsync(() => host.Document is null);
            AssertVisible(Element(root, "Present.Library"));
            AssertCollapsed(CanonicalWorkspace(root));
            AssertCollapsed(Element(root, "Present.MenuBar"));
            var search = Assert.IsType<Input>(Element(root, "Present.Library.Search"));
            Assert.Equal("Search local presentation titles", search.Accessibility.AccessibleName);
            search.Text = "Workspace composition control";
            Assert.Single(root.DescendantsAndSelf().OfType<HavenButton>(),
                button => button.Name?.StartsWith("Present.Library.Open.", StringComparison.Ordinal) == true);
            Invoke(Assert.IsType<HavenButton>(Element(root, $"Present.Library.Open.{documentId:N}")));
            await WaitUntilAsync(() => host.Document?.Id == documentId);
            AssertVisible(Element(root, "Present.MenuBar"));
            AssertVisible(CanonicalWorkspace(root));
            Assert.Equal("Workspace composition control", host.Document!.Title);
            Assert.Equal(2, host.Document.Slides.Count);
            Assert.True(await host.TrySaveBeforeCloseAsync());
        }

        using var reopened = PresentAppHost.Create(repository, new PresentPptxExportService(), new PresentPptxImportService(paths));
        await reopened.InitializeAsync();
        Assert.Equal(documentId, reopened.Document!.Id);
        Assert.Equal("Workspace composition control", reopened.Document.Title);
        Assert.Equal("Persisted speaker notes", reopened.Document.Slides[0].SpeakerNotes);
        Assert.Equal(2, reopened.Document.Slides.Count);
    }

    [AvaloniaFact]
    public async Task Failed_workspace_return_preserves_dirty_document_and_allows_a_real_save_retry()
    {
        using var paths = new TemporaryPresentPaths();
        var repository = new ControllablePresentRepository(new PresentRepository(paths));
        using var host = PresentAppHost.Create(repository, new PresentPptxExportService(), new PresentPptxImportService(paths));
        await host.InitializeAsync();
        var root = Assert.IsType<HavenSceneControl>(host.Page.Content).Root!;
        var documentId = host.Document!.Id;
        Assert.IsType<Input>(Element(root, "Present.Deck.Title")).Text = "Keep this unsaved edit";
        repository.FailSaves = true;
        InvokeFileAction(root, "Back to presentations");
        await WaitUntilAsync(() => repository.RefusedSaves > 0);

        Assert.True(host.Page.IsDirty);
        Assert.Equal(documentId, host.Document!.Id);
        Assert.Equal("Keep this unsaved edit", host.Document.Title);
        AssertVisible(CanonicalWorkspace(root));
        AssertCollapsed(Element(root, "Present.Library"));
        Assert.Equal("Untitled presentation", (await repository.LoadAsync(documentId, CancellationToken.None))!.Title);

        repository.FailSaves = false;
        InvokeFileAction(root, "Back to presentations");
        await WaitUntilAsync(() => host.Document is null);
        Assert.False(host.Page.IsDirty);
        AssertVisible(Element(root, "Present.Library"));
        Assert.Equal("Keep this unsaved edit", (await repository.LoadAsync(documentId, CancellationToken.None))!.Title);
    }

    private static HavenElement Element(HavenElement root, string name) =>
        Assert.Single(root.DescendantsAndSelf(), element => element.Name == name);

    private static Container CanonicalWorkspace(HavenElement root)
    {
        // The collapsed compatibility container retains its old name. Select
        // the actual mounted stage's parent rather than that hidden mirror.
        var workspace = Assert.IsType<Container>(Element(root, "Present.Stage").Parent);
        Assert.Equal("Present.Workspace", workspace.Name);
        return workspace;
    }

    private static void AssertVisible(HavenElement element)
    {
        for (HavenElement? current = element; current is not null; current = current.Parent)
            Assert.Equal(HavenVisibility.Visible, current.GetValue(HavenProperties.Visibility));
    }

    private static void AssertCollapsed(HavenElement element) =>
        Assert.Equal(HavenVisibility.Collapsed, element.GetValue(HavenProperties.Visibility));

    private static void InvokeFileAction(HavenElement root, string label)
    {
        var menu = Assert.Single(root.DescendantsAndSelf().OfType<HavenButton>(), button => button.Name == "Present.Menu.File");
        AssertVisible(menu);
        Invoke(menu);
        var popup = Assert.Single(root.DescendantsAndSelf().OfType<PopupMenu>());
        Assert.Equal("File menu", popup.Card.Accessibility.AccessibleName);
        Invoke(Assert.Single(popup.Card.DescendantsAndSelf().OfType<HavenButton>(), button => button.Content == label));
    }

    private static void Invoke(HavenButton button)
    {
        AssertVisible(button);
        Assert.Equal(HavenAccessibleRole.Button, button.Accessibility.Role);
        var input = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(button.KeyDown(input));
        Assert.True(button.KeyUp(input));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControllablePresentRepository(IPresentRepository inner) : IPresentRepository
    {
        public bool FailSaves { get; set; }
        public int RefusedSaves { get; private set; }
        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken) => inner.ListAsync(cancellationToken);
        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken) => inner.LoadAsync(documentId, cancellationToken);
        public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken cancellationToken)
        {
            if (!FailSaves) return inner.SaveAsync(document, reason, cancellationToken);
            RefusedSaves++;
            return Task.FromException<PresentSaveResult>(new IOException("Controlled storage refusal."));
        }
        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) => inner.DeleteAsync(documentId, cancellationToken);
    }

    private sealed class TemporaryPresentPaths : IAppPaths, IDisposable
    {
        public TemporaryPresentPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-present-workspace-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
}
