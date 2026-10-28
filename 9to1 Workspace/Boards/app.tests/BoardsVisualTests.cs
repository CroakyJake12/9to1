// Product-surface tests for the visual pass: toolbar availability, style
// gallery breadth, contextual panels, id-free rendered text, insert coverage,
// empty states, save-state mapping and theme resolution.
//
// THREADING: same single-delegate discipline as the other suites — everything
// UI-related runs inside one TestUiThread.Run delegate per test.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsVisualTests
{
    private static T Ui<T>(Func<T> work) => TestUiThread.Run(work);

    private static void Ui(Action work) => TestUiThread.Run(work);

    private static BoardsViewModel MemoryModel(out InMemoryRichBoardSession session)
    {
        InMemoryRichBoardSession.ClearStore();
        session = new InMemoryRichBoardSession();
        session.OpenAsync("memory://boards/visual-test").GetAwaiter().GetResult();
        return new BoardsViewModel(session);
    }

    private static List<Control> Flatten(Control root)
    {
        var result = new List<Control> { root };
        foreach (var child in LogicalChildren(root))
            result.AddRange(Flatten(child));
        return result;
    }

    private static IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
        else if (control is ScrollViewer scroller && scroller.Content is Control scChild)
        {
            yield return scChild;
        }
    }

    private static List<string> VisibleTexts(Control root) =>
        Flatten(root)
            .SelectMany<Control, string>(control => control switch
            {
                TextBlock text => [text.Text ?? string.Empty],
                TextBox box => [box.Text ?? string.Empty],
                Button button when button.Content is string label => [label],
                _ => [],
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

    [Fact]
    public async Task Toolbar_exposes_every_command_group()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var found = Ui(() =>
            {
                var host = new StackPanel();
                ToolbarBuilder.Rebuild(host, vm);
                return Flatten(host)
                    .Select(c => Avalonia.Automation.AutomationProperties.GetName(c))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToHashSet(StringComparer.Ordinal);
            });
            foreach (var command in new[]
            {
                "Paragraph style",
                "Subscript", "Superscript", "Font family", "Font size",
                "Text colour", "Highlight colour", "Bulleted list", "Numbered list",
                "Align left", "Align center", "Align right",
                "Decrease indent", "Increase indent", "Insert",
            })
                Assert.Contains(command, found);

            var authoredTextCommands = Ui(() =>
            {
                using var loader = BoardsCuiDocuments.CreateLoader();
                loader.SetBindingContext(vm);
                loader.SetActionDispatcher(vm);
                var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var authoredRoot = Assert.IsAssignableFrom<Control>(root);
                loader.WireBindings(authoredRoot);
                vm.Attach(authoredRoot);
                var ids = new[] { "RangeBold", "RangeItalic", "RangeUnderline", "RangeStrike" };
                var foundIds = ids.Where(id => BoardsCuiControlFinder.FindByAutomationId<Button>(authoredRoot, id) is not null).ToArray();
                vm.Detach();
                return foundIds;
            });
            Assert.Equal(new[] { "RangeBold", "RangeItalic", "RangeUnderline", "RangeStrike" }, authoredTextCommands);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Style_gallery_lists_builtin_and_custom_styles()
    {
        var root = TempDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(
                store, Path.Combine(root, "styles.9to1board"));
            await adapter.CreateCustomStyleAsync("Key Case");
            var vm = new BoardsViewModel(adapter);
            var names = Ui(() =>
            {
                var host = new StackPanel();
                ToolbarBuilder.Rebuild(host, vm);
                return Flatten(host)
                    .OfType<ComboBox>()
                    .SelectMany(combo => combo.ItemsSource?.Cast<object>() ?? [])
                    .OfType<ComboBoxItem>()
                    .Select(item => item.Content is TextBlock text ? text.Text ?? string.Empty : string.Empty)
                    .Where(text => !string.IsNullOrEmpty(text))
                    .ToList();
            });
            foreach (var expected in new[]
            {
                "Paragraph", "Title", "Subtitle", "Header 1", "Header 2", "Header 3", "Header 4", "Header 5", "Header 6",
                "Quote", "Code", "Key Case",
            })
                Assert.Contains(expected, names);
            await adapter.DisposeAsync();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Context_panels_follow_selection()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            var table = new RichBoardBlock { Id = "ctx-table", Kind = "table", TableRows = 2, TableCols = 2 };
            var image = new RichBoardBlock
            {
                Id = "ctx-image",
                Kind = "image",
                Image = new RichImageView { DisplayName = "a.bmp", MediaType = "image/bmp" },
            };
            var para = new RichBoardBlock { Id = "ctx-para", Kind = "paragraph", Text = "Plain." };
            page.Blocks.Add(table);
            page.Blocks.Add(image);
            page.Blocks.Add(para);

            var tablePanel = Ui(() =>
            {
                vm.FocusBlock("ctx-table");
                var host = new StackPanel();
                ContextPanels.Rebuild(host, vm);
                return Flatten(host)
                    .Select(c => Avalonia.Automation.AutomationProperties.GetName(c))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            });
            Assert.Contains("Add table row", tablePanel);
            Assert.Contains("Cell horizontal alignment", tablePanel);

            var imagePanel = Ui(() =>
            {
                vm.FocusBlock("ctx-image");
                var host = new StackPanel();
                ContextPanels.Rebuild(host, vm);
                return Flatten(host)
                    .Select(c => Avalonia.Automation.AutomationProperties.GetName(c))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            });
            Assert.Contains("Image alignment", imagePanel);
            Assert.Contains("Replace image from a file", imagePanel);

            var calm = Ui(() =>
            {
                vm.FocusBlock("ctx-para");
                var host = new StackPanel();
                ContextPanels.Rebuild(host, vm);
                return host.Children.Count;
            });
            Assert.Equal(0, calm);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Merged_table_renders_one_expanded_editor_and_hides_covered_slots()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var block = new RichBoardBlock
            {
                Id = "merged-table",
                Kind = "table",
                TableRows = 2,
                TableCols = 2,
                HasMergedCells = true,
                TableStyle = new RichTableStyleView
                {
                    Width = 420,
                    Alignment = "center",
                    HasHeaderRow = true
                },
                TableCells = new Dictionary<string, string>
                {
                    ["0,0"] = "Merged title", ["0,1"] = string.Empty,
                    ["1,0"] = string.Empty, ["1,1"] = string.Empty
                },
                CellSpans = new Dictionary<string, RichTableCellSpanView>
                {
                    ["0,0"] = new() { RowSpan = 2, ColumnSpan = 2 },
                    ["0,1"] = new() { CoveredByCellId = "cell-anchor" },
                    ["1,0"] = new() { CoveredByCellId = "cell-anchor" },
                    ["1,1"] = new() { CoveredByCellId = "cell-anchor" }
                }
            };
            session.Document.Sections[0].Pages[0].Blocks.Clear();
            session.Document.Sections[0].Pages[0].Blocks.Add(block);
            var result = Ui(() =>
            {
                var host = new StackPanel();
                BlockRenderer.Rebuild(host, vm, []);
                var grid = Flatten(host).OfType<Grid>().Single(candidate =>
                    candidate.RowDefinitions.Count == 2 && candidate.ColumnDefinitions.Count == 2);
                var frame = Assert.Single(grid.Children);
                var tableFrame = Flatten(host).OfType<Border>().Single(candidate =>
                    candidate.Name == "BoardTableFrame_" + block.Id);
                var editor = Assert.Single(Flatten(frame).OfType<TextBox>());
                Assert.Contains(Flatten(host).OfType<TextBox>(), box => box.Text == "Merged title");
                // UX27-BOARD-029/045 and UX27-TEST-032 (owner report C05): a Native flow page is
                // one seamless surface. The removed auto-appended "Freeform canvas" empty state
                // and its "+ Box" affordance must not come back as a side effect of a table.
                Assert.DoesNotContain(Flatten(host).OfType<Button>(), button => Equals(button.Content, "+ Box"));
                Assert.DoesNotContain(Flatten(host).OfType<TextBlock>(), text =>
                    (text.Text ?? string.Empty).Contains("Freeform canvas", StringComparison.Ordinal));
                return (Host: host, TableFrame: tableFrame, RowSpan: Grid.GetRowSpan(frame), ColumnSpan: Grid.GetColumnSpan(frame),
                    TableWidth: tableFrame.Width, TableAlignment: tableFrame.HorizontalAlignment,
                    Text: editor.Text, Weight: editor.FontWeight);
            });
            Assert.Equal(2, result.RowSpan);
            Assert.Equal(2, result.ColumnSpan);
            Assert.Equal(420, result.TableWidth);
            Assert.Equal(HorizontalAlignment.Center, result.TableAlignment);
            Assert.Equal("Merged title", result.Text);
            Assert.Equal(FontWeight.Bold, result.Weight);
            // This renderer intentionally appends the freeform surface after
            // native blocks, so the full editor contains both regions. The
            // component image below isolates the real table frame for review.
            // Capture the actual rendered table frame. The full Board editor
            // also contains the freeform canvas, which can otherwise dominate
            // a small headless viewport and hide the table under test.
            Ui(() =>
            {
                const int width = 960;
                const int height = 560;
                var originalParent = result.TableFrame.Parent as Panel;
                Assert.NotNull(originalParent);
                Assert.True(originalParent!.Children.Remove(result.TableFrame));
                var window = new Window
                {
                    Width = width,
                    Height = height,
                    Background = Brushes.White,
                    Content = result.TableFrame,
                };
                window.Show();
                window.Measure(new Size(width, height));
                window.Arrange(new Rect(0, 0, width, height));
                result.TableFrame.Measure(new Size(width, height));
                result.TableFrame.Arrange(new Rect(0, 0, width, height));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Contains(result.TableFrame.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "Merged title");
                var shot = Path.Combine(VisualCaptureTests.ShotsDirectory(), "merged-table-component.png");
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(shot);
                window.Close();
            });
        }
        finally
        {
            Ui(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
            vm.Dispose();
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Table_border_uses_active_accent_by_default_and_preserves_explicit_color_with_even_clip_radius()
    {
        var priorMode = Ui(() => BoardsTheme.Mode);
        var priorTheme = Ui(() => CuiSurfacePaletteCatalog.ActiveTheme);
        var vm = MemoryModel(out var session);
        try
        {
            Ui(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
                BoardsTheme.SetMode(BoardsThemeMode.Dark);
                var page = session.Document.Sections[0].Pages[0];
                page.Blocks.Clear();
                page.Blocks.Add(new RichBoardBlock
                {
                    Id = "default-accent-table", Kind = "table", TableRows = 1, TableCols = 1,
                    TableStyle = new RichTableStyleView { CornerRadius = 8, BorderWidth = 2 },
                });
                page.Blocks.Add(new RichBoardBlock
                {
                    Id = "explicit-color-table", Kind = "table", TableRows = 1, TableCols = 1,
                    TableStyle = new RichTableStyleView
                    {
                        CornerRadius = 8, BorderWidth = 2, BorderColor = "#FF4A6FA5",
                    },
                });
            });

            var actual = Ui(() =>
            {
                var host = new StackPanel { Width = 680 };
                BlockRenderer.Rebuild(host, vm, []);
                var window = new Window { Width = 700, Height = 500, Content = host };
                window.Show();
                window.Measure(new Size(700, 500));
                window.Arrange(new Rect(0, 0, 700, 500));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                static Color[] Colors(IBrush? brush) => brush switch
                {
                    ISolidColorBrush solid => [solid.Color],
                    LinearGradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color).ToArray(),
                    _ => [],
                };

                var defaultFrame = Assert.Single(host.GetVisualDescendants().OfType<Border>(), border =>
                    border.Name == "BoardTableFrame_default-accent-table");
                var defaultClip = Assert.IsType<Border>(defaultFrame.Child);
                var accentColors = Colors(BoardsTheme.AccentBrush);
                var defaultColors = Colors(defaultFrame.BorderBrush);
                var explicitFrame = Assert.Single(host.GetVisualDescendants().OfType<Border>(), border =>
                    border.Name == "BoardTableFrame_explicit-color-table");
                var explicitColor = Assert.IsType<SolidColorBrush>(explicitFrame.BorderBrush).Color;
                window.Close();
                return (AccentColors: accentColors, DefaultColors: defaultColors, ExplicitColor: explicitColor,
                    OuterRadius: defaultFrame.CornerRadius.TopLeft, InnerRadius: defaultClip.CornerRadius.TopLeft,
                    BorderWidth: defaultFrame.BorderThickness.Left, InnerClips: defaultClip.ClipToBounds);
            });

            Assert.NotEmpty(actual.AccentColors);
            Assert.Equal(actual.AccentColors, actual.DefaultColors);
            Assert.Equal(Color.Parse("#FF4A6FA5"), actual.ExplicitColor);
            Assert.Equal(8, actual.OuterRadius);
            Assert.Equal(actual.OuterRadius - actual.BorderWidth, actual.InnerRadius);
            Assert.True(actual.InnerClips);
        }
        finally
        {
            vm.Dispose();
            await session.DisposeAsync();
            Ui(() =>
            {
                CuiSurfacePaletteCatalog.ActiveTheme = priorTheme;
                BoardsTheme.SetMode(priorMode);
            });
        }
    }

    [Fact]
    public async Task Rendered_page_shows_no_internal_ids()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new RichBoardBlock { Id = "block-abc123def456", Kind = "heading", Text = "Hello", StyleId = "heading-1" });
            page.Blocks.Add(new RichBoardBlock { Id = "block-999888777666", Kind = "paragraph", Text = "World" });
            page.Blocks.Add(new RichBoardBlock { Id = "block-111222333444", Kind = "checklist", Text = "Done" });

            var texts = Ui(() =>
            {
                var host = new StackPanel();
                BlockRenderer.Rebuild(host, vm, []);
                return VisibleTexts(host);
            });
            Assert.Contains("Hello", texts);
            Assert.Contains("World", texts);
            foreach (var text in texts)
            {
                Assert.DoesNotContain("block-", text, StringComparison.Ordinal);
                Assert.DoesNotContain("memory://", text, StringComparison.Ordinal);
                Assert.DoesNotContain(".9to1board", text, StringComparison.Ordinal);
            }
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Insert_menu_covers_every_documented_kind()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/insert-kinds");
        var vm = new BoardsViewModel(session);
        foreach (var tag in new[]
        {
            "paragraph", "heading", "checklist", "bulleted", "numbered",
            "table", "graph", "image", "divider", "style:quote", "paragraph", "style:code",
            "attachment", "freeform",
        })
            await vm.InsertKindAsync(tag);
        var kinds = session.Document.Sections[0].Pages[0].Blocks.Select(b => b.Kind).ToList();
        Assert.Contains("paragraph", kinds);
        Assert.Contains("heading", kinds);
        Assert.Contains("table", kinds);
        Assert.Contains("graph", kinds);
        Assert.Contains("image", kinds);
        Assert.Contains("divider", kinds);
        Assert.True(kinds.Count(b => b == "checklist") >= 3);
        var styled = session.Document.Sections[0].Pages[0].Blocks
            .Where(b => b.StyleId is "quote" or "code")
            .ToList();
        Assert.Equal(2, styled.Count);
        Assert.DoesNotContain(kinds, kind => kind == "ink");
    }

    [Fact]
    public async Task Colour_and_drawing_controls_show_vector_icons_instead_of_placeholder_letters()
    {
        var vm = MemoryModel(out var session);
        try
        {
            Ui(() =>
            {
                var host = new StackPanel();
                ToolbarBuilder.Rebuild(host, vm);
                foreach (var name in new[] { "Text colour", "Highlight colour", "Draw" })
                {
                    var button = Assert.Single(Flatten(host).OfType<Button>(), candidate =>
                        Avalonia.Automation.AutomationProperties.GetName(candidate) == name);
                    var image = Assert.Single(Flatten(button).OfType<Image>());
                    Assert.IsType<DrawingImage>(image.Source);
                    Assert.Equal(18d, image.Width);
                }
                Assert.DoesNotContain("H", VisibleTexts(host));
            });
            await vm.ActivateDrawingAsync();
            Ui(() =>
            {
                var host = new StackPanel();
                ToolbarBuilder.Rebuild(host, vm);
                foreach (var tool in new[] { "Pen", "Highlighter", "Eraser", "Select" })
                {
                    var button = Assert.Single(Flatten(host).OfType<Avalonia.Controls.Primitives.ToggleButton>(),
                        candidate => Avalonia.Automation.AutomationProperties.GetName(candidate) == tool + " drawing tool");
                    var image = Assert.IsType<Image>(button.Content);
                    var drawing = Assert.IsType<GeometryDrawing>(Assert.IsType<DrawingImage>(image.Source).Drawing);
                    Assert.NotNull(drawing.Brush);
                    Assert.Equal(18d, image.Width);
                    Assert.True(button.MinWidth >= 32d);
                    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(tool, vm.InkTool);
                }
            });
        }
        finally
        {
            Ui(vm.Dispose);
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Draw_mode_is_a_first_class_page_tool_not_a_content_block()
    {
        var vm = MemoryModel(out var session);
        try
        {
            await vm.ActivateDrawingAsync();
            var commands = Ui(() =>
            {
                var host = new StackPanel();
                ToolbarBuilder.Rebuild(host, vm);
                return Flatten(host)
                    .Select(Avalonia.Automation.AutomationProperties.GetName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToHashSet(StringComparer.Ordinal);
            });
            Assert.True(vm.IsDrawMode);
            foreach (var expected in new[]
            {
                "Draw", "Pen drawing tool", "Highlighter drawing tool", "Eraser drawing tool",
                "Select drawing tool", "Black ink", "Stroke width 2", "Undo drawing",
                "Redo drawing", "Zoom drawing out", "Zoom drawing in", "Reset drawing view",
            })
                Assert.Contains(expected, commands);
            Assert.DoesNotContain(session.Document.Sections[0].Pages[0].Blocks, block => block.Kind == "ink");
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Empty_states_read_as_product_copy()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            var texts = Ui(() =>
            {
                var host = new StackPanel();
                BlockRenderer.Rebuild(host, vm, []);
                return VisibleTexts(host);
            });
            Assert.Contains(texts, t => t.Contains("Start typing", StringComparison.Ordinal));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public void Save_state_mapping_is_product_language()
    {
        Assert.StartsWith("Saved", BoardsViewModel.MapSaveState("Saved 10:24:11"), StringComparison.Ordinal);
        Assert.Equal("Saved", BoardsViewModel.MapSaveState(string.Empty));
        Assert.Equal("Save failed", BoardsViewModel.MapSaveState("Save failed: disk full"));
        Assert.Equal("Save failed", BoardsViewModel.MapSaveState("Autosave failed: busy"));
        Assert.Equal("Saving…", BoardsViewModel.MapSaveState("Saving…"));
        Assert.Equal("Unsaved changes", BoardsViewModel.MapSaveState("Editing…"));
        Assert.Equal("Loaded locally", BoardsViewModel.MapSaveState("Loaded locally"));
    }

    [Fact]
    public void Theme_toggle_flips_semantic_roles()
    {
        // Brushes are AvaloniaObjects: create them on the shared UI thread so
        // this test can never steal Dispatcher.UIThread ownership from it.
        TestUiThread.Run(() =>
        {
            var prior = BoardsTheme.Mode;
            try
            {
                BoardsTheme.SetMode(BoardsThemeMode.Light);
                var lightText = BoardsTheme.Current.Text;
                var lightBg = BoardsTheme.Current.AppBackground;
                BoardsTheme.SetMode(BoardsThemeMode.Dark);
                Assert.NotEqual(lightText, BoardsTheme.Current.Text);
                Assert.NotEqual(lightBg, BoardsTheme.Current.AppBackground);
                Assert.NotNull(BoardsTheme.TextBrush);
                Assert.NotNull(BoardsTheme.AccentBrush);
                Assert.NotNull(BoardsTheme.ErrorBrush);
            }
            finally
            {
                BoardsTheme.SetMode(prior);
            }
        });
    }

    [Fact]
    public void Boards_loads_the_shared_theme_while_retaining_its_appearance_toggle()
    {
        TestUiThread.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "boards-theme-test-" + Guid.NewGuid().ToString("N"));
            var priorMode = BoardsTheme.Mode;
            var priorTheme = CakeOS.Cui.Themes.CuiSurfacePaletteCatalog.ActiveTheme;
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "preferences.json"), """{"havenUiThemeName":"Bubble"}""");
                BoardsTheme.LoadGlobalThemePreference(directory);
                BoardsTheme.SetMode(BoardsThemeMode.Light);
                Assert.Equal(CakeOS.Cui.Themes.CuiTheme.Bubble, BoardsTheme.SharedPalette.Theme);
                var light = BoardsTheme.Current.AppBackground;
                BoardsTheme.SetMode(BoardsThemeMode.Dark);
                Assert.Equal(CakeOS.Cui.Themes.CuiTheme.Bubble, BoardsTheme.SharedPalette.Theme);
                Assert.NotEqual(light, BoardsTheme.Current.AppBackground);
            }
            finally
            {
                BoardsTheme.SetMode(priorMode);
                CakeOS.Cui.Themes.CuiSurfacePaletteCatalog.ActiveTheme = priorTheme;
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void Authored_light_highlights_keep_dark_text_in_dark_appearance()
    {
        TestUiThread.Run(() =>
        {
            var prior = BoardsTheme.Mode;
            try
            {
                BoardsTheme.SetMode(BoardsThemeMode.Dark);
                var foreground = Assert.IsType<Avalonia.Media.SolidColorBrush>(
                    BoardsTheme.ContrastText("#FFFFF3C4"));
                Assert.Equal(Avalonia.Media.Color.Parse("#FF212121"), foreground.Color);
            }
            finally
            {
                BoardsTheme.SetMode(prior);
            }
        });
    }

    [Fact]
    public async Task Selection_state_tracks_focus()
    {
        var vm = MemoryModel(out var session);
        try
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new RichBoardBlock { Id = "sel-a", Kind = "paragraph", Text = "A" });
            page.Blocks.Add(new RichBoardBlock { Id = "sel-b", Kind = "paragraph", Text = "B" });
            var fired = 0;
            vm.SelectionChanged += () => fired++;
            vm.FocusBlock("sel-b");
            Assert.Equal("sel-b", vm.SelectedBlockId);
            Assert.Equal(1, fired);
            vm.FocusBlock("missing");
            Assert.Equal("sel-b", vm.SelectedBlockId);
            Assert.Equal(1, fired);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-visual-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}



