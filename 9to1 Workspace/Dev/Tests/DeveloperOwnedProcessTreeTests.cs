using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>IX-BUILD-003: Stop the test and terminate only its owned tree; keep the server and orchestration session alive and report the real result.
/// Proves Stop ends only the test session's tree, keeps a concurrently running server session alive, and reports per-process results.</summary>
public sealed class DeveloperOwnedProcessTreeTests
{
    private static readonly Guid TestSession = Guid.NewGuid();
    private static readonly Guid ServerSession = Guid.NewGuid();

    [Fact]
    public void StopTerminatesOnlyTheTestSessionTree()
    {
        // Test run: root 100 spawns a worker 101.
        var test = DeveloperProcessSession.Start(TestSession, "test-suite", 100, "dotnet-test")
            .RegisterChild(101, 100, "testhost");

        // Preview server in another split: root 200 spawns 201.
        var server = DeveloperProcessSession.Start(ServerSession, "preview-server", 200, "dotnet-watch")
            .RegisterChild(201, 200, "server-worker");

        var result = test.Stop("Failed: 3 tests failed");

        // Only the test tree's PIDs are stopped.
        Assert.Equal(TestSession, result.SessionId);
        Assert.Equal([100, 101], result.StoppedPids.OrderBy(p => p));
        // The server session still owns its processes; they were not touched.
        Assert.True(server.Owns(200));
        Assert.True(server.Owns(201));
        Assert.DoesNotContain(200, result.StoppedPids);
        Assert.DoesNotContain(201, result.StoppedPids);
    }

    [Fact]
    public void StopReportsTheRealResult()
    {
        var test = DeveloperProcessSession.Start(TestSession, "test-suite", 100, "dotnet-test");
        var result = test.Stop("Passed: 42 tests passed");
        Assert.Equal("Passed: 42 tests passed", result.RealResult);
    }

    [Fact]
    public void RegisterChildRejectsForeignParentAndDuplicatePid()
    {
        var test = DeveloperProcessSession.Start(TestSession, "test-suite", 100, "dotnet-test");
        // A child may only attach to a running process within this session.
        Assert.Throws<InvalidOperationException>(() => test.RegisterChild(101, 999, "orphan"));
        var withChild = test.RegisterChild(101, 100, "testhost");
        Assert.Throws<InvalidOperationException>(() => withChild.RegisterChild(101, 100, "dupe"));
    }

    [Fact]
    public void SessionMustContainItsRootPid()
    {
        // A tree whose root is absent from its handles is invalid.
        var session = new DeveloperProcessSession(TestSession, "cfg", 100, [new DeveloperProcessHandle(101, 100, "child", DeveloperProcessState.Running)]);
        Assert.Throws<InvalidOperationException>(() => session.Capture());
    }

    [Fact]
    public void MissingRealResultIsRejected()
    {
        var test = DeveloperProcessSession.Start(TestSession, "test-suite", 100, "dotnet-test");
        Assert.Throws<InvalidDataException>(() => test.Stop(" "));
    }

    [Fact]
    public void StoppingOneSessionLeavesTheOtherRunning()
    {
        var test = DeveloperProcessSession.Start(TestSession, "test-suite", 100, "dotnet-test");
        var server = DeveloperProcessSession.Start(ServerSession, "preview-server", 200, "dotnet-watch");

        // Stopping the test does not affect the orchestration/server session's ownership.
        _ = test.Stop("Completed");
        Assert.True(server.Owns(200));
        Assert.False(server.Owns(100));
    }
}
