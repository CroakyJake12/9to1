using System.Diagnostics;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeSessionLeaseTests
{
    [Fact]
    public async Task Real_os_lease_prevents_concurrent_host_and_releases_on_close()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-lease-" + Guid.NewGuid().ToString("N"));
        var paths = new Paths(root); var actor = new Actors();
        try
        {
            using (var lease = await HomeNativeSessionLease.TryAcquireAsync(actor, paths))
            {
                Assert.NotNull(lease);
                Assert.Null(await HomeNativeSessionLease.TryAcquireAsync(actor, paths));
                if (OperatingSystem.IsLinux())
                {
                    var file = Assert.Single(Directory.GetFiles(Path.Combine(root, "Home", "Runtime")));
                    var start = new ProcessStartInfo("flock") { UseShellExecute = false };
                    start.ArgumentList.Add("-n"); start.ArgumentList.Add(file); start.ArgumentList.Add("true");
                    using var process = Process.Start(start)!;
                    await process.WaitForExitAsync();
                    Assert.Equal(1, process.ExitCode);
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
                }
            }
            using var after = await HomeNativeSessionLease.TryAcquireAsync(actor, paths);
            Assert.NotNull(after);
            actor.Current = null;
            Assert.Null(await HomeNativeSessionLease.TryAcquireAsync(actor, paths));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
