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
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

public sealed class BoardsEditorTests
{
    private static Task<T> OnUiThreadAsync<T>(Func<T> work) =>
        // All UI work funnels through the shared test UI thread: with Skia
        // registered, control trees are strictly thread-affine.
        Task.FromResult(TestUiThread.Run(work));

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

        var (errorCount, mentionsBad, canvasCount) = await OnUiThreadAsync(() =>
        {
            var host = new StackPanel();
            BlockRenderer.Rebuild(host, viewModel, []);
            var all = Flatten(host).ToArray();
            var errors = all.OfType<TextBlock>()
                .Where(t => (t.Text ?? string.Empty).StartsWith("Invalid expression", StringComparison.Ordinal))
                .ToArray();
            return (errors.Length, errors.Any(t => t.Text!.Contains("y = ???", StringComparison.Ordinal)),
                all.OfType<Canvas>().Count());
        });

        Assert.Equal(1, errorCount);
        Assert.True(mentionsBad);
        // One plot canvas plus the always-present freeform canvas.
        Assert.Equal(2, canvasCount);
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
            "TopBarLeft", "TopBarRight", "BoardTitleBox", "NavSearchBox", "NavHost",
            "ToolbarHost", "ContextHost", "EditorScroll", "PageCard", "BlocksHost",
        };
        var components = document.Components
            .SelectMany(c => c.DescendantsAndSelf())
            .Where(c => c.Properties.TryGetValue("automationid", out var id)
                && id is CakeOS.Cui.CuiLiteralValue literal
                && expected.Contains(literal.Value, StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(expected.Length, components.Length);

        var found = await OnUiThreadAsync(() =>
        {
            var loader = new CakeOS.Cui.Runtime.CuiControlLoader();
            var root = Assert.IsAssignableFrom<Control>(loader.LoadMarkup(
                """
                <Cui>
                  <StackPanel>
                    <TextBox automationid="BoardTitleBox" text="Title" />
                    <StackPanel automationid="NavHost" />
                    <StackPanel automationid="ToolbarHost" />
                    <StackPanel automationid="ContextHost" />
                    <StackPanel automationid="BlocksHost" />
                  </StackPanel>
                </Cui>
                """,
                "boards-host-lookup.cui").Root);
            return FindByAutomationId<StackPanel>(root, "BlocksHost") is not null
                && FindByAutomationId<StackPanel>(root, "NavHost") is not null
                && FindByAutomationId<StackPanel>(root, "ToolbarHost") is not null
                && FindByAutomationId<StackPanel>(root, "ContextHost") is not null
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
    {
        if (root is T match
            && (string.Equals(root.Name, automationId, StringComparison.Ordinal)
                || string.Equals(Avalonia.Automation.AutomationProperties.GetAutomationId(root), automationId, StringComparison.Ordinal)))
            return match;
        foreach (var child in Flatten(root))
        {
            if (child is T typed
                && (string.Equals(typed.Name, automationId, StringComparison.Ordinal)
                    || string.Equals(Avalonia.Automation.AutomationProperties.GetAutomationId(typed), automationId, StringComparison.Ordinal)))
                return typed;
        }
        return null;
    }

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
