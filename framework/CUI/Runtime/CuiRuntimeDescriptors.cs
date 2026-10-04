using System.Collections.Frozen;

namespace CakeOS.Cui.Runtime;

/// <summary>Read-only metadata exposed to the CUI compiler and runtime host.</summary>
public sealed record CuiRuntimeElementDescriptor(
    string Name,
    IReadOnlySet<string> Aliases,
    IReadOnlySet<string> AllowedProperties,
    bool RequiresSpecializedHost);

/// <summary>
/// Typed metadata for a property understood by the native runtime. Property parsing
/// and application remain in the runtime; consumers use these fields for validation.
/// </summary>
public sealed record CuiRuntimePropertyDescriptor(
    string Name,
    string LanguageType,
    Type RuntimeType,
    IReadOnlySet<string> SupportedElementTypes,
    bool IsAnimatable,
    bool SupportsBackdrop,
    bool IsAttached,
    bool IsWritable)
{
    /// <summary>Finite authored values for symbol properties; empty means an open value set.</summary>
    public IReadOnlySet<string> AllowedValues { get; init; } = FrozenSet<string>.Empty;
}
