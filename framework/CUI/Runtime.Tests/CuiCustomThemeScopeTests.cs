using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiCustomThemeScopeTests
{
    [Fact]
    public async Task Live_draft_updates_its_real_scope_without_replacing_unrelated_resource_owners()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var draft = CuiCustomThemeContract.CreateDefault("Local Preview");
            var catalog = new CuiThemeDefinitionCatalog();
            catalog.UpsertCustomTheme(draft, ephemeral: true);
            var source = new Source();
            using var context = new CuiThemePreferenceContext(source, catalog);
            using var loader = new CuiControlLoader();
            loader.SetThemePreferenceContext(context);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Page><Container Type="Vertical Stack">
                  <Input Type="Text" Value="Keep my selection" />
                  <Theme Name="Local Preview"><Button Content="Preview" AccentRole="Primary" /></Theme>
                </Container></Page>
                """);
            Assert.Empty(diagnostics);
            var window = new Window { Content = root, Width = 500, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var input = root!.GetVisualDescendants().OfType<TextBox>().Single();
                var button = root.GetVisualDescendants().OfType<Button>().Single();
                input.Focus(); input.SelectionStart = 5; input.SelectionEnd = 7;
                var owner = Assert.Single(input.Resources.MergedDictionaries);
                var previewOwner = Assert.Single(button.Resources.MergedDictionaries);
                var next = draft with { PrimaryAccent = new(CuiCustomFillKind.SolidColour, new("#CE3864")) };
                catalog.UpsertCustomTheme(next, ephemeral: true);
                Assert.Same(owner, Assert.Single(input.Resources.MergedDictionaries));
                Assert.NotSame(previewOwner, Assert.Single(button.Resources.MergedDictionaries));
                Assert.Equal(Color.Parse("#CE3864"), Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
                Assert.True(input.IsKeyboardFocusWithin);
                Assert.Equal("my", input.SelectedText);
                Assert.Equal("Keep my selection", input.Text);
                var stable = Assert.Single(button.Resources.MergedDictionaries);
                catalog.UpsertCustomTheme(next, ephemeral: true);
                Assert.Same(stable, Assert.Single(button.Resources.MergedDictionaries));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class Source : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new("Professional", 1, "account:scope-test", false, CuiAccessibilitySettings.Default);
        public event EventHandler? Changed { add { } remove { } }
    }
}
