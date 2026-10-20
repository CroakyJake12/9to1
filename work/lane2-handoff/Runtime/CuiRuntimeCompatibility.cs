// Lane 2 -> lane 1/root integration patch: REAL Runtime ABI/capability admission (pass 4).
//
// Apply to `framework/CUI/Runtime/CuiRuntimeCompatibility.cs` against preimage SHA
// 23d0e13059d8e1a02166022d00fc949e53b46e309c24eaa7b2773da221d96bdd.
// This is the full intended final content of that file. It supersedes the earlier constant-only
// a67a hunk (MinStatelessAbiVersion="1" / RuntimeAbiVersion="2") and adds the new
// `ValidateArtifactHostSupport` API which performs ACTUAL host-support admission
// (language, component registry, explicitly supported minimum/current ABI, all required
// capabilities) for the whole bundle before any reconstruction/effects.

using CakeOS.Cui;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>Version and feature contract exposed to compilers and runtime hosts.</summary>
public static class CuiRuntimeCompatibility
{
    public const string LanguageVersion = "1";

    /// <summary>Minimum ABI required by genuine stateless (V1-eligible) compiled documents. This is
    /// deliberately decoupled from <see cref="RuntimeAbiVersion"/>: a stateful-aware runtime
    /// advertises the higher current ABI without stamping stateless documents as stateful.</summary>
    public const string MinStatelessAbiVersion = "1";

    /// <summary>The ABI this runtime implements/advertises today. The stateful composer
    /// (local-state toggle / random-select) requires this stateful ABI; stateless documents require
    /// only <see cref="MinStatelessAbiVersion"/>.</summary>
    public const string RuntimeAbiVersion = "2";

    /// <summary>Numeric minimum supported runtime ABI (parse of <see cref="MinStatelessAbiVersion"/>).</summary>
    public const int MinStatelessAbi = 1;

    /// <summary>Numeric current (maximum supported) runtime ABI (parse of <see cref="RuntimeAbiVersion"/>).</summary>
    public const int CurrentAbi = 2;

    /// <summary>Capability name for the bounded local-state Toggle slice (document-root typed bool
    /// variables and pure-local toggle actions), used to gate compiled-artifact admission.</summary>
    public const string LocalStateToggleCapability = "local-state-toggle";

    /// <summary>Capability name for the bounded source-driven choose-once variable slice (a typed
    /// string variable selected once per mounted frame from a binding source pool).</summary>
    public const string LocalStateRandomSelectCapability = "local-state-random-select";

    public static IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "native-controls",
        "typed-properties",
        "live-bindings",
        "two-way-input",
        "live-conditionals",
        "stable-control-identity",
        "atomic-tree-replacement",
        LocalStateToggleCapability,
        LocalStateRandomSelectCapability,
    };

    public static bool IsLanguageVersionCompatible(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return true;

        var parts = version.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length is 1 or 2
            && int.TryParse(parts[0], out var major)
            && major == 1
            && (parts.Length == 1 || int.TryParse(parts[1], out var minor) && minor >= 0);
    }

    /// <summary>
    /// Admits a decoded compiled artifact against the REAL host-support contract: this runtime's
    /// supported CUI language, component registry, minimum/current ABI range and capability set.
    /// The codec validates internal structural/envelope consistency; this method verifies the host
    /// can actually execute every required requirement of the whole bundle (envelope and every
    /// document/closure) BEFORE any scene/control effect. Throws
    /// <see cref="CuiRuntimeLoadException"/> with a stable canonical diagnostic code.
    /// </summary>
    public static void ValidateArtifactHostSupport(CuiCompiledArtifact artifact, CuiSourceSpan diagnosticSpan)
    {
        if (artifact is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIB003", CuiDiagnosticSeverity.Error, "A compiled CUI artifact is required.", diagnosticSpan));
        if (artifact.Metadata is null)
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIB003", CuiDiagnosticSeverity.Error,
                "The compiled CUI artifact is missing envelope compatibility metadata.", diagnosticSpan));

        // Envelope requirements (the conservative whole-bundle boundary): this is the admitted
        // contract for the entire required closure, so a stateful sibling whose ABI/capability the
        // host does not support cannot escape selected-document checks.
        RequireLanguage(artifact.Metadata.LanguageVersion, diagnosticSpan);
        RequireRegistry(artifact.Metadata.ComponentRegistryVersion, diagnosticSpan);
        RequireAbi(artifact.Metadata.RuntimeAbiVersion, diagnosticSpan);
        RequireCapabilities(artifact.Metadata.RequiredCapabilities, diagnosticSpan);

        if (artifact.Documents is not null)
        {
            foreach (var document in artifact.Documents)
            {
                if (document?.Metadata is null) continue;
                RequireLanguage(document.Metadata.LanguageVersion, diagnosticSpan);
                RequireRegistry(document.Metadata.ComponentRegistryVersion, diagnosticSpan);
                RequireAbi(document.Metadata.RuntimeAbiVersion, diagnosticSpan);
                RequireCapabilities(document.Metadata.RequiredCapabilities, diagnosticSpan);
            }
        }
    }

    private static void RequireLanguage(string? version, CuiSourceSpan span)
    {
        if (!IsLanguageVersionCompatible(version))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR064", CuiDiagnosticSeverity.Error,
                $"Compiled CUI language version '{version}' is not supported by this runtime.", span));
    }

    private static void RequireRegistry(string? registry, CuiSourceSpan span)
    {
        if (!string.Equals(registry, CuiControlRegistry.ComponentRegistryVersion, StringComparison.Ordinal))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR066", CuiDiagnosticSeverity.Error,
                $"Compiled CUI component registry '{registry}' does not match the current registry '{CuiControlRegistry.ComponentRegistryVersion}'.", span));
    }

    private static void RequireAbi(string? abi, CuiSourceSpan span)
    {
        if (!IsAbiSupported(abi))
            throw new CuiRuntimeLoadException(new CuiDiagnostic(
                "CUIR065", CuiDiagnosticSeverity.Error,
                $"Compiled CUI runtime ABI '{abi}' is not supported by this runtime (supported range {MinStatelessAbi}..{CurrentAbi}).", span));
    }

    private static void RequireCapabilities(IEnumerable<string> capabilities, CuiSourceSpan span)
    {
        if (capabilities is null) return;
        foreach (var capability in capabilities)
        {
            if (capability is null || !Capabilities.Contains(capability))
                throw new CuiRuntimeLoadException(new CuiDiagnostic(
                    "CUIR067", CuiDiagnosticSeverity.Error,
                    $"Compiled CUI document requires unsupported capability '{capability}'.", span));
        }
    }

    private static bool IsAbiSupported(string? abi)
    {
        return int.TryParse(abi, out var value) && value >= MinStatelessAbi && value <= CurrentAbi;
    }
}
