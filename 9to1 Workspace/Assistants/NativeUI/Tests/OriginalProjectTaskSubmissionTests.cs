using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

/// <summary>Local correlation/custody controls. Fixture-issued choices are not Home,
/// project, Task, installation or native runtime acceptance evidence.</summary>
public sealed class OriginalProjectTaskSubmissionTests
{
    [Fact]
    public async Task Failed_original_keeps_the_same_choice_and_ids_for_the_explicit_retry()
    {
        var choice = Choice(); var definition = Definition();
        var submission = new OriginalProjectTaskSubmission(choice, definition.Identity, definition.Revision, "Project task");
        var conversationId = submission.ConversationId; var operationId = submission.OperationId;
        var first = new TaskCompletionSource<AssistantsWorkspaceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        submission.EnterOriginalFactory(); submission.CaptureOriginalCommand(first.Task);
        Assert.False(submission.CanRetry);
        var actualFault = new IOException("Held source outcome requires inspection."); first.SetException(actualFault);
        Assert.Same(actualFault, await Assert.ThrowsAsync<IOException>(() => first.Task));
        Assert.True(submission.CanRetry); Assert.True(submission.HasUnresolvedAttempt);
        submission.EnterOriginalFactory();
        var actual = Receipt(submission, definition); var retry = Task.FromResult(actual);
        submission.CaptureOriginalCommand(retry); submission.Acknowledge(actual);
        Assert.Same(choice, submission.OriginalChoice); Assert.Same(retry, submission.OriginalCommand);
        Assert.Equal(conversationId, submission.ConversationId); Assert.Equal(operationId, submission.OperationId);
        Assert.True(submission.IsAcknowledged); Assert.False(submission.CanRetry); Assert.False(submission.HasUnresolvedAttempt);
    }

    [Fact]
    public void A_pending_command_or_factory_without_an_actual_task_cannot_be_replaced()
    {
        var definition = Definition(); var submission = new OriginalProjectTaskSubmission(Choice(), definition.Identity, definition.Revision, "Project task");
        submission.EnterOriginalFactory();
        Assert.False(submission.CanSubmit); Assert.False(submission.CanRetry);
        Assert.Throws<InvalidOperationException>(submission.EnterOriginalFactory);
        var raw = new TaskCompletionSource<AssistantsWorkspaceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        submission.CaptureOriginalCommand(raw.Task);
        Assert.False(submission.CanRetry); Assert.Throws<InvalidOperationException>(submission.EnterOriginalFactory);
        raw.SetResult(Receipt(submission, definition));
        Assert.True(submission.CanRetry);
    }

    [Fact]
    public void A_copied_snapshot_or_foreign_conversation_cannot_acknowledge_the_retained_operation()
    {
        var definition = Definition(); var submission = new OriginalProjectTaskSubmission(Choice(), definition.Identity, definition.Revision, "Project task");
        var actual = Receipt(submission, definition);
        submission.EnterOriginalFactory(); submission.CaptureOriginalCommand(Task.FromResult(actual));
        Assert.Throws<InvalidOperationException>(() => submission.Acknowledge(actual with { }));
        Assert.True(submission.HasUnresolvedAttempt);
        submission.Acknowledge(actual); Assert.True(submission.IsAcknowledged);

        var other = new OriginalProjectTaskSubmission(Choice(), definition.Identity, definition.Revision, "Project task");
        other.EnterOriginalFactory(); other.CaptureOriginalCommand(Task.FromResult(actual));
        Assert.Throws<InvalidOperationException>(() => other.Acknowledge(actual));
        Assert.True(other.HasUnresolvedAttempt);
    }

    [Fact]
    public void Pending_creation_exposes_only_explicit_same_operation_retry_and_revoke_hides_it()
    {
        var bindings = new AssistantsCuiBindings((_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
        bindings.BeginProjectSelection(false); bindings.SetProjectSubmission(true, true, "Project task");
        Assert.True(bindings.IsActionAvailable("assistants.project.retry"));
        Assert.False(bindings.IsActionAvailable("assistants.project.choose"));
        Assert.False(bindings.IsActionAvailable("assistants.project.refresh"));
        bindings.SetProjectBusy(true); Assert.False(bindings.IsActionAvailable("assistants.project.retry"));
        bindings.SetProjectBusy(false); bindings.CloseProjectSelection();
        Assert.False(bindings.IsActionAvailable("assistants.project.retry"));
        bindings.BeginProjectSelection(false); Assert.True(bindings.IsActionAvailable("assistants.project.retry"));
        bindings.Revoke(); Assert.False(bindings.IsActionAvailable("assistants.project.retry"));
        Assert.True(bindings.TryGetValue("PendingProjectCreationTitle", out var title)); Assert.Null(title);
    }

    [Fact]
    public async Task Exact_settled_pre_effect_refusal_releases_only_unknown_work_barrier_without_success_or_new_ids()
    {
        var definition = Definition(); var choice = Choice();
        var submission = new OriginalProjectTaskSubmission(choice, definition.Identity, definition.Revision, "Project task");
        var conversationId = submission.ConversationId; var operationId = submission.OperationId;
        var refusal = new AssistantCommandRefusedException("Local state fixture; actual native issuer query is a separate source requirement.");
        var command = Task.FromException<AssistantsWorkspaceSnapshot>(refusal);
        submission.EnterOriginalFactory(); submission.CaptureOriginalCommand(command);
        Assert.Same(refusal, await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => command));
        Assert.False(submission.ObserveAcknowledgedOriginalPreEffectRefusal(Task.FromException<AssistantsWorkspaceSnapshot>(refusal)));
        Assert.True(submission.HasUnresolvedAttempt);
        Assert.True(submission.ObserveAcknowledgedOriginalPreEffectRefusal(command));
        Assert.True(submission.IsOriginalPreEffectRefused); Assert.False(submission.HasUnresolvedAttempt);
        Assert.False(submission.IsAcknowledged); Assert.True(submission.CanRetry);
        Assert.Same(choice, submission.OriginalChoice); Assert.Equal(conversationId, submission.ConversationId); Assert.Equal(operationId, submission.OperationId);
        submission.EnterOriginalFactory();
        Assert.False(submission.IsOriginalPreEffectRefused); Assert.True(submission.HasUnresolvedAttempt);
        var mixed = new AggregateException(refusal, new IOException("Unknown actual write outcome."));
        var mixedTask = Task.FromException<AssistantsWorkspaceSnapshot>(mixed); submission.CaptureOriginalCommand(mixedTask);
        Assert.Same(mixed, await Assert.ThrowsAsync<AggregateException>(() => mixedTask));
        // A mixed source has no canonical no-effect issuer receipt; the native query branch never calls the local marker.
        Assert.True(submission.HasUnresolvedAttempt); Assert.False(submission.IsOriginalPreEffectRefused);
    }

    [Fact]
    public void Known_declined_choice_keeps_explicit_retry_and_allows_a_separate_explicit_choice()
    {
        var bindings = new AssistantsCuiBindings((_, _, _) => ValueTask.CompletedTask, body => body(), () => true);
        var definition = Definition(); bindings.ApplySnapshot(AssistantsWorkspaceSnapshot.Empty with
        { Revision = 1, SelectedAssistant = definition, Assistants = [definition] });
        bindings.BeginProjectSelection(false); bindings.SetProjectSubmission(false, true, "Retained choice", settledPreEffectRefusal: true);
        Assert.True(bindings.IsActionAvailable("assistants.project.retry"));
        Assert.True(bindings.IsActionAvailable("assistants.project.refresh"));
        Assert.True(bindings.TryGetValue("HasPendingProjectCreation", out var unknown)); Assert.Equal(false, unknown);
        Assert.True(bindings.TryGetValue("HasDeclinedProjectCreation", out var declined)); Assert.Equal(true, declined);
        bindings.SetProjectSubmission(true, false, "SAME retry now accepted");
        Assert.False(bindings.IsActionAvailable("assistants.project.refresh"));
        Assert.True(bindings.TryGetValue("HasDeclinedProjectCreation", out var noLongerDeclined)); Assert.Equal(false, noLongerDeclined);
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

    private static AssistantsWorkspaceSnapshot Receipt(OriginalProjectTaskSubmission submission, AssistantDefinitionSnapshot definition)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(submission.ConversationId, HavenMode.Tasks, ConversationKind.Task,
            submission.Title, Guid.NewGuid(), null, false, false, now, now);
        var binding = new AssistantConversationBinding(new object(), definition, "fixture-session", 1, conversation);
        return AssistantsWorkspaceSnapshot.Empty with { Revision = 1, SelectedAssistant = definition,
            ConversationBinding = binding, Conversation = new(conversation, [], [], [], null, null) };
    }
}
