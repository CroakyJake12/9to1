/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Call/MultimodalSessionStore.cs, in the Vision & Voice application contract.
 * What: This file owns versioned persistence and optimistic revision checks for multimodal session metadata.
 * How: It stores metadata through Haven's shared settings contract and never stores audio, frames or captured payloads.
 * Why: A reopened surface must recover the same session identity and truthful state without creating a voice-only conversation store.
 * Maintenance: Keep the schema version explicit, reject unknown versions, and preserve conflicting or unreadable persisted values.
 */

namespace Haven.Application.Call;

/// <summary>Persists durable session metadata while leaving conversation content with the canonical conversation store.</summary>
public sealed class MultimodalSessionStore
{
    private const int CurrentSchemaVersion = 1;
    private const string KeyPrefix = "visionvoice.multimodal-session.v1.";
    private readonly IVersionedSettingsStore _settings;
    private readonly SemaphoreSlim _mutations = new(1, 1);

    /// <summary>Creates the session store over Haven's shared, versioned settings provider.</summary>
    public MultimodalSessionStore(IVersionedSettingsStore settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>Reads session metadata without creating a parallel conversation record.</summary>
    public async Task<MultimodalSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("SessionID cannot be empty.", nameof(sessionId));
        var key = Key(sessionId);
        var envelope = await ReadEnvelopeAsync(key, cancellationToken).ConfigureAwait(false);
        if (envelope is null && await ContainsKeyAsync(key, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("Existing session metadata could not be read; stored data was preserved.");
        return envelope?.Session;
    }

    /// <summary>Creates the initial durable metadata snapshot for a new session.</summary>
    public async Task<MultimodalSession> CreateAsync(MultimodalSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var validation = session.Validate();
        if (validation is not null) throw new ArgumentException(validation, nameof(session));
        if (session.Revision != 1) throw new ArgumentException("A new multimodal session must begin at revision 1.", nameof(session));

        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = Key(session.SessionId);
            var existing = await ReadEnvelopeAsync(key, cancellationToken).ConfigureAwait(false);
            if (existing is not null || await ContainsKeyAsync(key, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("A session with this SessionID already exists; existing metadata was preserved.");
            await _settings.SetAsync(key, new MultimodalSessionEnvelope(CurrentSchemaVersion, session), cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            _mutations.Release();
        }
    }

    /// <summary>Saves one new snapshot only when its expected revision still matches the stored session.</summary>
    public async Task<MultimodalSession> UpdateAsync(
        Guid sessionId,
        long expectedRevision,
        MultimodalSession updated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (sessionId == Guid.Empty) throw new ArgumentException("SessionID cannot be empty.", nameof(sessionId));
        if (updated.SessionId != sessionId) throw new ArgumentException("The updated snapshot must preserve SessionID.", nameof(updated));
        var validation = updated.Validate();
        if (validation is not null) throw new ArgumentException(validation, nameof(updated));

        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = Key(sessionId);
            var current = await ReadEnvelopeAsync(key, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                if (await ContainsKeyAsync(key, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("Existing session metadata could not be read; it was preserved.");
                throw new KeyNotFoundException("The multimodal session does not exist.");
            }
            if (current.Session.Revision != expectedRevision)
                throw new InvalidOperationException($"Session revision conflict: expected {expectedRevision}, found {current.Session.Revision}.");
            if (updated.Revision != expectedRevision + 1)
                throw new ArgumentException("An update must increment the session revision by exactly one.", nameof(updated));
            if (updated.ConversationId != current.Session.ConversationId || updated.CreatedAt != current.Session.CreatedAt)
                throw new ArgumentException("ConversationID and CreatedAt are immutable for a session.", nameof(updated));

            await _settings.SetAsync(key, new MultimodalSessionEnvelope(CurrentSchemaVersion, updated), cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _mutations.Release();
        }
    }

    private async Task<MultimodalSessionEnvelope?> ReadEnvelopeAsync(string key, CancellationToken cancellationToken)
    {
        var envelope = await _settings.GetAsync<MultimodalSessionEnvelope>(key, cancellationToken).ConfigureAwait(false);
        if (envelope is null) return null;
        if (envelope.SchemaVersion != CurrentSchemaVersion)
            throw new NotSupportedException($"Vision & Voice session schema {envelope.SchemaVersion} is not supported; stored data was preserved.");
        if (envelope.Session is null || envelope.Session.Validate() is not null)
            throw new InvalidDataException("Stored multimodal session metadata is invalid; stored data was preserved.");
        return envelope;
    }

    private async Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken)
    {
        var manifest = await _settings.ExportAsync(cancellationToken).ConfigureAwait(false);
        return manifest.Settings.Keys.Any(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static string Key(Guid sessionId) => KeyPrefix + sessionId.ToString("N");

    /// <summary>Versioned envelope; incompatible future formats are rejected instead of guessed.</summary>
    public sealed record MultimodalSessionEnvelope(int SchemaVersion, MultimodalSession Session);
}
