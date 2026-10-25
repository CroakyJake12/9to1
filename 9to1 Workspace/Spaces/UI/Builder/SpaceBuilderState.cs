using Haven.Application;

namespace HavenOS.Apps.Spaces.Builder;

/// <summary>Alias to the canonical category state so the Builder does not invent a second owner.</summary>
using SpaceCapabilityCategoryState = Haven.Application.SpaceCapabilityCategoryState;

/// <summary>One policy rule for a specific resource inside a Space. Mandatory + Blocked is a
/// validation failure, never a bypass.</summary>
/// <summary>Draft configuration for one canonical Space. Draft and Active are separate revisions
/// of the SAME SpaceID; an invalid draft never displaces the last known-good active revision.</summary>
/// <summary>Opaque in-process choice issued by the actual Home/Notes source. Metadata
/// alone never authorises reading a notebook body or writing its content.</summary>
public interface ISpaceBuilderOriginalBoardsSourceChoice { }
public sealed record SpaceBuilderBoardsSourceOption(string Id, string Label, string Revision,
    [property: System.Text.Json.Serialization.JsonIgnore] ISpaceBuilderOriginalBoardsSourceChoice Input);

public sealed record SpaceBuilderDraft(
    string Name,
    string Description,
    string IconKey,
    string Instructions,
    Haven.Application.SpaceCapabilityCategoryPolicy CategoryPolicy,
    IReadOnlyList<Haven.Application.SpaceCapabilityBinding> CapabilityBindings)
{
    // These are projections of the same canonical Space record. The Builder never
    // clones sources/capabilities or grants access merely by editing these policies.
    public SpaceContextPolicy ContextPolicy { get; init; } = new();
    public SpaceInheritancePolicy Inheritance { get; init; } = new();
    public IReadOnlyList<SpaceContextReference> ContextReferences { get; init; } = [];
    public IReadOnlyList<SpaceFileReference> LegacyFiles { get; init; } = [];
    public SpaceModelPolicy? ModelPolicy { get; init; }
    public SpaceMemoryPolicy? MemoryPolicy { get; init; }
    public IReadOnlyList<SpaceExamplePair> ExamplePairs { get; init; } = [];
    public IReadOnlyList<SpaceShareGrant> Shares { get; init; } = [];
    public SpaceGeneratedSurface? GeneratedSurface { get; init; }
    public SpaceLayoutDocument? LayoutDocument { get; init; }

    public static SpaceBuilderDraft Empty { get; } = new("", "", "", "",
        new Haven.Application.SpaceCapabilityCategoryPolicy(), []);

    internal static SpaceBuilderDraft From(SpaceDefinition space) => From(space,
        space.DraftConfiguration ?? SpaceConfigurationDocument.From(space));

    internal static SpaceBuilderDraft From(SpaceDefinition space, SpaceConfigurationDocument configuration)
    {
        var projected = configuration.ApplyTo(space);
        return
        new(projected.Name, projected.Description, projected.IconKey, projected.Instructions,
            projected.CapabilityCategoryPolicy ?? new Haven.Application.SpaceCapabilityCategoryPolicy(),
            projected.CapabilityBindings?.ToArray() ?? [])
        {
            ContextPolicy = projected.ContextPolicy ?? new SpaceContextPolicy(),
            Inheritance = projected.Inheritance ?? new SpaceInheritancePolicy(),
            ContextReferences = Array.AsReadOnly((projected.ContextReferences ?? []).ToArray()),
            LegacyFiles = Array.AsReadOnly(projected.Files.ToArray()),
            ModelPolicy = projected.ModelPolicy,
            MemoryPolicy = projected.MemoryPolicy,
            ExamplePairs = Array.AsReadOnly((projected.ExamplePairs ?? []).ToArray()),
            Shares = Array.AsReadOnly((projected.Shares ?? []).ToArray()),
            GeneratedSurface = projected.GeneratedSurface,
            LayoutDocument = projected.LayoutDocument
        };
    }
}

public sealed record SpaceBuilderState(
    Guid SpaceId,
    long? DraftRevision,
    long? ActiveRevision,
    SpaceBuilderDraft Draft,
    bool IsLoaded,
    bool IsReadOnly,
    bool IsLoading,
    bool IsSaving,
    bool HasChanges,
    bool HasConflict,
    string StatusMessage)
{
    public long? SpaceRevision { get; init; }
    public long? DraftBaseActiveRevision { get; init; }
    public bool HasPersistedDraft { get; init; }
    public bool HasParentSpace { get; init; }
    public SpaceBuilderEditorView EditorView { get; init; } = SpaceBuilderEditorView.Basic;
    public SpaceBuilderExpertSection ExpertSection { get; init; } = SpaceBuilderExpertSection.Details;
    public SpaceBuilderPreview? Preview { get; init; }
    public IReadOnlyList<string> ValidationIssues { get; init; } = [];
    public bool HasActivationValidator { get; init; }
    public bool IsBusy => IsLoading || IsSaving;
    public bool CanEdit => IsLoaded && !IsReadOnly && !IsLoading;
    public bool CanSave => CanEdit && !IsBusy && HasChanges && !HasConflict;
    public bool CanDiscard => IsLoaded && !IsBusy && (HasChanges || HasConflict);
    public bool CanReload => !IsBusy && !HasChanges;
    public bool HasActiveRevision => ActiveRevision is not null;
    public bool HasUnverifiedDependencies => Draft.CapabilityBindings.Count != 0 ||
        !string.IsNullOrWhiteSpace(Draft.ModelPolicy?.DefaultModelId) ||
        !string.IsNullOrWhiteSpace(Draft.ModelPolicy?.ProviderConstraint) || Draft.ModelPolicy?.RequiredCapabilities?.Count > 0 ||
        HasParentSpace;
    public bool HasUnsupportedActivationDependencies => Draft.LayoutDocument is { Nodes.Count: > 0 } ||
        Draft.Shares.Count != 0 || Draft.GeneratedSurface is not null;
    public bool HasDeferredResourceReferences => Draft.ContextReferences.Count != 0 || Draft.LegacyFiles.Count != 0;
    public bool CanActivate => CanEdit && !IsBusy && !HasChanges && !HasConflict && HasPersistedDraft && DraftRevision is not null &&
        ActiveRevision is not null && DraftBaseActiveRevision == ActiveRevision && !HasUnsupportedActivationDependencies &&
        (!HasUnverifiedDependencies || HasActivationValidator);
    public bool HasExpertConfiguration => Draft.LayoutDocument is { } layout &&
        (layout.Nodes.Count != 0 || layout.Edges.Count != 0 || layout.Groups.Count != 0 || layout.Comments.Count != 0);
}

public sealed record SpaceBuilderPreview(Guid SpaceId, long DraftRevision, string Name, string Description,
    string Instructions, int CapabilityCount, int GraphNodeCount, int GraphConnectionCount,
    string Notice = "Configuration preview only; no conversation, model, app, tool or external action was run.");

public enum SpaceBuilderEditorView { Basic, Advanced, Expert }
public enum SpaceBuilderExpertSection { Details, HarnessLogic, ModelLogic, UserMessageLogic, Files }
