using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiContextMenuFocusTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Closing_context_menu_returns_keyboard_input_to_focused_descendant(bool objectOwner, bool programmaticOpen)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture(objectOwner);
            fixture.Editor.Focus();
            fixture.Editor.CaretIndex = fixture.Editor.Text!.Length;
            fixture.Open(programmaticOpen);
            fixture.CloseAfterPlatformFocusReset();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Editor.IsFocused);
            fixture.Window.KeyTextInput(" after");
            Assert.Equal("before after", fixture.Editor.Text);
            fixture.Window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
            fixture.Window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
            Assert.Equal("before", fixture.Editor.Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("focus-before-close")]
    [InlineData("focus-after-close")]
    [InlineData("removed-editor")]
    [InlineData("removed-owner")]
    [InlineData("disabled-editor")]
    [InlineData("replaced-menu")]
    [InlineData("disposed-loader")]
    public async Task Closing_context_menu_does_not_reclaim_withdrawn_or_explicitly_moved_focus(string change)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture(true);
            fixture.Editor.Focus();
            fixture.Open(false);
            fixture.ResetPlatformFocus();
            if (change == "focus-before-close") fixture.Other.Focus();
            fixture.Menu.Close();
            switch (change)
            {
                case "focus-after-close": fixture.Other.Focus(); break;
                case "removed-editor": ((Panel)fixture.Owner).Children.Clear(); break;
                case "removed-owner": fixture.Window.Content = null; break;
                case "disabled-editor": fixture.Editor.IsEnabled = false; break;
                case "replaced-menu": fixture.Owner.ContextMenu = new ContextMenu(); break;
                case "disposed-loader": fixture.Loader.Dispose(); break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(fixture.Editor.IsFocused);
            if (change.StartsWith("focus-", StringComparison.Ordinal)) Assert.True(fixture.Other.IsFocused);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Reopened_menu_invalidates_the_previous_pending_focus_return()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture(true);
            fixture.Editor.Focus();
            fixture.Open(false);
            fixture.CloseAfterPlatformFocusReset();
            fixture.Other.Focus();
            fixture.Menu.Open(fixture.Owner);
            Assert.True(fixture.Menu.IsOpen);
            Assert.IsType<CuiMenuItem>(Assert.Single(fixture.Menu.Items)).Focus();
            fixture.CloseAfterPlatformFocusReset();
            Dispatcher.UIThread.RunJobs();
            Assert.False(fixture.Editor.IsFocused);
        }, CancellationToken.None);
    }

    private sealed class Fixture : IDisposable
    {
        public CuiControlLoader Loader { get; }
        public Window Window { get; }
        public Control Owner { get; }
        public TextBox Editor { get; }
        public TextBox Other { get; }
        public ContextMenu Menu => Owner.ContextMenu!;

        public Fixture(bool objectOwner)
        {
            var registry = new CuiControlRegistry();
            var nativeEditor = new TextBox { Text = "before" };
            registry.RegisterObjectRenderer("Document", _ => new Panel { Children = { nativeEditor } });
            Loader = new CuiControlLoader(registry);
            const string menu = "<Container Type='Context Menu'><Button Type='Menu Item' Content='Action'/></Container>";
            var ownerMarkup = objectOwner ? "<Object Type='Document'>" + menu + "</Object>"
                : "<Container Type='Vertical Stack'><Input Type='Text' Value='before'/>" + menu + "</Container>";
            var loaded = Loader.LoadMarkup("<Container Type='Vertical Stack'>" + ownerMarkup + "<Input Type='Text' Value='other'/></Container>");
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsType<CuiContainerControl>(loaded.Root);
            Owner = root.AuthoredChildren[0];
            Editor = objectOwner ? nativeEditor : Assert.IsType<TextBox>(Assert.IsType<CuiContainerControl>(Owner).AuthoredChildren[0]);
            Other = Assert.IsType<TextBox>(root.AuthoredChildren[1]);
            Assert.False(Owner.Focusable);
            Window = new Window { Width = 500, Height = 400, Content = root };
            Window.Show(); Window.UpdateLayout();
        }

        public void Open(bool programmatic)
        {
            if (programmatic) Menu.Open(Owner); else Owner.RaiseEvent(new ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.True(Menu.IsOpen);
            Assert.IsType<CuiMenuItem>(Assert.Single(Menu.Items)).Focus();
        }

        public void ResetPlatformFocus()
        {
            // A desktop Window normally restores its own remembered focus when
            // an overlay disappears. Exercise the focus-cleared platform path
            // separately so that native restoration cannot mask CUI's fallback.
            Window.FocusManager!.Focus(null);
            Window.FocusManager.Focus(null);
            Assert.Null(Window.FocusManager.GetFocusedElement());
        }

        public void CloseAfterPlatformFocusReset() { ResetPlatformFocus(); Menu.Close(); }

        public void Dispose() { Loader.Dispose(); Window.Close(); }
    }
}
