using System.Reflection;
using System.Text;
using System.Text.Json;
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
        var (engine, _) = await ProjectAsync(); var main = await engine.OpenProjectAsync(Actor);
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
    public async Task Stale_domain_history_recovers_restores_and_cancelled_refresh_can_retry()
    {
        var (engine, _) = await ProjectAsync(); var main = await engine.OpenProjectAsync(Actor);
        var branch = await engine.CreateDomainAsync(main.Id, "Temporary", Actor);
        var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var branchRow = Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", branchRow.Target);
        branchRow = Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D"));

        var admin = new StackActor("external-maintainer", Enum.GetValues<StackCapability>().ToHashSet(), true);
        await engine.PruneDomainAsync(branch.Id, admin);
        // This row was issued by the current view, but its canonical domain was
        // pruned externally before its action arrived.
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", branchRow.Target);
        Assert.Equal(main.Id, workspace.DomainId);
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Back"));
        Assert.Contains("no longer available", Assert.IsType<string>(Value(workspace, "Status")));

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.DispatchAsync("9to1.Stacks.Refresh", null, cancelled.Token).AsTask());
        Assert.Equal(main.Id, workspace.DomainId);
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(main.Id, workspace.DomainId);
        await engine.RestorePrunedDomainAsync(branch.Id, Actor);
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(main.Id, workspace.DomainId);
        Assert.Contains(Rows(workspace, "Domains"), row => row.Key == branch.Id.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        Assert.Equal(branch.Id, workspace.DomainId);
        await engine.PruneDomainAsync(branch.Id, admin);
        await workspace.DispatchAsync("9to1.Stacks.Back", null);
        await workspace.DispatchAsync("9to1.Stacks.Forward", null);
        Assert.Equal(main.Id, workspace.DomainId);
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Back"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Forward"));

        await engine.RestorePrunedDomainAsync(branch.Id, Actor);
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        await engine.PruneDomainAsync(branch.Id, admin);
        await workspace.DispatchAsync("9to1.Stacks.SetActive", null);
        Assert.Equal(main.Id, workspace.DomainId);
        Assert.Equal(main.Id, workspace.ActiveDomainId);
        Assert.Contains("without changing its active domain", Assert.IsType<string>(Value(workspace, "Status")));
        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Cancelled_command_and_failing_final_publication_retain_both_original_causes()
    {
        var (engine, _) = await ProjectAsync(); var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var publicationFailure = new IOException("Final cancellation-state publication failed"); var notifications = 0;
        workspace.PropertyChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref notifications) == 2) throw publicationFailure;
        };

        var command = workspace.DispatchAsync("9to1.Stacks.Refresh", null, cancellation.Token).AsTask();
        var failures = await Assert.ThrowsAsync<AggregateException>(() => command);
        Assert.Equal(2, failures.InnerExceptions.Count);
        var primary = Assert.IsAssignableFrom<OperationCanceledException>(failures.InnerExceptions[0]);
        Assert.Equal(cancellation.Token, primary.CancellationToken);
        Assert.Same(publicationFailure, failures.InnerExceptions[1]);
        Assert.Same(command, workspace.OriginalCommand);

        var close = workspace.CloseOriginalAsync();
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(closeFailure.InnerExceptions, inner => inner is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, publicationFailure)));
        Assert.Same(close, workspace.CloseOriginalAsync());
        RetainedFailures.Add((workspace, command, failures, close, closeFailure, publicationFailure));
    }

    [Fact]
    public async Task Unrelated_cancellation_exception_is_not_classified_or_closed_as_caller_cancellation()
    {
        var (engine, _) = await ProjectAsync(); var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        using var caller = new CancellationTokenSource(); using var unrelated = new CancellationTokenSource(); unrelated.Cancel();
        var notifications = 0;
        workspace.PropertyChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref notifications) == 1)
            {
                caller.Cancel();
                throw new OperationCanceledException("The publisher cancelled independently.", null, unrelated.Token);
            }
        };

        var command = workspace.DispatchAsync("9to1.Stacks.Refresh", null, caller.Token).AsTask();
        var commandFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command);
        Assert.Equal(unrelated.Token, commandFailure.CancellationToken);
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.Refresh"));
        var close = workspace.CloseOriginalAsync();
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(closeFailure.InnerExceptions, inner => inner is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.OfType<OperationCanceledException>()
                .Any(cause => cause.CancellationToken == unrelated.Token));
        RetainedFailures.Add((workspace, command, commandFailure, close, closeFailure));
    }

    [Fact]
    public async Task Refresh_reopens_a_stale_source_revision_without_changing_the_canonical_project_binding()
    {
        var (engine, directory) = await ProjectAsync(); var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var projectId = workspace.ProjectId; var mainId = workspace.DomainId!.Value;
        var external = new StackEngine(new JsonFileStackProjectStore(directory));
        await external.OpenProjectAsync(Actor);
        await external.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "external.txt", new(Encoding.UTF8.GetBytes("external revision"))), Actor);

        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);

        Assert.Equal(projectId, workspace.ProjectId);
        Assert.Equal(mainId, workspace.DomainId);
        Assert.Contains(Rows(workspace, "Files"), row => row.Key == "external.txt");
        Assert.Contains("current canonical source", Assert.IsType<string>(Value(workspace, "Status")));
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Refresh_refuses_foreign_project_rebind_and_preserves_current_tab_draft_history_and_source()
    {
        var store = new MutableProjectStore(); var engine = new StackEngine(store);
        await engine.CreateProjectAsync(new("Foreign rebind fixture", StackStorageMode.Files,
            Path.Combine(Path.GetTempPath(), "stacks-foreign-project-binding")), Actor);
        var main = await engine.OpenProjectAsync(Actor);
        var branch = await engine.CreateBranchAsync((await engine.GetProjectSummaryAsync(Actor)).ProjectId, "Current", Actor);
        var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        Assert.True(workspace.TrySetValue("DomainName", "Uncommitted branch work"));
        Assert.True(workspace.TrySetValue("CommitMessage", "Uncommitted commit intent"));
        var expectedHistory = NavigationHistory(workspace).ToArray();
        var expectedProjectId = workspace.ProjectId;
        var expectedDomainId = workspace.DomainId;
        var expectedActiveDomainId = workspace.ActiveDomainId;

        Guid foreignProjectId = Guid.NewGuid();
        store.ReplaceProjectId(foreignProjectId);
        byte[] foreignSource = store.Snapshot();

        StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(
            () => workspace.DispatchAsync("9to1.Stacks.Refresh", null).AsTask());

        Assert.Equal(StackFailureCode.ProjectNotFound, refused.Code);
        Assert.Equal(expectedProjectId, workspace.ProjectId);
        Assert.Equal(expectedDomainId, workspace.DomainId);
        Assert.Equal(expectedActiveDomainId, workspace.ActiveDomainId);
        Assert.Equal("Uncommitted branch work", Value(workspace, "DomainName"));
        Assert.Equal("Uncommitted commit intent", Value(workspace, "CommitMessage"));
        Assert.Equal(expectedHistory, NavigationHistory(workspace));
        Assert.Equal(new[] { main.Id, branch.Id }, Rows(workspace, "Domains").Select(row => Guid.Parse(row.Key)));
        Assert.Equal(foreignSource, store.Snapshot());
        Assert.Equal(expectedActiveDomainId, store.ActiveDomainId);

        var close = workspace.CloseOriginalAsync();
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(closeFailure.InnerExceptions, inner => inner is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, refused)));
        RetainedFailures.Add((workspace, refused, close, closeFailure));
    }

    [Fact]
    public async Task Project_tabs_contain_only_opened_projects_and_keep_form_state_per_tab()
    {
        var (firstEngine, _) = await ProjectAsync(); var (secondEngine, _) = await ProjectAsync();
        var workspace = new StackCuiWorkspace([new(firstEngine, Actor), new(secondEngine, Actor)]);
        await workspace.OriginalInitialization;
        Assert.Empty(Rows(workspace, "Tabs"));
        var projects = Rows(workspace, "Projects").ToArray();
        Guid firstId = Guid.Parse(projects[0].Key), secondId = Guid.Parse(projects[1].Key);

        await workspace.DispatchAsync("9to1.Stacks.OpenProject", projects[0].Target);
        Assert.Equal(new[] { firstId }, Rows(workspace, "Tabs").Select(row => Guid.Parse(row.Key)));
        Assert.Contains("current", Assert.Single(Rows(workspace, "Tabs")).Label);
        Assert.True(workspace.TrySetValue("DomainName", "First project draft"));
        Assert.True(workspace.TrySetValue("CommitMessage", "First project commit"));
        Assert.True(workspace.TrySetValue("FileFilter", "first-only"));

        var secondProject = Rows(workspace, "Projects").Single(row => row.Key == secondId.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", secondProject.Target);
        Assert.Equal(new[] { firstId, secondId }, Rows(workspace, "Tabs").Select(row => Guid.Parse(row.Key)));
        Assert.Equal("", Value(workspace, "DomainName"));
        Assert.Equal("", Value(workspace, "CommitMessage"));
        Assert.Equal("", Value(workspace, "FileFilter"));
        Assert.Contains("current", Rows(workspace, "Tabs").Single(row => row.Key == secondId.ToString("D")).Label);
        Assert.True(workspace.TrySetValue("DomainName", "Second project draft"));

        var firstTab = Rows(workspace, "Tabs").Single(row => row.Key == firstId.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", firstTab.Target);
        Assert.Equal(firstId, workspace.ProjectId);
        Assert.Equal("First project draft", Value(workspace, "DomainName"));
        Assert.Equal("First project commit", Value(workspace, "CommitMessage"));
        Assert.Equal("first-only", Value(workspace, "FileFilter"));
        Assert.Equal(2, Rows(workspace, "Tabs").Count);
        var secondTab = Rows(workspace, "Tabs").Single(row => row.Key == secondId.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", secondTab.Target);
        Assert.Equal("Second project draft", Value(workspace, "DomainName"));
        await workspace.DispatchAsync("9to1.Stacks.Home", null);
        Assert.Null(workspace.ProjectId);
        Assert.Equal(2, Rows(workspace, "Tabs").Count);
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Domain_paging_is_bounded_and_keeps_the_selected_domain_and_history()
    {
        var store = new MutableProjectStore(); var engine = new StackEngine(store);
        await engine.CreateProjectAsync(new("Paged lineage fixture", StackStorageMode.Files,
            Path.Combine(Path.GetTempPath(), "stacks-paged-lineage")), Actor);
        await engine.OpenProjectAsync(Actor);
        Guid[] branches = store.AddBranches(101);
        await engine.SetActiveDomainAsync(branches[100], Actor);
        var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        var history = NavigationHistory(workspace).ToArray();
        Assert.Equal(branches[100], workspace.DomainId);
        Assert.Equal(2, Rows(workspace, "Domains").Count);
        Assert.Equal(new[] { branches[99], branches[100] }, Rows(workspace, "Domains").Select(row => Guid.Parse(row.Key)));
        Assert.Equal("Domains · page 2 of 2", Value(workspace, "DomainPageLabel"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.NextDomains"));
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.PreviousDomains"));

        await workspace.DispatchAsync("9to1.Stacks.PreviousDomains", null);

        Assert.Equal(100, Rows(workspace, "Domains").Count);
        Assert.Equal("Domains · page 1 of 2", Value(workspace, "DomainPageLabel"));
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.NextDomains"));
        Assert.False(workspace.IsActionAvailable("9to1.Stacks.PreviousDomains"));
        Assert.Equal(branches[100], workspace.DomainId);
        Assert.Equal(branches[100], workspace.ActiveDomainId);
        Assert.Equal(history, NavigationHistory(workspace));

        await workspace.DispatchAsync("9to1.Stacks.NextDomains", null);
        Assert.Equal(2, Rows(workspace, "Domains").Count);
        Assert.Equal("Domains · page 2 of 2", Value(workspace, "DomainPageLabel"));
        Assert.Equal(branches[100], workspace.DomainId);
        Assert.Equal(history, NavigationHistory(workspace));
        await workspace.CloseOriginalAsync();
    }

    [Fact]
    public async Task Cancelled_and_failed_inactive_project_open_preserve_healthy_tab_and_retain_source_failure()
    {
        var (firstEngine, _) = await ProjectAsync(); var main = await firstEngine.OpenProjectAsync(Actor);
        await firstEngine.ApplyChangeAsync(main.Id,
            new(StackMutationKind.Upsert, "keep.txt", new(Encoding.UTF8.GetBytes("current project source"))), Actor);
        await firstEngine.CreateCommitAsync(main.Id, "Retain source for tab test", Actor);
        var branch = await firstEngine.CreateBranchAsync((await firstEngine.GetProjectSummaryAsync(Actor)).ProjectId, "Current", Actor);

        var secondStore = new MutableProjectStore(); var secondEngine = new StackEngine(secondStore);
        await secondEngine.CreateProjectAsync(new("Unavailable target", StackStorageMode.Files,
            Path.Combine(Path.GetTempPath(), "stacks-failing-open-target")), Actor);
        var workspace = new StackCuiWorkspace([new(firstEngine, Actor), new(secondEngine, Actor)]);
        await workspace.OriginalInitialization;
        var firstId = (await firstEngine.GetProjectSummaryAsync(Actor)).ProjectId;
        Guid secondId = (await secondEngine.GetProjectSummaryAsync(Actor)).ProjectId;
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", Rows(workspace, "Projects").Single(row => row.Key == firstId.ToString("D")).Target);
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain", Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        await workspace.DispatchAsync("9to1.Stacks.SelectFile", Rows(workspace, "Files").Single(row => row.Key == "keep.txt").Target);
        Assert.True(workspace.TrySetValue("DomainName", "Pending on healthy tab"));
        Assert.True(workspace.TrySetValue("CommitMessage", "Pending healthy commit"));
        Assert.True(workspace.TrySetValue("FileFilter", "keep"));
        var expectedHistory = NavigationHistory(workspace).ToArray();
        int targetLoads = secondStore.LoadCount;
        byte[] targetSource = secondStore.Snapshot();
        StackNativeRow targetRow = Rows(workspace, "Projects").Single(row => row.Key == secondId.ToString("D"));

        var heldLoad = secondStore.HoldNextLoad();
        using var canceled = new CancellationTokenSource();
        var cancelledOpen = workspace.DispatchAsync("9to1.Stacks.OpenProject", targetRow.Target, canceled.Token).AsTask();
        await heldLoad.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        canceled.Cancel(); heldLoad.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledOpen);
        Assert.Equal(targetLoads + 1, secondStore.LoadCount);
        Assert.Equal(firstId, workspace.ProjectId); Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
        Assert.Equal("Pending on healthy tab", Value(workspace, "DomainName"));
        Assert.Equal("Pending healthy commit", Value(workspace, "CommitMessage"));
        Assert.Equal("keep", Value(workspace, "FileFilter"));
        Assert.Equal("keep.txt", Value(workspace, "SourcePath")); Assert.Equal("current project source", Value(workspace, "SourceText"));
        Assert.Equal(expectedHistory, NavigationHistory(workspace)); Assert.Single(Rows(workspace, "Tabs"));

        var targetFailure = new StackFailureException(StackFailureCode.SourceCorrupt,
            "The target project's stored source is corrupt.", "target-project", recoverable: true);
        secondStore.FailNextLoad(targetFailure);
        var failedOpen = workspace.DispatchAsync("9to1.Stacks.OpenProject", targetRow.Target).AsTask();
        Assert.Same(targetFailure, await Assert.ThrowsAsync<StackFailureException>(() => failedOpen));
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.Refresh"));
        Assert.Equal(firstId, workspace.ProjectId); Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
        Assert.Equal("Pending on healthy tab", Value(workspace, "DomainName"));
        Assert.Equal("current project source", Value(workspace, "SourceText"));
        Assert.Equal(expectedHistory, NavigationHistory(workspace)); Assert.Single(Rows(workspace, "Tabs"));
        Assert.Equal(targetSource, secondStore.Snapshot());

        await workspace.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(firstId, workspace.ProjectId); Assert.Equal("current project source", Value(workspace, "SourceText"));
        targetRow = Rows(workspace, "Projects").Single(row => row.Key == secondId.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", targetRow.Target);
        Assert.Equal(secondId, workspace.ProjectId);
        Assert.Equal(2, Rows(workspace, "Tabs").Count);
        var firstTab = Rows(workspace, "Tabs").Single(row => row.Key == firstId.ToString("D"));
        await workspace.DispatchAsync("9to1.Stacks.OpenProject", firstTab.Target);
        Assert.Equal(firstId, workspace.ProjectId); Assert.Equal("Pending on healthy tab", Value(workspace, "DomainName"));

        var close = workspace.CloseOriginalAsync();
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(closeFailure.InnerExceptions, inner => inner is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, targetFailure)));
        RetainedFailures.Add((workspace, targetFailure, failedOpen, close, closeFailure));
    }

    private static IReadOnlyList<Guid> NavigationHistory(StackCuiWorkspace workspace)
    {
        var tab = typeof(StackCuiWorkspace).GetField("_tab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(workspace)!;
        return (IReadOnlyList<Guid>)tab.GetType().GetField("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tab)!;
    }

    [Fact]
    public async Task Reopened_actual_working_changes_enable_commit_and_committed_changes_do_not()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync(Actor);
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
        var saved = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync(Actor);
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
                await ClickAsync(loader, workspace, FindRow(root, Assert.Single(Rows(workspace, "Tabs"))));
                Assert.Equal(originalProject, workspace.ProjectId);
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
                var reopened = new StackEngine(new JsonFileStackProjectStore(directory)); var saved = await reopened.OpenProjectAsync(Actor);
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
    public async Task Actual_CUI_reviews_current_conflict_proposal_without_merge_and_preserves_unrelated_working_source()
    {
        var (engine, directory) = await ProjectAsync();
        Guid mainId = (await engine.OpenProjectAsync(Actor)).Id;
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "conflict.txt", new(Encoding.UTF8.GetBytes("base source"))), Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "rejected.txt", new(Encoding.UTF8.GetBytes("base to reject"))), Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "merged.txt", new(Encoding.UTF8.GetBytes("base to merge"))), Actor);
        await engine.CreateCommitAsync(mainId, "Seed conflict source", Actor);
        Guid projectId = (await engine.GetProjectSummaryAsync(Actor)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Reviewable", Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "conflict.txt", new(Encoding.UTF8.GetBytes("local source"))), Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "rejected.txt", new(Encoding.UTF8.GetBytes("local rejection side"))), Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "merged.txt", new(Encoding.UTF8.GetBytes("local merge side"))), Actor);
        await engine.CreateCommitAsync(branch.Id, "Commit conflict sides", Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "unrelated.txt", new(Encoding.UTF8.GetBytes("unrelated dirty source"))), Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "conflict.txt", new(Encoding.UTF8.GetBytes("incoming source"))), Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "rejected.txt", new(Encoding.UTF8.GetBytes("incoming rejection side"))), Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "merged.txt", new(Encoding.UTF8.GetBytes("incoming merge side"))), Actor);
        await engine.CreateCommitAsync(mainId, "Create upstream conflict", Actor);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Actor);
        var conflicts = await engine.GetConflictsAsync(branch.Id, Actor);
        StackConflict conflict = Assert.Single(conflicts, item => item.Path == "conflict.txt");
        StackConflict rejectedConflict = Assert.Single(conflicts, item => item.Path == "rejected.txt");
        StackConflict mergedConflict = Assert.Single(conflicts, item => item.Path == "merged.txt");
        StackConflictProposal proposal = await engine.ProposeConflictResolutionAsync(conflict.Id,
            "Keep reviewed proposal", new(Encoding.UTF8.GetBytes("proposed source")), "fixture reviewer", Actor);
        StackConflictProposal rejectedProposal = await engine.ProposeConflictResolutionAsync(rejectedConflict.Id,
            "Reject this proposal", new(Encoding.UTF8.GetBytes("not accepted")), "fixture reviewer", Actor);
        StackConflictProposal mergedProposal = await engine.ProposeConflictResolutionAsync(mergedConflict.Id,
            "Merge only after explicit review", new(Encoding.UTF8.GetBytes("merged proposal")), "fixture reviewer", Actor);

        await using var session = HeadlessUnitTestSession.StartNew(typeof(StackNativeTestApplication));
        var completed = await session.Dispatch<bool>(async () =>
        {
            var workspace = await WorkspaceAsync(engine);
            var loader = new CuiControlLoader(); loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
            var root = Assert.IsAssignableFrom<Control>(loader.Load(StackCuiWorkspace.LoadDocument())); loader.WireBindings(root);
            var window = new Window { Width = 1080, Height = 900, Content = root }; window.Show();
            try
            {
                await ClickAsync(loader, workspace, FindRow(root, Assert.Single(Rows(workspace, "Projects"))));
                await ClickAsync(loader, workspace, FindRow(root,
                    Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D"))));
                StackNativeRow proposalRow = Rows(workspace, "ConflictProposals").Single(row => row.Label.Contains(rejectedProposal.Id.ToString("D"), StringComparison.Ordinal));
                await ClickAsync(loader, workspace, FindRow(root, proposalRow));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksRejectConflictProposal"));
                Assert.Equal(StackConflictProposalState.Rejected,
                    (await engine.GetConflictProposalsAsync(Actor)).Single(item => item.Id == rejectedProposal.Id).State);
                Assert.False((await engine.GetConflictsAsync(branch.Id, Actor)).Single(item => item.Id == rejectedConflict.Id).IsResolved);

                proposalRow = Rows(workspace, "ConflictProposals").Single(row => row.Label.Contains(proposal.Id.ToString("D"), StringComparison.Ordinal));
                Assert.Contains(proposal.Id.ToString("D"), proposalRow.Label);
                await ClickAsync(loader, workspace, FindRow(root, proposalRow));
                Assert.Contains(proposal.Id.ToString("D"), Assert.IsType<string>(Value(workspace, "ReviewIdentity")));
                Assert.Contains(conflict.Id.ToString("D"), Assert.IsType<string>(Value(workspace, "ReviewIdentity")));
                var reviewLines = Assert.IsAssignableFrom<IReadOnlyList<StackNativeDiffRow>>(Value(workspace, "ReviewDiffLines"));
                Assert.Contains(reviewLines, line => line.BeforeText == "local source");
                Assert.Contains(reviewLines, line => line.AfterText == "proposed source");
                Assert.True(workspace.IsActionAvailable("9to1.Stacks.AcceptConflictProposalWithoutMerge"));
                Assert.True(workspace.IsActionAvailable("9to1.Stacks.RejectConflictProposal"));

                Guid currentProject = workspace.ProjectId!.Value;
                Guid currentDomain = workspace.DomainId!.Value;
                await ClickAsync(loader, workspace, NamedButton(root, "StacksAcceptConflictProposalWithoutMerge"));

                Assert.Equal(currentProject, workspace.ProjectId);
                Assert.Equal(currentDomain, workspace.DomainId);
                Assert.Equal(StackConflictProposalState.AcceptedWithoutMerge,
                    (await engine.GetConflictProposalsAsync(Actor)).Single(item => item.Id == proposal.Id).State);
                StackConflict reviewedConflict = (await engine.GetConflictsAsync(branch.Id, Actor)).Single(item => item.Id == conflict.Id);
                Assert.True(reviewedConflict.IsResolved); Assert.False(reviewedConflict.MergeCompleted);
                StackDomainSnapshot saved = await engine.GetDomainSnapshotAsync(branch.Id, Actor);
                Assert.Equal("proposed source", Encoding.UTF8.GetString(saved.WorkingChanges.Single(change => change.Path == "conflict.txt").Resource!.Content));
                Assert.Equal("unrelated dirty source", Encoding.UTF8.GetString(saved.WorkingChanges.Single(change => change.Path == "unrelated.txt").Resource!.Content));

                proposalRow = Rows(workspace, "ConflictProposals").Single(row => row.Label.Contains(mergedProposal.Id.ToString("D"), StringComparison.Ordinal));
                await ClickAsync(loader, workspace, FindRow(root, proposalRow));
                Assert.Contains(mergedConflict.Id.ToString("D"), Assert.IsType<string>(Value(workspace, "ReviewIdentity")));
                Assert.True(workspace.IsActionAvailable("9to1.Stacks.AcceptConflictProposalAndMerge"));
                Guid mergeProject = workspace.ProjectId!.Value;
                Guid mergeDomain = workspace.DomainId!.Value;
                await ClickAsync(loader, workspace, NamedButton(root, "StacksAcceptConflictProposalAndMerge"));
                Assert.Equal(mergeProject, workspace.ProjectId); Assert.Equal(mergeDomain, workspace.DomainId);
                Assert.Equal(StackConflictProposalState.AcceptedAndMerged,
                    (await engine.GetConflictProposalsAsync(Actor)).Single(item => item.Id == mergedProposal.Id).State);
                StackConflict mergeResult = (await engine.GetConflictsAsync(branch.Id, Actor)).Single(item => item.Id == mergedConflict.Id);
                Assert.True(mergeResult.IsResolved); Assert.True(mergeResult.MergeCompleted);
                saved = await engine.GetDomainSnapshotAsync(branch.Id, Actor);
                Assert.DoesNotContain(saved.WorkingChanges, change => change.Path == "merged.txt");
                Assert.Equal("merged proposal", Encoding.UTF8.GetString(saved.LocalChanges.Single(change => change.Path == "merged.txt").Resource!.Content));
                Assert.Equal("unrelated dirty source", Encoding.UTF8.GetString(saved.WorkingChanges.Single(change => change.Path == "unrelated.txt").Resource!.Content));
                StackManifest persisted = await new JsonFileStackProjectStore(directory).LoadAsync();
                Assert.Contains(persisted.Audit, entry => entry.TargetId == proposal.Id && entry.Action == "ConflictProposalAcceptedWithoutMerge");
                Assert.Contains(persisted.Audit, entry => entry.TargetId == mergedProposal.Id && entry.Action == "ConflictProposalAcceptedAndMerged");

                loader.Dispose(); await loader.WhenActionsIdleAsync(); await workspace.CloseOriginalAsync(); window.Close();
                return true;
            }
            catch { RetainedFailures.Add((workspace, loader, root, window)); throw; }
        });
        Assert.True(completed);
    }

    [Fact]
    public async Task Current_CUI_accept_route_cannot_bypass_a_root_added_after_proposal_creation()
    {
        var (engine, directory) = await ProjectAsync();
        Guid mainId = (await engine.OpenProjectAsync(Actor)).Id;
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "protected/contract.txt", new(Encoding.UTF8.GetBytes("base"))), Actor);
        await engine.CreateCommitAsync(mainId, "Seed protected conflict", Actor);
        Guid projectId = (await engine.GetProjectSummaryAsync(Actor)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Protected proposal", Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "protected/contract.txt", new(Encoding.UTF8.GetBytes("local"))), Actor);
        await engine.CreateCommitAsync(branch.Id, "Commit local side", Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "protected/contract.txt", new(Encoding.UTF8.GetBytes("incoming"))), Actor);
        await engine.CreateCommitAsync(mainId, "Commit incoming side", Actor);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Actor);
        StackConflict conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Actor));
        StackConflictProposal proposal = await engine.ProposeConflictResolutionAsync(conflict.Id,
            "Proposal predates Root", new(Encoding.UTF8.GetBytes("proposed")), "fixture reviewer", Actor);

        var workspace = await WorkspaceAsync(engine);
        await OpenAsync(workspace);
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain",
            Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        var proposalRow = Assert.Single(Rows(workspace, "ConflictProposals"));
        await workspace.DispatchAsync("9to1.Stacks.SelectConflictProposal", proposalRow.Target);
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.AcceptConflictProposalWithoutMerge"));

        var rootOwner = new StackActor("root-owner",
            new HashSet<StackCapability> { StackCapability.ViewSource, StackCapability.Contribute, StackCapability.ManageRoots },
            TwoFactorVerified: true);
        _ = await engine.CreateRootAsync(mainId, ["protected"], rootOwner);
        // The view predates this protection change; the engine must still enforce the authoritative Root.
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.AcceptConflictProposalWithoutMerge"));
        byte[] beforeManifest = await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        byte[] beforeRoots = await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.RootsRelativePath.Replace('/', Path.DirectorySeparatorChar)));

        StackFailureException refused = await Assert.ThrowsAsync<StackFailureException>(() =>
            workspace.DispatchAsync("9to1.Stacks.AcceptConflictProposalWithoutMerge", proposalRow.Target).AsTask());

        Assert.Equal(StackFailureCode.RootLocked, refused.Code);
        Assert.Equal(beforeManifest, await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(beforeRoots, await File.ReadAllBytesAsync(Path.Combine(directory,
            JsonFileStackProjectStore.RootsRelativePath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(StackConflictProposalState.PendingReview,
            (await engine.GetConflictProposalsAsync(Actor)).Single(item => item.Id == proposal.Id).State);

        var close = workspace.CloseOriginalAsync();
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(closeFailure.InnerExceptions, inner => inner is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, refused)));
        RetainedFailures.Add((workspace, refused, close, closeFailure));
    }

    [Fact]
    public async Task Conflict_proposals_are_paged_and_selected_review_target_remains_valid_off_page()
    {
        var store = new MutableProjectStore(); var engine = new StackEngine(store);
        await engine.CreateProjectAsync(new("Paged conflict reviews", StackStorageMode.Files,
            Path.Combine(Path.GetTempPath(), "stacks-paged-proposals")), Actor);
        Guid mainId = (await engine.OpenProjectAsync(Actor)).Id;
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "review.txt", new(Encoding.UTF8.GetBytes("base"))), Actor);
        await engine.CreateCommitAsync(mainId, "Seed review conflict", Actor);
        Guid projectId = (await engine.GetProjectSummaryAsync(Actor)).ProjectId;
        StackDomainSnapshot branch = await engine.CreateBranchAsync(projectId, "Review branch", Actor);
        await engine.ApplyChangeAsync(branch.Id,
            new(StackMutationKind.Upsert, "review.txt", new(Encoding.UTF8.GetBytes("local"))), Actor);
        await engine.CreateCommitAsync(branch.Id, "Commit local review side", Actor);
        await engine.ApplyChangeAsync(mainId,
            new(StackMutationKind.Upsert, "review.txt", new(Encoding.UTF8.GetBytes("incoming"))), Actor);
        await engine.CreateCommitAsync(mainId, "Commit incoming review side", Actor);
        _ = await engine.GetEffectiveTreeAsync(branch.Id, Actor);
        StackConflict conflict = Assert.Single(await engine.GetConflictsAsync(branch.Id, Actor));
        for (int index = 0; index < 101; index++)
            _ = await engine.ProposeConflictResolutionAsync(conflict.Id, $"Proposal {index:D3}",
                new(Encoding.UTF8.GetBytes($"proposal {index:D3}")), "fixture reviewer", Actor);
        StackConflictProposalPage firstPage = await engine.GetConflictProposalsPageAsync(Actor, branch.Id, 0, 100);
        StackConflictProposalPage secondPage = await engine.GetConflictProposalsPageAsync(Actor, branch.Id, 1, 100);
        StackConflictProposalPage located = await engine.GetConflictProposalsPageAsync(Actor, branch.Id, 0, 100,
            secondPage.Entries[0].Id, locateRequestedProposal: true);
        Assert.Equal(101, firstPage.TotalCount); Assert.Equal(100, firstPage.Entries.Count);
        Assert.Equal(1, secondPage.Entries.Count); Assert.Equal(1, located.PageIndex);
        Assert.Equal(secondPage.Entries[0].Id, located.RequestedProposal!.Id);
        Assert.Equal(secondPage.Entries.Select(proposal => proposal.Id), located.Entries.Select(proposal => proposal.Id));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.GetConflictProposalsPageAsync(Actor, branch.Id, 0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.GetConflictProposalsPageAsync(Actor, branch.Id, int.MaxValue, 2));

        var workspace = await WorkspaceAsync(engine); await OpenAsync(workspace);
        await workspace.DispatchAsync("9to1.Stacks.SelectDomain",
            Rows(workspace, "Domains").Single(row => row.Key == branch.Id.ToString("D")).Target);
        Assert.Equal(100, Rows(workspace, "ConflictProposals").Count);
        Assert.Equal("Proposals · page 1 of 2", Value(workspace, "ProposalPageLabel"));
        StackNativeRow selected = Rows(workspace, "ConflictProposals")[0];
        Guid proposalId = Guid.Parse(selected.Key.Split('/')[1]);
        await workspace.DispatchAsync("9to1.Stacks.SelectConflictProposal", selected.Target);
        Assert.Contains(proposalId.ToString("D"), Assert.IsType<string>(Value(workspace, "ReviewIdentity")));

        await workspace.DispatchAsync("9to1.Stacks.NextConflictProposals", null);

        Assert.Single(Rows(workspace, "ConflictProposals"));
        Assert.Equal("Proposals · page 2 of 2", Value(workspace, "ProposalPageLabel"));
        Assert.Contains(proposalId.ToString("D"), Assert.IsType<string>(Value(workspace, "ReviewIdentity")));
        Assert.True(workspace.IsActionAvailable("9to1.Stacks.AcceptConflictProposalWithoutMerge"));
        object reviewTarget = Assert.IsAssignableFrom<object>(Value(workspace, "ReviewTarget"));
        await workspace.DispatchAsync("9to1.Stacks.AcceptConflictProposalWithoutMerge", reviewTarget);

        Assert.Equal(StackConflictProposalState.AcceptedWithoutMerge,
            (await engine.GetConflictProposalsAsync(Actor)).Single(item => item.Id == proposalId).State);
        Assert.Equal(projectId, workspace.ProjectId); Assert.Equal(branch.Id, workspace.DomainId);
        await workspace.CloseOriginalAsync();
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
    private sealed class MutableProjectStore : IStackProjectStore
    {
        private StackManifest? _manifest;
        private Exception? _nextLoadFailure;
        private (TaskCompletionSource Gate, TaskCompletionSource Started)? _heldLoad;
        internal int LoadCount { get; private set; }
        public string ProjectDirectory => Path.Combine(Path.GetTempPath(), "stacks-foreign-project-binding");
        public Task<StackManifest> LoadAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            LoadCount++;
            if (_nextLoadFailure is { } failure)
            { _nextLoadFailure = null; return Task.FromException<StackManifest>(failure); }
            if (_heldLoad is { } held)
            {
                _heldLoad = null;
                return WaitForLoadAsync(held, token);
            }
            return Task.FromResult(Copy(_manifest ?? throw new InvalidOperationException("The fixture project is not created.")));
        }
        private async Task<StackManifest> WaitForLoadAsync((TaskCompletionSource Gate, TaskCompletionSource Started) held, CancellationToken token)
        {
            held.Started.TrySetResult();
            await held.Gate.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            return Copy(_manifest ?? throw new InvalidOperationException("The fixture project is not created."));
        }
        public Task CreateAsync(StackManifest manifest, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (_manifest is not null) throw new InvalidOperationException("The fixture project already exists.");
            _manifest = Copy(manifest); return Task.CompletedTask;
        }
        public Task SaveAsync(StackManifest manifest, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); _manifest = Copy(manifest); return Task.CompletedTask; }
        internal void ReplaceProjectId(Guid projectId)
        {
            var replacement = Copy(_manifest ?? throw new InvalidOperationException("The fixture project is not created."));
            replacement.ProjectId = projectId;
            foreach (var domain in replacement.Domains) domain.ProjectId = projectId;
            _manifest = replacement;
        }
        internal Guid[] AddBranches(int count)
        {
            var replacement = Copy(_manifest ?? throw new InvalidOperationException("The fixture project is not created."));
            var main = replacement.Domains.Single(domain => domain.Id == replacement.MainDomainId);
            var ids = new Guid[count];
            for (int index = 0; index < count; index++)
            {
                var branch = new StackDomainRecord
                {
                    Id = ids[index] = Guid.NewGuid(), ProjectId = replacement.ProjectId,
                    Name = $"Branch {index:D3}", Kind = StackDomainKind.Branch, ParentId = main.Id,
                    BaseRevisionId = main.HeadRevisionId ?? main.BaseRevisionId,
                };
                main.ChildDomainIds.Add(branch.Id); replacement.Domains.Add(branch);
            }
            _manifest = replacement;
            return ids;
        }
        internal void FailNextLoad(Exception failure) => _nextLoadFailure = failure;
        internal (Task Started, Action Release) HoldNextLoad()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _heldLoad = (gate, started);
            return (started.Task, () => gate.TrySetResult());
        }
        internal Guid ActiveDomainId => _manifest!.ActiveDomainId!.Value;
        internal byte[] Snapshot() => JsonSerializer.SerializeToUtf8Bytes(_manifest);
        private static StackManifest Copy(StackManifest manifest) => JsonSerializer.Deserialize<StackManifest>(
            JsonSerializer.SerializeToUtf8Bytes(manifest)) ?? throw new InvalidDataException("The fixture manifest could not be cloned.");
    }
}

public sealed class StackNativeTestApplication : Application { }
