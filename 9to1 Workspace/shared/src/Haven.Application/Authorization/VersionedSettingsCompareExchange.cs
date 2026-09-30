namespace Haven.Application;

public sealed record SettingsCompareExchangeResult(bool Exchanged, string? CurrentJson, int StoreVersion);

/// <summary>Exact per-key serialized compare/exchange. Null expected means absent; null replacement means delete.</summary>
public interface IVersionedSettingsCompareExchange
{
    Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
        string? replacementJson, CancellationToken cancellationToken);
}
