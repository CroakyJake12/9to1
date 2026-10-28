using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>The existing full Write repository, restricted to the actual admitted Home
/// actor. It owns no store, copy, grant or cache. Accepted writes retain their real result.</summary>
internal sealed class ProfileBoundNotesRepository : INotesRepository
{
    private readonly INotesRepository _inner;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly AuthenticatedResourceActor _actor;
    private readonly Action<Action> _scope;
    private readonly Action<Task> _retain;
    private readonly Func<bool> _canAdmit;

    internal ProfileBoundNotesRepository(INotesRepository inner, HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, Func<bool> canAdmit)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _retain = retain ?? throw new ArgumentNullException(nameof(retain));
        _canAdmit = canAdmit ?? throw new ArgumentNullException(nameof(canAdmit));
    }

    internal INotesRepository OriginalRepository => _inner;

    private Task<T> Acquire<T>(Func<Task<T>> source)
    {
        Task<T>? actual = null;
        _scope(() => { actual = source(); _retain(actual); });
        return actual ?? throw new InvalidOperationException("The actual Write source was not acquired.");
    }
    private Task Acquire(Func<Task> source)
    {
        Task? actual = null;
        _scope(() => { actual = source(); _retain(actual); });
        return actual ?? throw new InvalidOperationException("The actual Write source was not acquired.");
    }
    private async Task DemandActorAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_canAdmit()) throw new ObjectDisposedException(nameof(ProfileBoundNotesRepository));
        var current = await Acquire(() => _profiles.GetCurrentAsync(_scope, _retain, token).AsTask()).ConfigureAwait(false);
        if (current != _actor || !_canAdmit())
            throw new UnauthorizedAccessException("Reopen Write from the current local Home profile.");
    }
    private async Task<T> ReadAsync<T>(Func<Task<T>> source, CancellationToken token)
    {
        await DemandActorAsync(token).ConfigureAwait(false);
        var result = await Acquire(source).ConfigureAwait(false);
        await DemandActorAsync(token).ConfigureAwait(false);
        return result;
    }
    private async Task<T> WriteAsync<T>(Func<Task<T>> source, CancellationToken token)
    {
        await DemandActorAsync(token).ConfigureAwait(false);
        // The operation was admitted for this actor. A post-commit identity check
        // must not disguise a real write receipt as failure or invite a duplicate retry.
        return await Acquire(source).ConfigureAwait(false);
    }
    private async Task WriteAsync(Func<Task> source, CancellationToken token)
    {
        await DemandActorAsync(token).ConfigureAwait(false);
        await Acquire(source).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) =>
        ReadAsync(() => _inner.ListAsync(token), token);
    public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) =>
        ReadAsync(() => _inner.LoadAsync(id, token), token);
    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token) =>
        WriteAsync(() => _inner.SaveAsync(document, reason, token), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => WriteAsync(() => _inner.DeleteAsync(id, token), token);
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) =>
        ReadAsync(() => _inner.GetVersionsAsync(id, token), token);
    public Task<NotesDocument?> LoadVersionAsync(Guid id, string versionId, CancellationToken token) =>
        ReadAsync(() => _inner.LoadVersionAsync(id, versionId, token), token);
    public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) =>
        ReadAsync(() => _inner.RecoverLatestAsync(id, token), token);
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) =>
        ReadAsync(() => _inner.SearchAsync(query, token), token);
}
