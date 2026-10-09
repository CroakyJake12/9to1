using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private bool IsConfigurationProjectTargetCurrent(ProjectSelectionTarget target)
    {
        if (target.ConfigurationSubmission is not { } submitted) return true;
        var draft = Bindings.OriginalDraft;
        return draft is not null && ReferenceEquals(draft, submitted.Owner) && draft.Identity == submitted.Identity &&
            draft.ExpectedRevision == submitted.ExpectedRevision && draft.OriginalGeneration == submitted.Generation;
    }

    private async Task BeginConfigurationProjectPreferenceAsync(object? originalRow, CancellationToken token)
    {
        if (HasUnresolvedProjectCreation)
        { PublishSynchronous(() => Bindings.SetConversationStatus("Review the original unresolved project creation before choosing configuration resources.")); return; }
        var draft = Bindings.OriginalDraft;
        var definition = _controller.Snapshot.SelectedAssistant;
        if (draft is null || definition is null || draft.Identity != definition.Identity || draft.ExpectedRevision != definition.Revision)
        { PublishSynchronous(() => Bindings.SetConversationStatus("Save this Assistant first, then choose its current development project.")); return; }
        // Reviewing a saved preference opens the genuine chooser. The configured
        // reference is never passed to a resource reader as permission or adopted
        // as a source-issued project choice.
        if (originalRow is not null) _ = Bindings.DemandConfiguredProjectPreference(originalRow);
        var submitted = draft.CaptureSubmission();
        var target = new ProjectSelectionTarget(definition.Identity, definition.Revision,
            _controller.Snapshot.ConversationBinding, PresentationGeneration, null, false,
            checked(++_projectSelectionGeneration), ConfigurationSubmission: submitted);
        _projectTarget = target; _projectNextContinuation = null; _projectPublishedSearchRevision = -1;
        PublishSynchronous(() => { Bindings.BeginProjectSelection(false); Bindings.ShowWork(); });
        if (!IsProjectTargetCurrent(target)) return;
        await ReadProjectCatalogueAsync(target, token);
        if (IsProjectTargetCurrent(target)) PublishSynchronous(() => Bindings.SetProjectStatus(
            "Choose a development project through its current canonical source and Home READ. This adds a saved preference; every later Task or Dev operation requires fresh access. Save configuration to persist the choice."));
    }
}
