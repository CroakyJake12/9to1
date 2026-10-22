using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiRepeatedImageRefreshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Refresh_resolves_replacement_item_and_retires_removed_item_before_image_binding(int notification)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new MutableRows();
            var attempts = new List<string>();
            var registry = new CuiControlRegistry();
            var picture = new DrawingImage(new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = Brushes.Black
            });
            registry.RegisterImageSourceResolver((source, _) => { attempts.Add(source); return picture; });
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                    <Image Source="{Binding row.Source}" Width="16" Height="16" />
                  </Repeat>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = loaded.Root!;
            loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                var image = Assert.Single(root.GetVisualDescendants().OfType<Image>());
                attempts.Clear();
                model.Rows = [new("same-key", "icon:new")];
                Refresh();
                Assert.Same(image, Assert.Single(root.GetVisualDescendants().OfType<Image>()));
                Assert.NotEmpty(attempts);
                Assert.All(attempts, source => Assert.Equal("icon:new", source));

                attempts.Clear();
                model.Rows = [];
                Refresh();
                Assert.Empty(root.GetVisualDescendants().OfType<Image>());
                Assert.Empty(attempts);
            }
            finally { window.Close(); }

            void Refresh()
            {
                if (notification == 0) loader.RefreshBindings();
                else model.Notify(notification == 1 ? null : "Rows");
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retiring_conditional_branch_does_not_read_replacement_rows_or_resolve_their_images(bool notify)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var model = new MutableRows();
            var attempts = new List<string>();
            var registry = new CuiControlRegistry();
            var picture = new DrawingImage(new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = Brushes.Black
            });
            registry.RegisterImageSourceResolver((source, _) => { attempts.Add(source); return picture; });
            using var loader = new CuiControlLoader(registry);
            loader.SetBindingContext(model);
            var loaded = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <If Condition="{Binding Visible}">
                    <Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}">
                      <Image Source="{Binding row.Source}" Width="16" Height="16" />
                    </Repeat>
                  </If>
                </Container>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = loaded.Root!;
            loader.WireBindings(root);
            var window = new Window { Width = 400, Height = 300, Content = root };
            window.Show(); window.UpdateLayout();
            try
            {
                Assert.Single(root.GetVisualDescendants().OfType<Image>());
                var reads = model.RowsReads;
                attempts.Clear();
                model.Visible = false;
                model.Rows = [new("new-key", "icon:must-not-resolve")];
                if (notify) model.Notify(null);
                else loader.RefreshBindings();
                Assert.Empty(root.GetVisualDescendants().OfType<Image>());
                Assert.Equal(reads, model.RowsReads);
                Assert.Empty(attempts);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed record Row(string Id, string Source);

    private sealed class MutableRows : ICuiBindingContext, ICuiRepeatItemBindingContext, INotifyPropertyChanged
    {
        public Row[] Rows { get; set; } = [new("same-key", "icon:old")];
        public bool Visible { get; set; } = true;
        public int RowsReads { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Notify(string? property) => PropertyChanged?.Invoke(this, new(property));
        public bool TryGetValue(string path, out object? value)
        {
            if (path == "Rows") { RowsReads++; value = Rows; return true; }
            if (path == "Visible") { value = Visible; return true; }
            value = null;
            return false;
        }
        public bool TryGetItemValue(object item, string path, out object? value)
        {
            value = item is Row row ? path switch { "Id" => row.Id, "Source" => row.Source, _ => null } : null;
            return item is Row && path is "Id" or "Source";
        }
        public bool TrySetItemValue(object item, string path, object? value) => false;
    }
}
