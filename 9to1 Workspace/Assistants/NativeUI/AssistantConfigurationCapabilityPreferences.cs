using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Local metadata/form correlation only. The canonical source validates
/// each selected row; saved preferences grant no execution or resource access.</summary>
internal sealed class AssistantConfigurationCapabilityPreferences
{
    private AssistantConfigurationDraft.Submission? _submission;
    private AssistantOriginalConfigurationCapabilityCatalogue? _catalogue;
    internal IReadOnlyList<Row> Rows { get; private set; } = [];
    internal bool Busy { get; set; }
    internal string Detail { get; set; } = "Save the Assistant, then review its current Tools and connected Apps.";

    internal void Begin(AssistantConfigurationDraft.Submission submitted)
    {
        _submission = submitted; _catalogue = null; Rows = [];
        Detail = "Reading the current capability catalogue through its protected source…";
    }

    internal static bool IsCurrent(AssistantConfigurationDraft? draft, AssistantConfigurationDraft.Submission submitted) =>
        draft is not null && ReferenceEquals(draft, submitted.Owner) && draft.Identity == submitted.Identity &&
        draft.ExpectedRevision == submitted.ExpectedRevision && draft.OriginalGeneration == submitted.Generation;

    internal bool PublishCatalogue(AssistantConfigurationDraft? draft, AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalConfigurationCapabilityCatalogue catalogue)
    {
        if (!ReferenceEquals(submitted, _submission) || !IsCurrent(draft, submitted) ||
            catalogue.Definition.Identity != submitted.Identity || catalogue.Definition.Revision != submitted.ExpectedRevision)
            return false;
        _catalogue = catalogue;
        Rows = catalogue.State == CapabilityOriginalCatalogueState.Available
            ? catalogue.Definitions.Select((actual, index) => new Row(catalogue, actual, index)).ToArray() : [];
        Detail = catalogue.Detail;
        return true;
    }

    internal bool HasCurrentRows(AssistantConfigurationDraft? draft) => _submission is { } submitted &&
        IsCurrent(draft, submitted) && _catalogue is not null;

    internal bool CanSelect(AssistantConfigurationDraft? draft) => !Busy && _submission is { } submitted &&
        IsCurrent(draft, submitted) && _catalogue?.State == CapabilityOriginalCatalogueState.Available && Rows.Any(row => row.CanChoose);

    internal Row? FindCurrentRow(AssistantConfigurationDraft? draft, object? parameter) =>
        CanSelect(draft) && parameter is Row row && row.CanChoose && Rows.Any(actual => ReferenceEquals(actual, row)) ? row : null;

    internal bool ApplyChoice(AssistantConfigurationDraft? draft, AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalConfigurationCapabilityChoice choice)
    {
        if (!ReferenceEquals(submitted, _submission) || !IsCurrent(draft, submitted) ||
            !ReferenceEquals(choice.Catalogue, _catalogue) ||
            !Rows.Any(row => row.CanChoose && ReferenceEquals(row.Original, choice.Selected))) return false;
        var preferences = choice.Selected.IsBuiltIn ? draft!.Configuration.ToolIds : draft!.Configuration.ConnectedAppIds;
        if (!preferences.Contains(choice.PreferenceId, StringComparer.OrdinalIgnoreCase))
        {
            var ids = preferences.Append(choice.PreferenceId).ToArray();
            draft.SetConfiguration(choice.Selected.IsBuiltIn
                ? draft.Configuration with { ToolIds = ids }
                : draft.Configuration with { ConnectedAppIds = ids });
        }
        Rows = []; _catalogue = null;
        Detail = "The exact observed preference is in this draft. Save configuration to keep it, or review the catalogue to choose another.";
        return true;
    }

    internal sealed class Row(AssistantOriginalConfigurationCapabilityCatalogue sameCatalogue, CapabilityDefinition sameOriginal, int rowId)
    {
        internal AssistantOriginalConfigurationCapabilityCatalogue Catalogue { get; } = sameCatalogue;
        internal CapabilityDefinition Original { get; } = sameOriginal;
        public bool CanChoose => Original.IsEnabled && Original.IsAgentUsable &&
            Original.Availability is not CapabilityAvailability.Unsupported and not CapabilityAvailability.Restricted;
        public int Id { get; } = rowId;
        public string Name => Original.Name;
        public string Description => Original.Description;
        public string AvailabilityLabel => (Original.IsBuiltIn ? "Tool" : "Connected App capability") + ": " +
            Original.Availability + ". This choice is a saved preference; access and execution are checked at use.";
    }
}
