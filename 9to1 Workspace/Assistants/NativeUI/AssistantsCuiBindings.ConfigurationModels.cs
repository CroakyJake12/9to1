using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private AssistantConfigurationDraft.Submission? _configurationModelSubmission;
    private AssistantOriginalConfigurationModelCatalogue? _configurationModelCatalogue;
    private IReadOnlyList<ConfigurationModelRow> _configurationModelRows = [];
    private bool _configurationModelsBusy;
    private bool _hasConfigurationModelSource;
    private string _configurationModelDetail = "";

    private bool CanReadConfigurationModels => _hasConfigurationModelSource && _draft?.Identity is not null && !_draft.IsDirty && !Busy;
    private bool HasCurrentConfigurationModels => _configurationModelSubmission is { } submitted &&
        AssistantConfigurationCapabilityPreferences.IsCurrent(_draft, submitted) && _configurationModelCatalogue is not null;
    private bool CanChooseConfigurationModel => !Busy && HasCurrentConfigurationModels && _configurationModelRows.Count != 0;

    internal void SetConfigurationModelSource(bool present) { _hasConfigurationModelSource = present; Refresh(); }

    internal void BeginConfigurationModelRead(AssistantConfigurationDraft.Submission submitted)
    {
        _configurationModelSubmission = submitted; _configurationModelCatalogue = null; _configurationModelRows = [];
        _configurationModelDetail = "Checking models for your saved preferences…"; Refresh();
    }
    internal void SetConfigurationModelsBusy(bool busy) { _configurationModelsBusy = busy; Refresh(); }
    internal void PublishConfigurationModels(AssistantConfigurationDraft.Submission submitted, AssistantOriginalConfigurationModelCatalogue catalogue)
    {
        if (!ReferenceEquals(_configurationModelSubmission, submitted) ||
            !AssistantConfigurationCapabilityPreferences.IsCurrent(_draft, submitted) ||
            catalogue.Definition.Identity != submitted.Identity || catalogue.Definition.Revision != submitted.ExpectedRevision) return;
        _configurationModelCatalogue = catalogue;
        _configurationModelRows = Array.AsReadOnly(catalogue.Models.Select((model, index) => new ConfigurationModelRow(catalogue, model, index)).ToArray());
        _configurationModelDetail = catalogue.Detail; Refresh();
    }
    internal ConfigurationModelRow? CurrentConfigurationModelRow(object? value) => HasCurrentConfigurationModels && value is ConfigurationModelRow row &&
        _configurationModelRows.Any(actual => ReferenceEquals(actual, row)) ? row : null;
    internal void ApplyConfigurationModel(AssistantConfigurationDraft.Submission submitted, ConfigurationModelRow row)
    {
        if (!ReferenceEquals(submitted, _configurationModelSubmission) || CurrentConfigurationModelRow(row) is null ||
            !AssistantConfigurationCapabilityPreferences.IsCurrent(_draft, submitted)) return;
        var draft = _draft!;
        draft.SetConfiguration(draft.Configuration with { Model = draft.Configuration.Model with
        { ProviderId = row.Original.ProviderId, ModelId = row.Original.Model.Name } });
        _configurationModelCatalogue = null; _configurationModelRows = [];
        _configurationModelDetail = "Preferred model selected. Save configuration to keep it."; Refresh();
    }
    private void RefreshConfigurationModels(bool usable)
    {
        Set("ConfigurationModels", HasCurrentConfigurationModels ? _configurationModelRows : []);
        Set("CanReadConfigurationModels", usable && CanReadConfigurationModels);
        Set("CanChooseConfiguredModel", usable && CanChooseConfigurationModel);
        Set("ConfigurationModelStatus", _draft?.Identity is null
            ? "Save your Assistant first, then choose from models available under its saved preferences."
            : !_hasConfigurationModelSource ? "Model discovery is not configured on this host. Your saved preferences are preserved."
            : _draft.IsDirty ? "Save your changes to check models under the updated preferences."
            : HasCurrentConfigurationModels || _configurationModelsBusy ? _configurationModelDetail
            : "Refresh models to choose a preferred model for this saved Assistant. No conversation is needed.");
    }

    public sealed class ConfigurationModelRow
    {
        internal ConfigurationModelRow(AssistantOriginalConfigurationModelCatalogue catalogue, AssistantModelChoice original, int id)
        { Catalogue = catalogue; Original = original; Id = id; }
        internal AssistantOriginalConfigurationModelCatalogue Catalogue { get; }
        internal AssistantModelChoice Original { get; }
        public int Id { get; }
        public string Label => Original.Model.Name + " · " + Original.ProviderId;
    }
}
