using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsHeaderInteractionTests
{
    [Fact]
    public async Task Board_name_opens_file_operations_and_rename_updates_the_same_document()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://header-interaction");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            using var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(vm); loader.SetActionDispatcher(vm);
            var loaded = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root); vm.Attach(root);
            var window = new Window { Width = 900, Height = 700, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var menu = BoardsCuiControlFinder.FindByAutomationId<MenuItem>(root, "BoardSwitcher")!;
                Assert.Contains(session.Document.Title, menu.Header?.ToString());
                var position = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2), window)!.Value;
                window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(menu.IsSubMenuOpen);
                foreach (var id in new[] { "BoardSwitcherCreate", "BoardSwitcherOpen", "BoardSwitcherSave", "BoardSwitcherSaveAs", "RenameBoardMenuItem", "FileBoardProperties" })
                    Assert.NotNull(BoardsCuiControlFinder.FindByAutomationId<MenuItem>(root, id));
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                Assert.False(menu.IsSubMenuOpen);
                vm.DispatchAsync("RenameBoard", null).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var title = BoardsCuiControlFinder.FindByAutomationId<TextBox>(root, "BoardTitleBox")!;
                Assert.True(title.IsFocused);
                title.SelectAll(); window.KeyTextInput("Renamed with keyboard");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Renamed with keyboard", session.Document.Title);
                Assert.Equal("Renamed with keyboard ▾", menu.Header?.ToString());
                Assert.Equal(false, vm.Get("BoardRenameVisible"));
                Assert.Null(TopLevel.GetTopLevel(title));
                Assert.True(menu.IsFocused);
                var undo = BoardsCuiControlFinder.FindByAutomationId<Button>(root, "UndoButton")!;
                Assert.Equal(session.CanUndo, undo.IsEffectivelyEnabled);
                Assert.NotNull(BoardsCuiControlFinder.FindByAutomationId<TextBlock>(root, "SaveState"));
            }
            finally { window.Close(); }
        });
    }
}
