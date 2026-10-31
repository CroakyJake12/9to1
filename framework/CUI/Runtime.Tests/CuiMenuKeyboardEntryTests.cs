using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiMenuKeyboardEntryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Space_open_enters_first_enabled_field_inside_authored_submenu_content(bool nested)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var fields = """
                <Button ID="format" Type="Menu Item" Content="Font and style">
                  <Container Type="Vertical Stack">
                    <Input Type="Text" Value="Unavailable" Interactive="False"/>
                    <Input ID="font" Type="Text" Value="" Width="120"/>
                  </Container>
                </Button>
                """;
            using var loader = new CuiControlLoader();
            var loaded = loader.LoadMarkup("<Container Type='Menu'>" +
                (nested ? "<Button ID='more' Type='Menu Item' Content='More'>" + fields + "</Button>" : fields) +
                "</Container>");
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            T Find<T>(string id) where T : Control => root.GetSelfAndLogicalDescendants().OfType<T>()
                .Distinct<T>(ReferenceEqualityComparer.Instance)
                .Single(control => AutomationProperties.GetAutomationId(control) == id);
            var window = new Window { Content = root, Width = 600, Height = 400 };
            try
            {
                window.Show(); window.UpdateLayout();
                var format = Find<CuiMenuItem>("format");
                Assert.True((nested ? Find<CuiMenuItem>("more") : format).Focus());
                void PressSpace()
                {
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                }
                if (nested)
                {
                    PressSpace();
                    Assert.True(format.IsFocused);
                }
                PressSpace();
                Assert.True(format.IsSubMenuOpen);
                var font = Find<TextBox>("font");
                Assert.True(font.IsFocused, "Keyboard opening must enter the first enabled authored field.");
                window.KeyTextInput("Inter");
                Assert.Equal("Inter", font.Text);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
