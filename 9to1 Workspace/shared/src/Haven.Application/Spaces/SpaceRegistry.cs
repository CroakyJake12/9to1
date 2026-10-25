using System.Text.Json;

namespace Haven.Application;

public sealed partial class SpaceRegistry
{
    private const string SettingsKey = "spaces.registry";
    private const string CurrentSpaceSettingsKey = "spaces.current";
    private const int CurrentVersion = 4;
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<CancellationToken, ValueTask<ISettingsCommitAdmission>>? _captureAdmission;

    /// <summary>Original object association only; this does not grant store access or ownership.</summary>
    public bool HasOriginalSettingsStore(IVersionedSettingsStore actualStore) => ReferenceEquals(_settings, actualStore);

    public static Guid ChatSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000005");
    public static Guid StudySpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    public static Guid ShoppingSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    public static Guid ResearchSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000003");
    public static Guid AgentSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000004");
    public static Guid TasksSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000006");
    public static Guid TranslateSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000007");
    public static Guid ExperiencesSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000008");

    public SpaceRegistry(IVersionedSettingsStore settings) : this(settings, () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Trusted owner composition; capture occurs before the settings lease. The returned admission
    /// must not read this settings store or its evidence while its lease is held.</summary>
    public SpaceRegistry(IVersionedSettingsStore settings,
        Func<CancellationToken, ValueTask<ISettingsCommitAdmission>> captureAdmission) : this(settings)
    {
        if (settings is not IVersionedSettingsGuardedCompareExchange)
            throw new ArgumentException("Guarded Space writes require durable guarded compare-exchange.", nameof(settings));
        _captureAdmission = captureAdmission ?? throw new ArgumentNullException(nameof(captureAdmission));
    }

    internal SpaceRegistry(IVersionedSettingsStore settings, Func<DateTimeOffset> clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<SpaceDefinition>> GetAllAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
            return state.Spaces
                .Where(space => includeArchived || !space.IsArchived)
                .OrderByDescending(space => space.IsBuiltIn)
                .ThenBy(space => space.ParentSpaceId.HasValue)
                .ThenBy(space => space.ParentSpaceId)
                .ThenBy(space => space.Position)
                .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SpaceDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var spaces = await GetAllAsync(includeArchived: true, cancellationToken).ConfigureAwait(false);
        return spaces.FirstOrDefault(space => space.Id == id);
    }

    /// <summary>Reads only an existing stored Space; never seeds, migrates or writes settings.
    /// This snapshot grants no access: callers retain their actual owner/resource authority checks.</summary>
    public async Task<SpaceDefinition?> ReadExistingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (state is null) return null;
        if (state.Version is < 2 or > CurrentVersion) throw new InvalidDataException("A stored Space snapshot requires a supported schema.");
        ValidateDeletionState(state);
        var spaces = state.Spaces.Select(NormalizeStoredSpace).ToArray();
        ValidateRegistry(spaces);
        var matches = spaces.Where(space => space.Id == id).ToArray();
        return matches.Length == 1 ? CloneSpace(matches[0]) : null;
    }

    /// <summary>Reads a bounded census of active existing stored Spaces only. Never seeds,
    /// reconciles, migrates, changes current selection or grants access. Over-bound inventories
    /// require an explicitly narrower owning selection; this method never silently truncates.</summary>
    public async Task<IReadOnlyList<SpaceDefinition>> ReadExistingPageAsync(int maximumSpaces = 64,
        CancellationToken cancellationToken = default)
    {
        if (maximumSpaces is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumSpaces));
        Task<SpaceRegistryState?> actual;
        try { actual = _settings.GetAsync<SpaceRegistryState>(SettingsKey, cancellationToken); }
        catch (OperationCanceledException fault) { throw new AggregateException("The existing Space census source faulted synchronously.", fault); }
        SpaceRegistryState? state;
        try { state = await actual.ConfigureAwait(false); }
        catch { if (actual.IsFaulted) throw actual.Exception!; throw; } // Whole raw fault; actual cancellation remains canceled.
        cancellationToken.ThrowIfCancellationRequested();
        if (state is null) return [];
        if (state.Version is < 2 or > CurrentVersion) throw new InvalidDataException("A stored Space census requires a supported schema.");
        ValidateDeletionState(state);
        var spaces = state.Spaces.Select(NormalizeStoredSpace).ToArray();
        ValidateRegistry(spaces);
        var active = spaces.Where(space => !space.IsArchived).ToArray();
        if (active.Length > maximumSpaces)
            throw new InvalidOperationException("The existing Space census exceeds its explicit bound; select fewer Spaces through the owning service.");
        return active.Select(CloneSpace).ToArray();
    }

    public async Task<SpaceDefinition> CreateAsync(string name, string? description = null, CancellationToken cancellationToken = default)
        => await CreateAsync(name, description, null, cancellationToken).ConfigureAwait(false);

    public async Task<SpaceDefinition> CreateAsync(
        string name,
        string? description,
        Guid? parentSpaceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        return await MutateAsync(state =>
        {
            EnsureUniqueName(state.Spaces, normalizedName);
            ValidateParent(state.Spaces, Guid.Empty, parentSpaceId);
            var now = _clock();
            var position = state.Spaces
                .Where(space => space.ParentSpaceId == parentSpaceId && !space.IsBuiltIn && !space.IsArchived)
                .Select(space => space.Position)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            var created = new SpaceDefinition(
                Guid.NewGuid(), normalizedName, description?.Trim() ?? string.Empty, "sparkles", SpaceKind.General,
                false, false, null, string.Empty, SpaceThinkingMode.Default, [], [], null, now, now,
                Revision: 1,
                ModelPolicy: new SpaceModelPolicy(null),
                ContextPolicy: new SpaceContextPolicy(),
                MemoryPolicy: DefaultMemoryPolicy,
                CapabilityBindings: [],
                Inheritance: DefaultInheritance,
                ContextReferences: [],
                Shares: [],
                ParentSpaceId: parentSpaceId,
                Position: position);
            created = InitializeConfigurationHistory(created);
            ValidateDefinition(created, state.Spaces);
            return (state with { Spaces = [.. state.Spaces, created] }, created);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<SpaceDefinition> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        return MutateSpaceAsync(id, (space, spaces) =>
        {
            if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Space names are protected.");
            EnsureUniqueName(spaces, normalizedName, id);
            return space with { Name = normalizedName, UpdatedAt = _clock() };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        MutateSpaceAsync(id, (space, spaces) =>
        {
            if (space.IsBuiltIn && archived) throw new InvalidOperationException("Built-in Spaces cannot be archived.");
            if (archived && spaces.Any(candidate => candidate.ParentSpaceId == id && !candidate.IsArchived))
                throw new InvalidOperationException("Move or archive child Spaces before archiving their parent.");
            if (!archived && space.ParentSpaceId is { } parentId && FindRequired(spaces, parentId).IsArchived)
                throw new InvalidOperationException("Restore the parent Space before restoring this child.");
            return space with { IsArchived = archived, UpdatedAt = _clock() };
        }, cancellationToken);

    public Task<SpaceDefinition> RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        SetArchivedAsync(id, false, cancellationToken);

    public Task<SpaceDefinition> UpdateAsync(
        SpaceDefinition updated,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        return MutateSpaceAsync(updated.Id, (existing, spaces) =>
        {
            if (expectedRevision is { } revision && existing.Revision != revision)
                throw new SpaceRevisionConflictException(existing.Id, revision, existing.Revision);
            if (existing.IsBuiltIn && updated.ParentSpaceId != existing.ParentSpaceId)
                throw new InvalidOperationException("Built-in Space hierarchy is protected.");
            var name = existing.IsBuiltIn ? existing.Name : NormalizeName(updated.Name);
            if (!existing.IsBuiltIn) EnsureUniqueName(spaces, name, updated.Id);
            return updated with
            {
                Name = name,
                Description = existing.IsBuiltIn ? existing.Description : updated.Description,
                IconKey = existing.IsBuiltIn ? existing.IconKey : updated.IconKey,
                IsBuiltIn = existing.IsBuiltIn,
                Kind = existing.IsBuiltIn ? existing.Kind : updated.Kind,
                Instructions = existing.IsBuiltIn ? existing.Instructions : updated.Instructions,
                ThinkingMode = existing.IsBuiltIn ? existing.ThinkingMode : updated.ThinkingMode,
                LayoutDocument = existing.IsBuiltIn ? CloneLayout(existing.LayoutDocument) : CloneLayout(updated.LayoutDocument),
                CreatedAt = existing.CreatedAt,
                IsArchived = existing.IsBuiltIn ? existing.IsArchived : updated.IsArchived,
                ParentSpaceId = existing.ParentSpaceId,
                Position = existing.Position,
                UpdatedAt = _clock(),
                ActiveConfigurationRevision = existing.ActiveConfigurationRevision,
                DraftConfigurationRevision = existing.DraftConfigurationRevision,
                DraftBaseActiveRevision = existing.DraftBaseActiveRevision,
                DraftConfiguration = existing.DraftConfiguration,
                ConfigurationHistory = existing.ConfigurationHistory,
                ExamplePairs = updated.ExamplePairs?.ToArray() ?? [],
                Files = updated.Files?.ToArray() ?? [],
                CapabilityBindings = updated.CapabilityBindings?.ToArray() ?? [],
                ContextReferences = updated.ContextReferences?.ToArray() ?? [],
                Shares = updated.Shares?.ToArray() ?? []
            };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> UpdateAsync(SpaceDefinition updated, CancellationToken cancellationToken) =>
        UpdateAsync(updated, expectedRevision: null, cancellationToken);

    /// <summary>Persists the Builder's one canonical draft without changing active fields or
    /// SpaceDefinition.Revision, so live Tasks/Projects keep their active revision.</summary>
    public Task<SpaceDefinition> SaveDraftAsync(Guid id, SpaceConfigurationDocument draft,
        long expectedSpaceRevision, long expectedDraftRevision, long expectedActiveConfigurationRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return MutateAsync(state =>
        {
            var current = FindRequired(state.Spaces, id);
            if (current.IsBuiltIn || current.IsArchived)
                throw new InvalidOperationException("This Space cannot accept a Builder draft.");
            if (current.Revision != expectedSpaceRevision)
                throw new SpaceRevisionConflictException(id, expectedSpaceRevision, current.Revision);
            if (current.DraftConfigurationRevision != expectedDraftRevision)
                throw new SpaceDraftRevisionConflictException(id, expectedDraftRevision, current.DraftConfigurationRevision);
            if (current.ActiveConfigurationRevision != expectedActiveConfigurationRevision ||
                current.DraftBaseActiveRevision != current.ActiveConfigurationRevision)
                throw new InvalidOperationException("The active Space configuration changed after this draft began; reload and reconcile before saving.");

            var peers = state.Spaces.Where(space => space.Id != id).ToArray();
            var normalized = CloneConfiguration(draft);
            ValidateConfiguration(current, normalized, peers);
            var name = normalized.Name.Trim();
            if (peers.Any(space => StringComparer.OrdinalIgnoreCase.Equals(space.Name, name) ||
                space.DraftConfiguration is { } peerDraft && StringComparer.OrdinalIgnoreCase.Equals(peerDraft.Name.Trim(), name)))
                throw new InvalidOperationException($"A Space or saved draft named '{name}' already exists.");
            var saved = current with
            {
                DraftConfiguration = normalized,
                DraftConfigurationRevision = checked(current.DraftConfigurationRevision + 1),
                DraftBaseActiveRevision = current.ActiveConfigurationRevision,
                UpdatedAt = _clock()
            };
            ValidateDefinition(saved, peers);
            return (state with { Spaces = state.Spaces.Select(space => space.Id == id ? saved : space).ToArray() }, saved);
        }, cancellationToken);
    }

    /// <summary>Publishes a saved Builder draft as one new active configuration revision.
    /// The supplied registry and draft revisions are checked in the same global-store mutation.</summary>
    public Task<SpaceDefinition> ActivateDraftAsync(Guid id, long expectedSpaceRevision,
        long expectedDraftRevision, long expectedActiveConfigurationRevision,
        CancellationToken cancellationToken = default) => ActivateDraftCoreAsync(id, expectedSpaceRevision,
            expectedDraftRevision, expectedActiveConfigurationRevision, null, cancellationToken);

    /// <summary>Validates current external dependencies through their owning services, then
    /// publishes only if the same Space, draft, active configuration and policy ancestry remain
    /// current in the registry transaction.</summary>
    public async Task<SpaceDefinition> ActivateDraftAsync(Guid id, long expectedSpaceRevision,
        long expectedDraftRevision, long expectedActiveConfigurationRevision,
        ISpaceDraftActivationValidator validator, Action<Action> acquireOriginal,
        Action<Task> retainOriginal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(acquireOriginal);
        ArgumentNullException.ThrowIfNull(retainOriginal);
        var current = await ReadExistingAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Space '{id}' was not found.");
        if (current.Revision != expectedSpaceRevision)
            throw new SpaceRevisionConflictException(id, expectedSpaceRevision, current.Revision);
        if (current.DraftConfigurationRevision != expectedDraftRevision)
            throw new SpaceDraftRevisionConflictException(id, expectedDraftRevision, current.DraftConfigurationRevision);
        if (current.ActiveConfigurationRevision != expectedActiveConfigurationRevision ||
            current.DraftBaseActiveRevision != current.ActiveConfigurationRevision)
            throw new InvalidOperationException("The active Space configuration changed after this draft began; reload and reconcile before activation.");
        var draft = current.DraftConfiguration ?? throw new InvalidOperationException("Save the Builder draft before activating it.");
        Task<SpaceDraftActivationValidation>? actual = null;
        acquireOriginal(() =>
        {
            actual = validator.ValidateAsync(current, draft, acquireOriginal, retainOriginal, cancellationToken);
            retainOriginal(actual);
        });
        var validation = await (actual ?? throw new InvalidOperationException("The activation validator did not return its original task."))
            .ConfigureAwait(false);
        return await ActivateDraftCoreAsync(id, expectedSpaceRevision, expectedDraftRevision,
            expectedActiveConfigurationRevision, validation, cancellationToken).ConfigureAwait(false);
    }

    private Task<SpaceDefinition> ActivateDraftCoreAsync(Guid id, long expectedSpaceRevision,
        long expectedDraftRevision, long expectedActiveConfigurationRevision,
        SpaceDraftActivationValidation? validation, CancellationToken cancellationToken) => MutateAsync(state =>
    {
        var current = FindRequired(state.Spaces, id);
        if (current.IsBuiltIn || current.IsArchived)
            throw new InvalidOperationException("This Space cannot be activated.");
        if (current.Revision != expectedSpaceRevision)
            throw new SpaceRevisionConflictException(id, expectedSpaceRevision, current.Revision);
        if (current.DraftConfigurationRevision != expectedDraftRevision)
            throw new SpaceDraftRevisionConflictException(id, expectedDraftRevision, current.DraftConfigurationRevision);
        if (current.ActiveConfigurationRevision != expectedActiveConfigurationRevision ||
            current.DraftBaseActiveRevision != current.ActiveConfigurationRevision)
            throw new InvalidOperationException("The active Space configuration changed after this draft began; reload and reconcile before activation.");
        var draft = current.DraftConfiguration ?? throw new InvalidOperationException("Save the Builder draft before activating it.");
        ValidateConfiguration(current, draft, state.Spaces.Where(space => space.Id != id).ToArray());
        if (validation is not null)
        {
            if (validation.SpaceId != id || validation.SpaceRevision != expectedSpaceRevision ||
                validation.DraftConfigurationRevision != expectedDraftRevision ||
                validation.ActiveConfigurationRevision != expectedActiveConfigurationRevision ||
                !AncestryMatches(state.Spaces, id, validation.Ancestry))
                throw new InvalidOperationException("A Space or ancestor changed after dependency validation; no active configuration was published.");
        }
        // Pure candidate validation uses the same registered executor catalogue as request
        // derivation. Recheck inside the atomic publication transaction against the exact
        // Space revision that this activation will create.
        if (draft.LayoutDocument is { Nodes.Count: > 0 } layout)
        {
            var graph = SpaceUserMessageGraph.Compile(id, checked(current.Revision + 1), layout);
            var effectiveCandidate = ResolveGraphCandidatePolicy(state.Spaces, current, draft);
            graph.ValidateContextDependencies(effectiveCandidate);
        }
        if (draft.Shares.Count != 0 || draft.GeneratedSurface is not null)
            throw new InvalidOperationException("Activation is unavailable until the owning share or generated-surface service validates this draft.");
        if (validation is null && (draft.CapabilityBindings.Count != 0 ||
            !string.IsNullOrWhiteSpace(draft.ModelPolicy?.DefaultModelId) ||
            !string.IsNullOrWhiteSpace(draft.ModelPolicy?.ProviderConstraint) ||
            draft.ModelPolicy?.RequiredCapabilities?.Count > 0 || current.ParentSpaceId is not null))
            throw new InvalidOperationException("Activation requires the current model and installed capability owners to validate this draft.");

        var nextActiveRevision = checked(current.ActiveConfigurationRevision + 1);
        var activated = draft.ApplyTo(current, activateLayout: true) with
        {
            Revision = checked(current.Revision + 1),
            ActiveConfigurationRevision = nextActiveRevision,
            DraftConfigurationRevision = checked(current.DraftConfigurationRevision + 1),
            DraftBaseActiveRevision = nextActiveRevision,
            DraftConfiguration = null,
            UpdatedAt = _clock()
        };
        activated = AppendConfigurationRevision(activated, SpaceConfigurationDocument.From(activated), nextActiveRevision, activated.UpdatedAt);
        ValidateDefinition(activated, state.Spaces.Where(space => space.Id != id).ToArray());
        return (state with { Spaces = state.Spaces.Select(space => space.Id == id ? activated : space).ToArray() }, activated);
    }, cancellationToken);

    public async Task<SpaceDefinition> MoveAsync(
        Guid id,
        Guid? parentSpaceId,
        int? position = null,
        CancellationToken cancellationToken = default)
    {
        return await MutateAsync(state =>
        {
            var moving = FindRequired(state.Spaces, id);
            if (moving.IsBuiltIn)
                throw new InvalidOperationException("Built-in Spaces cannot be reordered or nested.");
            if (moving.IsArchived)
                throw new InvalidOperationException("Restore an archived Space before moving it.");
            ValidateParent(state.Spaces, id, parentSpaceId);
            if (position is < 0) throw new ArgumentOutOfRangeException(nameof(position));

            var now = _clock();
            var sourceParentId = moving.ParentSpaceId;
            var sourceSiblings = state.Spaces
                .Where(space => !space.IsBuiltIn && space.ParentSpaceId == sourceParentId && !space.IsArchived && space.Id != id)
                .OrderBy(space => space.Position)
                .ThenBy(space => space.CreatedAt)
                .ToList();
            var siblings = state.Spaces
                .Where(space => !space.IsBuiltIn && space.ParentSpaceId == parentSpaceId && !space.IsArchived && space.Id != id)
                .OrderBy(space => space.Position)
                .ThenBy(space => space.CreatedAt)
                .ToList();
            // A move without an explicit position preserves the Space's own slot; an explicit
            // position claims that slot and the remaining siblings fill the free slots in order.
            var targetPosition = position ?? moving.Position;
            var moved = moving with { ParentSpaceId = parentSpaceId, Position = targetPosition };
            siblings.Add(moved);
            var orderedSiblings = siblings.OrderBy(space => space.Position).ThenBy(space => space.CreatedAt).ToList();
            var freeSlots = Enumerable.Range(0, orderedSiblings.Count)
                .Where(slot => slot != targetPosition)
                .ToList();
            var updatedById = new Dictionary<Guid, SpaceDefinition>();
            var freeSlotIndex = 0;
            foreach (var space in orderedSiblings)
            {
                var positionValue = ReferenceEquals(space, moved) ? targetPosition : freeSlots[freeSlotIndex++];
                if (space.Position == positionValue && space.ParentSpaceId == parentSpaceId && !ReferenceEquals(space, moved))
                    continue;
                updatedById[space.Id] = space with
                {
                    ParentSpaceId = parentSpaceId,
                    Position = positionValue,
                    Revision = checked(space.Revision + 1),
                    UpdatedAt = now
                };
            }
            foreach (var (space, index) in sourceSiblings.Select((space, index) => (space, index)))
            {
                if (!updatedById.ContainsKey(space.Id))
                    updatedById[space.Id] = space with { Position = index, Revision = checked(space.Revision + 1), UpdatedAt = now };
            }
            var all = state.Spaces.Select(space => updatedById.TryGetValue(space.Id, out var updated) ? updated : space).ToArray();
            return (state with { Spaces = all }, updatedById[id]);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the Space the shell is currently scoped to, or null for unscoped Chat.</summary>
    public async Task<Guid?> GetCurrentSpaceIdAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = await _settings.GetAsync<string>(CurrentSpaceSettingsKey, cancellationToken).ConfigureAwait(false);
            return Guid.TryParse(value, out var id) ? id : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Persists the Space the shell is scoped to; null returns to unscoped Chat.</summary>
    public async Task SetCurrentSpaceIdAsync(Guid? spaceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_settings is IVersionedSettingsCompareExchange)
            {
                await SetCurrentComparedAsync(spaceId, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (spaceId is { } id)
            {
                var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
                var selected = state.Spaces.SingleOrDefault(space => space.Id == id);
                if (selected is null || selected.IsArchived)
                    throw new KeyNotFoundException($"Active Space '{id}' was not found.");
                await _settings.SetAsync(CurrentSpaceSettingsKey, id.ToString(), cancellationToken).ConfigureAwait(false);
            }
            else await _settings.RemoveAsync(CurrentSpaceSettingsKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SpaceDefinition> ForkAsync(Guid id, string? name = null, CancellationToken cancellationToken = default)
    {
        return await MutateAsync(state =>
        {
            var source = FindRequired(state.Spaces, id);
            var requested = string.IsNullOrWhiteSpace(name) ? $"{source.Name} copy" : name!;
            var forkName = MakeUniqueName(state.Spaces, NormalizeName(requested));
            var now = _clock();
            var fork = source with
            {
                Id = Guid.NewGuid(),
                Name = forkName,
                IsBuiltIn = false,
                IsArchived = false,
                ForkedFromSpaceId = source.Id,
                Files = source.Files.ToArray(),
                ExamplePairs = source.ExamplePairs.ToArray(),
                LayoutDocument = CloneLayout(source.LayoutDocument),
                Shares = [],
                Revision = 1,
                ActiveConfigurationRevision = 1,
                DraftConfigurationRevision = 1,
                DraftBaseActiveRevision = 1,
                DraftConfiguration = null,
                ConfigurationHistory = [],
                CreatedAt = now,
                UpdatedAt = now
            };
            var initializedFork = InitializeConfigurationHistory(fork);
            return (state with { Spaces = [.. state.Spaces, initializedFork] }, initializedFork);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var wasCurrent = await GetCurrentSpaceIdAsync(cancellationToken).ConfigureAwait(false) == id;
        await MutateAsync(state =>
        {
            var existing = FindRequired(state.Spaces, id);
            if (existing.IsBuiltIn)
                throw new InvalidOperationException("Built-in Spaces cannot be deleted. Fork one if you need an independent version.");
            var archived = existing with { IsArchived = true, Revision = checked(existing.Revision + 1), UpdatedAt = _clock() };
            return (state with { Spaces = state.Spaces.Select(space => space.Id == id ? archived : space).ToArray() }, true);
        }, cancellationToken).ConfigureAwait(false);
        if (wasCurrent)
            await SetCurrentSpaceIdAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public Task<SpaceDefinition> SetLayoutAsync(Guid id, SpaceLayoutDocument? layout, CancellationToken cancellationToken = default)
    {
        if (layout is not null) ValidateLayout(layout);
        return MutateSpaceAsync(id, (space, _) =>
        {
            if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Space graphs are protected.");
            return space with { LayoutDocument = CloneLayout(layout), UpdatedAt = _clock() };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> AddFileAsync(
        Guid id,
        string path,
        SpaceFilePermission permission = SpaceFilePermission.ReadOnly,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        return MutateSpaceAsync(id, (space, _) =>
        {
            var files = space.Files
                .Where(file => !file.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                .Append(new SpaceFileReference(fullPath, Path.GetFileName(fullPath), permission, _clock()))
                .ToArray();
            return space with { Files = files, UpdatedAt = _clock() };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> RemoveFileAsync(Guid id, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        return MutateSpaceAsync(id, (space, _) => space with
        {
            Files = space.Files.Where(file => !file.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)).ToArray(),
            UpdatedAt = _clock()
        }, cancellationToken);
    }

    private async Task<SpaceDefinition> MutateSpaceAsync(
        Guid id,
        Func<SpaceDefinition, IReadOnlyList<SpaceDefinition>, SpaceDefinition> update,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(state =>
        {
            var existing = FindRequired(state.Spaces, id);
            var changed = update(existing, state.Spaces);
            if (changed != existing)
            {
                changed = changed with { Revision = checked(existing.Revision + 1) };
                if (!ConfigurationEquals(SpaceConfigurationDocument.From(existing), SpaceConfigurationDocument.From(changed)))
                {
                    var activeRevision = checked(existing.ActiveConfigurationRevision + 1);
                    changed = changed with
                    {
                        ActiveConfigurationRevision = activeRevision,
                        DraftBaseActiveRevision = existing.DraftConfiguration is null
                            ? activeRevision : changed.DraftBaseActiveRevision
                    };
                    changed = AppendConfigurationRevision(changed, SpaceConfigurationDocument.From(changed), activeRevision, changed.UpdatedAt);
                }
            }
            ValidateDefinition(changed, state.Spaces.Where(space => space.Id != id).ToArray());
            var spaces = state.Spaces.Select(space => space.Id == id ? changed : space).ToArray();
            return (state with { Spaces = spaces }, changed);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<SpaceRegistryState, (SpaceRegistryState State, TResult Result)> mutation,
        CancellationToken cancellationToken, bool allowPendingDeletion = false)
    {
        var requestedMutation = mutation;
        mutation = state =>
        {
            var result = requestedMutation(state);
            if (!allowPendingDeletion) EnsurePendingDeletionsUnchanged(state, result.State);
            ValidateDeletionState(result.State);
            return result;
        };
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_settings is IVersionedSettingsCompareExchange)
                return await MutateComparedAsync(mutation, cancellationToken).ConfigureAwait(false);
            var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = mutation(state);
            await _settings.SetAsync(SettingsKey,
                next with { Spaces = next.Spaces.Select(CloneSpace).ToArray(), Deletions = next.Deletions?.ToArray() ?? [] }, cancellationToken).ConfigureAwait(false);
            return result is SpaceDefinition space ? (TResult)(object)CloneSpace(space) : result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SpaceRegistryState> LoadAndReconcileAsync(CancellationToken cancellationToken)
    {
        if (_settings is IVersionedSettingsCompareExchange)
        {
            ISettingsCommitAdmission? admission = null;
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var exported = await _settings.ExportAsync(cancellationToken).ConfigureAwait(false);
                exported.Settings.TryGetValue(SettingsKey, out var raw);
                var (snapshotState, reconcileNeeded) = ReadComparedState(raw);
                if (!reconcileNeeded) return snapshotState;
                admission ??= await CaptureAdmissionAsync(cancellationToken).ConfigureAwait(false);
                if (await TryCommitAsync(SettingsKey, raw, JsonSerializer.Serialize(snapshotState),
                    new Dictionary<string, string?>(), admission, cancellationToken).ConfigureAwait(false)) return snapshotState;
            }
            throw new InvalidOperationException("The Space registry kept changing during reconciliation; reload and retry.");
        }
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, cancellationToken).ConfigureAwait(false)
            ?? new SpaceRegistryState(CurrentVersion, []);
        if (state.Version > CurrentVersion)
            throw new InvalidDataException($"Space registry version {state.Version} is newer than supported version {CurrentVersion}.");
        ValidateDeletionState(state);
        var spaces = state.Spaces?.Select(NormalizeStoredSpace).ToList() ?? [];
        ValidateRegistry(spaces);
        var changed = state.Version != CurrentVersion;
        foreach (var builtIn in BuiltIns())
        {
            if (spaces.Any(space => space.Id == builtIn.Id)) continue;
            spaces.Add(builtIn);
            changed = true;
        }
        if (!changed) return state with { Spaces = spaces.Select(CloneSpace).ToArray() };
        var reconciled = state with { Version = CurrentVersion, Spaces = spaces, Deletions = state.Deletions?.ToArray() ?? [] };
        await _settings.SetAsync(SettingsKey, reconciled, cancellationToken).ConfigureAwait(false);
        return reconciled with { Spaces = reconciled.Spaces.Select(CloneSpace).ToArray() };
    }

    private async ValueTask<ISettingsCommitAdmission?> CaptureAdmissionAsync(CancellationToken token) =>
        _captureAdmission is null ? null : await _captureAdmission(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Current Space commit authority is unavailable.");

    private async Task<bool> TryCommitAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission? admission, CancellationToken token)
    {
        if (_settings is IVersionedSettingsGuardedCompareExchange guarded)
        {
            var result = admission is null
                ? await guarded.CompareExchangeGuardedAsync(key, expected, replacement, guards, token).ConfigureAwait(false)
                : await guarded.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token).ConfigureAwait(false);
            if (result.AdmissionRejected) throw new UnauthorizedAccessException("Space authority changed before publication.");
            return result.Exchanged;
        }
        if (admission is not null || guards.Count != 0)
            throw new NotSupportedException("This settings store cannot preserve the required Space commit guards.");
        return (await ((IVersionedSettingsCompareExchange)_settings)
            .CompareExchangeAsync(key, expected, replacement, token).ConfigureAwait(false)).Exchanged;
    }

    private (SpaceRegistryState State, bool Changed) ReadComparedState(string? raw)
    {
        var stored = raw is null ? new SpaceRegistryState(CurrentVersion, []) :
            JsonSerializer.Deserialize<SpaceRegistryState>(raw) ?? throw new InvalidDataException("The Space registry is invalid.");
        if (stored.Version > CurrentVersion) throw new InvalidDataException("The Space registry schema is newer than supported.");
        ValidateDeletionState(stored);
        var spaces = stored.Spaces?.Select(NormalizeStoredSpace).ToList() ?? [];
        ValidateRegistry(spaces);
        var changed = stored.Version != CurrentVersion;
        foreach (var builtIn in BuiltIns())
        {
            if (spaces.Any(space => space.Id == builtIn.Id)) continue;
            spaces.Add(builtIn); changed = true;
        }
        return (stored with { Version = CurrentVersion, Spaces = spaces.Select(CloneSpace).ToArray(), Deletions = stored.Deletions?.ToArray() ?? [] }, changed);
    }

    private async Task<TResult> MutateComparedAsync<TResult>(Func<SpaceRegistryState, (SpaceRegistryState Next, TResult Result)> mutation,
        CancellationToken token)
    {
        var admission = await CaptureAdmissionAsync(token).ConfigureAwait(false);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
            exported.Settings.TryGetValue(SettingsKey, out var raw);
            var (state, _) = ReadComparedState(raw);
            var (next, result) = mutation(state);
            var replacement = JsonSerializer.Serialize(next with { Spaces = next.Spaces.Select(CloneSpace).ToArray(), Deletions = next.Deletions?.ToArray() ?? [] });
            if (await TryCommitAsync(SettingsKey, raw, replacement, new Dictionary<string, string?>(), admission, token).ConfigureAwait(false))
                return result is SpaceDefinition space ? (TResult)(object)CloneSpace(space) : result;
        }
        throw new InvalidOperationException("The Space registry kept changing during this edit; reload and retry.");
    }

    private async Task SetCurrentComparedAsync(Guid? spaceId, CancellationToken token)
    {
        var admission = await CaptureAdmissionAsync(token).ConfigureAwait(false);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
            exported.Settings.TryGetValue(SettingsKey, out var registryRaw);
            exported.Settings.TryGetValue(CurrentSpaceSettingsKey, out var currentRaw);
            if (spaceId is { } id)
            {
                var (state, _) = ReadComparedState(registryRaw);
                if (!state.Spaces.Any(space => space.Id == id && !space.IsArchived))
                    throw new KeyNotFoundException($"Active Space '{id}' was not found.");
            }
            var guards = new Dictionary<string, string?> { [SettingsKey] = registryRaw };
            if (await TryCommitAsync(CurrentSpaceSettingsKey, currentRaw,
                spaceId is { } selected ? JsonSerializer.Serialize(selected.ToString()) : null, guards, admission, token).ConfigureAwait(false)) return;
        }
        throw new InvalidOperationException("The Space registry kept changing before selection; reload and retry.");
    }

    private static SpaceLayoutDocument? CloneLayout(SpaceLayoutDocument? layout)
    {
        if (layout is null) return null;
        var nodes = layout.Nodes.Select(node => node with
        {
            Ports = node.Ports.ToArray(),
            Metadata = new Dictionary<string, string>(node.Metadata, StringComparer.Ordinal)
        }).ToArray();
        var edges = layout.Edges.Select(edge => edge with
        {
            Metadata = new Dictionary<string, string>(edge.Metadata, StringComparer.Ordinal)
        }).ToArray();
        return layout with
        {
            Nodes = nodes,
            Edges = edges,
            Groups = layout.Groups.Select(group => group with { NodeIds = group.NodeIds.ToArray() }).ToArray(),
            Comments = layout.Comments.ToArray()
        };
    }

    private IReadOnlyList<SpaceDefinition> BuiltIns()
    {
        var epoch = DateTimeOffset.UnixEpoch;
        return
        [
            BuiltIn(ChatSpaceId, "Chat", "chat", SpaceKind.Chat, "General conversation, shared objects and authorised app actions.", SpaceThinkingMode.Default, epoch),
            BuiltIn(StudySpaceId, "Study", "book", SpaceKind.Study, "Use the Study product for subject, topic, progress and assessment work.", SpaceThinkingMode.Balanced, epoch),
            BuiltIn(TasksSpaceId, "Tasks", "tasks", SpaceKind.Tasks, "Keep the objective, plan, steps, dependencies, blockers, approvals and observed outputs explicit.", SpaceThinkingMode.Deep, epoch),
            // Product name is "Assistants" per the 13 October 2026 Spaces companion
            // (SPUI-154-159, OWNER-CONFIRMED): Assistants is a Space with a permanent
            // Main conversation per Assistant, Teams, and owned Specialist launch.
            // The stable ID and SpaceKind stay "Agent" on purpose: SpaceID must survive a
            // rename (identity invariant), SpaceKind numeric values must never be
            // renumbered, and persisted records key on both. This is a deliberate
            // display/registry migration, not a new Space and not a deletion.
            BuiltIn(AgentSpaceId, "Assistants", "agents", SpaceKind.Agent, "Open persistent Assistants, each with its permanent Main conversation, their Teams, and the user's owned Specialists. Subagents and delegated workers remain runtime children, not persistent Assistants.", SpaceThinkingMode.Deep, epoch),
            BuiltIn(ShoppingSpaceId, "Shopping", "cart", SpaceKind.Shopping, "Help compare options and preserve the user's requirements and trade-offs.", SpaceThinkingMode.Balanced, epoch),
            BuiltIn(ResearchSpaceId, "Research", "search", SpaceKind.Research, "Separate sourced facts, inference and unresolved questions.", SpaceThinkingMode.Deep, epoch),
            BuiltIn(TranslateSpaceId, "Translate", "translate", SpaceKind.Translate, "Use the selected target languages and preserve one labelled response variant per language.", SpaceThinkingMode.Default, epoch),
            BuiltIn(ExperiencesSpaceId, "Experiences", "experiences", SpaceKind.Experiences, "Run stateful interactive experiences with canonical state and matching conversation branches.", SpaceThinkingMode.Default, epoch)
        ];
    }

    private static SpaceDefinition BuiltIn(
        Guid id,
        string name,
        string icon,
        SpaceKind kind,
        string instructions,
        SpaceThinkingMode thinkingMode,
        DateTimeOffset createdAt)
    {
        var space = new SpaceDefinition(id, name, string.Empty, icon, kind, true, false, null, instructions, thinkingMode,
            [], [], null, createdAt, createdAt,
            Revision: 1,
            ModelPolicy: new SpaceModelPolicy(null),
            ContextPolicy: new SpaceContextPolicy(),
            MemoryPolicy: DefaultMemoryPolicy,
            CapabilityBindings: [],
            Inheritance: DefaultInheritance,
            ContextReferences: [],
            Shares: []);
        return InitializeConfigurationHistory(space);
    }

    private static readonly SpaceMemoryPolicy DefaultMemoryPolicy = new([SpaceMemoryScope.Conversation, SpaceMemoryScope.Space]);
    private static readonly SpaceInheritancePolicy DefaultInheritance = new();

    private static SpaceDefinition NormalizeStoredSpace(SpaceDefinition space)
    {
        var normalized = space with
        {
            Revision = Math.Max(1, space.Revision),
            ActiveConfigurationRevision = Math.Max(1, space.ActiveConfigurationRevision),
            DraftConfigurationRevision = Math.Max(1, space.DraftConfigurationRevision),
            DraftBaseActiveRevision = Math.Max(1, space.DraftBaseActiveRevision),
            ContextPolicy = space.ContextPolicy ?? new SpaceContextPolicy(),
            CapabilityBindings = space.CapabilityBindings?.ToArray() ?? [],
            Inheritance = space.Inheritance ?? DefaultInheritance,
            ContextReferences = space.ContextReferences?.ToArray() ?? [],
            Shares = space.Shares?.ToArray() ?? [],
            Files = space.Files?.ToArray() ?? [],
            ExamplePairs = space.ExamplePairs?.ToArray() ?? [],
            LayoutDocument = CloneLayout(space.LayoutDocument),
            ModelPolicy = (space.ModelPolicy ?? new SpaceModelPolicy(space.ModelName)) with
            {
                RequiredCapabilities = space.ModelPolicy?.RequiredCapabilities?.ToArray()
            },
            MemoryPolicy = (space.MemoryPolicy ?? DefaultMemoryPolicy) with
            {
                AllowedScopes = space.MemoryPolicy?.AllowedScopes?.ToArray()
            }
        };
        normalized = normalized with
        {
            DraftConfiguration = normalized.DraftConfiguration is null ? null : CloneConfiguration(normalized.DraftConfiguration),
            ConfigurationHistory = normalized.ConfigurationHistory is { Count: > 0 } history
                ? history.Select(revision => revision with { Configuration = CloneConfiguration(revision.Configuration) }).ToArray()
                : [new SpaceConfigurationRevision(normalized.ActiveConfigurationRevision,
                    SpaceConfigurationDocument.From(normalized), normalized.UpdatedAt)]
        };
        return normalized;
    }

    private static SpaceDefinition CloneSpace(SpaceDefinition space) => NormalizeStoredSpace(space);

    private static SpaceConfigurationDocument CloneConfiguration(SpaceConfigurationDocument configuration) => configuration with
    {
        ExamplePairs = configuration.ExamplePairs.ToArray(),
        Files = configuration.Files.ToArray(),
        LayoutDocument = CloneLayout(configuration.LayoutDocument),
        ModelPolicy = configuration.ModelPolicy is null ? null : configuration.ModelPolicy with
            { RequiredCapabilities = configuration.ModelPolicy.RequiredCapabilities?.ToArray() },
        MemoryPolicy = configuration.MemoryPolicy is null ? null : configuration.MemoryPolicy with
            { AllowedScopes = configuration.MemoryPolicy.AllowedScopes?.ToArray() },
        CapabilityBindings = configuration.CapabilityBindings.ToArray(),
        ContextReferences = configuration.ContextReferences.ToArray(),
        Shares = configuration.Shares.ToArray()
    };

    private static SpaceDefinition InitializeConfigurationHistory(SpaceDefinition space)
    {
        var revision = Math.Max(1, space.ActiveConfigurationRevision);
        return space with
        {
            ActiveConfigurationRevision = revision,
            DraftConfigurationRevision = revision,
            DraftBaseActiveRevision = revision,
            ConfigurationHistory = [new SpaceConfigurationRevision(revision,
                CloneConfiguration(SpaceConfigurationDocument.From(space)), space.UpdatedAt)]
        };
    }

    private static SpaceDefinition AppendConfigurationRevision(SpaceDefinition space,
        SpaceConfigurationDocument configuration, long revision, DateTimeOffset at)
    {
        var history = space.ConfigurationHistory.Where(item => item.Revision != revision).Append(
            new SpaceConfigurationRevision(revision, CloneConfiguration(configuration), at))
            .OrderBy(item => item.Revision).TakeLast(100).ToArray();
        return space with { ConfigurationHistory = history };
    }

    private static bool ConfigurationEquals(SpaceConfigurationDocument first, SpaceConfigurationDocument second) =>
        StringComparer.Ordinal.Equals(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));

    private static void ValidateConfiguration(SpaceDefinition owner, SpaceConfigurationDocument configuration,
        IReadOnlyList<SpaceDefinition> peers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var projection = configuration.ApplyTo(owner with { DraftConfiguration = null });
        ValidateDefinition(projection, peers);
    }

    private static void ValidateLayout(SpaceLayoutDocument layout)
    {
        if (layout.Nodes is null || layout.Edges is null || layout.Nodes.Count > SpaceLayoutDocument.MaximumNodes ||
            layout.Edges.Count > SpaceLayoutDocument.MaximumEdges || layout.SchemaVersion is < 1 or > SpaceLayoutDocument.CurrentSchemaVersion ||
            !Enum.IsDefined(layout.State) || layout.Revision < 0 || layout.Groups is null || layout.Comments is null)
            throw new ArgumentException("Space layout graph is missing collections or exceeds supported bounds.", nameof(layout));
        var nodes = new Dictionary<Guid, SpaceLayoutNode>();
        foreach (var node in layout.Nodes)
        {
            if (node is null || node.Id == Guid.Empty || string.IsNullOrWhiteSpace(node.Category) ||
                node.SchemaVersion < 1 ||
                string.IsNullOrWhiteSpace(node.Title) || !double.IsFinite(node.X) || !double.IsFinite(node.Y) ||
                !double.IsFinite(node.Width) || !double.IsFinite(node.Height) || node.Width <= 0 || node.Height <= 0 ||
                node.Ports is null || node.Metadata is null || !nodes.TryAdd(node.Id, node))
                throw new ArgumentException("Space layout nodes must have unique IDs and valid geometry.", nameof(layout));
            if (node.Ports.Any(port => port is null || string.IsNullOrWhiteSpace(port.Id) ||
                                       !Enum.IsDefined(port.Direction)) ||
                node.Ports.Select(port => port.Id).Distinct(StringComparer.Ordinal).Count() != node.Ports.Count)
                throw new ArgumentException($"Layout node '{node.Id}' has invalid or duplicate ports.", nameof(layout));
        }
        var edgeIds = new HashSet<Guid>();
        foreach (var edge in layout.Edges)
        {
            if (edge is null || edge.Id == Guid.Empty || !edgeIds.Add(edge.Id) ||
                !nodes.TryGetValue(edge.FromNodeId, out var source) || !nodes.TryGetValue(edge.ToNodeId, out var target))
                throw new ArgumentException("Space layout edges must have unique IDs and reference existing nodes.", nameof(layout));
            var from = source.Ports.SingleOrDefault(port => port.Id == edge.FromPortId);
            var to = target.Ports.SingleOrDefault(port => port.Id == edge.ToPortId);
            if (from?.Direction != SpaceLayoutPortDirection.Output || to?.Direction != SpaceLayoutPortDirection.Input)
                throw new ArgumentException($"Layout edge '{edge.Id}' must connect an output port to an input port.", nameof(layout));
        }
        var groups = new HashSet<Guid>();
        foreach (var group in layout.Groups)
            if (group is null || group.Id == Guid.Empty || string.IsNullOrWhiteSpace(group.Title) ||
                group.NodeIds is null || !groups.Add(group.Id) || group.NodeIds.Distinct().Count() != group.NodeIds.Count ||
                group.NodeIds.Any(nodeId => !nodes.ContainsKey(nodeId)))
                throw new ArgumentException("Space layout groups require unique IDs and references to existing nodes.", nameof(layout));
        var comments = new HashSet<Guid>();
        foreach (var comment in layout.Comments)
            if (comment is null || comment.Id == Guid.Empty || string.IsNullOrWhiteSpace(comment.Text) ||
                !double.IsFinite(comment.X) || !double.IsFinite(comment.Y) || !double.IsFinite(comment.Width) ||
                !double.IsFinite(comment.Height) || comment.Width <= 0 || comment.Height <= 0 || !comments.Add(comment.Id))
                throw new ArgumentException("Space layout comments require unique IDs and valid geometry.", nameof(layout));
    }

    /// <summary>Side-effect-free validation entry point for an authored Space graph draft.
    /// Resource availability and grants remain owned by their respective registries.</summary>
    public static void ValidateLayoutDocument(SpaceLayoutDocument layout) => ValidateLayout(layout);

    private static void ValidateRegistry(IReadOnlyList<SpaceDefinition> spaces)
    {
        if (spaces.Select(space => space.Id).Distinct().Count() != spaces.Count)
            throw new InvalidDataException("Space registry contains duplicate stable IDs.");
        if (spaces.Select(space => space.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != spaces.Count)
            throw new InvalidDataException("Space registry contains duplicate names.");
        foreach (var space in spaces)
        {
            try { ValidateDefinition(space, spaces.Where(other => other.Id != space.Id).ToArray()); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException($"Space '{space.Id}' has invalid persisted data.", exception);
            }
        }
        foreach (var space in spaces.Where(item => item.ParentSpaceId.HasValue))
            ValidateParent(spaces, space.Id, space.ParentSpaceId);
    }

    private static void ValidateDefinition(SpaceDefinition space, IReadOnlyList<SpaceDefinition> peers)
    {
        if (space.Id == Guid.Empty) throw new InvalidDataException("Space stable ID cannot be empty.");
        if (space.Name is null || space.Description is null || space.IconKey is null || space.Instructions is null)
            throw new ArgumentException("Space text fields cannot be null.");
        _ = NormalizeName(space.Name);
        EnsureUniqueName(peers, space.Name, space.Id);
        if (peers.Any(peer => peer.DraftConfiguration is { } draft &&
            StringComparer.OrdinalIgnoreCase.Equals(draft.Name.Trim(), space.Name.Trim())))
            throw new InvalidOperationException($"A saved Space draft already uses the name '{space.Name}'.");
        if (!Enum.IsDefined(space.Kind) || !Enum.IsDefined(space.ThinkingMode))
            throw new ArgumentException("Space type or thinking mode is unknown.");
        if (space.Position < 0 || space.Revision < 1 || space.ActiveConfigurationRevision < 1 ||
            space.DraftConfigurationRevision < 1 || space.DraftBaseActiveRevision < 1 ||
            space.DraftBaseActiveRevision > space.ActiveConfigurationRevision)
            throw new ArgumentOutOfRangeException(nameof(space), "Space position and revision are invalid.");
        if (space.ConfigurationHistory is null || space.ConfigurationHistory.Count is < 1 or > 100 ||
            space.ConfigurationHistory.Any(revision => revision is null || revision.Revision < 1 ||
                revision.Revision > space.ActiveConfigurationRevision || revision.Configuration is null) ||
            space.ConfigurationHistory.Select(revision => revision.Revision).Distinct().Count() != space.ConfigurationHistory.Count ||
            !space.ConfigurationHistory.Any(revision => revision.Revision == space.ActiveConfigurationRevision))
            throw new ArgumentException("Space configuration history is invalid or missing its active revision.");
        if (space.ContextPolicy is { } context &&
            (!Enum.IsDefined(context.WebAccess) || context.MaximumRetrievedItems is < 0 or > 1000))
            throw new ArgumentException("Space context policy is invalid.");
        if (space.MemoryPolicy is { } memory &&
            (!Enum.IsDefined(memory.DefaultScope) || memory.AllowedScopes?.Any(scope => !Enum.IsDefined(scope)) == true ||
             memory.AllowedScopes is { } allowed && !allowed.Contains(memory.DefaultScope)))
            throw new ArgumentException("Space memory policy is invalid.");
        if (space.ModelPolicy is { } modelPolicy &&
            (modelPolicy.RequiredCapabilities?.Any(string.IsNullOrWhiteSpace) == true ||
             modelPolicy.DefaultModelId is not null && string.IsNullOrWhiteSpace(modelPolicy.DefaultModelId) ||
             modelPolicy.ProviderConstraint is not null && string.IsNullOrWhiteSpace(modelPolicy.ProviderConstraint)))
            throw new ArgumentException("Model policy identifiers and required capabilities must be non-empty when supplied.");
        if (space.CapabilityBindings is null || space.CapabilityBindings.Any(binding => binding is null ||
                string.IsNullOrWhiteSpace(binding.CapabilityId) || !Enum.IsDefined(binding.Kind) || !Enum.IsDefined(binding.Override)) ||
            space.CapabilityBindings.Select(binding => (binding.Kind, binding.CapabilityId)).Distinct().Count() != space.CapabilityBindings.Count)
            throw new ArgumentException("Space capability bindings must be valid and unique.");
        if (space.ContextReferences is null || space.ContextReferences.Any(reference => reference is null ||
                reference.ContextId == Guid.Empty || string.IsNullOrWhiteSpace(reference.OwnerAppId) ||
                string.IsNullOrWhiteSpace(reference.CanonicalEntityId) || !Enum.IsDefined(reference.Kind) ||
                reference.HostedFileId == Guid.Empty ||
                !Enum.IsDefined(reference.Permission) || !Enum.IsDefined(reference.IndexState) ||
                reference.ContentSha256 is { } digest && (reference.Kind != SpaceContextReferenceKind.File ||
                    reference.OwnerAppId != "files" || !IsSha256Digest(digest)) ||
                reference.ParentFolderId is { } parentFolderId && (parentFolderId == Guid.Empty ||
                    reference.Kind != SpaceContextReferenceKind.File || reference.OwnerAppId != "files")) ||
            space.ContextReferences.Select(reference => reference.ContextId).Distinct().Count() != space.ContextReferences.Count)
            throw new ArgumentException("Space context references must be valid and unique.");
        if (space.Shares is null || space.Shares.Any(grant => grant is null ||
                string.IsNullOrWhiteSpace(grant.PrincipalId) || !Enum.IsDefined(grant.Role)) ||
            space.Shares.Select(grant => grant.PrincipalId).Distinct(StringComparer.Ordinal).Count() != space.Shares.Count)
            throw new ArgumentException("Space share grants must be valid and unique.");
        if (space.Inheritance is null || space.ExamplePairs is null || space.Files is null)
            throw new ArgumentException("Space configuration collections and inheritance policy are required.");
        if (space.Files.Any(file => file is null || string.IsNullOrWhiteSpace(file.Path) ||
            string.IsNullOrWhiteSpace(file.DisplayName) || !Enum.IsDefined(file.Permission)))
            throw new ArgumentException("Space file references must have valid identifying metadata.");
        if (space.CapabilityCategoryPolicy is { } categoryPolicy &&
            (!Enum.IsDefined(categoryPolicy.Skills) || !Enum.IsDefined(categoryPolicy.Plugins) ||
             !Enum.IsDefined(categoryPolicy.Apps) || !Enum.IsDefined(categoryPolicy.Models) ||
             !Enum.IsDefined(categoryPolicy.Memory)))
            throw new ArgumentException("Space capability category policy contains an unknown state.");
        if (space.LayoutDocument is { } layout) ValidateLayout(layout);
        if (space.DraftConfiguration is { } draft)
            ValidateConfiguration(space with { DraftConfiguration = null }, draft, peers);
        if (space.ParentSpaceId is { } parent) ValidateParent(peers.Append(space).ToArray(), space.Id, parent);
    }

    private static void ValidateParent(IReadOnlyList<SpaceDefinition> spaces, Guid spaceId, Guid? parentSpaceId)
    {
        if (parentSpaceId is null) return;
        if (parentSpaceId == spaceId) throw new InvalidOperationException("A Space cannot be its own parent.");
        var parent = FindRequired(spaces, parentSpaceId.Value);
        if (parent.IsArchived) throw new InvalidOperationException("An archived Space cannot be a parent.");
        var current = parent;
        var visited = new HashSet<Guid> { spaceId };
        while (true)
        {
            if (!visited.Add(current.Id)) throw new InvalidOperationException("The requested hierarchy would create a cycle.");
            if (current.ParentSpaceId is not { } nextId) break;
            current = FindRequired(spaces, nextId);
        }
    }

    private static EffectiveSpacePolicy ResolveGraphCandidatePolicy(IReadOnlyList<SpaceDefinition> spaces,
        SpaceDefinition current, SpaceConfigurationDocument draft)
    {
        // The SAME mutation snapshot supplies every active ancestor. No settings/body
        // reads, grants or asynchronous resolver call are permitted inside this CAS.
        var chain = new List<SpaceDefinition>();
        var seen = new HashSet<Guid>();
        var candidate = draft.ApplyTo(current with { DraftConfiguration = null });
        while (true)
        {
            if (chain.Count >= 128 || !seen.Add(candidate.Id) || candidate.IsArchived)
                throw new InvalidOperationException("The candidate graph requires an available bounded acyclic Space ancestry.");
            chain.Add(candidate);
            if (candidate.ParentSpaceId is not { } parentId) break;
            candidate = FindRequired(spaces, parentId); // Active fields only; a parent's draft is not policy.
        }
        chain.Reverse();
        return SpacePolicyResolver.ResolveKnownChain(chain, current.Id);
    }

    private static bool AncestryMatches(IReadOnlyList<SpaceDefinition> spaces, Guid spaceId,
        IReadOnlyList<SpaceActivationAncestryRevision> expected)
    {
        if (expected is null || expected.Count is < 1 or > 128 ||
            expected.Any(revision => revision is null || revision.SpaceId == Guid.Empty || revision.SpaceRevision < 1)) return false;
        var chain = new List<SpaceDefinition>();
        var seen = new HashSet<Guid>();
        var id = spaceId;
        while (true)
        {
            if (chain.Count >= 128 || !seen.Add(id)) return false;
            var current = spaces.SingleOrDefault(space => space.Id == id);
            if (current is null || current.IsArchived) return false;
            chain.Add(current);
            if (current.ParentSpaceId is not { } parentId) break;
            id = parentId;
        }
        chain.Reverse();
        return chain.Count == expected.Count && chain.Select((space, index) =>
            space.Id == expected[index].SpaceId && space.Revision == expected[index].SpaceRevision).All(matches => matches);
    }

    private static SpaceDefinition FindRequired(IReadOnlyList<SpaceDefinition> spaces, Guid id) =>
        spaces.FirstOrDefault(space => space.Id == id)
        ?? throw new KeyNotFoundException($"Space '{id}' was not found.");

    private static string NormalizeName(string name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("A Space name is required.", nameof(name));
        if (value.Length > 80) throw new ArgumentException("Space names can be at most 80 characters.", nameof(name));
        return value;
    }

    private static void EnsureUniqueName(IReadOnlyList<SpaceDefinition> spaces, string name, Guid? exceptId = null)
    {
        if (spaces.Any(space => space.Id != exceptId && space.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A Space named '{name}' already exists.");
    }

    private static bool IsSha256Digest(string value) => value.Length == 71 &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static string MakeUniqueName(IReadOnlyList<SpaceDefinition> spaces, string desired)
    {
        if (!spaces.Any(space => space.Name.Equals(desired, StringComparison.OrdinalIgnoreCase))) return desired;
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{desired} {suffix}";
            if (!spaces.Any(space => space.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        throw new InvalidOperationException("Could not create a unique Space name.");
    }
}
