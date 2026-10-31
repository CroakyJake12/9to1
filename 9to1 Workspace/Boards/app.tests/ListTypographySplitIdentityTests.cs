using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class ListTypographySplitIdentityTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Save_keeps_new_row_identity_and_subsequent_typing_updates_that_same_row(HavenRichBlockKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "list-split-identity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "split.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Stable split"))
            {
                await owner.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks =
                    [new HavenRichBlock { Id = "list", Kind = kind, Items = [new HavenRichListItem { Id = "row", Text = "AB", FontSize = 24 }] }]);
                await owner.SaveAsAsync(path);
            }
            string created;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                using var vm = new BoardsViewModel(adapter);
                if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
                Assert.True(await vm.SplitTextBlockAtAsync("list:row", 1));
                created = vm.SelectedBlockId;
                Assert.NotEqual("list:row", created);
                await adapter.SaveAsync();
                var rows = adapter.Document.Sections[0].Pages[0].Blocks;
                Assert.Equal(2, rows.Count);
                var following = Assert.Single(rows, row => row.Id == created);
                Assert.Equal("B", following.Text); Assert.Equal(24, following.FontSize);
                vm.EditText("chk_" + created.Replace(':', '_'), "B continued");
                await adapter.SaveAsync();
                rows = adapter.Document.Sections[0].Pages[0].Blocks;
                Assert.Equal(2, rows.Count);
                Assert.Equal("B continued", Assert.Single(rows, row => row.Id == created).Text);
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var persisted = reopened.Document.Sections[0].Pages[0].Blocks;
            Assert.Equal(2, persisted.Count); Assert.Equal("B continued", Assert.Single(persisted, row => row.Id == created).Text);
        }
        finally { Directory.Delete(root, true); }
    }
}
