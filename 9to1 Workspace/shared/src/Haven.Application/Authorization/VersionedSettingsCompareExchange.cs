namespace Haven.Application;

public sealed record SettingsCompareExchangeResult(bool Exchanged, string? CurrentJson, int StoreVersion);

/// <summary>Exact per-key serialized compare/exchange. Null expected means absent; null replacement means delete.</summary>
public interface IVersionedSettingsCompareExchange
{
    Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
        string? replacementJson, CancellationToken cancellationToken);
}

public sealed record SettingsGuardedCompareExchangeResult(bool Exchanged, string? CurrentJson,
    int StoreVersion, string? ConflictingGuardKey = null)
{
    public bool AdmissionRejected { get; init; }
}

public enum SettingsCommitPhase { Admission, Publication }
public sealed record SettingsCommitContext(SettingsStoreIdentity StoreIdentity, int StoreVersion, SettingsCommitPhase Phase);

/// <summary>Trusted in-process guard. Runs while the settings lease is held; must not call back into this
/// settings store or its evidence providers. A separate authority store is not globally atomic with this one.</summary>
public interface ISettingsCommitAdmission
{
    ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken);
}

/// <summary>Checks exact guard values and replaces one key inside the same durable store transaction.</summary>
public interface IVersionedSettingsGuardedCompareExchange : IVersionedSettingsCompareExchange
{
    Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, CancellationToken cancellationToken);
    Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, ISettingsCommitAdmission admission,
        CancellationToken cancellationToken) => Task.FromResult(new SettingsGuardedCompareExchangeResult(false, null, 0) { AdmissionRejected = true });
}
