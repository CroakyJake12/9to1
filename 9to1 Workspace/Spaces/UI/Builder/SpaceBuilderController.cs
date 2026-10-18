using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Canonical Builder controller for one UserCreated Space. Holds a revisioned
/// Draft/Active configuration with policy rules. The caller owns and awaits every operation
/// and supplies its actual authorization/lifetime boundary. This controller neither grants
/// resource access nor starts a conversation, graph or model.</summary>
public sealed class SpaceBuilderController
{
    private readonly SpaceRegistry _registry;
    private readonly object _gate = new();
    private SpaceBuilderState _state;
    private SpaceBuilderDraft _baseline = SpaceBuilderDraft.Empty;
    private long _editVersion;

    public SpaceBuilderController(SpaceRegistry registry, Guid spaceId)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        if (spaceId == Guid.Empty) throw new ArgumentException("An existing Space ID is required.", nameof(spaceId));
        _state = new(spaceId, null, null, SpaceBuilderDraft.Empty, false, true, false, false, false, false, "Load this Space to edit its Builder configuration.");
    }

    public SpaceBuilderState State { get { lock (_gate) return _state; } }
    public event Action<SpaceBuilderState>? StateChanged;

    public bool SetField(SpaceBuilderField field, string? value)
    {
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            value ??= string.Empty;
            var draft = field switch
            {
                SpaceBuilderField.Name => _state.Draft with { Name = value },
                SpaceBuilderField.Description => _state.Draft with { Description = value },
                SpaceBuilderField.IconKey => _state.Draft with { IconKey = value },
                SpaceBuilderField.Instructions => _state.Draft with { Instructions = value },
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

    public bool SetCategoryState(SpaceBuilderCategory category, Haven.Application.SpaceCapabilityCategoryState state)
    {
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var policy = _state.Draft.CategoryPolicy;
            policy = category switch
            {
                SpaceBuilderCategory.Skills => policy with { Skills = state },
                SpaceBuilderCategory.Plugins => policy with { Plugins = state },
                SpaceBuilderCategory.Apps => policy with { Apps = state },
                SpaceBuilderCategory.Models => policy with { Models = state },
                SpaceBuilderCategory.Memory => policy with { Memory = state },
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
            var draft = _state.Draft with { CategoryPolicy = policy };
            if (draft == _state.Draft) return true;
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = draft == _baseline ? "No unsaved changes." : "Unsaved changes." };
        }
        StateChanged?.Invoke(next);
        return true;
    }

    public Task<bool> LoadAsync(CancellationToken cancellationToken = default) => LoadCoreAsync(false, cancellationToken);
    public Task<bool> DiscardChangesAsync(CancellationToken cancellationToken = default) => LoadCoreAsync(true, cancellationToken);

    private async Task<bool> LoadCoreAsync(bool discardChanges, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SpaceBuilderState loading;
        lock (_gate)
        {
            if (_state.IsBusy || _state.HasChanges && !discardChanges) return false;
            loading = _state = _state with { IsLoading = true, StatusMessage = "Loading Space Builder configuration…" };
        }
        try
        {
            StateChanged?.Invoke(loading);
            var space = await _registry.GetAsync(loading.SpaceId, token).ConfigureAwait(false);
            if (space is null || space.IsArchived)
            {
                Change(state => state with { IsLoaded = false, IsReadOnly = true,
                    StatusMessage = "This Space is unavailable. Your unsaved Builder configuration has been kept." });
                return false;
            }
            Change(state =>
            {
                _baseline = SpaceBuilderDraft.From(space);
                _editVersion++;
                return state with { DraftRevision = space.Revision, ActiveRevision = space.Revision,
                    Draft = _baseline, IsLoaded = true, IsReadOnly = space.IsBuiltIn,
                    HasChanges = false, HasConflict = false,
                    StatusMessage = space.IsBuiltIn ? "Built-in Space configuration is protected." : "Space Builder configuration loaded." };
            });
            return true;
        }
        catch (Exception error)
        {
            Change(state => state with { StatusMessage = "Could not load Space Builder configuration: " + error.Message });
            throw;
        }
        finally { Change(state => state with { IsLoading = false }); }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpaceBuilderState saving;
        long editVersion;
        lock (_gate)
        {
            if (!_state.CanSave) return false;
            editVersion = _editVersion;
            saving = _state = _state with { IsSaving = true, StatusMessage = "Saving Space Builder configuration…" };
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
            if (current.Revision != saving.DraftRevision)
                throw new SpaceRevisionConflictException(current.Id, saving.DraftRevision!.Value, current.Revision);
            // Patch the canonical registry record with the Basic fields; policy/resource rules are
            // persisted through the same canonical Space revision (incremental: advanced/expert
            // policy wiring follows). Registry checks the SAME expected revision again.
            var saved = await _registry.UpdateAsync(current with
            {
                Name = saving.Draft.Name, Description = saving.Draft.Description,
                IconKey = saving.Draft.IconKey, Instructions = saving.Draft.Instructions,
                CapabilityCategoryPolicy = saving.Draft.CategoryPolicy
            }, saving.DraftRevision, cancellationToken).ConfigureAwait(false);
            Change(state =>
            {
                _baseline = SpaceBuilderDraft.From(saved);
                var draft = _editVersion == editVersion ? _baseline : state.Draft;
                return state with { DraftRevision = saved.Revision,
                    Draft = draft, HasChanges = draft != _baseline, HasConflict = false,
                    StatusMessage = draft == _baseline ? "Space Builder draft saved. Activation is not implemented." : "Earlier changes saved. Your latest edits are still unsaved." };
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
            Change(state => state with { StatusMessage = "Could not save Space Builder configuration: " + error.Message });
            return false;
        }
        catch (Exception error)
        {
            Change(state => state with { StatusMessage = "Could not save Space Builder configuration; your draft has been kept: " + error.Message });
            throw;
        }
        finally { Change(state => state with { IsSaving = false }); }
    }

    private void Change(Func<SpaceBuilderState, SpaceBuilderState> update)
    {
        SpaceBuilderState next;
        lock (_gate) next = _state = update(_state);
        StateChanged?.Invoke(next);
    }
}

public enum SpaceBuilderField { Name, Description, IconKey, Instructions }
public enum SpaceBuilderCategory { Skills, Plugins, Apps, Models, Memory }
