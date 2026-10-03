using System.Collections.Concurrent;
using Haven.Application;
using Haven.Core;
namespace Haven.Infrastructure;
public sealed class ProviderUsageCaptureBuffer : IModelUsageCapture
{
    /// <summary>
    /// Stores usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ProviderUsageSnapshot>> _usage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the set step owned by this component.
    /// </summary>
    public void Set(ProviderUsageSnapshot usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _usage.GetOrAdd(Key(usage.ProviderId, usage.ModelName), static _ => new ConcurrentQueue<ProviderUsageSnapshot>()).Enqueue(usage);
    }

    /// <summary>
    /// Performs the consume step owned by this component.
    /// </summary>
    public ProviderUsageSnapshot? Consume(string providerId, string modelName)
    {
        var key = Key(providerId, modelName);
        if (!_usage.TryGetValue(key, out var queue)) return null;
        var values = new List<ProviderUsageSnapshot>();
        while (queue.TryDequeue(out var value)) values.Add(value);
        _usage.TryRemove(key, out _);
        return Aggregate(values);
    }

    /// <summary>
    /// Performs the consume last usage step owned by this component.
    /// </summary>
    public ProviderUsageSnapshot? ConsumeLastUsage()
    {
        foreach (var key in _usage.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (!_usage.TryGetValue(key, out var queue)) continue;
            var values = new List<ProviderUsageSnapshot>();
            while (queue.TryDequeue(out var value)) values.Add(value);
            _usage.TryRemove(key, out _);
            if (Aggregate(values) is { } aggregate) return aggregate;
        }
        return null;
    }

    /// <summary>
    /// Performs the aggregate step owned by this component.
    /// </summary>
    private static ProviderUsageSnapshot? Aggregate(IReadOnlyList<ProviderUsageSnapshot> values)
    {
        if (values.Count == 0) return null;
        var first = values[0];
        return new ProviderUsageSnapshot(
            first.ProviderId,
            first.ModelName,
            SumNullable(values.Select(item => item.InputTokens)),
            SumNullable(values.Select(item => item.OutputTokens)),
            SumNullable(values.Select(item => item.CachedTokens)),
            SumNullable(values.Select(item => item.ReasoningTokens)),
            values.All(item => item.Measurement == UsageMeasurementKind.ProviderConfirmed)
                ? UsageMeasurementKind.ProviderConfirmed
                : values.All(item => item.Measurement == UsageMeasurementKind.LocallyCalculated)
                    ? UsageMeasurementKind.LocallyCalculated
                    : UsageMeasurementKind.Estimated,
            values.Max(item => item.CapturedAt));
    }

    /// <summary>
    /// Performs the sum nullable step owned by this component.
    /// </summary>
    private static long? SumNullable(IEnumerable<long?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => value is not null) ? materialized.Sum(value => value ?? 0) : null;
    }

    /// <summary>
    /// Performs the key step owned by this component.
    /// </summary>
    private static string Key(string providerId, string modelName) => providerId.Trim().ToLowerInvariant() + "\n" + modelName.Trim().ToLowerInvariant();
}

