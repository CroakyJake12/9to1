using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Source-owned absence proof only. Authentic Windows signer/catalogue enrollment,
// installation and Root/Home connections remain a separate unrun platform gate.
public sealed class NativeWindowsHomeInstallerBootstrapAdmissionTests
{
    [NonWindowsBootstrapFact]
    public async Task Unsupported_platform_never_creates_profile_store_or_fabricates_a_trusted_installer()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-installer-absent-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "canonical-home.json");
        var store = new FileHomeCoreStateStore(path);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actual = new NativeWindowsHomeInstallerBootstrapAdmission(profiles);
        var raw = new List<Task>(); var gate = new object(); var failures = new List<Exception>();
        Task<NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstaller?>? inspect = null; Task? close = null;
        try
        {
            inspect = actual.InspectOriginalInstallerWithinSourceAsync(body => body(),
                value => { lock (gate) raw.Add(value); }, CancellationToken.None);
            Assert.Null(await inspect); Assert.True(inspect.IsCompletedSuccessfully);
            Assert.True(actual.TryObserveOriginalUnavailableInspection(inspect, out var reason));
            Assert.Equal("WindowsInstallerPlatformRequired", reason);
            Assert.False(actual.TryObserveOriginalUnavailableInspection(Task.FromResult<NativeWindowsHomeInstallerBootstrapAdmission.OriginalInstaller?>(null), out _));
            Assert.False(File.Exists(path)); Assert.False(Directory.Exists(root));
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { close = actual.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (inspect is not null) try { await inspect; } catch (Exception cause) { failures.Add(inspect.Exception ?? cause); }
            if (close is not null) try { await close; } catch (Exception cause) { failures.Add(close.Exception ?? cause); }
            Task[] children; lock (gate) children = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var child in children) try { await child; } catch (Exception cause) { failures.Add(child.Exception ?? cause); }
        }
        if (failures.Count != 0) throw new AggregateException("Actual unsupported bootstrap original/cleanup failed.", failures);
        Assert.Same(close, actual.OriginalClose); Assert.Same(close, actual.CloseAndDrainAsync());
        Assert.False(File.Exists(path)); Assert.False(Directory.Exists(root));
    }
}
public sealed class NonWindowsBootstrapFactAttribute : FactAttribute
{
    public NonWindowsBootstrapFactAttribute()
    { if (OperatingSystem.IsWindows()) Skip = "This control proves non-Windows absence; authentic Windows signed bootstrap material is required separately."; }
}
