namespace HavenOS.Apps.Dev;

/// <summary>A single capability verdict with evidence. A false verdict always names why, so a missing dependency shows evidence rather than a working-looking claim.</summary>
public sealed record DeveloperToolchainCapability(string Name, bool Available, string? Reason)
{
    public static DeveloperToolchainCapability Yes(string name) => new(name, true, null);
    public static DeveloperToolchainCapability No(string name, string reason) => new(name, false, reason);
}

/// <summary>A capability report for one discovered toolchain: real version, provenance, and per-capability availability derived from the toolchain's
/// state and populated fields. CanBuild/CanDebug/CanIntellisense are true only when every required capability is genuinely available.</summary>
public sealed record DeveloperToolchainCapabilityReport(string ToolchainId, string? Version, string Provenance,
    IReadOnlyList<DeveloperToolchainCapability> Capabilities)
{
    public const int MaximumCapabilities = 32;

    public DeveloperToolchainCapabilityReport Capture()
    {
        if (string.IsNullOrWhiteSpace(ToolchainId)) throw new InvalidDataException("A toolchain report requires an identity.");
        if (string.IsNullOrWhiteSpace(Provenance)) throw new InvalidDataException("A toolchain report requires provenance evidence.");
        var caps = (Capabilities ?? []).Select(c => c ?? throw new InvalidDataException("A capability entry is missing.")).ToArray();
        if (caps.Length == 0 || caps.Length > MaximumCapabilities) throw new InvalidDataException("A toolchain report must list its capabilities within bounds.");
        if (caps.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != caps.Length) throw new InvalidOperationException("Duplicate capability name in a toolchain report.");
        // A false capability must carry evidence.
        if (caps.Any(c => !c.Available && string.IsNullOrWhiteSpace(c.Reason))) throw new InvalidDataException("An unavailable capability must state why.");
        return this with { Capabilities = Array.AsReadOnly(caps) };
    }

    private bool IsAvailable(string name) => Capture().Capabilities.Any(c => StringComparer.Ordinal.Equals(c.Name, name) && c.Available);
    private string? ReasonFor(string name) => Capture().Capabilities.FirstOrDefault(c => StringComparer.Ordinal.Equals(c.Name, name))?.Reason;

    /// <summary>Building requires a compiler and runtime. Debugging additionally requires a debugger. IntelliSense requires a language server.</summary>
    public bool CanBuild => IsAvailable("compiler") && IsAvailable("runtime");
    public string? BuildBlocker => !IsAvailable("compiler") ? ReasonFor("compiler") ?? "No compiler available." : !IsAvailable("runtime") ? ReasonFor("runtime") ?? "No runtime available." : null;

    public bool CanDebug => CanBuild && IsAvailable("debugger");
    public string? DebugBlocker => BuildBlocker ?? (IsAvailable("debugger") ? null : ReasonFor("debugger") ?? "No debugger available.");

    public bool CanIntellisense => IsAvailable("language-server");
    public string? IntellisenseBlocker => CanIntellisense ? null : ReasonFor("language-server") ?? "No language server available.";
}

/// <summary>Assembles discovered toolchains into honest capability reports. Availability is derived from the toolchain's state and populated
/// fields — never assumed — so a missing/mismatched/unverified dependency yields a named blocker instead of an optimistic verdict.</summary>
public static class DeveloperToolchainManager
{
    public static DeveloperToolchainCapabilityReport Report(DeveloperToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);
        var state = toolchain.State;

        // Compiler/runtime are the core build capability. A non-ready core state blocks build with the state named as evidence.
        var coreReady = state is DeveloperToolchainState.Ready or DeveloperToolchainState.MissingDebugger or DeveloperToolchainState.MissingLanguageServer or DeveloperToolchainState.MissingPackageManager;
        var compiler = coreReady && !string.IsNullOrWhiteSpace(toolchain.Executable)
            ? DeveloperToolchainCapability.Yes("compiler")
            : DeveloperToolchainCapability.No("compiler", Describe(state, "compiler"));
        var runtime = coreReady
            ? DeveloperToolchainCapability.Yes("runtime")
            : DeveloperToolchainCapability.No("runtime", Describe(state, "runtime"));

        var debugger = !string.IsNullOrWhiteSpace(toolchain.Debugger) && state != DeveloperToolchainState.MissingDebugger
            ? DeveloperToolchainCapability.Yes("debugger")
            : DeveloperToolchainCapability.No("debugger", state == DeveloperToolchainState.MissingDebugger ? "The toolchain reports no debugger." : "No debugger is registered for this toolchain.");

        var languageServer = !string.IsNullOrWhiteSpace(toolchain.LanguageServer) && state != DeveloperToolchainState.MissingLanguageServer
            ? DeveloperToolchainCapability.Yes("language-server")
            : DeveloperToolchainCapability.No("language-server", state == DeveloperToolchainState.MissingLanguageServer ? "The toolchain reports no language server." : "No language server is registered for this toolchain.");

        var packageManager = !string.IsNullOrWhiteSpace(toolchain.PackageManager) && state != DeveloperToolchainState.MissingPackageManager
            ? DeveloperToolchainCapability.Yes("package-manager")
            : DeveloperToolchainCapability.No("package-manager", state == DeveloperToolchainState.MissingPackageManager ? "The toolchain reports no package manager." : "No package manager is registered for this toolchain.");

        var version = string.IsNullOrWhiteSpace(toolchain.Version)
            ? null
            : toolchain.Version;

        return new DeveloperToolchainCapabilityReport(toolchain.ToolchainId, version, toolchain.Provenance,
            [compiler, runtime, debugger, languageServer, packageManager]).Capture();
    }

    private static string Describe(DeveloperToolchainState state, string capability) => state switch
    {
        DeveloperToolchainState.MissingCompilerOrRuntime => "The " + capability + " is missing from this toolchain.",
        DeveloperToolchainState.VersionMismatch => "The " + capability + " version does not satisfy the project requirement.",
        DeveloperToolchainState.RegistryUnavailable => "The toolchain registry is unavailable; " + capability + " cannot be verified.",
        DeveloperToolchainState.AuthenticationRequired => "Authentication is required before the " + capability + " can be used.",
        DeveloperToolchainState.RestoreIncomplete => "Restore is incomplete; the " + capability + " is not yet available.",
        DeveloperToolchainState.CapabilityUnavailable => "This environment does not provide the " + capability + ".",
        _ => "The " + capability + " is unavailable.",
    };
}
