namespace CakeOS.Cui.Compiler;

/// <summary>Compatibility values written into each compiled CUI artifact. This contract lives in
/// Core (below the compiler) so both the Build.Tasks emitter and runtime consumers can share it
/// without a dependency inversion through the compiler assembly.</summary>
public sealed record CuiCompilationMetadata
{
    public CuiCompilationMetadata(
        string languageVersion,
        string runtimeAbiVersion,
        string componentRegistryVersion,
        IEnumerable<string>? requiredCapabilities = null)
    {
        LanguageVersion = RequireVersion(languageVersion, nameof(languageVersion));
        RuntimeAbiVersion = RequireVersion(runtimeAbiVersion, nameof(runtimeAbiVersion));
        ComponentRegistryVersion = RequireVersion(componentRegistryVersion, nameof(componentRegistryVersion));
        RequiredCapabilities = Array.AsReadOnly((requiredCapabilities ?? [])
            .Select(value => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Capability names cannot be empty.", nameof(requiredCapabilities))
                : value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
    }

    public string LanguageVersion { get; }
    public string RuntimeAbiVersion { get; }
    public string ComponentRegistryVersion { get; }
    public IReadOnlyList<string> RequiredCapabilities { get; }

    private static string RequireVersion(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty compatibility version is required.", parameterName)
            : value.Trim();
}
