namespace Haven.Application;

public sealed class SpaceRegistry
{
    private const string SettingsKey = "spaces.registry";
    private const string CurrentSpaceSettingsKey = "spaces.current";
    private const int CurrentVersion = 2;
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

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
            var insertion = Math.Clamp(position ?? siblings.Count, 0, siblings.Count);
            siblings.Insert(insertion, moving with { ParentSpaceId = parentSpaceId });
            var updatedById = siblings.Select((space, index) => space with
            {
                ParentSpaceId = parentSpaceId,
                Position = index,
                Revision = checked(space.Revision + 1),
                UpdatedAt = now
            }).ToDictionary(space => space.Id);
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
                CreatedAt = now,
                UpdatedAt = now
            };
            return (state with { Spaces = [.. state.Spaces, fork] }, fork);
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
                changed = changed with { Revision = checked(existing.Revision + 1) };
            ValidateDefinition(changed, state.Spaces.Where(space => space.Id != id).ToArray());
            var spaces = state.Spaces.Select(space => space.Id == id ? changed : space).ToArray();
            return (state with { Spaces = spaces }, changed);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<SpaceRegistryState, (SpaceRegistryState State, TResult Result)> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = mutation(state);
            await _settings.SetAsync(SettingsKey,
                new SpaceRegistryState(next.Version, next.Spaces.Select(CloneSpace).ToArray()), cancellationToken).ConfigureAwait(false);
            return result is SpaceDefinition space ? (TResult)(object)CloneSpace(space) : result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SpaceRegistryState> LoadAndReconcileAsync(CancellationToken cancellationToken)
    {
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, cancellationToken).ConfigureAwait(false)
            ?? new SpaceRegistryState(CurrentVersion, []);
        if (state.Version > CurrentVersion)
            throw new InvalidDataException($"Space registry version {state.Version} is newer than supported version {CurrentVersion}.");
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
        var reconciled = new SpaceRegistryState(CurrentVersion, spaces);
        await _settings.SetAsync(SettingsKey, reconciled, cancellationToken).ConfigureAwait(false);
        return reconciled with { Spaces = reconciled.Spaces.Select(CloneSpace).ToArray() };
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
        return new SpaceLayoutDocument(nodes, edges);
    }

    private IReadOnlyList<SpaceDefinition> BuiltIns()
    {
        var epoch = DateTimeOffset.UnixEpoch;
        return
        [
            BuiltIn(ChatSpaceId, "Chat", "chat", SpaceKind.Chat, "General conversation, shared objects and authorised app actions.", SpaceThinkingMode.Default, epoch),
            BuiltIn(StudySpaceId, "Study", "book", SpaceKind.Study, "Use the Study product for subject, topic, progress and assessment work.", SpaceThinkingMode.Balanced, epoch),
            BuiltIn(TasksSpaceId, "Tasks", "tasks", SpaceKind.Tasks, "Keep the objective, plan, steps, dependencies, blockers, approvals and observed outputs explicit.", SpaceThinkingMode.Deep, epoch),
            BuiltIn(AgentSpaceId, "Agent", "agents", SpaceKind.Agent, "Show persistent Agent identity and effective permissions; Subagents remain runtime children, not persistent Agents.", SpaceThinkingMode.Deep, epoch),
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
        DateTimeOffset createdAt) =>
        new(id, name, string.Empty, icon, kind, true, false, null, instructions, thinkingMode,
            [], [], null, createdAt, createdAt,
            Revision: 1,
            ModelPolicy: new SpaceModelPolicy(null),
            ContextPolicy: new SpaceContextPolicy(),
            MemoryPolicy: DefaultMemoryPolicy,
            CapabilityBindings: [],
            Inheritance: DefaultInheritance,
            ContextReferences: [],
            Shares: []);

    private static readonly SpaceMemoryPolicy DefaultMemoryPolicy = new([SpaceMemoryScope.Conversation, SpaceMemoryScope.Space]);
    private static readonly SpaceInheritancePolicy DefaultInheritance = new();

    private static SpaceDefinition NormalizeStoredSpace(SpaceDefinition space) => space with
    {
        Revision = Math.Max(1, space.Revision),
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

    private static SpaceDefinition CloneSpace(SpaceDefinition space) => NormalizeStoredSpace(space);

    private static void ValidateLayout(SpaceLayoutDocument layout)
    {
        if (layout.Nodes is null || layout.Edges is null || layout.Nodes.Count > 500 || layout.Edges.Count > 2000)
            throw new ArgumentException("Space layout graph is missing collections or exceeds supported bounds.", nameof(layout));
        var nodes = new Dictionary<Guid, SpaceLayoutNode>();
        foreach (var node in layout.Nodes)
        {
            if (node is null || node.Id == Guid.Empty || string.IsNullOrWhiteSpace(node.Category) ||
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
    }

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
        if (!Enum.IsDefined(space.Kind) || !Enum.IsDefined(space.ThinkingMode))
            throw new ArgumentException("Space type or thinking mode is unknown.");
        if (space.Position < 0 || space.Revision < 1)
            throw new ArgumentOutOfRangeException(nameof(space), "Space position and revision are invalid.");
        if (space.ContextPolicy is { } context &&
            (!Enum.IsDefined(context.WebAccess) || context.MaximumRetrievedItems is < 0 or > 1000))
            throw new ArgumentException("Space context policy is invalid.");
        if (space.MemoryPolicy is { } memory &&
            (!Enum.IsDefined(memory.DefaultScope) || memory.AllowedScopes?.Any(scope => !Enum.IsDefined(scope)) == true ||
             memory.AllowedScopes is { } allowed && !allowed.Contains(memory.DefaultScope)))
            throw new ArgumentException("Space memory policy is invalid.");
        if (space.ModelPolicy?.RequiredCapabilities?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Required model capabilities cannot be empty.");
        if (space.CapabilityBindings is null || space.CapabilityBindings.Any(binding => binding is null ||
                string.IsNullOrWhiteSpace(binding.CapabilityId) || !Enum.IsDefined(binding.Kind) || !Enum.IsDefined(binding.Override)) ||
            space.CapabilityBindings.Select(binding => (binding.Kind, binding.CapabilityId)).Distinct().Count() != space.CapabilityBindings.Count)
            throw new ArgumentException("Space capability bindings must be valid and unique.");
        if (space.ContextReferences is null || space.ContextReferences.Any(reference => reference is null ||
                reference.ContextId == Guid.Empty || string.IsNullOrWhiteSpace(reference.OwnerAppId) ||
                string.IsNullOrWhiteSpace(reference.CanonicalEntityId) || !Enum.IsDefined(reference.Kind) ||
                !Enum.IsDefined(reference.Permission) || !Enum.IsDefined(reference.IndexState)) ||
            space.ContextReferences.Select(reference => reference.ContextId).Distinct().Count() != space.ContextReferences.Count)
            throw new ArgumentException("Space context references must be valid and unique.");
        if (space.Shares is null || space.Shares.Any(grant => grant is null ||
                string.IsNullOrWhiteSpace(grant.PrincipalId) || !Enum.IsDefined(grant.Role)) ||
            space.Shares.Select(grant => grant.PrincipalId).Distinct(StringComparer.Ordinal).Count() != space.Shares.Count)
            throw new ArgumentException("Space share grants must be valid and unique.");
        if (space.Inheritance is null || space.ExamplePairs is null || space.Files is null)
            throw new ArgumentException("Space configuration collections and inheritance policy are required.");
        if (space.LayoutDocument is { } layout) ValidateLayout(layout);
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
