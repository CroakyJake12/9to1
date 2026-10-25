namespace Haven.Application;

public enum SpaceKind
{
    General = 0,
    Study = 1,
    Shopping = 2,
    Research = 3,
    Agent = 4,
    Chat = 5,
    Tasks = 6,
    Translate = 7,
    Experiences = 8
}

public enum SpaceWebAccessPolicy { Allowed = 0, Ask = 1, Disabled = 2 }
public enum SpaceMemoryScope { Conversation = 0, Space = 1, Personal = 2, Team = 3, Organisation = 4 }
public enum SpaceCapabilityKind { Plugin = 0, Skill = 1, Agent = 2, Mcp = 3, ConnectedApp = 4 }
public enum SpaceCapabilityOverride { Inherit = 0, Enabled = 1, Disabled = 2 }
public enum SpaceContextReferenceKind
{
    File = 0, Folder = 1, BoardPage = 2, WriteArtifact = 3, PresentArtifact = 4,
    CanvasArtifact = 5, DataArtifact = 6, MailThread = 7, PlannerItem = 8,
    MapRoute = 9, StackProject = 10, SiteProject = 11, Conversation = 12, Upload = 13,
    ConnectedEntity = 14, PictureArtifact = 15, GamesProject = 16
}
public enum SpaceContextPermission { Unknown = 0, Denied = 1, Read = 2, ReadWrite = 3 }
public enum SpaceContextIndexState { Unknown = 0, NotRequired = 1, Pending = 2, Ready = 3, Stale = 4, Failed = 5 }
public enum SpaceShareRole { Viewer = 0, Participant = 1, Editor = 2, Admin = 3 }

public sealed record SpaceModelPolicy(
    string? DefaultModelId,
    IReadOnlyList<string>? RequiredCapabilities = null,
    string? ProviderConstraint = null);

public sealed record SpaceContextPolicy(
    SpaceWebAccessPolicy WebAccess = SpaceWebAccessPolicy.Ask,
    bool SourceOnly = false,
    bool IncludeCurrentSelection = true,
    int? MaximumRetrievedItems = null);

public sealed record SpaceMemoryPolicy(
    IReadOnlyList<SpaceMemoryScope>? AllowedScopes = null,
    SpaceMemoryScope DefaultScope = SpaceMemoryScope.Conversation);

public sealed record SpaceCapabilityBinding(
    string CapabilityId,
    SpaceCapabilityKind Kind,
    SpaceCapabilityOverride Override,
    string? VersionPin = null);

public sealed record SpaceInheritancePolicy(
    bool Sources = false,
    bool ConnectedApps = false,
    bool ModelPolicy = false,
    bool MemoryPolicy = false,
    bool Plugins = true,
    bool Skills = true,
    bool Agents = true);

public sealed record SpaceContextReference(
    Guid ContextId,
    SpaceContextReferenceKind Kind,
    string OwnerAppId,
    string CanonicalEntityId,
    string? RevisionToken,
    SpaceContextPermission Permission,
    SpaceContextIndexState IndexState,
    bool IncludedAutomatically,
    DateTimeOffset AddedAt,
    Guid? HostedFileId = null);

public sealed record SpaceShareGrant(
    string PrincipalId,
    SpaceShareRole Role,
    DateTimeOffset SharedAt);

public enum SpaceThinkingMode
{
    Default = 0,
    Fast = 1,
    Balanced = 2,
    Deep = 3
}

public enum SpaceFilePermission
{
    ReadOnly = 0,
    ReadWrite = 1
}

public sealed record SpaceExamplePair(string User, string Assistant);

public sealed record SpaceFileReference(
    string Path,
    string DisplayName,
    SpaceFilePermission Permission,
    DateTimeOffset AddedAt);

public sealed record SpaceGeneratedSurface(
    string TemplateKey,
    string InputsJson);

public enum SpaceCapabilityCategoryState
{
    Inherited = 0,
    Enabled = 1,
    Disabled = 2
}

public sealed record SpaceCapabilityCategoryPolicy(
    SpaceCapabilityCategoryState Skills = SpaceCapabilityCategoryState.Inherited,
    SpaceCapabilityCategoryState Plugins = SpaceCapabilityCategoryState.Inherited,
    SpaceCapabilityCategoryState Apps = SpaceCapabilityCategoryState.Inherited,
    SpaceCapabilityCategoryState Models = SpaceCapabilityCategoryState.Inherited,
    SpaceCapabilityCategoryState Memory = SpaceCapabilityCategoryState.Inherited);

public sealed record SpaceDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    SpaceKind Kind,
    bool IsBuiltIn,
    bool IsArchived,
    string? ModelName,
    string Instructions,
    SpaceThinkingMode ThinkingMode,
    IReadOnlyList<SpaceExamplePair> ExamplePairs,
    IReadOnlyList<SpaceFileReference> Files,
    SpaceGeneratedSurface? GeneratedSurface,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ForkedFromSpaceId = null,
    SpaceLayoutDocument? LayoutDocument = null,
    Guid? ParentSpaceId = null,
    int Position = 0,
    long Revision = 1,
    SpaceModelPolicy? ModelPolicy = null,
    SpaceContextPolicy? ContextPolicy = null,
    SpaceMemoryPolicy? MemoryPolicy = null,
    IReadOnlyList<SpaceCapabilityBinding>? CapabilityBindings = null,
    SpaceCapabilityCategoryPolicy? CapabilityCategoryPolicy = null,
    SpaceInheritancePolicy? Inheritance = null,
    IReadOnlyList<SpaceContextReference>? ContextReferences = null,
    IReadOnlyList<SpaceShareGrant>? Shares = null)
{
    /// <summary>Monotonic active configuration version; independent of registry CAS revision.</summary>
    public long ActiveConfigurationRevision { get; init; } = 1;
    /// <summary>Revision used by the Builder draft editor; null draft means edits start from active.</summary>
    public long DraftConfigurationRevision { get; init; } = 1;
    /// <summary>The active version on which the retained draft was based.</summary>
    public long DraftBaseActiveRevision { get; init; } = 1;
    /// <summary>One saved draft projection of this Space; never read by runtime consumers.</summary>
    public SpaceConfigurationDocument? DraftConfiguration { get; init; }
    public IReadOnlyList<SpaceConfigurationRevision> ConfigurationHistory { get; init; } = [];

    /// <summary>Origin is derived from the protected registry bit and is not independently mutable.</summary>
    public SpaceOrigin Origin => IsBuiltIn ? SpaceOrigin.BuiltIn : SpaceOrigin.UserCreated;
}

/// <summary>Typed single document projection used by Basic, Advanced and Expert. It contains
/// configuration only; Space identity, ownership, hierarchy and lifecycle stay on SpaceDefinition.</summary>
public sealed record SpaceConfigurationDocument(
    string Name,
    string Description,
    string IconKey,
    string? ModelName,
    string Instructions,
    SpaceThinkingMode ThinkingMode,
    IReadOnlyList<SpaceExamplePair> ExamplePairs,
    IReadOnlyList<SpaceFileReference> Files,
    SpaceGeneratedSurface? GeneratedSurface,
    SpaceLayoutDocument? LayoutDocument,
    SpaceModelPolicy? ModelPolicy,
    SpaceContextPolicy? ContextPolicy,
    SpaceMemoryPolicy? MemoryPolicy,
    IReadOnlyList<SpaceCapabilityBinding> CapabilityBindings,
    SpaceCapabilityCategoryPolicy? CapabilityCategoryPolicy,
    SpaceInheritancePolicy? Inheritance,
    IReadOnlyList<SpaceContextReference> ContextReferences,
    IReadOnlyList<SpaceShareGrant> Shares)
{
    public static SpaceConfigurationDocument From(SpaceDefinition space) => new(
        space.Name, space.Description, space.IconKey, space.ModelName, space.Instructions, space.ThinkingMode,
        space.ExamplePairs?.ToArray() ?? [], space.Files?.ToArray() ?? [], space.GeneratedSurface, space.LayoutDocument,
        space.ModelPolicy is null ? null : space.ModelPolicy with { RequiredCapabilities = space.ModelPolicy.RequiredCapabilities?.ToArray() },
        space.ContextPolicy, space.MemoryPolicy is null ? null : space.MemoryPolicy with { AllowedScopes = space.MemoryPolicy.AllowedScopes?.ToArray() },
        space.CapabilityBindings?.ToArray() ?? [], space.CapabilityCategoryPolicy, space.Inheritance,
        space.ContextReferences?.ToArray() ?? [], space.Shares?.ToArray() ?? []);

    public SpaceDefinition ApplyTo(SpaceDefinition space, bool activateLayout = false) => space with
    {
        Name = Name,
        Description = Description,
        IconKey = IconKey,
        ModelName = ModelName,
        Instructions = Instructions,
        ThinkingMode = ThinkingMode,
        ExamplePairs = ExamplePairs.ToArray(),
        Files = Files.ToArray(),
        GeneratedSurface = GeneratedSurface,
        LayoutDocument = LayoutDocument is null ? null : activateLayout ? LayoutDocument with { State = SpaceLayoutRevisionState.Active } : LayoutDocument,
        ModelPolicy = ModelPolicy is null ? null : ModelPolicy with { RequiredCapabilities = ModelPolicy.RequiredCapabilities?.ToArray() },
        ContextPolicy = ContextPolicy,
        MemoryPolicy = MemoryPolicy is null ? null : MemoryPolicy with { AllowedScopes = MemoryPolicy.AllowedScopes?.ToArray() },
        CapabilityBindings = CapabilityBindings.ToArray(),
        CapabilityCategoryPolicy = CapabilityCategoryPolicy,
        Inheritance = Inheritance,
        ContextReferences = ContextReferences.ToArray(),
        Shares = Shares.ToArray()
    };
}

public sealed record SpaceConfigurationRevision(long Revision, SpaceConfigurationDocument Configuration, DateTimeOffset ActivatedAt);

public enum SpaceOrigin { BuiltIn = 0, UserCreated = 1 }

public sealed class SpaceRevisionConflictException(Guid spaceId, long expectedRevision, long actualRevision)
    : InvalidOperationException($"Space '{spaceId}' changed from revision {expectedRevision} to revision {actualRevision}.")
{
    public Guid SpaceId { get; } = spaceId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}

public sealed class SpaceDraftRevisionConflictException(Guid spaceId, long expectedRevision, long actualRevision)
    : InvalidOperationException($"Space '{spaceId}' draft changed from revision {expectedRevision} to {actualRevision}.")
{
    public Guid SpaceId { get; } = spaceId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}

internal sealed record SpaceRegistryState(int Version, IReadOnlyList<SpaceDefinition> Spaces, IReadOnlyList<SpaceDeletionOperation>? Deletions = null);
