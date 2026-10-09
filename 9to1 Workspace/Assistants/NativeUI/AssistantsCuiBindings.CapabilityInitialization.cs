using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private bool _configurationInitializationConfigured, _configurationInitializationBusy;
    private AssistantConfigurationDraft.Submission? _configurationInitializationSubmission;
    private AssistantOriginalCapabilityInitializationIntent? _configurationInitializationIntent;
    private string _configurationInitializationDetail = "Individual Home setup is unavailable on this host.";

    internal void ConfigureOriginalCapabilityInitializationSource(bool present)
    {
        _configurationInitializationConfigured = present;
        _configurationInitializationDetail = present
            ? "Save this Assistant, then review which built-in Tools need setup."
            : "Individual Home setup is unavailable on this host.";
        Refresh();
    }
    private bool CanReviewOriginalCapabilityInitialization() => _configurationInitializationConfigured &&
        CanChooseOriginalConfigurationProject() && _draft?.IsDirty == false;
    private bool CanStartOriginalCapabilityInitialization() => CanReviewOriginalCapabilityInitialization() &&
        _configurationInitializationSubmission is { } submitted &&
        AssistantConfigurationCapabilityPreferences.IsCurrent(_draft, submitted) &&
        _configurationInitializationIntent is { MissingDefinitions.Count: > 0 } intent &&
        intent.Definition.Identity == submitted.Identity && intent.Definition.Revision == submitted.ExpectedRevision;

    internal void BeginOriginalCapabilityInitializationReview(AssistantConfigurationDraft.Submission submitted)
    {
        _configurationInitializationSubmission = submitted; _configurationInitializationIntent = null;
        _configurationInitializationDetail = "Reviewing the current built-in Tools setup…";
        Refresh();
    }
    internal bool PublishOriginalCapabilityInitializationIntent(AssistantConfigurationDraft.Submission submitted,
        AssistantOriginalCapabilityInitializationIntent intent)
    {
        if (!ReferenceEquals(submitted, _configurationInitializationSubmission) ||
            !AssistantConfigurationCapabilityPreferences.IsCurrent(_draft, submitted) ||
            intent.Definition.Identity != submitted.Identity || intent.Definition.Revision != submitted.ExpectedRevision) return false;
        _configurationInitializationIntent = intent;
        _configurationInitializationDetail = intent.MissingDefinitions.Count == 0
            ? "The built-in Tools are already set up. Review the current Tools and Apps to choose preferences."
            : $"{intent.MissingDefinitions.Count} built-in Tools need setup. Request their individual approval in Home.";
        Refresh(); return true;
    }
    internal void SetOriginalCapabilityInitializationBusy(bool busy)
    { _configurationInitializationBusy = busy; Refresh(); }
    internal void SetOriginalCapabilityInitializationStatus(string detail)
    { _configurationInitializationDetail = detail; Refresh(); }
    private void RefreshConfigurationInitialization()
    {
        Set("CapabilityInitializationStatus", _configurationInitializationDetail);
        Set("CanReviewCapabilityInitialization", IsActionAvailable("assistants.configuration.capabilities.setup.review") == true);
        Set("CanStartCapabilityInitialization", IsActionAvailable("assistants.configuration.capabilities.setup.start") == true);
    }
}
