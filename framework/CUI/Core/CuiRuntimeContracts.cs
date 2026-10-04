namespace CakeOS.Cui;

/// <summary>Host-owned data lookup. CUI bindings never use reflection implicitly.</summary>
public interface ICuiBindingContext
{
    bool TryGetValue(string path, out object? value);
}

/// <summary>Host-owned typed action dispatch. Markup cannot execute arbitrary methods.</summary>
public interface ICuiActionDispatcher
{
    ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default);
}

/// <summary>
/// Distinguishes retirement of the CUI view from explicit cancellation by an external caller.
/// Neither token grants authority, establishes a trusted identity, or attests action success.
/// </summary>
public readonly record struct CuiActionDispatchLifetime(
    CancellationToken ViewCancellation,
    CancellationToken CallerCancellation);

/// <summary>
/// Optional host-owned lifetime-aware action dispatch. The host retains its own admission and
/// command ownership policy; shared CUI supplies lifetime roles without choosing commands.
/// Existing direct ICuiActionDispatcher callers retain their supplied cancellation unchanged.
/// </summary>
public interface ICuiLifetimeAwareActionDispatcher : ICuiActionDispatcher
{
    ValueTask DispatchWithLifetimeAsync(string command, object? parameter,
        CuiActionDispatchLifetime lifetime);
}

/// <summary>Optional read-only host signal; null means availability is not known.</summary>
public interface ICuiActionAvailability
{
    bool? IsActionAvailable(string command);
}

public static class CuiBindingEvaluator
{
    public static object? Evaluate(CuiValue value, ICuiBindingContext context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);

        return value switch
        {
            CuiLiteralValue literal => literal.Value,
            CuiBindingValue binding when context.TryGetValue(binding.Path, out var result) => result,
            CuiBindingValue binding => binding.Fallback,
            CuiResourceValue resource => throw new InvalidOperationException(
                $"Resource '{resource.Key}' must be resolved by a resource scope before binding evaluation."),
            _ => throw new NotSupportedException($"Unsupported CUI value type '{value.GetType().Name}'.")
        };
    }
}
