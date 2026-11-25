using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>APP-BUILD-002: the Toolchain Manager reports real versions, capabilities and provenance; missing dependencies show evidence
/// instead of a working-looking preview claim. Proves a ready toolchain reports all capabilities true with provenance, a missing language server
/// disables IntelliSense while build stays available, and a version mismatch blocks build rather than claiming success.</summary>
public sealed class DeveloperToolchainManagerTests
{
    private static DeveloperToolchain Toolchain(DeveloperToolchainState state, string? debugger = "dap", string? languageServer = "lsp", string? packageManager = "pkg") =>
        new("dotnet-sdk", "C#", "dotnet", "/usr/bin/dotnet", "8.0.100", ["x64"], debugger, languageServer, packageManager, "local", state, "sha256:abc/provenance");

    [Fact]
    public void ReadyToolchainReportsAllCapabilitiesWithProvenance()
    {
        var report = DeveloperToolchainManager.Report(Toolchain(DeveloperToolchainState.Ready));
        Assert.Equal("8.0.100", report.Version);
        Assert.Equal("sha256:abc/provenance", report.Provenance);
        Assert.True(report.CanBuild);
        Assert.Null(report.BuildBlocker);
        Assert.True(report.CanDebug);
        Assert.True(report.CanIntellisense);
    }

    [Fact]
    public void MissingLanguageServerDisablesIntelliSenseButKeepsBuild()
    {
        var report = DeveloperToolchainManager.Report(Toolchain(DeveloperToolchainState.MissingLanguageServer, languageServer: null));
        Assert.True(report.CanBuild);
        Assert.False(report.CanIntellisense);
        Assert.Contains("language server", report.IntellisenseBlocker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionMismatchBlocksBuildRatherThanClaimingSuccess()
    {
        var report = DeveloperToolchainManager.Report(Toolchain(DeveloperToolchainState.VersionMismatch));
        Assert.False(report.CanBuild);
        Assert.Contains("version", report.BuildBlocker, StringComparison.OrdinalIgnoreCase);
        // Debugging depends on build, so it is blocked too.
        Assert.False(report.CanDebug);
    }

    [Fact]
    public void MissingDebuggerBlocksDebugButKeepsBuildAndIntellisense()
    {
        var report = DeveloperToolchainManager.Report(Toolchain(DeveloperToolchainState.MissingDebugger, debugger: null));
        Assert.True(report.CanBuild);
        Assert.False(report.CanDebug);
        Assert.Contains("debugger", report.DebugBlocker, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.CanIntellisense);
    }

    [Fact]
    public void MissingCompilerOrRuntimeBlocksBuildWithEvidence()
    {
        var report = DeveloperToolchainManager.Report(Toolchain(DeveloperToolchainState.MissingCompilerOrRuntime));
        Assert.False(report.CanBuild);
        Assert.Contains("missing", report.BuildBlocker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportRequiresProvenanceEvidence()
    {
        var noProvenance = new DeveloperToolchain("tc", "C#", "dotnet", "/usr/bin/dotnet", "8.0", ["x64"], "dap", "lsp", "pkg", "local", DeveloperToolchainState.Ready, " ");
        Assert.Throws<InvalidDataException>(() => DeveloperToolchainManager.Report(noProvenance));
    }

    [Fact]
    public void UnavailableCapabilityWithoutReasonIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperToolchainCapabilityReport("tc", "1", "prov",
            [new DeveloperToolchainCapability("compiler", false, null)]).Capture());
    }
}
