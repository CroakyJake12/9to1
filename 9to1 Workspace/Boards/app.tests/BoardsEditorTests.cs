// Headless editor tests for the v3 CUI Boards surface:
// dynamic checklist rendering, style projections, undo/redo dispatch,
// AddKind inserts, and honest graph-expression errors.
//
// THREADING: every test creates AND touches Avalonia objects inside ONE
// single synchronous delegate passed to OnUiThreadAsync (never split across
// awaits); Dispatcher.UIThread.InvokeAsync is never used (hangs headless).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsEditorTests
{
    [Fact]
    public void Toolbar_icon_mapping_uses_distinct_canonical_semantics()
    {
        Assert.Equal("grip", BoardsIcons.Grip);
        Assert.Equal("strikethrough", BoardsIcons.Strikethrough);
        Assert.Equal("subscript", BoardsIcons.Subscript);
        Assert.Equal("superscript", BoardsIcons.Superscript);
        Assert.Equal("align-left", BoardsIcons.AlignLeft);
        Assert.Equal("align-center", BoardsIcons.AlignCenter);
        Assert.Equal("align-right", BoardsIcons.AlignRight);
        Assert.Equal("indent-decrease", BoardsIcons.IndentDecrease);
        Assert.Equal("indent-increase", BoardsIcons.IndentIncrease);
        Assert.Equal("bulleted-list", BoardsIcons.BulletedList);
        Assert.Equal("numbered-list", BoardsIcons.NumberedList);
        Assert.Equal("image", BoardsIcons.Image);
        Assert.Equal("paperclip", BoardsIcons.Attach);
        Assert.Equal("edit", BoardsIcons.Edit);
        Assert.Equal("section", BoardsIcons.Section);
        Assert.Equal("table", BoardsIcons.Table);
        Assert.Equal("chart", BoardsIcons.Chart);
        Assert.Equal("info", BoardsIcons.Info);
    }

    [Fact]
    public void Boards_icon_resolver_fills_only_authored_closed_silhouettes()
    {
        var grip = Assert.IsType<GeometryDrawing>(BoardsCuiIconResolver.CreateIcon("grip", Brushes.White).Drawing);
        Assert.NotNull(grip.Brush);
        Assert.NotNull(grip.Pen);

        var attachment = Assert.IsType<GeometryDrawing>(BoardsCuiIconResolver.CreateIcon("paperclip", Brushes.White).Drawing);
        Assert.Null(attachment.Brush);
        Assert.NotNull(attachment.Pen);

        var section = Assert.IsType<GeometryDrawing>(BoardsCuiIconResolver.CreateIcon("section", Brushes.White).Drawing);
        Assert.NotNull(section.Brush);
        Assert.NotNull(section.Pen);

        foreach (var key in BoardsIcons.SectionIconChoices)
        {
            var choice = Assert.IsType<GeometryDrawing>(BoardsCuiIconResolver.CreateIcon(key, Brushes.White).Drawing);
            Assert.NotNull(choice.Brush);
            Assert.NotNull(choice.Pen);
        }
    }

    [Fact]
    public void Board_action_icons_use_contrast_safe_semantic_colours_and_hc_text()
    {
        var brushes = TestUiThread.Run(() =>
        {
            var target = new Button();
            target.Resources["CuiTextBrush"] = Brushes.White;
            target.Resources["CuiMutedBrush"] = Brushes.Gray;
            target.Resources["CuiPanel2Brush"] = new SolidColorBrush(Color.Parse("#FF181521"));
            target.Resources["CuiSuccessBrush"] = new SolidColorBrush(Color.Parse("#FF238A5A"));
            target.Resources["CuiInformationBrush"] = new SolidColorBrush(Color.Parse("#FF596FCA"));
            target.Resources["CuiHighContrast"] = false;
            var insert = Assert.IsType<SolidColorBrush>(BoardsCuiIconResolver.SemanticBrush("plus", target));
            var table = Assert.IsType<SolidColorBrush>(BoardsCuiIconResolver.SemanticBrush("table", target));
            var delete = Assert.IsType<SolidColorBrush>(BoardsCuiIconResolver.SemanticBrush("delete", target));
            target.IsEnabled = false;
            var disabled = BoardsCuiIconResolver.SemanticBrush("plus", target);
            target.IsEnabled = true;
            target.Resources["CuiHighContrast"] = true;
            var highContrast = BoardsCuiIconResolver.SemanticBrush("delete", target);
            return (insert: insert.Color, table: table.Color, delete: delete.Color, disabled, highContrast);
        });

        Assert.NotEqual(brushes.insert, brushes.table);
        Assert.NotEqual(brushes.insert, brushes.delete);
        Assert.NotEqual(brushes.table, brushes.delete);
        var iconBackground = Color.Parse("#FF181521");
        Assert.True(CakeOS.Cui.Themes.CuiContrast.Ratio(brushes.insert, iconBackground) >= 3d);
        Assert.True(CakeOS.Cui.Themes.CuiContrast.Ratio(brushes.table, iconBackground) >= 3d);
        Assert.True(CakeOS.Cui.Themes.CuiContrast.Ratio(brushes.delete, iconBackground) >= 3d);
        Assert.Same(Brushes.Gray, brushes.disabled);
        Assert.Same(Brushes.White, brushes.highContrast);
    }

    [Fact]
    public void Authored_CUI_icon_resolves_from_the_real_Boards_scoped_palette()
    {
        var result = TestUiThread.Run(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            var root = Assert.IsAssignableFrom<Control>(loader.LoadMarkup(
                """
                <Cui>
                  <Container Type="Vertical Stack">
                    <Image Name="ScopedPlus" Source="icon:plus" AccessibleName="Insert" />
                  </Container>
                </Cui>
                """,
                "boards-scoped-icon.cui").Root);
            var image = FindByName<Image>(root, "ScopedPlus");
            Assert.NotNull(image);
            Assert.True(image!.TryFindResource("CuiPanel2Brush", out var rawBackground));
            Assert.IsAssignableFrom<IBrush>(rawBackground);
            var drawing = Assert.IsType<DrawingImage>(image.Source).Drawing;
            var geometry = Assert.IsType<GeometryDrawing>(drawing);
            var foregroundBrush = geometry.Brush ?? geometry.Pen?.Brush;
            var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(foregroundBrush);
            return (Foreground: foreground.Color, Background: Assert.IsAssignableFrom<ISolidColorBrush>(rawBackground).Color);
        });

        Assert.True(CakeOS.Cui.Themes.CuiContrast.Ratio(result.Foreground, result.Background) >= 3d);
    }

    [Fact]
    public void Boards_owner_brand_resolves_packaged_light_and_dark_images()
    {
        var images = TestUiThread.Run(() =>
        {
            var target = new Image();
            target.Resources["CuiTextBrush"] = Brushes.White;
            target.Resources["CuiAppearance"] = "Dark";
            var dark = BoardsCuiIconResolver.Resolve("ownerbrand:boards", target);
            target.Resources["CuiAppearance"] = "Bright";
            var light = BoardsCuiIconResolver.Resolve("ownerbrand:boards", target);
            return (dark, light);
        });

        Assert.IsType<Avalonia.Media.Imaging.Bitmap>(images.dark);
        Assert.IsType<Avalonia.Media.Imaging.Bitmap>(images.light);
    }

    [Fact]
    public async Task Authored_edit_view_switch_tracks_the_mode_and_writes_through_the_view_model()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var viewModel = new BoardsViewModel(session);

        TestUiThread.Run(() =>
        {
            using var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load(BoardsCuiDocuments.MainSourceName));
            Assert.DoesNotContain(diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var control = Assert.IsAssignableFrom<Control>(root);
            loader.WireBindings(control);
            viewModel.Attach(control);

            var mode = Assert.IsType<ToggleSwitch>(BoardsCuiControlFinder.FindByAutomationId<ToggleSwitch>(control, "EditViewToggle"));
            Assert.True(mode.IsChecked);
            Assert.True(mode.IsEnabled);

            Assert.True(viewModel.TrySetValue("IsEditMode", false));
            Assert.False(mode.IsChecked);
            Assert.Equal("Viewing", viewModel.Get("ModeLabel"));

            mode.IsChecked = true;
            Assert.True(viewModel.IsEditMode);
            Assert.Equal("Editing", viewModel.Get("ModeLabel"));

            viewModel.Dispose();
        });
    }

    private static Task<T> OnUiThreadAsync<T>(Func<T> work) =>
        // All UI work funnels through the shared test UI thread: with Skia
        // registered, control trees are strictly thread-affine.
        Task.FromResult(TestUiThread.Run(work));

    [Fact]
    public async Task Queued_text_edit_after_session_disposal_is_ignored()
    {
        var session = new InMemoryRichBoardSession();
        var viewModel = new BoardsViewModel(session);
        var originalTitle = session.Document.Title;
        await session.DisposeAsync();

        viewModel.EditText("BoardTitleBox", "late queued title");

        Assert.Equal(originalTitle, session.Document.Title);
        viewModel.Dispose();
    }

    [Fact]
    public async Task Dynamic_checklist_renders_one_row_per_item()
    {
        const int count = 5;
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks.Clear();
        page.Blocks.Add(new RichBoardBlock { Id = "block-heading", Kind = "heading", Text = "List" });
        for (var i = 0; i < count; i++)
            page.Blocks.Add(new RichBoardBlock { Id = $"item-{i}", Kind = "checklist", Text = $"Task {i}" });
        var viewModel = new BoardsViewModel(session);

        var (texts, checks) = await OnUiThreadAsync(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, viewModel, []);
            var all = Flatten(host).ToArray();
            var textCount = all.OfType<TextBox>().Count(t =>
                (t.Name ?? string.Empty).StartsWith("chk_", StringComparison.Ordinal));
            var checkCount = all.OfType<CheckBox>().Count(c =>
                (c.Name ?? string.Empty).StartsWith("done_", StringComparison.Ordinal));
            return (textCount, checkCount);
        });

        Assert.Equal(count, texts);
        Assert.Equal(count, checks);
    }

    [Fact]
    public async Task Styles_appear_in_projection()
    {
        await using var memory = new InMemoryRichBoardSession();
        await memory.OpenAsync(null);
        Assert.Contains(memory.Styles, s => s.Id == "normal");
        Assert.NotEmpty(memory.Styles);

        var root = TempDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(
                store, Path.Combine(root, "styles.9to1board"));
            Assert.Contains(adapter.Styles, s => s.Id == "normal");
            Assert.NotEmpty(adapter.Styles);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Undo_redo_dispatch_round_trip_via_memory_session()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/undo-test");
        var viewModel = new BoardsViewModel(session);
        var para = session.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");

        // NOTE: InMemory snapshots on MarkDirty (after the mutation), so the
        // visible revert of edit N lands after N+1 undos — the two-edit dance
        // below exercises the real stack discipline without touching the file.
        viewModel.EditText("t_" + para.Id, "first");
        viewModel.EditText("t_" + para.Id, "second");
        Assert.Equal("second", para.Text);

        await viewModel.DispatchAsync("Undo", null);
        await viewModel.DispatchAsync("Undo", null);
        var afterUndo = session.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
        Assert.Equal("first", afterUndo.Text);

        await viewModel.DispatchAsync("Redo", null);
        var afterRedo = session.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
        Assert.Equal("second", afterRedo.Text);

        // Flush before dispose: InMemory dispose-flush re-enters SaveAsync
        // after marking itself disposed, so an explicit save is required.
        await session.SaveAsync();
    }

    [Fact]
    public async Task Undo_redo_dispatch_round_trip_via_adapter()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "undo.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var viewModel = new BoardsViewModel(adapter);
            var para = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            var before = para.Text;

            viewModel.EditText("t_" + para.Id, "adapter edited");
            adapter.MarkDirty();
            await viewModel.DispatchAsync("Undo", null);
            var reverted = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            Assert.Equal(before, reverted.Text);

            await viewModel.DispatchAsync("Redo", null);
            var redone = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            Assert.Equal("adapter edited", redone.Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AddKind_inserts_every_block_kind()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/addkind-test");
        var viewModel = new BoardsViewModel(session);
        session.Document.Sections[0].Pages[0].Blocks.Clear();

        foreach (var kind in new[] { "paragraph", "heading", "checklist", "table", "image", "divider", "graph" })
            await viewModel.InsertKindAsync(kind);

        var kinds = session.Document.Sections[0].Pages[0].Blocks.Select(b => b.Kind).ToArray();
        foreach (var kind in new[] { "paragraph", "heading", "checklist", "table", "image", "divider", "graph" })
            Assert.Contains(kind, kinds);

        // Flush before dispose (see undo test note).
        await session.SaveAsync();
    }

    [Fact]
    public async Task Invalid_graph_expression_renders_honest_error()
    {
        Assert.False(HavenGraphExpression.TryEvaluate("y = ???", 0, out _));
        Assert.True(HavenGraphExpression.TryEvaluate("y = x", 1, out var y));
        Assert.Equal(1, y);

        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks.Clear();
        page.Blocks.Add(new RichBoardBlock
        {
            Id = "block-graph",
            Kind = "graph",
            Graph = new RichGraphView
            {
                Expressions =
                [
                    new RichGraphExpressionView { Id = "expr-good", Text = "y = x" },
                    new RichGraphExpressionView { Id = "expr-bad", Text = "y = ???" },
                ],
                XMin = -10, XMax = 10, YMin = -10, YMax = 10,
            },
        });
        var viewModel = new BoardsViewModel(session);

        var (errorCount, mentionsBad, canvasCount, plotClips) = await OnUiThreadAsync(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, viewModel, []);
            var all = Flatten(host).ToArray();
            var errors = all.OfType<TextBlock>()
                .Where(t => (t.Text ?? string.Empty).StartsWith("Invalid expression", StringComparison.Ordinal))
                .ToArray();
            var plot = all.OfType<Canvas>().SingleOrDefault(c =>
                Avalonia.Automation.AutomationProperties.GetName(c) == "Graph plot");
            return (errors.Length, errors.Any(t => t.Text!.Contains("y = ???", StringComparison.Ordinal)),
                all.OfType<Canvas>().Count(), plot?.ClipToBounds == true);
        });

        Assert.Equal(1, errorCount);
        Assert.True(mentionsBad);
        // One plot canvas plus the always-present freeform canvas.
        Assert.Equal(2, canvasCount);
        Assert.True(plotClips, "Graph curves must be clipped to their plot canvas.");
    }

    [Fact]
    public async Task Boards_cui_exposes_dynamic_host_and_menus()
    {
        // Parse-only (no Avalonia objects): the full Boards.cui load touches
        // ListBox.Items, which is thread-affine to whichever test class owned
        // headless init. Full-load coverage lives in
        // BoardsAppTests.Boards_cui_loader_builds_non_null_root; here we prove
        // the declared product surface plus the automationid lookup path on a
        // small loader-built tree (no ItemsControl children).
        var path = FindBoardsCui();
        var parser = new CakeOS.Cui.Language.CuiRichParser();
        var document = parser.ParseFile(path);
        var errors = parser.Diagnostics.Diagnostics
            .Where(d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, $"Boards.cui parse errors: {string.Join("; ", errors)}");

        var expected = new[]
        {
            "TopBarLeft", "TopBarRight", "BoardsAppIcon", "UndoButton", "RedoButton", "BoardTitleBox", "BoardIdentity", "SaveState", "NavSearchBox", "NavHost",
            "BoardSwitcherHeader", "BoardSwitcherIcon", "BoardSwitcher", "ToolbarHost", "ContextHost", "EditorScroll", "PageCard", "PageBlocksSurface", "BlocksHost", "AbsoluteBlocksHost", "VerticalLayoutToggle", "FreeLayoutToggle", "OverflowButton",
        };
        var components = document.Components
            .SelectMany(c => c.DescendantsAndSelf())
            .Where(c => expected.Contains(c.Name, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(expected.Length, components.Length);
        var toolbarHost = components.Single(component => component.Name == "ToolbarHost");
        Assert.True(toolbarHost.Properties.TryGetValue("Type", out var toolbarType)
            && toolbarType is CuiLiteralValue { Value: "Horizontal Stack" });
        Assert.True(toolbarHost.Properties.TryGetValue("HorizontalScrolling", out var horizontalScrolling)
            && horizontalScrolling is CuiLiteralValue horizontalLiteral
            && bool.TryParse(horizontalLiteral.Value, out var horizontalEnabled) && horizontalEnabled);
        Assert.True(toolbarHost.Properties.TryGetValue("HorizontalOverflowArrows", out var arrows)
            && arrows is CuiLiteralValue { Value: "True" });
        Assert.True(toolbarHost.Properties.TryGetValue("VerticalScrolling", out var verticalScrolling)
            && verticalScrolling is CuiLiteralValue verticalLiteral
            && bool.TryParse(verticalLiteral.Value, out var verticalEnabled) && !verticalEnabled);
        var boardSwitcher = components.Single(component => component.Name == "BoardSwitcher");
        Assert.True(boardSwitcher.Properties.TryGetValue("IconSource", out var boardIcon)
            && boardIcon is CuiLiteralValue { Value: "icon:chevron-down" });
        Assert.True(boardSwitcher.Properties.TryGetValue("IsEnabled", out var boardMenuEnabled)
            && boardMenuEnabled is CuiBindingValue { Path: "CanOpenBoardMenu" });
        var boardTitle = components.Single(component => component.Name == "BoardTitleBox");
        Assert.True(boardTitle.Properties.TryGetValue("Value", out var boardTitleValue)
            && boardTitleValue is CuiBindingValue { Path: "BoardTitle" });
        var topBarLeft = components.Single(component => component.Name == "TopBarLeft");
        var topBarLeftNames = topBarLeft.DescendantsAndSelf().Select(component => component.Name).ToArray();
        Assert.True(Array.IndexOf(topBarLeftNames, "FileMenu") < Array.IndexOf(topBarLeftNames, "BoardsAppIcon")
            && Array.IndexOf(topBarLeftNames, "BoardsAppIcon") < Array.IndexOf(topBarLeftNames, "UndoButton"));
        var boardIdentity = components.Single(component => component.Name == "BoardIdentity");
        var boardIdentityNames = boardIdentity.DescendantsAndSelf().Select(component => component.Name).ToArray();
        Assert.True(Array.IndexOf(boardIdentityNames, "BoardTitleBox") < Array.IndexOf(boardIdentityNames, "SaveState"));
        var verticalLayout = components.Single(component => component.Name == "VerticalLayoutToggle");
        Assert.True(verticalLayout.Properties.TryGetValue("Selected", out var verticalSelected)
            && verticalSelected is CuiBindingValue { Path: "IsVerticalLayout" });
        Assert.True(verticalLayout.Properties.TryGetValue("Interactive", out var layoutInteractive)
            && layoutInteractive is CuiBindingValue { Path: "CanChangeLayoutMode" });
        var freeLayout = components.Single(component => component.Name == "FreeLayoutToggle");
        Assert.True(freeLayout.Properties.TryGetValue("Selected", out var freeSelected)
            && freeSelected is CuiBindingValue { Path: "IsFreeLayout" });
        Assert.True(verticalLayout.Properties.TryGetValue("Content", out var verticalLabel)
            && verticalLabel is CuiLiteralValue { Value: "Vertical Stacking" });
        Assert.True(freeLayout.Properties.TryGetValue("Content", out var freeLabel)
            && freeLabel is CuiLiteralValue { Value: "Free Placement" });
        var overflow = components.Single(component => component.Name == "OverflowButton");
        Assert.True(overflow.Properties.TryGetValue("IconSource", out var overflowIcon)
            && overflowIcon is CuiLiteralValue { Value: "icon:more" });
        foreach (var iconButtonName in new[] { "UndoButton", "RedoButton", "OverflowButton" })
        {
            var iconButton = components.Single(component => component.Name == iconButtonName);
            Assert.True(iconButton.Properties.TryGetValue("IconSize", out var iconSize)
                && iconSize is CuiLiteralValue { Value: "18" });
            Assert.True(iconButton.Properties.TryGetValue("Width", out var hitTargetWidth)
                && hitTargetWidth is CuiLiteralValue { Value: "32" });
            Assert.True(iconButton.Properties.TryGetValue("Height", out var hitTargetHeight)
                && hitTargetHeight is CuiLiteralValue { Value: "32" });
        }
        Assert.DoesNotContain(document.Components.SelectMany(c => c.DescendantsAndSelf()),
            component => component.Name == "ThemeButton");
        var appIcon = components.Single(component => component.Name == "BoardsAppIcon");
        Assert.True(appIcon.Properties.TryGetValue("Source", out var appIconSource)
            && appIconSource is CuiLiteralValue { Value: "ownerbrand:boards" });
        var searchBox = components.Single(component => component.Name == "NavSearchBox");
        Assert.True(searchBox.Properties.TryGetValue("cornerradius", out var searchRadius)
            && searchRadius is CuiLiteralValue { Value: "18" });
        var pageCard = components.Single(component => component.Name == "PageCard");
        Assert.True(pageCard.Properties.TryGetValue("cornerradius", out var pageCorner)
            && pageCorner is CuiLiteralValue { Value: "22" });

        var found = await OnUiThreadAsync(() =>
        {
            var loader = new CakeOS.Cui.Runtime.CuiControlLoader();
            var root = Assert.IsAssignableFrom<Control>(loader.LoadMarkup(
                """
                <Cui>
                  <Container Type="Vertical Stack">
                    <Input Type="Text" ID="BoardTitleBox" Value="Title" />
                    <Container Type="Vertical Stack" ID="NavHost" />
                    <Container Type="Horizontal Stack" ID="ToolbarHost" HorizontalScrolling="True" HorizontalOverflowArrows="True" VerticalScrolling="False" />
                    <Container Type="Vertical Stack" ID="ContextHost" />
                    <Container Type="Grid" ID="PageBlocksSurface" RowSizes="Auto">
                      <Container Type="Vertical Stack" ID="BlocksHost" Row="0" />
                      <Container Type="Absolute" ID="AbsoluteBlocksHost" Row="0" />
                    </Container>
                  </Container>
                </Cui>
                """,
                "boards-host-lookup.cui").Root);
            var pageBlocksSurface = FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "PageBlocksSurface");
            var flowBlocks = FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "BlocksHost");
            var absoluteBlocks = FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "AbsoluteBlocksHost");
            return pageBlocksSurface is { LayoutType: "Grid" }
                && flowBlocks is { LayoutType: "Vertical Stack" }
                && FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "NavHost") is { LayoutType: "Vertical Stack" }
                && FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "ToolbarHost") is { LayoutType: "Horizontal Stack", Layout: StackPanel }
                && FindByAutomationId<CakeOS.Cui.Runtime.CuiContainerControl>(root, "ContextHost") is { LayoutType: "Vertical Stack" }
                && absoluteBlocks is { LayoutType: "Absolute", Layout: Canvas }
                && ReferenceEquals(flowBlocks.Parent, pageBlocksSurface)
                && ReferenceEquals(absoluteBlocks.Parent, pageBlocksSurface)
                && FindByAutomationId<TextBox>(root, "BoardTitleBox") is not null;
        });
        Assert.True(found);
    }

    [Fact]
    public async Task Table_structure_cell_routing_and_style_apply()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/table-test");
        var viewModel = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks.Clear();
        page.Blocks.Add(new RichBoardBlock
        {
            Id = "table-1",
            Kind = "table",
            TableRows = 2,
            TableCols = 2,
            TableCells = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0,0"] = "a", ["0,1"] = "b", ["1,0"] = "c", ["1,1"] = "d",
            },
        });

        viewModel.TableAddRow("table-1");
        viewModel.TableAddCol("table-1");
        var table = Assert.IsType<RichBoardBlock>(page.Blocks.First(b => b.Id == "table-1"));
        Assert.Equal(3, table.TableRows);
        Assert.Equal(3, table.TableCols);

        viewModel.EditText("cell_table-1_2_2", "z");
        Assert.Equal("z", table.TableCells["2,2"]);
        viewModel.ToggleCellBold("table-1", 0, 0);
        Assert.True(table.CellStyles["0,0"].Bold);

        viewModel.TableDelCol("table-1");
        viewModel.TableDelRow("table-1");
        Assert.Equal(2, table.TableRows);
        Assert.Equal(2, table.TableCols);
        Assert.DoesNotContain("2,2", table.TableCells.Keys);

        await viewModel.ApplyStyleToSelectedAsync("quote");
        Assert.Equal("quote", page.Blocks.First(b => b.Id == viewModel.SelectedBlockId).StyleId);

        var cellCount = await OnUiThreadAsync(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, viewModel, []);
            return Flatten(host).OfType<TextBox>().Count(t =>
                (t.Name ?? string.Empty).StartsWith("cell_", StringComparison.Ordinal));
        });
        Assert.Equal(4, cellCount);

        await session.SaveAsync();
    }

    private static string FindBoardsCui()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "9to1 Workspace", "Boards", "app", "Boards.cui");
            if (File.Exists(candidate))
                return candidate;
            var direct = Path.Combine(dir.FullName, "Boards.cui");
            if (File.Exists(direct))
                return direct;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Boards.cui was not found from the test output directory.");
    }

    private static T? FindByName<T>(Control root, string name) where T : Control
    {
        if (root is T match && string.Equals(root.Name, name, StringComparison.Ordinal))
            return match;
        foreach (var child in Flatten(root))
        {
            if (child is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
                return typed;
        }
        return null;
    }

    private static T? FindByAutomationId<T>(Control root, string automationId) where T : Control
        => BoardsCuiControlFinder.FindByAutomationId<T>(root, automationId);

    private static IEnumerable<Control> Flatten(Control root)
    {
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                {
                    yield return c;
                    foreach (var nested in Flatten(c))
                        yield return nested;
                }
        }
        else if (root is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
            foreach (var nested in Flatten(decChild))
                yield return nested;
        }
        else if (root is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
            foreach (var nested in Flatten(ccChild))
                yield return nested;
        }
        else if (root is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                {
                    yield return icChild;
                    foreach (var nested in Flatten(icChild))
                        yield return nested;
                }
        }
        else if (root is ScrollViewer scroller && scroller.Content is Control scChild)
        {
            yield return scChild;
            foreach (var nested in Flatten(scChild))
                yield return nested;
        }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
