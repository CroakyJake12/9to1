using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

// Exercises the actual Page observation functions. These DTO controls configure
// no actor, provider, source-issued Ask, response capability or continuation grant.
public sealed class NewChatCanonicalPermissionObservationTests
{
    [Fact]
    public void Actual_returned_observation_keeps_same_request_context_and_all_canonical_ids()
    {
        var conversation = Conversation();
        var context = new ProviderExecutionContext(Guid.NewGuid(), conversation.Id, Guid.NewGuid(), Guid.NewGuid(), 7);
        var request = Request(context.ExecutionId, RemediationState.Waiting);
        var messageId = Guid.NewGuid();
        var actualEvent = ChatStreamEvent.PermissionRequired(messageId, request) with { CanonicalTaskContext = context };
        var observation = NewChatPage.CaptureCanonicalPermissionObservation(conversation, actualEvent);
        Assert.Same(request, observation.PermissionRequest);
        Assert.Same(context, observation.CanonicalTaskContext);
        Assert.Equal(messageId, observation.AssistantMessageId);
        Assert.Equal(context.TaskId, observation.CanonicalTaskContext.TaskId);
        Assert.Equal(conversation.Id, observation.CanonicalTaskContext.ContextId);
        Assert.Equal(request.ExecutionId, observation.CanonicalTaskContext.ExecutionId);
        Assert.Equal(context.AttemptId, observation.CanonicalTaskContext.AttemptId);
        Assert.Equal(7, observation.CanonicalTaskContext.PersistenceRevision);
        Assert.Contains("Waiting for permission", NewChatPage.FormatCanonicalPermissionObservation(observation));
        Assert.Contains(request.Title, NewChatPage.FormatCanonicalPermissionObservation(observation));
        Assert.Contains("This task is suspended.", NewChatPage.FormatCanonicalPermissionObservation(observation));
    }

    [Theory]
    [InlineData(RemediationState.Completed)]
    [InlineData(RemediationState.Cancelled)]
    [InlineData(RemediationState.Expired)]
    [InlineData(RemediationState.Failed)]
    public void Recorded_terminal_request_is_not_classified_as_waiting_or_as_task_resume(RemediationState state)
    {
        var conversation = Conversation();
        var context = new ProviderExecutionContext(Guid.NewGuid(), conversation.Id, Guid.NewGuid(), null, 2);
        var request = Request(context.ExecutionId, state);
        var actualEvent = ChatStreamEvent.PermissionRequired(Guid.NewGuid(), request) with { CanonicalTaskContext = context };
        var observation = NewChatPage.CaptureCanonicalPermissionObservation(conversation, actualEvent);
        var text = NewChatPage.FormatCanonicalPermissionObservation(observation);
        Assert.Same(request, observation.PermissionRequest);
        Assert.Equal(state, observation.PermissionRequest.State);
        Assert.Contains("Permission response recorded", text);
        Assert.DoesNotContain("Waiting for permission", text);
        Assert.Contains("This task is suspended.", text);
        Assert.Contains("does not start or retry", text);
        Assert.Null(observation.CanonicalTaskContext.RequestedCandidate);
        Assert.Null(observation.CanonicalTaskContext.SelectedCandidate);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("other-context")]
    [InlineData("other-run")]
    [InlineData("unacknowledged")]
    [InlineData("missing-request")]
    [InlineData("wrong-event")]
    public void Incomplete_or_foreign_observation_refuses_before_page_waiting_publication(string mismatch)
    {
        var conversation = Conversation();
        var context = new ProviderExecutionContext(Guid.NewGuid(), conversation.Id, Guid.NewGuid(), null, 1);
        var request = Request(context.ExecutionId, RemediationState.Waiting);
        var actualEvent = ChatStreamEvent.PermissionRequired(Guid.NewGuid(), request) with { CanonicalTaskContext = context };
        switch (mismatch)
        {
            case "ordinary": conversation = conversation with { Mode = HavenMode.Chat }; break;
            case "other-context": actualEvent = actualEvent with { CanonicalTaskContext = context with { ContextId = Guid.NewGuid() } }; break;
            case "other-run": actualEvent = actualEvent with { PermissionRequest = request with { ExecutionId = Guid.NewGuid() } }; break;
            case "unacknowledged": actualEvent = actualEvent with { CanonicalTaskContext = context with { PersistenceRevision = 0 } }; break;
            case "missing-request": actualEvent = actualEvent with { PermissionRequest = null }; break;
            case "wrong-event": actualEvent = actualEvent with { Kind = ChatStreamEventKind.AssistantCompleted }; break;
        }
        Assert.Throws<InvalidOperationException>(() => NewChatPage.CaptureCanonicalPermissionObservation(conversation, actualEvent));
        Assert.Equal(RemediationState.Waiting, request.State);
        Assert.Equal(1, context.PersistenceRevision);
    }

    [Fact]
    public void Empty_actual_permission_action_identity_is_refused_without_changing_request_or_run()
    {
        var conversation = Conversation();
        var context = new ProviderExecutionContext(Guid.NewGuid(), conversation.Id, Guid.NewGuid(), null, 3);
        var request = Request(context.ExecutionId, RemediationState.Waiting) with { ActionId = Guid.Empty };
        var actualEvent = ChatStreamEvent.PermissionRequired(Guid.NewGuid(), request) with { CanonicalTaskContext = context };
        Assert.Throws<InvalidOperationException>(() => NewChatPage.CaptureCanonicalPermissionObservation(conversation, actualEvent));
        Assert.Equal(Guid.Empty, request.ActionId);
        Assert.Equal(context.ExecutionId, request.ExecutionId);
        Assert.Equal(RemediationState.Waiting, request.State);
    }

    private static Conversation Conversation()
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "Actual context observation", null, null, false, false, now, now);
    }
    private static RemediationRequest Request(Guid runId, RemediationState state)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), runId, Guid.NewGuid(), RemediationType.PermissionRequest,
            "Allow remote model use for this task?", "The owning source requested this task/run scope.",
            "task.cloud", "Task remote-use permission", "configured provider/model", [], ["Approve", "Deny"],
            RemediationSensitivity.Normal, false, true, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), state, now, now);
    }
}
