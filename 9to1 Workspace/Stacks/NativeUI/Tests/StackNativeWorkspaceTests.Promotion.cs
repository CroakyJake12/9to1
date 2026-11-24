using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Stacks.NativeUI.Tests;

public sealed partial class StackNativeWorkspaceTests
{
    [Fact]
    public async Task Native_parent_comparison_keeps_drafts_out_and_never_writes_source()
    {
        var (engine, directory) = await ProjectAsync();
        var main = await engine.OpenProjectAsync();
        await engine.ApplyChangeAsync(main.Id, new(StackMutationKind.Upsert, "shared.txt", new(Encoding.UTF8.GetBytes("base"))), Actor);
        var targetHead = await engine.CreateCommitAsync(main.Id, "Seed", Actor);
        var branch = await engine.CreateDomainAsync(main.Id, "Feature", Actor);
        await engine.ApplyChangeAsync(branch.Id, new(StackMutationKind.Upsert, "shared.txt", new(Encoding.UTF8.GetBytes("committed"))), Actor);
        var sourceHead = await engine.CreateCommitAsync(branch.Id, "Feature commit", Actor);
        await engine.ApplyChangeAsync(branch.Id, new(StackMutationKind.Upsert, "working-only.txt", new(Encoding.UTF8.GetBytes("draft"))), Actor);
        await using var session = HeadlessUnitTestSession.StartNew(typeof(StackNativeTestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var workspace = await WorkspaceAsync(engine);
            var loader = new CuiControlLoader();
            loader.SetBindingContext(workspace); loader.SetActionDispatcher(workspace);
            var root = Assert.IsAssignableFrom<Control>(loader.Load(StackCuiWorkspace.LoadDocument()));
            loader.WireBindings(root);
            var window = new Window { Width = 1080, Height = 760, Content = root };
            window.Show();
            try
            {
                await OpenAsync(workspace);
                Assert.False(workspace.IsActionAvailable("9to1.Stacks.PreviewPromotion"));
                var selected = Assert.Single(Rows(workspace, "Domains"), row => row.Key == branch.Id.ToString("D"));
                await workspace.DispatchAsync("9to1.Stacks.SelectDomain", selected.Target);
                var manifest = Path.Combine(directory, JsonFileStackProjectStore.ManifestRelativePath);
                var before = await File.ReadAllBytesAsync(manifest);
                await ClickAsync(loader, workspace, NamedButton(root, "StacksPreviewPromotion"));
                var comparison = Assert.IsType<string>(Value(workspace, "PromotionSummary"));
                Assert.Contains(sourceHead.Id.ToString("D"), comparison);
                Assert.Contains(targetHead.Id.ToString("D"), comparison);
                Assert.Contains("Upsert: shared.txt", comparison);
                Assert.Contains("1 uncommitted changes excluded", comparison);
                Assert.DoesNotContain("working-only.txt", comparison);
                Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
                await ClickAsync(loader, workspace, NamedButton(root, "StacksBack"));
                Assert.DoesNotContain(sourceHead.Id.ToString("D"), Assert.IsType<string>(Value(workspace, "PromotionSummary")));
                loader.Dispose(); await loader.WhenActionsIdleAsync(); await workspace.CloseOriginalAsync(); window.Close();
                return true;
            }
            catch { RetainedFailures.Add((workspace, loader, root, window)); throw; }
        }, CancellationToken.None));
    }
}
