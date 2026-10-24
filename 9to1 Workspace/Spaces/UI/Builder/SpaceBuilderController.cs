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
        if (field == SpaceBuilderField.MaximumRetrievedItems && !string.IsNullOrWhiteSpace(value) &&
            (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var limit) || limit is < 0 or > 1000))
        {
            Change(state => state with { StatusMessage = "Enter a retrieval limit from 0 to 1000, or leave it empty. The original source currently supports at most 32 selected items per request." });
            return false;
        }
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
                SpaceBuilderField.MaximumRetrievedItems => _state.Draft with { ContextPolicy = _state.Draft.ContextPolicy with
                    { MaximumRetrievedItems = string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) } },
                SpaceBuilderField.SourceOnly => _state.Draft with { ContextPolicy = _state.Draft.ContextPolicy with { SourceOnly = ReadBoolean(value) } },
                SpaceBuilderField.IncludeCurrentSelection => _state.Draft with { ContextPolicy = _state.Draft.ContextPolicy with { IncludeCurrentSelection = ReadBoolean(value) } },
                SpaceBuilderField.WebAccess => _state.Draft with { ContextPolicy = _state.Draft.ContextPolicy with { WebAccess = ReadWebAccess(value) } },
                SpaceBuilderField.InheritSources => _state.Draft with { Inheritance = _state.Draft.Inheritance with { Sources = ReadBoolean(value) } },
                SpaceBuilderField.InheritConnectedApps => _state.Draft with { Inheritance = _state.Draft.Inheritance with { ConnectedApps = ReadBoolean(value) } },
                SpaceBuilderField.InheritModelPolicy => _state.Draft with { Inheritance = _state.Draft.Inheritance with { ModelPolicy = ReadBoolean(value) } },
                SpaceBuilderField.InheritMemoryPolicy => _state.Draft with { Inheritance = _state.Draft.Inheritance with { MemoryPolicy = ReadBoolean(value) } },
                SpaceBuilderField.InheritPlugins => _state.Draft with { Inheritance = _state.Draft.Inheritance with { Plugins = ReadBoolean(value) } },
                SpaceBuilderField.InheritSkills => _state.Draft with { Inheritance = _state.Draft.Inheritance with { Skills = ReadBoolean(value) } },
                SpaceBuilderField.InheritAgents => _state.Draft with { Inheritance = _state.Draft.Inheritance with { Agents = ReadBoolean(value) } },
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
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
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
                return state with { DraftRevision = space.Revision, ActiveRevision = null,
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
            // Patch only this editor projection's fields on the canonical Space revision.
            // Unexposed model/memory/graph configuration survives; the registry checks the
            // SAME expected revision again. Saving preferences does not claim activation.
            var saved = await _registry.UpdateAsync(current with
            {
                Name = saving.Draft.Name, Description = saving.Draft.Description,
                IconKey = saving.Draft.IconKey, Instructions = saving.Draft.Instructions,
                CapabilityCategoryPolicy = saving.Draft.CategoryPolicy,
                CapabilityBindings = saving.Draft.CapabilityBindings,
                ContextPolicy = saving.Draft.ContextPolicy,
                Inheritance = saving.Draft.Inheritance,
                ContextReferences = saving.Draft.ContextReferences.ToArray(),
                Files = saving.Draft.LegacyFiles.ToArray()
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

    /// <summary>Attaches a canonical capability binding to the draft. Does NOT install,
    /// enable or invoke the capability globally; it only records the Space reference.</summary>
    public bool AttachCapability(SpaceCapabilityBinding binding)
    {
        if (binding is null) throw new ArgumentNullException(nameof(binding));
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var existing = _state.Draft.CapabilityBindings.FirstOrDefault(b => b.Kind == binding.Kind && b.CapabilityId == binding.CapabilityId);
            if (existing is not null)
            {
                if (existing.Override == binding.Override && existing.VersionPin == binding.VersionPin) return true; // already attached, no change
                // Update override/version on re-attach.
                var updated = _state.Draft.CapabilityBindings.Select(b =>
                    b.Kind == binding.Kind && b.CapabilityId == binding.CapabilityId ? binding : b).ToArray();
                var updatedDraft = _state.Draft with { CapabilityBindings = updated };
                _editVersion++;
                next = _state = _state with { Draft = updatedDraft, HasChanges = updatedDraft != _baseline,
                    StatusMessage = "Capability updated. Save to persist." };
                StateChanged?.Invoke(next);
                return true;
            }
            var bindings = _state.Draft.CapabilityBindings.Append(binding).ToArray();
            var draft = _state.Draft with { CapabilityBindings = bindings };
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = "Capability attached. Save to persist." };
        }
        StateChanged?.Invoke(next);
        return true;
    }

    /// <summary>Removes a canonical capability binding from the draft. Does NOT uninstall
    /// or delete the global capability.</summary>
    public bool DetachCapability(SpaceCapabilityKind kind, string capabilityId)
    {
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var bindings = _state.Draft.CapabilityBindings
                .Where(b => !(b.Kind == kind && b.CapabilityId == capabilityId)).ToArray();
            if (bindings.Length == _state.Draft.CapabilityBindings.Count) return true;
            var draft = _state.Draft with { CapabilityBindings = bindings };
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = "Capability detached. Save to persist." };
        }
        StateChanged?.Invoke(next);
        return true;
    }

    /// <summary>Store a canonical read reference selected by the host's genuine owning-app
    /// source. This metadata grants no body read; the request source needs new Home admission.</summary>
    public bool AttachContextReference(SpaceContextReference reference, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.ContextId == Guid.Empty || reference.Kind != SpaceContextReferenceKind.BoardPage ||
            reference.OwnerAppId != "boards" || reference.Permission != SpaceContextPermission.Read ||
            !Guid.TryParseExact(reference.CanonicalEntityId, "D", out var id) || id == Guid.Empty ||
            reference.CanonicalEntityId != id.ToString("D") ||
            !long.TryParse(reference.RevisionToken, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var version) || version < 1)
            throw new ArgumentException("Choose an original canonical Boards notebook/version.", nameof(reference));
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit || _state.DraftRevision != expectedRevision) return false;
            var existing = _state.Draft.ContextReferences.SingleOrDefault(value => value.Kind == reference.Kind &&
                value.OwnerAppId == reference.OwnerAppId && value.CanonicalEntityId == reference.CanonicalEntityId);
            if (existing is not null) reference = reference with { ContextId = existing.ContextId };
            var rows = _state.Draft.ContextReferences.Where(value => value.ContextId != reference.ContextId).Append(reference).ToArray();
            var draft = _state.Draft with { ContextReferences = Array.AsReadOnly(rows) };
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = "Canonical Boards source selected. Save to persist; its body still requires original Home approval when used." };
        }
        StateChanged?.Invoke(next); return true;
    }
    public bool DetachContextReference(Guid contextId)
    {
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var rows = _state.Draft.ContextReferences.Where(value => value.ContextId != contextId).ToArray();
            if (rows.Length == _state.Draft.ContextReferences.Count) return false;
            var draft = _state.Draft with { ContextReferences = Array.AsReadOnly(rows) };
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = "Source reference removed from the draft. The notebook remains unchanged. Save to persist." };
        }
        StateChanged?.Invoke(next); return true;
    }
    public bool DetachLegacyFileReference(string path)
    {
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var rows = _state.Draft.LegacyFiles.Where(value => !StringComparer.Ordinal.Equals(value.Path, path)).ToArray();
            if (rows.Length == _state.Draft.LegacyFiles.Count) return false;
            var draft = _state.Draft with { LegacyFiles = Array.AsReadOnly(rows) };
            _editVersion++;
            next = _state = _state with { Draft = draft, HasChanges = draft != _baseline,
                StatusMessage = "Legacy reference removed from the draft. The original file is not deleted. Save to persist." };
        }
        StateChanged?.Invoke(next); return true;
    }
    public void ReportSourceStatus(string message) => Change(state => state with { StatusMessage = message });

    private static bool ReadBoolean(string value) => bool.TryParse(value, out var parsed)
        ? parsed : throw new ArgumentException("A Space policy switch requires a boolean value.", nameof(value));

    private static SpaceWebAccessPolicy ReadWebAccess(string value) =>
        Enum.TryParse<SpaceWebAccessPolicy>(value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("A supported web access policy is required.", nameof(value));

    private void Change(Func<SpaceBuilderState, SpaceBuilderState> update)
    {
        SpaceBuilderState next;
        lock (_gate) next = _state = update(_state);
        StateChanged?.Invoke(next);
    }
}

public enum SpaceBuilderField
{
    Name, Description, IconKey, Instructions,
    SourceOnly, IncludeCurrentSelection, WebAccess, MaximumRetrievedItems,
    InheritSources, InheritConnectedApps, InheritModelPolicy, InheritMemoryPolicy,
    InheritPlugins, InheritSkills, InheritAgents
}
public enum SpaceBuilderCategory { Skills, Plugins, Apps, Models, Memory }
