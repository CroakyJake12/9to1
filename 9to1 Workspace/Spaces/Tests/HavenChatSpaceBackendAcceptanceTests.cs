using System.Reflection;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>Action-time adapter checks; actual persistence admission is covered by the SQLite owner tests.</summary>
public sealed class HavenChatSpaceBackendAcceptanceTests
{
    [Theory]
    [InlineData("stale-branch")]
    [InlineData("locked")]
    [InlineData("missing-model")]
    [InlineData("foreign-attachment")]
    [InlineData("already-associated")]
    [InlineData("unapproved-existing-input")]
    public async Task Send_refuses_changed_action_time_ownership_before_entering_the_canonical_executor(string reason)
    {
        var rig = new Rig();
        var request = rig.Request();
        switch (reason)
        {
            case "stale-branch": request = request with { BranchId = Guid.NewGuid() }; break;
            case "locked": rig.Locked = true; break;
            case "missing-model": rig.Models = []; break;
            case "foreign-attachment": request = request with { AttachmentIds = [Guid.NewGuid()] }; break;
            case "already-associated": rig.Attachment = rig.Attachment with { MessageId = Guid.NewGuid() }; break;
            case "unapproved-existing-input": request = request with { ExistingUserInput = new(Guid.NewGuid(), rig.Conversation.Id, MessageRole.User, "not admitted by regeneration", null, null, null, DateTimeOffset.UtcNow) }; break;
        }
        var failure = Assert.IsAssignableFrom<InvalidOperationException>(await Record.ExceptionAsync(() => rig.Backend.SendAsync(request, null, CancellationToken.None)));
        var expected = reason switch
        {
            "stale-branch" => "original conversation branch changed",
            "locked" => "safety-locked",
            "missing-model" => "selected model is no longer available",
            "unapproved-existing-input" => "Existing input is admitted only",
            _ => "submitted attachment is no longer owned"
        };
        Assert.Contains(expected, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.Executed);
    }

    [Fact]
    public async Task Send_uses_current_canonical_records_and_fresh_governed_model_without_losing_acceptance_callback()
    {
        var rig = new Rig();
        Func<ChatMessage, CancellationToken, Task> observer = (_, _) => Task.CompletedTask;
        var request = rig.Request() with { Conversation = rig.Conversation with { Title = "stale title" },
            Model = rig.Models[0] with { Family = "stale descriptor" }, InputAccepted = observer };
        await rig.Backend.SendAsync(request, null, CancellationToken.None);
        var actual = Assert.Single(rig.Executed);
        Assert.Same(rig.Conversation, actual.Conversation);
        Assert.Same(rig.Models[0], actual.Model);
        Assert.Equal(rig.Branch.Id, actual.BranchId);
        Assert.Equal([rig.Attachment.Id], actual.AttachmentIds);
        Assert.Same(observer, actual.InputAccepted);
        Assert.Contains("chat.send", rig.SafetyAdmissions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Removing_an_attachment_requires_pending_branch_ownership_and_current_safety(bool locked)
    {
        var rig = new Rig { Locked = locked };
        if (!locked) rig.Attachment = rig.Attachment with { MessageId = Guid.NewGuid() };
        var failure = Assert.IsAssignableFrom<InvalidOperationException>(await Record.ExceptionAsync(() =>
            rig.Backend.RemoveAttachmentAsync(rig.Conversation.Id, rig.Branch.Id, rig.Attachment.Id, CancellationToken.None)));
        Assert.Contains(locked ? "safety-locked" : "Only a pending attachment", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.Deleted);
    }

    private sealed class Rig
    {
        internal readonly Conversation Conversation;
        internal readonly ConversationBranch Branch;
        internal MessageAttachment Attachment;
        internal ModelDescriptor[] Models;
        internal bool Locked;
        internal readonly List<ChatSpaceTurnRequest> Executed = [];
        internal readonly List<Guid> Deleted = [];
        internal readonly List<string> SafetyAdmissions = [];
        internal readonly HavenChatSpaceBackend Backend;
        internal Rig()
        {
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            Conversation = new(id, HavenMode.Chat, ConversationKind.Chat, "Canonical title", null, null, false, false, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
            Branch = new(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, now, now);
            Attachment = new(Guid.NewGuid(), id, null, Branch.Id, "selected.txt", "selected.txt", "text/plain", MessageAttachmentKind.PlainText,
                4, "controlled-hash", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "text", "{}", now, now);
            Models = [new("model-a", 1, "fresh descriptor", "controlled", "controlled", new HashSet<ToolCapability> { ToolCapability.Text }, now)];
            var conversations = StrictProxy.Create<IConversationRepository>((method, _) => method switch
            {
                nameof(IConversationRepository.GetAsync) => Task.FromResult<Conversation?>(Conversation),
                nameof(IConversationRepository.GetMessagesAsync) => Task.FromResult<IReadOnlyList<ChatMessage>>([]),
                _ => throw Unexpected(method)
            });
            var production = StrictProxy.Create<IConversationProductionRepository>((method, _) => method switch
            {
                nameof(IConversationProductionRepository.GetCurrentBranchAsync) => Task.FromResult<ConversationBranch?>(Branch),
                nameof(IConversationProductionRepository.GetAttachmentsAsync) => Task.FromResult<IReadOnlyList<MessageAttachment>>([Attachment]),
                nameof(IConversationProductionRepository.GetBranchesAsync) => Task.FromResult<IReadOnlyList<ConversationBranch>>([Branch]),
                nameof(IConversationProductionRepository.GetDraftAsync) => Task.FromResult<ConversationDraft?>(null),
                _ => throw Unexpected(method)
            });
            var modelCatalog = StrictProxy.Create<IChatSpaceModelCatalog>((method, _) => method == nameof(IChatSpaceModelCatalog.GetInventoryAsync)
                ? Task.FromResult(new ChatSpaceModelInventory(Models, "model-a")) : throw Unexpected(method));
            var turns = StrictProxy.Create<IChatSpaceTurnExecutor>((method, args) =>
            {
                if (method != nameof(IChatSpaceTurnExecutor.ExecuteAsync)) throw Unexpected(method);
                Executed.Add((ChatSpaceTurnRequest)args[0]!); return Task.CompletedTask;
            });
            var attachments = StrictProxy.Create<IMessageAttachmentService>((method, args) =>
            {
                if (method != nameof(IMessageAttachmentService.DeleteAsync)) throw Unexpected(method);
                Deleted.Add((Guid)args[0]!); return Task.CompletedTask;
            });
            var safety = StrictProxy.Create<IConversationSafetyService>((method, args) =>
            {
                if (method != nameof(IConversationSafetyService.EnsureMayActAsync)) throw Unexpected(method);
                SafetyAdmissions.Add((string)args[1]!);
                return Locked ? Task.FromException(new ConversationSafetyLockException((Guid)args[0]!, (string)args[1]!)) : Task.CompletedTask;
            });
            Backend = new(conversations, production, StrictProxy.Create<IConversationVersioningService>((method, _) => throw Unexpected(method)), attachments, modelCatalog, turns, safety);
        }
        internal ChatSpaceTurnRequest Request() => new(Conversation, "Question", Models[0], [Attachment.Id], BranchId: Branch.Id);
        private static Exception Unexpected(string method) => new InvalidOperationException("Unexpected controlled boundary call: " + method);
    }

    public class StrictProxy : DispatchProxy
    {
        private Func<string, object?[], object?> _invoke = null!;
        internal static T Create<T>(Func<string, object?[], object?> invoke) where T : class
        {
            var value = DispatchProxy.Create<T, StrictProxy>();
            ((StrictProxy)(object)value)._invoke = invoke;
            return value;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            _invoke((method ?? throw new InvalidOperationException("Missing method")).Name, args ?? []);
    }
}
