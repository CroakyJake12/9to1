using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Stacks.NativeUI.Tests;

public sealed partial class StackNativeWorkspaceTests
{
    private static readonly StackActor Actor = new("native-fixture-owner",
        new HashSet<StackCapability> { StackCapability.ViewSource, StackCapability.Contribute, StackCapability.CreateDomain });
    private static readonly List<object> RetainedFailures = [];
    private static IReadOnlyList<StackNativeRow> Rows(StackCuiWorkspace workspace, string key)
    { Assert.True(workspace.TryGetValue(key, out var value)); return Assert.IsAssignableFrom<IReadOnlyList<StackNativeRow>>(value); }
    private static object? Value(StackCuiWorkspace workspace, string key)
    { Assert.True(workspace.TryGetValue(key, out var value)); return value; }
    private static async Task<(StackEngine Engine, string Directory)> ProjectAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "9to1-stacks-native-fixtures", Guid.NewGuid().ToString("N"));
        var engine = new StackEngine(new JsonFileStackProjectStore(directory));
        await engine.CreateProjectAsync(new("Native workflow", StackStorageMode.Files, directory), Actor);
        return (engine, directory);
    }
    private static async Task<StackCuiWorkspace> WorkspaceAsync(StackEngine engine)
    {
        var workspace = new StackCuiWorkspace([new(engine, Actor)]);
        await workspace.OriginalInitialization;
        return workspace;
    }
    private static async Task OpenAsync(StackCuiWorkspace workspace)
        => await workspace.DispatchAsync("9to1.Stacks.OpenProject", Assert.Single(Rows(workspace, "Projects")).Target);

    [Fact]
    public void Shared_diff_preserves_actual_missing_sides_line_coordinates_and_final_empty_line()
    {
        var rows = SharedSourceTextDiff.Compare("same\r\nold\r\n", "same\nnew\n");
        Assert.Collection(rows,
            row => { Assert.Equal(SourceTextDiffKind.Unchanged, row.Kind); Assert.Equal(1, row.BeforeLineNumber); Assert.Equal(1, row.AfterLineNumber); },
            row => { Assert.Equal(SourceTextDiffKind.Removed, row.Kind); Assert.Equal("old", row.BeforeText); Assert.Null(row.AfterLineNumber); Assert.Equal(2, row.BeforeLineNumber); },
            row => { Assert.Equal(SourceTextDiffKind.Added, row.Kind); Assert.Equal("new", row.AfterText); Assert.Null(row.BeforeLineNumber); Assert.Equal(2, row.AfterLineNumber); },
            row => { Assert.Equal("", row.BeforeText); Assert.Equal("", row.AfterText); Assert.Equal(3, row.BeforeLineNumber); Assert.Equal(3, row.AfterLineNumber); });
        var emptyFile = Assert.Single(SharedSourceTextDiff.Compare(null, ""));
        Assert.Equal(SourceTextDiffKind.Added, emptyFile.Kind); Assert.Null(emptyFile.BeforeText); Assert.Equal("", emptyFile.AfterText);
        Assert.Empty(SharedSourceTextDiff.Compare(null, null));
        var equalLarge = string.Join("\n", Enumerable.Repeat("same", 3000));
        var unchanged = SharedSourceTextDiff.Compare(equalLarge, equalLarge);
        Assert.Equal(3000, unchanged.Count); Assert.All(unchanged, line => Assert.Equal(SourceTextDiffKind.Unchanged, line.Kind));
    }

    [Fact]
    public void Binary_invalid_utf8_and_source_limits_preserve_bytes_without_a_text_substitute()
    {
        var bytes = new byte[] { 0xff, 0xfe, 0x01 };
        Assert.False(StackNativeSourceDiff.Read(null, new(bytes)).IsText);
        Assert.False(StackNativeSourceDiff.Read(null, new([65, 66], IsBinary: true)).IsText);
        Assert.False(StackNativeSourceDiff.Read(null, new([65, 0, 66])).IsText);
        Assert.Equal(new byte[] { 0xff, 0xfe, 0x01 }, bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => SharedSourceTextDiff.Compare(null, new string('\n', SharedSourceTextDiff.MaximumSourceLines)));
    }

    [Fact]
    public async Task Effective_inherited_source_does_not_inflate_child_owned_changes_and_navigation_stays_inside_its_tab()
    {
        var (engine, _) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        await engine.ApplyChangeAsync(main.Id, new(StackMutationKind.Upsert, "src/shared.txt", new(Encoding.UTF8.GetBytes("source\n"))), Actor);
        await engine.CreateCommitAsync(main.Id, "Shared source", Actor);
        var branch = await engine.CreateDomainAsync(main.Id, "Feature", Actor);
        var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var row = Rows(workspace, "Domains").Single(r => r.Key == branch.Id.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", row.Target);
        Assert.Equal(branch.Id, workspace.DomainId);
        Assert.Equal("0 added · 0 deleted · 0 modified · 0 renamed", Value(workspace, "Counts"));
        var file = Assert.Single(Rows(workspace, "Files")); Assert.Contains("inherited", file.Label);
        await workspace.DispatchAsync("9to1.Stacks.SelectFile", file.Target);
        Assert.Equal("source\n", Value(workspace, "SourceText"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Commit"));
        await workspace.DispatchAsync("9to1.Stacks.Back", null); Assert.Equal(main.Id, workspace.DomainId);
        await workspace.DispatchAsync("9to1.Stacks.Forward", null); Assert.Equal(branch.Id, workspace.DomainId);
        await workspace.DispatchAsync("9to1.Stacks.Home", null); Assert.Null(workspace.ProjectId);
        await OpenAsync(workspace); Assert.Equal(branch.Id, workspace.DomainId);
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Same_key_old_domain_and_foreign_workspace_rows_cannot_dispatch_current_source()
    {
        var (engine, _) = await ProjectAsync(); var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var old = Assert.Single(Rows(workspace, "Domains"));
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(old.Key, Assert.Single(Rows(workspace, "Domains")).Key);
        Assert.Throws<InvalidOperationException>(() => { _ = workspace.DispatchAsync("9to1.Stacks.SelectDomain", old.Target); });
        var other = await WorkspaceAsync(engine); await OpenAsync(other);
        Assert.Throws<InvalidOperationException>(() => { _ = workspace.DispatchAsync("9to1.Stacks.SelectDomain", Assert.Single(Rows(other, "Domains")).Target); });
        Assert.Equal(workspace.DomainId, other.DomainId);
        await workspace.CloseOriginalAsync(); await other.CloseOriginalAsync();
    }

    [Fact]
    public async Task Reopened_actual_working_changes_enable_commit_and_committed_changes_do_not()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        await engine.ApplyChangeAsync(main.Id, new(StackMutationKind.Upsert, "pending.txt", new(Encoding.UTF8.GetBytes("pending"))), Actor);
        var reopened = new StackEngine(new JsonFileStackProjectStore(directory)); var workspace = await WorkspaceAsync(reopened); await OpenAsync(workspace);
        Assert.True(workspace.TrySetValue("CommitMessage", "Persist pending source"));
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.Commit"));
        Assert.Contains("↑", Assert.Single(Rows(workspace, "Domains")).Label);
        Assert.True(workspace.TrySetValue("CommitMessage", "Persist pending source"));
        await workspace.DispatchAsync("9to1.Stacks.Commit", null);
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Commit"));
        Assert.DoesNotContain("↑", Assert.Single(Rows(workspace, "Domains")).Label);
        await workspace.DispatchAsync("9to1.Stacks.NewTextFile", null);
        Assert.True(workspace.TrySetValue("SourcePath", "pending.txt"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.ApplyText"));
        Assert.True(workspace.TrySetValue("SourcePath", "../outside.txt"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.ApplyText"));
        var saved = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Empty(saved.WorkingChanges); Assert.Single(saved.LocalChanges);
        Assert.Equal("pending", Encoding.UTF8.GetString(saved.LocalChanges[0].Resource!.Content));
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Actual_CUI_fields_buttons_save_real_bytes_commit_and_reopen_same_project_and_domain()
    {
        var (engine, directory) = await ProjectAsync();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(StackNativeTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            var workspace = await WorkspaceAsync(engine);
            var loader = new CuiControlLoader(); loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
            var root = Assert.IsAssignableFrom<Control>(loader.Load(StackCuiWorkspace.LoadDocument())); loader.WireBindings(root);
            var window = new Window { Width = 1080, Height = 760, Content = root }; window.Show();
            try
            {
                await ClickAsync(loader, workspace, FindRow(root, Assert.Single(Rows(workspace, "Projects"))));
                var originalProject = workspace.ProjectId; var main = workspace.DomainId;
                Field(root, "StacksDomainName").Text = "Feature";
                Assert.Equal("Feature", Value(workspace, "DomainName"));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksCreateDomain"));
                var branch = workspace.DomainId; Assert.NotEqual(main, branch);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksNewTextFile"));
                Field(root, "StacksSourcePath").Text = "src/new.txt";
                Field(root, "StacksSourceEditor").Text = "first\nsecond\n";
                Assert.Equal("first\nsecond\n", Value(workspace, "SourceText")); Assert.True(workspace.HasDraft);
                Assert.False(workspace.IsActionAvailable("9to1.Stacks.Home"));
                Assert.Throws<InvalidOperationException>(() => { _ = workspace.CloseOriginalAsync(); }); Assert.Null(workspace.OriginalClose);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksApplyText"));
                Assert.False(workspace.HasDraft);
                var actualTree = await engine.GetEffectiveTreeAsync(branch!.Value, Actor);
                Assert.Equal(Encoding.UTF8.GetBytes("first\nsecond\n"), actualTree.Files["src/new.txt"].Content);
                Field(root, "StacksCommitMessage").Text = "Add source from native Stacks";
                await ClickAsync(loader, workspace, NamedButton(root, "StacksCommit"));
                Assert.False(workspace.IsActionAvailable("9to1.Stacks.Commit"));
                // Working on a browsed domain does not change the project's default context.
                Assert.Equal(main, workspace.ActiveDomainId);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksSetActive"));
                Assert.Equal(branch, workspace.ActiveDomainId);
                var reopened = new StackEngine(new JsonFileStackProjectStore(directory)); var saved = await reopened.OpenProjectAsync();
                Assert.Equal(originalProject, saved.ProjectId); Assert.Equal(branch, saved.Id); Assert.Empty(saved.WorkingChanges);
                var bytes = (await reopened.GetEffectiveTreeAsync(saved.Id, Actor)).Files["src/new.txt"].Content;
                Assert.Equal(Encoding.UTF8.GetBytes("first\nsecond\n"), bytes);
                loader.Dispose(); await loader.WhenActionsIdleAsync(); await workspace.CloseOriginalAsync(); window.Close();
                return true;
            }
            catch { RetainedFailures.Add((workspace, loader, root, window)); throw; }
        });
        Assert.True(completed);
    }

    [Fact]
    public async Task Actual_observer_neutral_context_self_join_is_refused_and_external_close_reuses_same_driver()
    {
        var (engine, _) = await ProjectAsync(); var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var neutral = ExecutionContext.Capture()!; var observed = false;
        workspace.PropertyChanged += (_, _) => ExecutionContext.Run(neutral, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = workspace.CloseOriginalAsync(); });
            Assert.Null(workspace.OriginalClose); observed = true;
        }, null);
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null); Assert.True(observed);
        var close = workspace.CloseOriginalAsync(); await close; Assert.Same(close, workspace.CloseOriginalAsync());
    }

    [Fact]
    public async Task Foreign_actual_store_failure_is_retained_and_failed_close_is_not_replaced()
    {
        var failure = new IOException("Actual source read failed"); var raw = Task.FromException<StackManifest>(failure);
        var workspace = new StackCuiWorkspace([new(new StackEngine(new FailedStore(raw)), Actor)]);
        var original = workspace.OriginalInitialization;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => original));
        var close = workspace.CloseOriginalAsync(); var group = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(group.InnerExceptions.OfType<AggregateException>(), envelope => envelope.InnerExceptions.Contains(failure));
        Assert.Same(original, workspace.OriginalInitialization); Assert.Same(close, workspace.CloseOriginalAsync());
        RetainedFailures.Add((workspace, original, raw, close, failure));
    }
    private static TextBox Field(Control root, string name) => root.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == name);
    private static Button NamedButton(Control root, string name) => root.GetVisualDescendants().OfType<Button>().Single(c => c.Name == name);
    private static Button FindRow(Control root, StackNativeRow row) => root.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, row.Label));
    private static async Task ClickAsync(CuiControlLoader loader, StackCuiWorkspace workspace, Button button)
    {
        Assert.True(button.IsEnabled);
        var actual = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button)); Assert.True(actual.DispatcherConnected); Assert.True(actual.ActionsWired);
        var prior = workspace.OriginalCommand; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pipeline = loader.WhenActionsIdleAsync(); await pipeline; Assert.True(pipeline.IsCompletedSuccessfully);
        var command = workspace.OriginalCommand; Assert.NotNull(command); Assert.NotSame(prior, command); await command;
        Assert.True(command.IsCompletedSuccessfully);
    }
    private sealed class FailedStore(Task<StackManifest> raw) : IStackProjectStore
    {
        public string ProjectDirectory => "/fixture/no-path-authority";
        public Task<StackManifest> LoadAsync(CancellationToken token = default) => raw;
        public Task CreateAsync(StackManifest manifest, CancellationToken token = default) => throw new InvalidOperationException("No fixture write is authorised.");
        public Task SaveAsync(StackManifest manifest, CancellationToken token = default) => throw new InvalidOperationException("No fixture write is authorised.");
    }
}

public sealed class StackNativeTestApplication : Application { }
