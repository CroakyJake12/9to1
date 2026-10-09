using System.Reflection;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Tests;

/// <summary>Real maintained Files/Home/Dev stores with an observing actor-source wrapper.
/// The wrapper supplies no grant: actual profile/owner/resource authorization stays genuine.</summary>
public sealed partial class FilesDeveloperIdentityOriginalCallbackTests
{
    [Fact]
    public async Task Nested_actor_factory_after_held_project_read_restored_context_cannot_join_same_read()
    {
        OriginalActorProbe? probe = null;
        await using var rig = await Rig.CreateAsync(actual => probe = new(actual));
        var previous = ExecutionContext.Capture()!; Task? wrong = null; var refused = 0;
        var actual = rig.Resolve(() => true);
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        probe!.BeforeGet = () =>
        {
            ExecutionContext.Run(previous, _ =>
            { try { wrong = rig.Browser.CloseOriginalDeveloperReadsAsync(); } catch (InvalidOperationException) { refused++; } }, null);
            return null;
        };
        rig.Projects.Release.TrySetResult();
        var observed = await actual.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.True(observed.Succeeded); Assert.Null(wrong); Assert.True(refused > 0);
        probe.BeforeGet = null;
        await rig.Browser.CloseOriginalDeveloperReadsAsync().WaitAsync(Bound, TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task Nested_actual_actor_task_is_enrolled_and_faulted_OCE_siblings_survive_whole_close()
    {
        OriginalActorProbe? probe = null;
        await using var rig = await Rig.CreateAsync(actual => probe = new(actual));
        var first = new OperationCanceledException("Actual raw actor-source Task is faulted, not canceled.");
        var second = new IOException("Actual raw actor-source sibling.");
        var raw = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task? close = null;
        var actual = rig.Resolve(() => true);
        try
        {
            await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            probe!.BeforeGet = () => { entered.TrySetResult(); return raw.Task; };
            rig.Projects.Release.TrySetResult();
            await entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
            var bound = DateTimeOffset.UtcNow + Bound;
            while (!RetainsOriginalNestedTask(rig.Browser, raw.Task))
            {
                if (DateTimeOffset.UtcNow > bound) throw new TimeoutException("The genuine metadata owner never enrolled its SAME raw actor task.");
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
            Assert.False(actual.IsCompleted); close = rig.Browser.CloseOriginalDeveloperReadsAsync();
            Assert.False(close.IsCompleted); Assert.Same(close, rig.Browser.CloseOriginalDeveloperReadsAsync());
            rig.ExpectedReadFault = true; raw.TrySetException([first, second]);
            await Assert.ThrowsAnyAsync<Exception>(() => actual);
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(actual.IsFaulted); Assert.True(close.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(AllCauses(observed), value => ReferenceEquals(value, first));
            Assert.Contains(AllCauses(observed), value => ReferenceEquals(value, second));
            Assert.True(RetainsOriginalNestedTask(rig.Browser, raw.Task));
        }
        finally
        {
            raw.TrySetException([first, second]); probe!.BeforeGet = null; rig.Projects.Release.TrySetResult();
            try { await actual; } catch { rig.ExpectedReadFault = true; }
            if (close is not null) try { await close; } catch { rig.ExpectedReadFault = true; }
        }
    }
    private static bool RetainsOriginalNestedTask(FilesNativeBrowserService browser, Task raw)
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var gate = typeof(FilesNativeBrowserService).GetField("_developerIdentityGate", flags)!.GetValue(browser)!;
        lock (gate)
        {
            var owners = (System.Collections.IEnumerable)typeof(FilesNativeBrowserService).GetField("_developerIdentityOriginals", flags)!.GetValue(browser)!;
            foreach (var owner in owners)
            {
                var sources = (IEnumerable<Task>)owner.GetType().GetField("Sources", flags)!.GetValue(owner)!;
                if (sources.Any(value => ReferenceEquals(value, raw))) return true;
            }
            return false;
        }
    }
    private sealed class OriginalActorProbe(HomeLocalProfileIdentity actual) : IAuthenticatedResourceActorSource
    {
        internal Func<Task<AuthenticatedResourceActor?>?>? BeforeGet;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) =>
            BeforeGet?.Invoke() is { } original ? new(original) : actual.GetCurrentAsync(token);
    }
}
