using Haven.Application;
using Haven.Core;
using RegisteredSpaceDefinition = Haven.Application.SpaceDefinition;

namespace HavenOS.Apps.Spaces;

/// <summary>
/// A stable identity for a Spaces destination. It is separate from the backing registry record so
/// Chat can be explicitly scoped even though the existing Chat surface remains unscoped.
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

    public static SpaceScope Chat { get; } = new("spaces.chat", null);

    public static SpaceScope Study { get; } = new("spaces.study", SpaceRegistry.StudySpaceId);

    public static SpaceScope Tasks { get; } = new("spaces.tasks", SpaceRegistry.AgentSpaceId);

    public static SpaceScope ForCustom(Guid registeredSpaceId) =>
        new($"spaces.custom.{registeredSpaceId:N}", registeredSpaceId);
}

public enum SpacesDestinationKind
{
    Chat,
    Study,
    Tasks,
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
    bool IsBuiltIn);

public sealed record SpacesSidebarDestination(
    string Id,
    string Label,
    string IconKey,
    SpaceScope Scope,
    SpacesDestinationKind Destination,
    bool IsBuiltIn);

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
            SpacesDestinationKind.Study or SpacesDestinationKind.Tasks or SpacesDestinationKind.Custom =>
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
            "Start an unscoped Chat conversation.",
            "chat",
            true),
        new(
            SpaceScope.Study,
            SpacesDestinationKind.Study,
            "Study",
            "Open the existing Study product with its configured Space scope.",
            "book",
            true),
        new(
            SpaceScope.Tasks,
            SpacesDestinationKind.Tasks,
            "Tasks",
            "Open the existing Tasks product with its configured Space scope.",
            "tasks",
            true)
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
            .. BuiltInDefinitions.Select(ToSidebarDestination),
            .. registeredSpaces.Where(space => !space.IsBuiltIn).Select(space => ToSidebarDestination(FromRegisteredSpace(space)))
        ];
    }

    public async Task<SpacesSpaceDefinition> CreateCustomSpaceAsync(
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var registeredSpace = await _spaces.CreateAsync(name, description, cancellationToken).ConfigureAwait(false);
        return FromRegisteredSpace(registeredSpace);
    }

    public async Task<SpacesAction> CreateOpenActionAsync(
        SpaceScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope == SpaceScope.Chat)
            return new(SpacesActionKind.Open, new(FindBuiltIn(SpacesDestinationKind.Chat), null));

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
            false);

    private static SpacesSidebarDestination ToSidebarDestination(SpacesSpaceDefinition definition) =>
        new(
            definition.Scope.Key,
            definition.Label,
            definition.IconKey,
            definition.Scope,
            definition.Destination,
            definition.IsBuiltIn);
}
