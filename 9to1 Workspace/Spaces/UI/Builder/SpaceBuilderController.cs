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

    public bool SelectEditorView(SpaceBuilderEditorView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        Change(state => state with { EditorView = view });
        return true;
    }

    public bool SelectExpertSection(SpaceBuilderExpertSection section)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        Change(state => state with { EditorView = SpaceBuilderEditorView.Expert, ExpertSection = section });
        return true;
    }

    /// <summary>Publishes one edit from the shared NodeEditor into this Space's unsaved Draft.
    /// The caller cannot write the registry directly; the normal same-revision Save path owns persistence.</summary>
    public bool SetLayoutDocument(SpaceLayoutDocument layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Nodes is null || layout.Edges is null || layout.Nodes.Count > SpaceLayoutDocument.MaximumNodes ||
            layout.Edges.Count > SpaceLayoutDocument.MaximumEdges)
            throw new ArgumentException("The Space graph exceeds the supported node or connection bounds.", nameof(layout));
        SpaceBuilderState next;
        lock (_gate)
        {
            if (!_state.CanEdit) return false;
            var canonicalLayout = layout with
            {
                GraphId = layout.GraphId == Guid.Empty ? _state.Draft.LayoutDocument?.GraphId ?? Guid.NewGuid() : layout.GraphId,
                Revision = _state.Draft.LayoutDocument?.Revision ?? layout.Revision,
                State = SpaceLayoutRevisionState.Draft
            };
            var draft = _state.Draft with { LayoutDocument = canonicalLayout };
            if (draft == _state.Draft) return true;
            _editVersion++;
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
                StatusMessage = "Expert graph changes are in the Space draft. Save to persist." };
        }
        StateChanged?.Invoke(next);
        return true;
    }

    public SpaceBuilderPreview? PreviewDraft()
    {
        SpaceBuilderPreview? preview = null;
        Change(state =>
        {
            if (!state.IsLoaded || state.DraftRevision is not { } revision) return state with { StatusMessage = "Load the Space before previewing its draft." };
            preview = new(state.SpaceId, revision, state.Draft.Name, state.Draft.Description,
                state.Draft.Instructions, state.Draft.CapabilityBindings.Count,
                state.Draft.LayoutDocument?.Nodes.Count ?? 0, state.Draft.LayoutDocument?.Edges.Count ?? 0);
            return state with { Preview = preview, StatusMessage = preview.Notice };
        });
        return preview;
    }

    /// <summary>Validates the actual saved Space and current draft without publishing any change.
    /// It cannot validate provider/resource availability owned by other services.</summary>
    public async Task<IReadOnlyList<string>> ValidateDraftAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpaceBuilderState snapshot;
        long editVersion;
        lock (_gate) { snapshot = _state; editVersion = _editVersion; }
        List<string> issues = [];
        if (!snapshot.IsLoaded || snapshot.DraftRevision is not { } expectedRevision)
            issues.Add("Load the existing Space before validating its draft.");
        else
        {
            var spaces = await _registry.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var current = spaces.SingleOrDefault(space => space.Id == snapshot.SpaceId);
            if (current is null || current.IsArchived) issues.Add("The Space is unavailable or archived.");
            else
            {
                if (current.Revision != snapshot.SpaceRevision || current.DraftConfigurationRevision != expectedRevision ||
                    current.ActiveConfigurationRevision != snapshot.ActiveRevision ||
                    current.DraftBaseActiveRevision != snapshot.DraftBaseActiveRevision)
                    issues.Add($"The saved Space changed to revision {current.Revision}; reload before validating or saving.");
                var name = snapshot.Draft.Name.Trim();
                if (name.Length is < 1 or > 80) issues.Add("Space name must contain 1 to 80 characters.");
                if (spaces.Any(space => space.Id != snapshot.SpaceId && StringComparer.OrdinalIgnoreCase.Equals(space.Name.Trim(), name)))
                    issues.Add($"A Space named '{name}' already exists.");
            }
        }
        if (!Enum.IsDefined(snapshot.Draft.ContextPolicy.WebAccess) || snapshot.Draft.ContextPolicy.MaximumRetrievedItems is < 0 or > 1000)
            issues.Add("The source context policy contains an unsupported value.");
        if (snapshot.Draft.CapabilityBindings.GroupBy(binding => (binding.Kind, binding.CapabilityId)).Any(group => group.Count() > 1) ||
            snapshot.Draft.CapabilityBindings.Any(binding => string.IsNullOrWhiteSpace(binding.CapabilityId) ||
                !Enum.IsDefined(binding.Kind) || !Enum.IsDefined(binding.Override)))
            issues.Add("Canonical capability bindings must be valid and unique.");
        if (snapshot.Draft.LayoutDocument is { } layout)
        {
            try { SpaceRegistry.ValidateLayoutDocument(layout); }
            catch (ArgumentException error) { issues.Add("Graph validation failed: " + error.Message); }
            if (layout.Nodes.Any(node => string.IsNullOrWhiteSpace(node.TypeId)))
                issues.Add("The graph contains structural-only nodes without a registered TypeId; they cannot be activated.");
        }
        if (issues.Count == 0)
            issues.Add(snapshot.HasUnverifiedDependencies
                ? "Draft structure is valid. Registered node schemas, resource availability, permissions and model capabilities still require validation by their owning services before activation."
                : "Draft structure is valid and ready for activation.");
        SpaceBuilderState next;
        lock (_gate)
        {
            if (_editVersion != editVersion || _state.Draft != snapshot.Draft)
                issues = ["The Space draft changed while validation was running; validate the current draft again."];
            var result = Array.AsReadOnly(issues.ToArray());
            var status = result.Count == 1 && result[0].StartsWith("Draft structure is valid", StringComparison.Ordinal)
                ? result[0] : $"Draft validation found {result.Count} blocking issue(s).";
            next = _state = _state with { ValidationIssues = result, StatusMessage = status };
        }
        StateChanged?.Invoke(next);
        var result = next.ValidationIssues;
        return result;
    }

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
                Draft = draft, Preview = null, ValidationIssues = [],
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
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
                return state with { DraftRevision = space.DraftConfigurationRevision,
                    ActiveRevision = space.ActiveConfigurationRevision, SpaceRevision = space.Revision,
                    DraftBaseActiveRevision = space.DraftBaseActiveRevision,
                    HasPersistedDraft = space.DraftConfiguration is not null,
                    Draft = _baseline, IsLoaded = true, IsReadOnly = space.IsBuiltIn,
                    HasChanges = false, HasConflict = false,
                    Preview = null, ValidationIssues = [],
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
            if (current.Revision != saving.SpaceRevision)
                throw new SpaceRevisionConflictException(current.Id, saving.SpaceRevision!.Value, current.Revision);
            var savedLayout = saving.Draft.LayoutDocument;
            if (savedLayout is not null &&
                (!Equals(savedLayout, _baseline.LayoutDocument) || savedLayout.State != SpaceLayoutRevisionState.Draft))
                savedLayout = savedLayout with
                {
                    GraphId = savedLayout.GraphId == Guid.Empty ? _baseline.LayoutDocument?.GraphId ?? Guid.NewGuid() : savedLayout.GraphId,
                    Revision = checked((_baseline.LayoutDocument?.Revision ?? savedLayout.Revision) + 1),
                    State = SpaceLayoutRevisionState.Draft
                };
            // Patch only this editor projection's fields over the saved draft (or active
            // configuration when no draft exists). Other authored fields remain intact.
            var configuration = (current.DraftConfiguration ?? SpaceConfigurationDocument.From(current)) with
            {
                Name = saving.Draft.Name, Description = saving.Draft.Description,
                IconKey = saving.Draft.IconKey, Instructions = saving.Draft.Instructions,
                CapabilityCategoryPolicy = saving.Draft.CategoryPolicy,
                CapabilityBindings = saving.Draft.CapabilityBindings,
                ContextPolicy = saving.Draft.ContextPolicy,
                Inheritance = saving.Draft.Inheritance,
                ContextReferences = saving.Draft.ContextReferences.ToArray(),
                Files = saving.Draft.LegacyFiles.ToArray(),
                LayoutDocument = savedLayout,
                ModelPolicy = saving.Draft.ModelPolicy,
                MemoryPolicy = saving.Draft.MemoryPolicy,
                ExamplePairs = saving.Draft.ExamplePairs.ToArray(),
                Shares = saving.Draft.Shares.ToArray(),
                GeneratedSurface = saving.Draft.GeneratedSurface
            };
            var saved = await _registry.SaveDraftAsync(current.Id, configuration, saving.SpaceRevision!.Value,
                saving.DraftRevision!.Value, saving.ActiveRevision!.Value, cancellationToken).ConfigureAwait(false);
            Change(state =>
            {
                _baseline = SpaceBuilderDraft.From(saved);
                var draft = _editVersion == editVersion ? _baseline : state.Draft;
                return state with { DraftRevision = saved.DraftConfigurationRevision,
                    ActiveRevision = saved.ActiveConfigurationRevision, SpaceRevision = saved.Revision,
                    DraftBaseActiveRevision = saved.DraftBaseActiveRevision,
                    HasPersistedDraft = true,
                    Draft = draft, HasChanges = draft != _baseline, HasConflict = false,
                    Preview = null, ValidationIssues = [],
                    StatusMessage = draft == _baseline ? "Space Builder draft saved. Activate to publish this validated configuration." : "Earlier changes saved. Your latest edits are still unsaved." };
            });
            return true;
        }
        catch (SpaceRevisionConflictException)
        {
            Change(state => state with { HasConflict = true,
                StatusMessage = "This Space changed elsewhere. Your draft is unchanged. Discard changes and reload to use the saved version." });
            return false;
        }
        catch (SpaceDraftRevisionConflictException)
        {
            Change(state => state with { HasConflict = true,
                StatusMessage = "This Space draft changed elsewhere. Your draft is unchanged. Discard changes and reload to use the saved version." });
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

    /// <summary>Publishes the already-saved Builder draft through SpaceRegistry's atomic
    /// active/draft CAS. The registry revalidates the configuration and rejects dependencies
    /// whose owning services are not available to this publication path.</summary>
    public async Task<bool> ActivateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SpaceBuilderState activating;
        lock (_gate)
        {
            if (!_state.CanActivate) return false;
            activating = _state = _state with { IsSaving = true, StatusMessage = "Publishing the saved Space configuration…" };
        }
        try
        {
            StateChanged?.Invoke(activating);
            var activated = await _registry.ActivateDraftAsync(activating.SpaceId, activating.SpaceRevision!.Value,
                activating.DraftRevision!.Value, activating.ActiveRevision!.Value, cancellationToken).ConfigureAwait(false);
            Change(state =>
            {
                _baseline = SpaceBuilderDraft.From(activated);
                _editVersion++;
                return state with
                {
                    SpaceRevision = activated.Revision,
                    DraftRevision = activated.DraftConfigurationRevision,
                    ActiveRevision = activated.ActiveConfigurationRevision,
                    DraftBaseActiveRevision = activated.DraftBaseActiveRevision,
                    HasPersistedDraft = false,
                    Draft = _baseline,
                    HasChanges = false,
                    HasConflict = false,
                    Preview = null,
                    ValidationIssues = [],
                    StatusMessage = $"Space configuration activated at revision {activated.ActiveConfigurationRevision}."
                };
            });
            return true;
        }
        catch (SpaceRevisionConflictException)
        {
            Change(state => state with { HasConflict = true,
                StatusMessage = "The active Space changed elsewhere. Your draft is unchanged; reload and reconcile before activation." });
            return false;
        }
        catch (SpaceDraftRevisionConflictException)
        {
            Change(state => state with { HasConflict = true,
                StatusMessage = "The saved Space draft changed elsewhere. Your draft is unchanged; reload before activation." });
            return false;
        }
        catch (InvalidOperationException error)
        {
            Change(state => state with { StatusMessage = "Space configuration was not activated: " + error.Message });
            return false;
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
                next = _state = _state with { Draft = updatedDraft, Preview = null, ValidationIssues = [], HasChanges = updatedDraft != _baseline,
                    StatusMessage = "Capability updated. Save to persist." };
                StateChanged?.Invoke(next);
                return true;
            }
            var bindings = _state.Draft.CapabilityBindings.Append(binding).ToArray();
            var draft = _state.Draft with { CapabilityBindings = bindings };
            _editVersion++;
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
            if (!_state.CanEdit || _state.SpaceRevision != expectedRevision) return false;
            var existing = _state.Draft.ContextReferences.SingleOrDefault(value => value.Kind == reference.Kind &&
                value.OwnerAppId == reference.OwnerAppId && value.CanonicalEntityId == reference.CanonicalEntityId);
            if (existing is not null) reference = reference with { ContextId = existing.ContextId };
            var rows = _state.Draft.ContextReferences.Where(value => value.ContextId != reference.ContextId).Append(reference).ToArray();
            var draft = _state.Draft with { ContextReferences = Array.AsReadOnly(rows) };
            _editVersion++;
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
            next = _state = _state with { Draft = draft, Preview = null, ValidationIssues = [], HasChanges = draft != _baseline,
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
