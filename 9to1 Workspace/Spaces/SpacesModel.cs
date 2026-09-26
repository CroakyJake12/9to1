using Haven.Application;
using Haven.Core;
using RegisteredSpaceDefinition = Haven.Application.SpaceDefinition;

namespace HavenOS.Apps.Spaces;

/// <summary>
/// A stable Space-scoped identity, separate from mutable display names and sidebar position.
/// </summary>
public sealed record SpaceScope
{
    private SpaceScope(string key, Guid? registeredSpaceId)
    {
        Key = key;
        RegisteredSpaceId = registeredSpaceId;
    }

    public string Key { get; }

    public Guid? RegisteredSpaceId { get; }

    public static SpaceScope Chat { get; } = new("spaces.chat", SpaceRegistry.ChatSpaceId);

    public static SpaceScope Study { get; } = new("spaces.study", SpaceRegistry.StudySpaceId);

    public static SpaceScope Tasks { get; } = new("spaces.tasks", SpaceRegistry.TasksSpaceId);

    public static SpaceScope Agent { get; } = new("spaces.agent", SpaceRegistry.AgentSpaceId);

    public static SpaceScope Shopping { get; } = new("spaces.shopping", SpaceRegistry.ShoppingSpaceId);

    public static SpaceScope Research { get; } = new("spaces.research", SpaceRegistry.ResearchSpaceId);

    public static SpaceScope Translate { get; } = new("spaces.translate", SpaceRegistry.TranslateSpaceId);

    public static SpaceScope Experiences { get; } = new("spaces.experiences", SpaceRegistry.ExperiencesSpaceId);

    public static SpaceScope ForCustom(Guid registeredSpaceId) =>
        new($"spaces.custom.{registeredSpaceId:N}", registeredSpaceId);
}

public enum SpacesDestinationKind
{
    Chat,
    Study,
    Tasks,
    Agent,
    Shopping,
    Research,
    Translate,
    Experiences,
    Custom
}

/// <summary>
/// A platform-neutral Spaces definition. The backing registry record, when present, remains owned
/// by Haven.Application and is supplied only when an existing surface needs to be opened.
/// </summary>
public sealed record SpacesSpaceDefinition(
    SpaceScope Scope,
    SpacesDestinationKind Destination,
    string Label,
    string Description,
    string IconKey,
    bool IsBuiltIn,
    Guid? ParentSpaceId = null,
    int Position = 0,
    long Revision = 1);

public sealed record SpacesSidebarDestination(
    string Id,
    string Label,
    string IconKey,
    SpaceScope Scope,
    SpacesDestinationKind Destination,
    bool IsBuiltIn,
    Guid? ParentSpaceId = null,
    int Position = 0,
    long Revision = 1);

/// <summary>
/// The explicit context delivered to an existing app surface. A host receives the registry record
/// rather than a Spaces-owned copy, so Chat, Study, Tasks, and custom Spaces retain their current
/// routing and persistence owners.
/// </summary>
public sealed record SpacesContext(
    SpacesSpaceDefinition Definition,
    RegisteredSpaceDefinition? RegisteredSpace)
{
    public SpaceScope Scope => Definition.Scope;
}

public enum SpacesActionKind
{
    Open
}

public sealed record SpacesAction(SpacesActionKind Kind, SpacesContext Context);

/// <summary>
/// Implemented by a shell or platform host to perform a typed Spaces action.
/// </summary>
public interface ISpacesActionHost
{
    Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts the typed model to the existing Haven navigation surface without recreating Chat, Study,
/// Tasks, or configured Space launch behaviour.
/// </summary>
public sealed class SpacesNavigationActionHost : ISpacesActionHost
{
    private readonly ISpacesNavigationHost _navigation;

    public SpacesNavigationActionHost(ISpacesNavigationHost navigation)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        return action.Kind switch
        {
            SpacesActionKind.Open => OpenAsync(action.Context, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown Spaces action.")
        };
    }

    private Task OpenAsync(SpacesContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Definition.Destination switch
        {
        SpacesDestinationKind.Chat => _navigation.OpenModeAsync(HavenMode.Chat, cancellationToken),
        SpacesDestinationKind.Study or SpacesDestinationKind.Tasks or SpacesDestinationKind.Agent or
            SpacesDestinationKind.Shopping or SpacesDestinationKind.Research or
            SpacesDestinationKind.Translate or SpacesDestinationKind.Experiences or SpacesDestinationKind.Custom =>
                _navigation.OpenSpaceAsync(
                    context.RegisteredSpace ?? throw new InvalidOperationException(
                        $"Spaces destination '{context.Definition.Label}' requires a registered Space."),
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(context),
                context.Definition.Destination,
                "Unknown Spaces destination.")
        };
    }
}

/// <summary>
/// Platform-neutral catalog and context factory for the Spaces sidebar.
/// </summary>
public sealed class SpacesModel
{
    private static readonly IReadOnlyList<SpacesSpaceDefinition> BuiltInDefinitions =
    [
        new(
            SpaceScope.Chat,
            SpacesDestinationKind.Chat,
            "Chat",
            "Start a general conversation inside the Chat Space.",
            "chat",
            true),
        new(
            SpaceScope.Study,
            SpacesDestinationKind.Study,
            "Study",
            "Open the Study product with its configured Space scope.",
            "book",
            true),
        new(
            SpaceScope.Tasks,
            SpacesDestinationKind.Tasks,
            "Tasks",
            "Plan and supervise individual work items and runs.",
            "tasks",
            true),
        new(SpaceScope.Agent, SpacesDestinationKind.Agent, "Agent", "Select and supervise persistent Agents and their runs.", "agents", true),
        new(SpaceScope.Shopping, SpacesDestinationKind.Shopping, "Shopping", "Compare products while preserving source provenance.", "cart", true),
        new(SpaceScope.Research, SpacesDestinationKind.Research, "Research", "Manage source-backed research plans, findings and outputs.", "search", true),
        new(SpaceScope.Translate, SpacesDestinationKind.Translate, "Translate", "Translate through the normal Space conversation and context.", "translate", true),
        new(SpaceScope.Experiences, SpacesDestinationKind.Experiences, "Experiences", "Run stateful interactive experiences.", "experiences", true)
    ];

    private readonly SpaceRegistry _spaces;

    public SpacesModel(SpaceRegistry spaces)
    {
        _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
    }

    public static IReadOnlyList<SpacesSpaceDefinition> BuiltIns => BuiltInDefinitions;

    public async Task<IReadOnlyList<SpacesSidebarDestination>> GetSidebarDestinationsAsync(
        CancellationToken cancellationToken = default)
    {
        var registeredSpaces = await _spaces.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return
        [
            .. BuiltInDefinitions.Select(definition => ToSidebarDestination(definition)),
            .. registeredSpaces.Where(space => !space.IsBuiltIn).Select(space => ToSidebarDestination(FromRegisteredSpace(space), space))
        ];
    }

    public async Task<SpacesSpaceDefinition> CreateCustomSpaceAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateCustomSpaceAsync(name, description, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SpacesSpaceDefinition> CreateCustomSpaceAsync(
        string name,
        string? description,
        Guid? parentSpaceId,
        CancellationToken cancellationToken = default)
    {
        var registeredSpace = await _spaces.CreateAsync(name, description, parentSpaceId, cancellationToken).ConfigureAwait(false);
        return FromRegisteredSpace(registeredSpace);
    }

    public async Task<SpacesSpaceDefinition> RenameCustomSpaceAsync(
        SpaceScope scope,
        string name,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var space = await RequireRegisteredSpaceAsync(scope, cancellationToken).ConfigureAwait(false);
        if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Space names are protected.");
        return FromRegisteredSpace(await _spaces.UpdateAsync(space with { Name = name }, expectedRevision, cancellationToken).ConfigureAwait(false));
    }

    public async Task<SpacesSpaceDefinition> ForkSpaceAsync(
        SpaceScope scope,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        var space = await RequireRegisteredSpaceAsync(scope, cancellationToken).ConfigureAwait(false);
        return FromRegisteredSpace(await _spaces.ForkAsync(space.Id, name, cancellationToken).ConfigureAwait(false));
    }

    public async Task ArchiveCustomSpaceAsync(SpaceScope scope, CancellationToken cancellationToken = default)
    {
        var space = await RequireRegisteredSpaceAsync(scope, cancellationToken).ConfigureAwait(false);
        if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Spaces cannot be archived.");
        await _spaces.SetArchivedAsync(space.Id, true, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreCustomSpaceAsync(SpaceScope scope, CancellationToken cancellationToken = default)
    {
        var space = await RequireRegisteredSpaceAsync(scope, cancellationToken).ConfigureAwait(false);
        if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Spaces cannot be archived.");
        await _spaces.RestoreAsync(space.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SpacesSpaceDefinition> MoveCustomSpaceAsync(
        SpaceScope scope,
        Guid? parentSpaceId,
        int? position = null,
        CancellationToken cancellationToken = default)
    {
        var space = await RequireRegisteredSpaceAsync(scope, cancellationToken).ConfigureAwait(false);
        if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Spaces cannot be moved.");
        return FromRegisteredSpace(await _spaces.MoveAsync(space.Id, parentSpaceId, position, cancellationToken).ConfigureAwait(false));
    }

    private async Task<RegisteredSpaceDefinition> RequireRegisteredSpaceAsync(
        SpaceScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var id = scope.RegisteredSpaceId ?? throw new InvalidOperationException("This destination has no registered Space record.");
        var space = await _spaces.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Registered Space '{id}' was not found.");
        if (!space.IsBuiltIn && scope != SpaceScope.ForCustom(id))
            throw new InvalidOperationException("The supplied Space scope does not match the registered custom Space.");
        if (space.IsBuiltIn && !BuiltInDefinitions.Any(definition => definition.Scope == scope))
            throw new InvalidOperationException("The supplied scope does not match the registered built-in Space.");
        return space;
    }

    public async Task<SpacesAction> CreateOpenActionAsync(
        SpaceScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        var registeredSpaceId = scope.RegisteredSpaceId
            ?? throw new InvalidOperationException($"Spaces scope '{scope.Key}' does not identify a registered Space.");
        var registeredSpace = await _spaces.GetAsync(registeredSpaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Registered Space '{registeredSpaceId}' was not found.");
        var definition = registeredSpace.IsBuiltIn
            ? FindBuiltIn(registeredSpaceId)
            : FromRegisteredSpace(registeredSpace);
        if (scope != definition.Scope)
            throw new InvalidOperationException(
                $"Spaces scope '{scope.Key}' does not match registered Space '{registeredSpaceId}'.");
        return new(SpacesActionKind.Open, new(definition, registeredSpace));
    }

    private static SpacesSpaceDefinition FindBuiltIn(SpacesDestinationKind destination) =>
        BuiltInDefinitions.FirstOrDefault(definition => definition.Destination == destination)
        ?? throw new KeyNotFoundException($"Built-in Spaces destination '{destination}' was not found.");

    private static SpacesSpaceDefinition FindBuiltIn(Guid registeredSpaceId) =>
        BuiltInDefinitions.FirstOrDefault(definition => definition.Scope.RegisteredSpaceId == registeredSpaceId)
        ?? throw new KeyNotFoundException($"Registered Space '{registeredSpaceId}' is not a Spaces built-in.");

    private static SpacesSpaceDefinition FromRegisteredSpace(RegisteredSpaceDefinition space) =>
        new(
            SpaceScope.ForCustom(space.Id),
            SpacesDestinationKind.Custom,
            space.Name,
            space.Description,
            space.IconKey,
            false,
            space.ParentSpaceId,
            space.Position,
            space.Revision);

    private static SpacesSidebarDestination ToSidebarDestination(SpacesSpaceDefinition definition, RegisteredSpaceDefinition? registered = null) =>
        new(
            definition.Scope.Key,
            definition.Label,
            definition.IconKey,
            definition.Scope,
            definition.Destination,
            definition.IsBuiltIn,
            registered?.ParentSpaceId ?? definition.ParentSpaceId,
            registered?.Position ?? definition.Position,
            registered?.Revision ?? definition.Revision);
}
