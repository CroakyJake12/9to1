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
    [LinuxAdminFact]
    public async Task Actual_settled_signed_code_exports_private_complete_unit_and_provenance_without_changing_canonical_state()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeNativeHelper: true); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var before = File.ReadAllBytes(rig.StatePath);
        var request = ArtifactRequest(rig, Guid.NewGuid());
        var result = await new LinuxHomeRootServiceUnitArtifactOwner(rig.Owner).ExportAsync(request, rig.Token);
        var unit = Path.Combine(result.ArtifactDirectory, LinuxHomeRegisteredRootServiceContext.ServiceUnit);
        var provenance = Path.Combine(result.ArtifactDirectory, "provenance.json");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(result.ArtifactDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(unit));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(provenance));
        Assert.Equal(result.UnitSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(unit))));
        Assert.Equal(result.ProvenanceSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(provenance))));
        Assert.Contains(LinuxHomeRootServiceUnitArtifactOwner.QuoteSystemdArgument(rig.StatePath), File.ReadAllText(unit));
        Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(unit));
        using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(provenance));
        Assert.False(record.RootElement.GetProperty("serviceInstalled").GetBoolean());
        Assert.False(record.RootElement.GetProperty("serviceStarted").GetBoolean());
        Assert.False(record.RootElement.GetProperty("runtimeReady").GetBoolean());
        Assert.Equal(rig.StatePath, record.RootElement.GetProperty("machineStatePath").GetString());
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(result.ArtifactDirectory).Count());
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
        await Assert.ThrowsAnyAsync<IOException>(() => new LinuxHomeRootServiceUnitArtifactOwner(rig.Owner).ExportAsync(request, rig.Token));
        Assert.Equal(result.UnitSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(unit))));
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }
    [LinuxAdminFact]
    public async Task Existing_foreign_artifact_bundle_is_never_adopted_or_overwritten()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeNativeHelper: true); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var request = ArtifactRequest(rig, Guid.NewGuid());
        var foreign = Path.Combine(request.ExistingPrivateArtifactParent, "root-unit-" + request.ArtifactId.ToString("N"));
        Directory.CreateDirectory(foreign, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(foreign, "unrelated"), "preserve foreign bytes");
        await Assert.ThrowsAnyAsync<IOException>(() => new LinuxHomeRootServiceUnitArtifactOwner(rig.Owner).ExportAsync(request, rig.Token));
        Assert.Equal("preserve foreign bytes", File.ReadAllText(Path.Combine(foreign, "unrelated")));
        Assert.Single(Directory.EnumerateFileSystemEntries(foreign));
    }
    [LinuxAdminFact]
    public async Task A_symlink_output_parent_cannot_redirect_the_actual_export()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeNativeHelper: true); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var request = ArtifactRequest(rig, Guid.NewGuid());
        var link = Path.Combine(rig.Root, "output-link"); Directory.CreateSymbolicLink(link, request.ExistingPrivateArtifactParent);
        await Assert.ThrowsAnyAsync<Exception>(() => new LinuxHomeRootServiceUnitArtifactOwner(rig.Owner).ExportAsync(
            request with { ExistingPrivateArtifactParent = link }, rig.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(request.ExistingPrivateArtifactParent));
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }
    private static LinuxHomeRootServiceUnitArtifactOwner.Request ArtifactRequest(LinuxHomeBootstrapActivationOwnerTests.Rig rig, Guid artifact)
    {
        var output = Path.Combine(rig.Root, "artifacts"); Directory.CreateDirectory(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var control = Path.Combine(rig.Root, "control"); Directory.CreateDirectory(control,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);
        return new(rig.Request(), control, output, artifact);
    }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
