using System.Collections;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

// Local presentation/correlation controls only. Internal fixture outcomes issue no
// Den checkpoint, Home READ/WRITE, SQL receipt, project permission or Task authority.
public sealed class OriginalCompatibleProjectSubmissionTests
{
    [Fact]
    public async Task Only_same_completed_durable_pending_outcome_settles_barrier_without_ready_Task()
    {
        var definition = Definition(); var submission = new OriginalProjectTaskSubmission(Choice(), definition.Identity, 1, "Project task");
        var checkpoint = Checkpoint(submission, definition);
        var actual = new AssistantCompatibleConversationCreationOutcome(new object(), null, checkpoint);
        var command = new TaskCompletionSource<AssistantCompatibleConversationCreationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        submission.EnterOriginalFactory(); submission.CaptureOriginalCompatibleCommand(command.Task);
        Assert.True(submission.HasUnresolvedAttempt); Assert.False(submission.CanRetry);
        Assert.Throws<InvalidOperationException>(() => submission.AcknowledgeCompatible(actual));
        command.SetResult(actual); await command.Task;
        Assert.Throws<InvalidOperationException>(() => submission.AcknowledgeCompatible(new(new object(), null, checkpoint)));
        Assert.True(submission.HasUnresolvedAttempt);
        submission.AcknowledgeCompatible(actual);
        Assert.Same(command.Task, submission.OriginalCompatibleCommand); Assert.Same(checkpoint, submission.DeclinedCheckpoint);
        Assert.False(submission.IsAcknowledged); Assert.False(submission.HasUnresolvedAttempt); Assert.False(submission.CanSubmit);
        Assert.Null(actual.Binding); Assert.Throws<InvalidOperationException>(submission.EnterOriginalFactory);
    }

    [Fact]
    public async Task Resume_preserves_allocated_ids_and_unknown_raw_fault_stays_unresolved()
    {
        var definition = Definition(); var first = new OriginalProjectTaskSubmission(Choice(), definition.Identity, 1, "Project task");
        var checkpoint = Checkpoint(first, definition);
        var resume = OriginalProjectTaskSubmission.Resume(checkpoint, first.OriginalChoice);
        Assert.Same(checkpoint, resume.ResumeCheckpoint); Assert.Same(first.OriginalChoice, resume.OriginalChoice);
        Assert.Equal(first.ConversationId, resume.ConversationId); Assert.Equal(first.OperationId, resume.OperationId);
        Assert.NotEqual(checkpoint.LastCommandOperationId, resume.CommandOperationId);
        var commandOperation = resume.CommandOperationId;
        var raw = new TaskCompletionSource<AssistantCompatibleConversationCreationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        resume.EnterOriginalFactory(); resume.CaptureOriginalCompatibleCommand(raw.Task);
        var canceledCause = new OperationCanceledException("Faulted raw source is not a durable checkpoint.");
        var sibling = new IOException("Actual unresolved source sibling."); raw.SetException([canceledCause, sibling]);
        try { await raw.Task; } catch (OperationCanceledException actual) { Assert.Same(canceledCause, actual); }
        Assert.True(raw.Task.IsFaulted); Assert.Contains(raw.Task.Exception!.InnerExceptions, cause => ReferenceEquals(cause, sibling));
        Assert.True(resume.HasUnresolvedAttempt); Assert.Null(resume.DeclinedCheckpoint); Assert.False(resume.IsAcknowledged);
        resume.EnterOriginalFactory();
        Assert.Equal(commandOperation, resume.CommandOperationId); Assert.Equal(first.OperationId, resume.OperationId);
        Assert.Equal(first.ConversationId, resume.ConversationId); Assert.Same(checkpoint, resume.ResumeCheckpoint);
    }

    [Fact]
    public void Foreign_allocated_tuple_cannot_settle_a_completed_resume_attempt()
    {
        var definition = Definition(); var first = new OriginalProjectTaskSubmission(Choice(), definition.Identity, 1, "Project task");
        var checkpoint = Checkpoint(first, definition);
        var resume = OriginalProjectTaskSubmission.Resume(checkpoint, first.OriginalChoice);
        var foreign = Checkpoint(resume, definition, checkpoint.MembershipRevision + 2);
        var actual = new AssistantCompatibleConversationCreationOutcome(new object(), null, foreign);
        resume.EnterOriginalFactory(); resume.CaptureOriginalCompatibleCommand(Task.FromResult(actual));
        Assert.Throws<InvalidOperationException>(() => resume.AcknowledgeCompatible(actual));
        Assert.True(resume.HasUnresolvedAttempt); Assert.Null(resume.DeclinedCheckpoint);
    }

    [Fact]
    public void Pending_row_requires_current_identity_and_revision_and_unknown_attempt_blocks_resume_action()
    {
        var definition = Definition(); var submission = new OriginalProjectTaskSubmission(Choice(), definition.Identity, 1, "Project task");
        var checkpoint = Checkpoint(submission, definition); var bindings = new AssistantsCuiBindings((_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        var snapshot = AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition], DeclinedPendingConversations = [checkpoint] };
        bindings.ApplySnapshot(snapshot); Assert.True(bindings.IsActionAvailable("assistants.project.pending.resume"));
        Assert.True(bindings.TryGetValue("CompatiblePendingCreations", out var rows));
        var row = Assert.IsAssignableFrom<IEnumerable>(rows).Cast<object>().Single();
        Assert.Same(checkpoint, bindings.DemandCompatiblePending(row));
        bindings.SetProjectSubmission(true, false, "Unknown original"); Assert.False(bindings.IsActionAvailable("assistants.project.pending.resume"));
        bindings.ApplySnapshot(snapshot with { Revision = 2, SelectedAssistant = definition with { Revision = 2 } });
        Assert.Throws<InvalidOperationException>(() => bindings.DemandCompatiblePending(row));
        Assert.False(bindings.IsActionAvailable("assistants.project.pending.resume"));
    }

    private static AssistantDefinitionSnapshot Definition() => new(new("fixture-den", "personal", "fixture-assistant"), 1,
        ConfiguredIdentityKind.Assistant, new() { Name = "Fixture Assistant" }, []);
    private static AssistantOriginalProjectChoice Choice()
    {
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("fixture-project"));
        var project = new DeveloperProject(Guid.NewGuid(), "fixture", "Fixture project", [root.RootId], "C#", null, "dotnet", [], [], [], [], null);
        var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project] };
        var reference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision, project.ProjectId, 1, root.RootId);
        return new(new object(), new object(), new(reference, workspace, project, root, null));
    }
    private static AssistantCompatibleConversationCheckpoint Checkpoint(OriginalProjectTaskSubmission submission,
        AssistantDefinitionSnapshot definition, long revision = 2)
    {
        var now = DateTimeOffset.UtcNow;
        var container = new ContainerDefinition(Guid.NewGuid(), HavenMode.Tasks, "Fixture allocated task", null,
            JsonSerializer.Serialize(submission.OriginalChoice.Project.Reference), "", now, now);
        var conversation = new Conversation(submission.ConversationId, HavenMode.Tasks, ConversationKind.Task,
            submission.Title, container.Id, null, false, false, now, now);
        return new(new object(), definition.Identity, definition.Revision, "fixture-session", revision,
            submission.ConversationId, submission.OperationId, submission.CommandOperationId, submission.Title,
            submission.OriginalChoice.Project.Reference, Guid.NewGuid(), Guid.NewGuid(), new(1, Guid.NewGuid(), now, false),
            conversation, container, "Fixture-only acknowledged pending state; actual source proof belongs to genuine Den/SQL controls.");
    }
}
