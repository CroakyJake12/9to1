using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Dev;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

// These injected source fixtures test presentation custody and saved-state behavior.
// They are not Home/runtime issuance and provide no product acceptance evidence.
public sealed partial class AssistantsWorkspaceControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_pending_publication_never_becomes_an_enclosing_pre_effect_receipt(bool sqlChildDeclined)
    {
        // Presentation/classification control only; does not issue Home/SQL authority.
        var conversationId = Guid.NewGuid(); var operationId = Guid.NewGuid();
        var child = sqlChildDeclined ? new AssistantCommandRefusedException("The actual SQL child entered no write.") : null;
        var pending = new AssistantCompatibleTaskPublicationPendingException(conversationId, operationId, 7, child);
        var raw = Task.FromException<AssistantCatalogueObservation>(pending);
        var bridge = new Bridge { List = () => raw };
        var controller = new AssistantsWorkspaceController(bridge);
        var actual = controller.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Same(pending, await Assert.ThrowsAsync<AssistantCompatibleTaskPublicationPendingException>(() => actual));
        Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.Equal(conversationId, pending.OriginalConversationId); Assert.Equal(operationId, pending.OriginalOperationId);
        Assert.Equal(7, pending.OriginalMembershipRevision); Assert.Same(child, pending.InnerException);
        var close = controller.CloseAndDrainAsync();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(Contains(failure, pending)); Assert.Same(close, controller.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Close_joins_the_held_original_before_retiring_its_scoped_bridge()
    {
        var source = new TaskCompletionSource<AssistantCatalogueObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new Bridge { List = () => { entered.SetResult(); return source.Task; } };
        var controller = new AssistantsWorkspaceController(bridge);
        var original = controller.InitializeAsync(TestContext.Current.CancellationToken); await entered.Task;
        var close = controller.CloseAndDrainAsync();
        Assert.Same(close, controller.OriginalClose); Assert.Same(close, controller.CloseAndDrainAsync());
        Assert.False(close.IsCompleted); Assert.Equal(0, bridge.CloseCalls);
        Assert.Throws<ObjectDisposedException>(() => { _ = controller.InitializeAsync(TestContext.Current.CancellationToken); });
        source.SetResult(new([], []));
        await original; await close;
        Assert.Equal(1, bridge.CloseCalls); Assert.True(controller.Snapshot.IsRetiring);
    }

    [Fact]
    public async Task Mixed_raw_faults_preserve_the_unknown_sibling_and_close_every_owner()
    {
        var refusal = new DenException(DenErrorCode.Conflict, "Definition revision changed.");
        var unknown = new IOException("Original source cleanup failed.");
        var source = new TaskCompletionSource<AssistantCatalogueObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new Bridge { List = () => { entered.SetResult(); return source.Task; } };
        var controller = new AssistantsWorkspaceController(bridge);
        var original = controller.InitializeAsync(TestContext.Current.CancellationToken); await entered.Task;
        source.SetException([refusal, unknown]);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => original);
        Assert.True(Contains(observed, refusal)); Assert.True(Contains(observed, unknown));
        var close = controller.CloseAndDrainAsync();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.True(Contains(failure, refusal)); Assert.True(Contains(failure, unknown));
        Assert.Same(close, controller.CloseAndDrainAsync()); Assert.Equal(1, bridge.CloseCalls);
    }

    [Fact]
    public async Task Configuration_conflict_preserves_the_acknowledged_configuration()
    {
        var saved = Definition("developer", "9to1 Developer", 4);
        var bridge = new Bridge
        {
            List = () => Task.FromResult(new AssistantCatalogueObservation([saved], [])),
            Get = _ => Task.FromResult(saved),
            Update = (_, _, _, _) => Task.FromException<AssistantDefinitionSnapshot>(new DenException(DenErrorCode.Conflict, "A newer definition exists."))
        };
        var controller = new AssistantsWorkspaceController(bridge);
        await controller.InitializeAsync(TestContext.Current.CancellationToken); await controller.OpenAssistantAsync(saved.Identity, TestContext.Current.CancellationToken);
        var draft = saved.Configuration with { Instructions = "Unsaved instructions" };
        await Assert.ThrowsAsync<DenException>(() => controller.ConfigureAsync(saved.Identity, 4, draft, Guid.NewGuid(), TestContext.Current.CancellationToken));
        Assert.Equal(4, controller.Snapshot.SelectedAssistant!.Revision);
        Assert.Equal(saved.Configuration.Instructions, controller.Snapshot.SelectedAssistant.Configuration.Instructions);
        Assert.Equal("Unsaved instructions", draft.Instructions);
        await controller.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Rapid_navigation_does_not_publish_the_older_selected_identity()
    {
        var first = Definition("first", "First", 1); var second = Definition("second", "Second", 1);
        var held = new TaskCompletionSource<AssistantDefinitionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new Bridge
        {
            Get = id => id == first.Identity ? Hold() : Task.FromResult(second)
        };
        Task<AssistantDefinitionSnapshot> Hold() { entered.SetResult(); return held.Task; }
        var controller = new AssistantsWorkspaceController(bridge);
        var selections = new List<AssistantIdentity>();
        controller.StateChanged += state => { if (state.SelectedAssistant is { } definition) selections.Add(definition.Identity); };
        var oldOpen = controller.OpenAssistantAsync(first.Identity, TestContext.Current.CancellationToken); await entered.Task;
        var latestOpen = controller.OpenAssistantAsync(second.Identity, TestContext.Current.CancellationToken); held.SetResult(first);
        await oldOpen; await latestOpen;
        Assert.Equal(second.Identity, controller.Snapshot.SelectedAssistant!.Identity);
        Assert.DoesNotContain(first.Identity, selections);
        await controller.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Restored_context_source_and_publication_callbacks_cannot_join_their_own_controller()
    {
        var clean = ExecutionContext.Capture()!; var saved = Definition("same", "Same", 1);
        AssistantsWorkspaceController? controller = null; var sourceDenials = 0; var publicationDenials = 0;
        var bridge = new Bridge
        {
            Get = async _ => { await Task.Yield(); return saved; },
            Conversations = _ =>
            {
                ExecutionContext.Run(clean, _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { _ = controller!.CloseAndDrainAsync(); }); sourceDenials++;
                }, null);
                return Task.FromResult<IReadOnlyList<AssistantConversationSummary>>([]);
            }
        };
        controller = new(bridge);
        controller.StateChanged += _ => ExecutionContext.Run(clean, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = controller.CloseAndDrainAsync(); }); publicationDenials++;
        }, null);
        await controller.OpenAssistantAsync(saved.Identity, TestContext.Current.CancellationToken);
        Assert.Equal(1, sourceDenials); Assert.True(publicationDenials > 0);
        await controller.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Repeated_known_revision_refusals_do_not_consume_unresolved_custody()
    {
        var bridge = new Bridge { List = () => Task.FromException<AssistantCatalogueObservation>(new DenException(DenErrorCode.Conflict, "Refresh current state.")) };
        var controller = new AssistantsWorkspaceController(bridge);
        for (var index = 0; index <= AssistantsWorkspaceController.MaximumRetainedOriginals; index++)
            await Assert.ThrowsAsync<DenException>(() => controller.InitializeAsync(TestContext.Current.CancellationToken));
        await controller.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Foreign_empty_aggregate_remains_the_actual_close_fault()
    {
        var foreign = new AggregateException("Foreign empty group.");
        var bridge = new Bridge { List = () => Task.FromException<AssistantCatalogueObservation>(foreign) };
        var controller = new AssistantsWorkspaceController(bridge);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => controller.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Same(foreign, observed);
        var close = controller.CloseAndDrainAsync();
        var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, foreign));
        Assert.Same(close, controller.CloseAndDrainAsync()); Assert.Equal(1, bridge.CloseCalls);
    }

    [Fact]
    public async Task Foreign_nested_refusal_group_cannot_prune_or_replace_its_identity()
    {
        var direct = new DenException(DenErrorCode.Conflict, "Refusal-looking foreign child.");
        var nested = new AggregateException("Foreign nested group.", direct);
        var foreign = new AggregateException("Foreign outer group.", nested);
        var bridge = new Bridge { List = () => Task.FromException<AssistantCatalogueObservation>(foreign) };
        var controller = new AssistantsWorkspaceController(bridge);
        Assert.Same(foreign, await Assert.ThrowsAsync<AggregateException>(() => controller.InitializeAsync(TestContext.Current.CancellationToken)));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => controller.CloseAndDrainAsync());
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, foreign));
        Assert.DoesNotContain(failure.InnerExceptions, value => ReferenceEquals(value, nested) || ReferenceEquals(value, direct));
        Assert.Equal(1, bridge.CloseCalls);
    }

    [Fact]
    public async Task Multiple_raw_causes_preserve_foreign_empty_group_and_io_sibling()
    {
        var foreign = new AggregateException("Foreign empty group."); var unknown = new IOException("Actual source fault.");
        var source = new TaskCompletionSource<AssistantCatalogueObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetException([foreign, unknown]);
        var bridge = new Bridge { List = () => source.Task };
        var controller = new AssistantsWorkspaceController(bridge);
        var observed = await Assert.ThrowsAsync<AggregateException>(() => controller.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Contains(observed.InnerExceptions, value => ReferenceEquals(value, foreign));
        Assert.Contains(observed.InnerExceptions, value => ReferenceEquals(value, unknown));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => controller.CloseAndDrainAsync());
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, foreign));
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, unknown));
        Assert.Equal(2, failure.InnerExceptions.Count); Assert.Equal(1, bridge.CloseCalls);
    }

    [Fact]
    public async Task Only_the_same_settled_refusal_command_is_acknowledged_to_native_presentation()
    {
        var refusal = new DenException(DenErrorCode.Conflict, "Refresh the current revision.");
        var bridge = new Bridge { List = () => Task.FromException<AssistantCatalogueObservation>(refusal) };
        var controller = new AssistantsWorkspaceController(bridge);
        var actual = controller.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Same(refusal, await Assert.ThrowsAsync<DenException>(() => actual));
        Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual));
        Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(Task.FromException<AssistantCatalogueObservation>(refusal)));
        Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(Task.CompletedTask));
        bridge.List = () => Task.FromResult(new AssistantCatalogueObservation([], []));
        var corrected = controller.InitializeAsync(TestContext.Current.CancellationToken); await corrected;
        Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(corrected));
        Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(actual)); // released custody cannot recreate an acknowledgment
        await controller.CloseAndDrainAsync();

        var raw = new TaskCompletionSource<AssistantCatalogueObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unknown = new IOException("Unknown cleanup sibling."); raw.SetException([refusal, unknown]);
        var other = new AssistantsWorkspaceController(new Bridge { List = () => raw.Task });
        var mixed = other.InitializeAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AggregateException>(() => mixed);
        Assert.False(other.IsAcknowledgedOriginalCommandRefusal(mixed));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => other.CloseAndDrainAsync());
        Assert.Contains(failure.InnerExceptions, value => ReferenceEquals(value, unknown));
    }

    private static AssistantDefinitionSnapshot Definition(string id, string name, long revision) => new(
        new("personal-den", "user", id), revision, ConfiguredIdentityKind.Assistant,
        new() { Name = name, Instructions = "Retained instructions" }, []);

    private static bool Contains(Exception error, Exception expected) => ReferenceEquals(error, expected)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(value => Contains(value, expected));

    private sealed partial class Bridge : IAssistantCanonicalBridge
    {
        public Func<Task<AssistantCatalogueObservation>> List = () => Task.FromResult(new AssistantCatalogueObservation([], []));
        public Func<AssistantIdentity, Task<AssistantDefinitionSnapshot>> Get = _ => Task.FromException<AssistantDefinitionSnapshot>(new NotSupportedException());
        public Func<AssistantIdentity, long, AssistantConfiguration, Guid, Task<AssistantDefinitionSnapshot>> Update = (_, _, _, _) => Task.FromException<AssistantDefinitionSnapshot>(new NotSupportedException());
        public Func<AssistantIdentity, Task<IReadOnlyList<AssistantConversationSummary>>> Conversations = _ => Task.FromResult<IReadOnlyList<AssistantConversationSummary>>([]);
        public int CloseCalls;
        public Task<AssistantCatalogueObservation> ListAsync(CancellationToken token = default) => List();
        public Task<AssistantDefinitionSnapshot> GetAsync(AssistantIdentity identity, CancellationToken token = default) => Get(identity);
        public Task<AssistantDefinitionSnapshot> UpdateAsync(AssistantIdentity identity, long expectedRevision, AssistantConfiguration configuration, Guid operationId, CancellationToken token = default) => Update(identity, expectedRevision, configuration, operationId);
        public Task<IReadOnlyList<AssistantConversationSummary>> ReadConversationsAsync(AssistantIdentity identity, int maximum = 100, CancellationToken token = default) => Conversations(identity);
        public void RequestRetirement() { }
        public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() { CloseCalls++; return Task.CompletedTask; }
        public Task<AssistantDefinitionSnapshot> CreateAsync(ConfiguredIdentityKind kind, AssistantConfiguration configuration, Guid operationId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssistantLegacyMigrationCandidate>> ReadLegacyMigrationCandidatesAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantDefinitionSnapshot> ResolveLegacyDefinitionAsync(AssistantIdentity legacyIdentity, long expectedRevision, ConfiguredIdentityKind confirmedKind, AssistantConfiguration configuration, Guid operationId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantConversationBinding> CreateConversationAsync(AssistantIdentity identity, long expectedDefinitionRevision, Guid conversationId, string title, Guid operationId, CancellationToken token = default, AssistantConversationKind kind = AssistantConversationKind.Chat) => throw new NotSupportedException();
        public Task<AssistantConversationBinding> OpenConversationAsync(AssistantIdentity identity, Guid conversationId, CancellationToken token = default) => throw new NotSupportedException();
        public bool IsIssuedOriginalBinding(AssistantConversationBinding binding) => false;
        public Task<AssistantConversationData> ReadConversationAsync(AssistantConversationBinding binding, CancellationToken token = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssistantModelChoice>> ListAvailableModelsAsync(AssistantConversationBinding binding, CancellationToken token = default) => throw new NotSupportedException();
        public Task SaveConversationDraftAsync(AssistantConversationBinding binding, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken token = default) => throw new NotSupportedException();
        public Task<MessageAttachment> ImportAttachmentOriginalAsync(AssistantConversationBinding binding, string selectedPath, Guid? branchId, CancellationToken token = default) => throw new NotSupportedException();
        public Task RemoveAttachmentOriginalAsync(AssistantConversationBinding binding, Guid attachmentId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ConversationBranch> CreateBranchAsync(AssistantConversationBinding binding, Guid messageId, string? name, CancellationToken token = default) => throw new NotSupportedException();
        public Task SwitchBranchAsync(AssistantConversationBinding binding, Guid branchId, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantOriginalConversationObservation> SendConversationOriginalAsync(AssistantConversationBinding binding, AssistantTaskInput input, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantWorkObservation> ReadWorkAsync(AssistantConversationBinding binding, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantOriginalSendObservation> StartOriginalTaskAsync(AssistantConversationBinding binding, AssistantTaskInput input, CancellationToken token = default) => throw new NotSupportedException();
        public Task<FollowUpDecision> SubmitFollowUpAsync(AssistantConversationBinding binding, ProviderExecutionContext expectedTask, string instruction, TaskFollowUpMode mode, CancellationToken token = default) => throw new NotSupportedException();
        public Task<TaskRunOriginalRunControlResult> ControlOriginalRunAsync(AssistantConversationBinding binding, ProviderExecutionContext expectedTask, TaskRunOriginalRunControlKind kind, CancellationToken token = default) => throw new NotSupportedException();
        public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalResumeAsync(AssistantConversationBinding binding, ProviderExecutionContext expectedTask, CancellationToken token = default) => throw new NotSupportedException();
        public Task<AssistantDevelopmentBinding> OpenDevelopmentOriginalAsync(AssistantConversationBinding binding, DeveloperProjectReference reference, ProviderExecutionContext expectedTask, CancellationToken token = default) => throw new NotSupportedException();
    }
}
