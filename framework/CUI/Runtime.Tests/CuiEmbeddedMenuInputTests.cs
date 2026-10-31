using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiEmbeddedMenuInputTests
{
    [Fact]
    public async Task Submenu_selector_retains_keyboard_focus_opens_with_space_and_closes_with_escape()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("""
                <Cui><Container ID="menubar" Type="Menu">
                  <Button ID="more" Type="Menu Item" Content="More">
                    <Button ID="format" Type="Menu Item" Content="Format">
                      <Container ID="fields" Type="Vertical Stack">
                        <Input ID="choice" Type="Select" Width="180" />
                      </Container>
                    </Button>
                  </Button>
                </Container></Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 600, Height = 400 };
            window.Show(); window.UpdateLayout();
            try
            {
                T Find<T>(string id) where T : Control => root.GetSelfAndLogicalDescendants().OfType<T>().Single(control =>
                    AutomationProperties.GetAutomationId(control) == id);
                var more = Find<MenuItem>("more");
                var format = Find<MenuItem>("format");
                var choice = Find<ComboBox>("choice");
                choice.ItemsSource = new[] { "Paragraph", "Heading 1" };
                choice.SelectedIndex = 0;
                more.IsSubMenuOpen = true; format.IsSubMenuOpen = true;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(choice.IsEffectivelyVisible);
                Assert.True(choice.IsEffectivelyEnabled);
                Assert.True(choice.Focus(), "The containing menu item must not reclaim the embedded selector's focus.");
                Assert.True(choice.IsFocused);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                Dispatcher.UIThread.RunJobs();
                Assert.True(choice.IsDropDownOpen);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");
                Dispatcher.UIThread.RunJobs();
                Assert.False(choice.IsDropDownOpen);
                Assert.True(choice.IsFocused);
                Assert.True(format.Focus(), "Ordinary menu-item focus still works after using the editor.");
            }
            finally { window.Close(); }
        }, lifetime.Token);
    }
}
