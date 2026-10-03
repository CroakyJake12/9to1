using Haven.Application;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeControlledLaunchTests
{
    [Fact]
    public async Task Unconfigured_launch_authority_cannot_accept_even_a_current_process_or_claimed_host_role()
    {
        var unavailable = new UnavailableHomeNativeControlledLaunchAuthority();
        var observation = new HomeNativeControlledLaunchObservation(Environment.ProcessId, "observed-principal",
            "observed-start", "immutable-executable", "profile", Guid.NewGuid().ToString("D"), "os.shell", "home.session-host");
        Assert.False(await unavailable.IsCurrentAsync(observation, default));
        Assert.False(await unavailable.IsCurrentAsync(observation with { RequiredRole = "another-role" }, default));
    }
}
