using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Present;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class PresentLibraryTitleSearchTests
{
    [AvaloniaFact]
    public async Task Actual_local_library_search_filters_pinned_and_recent_titles_without_rewriting_decks()
    {
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            var repository = new PresentRepository(paths);
            var alpha = PresentDocument.Create("Alpha research");
            alpha.Metadata["pinned"] = "True";
            var beta = PresentDocument.Create("Beta research");
            await repository.SaveAsync(alpha, "Actual pinned deck", CancellationToken.None);
            await repository.SaveAsync(beta, "Actual recent deck", CancellationToken.None);
            var before = Snapshot(paths.DataDirectory);
            using var scene = new PresentHavenScene();
            scene.SetLibrary(await repository.ListAsync(CancellationToken.None));
            Assert.Equal(2, Cards(scene.RecentDecks).Length);
            Assert.Single(Cards(scene.PinnedDecks));
            scene.LibrarySearch.Text = "  BETA  ";
            Assert.Equal($"Present.Library.Card.{beta.Id:N}", Assert.Single(Cards(scene.RecentDecks)));
            Assert.Empty(Cards(scene.PinnedDecks));
            Assert.Contains(scene.PinnedDecks.DescendantsAndSelf().OfType<Haven.UI.Components.Text>(),
                text => text.Content == "No matching pinned presentations");
            Assert.Equal("1 of 2 local presentations match.", scene.LibrarySearchSummary.Content);
            scene.LibrarySearch.Text = "missing deck";
            Assert.Empty(Cards(scene.RecentDecks));
            Assert.Equal("No local presentation titles match your search.", scene.LibrarySearchSummary.Content);
            scene.LibrarySearch.Text = string.Empty;
            Assert.Equal(2, Cards(scene.RecentDecks).Length);
            Assert.Single(Cards(scene.PinnedDecks));
            Assert.Equal(before, Snapshot(paths.DataDirectory));
            Assert.Equal(alpha.Id, (await new PresentRepository(paths).LoadAsync(alpha.Id, CancellationToken.None))!.Id);
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }

    [AvaloniaFact]
    public async Task Search_survives_real_library_refresh_and_retired_search_input_does_not_rebuild_cards()
    {
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            var repository = new PresentRepository(paths);
            var first = PresentDocument.Create("Shared title");
            var second = PresentDocument.Create("Shared title");
            await repository.SaveAsync(first, "First distinct identity", CancellationToken.None);
            await repository.SaveAsync(second, "Second distinct identity", CancellationToken.None);
            using var scene = new PresentHavenScene();
            scene.SetLibrary(await repository.ListAsync(CancellationToken.None));
            scene.LibrarySearch.Text = "shared";
            Assert.Equal(2, Cards(scene.RecentDecks).Length);
            await repository.DeleteAsync(first.Id, CancellationToken.None);
            scene.SetLibrary(await repository.ListAsync(CancellationToken.None));
            Assert.Equal("shared", scene.LibrarySearch.Text);
            Assert.Equal($"Present.Library.Card.{second.Id:N}", Assert.Single(Cards(scene.RecentDecks)));
            var retainedCards = scene.RecentDecks.Children.ToArray();
            scene.Dispose();
            scene.LibrarySearch.Text = "unmatched after retirement";
            Assert.Equal(retainedCards, scene.RecentDecks.Children.ToArray());
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }

    private static string[] Cards(Haven.UI.Components.Container gallery) => gallery.Children
        .Where(element => element.Name?.StartsWith("Present.Library.Card.", StringComparison.Ordinal) == true)
        .Select(element => element.Name!).ToArray();
    private static string Snapshot(string root) => string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal).Select(path => Path.GetRelativePath(root, path) + ":" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))));
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-present-search-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
