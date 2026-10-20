using CakeOS.Cui;

namespace CakeOS.Cui.Compiler;

/// <summary>
/// Projects the real compiler output (<see cref="CuiCompiledDocument"/>) into the typed V2
/// transport DTOs. This is the only place that translates compiler-owned compiled shapes into the
/// Core wire model; the Core codec itself never depends on the compiler assembly.
/// </summary>
public static class CuiV2Projection
{
    public static CuiCompiledArtifactDocument ToDocument(
        CuiCompiledDocument compiled,
        string sourceName,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        return new CuiCompiledArtifactDocument(
            sourceName,
            sha256,
            compiled.Metadata,
            CuiV2Semantics.From(compiled.Semantics),
            compiled.SourceMap.Select(entry => new CuiV2SourceMapEntry(
                entry.StableId, CuiV2Span.From(entry.Source), entry.AuthoredId, entry.IsUnambiguous)).ToArray(),
            new CuiV2DependencyGraph(compiled.Dependencies.Entries
                .SelectMany(pair => pair.Value.Select(dependency => new CuiV2Dependency(
                    pair.Key, dependency.Kind.ToString(), dependency.Name)))
                .ToArray()));
    }
}
