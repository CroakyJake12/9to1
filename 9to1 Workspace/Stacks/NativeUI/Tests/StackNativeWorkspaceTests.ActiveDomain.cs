using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Stacks.NativeUI.Tests;

public sealed partial class StackNativeWorkspaceTests
{
    [Fact]
    public async Task Actual_CUI_browsing_history_and_inactive_working_edit_preserve_default_until_explicit_SetActive()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        var branch = await engine.CreateDomainAsync(main.Id, "Independent feature", Actor);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(StackNativeTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var workspace = await WorkspaceAsync(engine);
            var loader = new CuiControlLoader(); loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
            var root = Assert.IsAssignableFrom<Control>(loader.Load(StackCuiWorkspace.LoadDocument())); loader.WireBindings(root);
            var window = new Window { Width = 1080, Height = 760, Content = root }; window.Show();
            try
            {
                await ClickAsync(loader, workspace, FindRow(root, Assert.Single(Rows(workspace, "Projects"))));
                Assert.Equal(main.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
                var selected = Assert.Single(Rows(workspace, "Domains"), row => row.Key == branch.Id.ToString("D"));
                await ClickAsync(loader, workspace, FindRow(root, selected));
                Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
                Assert.Contains("Main · main", Assert.IsType<string>(Value(workspace, "ActiveDomainLabel")));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksBack"));
                Assert.Equal(main.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksForward"));
                Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksNewTextFile"));
                Field(root, "StacksSourcePath").Text = "inactive-feature.txt";
                Field(root, "StacksSourceEditor").Text = "working bytes in the browsed inactive branch\n";
                Assert.False(workspace.IsActionAvailable("9to1.Stacks.SetActive"));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksApplyText"));
                Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(main.Id, workspace.ActiveDomainId);
                var beforeActivation = new StackEngine(new JsonFileStackProjectStore(directory));
                Assert.Equal(main.Id, (await beforeActivation.OpenProjectAsync()).Id);
                var actualInactive = Assert.Single(await beforeActivation.GetLineageAsync(), domain => domain.Id == branch.Id);
                Assert.Equal(Encoding.UTF8.GetBytes("working bytes in the browsed inactive branch\n"),
                    Assert.Single(actualInactive.WorkingChanges).Resource!.Content);
                Assert.False((await beforeActivation.GetEffectiveTreeAsync(main.Id, Actor)).Files.ContainsKey("inactive-feature.txt"));
                Assert.True(workspace.IsActionAvailable("9to1.Stacks.SetActive"));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksSetActive"));
                Assert.Equal(branch.Id, workspace.DomainId); Assert.Equal(branch.Id, workspace.ActiveDomainId);
                Assert.Contains("Independent feature", Assert.IsType<string>(Value(workspace, "ActiveDomainLabel")));
                Assert.False(workspace.IsActionAvailable("9to1.Stacks.SetActive"));
                var cold = new StackEngine(new JsonFileStackProjectStore(directory));
                var reopened = await cold.OpenProjectAsync();
                Assert.Equal(main.ProjectId, reopened.ProjectId); Assert.Equal(branch.Id, reopened.Id);
                Assert.Equal(Encoding.UTF8.GetBytes("working bytes in the browsed inactive branch\n"),
                    Assert.Single(reopened.WorkingChanges).Resource!.Content);
                loader.Dispose(); await loader.WhenActionsIdleAsync(); await workspace.CloseOriginalAsync(); window.Close();
                return true;
            }
            catch { RetainedFailures.Add((workspace, loader, root, window)); throw; }
        }));
    }

    [Fact]
    public async Task Two_native_surfaces_keep_independent_browsed_domains_when_same_engine_active_context_changes()
    {
        var (engine, directory) = await ProjectAsync(); var main = await engine.OpenProjectAsync();
        var branch = await engine.CreateDomainAsync(main.Id, "Feature", Actor);
        var first = await WorkspaceAsync(engine); var second = await WorkspaceAsync(engine);
        await OpenAsync(first); await OpenAsync(second);
        var branchRow = Assert.Single(Rows(first, "Domains"), row => row.Key == branch.Id.ToString("D"));
        await first.DispatchAsync("9to1.Stacks.SelectDomain", branchRow.Target);
        Assert.Equal(branch.Id, first.DomainId); Assert.Equal(main.Id, second.DomainId);
        Assert.Equal(main.Id, first.ActiveDomainId); Assert.Equal(main.Id, second.ActiveDomainId);
        var oldSecond = Assert.Single(Rows(second, "Domains"), row => row.Key == main.Id.ToString("D"));
        await first.DispatchAsync("9to1.Stacks.SetActive", null);
        await second.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(branch.Id, first.DomainId); Assert.Equal(main.Id, second.DomainId);
        Assert.Equal(branch.Id, second.ActiveDomainId);
        Assert.Throws<InvalidOperationException>(() => { _ = second.DispatchAsync("9to1.Stacks.SelectDomain", oldSecond.Target); });
        var current = Assert.Single(Rows(second, "Domains"), row => row.Key == main.Id.ToString("D"));
        Assert.Equal(oldSecond.Key, current.Key); Assert.NotSame(oldSecond.Target, current.Target);
        await second.DispatchAsync("9to1.Stacks.SetActive", null);
        await first.DispatchAsync("9to1.Stacks.Refresh", null);
        Assert.Equal(branch.Id, first.DomainId); Assert.Equal(main.Id, first.ActiveDomainId);
        Assert.Equal(main.Id, second.DomainId); Assert.Equal(main.Id, second.ActiveDomainId);
        var cold = await new StackEngine(new JsonFileStackProjectStore(directory)).OpenProjectAsync();
        Assert.Equal(main.Id, cold.Id); Assert.Equal(main.ProjectId, cold.ProjectId);
        await first.CloseOriginalAsync(); await second.CloseOriginalAsync();
    }
}
