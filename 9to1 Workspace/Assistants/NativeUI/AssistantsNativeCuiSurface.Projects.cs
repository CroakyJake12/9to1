using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private readonly IAssistantsNativeOriginalDevelopmentRoute? _developmentRoute;
    private ProjectSelectionTarget? _projectTarget;
    private long _projectSelectionGeneration;
    private long _projectCatalogueRequestGeneration;
    private AssistantOriginalProjectCatalogueContinuation? _projectNextContinuation;
    private long _projectPublishedSearchRevision = -1;
    private AssistantOriginalProjectChoice? _originalProjectChoice;
    private OriginalProjectTaskSubmission? _projectSubmission;
    private ProjectSelectionTarget? _projectSubmissionTarget;
    private bool HasUnresolvedProjectCreation => _projectSubmission is { HasUnresolvedAttempt: true };
    public IAssistantsNativeOriginalDevelopmentRoute? OriginalDevelopmentRoute => _developmentRoute;
    public AssistantOriginalProjectChoice? OriginalProjectChoice => IsRetiring ? null : _originalProjectChoice;

    private sealed record ProjectSelectionTarget(AssistantIdentity Identity, long DefinitionRevision,
        AssistantConversationBinding? Conversation, long PresentationGeneration, ProviderExecutionContext? CanonicalTask,
        bool OpenDevelopment, long SelectionGeneration, AssistantCompatibleConversationCheckpoint? ResumeCheckpoint = null,
        AssistantConfigurationDraft.Submission? ConfigurationSubmission = null);

    private bool IsProjectTargetCurrent(ProjectSelectionTarget target)
    {
        var current = _controller.Snapshot;
        return !IsRetiring && ReferenceEquals(_projectTarget, target) && target.SelectionGeneration == _projectSelectionGeneration &&
            current.SelectedAssistant?.Identity == target.Identity && current.SelectedAssistant.Revision == target.DefinitionRevision &&
            ReferenceEquals(current.ConversationBinding, target.Conversation) && ReferenceEquals(_binding, target.Conversation) &&
            PresentationGeneration == target.PresentationGeneration && IsConfigurationProjectTargetCurrent(target) &&
            (!target.OpenDevelopment || current.Work?.Controls?.CanonicalTaskContext == target.CanonicalTask);
    }

    private async Task BeginProjectSelectionAsync(bool development, CancellationToken token,
        AssistantCompatibleConversationCheckpoint? resumeCheckpoint = null)
    {
        if (HasUnresolvedProjectCreation)
        {
            PublishSynchronous(() =>
            {
                Bindings.BeginProjectSelection(false);
                PublishProjectSubmissionState();
                Bindings.SetProjectStatus("The earlier project Task creation is unresolved. Retry its same choice and operation. An unresolved result requires its original owner review; a new choice will not replace it.");
            });
            return;
        }
        if (!await PrepareNavigationAsync(token)) return;
        if (development) await SourceAsync(() => _controller.RefreshWorkAsync(token)); // Read the actual latest acknowledged Task, never a cached claim.
        var definition = DemandDefinition();
        if (resumeCheckpoint is not null && (development || resumeCheckpoint.Identity != definition.Identity ||
            resumeCheckpoint.DefinitionRevision != definition.Revision))
        { PublishSynchronous(() => Bindings.SetConversationStatus("Read the current Assistant's pending checkpoint before resuming.")); return; }
        var binding = _controller.Snapshot.ConversationBinding;
        var task = development ? DemandTask() : null;
        var target = new ProjectSelectionTarget(definition.Identity, definition.Revision, binding,
            PresentationGeneration, task, development, checked(++_projectSelectionGeneration), resumeCheckpoint);
        _projectTarget = target; _projectNextContinuation = null; _projectPublishedSearchRevision = -1;
        PublishSynchronous(() => Bindings.BeginProjectSelection(development));
        if (!IsProjectTargetCurrent(target))
        { PublishSynchronous(() => Bindings.SetProjectStatus("The Assistant changed. Return to its current conversation before choosing a project.")); return; }
        await ReadProjectCatalogueAsync(target, token);
        if (resumeCheckpoint is not null && IsProjectTargetCurrent(target)) PublishSynchronous(() =>
            Bindings.SetProjectStatus("Resuming saved creation: " + resumeCheckpoint.Title + ". Select the same source project for fresh Home READ and individual WRITE approval. Its original creation IDs are preserved."));
    }

    private async Task ReadProjectCatalogueAsync(ProjectSelectionTarget target, CancellationToken token, bool older = false)
    {
        var search = Bindings.OriginalProjectSearch; var searchRevision = Bindings.ProjectSearchRevision;
        var continuation = older && _projectPublishedSearchRevision == searchRevision ? _projectNextContinuation : null;
        if (older && continuation is null) return;
        var request = checked(++_projectCatalogueRequestGeneration);
        bool CanPublish() => IsProjectTargetCurrent(target) && request == _projectCatalogueRequestGeneration &&
            Bindings.ProjectSearchRevision == searchRevision && Bindings.OriginalProjectSearch == search;
        PublishSynchronous(() => Bindings.SetProjectBusy(true));
        try
        {
            var actual = await SourceAsync(() => _controller.ReadOriginalProjectCandidatesPageAsync(32, continuation, search, token));
            if (CanPublish()) PublishSynchronous(() =>
            {
                if (!CanPublish()) return;
                _projectNextContinuation = actual.NextContinuation; _projectPublishedSearchRevision = searchRevision;
                Bindings.SetProjectCatalogue(actual, searchRevision);
            });
        }
        finally { if (IsProjectTargetCurrent(target) && request == _projectCatalogueRequestGeneration) PublishSynchronous(() =>
        { if (IsProjectTargetCurrent(target) && request == _projectCatalogueRequestGeneration) Bindings.SetProjectBusy(false); }); }
    }

    private async Task ChooseOriginalProjectAsync(object? parameter, CancellationToken token)
    {
        if (HasUnresolvedProjectCreation)
        { PublishSynchronous(() => Bindings.SetProjectStatus("An earlier project Task creation remains unresolved. Use its explicit retry; choosing another project does not replace it.")); return; }
        var target = _projectTarget ?? throw new InvalidOperationException("Open the original project chooser first.");
        if (!IsProjectTargetCurrent(target))
        { PublishSynchronous(() => Bindings.SetProjectStatus("This project review belongs to an older Assistant selection. Close it and choose again.")); return; }
        var candidate = Bindings.DemandOriginalProjectCandidate(parameter); // SAME issued row, no ID/path reconstruction.
        PublishSynchronous(() => Bindings.SetProjectBusy(true));
        try
        {
            var choice = await SourceAsync(() => _controller.AuthorizeOriginalProjectChoiceAsync(candidate, token));
            if (HasUnresolvedProjectCreation)
            { if (!IsRetiring) PublishSynchronous(() => Bindings.SetProjectStatus("Another accepted project creation is unresolved. Its original choice and operation are preserved.")); return; }
            _originalProjectChoice = choice; // Retain the SAME owner-issued READ choice through actual creation/handoff.
            if (!IsProjectTargetCurrent(target))
            { if (!IsRetiring) PublishSynchronous(() => Bindings.SetProjectStatus("The Assistant changed while access was checked. Choose again in its current conversation.")); return; }
            if (target.ConfigurationSubmission is { } configuration)
            {
                if (!IsProjectTargetCurrent(target)) return;
                PublishSynchronous(() =>
                {
                    if (!IsProjectTargetCurrent(target)) return;
                    Bindings.ApplyOriginalProjectPreference(configuration, choice);
                    Bindings.ReturnToConfiguration(); Bindings.CloseProjectSelection();
                });
            }
            else if (target.OpenDevelopment)
            {
                var route = _developmentRoute;
                if (route is null || target.Conversation is null || target.CanonicalTask is null)
                { PublishSynchronous(() => Bindings.SetProjectStatus("The genuine native Dev route is unavailable here.")); return; }
                var binding = await SourceAsync(() => _controller.OpenDevelopmentOriginalAsync(
                    choice.Project.Reference, target.CanonicalTask, token));
                if (!IsProjectTargetCurrent(target) || !ReferenceEquals(binding.Conversation, target.Conversation) ||
                    binding.CanonicalTask != target.CanonicalTask || binding.Project.Reference != choice.Project.Reference)
                { if (!IsRetiring) PublishSynchronous(() => Bindings.SetProjectStatus("The current Task changed before Dev opened. Review its project again.")); return; }
                await SourceAsync(() => route.OpenOriginalAsync(this, binding, target.PresentationGeneration, token));
                if (IsProjectTargetCurrent(target)) PublishSynchronous(Bindings.CloseProjectSelection);
            }
            else
            {
                // The source creates the compatible canonical Tasks context for the SAME
                // authorized project; starting work later uses the existing Task producer.
                _projectSubmission = target.ResumeCheckpoint is { } checkpoint
                    ? OriginalProjectTaskSubmission.Resume(checkpoint, choice)
                    : new(choice, target.Identity, target.DefinitionRevision, "Project task");
                _projectSubmissionTarget = target; // Retain BEFORE entering the accepted canonical producer.
                PublishSynchronous(PublishProjectSubmissionState);
                await SubmitOriginalProjectTaskAsync(_projectSubmission, target, token);
            }
        }
        finally { if (!IsRetiring) PublishSynchronous(() => Bindings.SetProjectBusy(false)); }
    }

    private async Task RetryOriginalProjectTaskAsync(CancellationToken token)
    {
        var submission = _projectSubmission ?? throw new InvalidOperationException("There is no retained project Task creation to retry.");
        var target = _projectSubmissionTarget ?? throw new InvalidOperationException("The original project creation target is unavailable.");
        await SubmitOriginalProjectTaskAsync(submission, target, token);
    }

    private async Task SubmitOriginalProjectTaskAsync(OriginalProjectTaskSubmission submission,
        ProjectSelectionTarget target, CancellationToken token)
    {
        if (!IsProjectSubmissionCurrent(submission) || !submission.CanSubmit)
        { PublishSynchronous(() => Bindings.SetProjectStatus("Return to the same Assistant configuration, and join the original attempt before retrying. Its pending operation is preserved.")); return; }
        PublishSynchronous(() => { Bindings.SetProjectBusy(true); PublishProjectSubmissionState(); });
        try
        {
            var actual = await SourceAsync(() =>
            {
                submission.EnterOriginalFactory();
                var command = submission.ResumeCheckpoint is { } checkpoint
                    ? _controller.ResumeCompatibleConversationOriginalAsync(checkpoint, submission.CommandOperationId,
                        submission.OriginalChoice, token)
                    : _controller.CreateCompatibleConversationOriginalAsync(submission.OriginalChoice,
                        submission.ConversationId, submission.Title, submission.OperationId, submission.CommandOperationId, token);
                submission.CaptureOriginalCompatibleCommand(command); return command;
            });
            submission.AcknowledgeCompatible(actual); // SAME completed source outcome even if presentation moved meanwhile.
            if (!IsRetiring) PublishSynchronous(() =>
            {
                PublishProjectSubmissionState();
                if (IsProjectSubmissionCurrent(submission))
                {
                    ApplySnapshot(_controller.Snapshot); Bindings.CloseProjectSelection(); Bindings.ShowWork();
                    if (submission.DeclinedCheckpoint is not null)
                        Bindings.SetConversationStatus("Project creation is paused and saved. You can close now. Resume the saved pending creation with fresh Home access and individual WRITE approval.");
                }
            });
        }
        finally
        {
            if (!IsRetiring) PublishSynchronous(() =>
            {
                Bindings.SetProjectBusy(false); PublishProjectSubmissionState();
                if (submission.IsOriginalPreEffectRefused)
                    Bindings.SetProjectStatus("The canonical owner declined this operation before effects. Its same choice and IDs are retained for explicit retry, or choose another project.");
                else if (submission.DeclinedCheckpoint is not null)
                    Bindings.SetProjectStatus("The canonical owner acknowledged a saved pending creation. No Task is ready; resume the same saved operation with fresh authorization.");
                else if (!submission.IsAcknowledged)
                    Bindings.SetProjectStatus("The project Task creation has no completed canonical receipt. Its same project and operation are retained; retry checks the original state and does not replay unknown writes.");
            });
        }
    }

    private async Task ReadCompatiblePendingAsync(CancellationToken token)
    {
        if (HasUnresolvedProjectCreation)
        { PublishSynchronous(() => Bindings.SetConversationStatus("Join and review the earlier unresolved creation before loading another pending operation.")); return; }
        var definition = DemandDefinition();
        var actual = await SourceAsync(() => _controller.ReadOriginalDeclinedPendingAsync(32, token));
        var selected = _controller.Snapshot.SelectedAssistant;
        if (!IsRetiring && selected?.Identity == definition.Identity && selected.Revision == definition.Revision)
            PublishSynchronous(() =>
            {
                var current = _controller.Snapshot.SelectedAssistant;
                if (current?.Identity != definition.Identity || current.Revision != definition.Revision) return;
                Bindings.SetCompatiblePending(actual); Bindings.ShowWork();
            });
    }

    private async Task BeginCompatibleResumeAsync(object? parameter, CancellationToken token)
    {
        if (HasUnresolvedProjectCreation)
        { PublishSynchronous(() => Bindings.SetConversationStatus("The earlier creation is unresolved; its original receipt must settle before another attempt.")); return; }
        var checkpoint = Bindings.DemandCompatiblePending(parameter); // SAME source row, never a reconstructed ID/path.
        await BeginProjectSelectionAsync(false, token, checkpoint);
    }

    private bool IsProjectSubmissionCurrent(OriginalProjectTaskSubmission actual) => !IsRetiring &&
        ReferenceEquals(_projectSubmission, actual) &&
        _controller.Snapshot.SelectedAssistant?.Identity == actual.Identity &&
        _controller.Snapshot.SelectedAssistant.Revision == actual.DefinitionRevision;

    private void PublishProjectSubmissionState()
    {
        var pending = _projectSubmission is { IsAcknowledged: false } actual ? actual : null;
        Bindings.SetProjectSubmission(pending is { HasUnresolvedAttempt: true },
            pending is { CanRetry: true } && IsProjectSubmissionCurrent(pending),
            pending?.Title ?? "", settledPreEffectRefusal: pending is { IsOriginalPreEffectRefused: true });
    }

    private void CancelOriginalProjectSelection() => PublishSynchronous(() =>
    {
        var returnToConfiguration = _projectTarget?.ConfigurationSubmission is not null;
        if (!HasUnresolvedProjectCreation)
        {
            _projectSelectionGeneration = checked(_projectSelectionGeneration + 1);
            _projectTarget = null; _originalProjectChoice = null; _projectNextContinuation = null;
            _projectCatalogueRequestGeneration = checked(_projectCatalogueRequestGeneration + 1);
        }
        if (returnToConfiguration) Bindings.ReturnToConfiguration();
        Bindings.CloseProjectSelection(); // Hiding the chooser never abandons an accepted pending creation.
    }); // Cancel has no create/authorize/Dev command.
}
