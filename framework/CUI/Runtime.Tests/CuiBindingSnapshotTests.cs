using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiBindingSnapshotTests
{
    [Fact]
    public async Task Binding_refresh_reads_each_current_row_value_once_and_keeps_its_typed_brush()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var model = new Rows();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                    <Text Text="{Binding row.Text}" Foreground="{Binding row.Brush}" />
                  </Repeat>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = loaded.Root!;
            loader.WireBindings(root);
            var text = Assert.Single(root.GetVisualDescendants().OfType<TextBlock>());
            var current = new Row("Current", Brushes.Coral);
            model.Replace(current);
            Assert.Same(text, Assert.Single(root.GetVisualDescendants().OfType<TextBlock>()));
            Assert.Equal("Current", text.Text);
            Assert.Same(Brushes.Coral, text.Foreground);
            Assert.Equal(1, current.TextReads);
            Assert.Equal(1, current.BrushReads);
        }, CancellationToken.None);
    }

    private sealed class Rows : ICuiBindingContext, INotifyPropertyChanged
    {
        private Row _row = new("Initial", Brushes.CadetBlue);
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        { value = path == "Rows" ? new[] { _row } : null; return path == "Rows"; }
        public void Replace(Row row)
        { _row = row; PropertyChanged?.Invoke(this, new("Rows")); }
    }

    private sealed class Row(string text, IBrush brush) : ICuiBindingContext
    {
        public int TextReads { get; private set; }
        public int BrushReads { get; private set; }
        public bool TryGetValue(string path, out object? value)
        {
            value = null;
            switch (path)
            {
                case "Id": value = "stable"; return true;
                case "Text": TextReads++; value = text; return true;
                case "Brush": BrushReads++; value = brush; return true;
                default: return false;
            }
        }
    }
}
