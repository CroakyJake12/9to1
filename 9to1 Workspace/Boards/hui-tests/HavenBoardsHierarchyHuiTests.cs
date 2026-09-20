using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Xunit;
using HavenText = Haven.UI.Components.Text;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardsHierarchyHuiTests
{
    [Fact]
    public async Task Hierarchy_parent_and_nested_marker_survive_move_and_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var firstStore = new JsonFileHavenBoardStore(root))
            {
                await using var first = await HavenBoardsHuiSession.OpenAsync(firstStore);
                await first.ExecuteAsync(new SetCardParentCommand("card-3", "card-1"));
                await first.ExecuteAsync(new MoveCardCommand("done", 0, "doing", 1));

                var nested = FindCard(first.Snapshot, "card-3");
                Assert.Equal("card-1", nested.ParentCardId);
                Assert.Equal("doing", GroupFor(first.Snapshot, "card-3"));
                AssertNestedMarker(first, "card-3");
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);

            var reopenedNested = FindCard(reopened.Snapshot, "card-3");
            Assert.Equal("card-1", reopenedNested.ParentCardId);
            Assert.Equal("doing", GroupFor(reopened.Snapshot, "card-3"));
            AssertNestedMarker(reopened, "card-3");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Session_open_rejects_persisted_missing_parent_before_render()
    {
        var root = TempBoardDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var malformed = new HavenBoardSnapshot(
                "board-main",
                "Malformed",
                9,
                [new HavenBoardGroup(
                    "todo",
                    "To do",
                    [new HavenBoardCard("child", "Child", ParentCardId: "missing-parent")])]);
            await store.SaveAsync(malformed);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                HavenBoardsHuiSession.OpenAsync(store));

            Assert.Contains("missing parent", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static HavenBoardCard FindCard(HavenBoardSnapshot snapshot, string cardId) =>
        snapshot.Groups.SelectMany(group => group.Cards).Single(card => card.Id == cardId);

    private static string GroupFor(HavenBoardSnapshot snapshot, string cardId) =>
        snapshot.Groups.Single(group => group.Cards.Any(card => card.Id == cardId)).Id;

    private static void AssertNestedMarker(HavenBoardsHuiSession session, string cardId)
    {
        var sceneCard = session.Scene.Root.DescendantsAndSelf()
            .Single(element => element.Name == $"BoardCard_{SafeName(cardId)}");
        Assert.Contains(
            sceneCard.DescendantsAndSelf().OfType<HavenText>(),
            text => text.Content == "Nested card");
    }

    private static string SafeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-hierarchy-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
