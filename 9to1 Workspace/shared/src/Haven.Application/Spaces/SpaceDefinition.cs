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
    ConnectedEntity = 14
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
    DateTimeOffset AddedAt);

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
    SpaceInheritancePolicy? Inheritance = null,
    IReadOnlyList<SpaceContextReference>? ContextReferences = null,
    IReadOnlyList<SpaceShareGrant>? Shares = null)
{
    /// <summary>Origin is derived from the protected registry bit and is not independently mutable.</summary>
    public SpaceOrigin Origin => IsBuiltIn ? SpaceOrigin.BuiltIn : SpaceOrigin.UserCreated;
}

public enum SpaceOrigin { BuiltIn = 0, UserCreated = 1 }

internal sealed record SpaceRegistryState(int Version, IReadOnlyList<SpaceDefinition> Spaces);
