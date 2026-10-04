using System.Runtime.CompilerServices;
using Haven.Application;

namespace Dulche.Runtime.Translate;

/// <summary>
/// Stores translation sets, glossaries and recoverable jobs in Home's versioned settings store.
/// A single versioned snapshot makes each local domain operation atomic.
/// </summary>
public sealed class VersionedTranslationRepository(IVersionedSettingsStore settings) : ITranslationRepository
{
    private const string SettingsKey = "Dulche.Translate.Database.v1";
    private static readonly ConditionalWeakTable<IVersionedSettingsStore, SemaphoreSlim> Gates = new();
    private readonly SemaphoreSlim _gate = Gates.GetValue(settings, static _ => new SemaphoreSlim(1, 1));

    public async Task<T> ReadAsync<T>(Func<TranslationDatabaseState, T> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return read(Clone(state));
        }
        finally { _gate.Release(); }
    }

    public async Task<T> UpdateAsync<T>(
        Func<TranslationDatabaseState, (TranslationDatabaseState State, T Result)> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = update(Clone(current));
            ArgumentNullException.ThrowIfNull(next);
            if (next.SchemaVersion != 1)
                throw new InvalidOperationException($"Dulche Translate cannot write database schema {next.SchemaVersion}.");

            await settings.SetAsync(SettingsKey, Clone(next), cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<TranslationDatabaseState> LoadAsync(CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<TranslationDatabaseState>(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (state is null) return TranslationDatabaseState.Empty;
        if (state.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported Dulche Translate database schema {state.SchemaVersion}; the data was preserved without migration.");
        if (state.Sets is null || state.Glossaries is null || state.Jobs is null)
            throw new InvalidDataException("Dulche Translate database is missing a required collection; the data was preserved.");
        return Clone(state);
    }

    private static TranslationDatabaseState Clone(TranslationDatabaseState value) => new(
        value.SchemaVersion,
        value.Sets.ToArray(),
        value.Glossaries.ToArray(),
        value.Jobs.ToArray());
}
