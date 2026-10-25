using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Haven.Infrastructure.Native.Linux;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Unit text and real retained signed-activation/file evidence only.
/// No systemd, service, native launcher or privileged child is executed.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomeRootServiceUnitArtifactOwnerTests
{
    private sealed class LinuxAdminFactAttribute : FactAttribute
    { public LinuxAdminFactAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires actual Linux UID0 isolated signed-file fixture."; } }
    [Fact]
    public void Unit_uses_literal_argv_without_shell_environment_or_specifier_expansion()
    {
        var unit = LinuxHomeRootServiceUnitArtifactOwner.RenderUnit(["/protected/code%h/root", "--home-root-service", "/private/a $HOME %n \"quoted\""], "/protected/code%h");
        Assert.Contains("Type=simple\n", unit); Assert.Contains("Restart=no\n", unit);
        Assert.Contains("ExecStart=:\"/protected/code%%h/root\" \"--home-root-service\" \"/private/a $HOME %%n \\\"quoted\\\"\"", unit);
        Assert.Contains("WorkingDirectory=\"/protected/code%%h\"", unit);
        Assert.DoesNotContain("Environment=", unit); Assert.DoesNotContain("/bin/sh", unit);
        Assert.DoesNotContain("Type=notify", unit); Assert.DoesNotContain("WantedBy=", unit);
    }
    [Theory]
    [InlineData("bad\nExecStart=/unowned")]
    [InlineData("bad\rvalue")]
    [InlineData("bad\0value")]
    public void Unit_rejects_control_character_injection(string value)
        => Assert.Throws<ArgumentException>(() => LinuxHomeRootServiceUnitArtifactOwner.QuoteSystemdArgument(value));
    [Theory]
    [InlineData("/etc/systemd/system")]
    [InlineData("/run/systemd/system")]
    [InlineData("/usr/lib/systemd/system")]
    [InlineData("/usr/local/lib/systemd/system")]
    [InlineData("/lib/systemd/system")]
    [InlineData("/tmp/../etc/systemd/system")]
    public void Export_cannot_target_systemd_configuration(string path)
        => Assert.Throws<UnauthorizedAccessException>(() => LinuxHomeRootServiceUnitArtifactOwner.RequireArtifactParent(path));
    [LinuxAdminFact]
    public async Task Real_settled_signed_fixture_without_native_helper_cannot_export_a_misleading_unit()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var before = File.ReadAllBytes(rig.StatePath);
        var output = Path.Combine(rig.Root, "artifacts"); Directory.CreateDirectory(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var control = Path.Combine(rig.Root, "control"); Directory.CreateDirectory(control,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);
        var owner = new LinuxHomeRootServiceUnitArtifactOwner(rig.Owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExportAsync(new(rig.Request(), control, output, Guid.NewGuid()), rig.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output)); Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
