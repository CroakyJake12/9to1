namespace CakeOS.Cui.Runtime;

/// <summary>Version and feature contract exposed to compilers and runtime hosts.</summary>
public static class CuiRuntimeCompatibility
{
    public const string LanguageVersion = "1";
    public const string RuntimeAbiVersion = "1";

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
}
