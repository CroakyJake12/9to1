using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Edits four details on the same canonical UserCreated Space. The caller owns and
/// awaits every operation and supplies its actual authorization/lifetime boundary. This
/// controller neither grants resource access nor starts a conversation, graph or model.</summary>
public sealed class SpaceDetailsController
{
    private readonly SpaceRegistry _registry;
    private readonly object _gate = new();
    private SpaceDetailsState _state;
    private SpaceDetailsDraft _baseline = SpaceDetailsDraft.Empty;
    private long _editVersion;

    public SpaceDetailsController(SpaceRegistry registry, Guid spaceId)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (spaceId == Guid.Empty) throw new ArgumentException("An existing Space ID is required.", nameof(spaceId));
        _state = new(spaceId, null, SpaceDetailsDraft.Empty, false, true, false, false, false, false, "Load this Space to edit its details.");
    }

    public SpaceDetailsState State { get { lock (_gate) return _state; } }
    public event Action<SpaceDetailsState>? StateChanged;

    public bool SetField(SpaceDetailsField field, string? value)
    {
        SpaceDetailsState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            value ??= string.Empty;
            var draft = field switch
            {
                SpaceDetailsField.Name => _state.Draft with { Name = value },
                SpaceDetailsField.Description => _state.Draft with { Description = value },
                SpaceDetailsField.IconKey => _state.Draft with { IconKey = value },
                SpaceDetailsField.Instructions => _state.Draft with { Instructions = value },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
            if (draft == _state.Draft) return true;
            _editVersion++;
            next = _state = _state with
            {
                Draft = draft,
                HasChanges = draft != _baseline,
                StatusMessage = _state.HasConflict ? _state.StatusMessage
                    : _state.IsSaving ? "Saving earlier changes; your latest edits are still unsaved."
                    : draft == _baseline ? "No unsaved changes." : "Unsaved changes."
            };
        }
        StateChanged?.Invoke(next);
        return true;
    }

    public Task<bool> LoadAsync(CancellationToken cancellationToken = default) => LoadCoreAsync(false, cancellationToken);

    /// <summary>Explicitly abandons the local details draft and reads the current saved revision.
    /// A failed read keeps the draft; ordinary LoadAsync never discards dirty edits.</summary>
    public Task<bool> DiscardChangesAsync(CancellationToken cancellationToken = default) => LoadCoreAsync(true, cancellationToken);

    private async Task<bool> LoadCoreAsync(bool discardChanges, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SpaceDetailsState loading;
        lock (_gate)
        {
            if (_state.IsBusy || _state.HasChanges && !discardChanges) return false;
            loading = _state = _state with { IsLoading = true, StatusMessage = "Loading Space details…" };
        }
        try
        {
            StateChanged?.Invoke(loading);
            var space = await _registry.GetAsync(loading.SpaceId, token).ConfigureAwait(false);
            if (space is null || space.IsArchived)
            {
                Change(state => state with { IsLoaded = false, IsReadOnly = true,
                    StatusMessage = "This Space is unavailable. Your unsaved details have been kept." });
                return false;
            }
            Change(state =>
            {
                _baseline = SpaceDetailsDraft.From(space);
                _editVersion++;
                return state with { LoadedRevision = space.Revision, Draft = _baseline, IsLoaded = true,
                    IsReadOnly = space.IsBuiltIn, HasChanges = false, HasConflict = false,
                    StatusMessage = space.IsBuiltIn ? "Built-in Space details are protected." : "Space details loaded." };
            });
            return true;
        }
        catch (Exception error)
        {
            Change(state => state with { StatusMessage = "Could not load Space details: " + error.Message });
            throw;
        }
        finally { Change(state => state with { IsLoading = false }); }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpaceDetailsState saving;
        long editVersion;
        lock (_gate)
        {
            if (!_state.CanSave) return false;
            editVersion = _editVersion;
            saving = _state = _state with { IsSaving = true, StatusMessage = "Saving Space details…" };
        }
        try
        {
            StateChanged?.Invoke(saving);
            var current = await _registry.GetAsync(saving.SpaceId, cancellationToken).ConfigureAwait(false);
            if (current is null || current.IsArchived || current.IsBuiltIn)
            {
                Change(state => state with { IsReadOnly = true,
                    StatusMessage = "This Space cannot be edited. Your draft has been kept." });
                return false;
            }
            if (current.Revision != saving.LoadedRevision)
                throw new SpaceRevisionConflictException(current.Id, saving.LoadedRevision!.Value, current.Revision);
            // Patch the freshly read canonical row, never an old full-record editor copy.
            // Registry checks the SAME expected revision again at its mutation boundary.
            var saved = await _registry.UpdateAsync(current with
            {
                Name = saving.Draft.Name, Description = saving.Draft.Description,
                IconKey = saving.Draft.IconKey, Instructions = saving.Draft.Instructions
            }, saving.LoadedRevision, cancellationToken).ConfigureAwait(false);
            Change(state =>
            {
                _baseline = SpaceDetailsDraft.From(saved);
                var draft = _editVersion == editVersion ? _baseline : state.Draft;
                return state with { LoadedRevision = saved.Revision, Draft = draft,
                    HasChanges = draft != _baseline, HasConflict = false,
                    StatusMessage = draft == _baseline ? "Space details saved." : "Earlier changes saved. Your latest edits are still unsaved." };
            });
            return true;
        }
        catch (SpaceRevisionConflictException)
        {
            Change(state => state with { HasConflict = true,
                StatusMessage = "This Space changed elsewhere. Your draft is unchanged. Discard changes and reload to use the saved version." });
            return false;
        }
        catch (ArgumentException error)
        {
            Change(state => state with { StatusMessage = "Could not save Space details: " + error.Message });
            return false;
        }
        catch (Exception error)
        {
            Change(state => state with { StatusMessage = "Could not save Space details; your draft has been kept: " + error.Message });
            throw;
        }
        finally { Change(state => state with { IsSaving = false }); }
    }

    private void Change(Func<SpaceDetailsState, SpaceDetailsState> update)
    {
        SpaceDetailsState next;
        lock (_gate) next = _state = update(_state);
        StateChanged?.Invoke(next);
    }
}
