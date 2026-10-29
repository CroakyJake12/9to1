// Headless tests for the Boards CUI app:
// 1. Boards.cui parses with 0 errors.
// 2. CuiControlLoader builds a non-null root.
// 3. Simulated edit -> save -> dispose -> reopen via IRichBoardSession preserves content.

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsAppTests
{
    private static Task<T> OnUiThreadAsync<T>(Func<T> work) =>
        // All UI work funnels through the shared test UI thread: with Skia
        // registered, control trees are strictly thread-affine.
        Task.FromResult(TestUiThread.Run(work));

    private static Task OnUiThreadAsync(Action work)
    {
        TestUiThread.Run(work);
        return Task.CompletedTask;
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

    [Fact]
    public void Boards_authored_views_load_from_the_compiled_embedded_cui_transport()
    {
        foreach (var source in new[]
        {
            BoardsCuiDocuments.MainSourceName,
            BoardsCuiDocuments.GraphSlidersSourceName,
            BoardsCuiDocuments.BlockGripSourceName,
            BoardsCuiDocuments.BlockActionsMenuSourceName,
            BoardsCuiDocuments.BoardPropertiesSourceName,
            BoardsCuiDocuments.StartupFailureSourceName,
        })
        {
            var document = BoardsCuiDocuments.Load(source);
            Assert.Equal(source, document.SourceName);
            Assert.NotEmpty(document.Components);
        }
    }

    [Fact]
    public void Boards_cui_parses_with_zero_errors()
    {
        var path = FindBoardsCui();
        var parser = new CuiRichParser();
        var document = parser.ParseFile(path);

        var errors = parser.Diagnostics.Diagnostics
            .Where(d => d.Severity == CuiDiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(errors.Length == 0, $"Boards.cui parse errors: {string.Join("; ", errors)}");
        Assert.NotEmpty(document.Components);
    }

    [Fact]
    public void Graph_slider_cui_fragment_parses_with_zero_errors()
    {
        var path = Path.Combine(Path.GetDirectoryName(FindBoardsCui())!, "GraphSliderCui.cui");
        var parser = new CuiRichParser();
        var document = parser.ParseFile(path);
        var errors = parser.Diagnostics.Diagnostics
            .Where(d => d.Severity == CuiDiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(errors.Length == 0, $"GraphSliderCui.cui parse errors: {string.Join("; ", errors)}");
        Assert.NotEmpty(document.Components);
    }

    [Fact]
    public void Block_grip_cui_fragment_parses_with_authored_typed_actions()
    {
        // Split 29 October: the gutter holds only the toggle, the owner's menu lives in its own authored
        // document (BlockActionsMenuCui.cui) because a 232-unit list cannot fit the 32-unit gutter column.
        // Both documents are parse-checked and their action rows asserted, so the menu cannot silently
        // regress into the fallback "unavailable" control that BlockGripCui.Build returns on a bad parse.
        var directory = Path.GetDirectoryName(FindBoardsCui())!;
        var gripPath = Path.Combine(directory, "BlockGripCui.cui");
        var menuPath = Path.Combine(directory, "BlockActionsMenuCui.cui");
        var grip = ParseCuiWithoutErrors(gripPath);
        var menu = ParseCuiWithoutErrors(menuPath);

        Assert.Contains("Action=\"ToggleActions\"", grip, StringComparison.Ordinal);
        Assert.Contains("ID=\"BlockGripToggle\"", grip, StringComparison.Ordinal);

        // The owner's mockup order and the submenu groups the grip menu must carry.
        foreach (var action in new[] { "Action=\"Delete\"", "Action=\"Duplicate\"", "Action=\"MoveUp\"",
            "Action=\"MoveDown\"", "Action=\"ToggleMoveMenu\"", "Action=\"ToggleDesignMenu\"" })
            Assert.Contains(action, menu, StringComparison.Ordinal);
        // The Design submenu re-types the block in place through the same insert machinery as the plus
        // menu, so its rows must stay authored commands rather than code-built items.
        foreach (var turn in new[] { "TurnInto:paragraph", "TurnInto:heading", "TurnInto:checklist",
            "TurnInto:bulleted", "TurnInto:numbered", "TurnInto:table", "TurnInto:image", "TurnInto:divider" })
            Assert.Contains(turn, menu, StringComparison.Ordinal);
        // CUI is the authoring language for presentation: these rows need the hover/preview hooks the
        // owner asked for (insertion line on Above/Below, red outline on Replace), so their absence in
        // the authored document is recorded here rather than left as an unmeasured assumption.
        Assert.DoesNotContain("PointerEntered", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("BlockInsertionPreview", menu, StringComparison.Ordinal);
    }

    /// <summary>Parses one authored Boards CUI document and fails with its real compiler diagnostics.
    /// Returning the source keeps the assertion over authored commands readable at the call site.</summary>
    private static string ParseCuiWithoutErrors(string path)
    {
        var parser = new CuiRichParser();
        var document = parser.ParseFile(path);
        var errors = parser.Diagnostics.Diagnostics
            .Where(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, $"{Path.GetFileName(path)} parse errors: {string.Join("; ", errors)}");
        Assert.NotEmpty(document.Components);
        return File.ReadAllText(path);
    }

    [Fact]
    public async Task Boards_cui_loader_builds_non_null_root()
    {
        var path = FindBoardsCui();
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var viewModel = new BoardsViewModel(session);

        var (root, diagnostics) = await OnUiThreadAsync(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            return loader.LoadFile(path);
        });

        var errors = diagnostics
            .Where(d => d.Severity == CuiDiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();

        Assert.True(errors.Length == 0, $"Boards.cui load errors: {string.Join("; ", errors)}");
        Assert.NotNull(root);
        Assert.IsAssignableFrom<Control>(root);
    }

    [Fact]
    public async Task Boards_cui_action_attributes_wire_to_dispatcher_tag()
    {
        // Deterministic small-tree proof of the action= → Tag → dispatcher
        // mechanism (full Boards.cui load is covered by the loader test above).
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync(null);
        var viewModel = new BoardsViewModel(session);

        var found = await OnUiThreadAsync(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            var root = Assert.IsAssignableFrom<Control>(loader.LoadMarkup(
                """
                <Cui>
                  <Container Type="Vertical Stack">
                    <Button content="Save" action="SaveBoard" />
                    <Button content="Add" action="AddBlock" />
                  </Container>
                </Cui>
                """,
                "boards-actions.cui").Root);
            return FindButtonByTag(root, "SaveBoard") is not null
                && FindButtonByTag(root, "AddBlock") is not null;
        });

        Assert.True(found);
    }

    [Fact]
    public async Task View_mode_blocks_document_edits_and_edit_mode_restores_them()
    {
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/view-mode-test");
        var viewModel = new BoardsViewModel(session);
        var originalTitle = session.Document.Title;
        var originalBlockCount = viewModel.PageBlocks.Count;

        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.False(viewModel.IsEditMode);
        viewModel.EditText("BoardTitleBox", "must not be applied");
        await viewModel.DispatchAsync("AddBlock", null);
        Assert.Equal(originalTitle, session.Document.Title);
        Assert.Equal(originalBlockCount, viewModel.PageBlocks.Count);
        Assert.Contains("View mode", viewModel.Get("StatusText")?.ToString());

        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.True(viewModel.IsEditMode);
        await viewModel.DispatchAsync("AddBlock", null);
        Assert.Equal(originalBlockCount + 1, viewModel.PageBlocks.Count);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task View_mode_keeps_ink_and_graph_navigation_session_local()
    {
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/view-navigation-test");
        var page = session.Document.Sections[0].Pages[0];
        var graph = new RichGraphView
        {
            Sliders = [new RichGraphSliderView
            {
                VariableId = "variable-a", Name = "a", Minimum = -2, Maximum = 6, Step = 0.5, DefaultValue = 1.5
            }]
        };
        page.Blocks.Add(new RichBoardBlock { Id = "view-graph", Kind = "graph", Graph = graph });
        var viewModel = new BoardsViewModel(session);
        await viewModel.DispatchAsync("ToggleEditView", null);

        await viewModel.PanInkViewAsync(12, -4);
        await viewModel.SetInkZoomAsync(2);
        viewModel.GraphPanZoom("view-graph", 0.1, 0, 1);
        viewModel.SetGraphSliderValue("view-graph", "variable-a", 2.5);

        Assert.Equal(12, viewModel.InkPanX);
        Assert.Equal(2, viewModel.InkZoom);
        Assert.Equal(0, page.InkPanX);
        Assert.Equal(1, page.InkZoom);
        Assert.Equal(-10, graph.XMin);
        Assert.Equal(-8, viewModel.GetGraphForRendering("view-graph")!.XMin);
        Assert.Equal(2.5, viewModel.GetGraphSliderValues("view-graph")["a"]);

        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.Equal(-10, viewModel.GetGraphForRendering("view-graph")!.XMin);
        Assert.Equal(0, viewModel.InkPanX);
        Assert.Equal(1, viewModel.InkZoom);
        Assert.Equal(1.5, viewModel.GetGraphSliderValues("view-graph")["a"]);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task View_mode_restores_only_the_controls_that_were_editable()
    {
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/view-control-state-test");
        var viewModel = new BoardsViewModel(session);
        var search = new TextBox { Name = "NavSearchBox" };
        var editable = new TextBox { Name = "Editable" };
        var readOnly = new TextBox { Name = "ReadOnly", IsReadOnly = true };
        var enabledCheck = new CheckBox { Name = "EnabledCheck", IsEnabled = true };
        var disabledCheck = new CheckBox { Name = "DisabledCheck", IsEnabled = false };
        var root = new StackPanel { Children = { search, editable, readOnly, enabledCheck, disabledCheck } };
        viewModel.Attach(root);

        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.True(editable.IsReadOnly);
        Assert.True(readOnly.IsReadOnly);
        Assert.False(search.IsReadOnly);
        Assert.False(enabledCheck.IsEnabled);
        Assert.False(disabledCheck.IsEnabled);

        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.False(editable.IsReadOnly);
        Assert.True(readOnly.IsReadOnly);
        Assert.False(search.IsReadOnly);
        Assert.True(enabledCheck.IsEnabled);
        Assert.False(disabledCheck.IsEnabled);
        viewModel.Detach();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Landing_repeats_search_and_dispatch_only_current_typed_rows()
    {
        var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/landing-row-test");
        var viewModel = new BoardsViewModel(session);
        var matching = new RecentBoardCard("board-a", "C:/boards/a.9to1board", "Algebra notes", "Quadratic review", DateTimeOffset.UtcNow, true);
        var other = new RecentBoardCard("board-b", "C:/boards/b.9to1board", "Travel", "Packing list", DateTimeOffset.UtcNow, true);
        var recentPage = new RecentPageCard(matching.Path, "section-a", "page-a", matching.Title, "Quadratics", "Roots", DateTimeOffset.UtcNow);
        string? openedPath = null;
        string? openedPage = null;
        viewModel.OpenBoardPathAsync = (path, page) => { openedPath = path; openedPage = page; return Task.CompletedTask; };
        viewModel.SetLandingData([matching, other], [recentPage]);
        viewModel.SetLanding(true);
        viewModel.SetLandingSearch("quadratic");

        Assert.Single(viewModel.RecentBoardCards);
        Assert.Equal("Algebra notes", viewModel.RecentBoardCards[0].Title);
        Assert.True(viewModel.TryGetItemValue(matching, "Title", out var title));
        Assert.Equal("Algebra notes", title);
        await viewModel.DispatchAsync("OpenRecentBoard", matching);
        Assert.Equal(matching.Path, openedPath);
        Assert.Null(openedPage);

        await viewModel.DispatchAsync("OpenRecentPage", recentPage);
        Assert.Equal(matching.Path, openedPath);
        Assert.Equal("page-a", openedPage);
        viewModel.SetLandingData([other], []);
        await viewModel.DispatchAsync("OpenRecentBoard", matching);
        Assert.Equal("page-a", openedPage);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Boards_cui_extended_properties_apply()
    {
        var markup = """
            <Cui>
              <Container Type="Vertical Stack">
                <Input Type="Text" id="Para" Value="hello" Multiline="true" TextWrapping="Wrap" FontSize="14" FontWeight="Bold" FontStyle="Italic" />
                <Text Text="styled" TextDecoration="Underline" />
                <Container Type="Absolute" Width="100" Height="100" XInherited="True" YInherited="True">
                  <Text Text="dot" X-px="12" Y-px="34" />
                </Container>
              </Container>
            </Cui>
            """;
        var (acceptsReturn, wrapping, weight, style, hasDecorations, left, top) = await OnUiThreadAsync(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            var (root, diagnostics) = loader.LoadMarkup(markup, "boards-ext.cui");
            Assert.True(diagnostics.Count == 0, $"Diagnostics: {string.Join("; ", diagnostics.Select(d => d.ToString()))}");
            var panel = Assert.IsType<CuiContainerControl>(root);
            var textBox = Assert.IsType<TextBox>(panel.AuthoredChildren[0]);
            var styled = Assert.IsType<TextBlock>(panel.AuthoredChildren[1]);
            var canvas = Assert.IsType<CuiContainerControl>(panel.AuthoredChildren[2]);
            var dot = Assert.IsType<TextBlock>(canvas.AuthoredChildren[0]);
            var window = new Window { Width = 300, Height = 300, Content = panel };
            try
            {
                window.Show(); window.UpdateLayout();
                var centre = dot.TranslatePoint(new Point(dot.Bounds.Width / 2, dot.Bounds.Height / 2), canvas)!.Value;
                return (textBox.AcceptsReturn, textBox.TextWrapping, textBox.FontWeight, textBox.FontStyle,
                    styled.TextDecorations is not null, centre.X, centre.Y);
            }
            finally { window.Close(); }
        });

        Assert.True(acceptsReturn);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, wrapping);
        Assert.Equal(Avalonia.Media.FontWeight.Bold, weight);
        Assert.Equal(Avalonia.Media.FontStyle.Italic, style);
        Assert.True(hasDecorations);
        // Owner-approved pixels use the inherited bounds' centre, with Y up.
        Assert.Equal(50 + 12, left, 5);
        Assert.Equal(50 - 34, top, 5);
    }

    [Fact]
    public async Task Boards_cui_grid_definitions_apply()
    {
        var layout = await OnUiThreadAsync(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            var (loaded, diagnostics) = loader.LoadMarkup(
                """
                <Cui>
                  <Container Type="Grid" ColumnSizes="100 1fr" RowSizes="Auto 1fr">
                    <Text Text="layout" Column="1" Row="1" />
                  </Container>
                </Cui>
                """,
                "boards-grid.cui");
            Assert.Empty(diagnostics);
            var container = Assert.IsType<CuiContainerControl>(loaded);
            var grid = Assert.IsType<Grid>(container.Layout);
            var child = Assert.IsType<TextBlock>(container.AuthoredChildren[0]);
            return (
                ColumnCount: grid.ColumnDefinitions.Count,
                RowCount: grid.RowDefinitions.Count,
                Column: Grid.GetColumn(child),
                Row: Grid.GetRow(child));
        });

        Assert.Equal(2, layout.ColumnCount);
        Assert.Equal(2, layout.RowCount);
        Assert.Equal(1, layout.Column);
        Assert.Equal(1, layout.Row);
    }

    [Fact]
    public async Task Edit_save_dispose_reopen_preserves_content()
    {
        InMemoryRichBoardSession.ClearStore();
        const string path = "memory://boards/roundtrip-test";
        string statusAfterSave;

        await using (var session = new InMemoryRichBoardSession())
        {
            await session.OpenAsync(path);
            var viewModel = new BoardsViewModel(session);

            // Simulate multiline paragraph edit wiring: Attach subscribes without
            // live bindings so the test stays deterministic under headless.
            await OnUiThreadAsync(() =>
            {
                var hostLoader = BoardsCuiDocuments.CreateLoader();
                var host = Assert.IsAssignableFrom<Control>(hostLoader.LoadMarkup(
                    """
                    <Cui>
                      <Container Type="Vertical Stack">
                        <Input Type="Text" id="ParaBox" Value="Edit this paragraph." Multiline="true" TextWrapping="Wrap" />
                      </Container>
                    </Cui>
                    """,
                    "boards-edit-host.cui").Root);
                viewModel.Attach(host);
            });
            session.Document.Title = "Roundtrip board";
            var para = session.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            para.Text = "Line one\nLine two\nLine three";
            para.Bold = true;
            session.MarkDirty();

            await session.DispatchSaveForTestAsync();
            statusAfterSave = session.Status;
        }

        // Durable save reports success before dispose.
        Assert.StartsWith("Saved ", statusAfterSave, StringComparison.Ordinal);

        await using (var reopened = new InMemoryRichBoardSession())
        {
            await reopened.OpenAsync(path);
            Assert.Equal("Roundtrip board", reopened.Document.Title);
            var para = reopened.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
            Assert.Equal("Line one\nLine two\nLine three", para.Text);
            Assert.True(para.Bold);
        }
    }

    [Fact]
    public async Task Explicit_save_sets_saved_status_only_after_durable_save()
    {
        InMemoryRichBoardSession.ClearStore();
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/status-test");
        var viewModel = new BoardsViewModel(session);

        string? observed = null;
        session.StatusChanged += (_, status) => observed = status;

        await viewModel.DispatchAsync("SaveBoard", null);

        Assert.StartsWith("Saved ", session.Status, StringComparison.Ordinal);
        Assert.Equal(session.Status, observed);
        Assert.Equal(session.Status, viewModel.Get("StatusText")?.ToString());
    }

    private static Task<Control> CreateEditHostAsync(BoardsViewModel viewModel)
    {
        // Minimal host so Attach() can subscribe without the full Boards.cui tree.
        return OnUiThreadAsync(() =>
        {
            var loader = BoardsCuiDocuments.CreateLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(viewModel);
            var root = loader.LoadMarkup(
                """
                <Cui>
                  <Container Type="Vertical Stack">
                    <Input Type="Text" id="ParaBox" Value="{Binding ParaText, fallback=Edit this paragraph.}" WriteBack="False" Multiline="true" TextWrapping="Wrap" />
                  </Container>
                </Cui>
                """,
                "boards-edit-host.cui").Root;
            return Assert.IsAssignableFrom<Control>(root);
        });
    }

    private static Button? FindButtonByTag(Control root, string tag)
    {
        if (root is Button button && string.Equals(button.Tag as string, tag, StringComparison.Ordinal))
            return button;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindButtonByTag(child, tag);
            if (found is not null)
                return found;
        }
        return null;
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
    }
}

internal static class BoardsSessionTestExtensions
{
    /// <summary>Explicit durable save through the session (what SaveBoard dispatches to).</summary>
    public static ValueTask DispatchSaveForTestAsync(this IRichBoardSession session) =>
        session.SaveAsync();
}
