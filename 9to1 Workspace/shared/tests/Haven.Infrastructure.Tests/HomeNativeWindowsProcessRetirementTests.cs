using System.Reflection;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Windows-only original Home lifecycle controls. No installed peer/configuration or CF grant.
public sealed class HomeNativeWindowsProcessRetirementTests
{
    [Fact] public async Task Request_stops_actual_held_start_without_disposing_borrowed_Home()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-home-process-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var principal = new HeldPrincipal();
        var home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(root,"home.json")),
            principal, new Paths(root), new HomeNativeWindowsEndpoint("haven-control-" + Guid.NewGuid().ToString("N")));
        var start = home.StartOriginalAsync(); Task? close = null;
        try
        {
            await principal.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            home.RequestOriginalProcessRetirement(); var request = home.OriginalProcessRetirementRequestTask!;
            Assert.Same(request, home.OriginalProcessRetirementRequestTask);
            Assert.Null(home.OriginalCloseTask); Assert.False(start.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => { _ = home.StartOriginalAsync(); });
        }
        finally
        {
            principal.Release.TrySetResult();
            home.RequestOriginalProcessRetirement();
            try { await start; } catch { _ = start.Exception; }
            if (home.OriginalProcessRetirementRequestTask is { } request)
                try { await request; } catch { _ = request.Exception; }
            close = home.CloseAndDrainAsync();
            try { await close; } catch { _ = close.Exception; }
            Assert.True(start.IsCompleted); Assert.True(close.IsCompleted);
            Assert.Same(close, home.CloseAndDrainAsync()); Directory.Delete(root,true);
        }
    }
    [Fact] public async Task Actual_process_cancel_callback_restoring_context_cannot_join_Home_close()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-home-process-guard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(root,"home.json")),
            new OperatingSystemPrincipalSource(), new Paths(root), new HomeNativeWindowsEndpoint("haven-control-" + Guid.NewGuid().ToString("N")));
        var earlier = ExecutionContext.Capture()!; bool invoked = false;
        var tokenSource = (CancellationTokenSource)typeof(HomeNativeWindowsComposition).GetField("_process",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(home)!;
        using var actualCallback = tokenSource.Token.Register(() => ExecutionContext.Run(earlier, _ =>
        { invoked = true; Assert.Throws<InvalidOperationException>(() => { _ = home.CloseAndDrainAsync(); }); }, null));
        try
        {
            home.RequestOriginalProcessRetirement(); await home.OriginalProcessRetirementRequestTask!;
            Assert.True(invoked); Assert.Null(home.OriginalCloseTask);
            home.RequestOriginalProcessRetirement();
            var close = home.CloseAndDrainAsync(); await close; Assert.Same(close, home.OriginalCloseTask);
        }
        finally { await home.CloseAndDrainAsync(); Directory.Delete(root,true); }
    }
    private sealed class HeldPrincipal : ITrustedHostPrincipalSource
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => new(ReadAsync(token));
        private async Task<string?> ReadAsync(CancellationToken token)
        { Reached.TrySetResult(); await Release.Task; token.ThrowIfCancellationRequested(); return await new OperatingSystemPrincipalSource().GetPrincipalAsync(token); }
    }
    private sealed class Paths(string root) : IAppPaths
    { public string DataDirectory => root; public string DatabasePath => Path.Combine(root,"db.sqlite"); public string BrowserProfileDirectory => Path.Combine(root,"browser");
      public string AttachmentsDirectory => Path.Combine(root,"attachments"); public string LogsDirectory => Path.Combine(root,"logs"); public string LegacyStatePath => Path.Combine(root,"legacy.json"); }
}
