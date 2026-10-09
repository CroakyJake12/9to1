#if !ANDROID
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Files.NativeHost;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using ActualHomeRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Real_Home_disclosure_decline_preserves_accepted_document_without_controlled_transport_start() => EgressControl(0);
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Real_pending_disclosure_withdrawal_joins_same_Home_receipt_without_controlled_transport_start() => EgressControl(1);
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Separate_Home_disclosure_and_Task_permission_deliver_only_selected_document_to_controlled_transport() => EgressControl(2);

    // Genuine Home/Files/Den/SQLite/Task admission; TEST-ONLY registered remote
    // transport and credential source. No external request or installed-model claim.
    private static async Task EgressControl(int decision)
    {
        var c = new EgressFixture(); var errors = new List<Exception>(); RetainedAttachmentGraphs.Add([c, errors]);
        try
        {
            await c.Initialize();
            var starting = c.Rig.Bridge.StartOriginalTaskAsync(c.Binding,
                new(EgressFixture.Prompt, c.Model.Model, EffortLevel.Medium, [c.Attachment.Id], c.Model.ProviderId), Token); c.Retain(starting);
            await c.Graph.Provider.CatalogueReached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var task = Assert.IsType<TaskExecutionSnapshot>(await c.Graph.Tasks.GetByContextAsync(c.Binding.Conversation.Id, Token));
            var owner = Assert.IsType<TaskExecutionOwnerBinding>(task.OwnerBinding);
            Assert.NotEqual((await c.Rig.Profiles.GetCurrentAsync(Token))!.ActorId, owner.ActorId);
            var candidate = await c.Graph.Authority.CaptureSelectedRouteAsync(task, c.Graph.Provider.Model, [ToolCapability.Text], [], Token);
            var scope = TaskRunCentralCloudUsePermissionSource.ScopeFor(owner, candidate);
            c.Graph.Policy.Grant(scope); // Explicit decision through the SAME actual central policy.
            Assert.Contains(scope, c.Graph.Policy.Grants); c.Graph.Provider.ReleaseCatalogue.TrySetResult();
            var observation = await starting; c.Roots.Add(observation);
            var waiting = observation.WaitAsync(Token); c.Retain(waiting);
            var request = await c.WaitForDisclosure(waiting);
            Assert.NotEqual(c.ImportRequest, request.RequestId); Assert.NotEqual(c.ReadRequest, request.RequestId);
            Assert.True(request.Policy.RequiresPerActionApproval); Assert.Equal(0, c.Graph.Provider.Starts);
            var accepted = await c.Rig.OriginalConversations.GetMessagesAsync(c.Binding.Conversation.Id, Token);
            Assert.Equal(EgressFixture.Prompt, Assert.Single(accepted, value => value.Role == MessageRole.User).Content);
            Assert.DoesNotContain(accepted, value => value.Role == MessageRole.Assistant);
            if (decision == 1)
            {
                c.Home.RequestOriginalRetirement();
                var withdrawal = Assert.IsAssignableFrom<Task>(c.Home.OriginalPendingReviewWithdrawalTask); c.Retain(withdrawal);
                await withdrawal; Assert.Same(withdrawal, c.Home.OriginalPendingReviewWithdrawalTask);
            }
            else Assert.True((await c.Rig.Permissions.DecideAsync(request.RequestId,
                decision == 2 ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
            if (decision == 2)
            {
                Assert.Equal(TaskRunInitialChatObservationDisposition.ProducerTerminal, (await waiting).Disposition);
                Assert.Equal(1, c.Graph.Provider.Starts);
                var payload = Assert.IsType<OllamaChatRequest>(c.Graph.Provider.ActualRequest);
                Assert.Contains(EgressFixture.Selected, JsonSerializer.Serialize(payload));
                Assert.DoesNotContain(EgressFixture.Unselected, JsonSerializer.Serialize(payload));
                var context = Assert.IsType<ProviderExecutionContext>(payload.ExecutionContext);
                Assert.Equal(owner.TaskId, context.TaskId); Assert.Equal(owner.ExecutionId, context.ExecutionId);
            }
            else
            {
                c.ExpectRefusal(await Record.ExceptionAsync(() => waiting)); Assert.Equal(0, c.Graph.Provider.Starts);
                AssertActualDisclosureRefusalGraph(c.Graph.Cloud);
                var current = await c.Rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(request.RequestId, Scope, c.Retain, Token);
                Assert.NotNull(current); Assert.Equal(decision == 1 ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Denied, current!.State);
            }
            var same = Assert.IsType<TaskExecutionSnapshot>(await c.Graph.Tasks.GetAsync(owner.TaskId, Token));
            Assert.Equal(owner, same.OwnerBinding); Assert.Equal(owner.ExecutionId, same.ExecutionId);
            var unchangedBytes = await File.ReadAllBytesAsync(c.Path, Token);
            Assert.True(c.Bytes.SequenceEqual(unchangedBytes));
            var reopened = await c.Rig.Bridge.OpenConversationAsync(c.Binding.Definition.Identity, c.Binding.Conversation.Id, Token);
            var data = await c.Rig.Bridge.ReadConversationAsync(reopened, Token);
            Assert.Contains(data.Attachments, value => value.Id == c.Attachment.Id && value.ExtractedText == c.Attachment.ExtractedText);
            Assert.Equal(EgressFixture.Prompt, Assert.Single(data.Messages, value => value.Role == MessageRole.User).Content);
            Assert.Equal(owner.TaskId, Assert.IsType<TaskExecutionSnapshot>(data.CanonicalTask).TaskId);
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            c.Graph?.Provider.ReleaseCatalogue.TrySetResult();
            try { c.Home?.RequestOriginalPendingReviewWithdrawals(); } catch (Exception cause) { errors.Add(cause); }
            try { await c.Close(); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual attachment disclosure control and retained originals.", errors);
    }
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Pruned_healthy_Home_disclosure_keeps_late_callback_failure_and_refuses_next_productive_entry() => EgressCallbackControl(0);
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Admitted_Home_disclosure_stops_after_old_callback_failure_and_independently_closes_actual_lease() => EgressCallbackControl(1);

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Closed_Home_disclosure_context_refuses_late_productive_scope_before_body() => EgressCallbackControl(2);
    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Closed_Home_disclosure_context_keeps_actual_late_raw_before_refusing_retention() => EgressCallbackControl(3);

    private static async Task EgressCallbackControl(int callbackKind)
    {
        var holdAdmitted = callbackKind == 1;
        var observation = new EgressHomeObservation(); var c = new EgressFixture(observation);
        var errors = new List<Exception>(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? probe = null, waiting = null; Exception? original = null;
        RetainedAttachmentGraphs.Add([c, observation, errors, release, reached]);
        try
        {
            await c.Initialize();
            var first = await StartEgressControl(c); c.Roots.Add(first);
            var firstWaiting = first.WaitAsync(Token); c.Retain(firstWaiting);
            var firstRequest = await c.WaitForDisclosure(firstWaiting);
            Assert.True((await c.Rig.Permissions.DecideAsync(firstRequest.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            Assert.Equal(TaskRunInitialChatObservationDisposition.ProducerTerminal, (await firstWaiting).Disposition);
            var firstCall = Assert.Single(observation.Calls);
            var firstLease = Assert.IsAssignableFrom<ICanonicalAttachmentHomeEgressLease>(firstCall.Lease);
            var firstClose = firstLease.CloseAndDrainOriginalAsync(); c.Retain(firstClose); await firstClose;
            Assert.Same(firstClose, firstLease.OriginalClose); Assert.Equal(1, c.Graph.Provider.Starts);

            await c.PrepareConversation(); observation.HoldNext = true;
            var second = await StartEgressControl(c); c.Roots.Add(second);
            waiting = second.WaitAsync(Token); c.Retain(waiting);
            var secondRequest = await c.WaitForDisclosure(waiting);
            Assert.NotEqual(firstRequest.RequestId, secondRequest.RequestId);
            Assert.True((await c.Rig.Permissions.DecideAsync(secondRequest.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            var held = await observation.Held.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            var secondLease = Assert.IsAssignableFrom<ICanonicalAttachmentHomeEgressLease>(held.Lease);
            // The second genuine acquisition has pruned the first independently
            // joined healthy Read. The observation adapter grants nothing and
            // holds only delivery to the actual attachment consumer.
            Assert.Equal(2, observation.Calls.Count); Assert.Equal(1, c.Graph.Provider.Starts);
            AssertOriginalHomeReadPruned(c.Home, firstLease, secondLease);
            if (holdAdmitted)
            {
                var once = 0;
                probe = Task.Run(async () => await secondLease.ValidateOriginalWithinSourceAsync(body =>
                {
                    body();
                    if (Interlocked.Exchange(ref once, 1) == 0)
                    { reached.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
                }, c.Retain, Token), CancellationToken.None);
                c.Retain(probe); await reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            }
            Exception? actual;
            if (callbackKind < 2)
            {
                var escaped = Assert.IsAssignableFrom<Action>(firstCall.SavedCallback);
                actual = Record.Exception(escaped);
            }
            else
            {
                const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var context = firstLease.GetType().GetField("_sources", fields)?.GetValue(firstLease);
                Assert.NotNull(context);
                if (callbackKind == 2)
                {
                    var run = context!.GetType().GetMethod("Run", fields)!.CreateDelegate<Action<Action>>(context);
                    var effects = 0; actual = Record.Exception(() => run(() => effects++)); Assert.Equal(0, effects);
                }
                else
                {
                    var retain = context!.GetType().GetMethod("Retain", fields)!.CreateDelegate<Action<Task>>(context);
                    var raw = Assert.IsAssignableFrom<Task>(firstCall.Acquisition);
                    actual = Record.Exception(() => retain(raw));
                    AssertOriginalLateHomeRaw(c.Home, raw);
                }
            }
            var leaves = EgressFixture.Leaves(actual ?? throw new InvalidOperationException("The expired Home callback was accepted.")).ToArray();
            original = Assert.Single(leaves.Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.IsType<InvalidOperationException>(original); Assert.Null(original.InnerException);
            Assert.Equal(callbackKind < 2 ? "The original ownership callback is inactive, foreign-thread or consumed."
                : "The original attachment disclosure productive context has closed.", original.Message);
            c.ExpectOnlyOriginal(actual!, original);
            release.TrySetResult();
            if (!holdAdmitted)
            {
                probe = Task.Run(async () => await secondLease.ValidateOriginalWithinSourceAsync(Scope, c.Retain, Token), CancellationToken.None);
                c.Retain(probe);
            }
            var refusal = await Record.ExceptionAsync(() => probe!);
            Assert.NotNull(refusal); c.ExpectOnlyOriginal(probe!.Exception ?? refusal!, original);
            held.Probe = probe; held.Release.TrySetResult();
            var secondFailure = await Record.ExceptionAsync(() => waiting!);
            Assert.NotNull(secondFailure); c.ExpectOnlyOriginal(waiting.Exception ?? secondFailure!, original);
            Assert.Equal(1, c.Graph.Provider.Starts);
            Assert.True(firstClose.IsCompletedSuccessfully); // No retrospective receipt rewriting.
            var unchangedBytes = await File.ReadAllBytesAsync(c.Path, Token);
            Assert.True(c.Bytes.SequenceEqual(unchangedBytes));
            Assert.False(c.Home.IsIssuedOriginalEgressLease(held.Intent, secondLease));
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            release.TrySetResult();
            if (probe is not null)
            {
                try { await probe; }
                catch (Exception cause)
                {
                    if (original is null) errors.Add(probe.Exception ?? cause);
                    else try { c.ExpectOnlyOriginal(probe.Exception ?? cause, original); } catch (Exception unexpected) { errors.Add(unexpected); }
                }
            }
            // Release held delivery even after an early assertion fails. Both its
            // actual raw Home acquisition and independently acquired cached lease
            // close remain rooted; no callback failure skips those receipts.
            foreach (var call in observation.Calls)
            { call.Probe ??= probe; call.Release.TrySetResult(); }
            c.Graph?.Provider.ReleaseCatalogue.TrySetResult();
            try { c.Home?.RequestOriginalPendingReviewWithdrawals(); } catch (Exception cause) { errors.Add(cause); }
            if (waiting is not null)
                try { await waiting; }
                catch (Exception cause)
                {
                    if (original is null) errors.Add(waiting.Exception ?? cause);
                    else try { c.ExpectOnlyOriginal(waiting.Exception ?? cause, original); } catch (Exception unexpected) { errors.Add(unexpected); }
                }
            foreach (var call in observation.Calls)
            {
                Task? close = null;
                try
                {
                    if (call.Lease is null) continue;
                    close = call.Lease.CloseAndDrainOriginalAsync(); c.Retain(close); await close;
                }
                catch (Exception cause)
                {
                    if (original is null) errors.Add(close?.Exception ?? cause);
                    else try { c.ExpectOnlyOriginal(close?.Exception ?? cause, original); } catch (Exception unexpected) { errors.Add(unexpected); }
                }
            }
            Task? homeClose = null;
            try
            {
                if (c.Home is not null)
                {
                    homeClose = c.Home.CloseAndDrainOriginalAsync(); c.Retain(homeClose);
                    Assert.Same(homeClose, c.Home.OriginalClose);
                    await homeClose;
                    if (original is not null) errors.Add(new InvalidOperationException("The Home owner discarded the actual expired callback failure."));
                }
            }
            catch (Exception cause)
            {
                if (original is null) errors.Add(homeClose?.Exception ?? cause);
                else try { c.ExpectOnlyOriginal(homeClose?.Exception ?? cause, original); } catch (Exception unexpected) { errors.Add(unexpected); }
            }
            try { await c.Close(); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual Home callback and independent disclosure custody control.", errors);
    }
    private static void AssertOriginalHomeReadPruned(HomeCanonicalAssistantAttachmentEgressSource home,
        ICanonicalAttachmentHomeEgressLease first, ICanonicalAttachmentHomeEgressLease second)
    {
        // Test-only observation of the actual owner's retained cohort. Neither a
        // reflected field nor lease identity supplies an execution permission.
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(HomeCanonicalAssistantAttachmentEgressSource);
        var gate = type.GetField("_gate", fields)?.GetValue(home);
        Assert.NotNull(gate);
        lock (gate!)
        {
            var active = Assert.IsAssignableFrom<System.Collections.IEnumerable>(type.GetField("_active", fields)?.GetValue(home));
            var retained = active.Cast<object>().ToArray();
            Assert.DoesNotContain(retained, value => ReferenceEquals(value, first));
            Assert.Contains(retained, value => ReferenceEquals(value, second));
        }
    }
    private static void AssertOriginalLateHomeRaw(HomeCanonicalAssistantAttachmentEgressSource home, Task actual)
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(HomeCanonicalAssistantAttachmentEgressSource);
        var gate = type.GetField("_unexpectedGate", fields)?.GetValue(home); Assert.NotNull(gate);
        lock (gate!)
        {
            var retained = Assert.IsAssignableFrom<System.Collections.IEnumerable>(type.GetField("_unexpectedOriginalTasks", fields)?.GetValue(home));
            Assert.Contains(retained.Cast<object>(), value => ReferenceEquals(value, actual));
        }
    }
    private static async Task<AssistantOriginalSendObservation> StartEgressControl(EgressFixture c)
    {
        var starting = c.Rig.Bridge.StartOriginalTaskAsync(c.Binding,
            new(EgressFixture.Prompt, c.Model.Model, EffortLevel.Medium, [c.Attachment.Id], c.Model.ProviderId), Token); c.Retain(starting);
        await c.Graph.Provider.CatalogueReached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
        var task = Assert.IsType<TaskExecutionSnapshot>(await c.Graph.Tasks.GetByContextAsync(c.Binding.Conversation.Id, Token));
        var owner = Assert.IsType<TaskExecutionOwnerBinding>(task.OwnerBinding);
        var candidate = await c.Graph.Authority.CaptureSelectedRouteAsync(task, c.Graph.Provider.Model, [ToolCapability.Text], [], Token);
        c.Graph.Policy.Grant(TaskRunCentralCloudUsePermissionSource.ScopeFor(owner, candidate));
        c.Graph.Provider.ReleaseCatalogue.TrySetResult(); return await starting;
    }
    // TEST-ONLY observation/delivery gate. All issuer queries, metadata, manual
    // decisions, actual acquisition and leases remain the SAME concrete Home owner.
    private sealed class EgressHomeObservation : ICanonicalAttachmentHomeEgressSource
    {
        private HomeCanonicalAssistantAttachmentEgressSource _home = null!;
        private Action<Task> _retain = null!;
        internal readonly List<Call> Calls = [];
        internal bool HoldNext;
        internal readonly TaskCompletionSource<Call> Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Bind(HomeCanonicalAssistantAttachmentEgressSource home, Action<Task> retain) { _home = home; _retain = retain; }
        public ICanonicalAttachmentEgressProducer OriginalProducer => _home.OriginalProducer;
        public Task<ICanonicalAttachmentHomeEgressLease> AcquireOriginalEgressWithinSourceAsync(ICanonicalAttachmentEgressIntent intent,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var call = new Call(intent); Calls.Add(call); var hold = HoldNext; HoldNext = false;
            var actual = _home.AcquireOriginalEgressWithinSourceAsync(intent, body =>
            { call.SavedCallback ??= body; scope(body); }, retain, token);
            call.Acquisition = actual; _retain(actual);
            var driver = Observe(); _retain(driver); return driver;
            async Task<ICanonicalAttachmentHomeEgressLease> Observe()
            {
                var lease = await actual.ConfigureAwait(false); call.Lease = lease;
                if (hold)
                {
                    Held.TrySetResult(call); await call.Release.Task.ConfigureAwait(false);
                    if (call.Probe is { } probe) await probe.ConfigureAwait(false);
                }
                return lease;
            }
        }
        public bool IsIssuedOriginalEgressLease(ICanonicalAttachmentEgressIntent intent, ICanonicalAttachmentHomeEgressLease lease) =>
            _home.IsIssuedOriginalEgressLease(intent, lease);
        public bool IsAcknowledgedOriginalEgressRefusal(Task original) => _home.IsAcknowledgedOriginalEgressRefusal(original);
        internal sealed class Call(ICanonicalAttachmentEgressIntent intent)
        {
            internal ICanonicalAttachmentEgressIntent Intent { get; } = intent;
            internal Action? SavedCallback;
            internal Task<ICanonicalAttachmentHomeEgressLease>? Acquisition;
            internal ICanonicalAttachmentHomeEgressLease? Lease;
            internal Task? Probe;
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
    private static void AssertActualDisclosureRefusalGraph(TaskRunConfiguredCloudAdmissionSource cloud)
    {
        // Observe the exact actual frame's acknowledged occurrence; these local
        // exception graphs never become provider input, permission or task proof.
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(TaskRunConfiguredCloudAdmissionSource);
        var gate = type.GetField("_sync", fields)?.GetValue(cloud); Assert.NotNull(gate);
        object frame;
        lock (gate!)
        {
            var frames = Assert.IsAssignableFrom<System.Collections.IEnumerable>(type.GetField("_attachmentFrames", fields)?.GetValue(cloud));
            frame = Assert.Single(frames.Cast<object>());
        }
        var original = Assert.IsAssignableFrom<Exception>(frame.GetType().GetField("_knownRefusal", fields)?.GetValue(frame));
        var accepts = frame.GetType().GetMethod("OnlyKnownRefusal", fields)!.CreateDelegate<Func<Exception, bool>>(frame);
        Assert.True(accepts(original)); Assert.True(accepts(new AggregateException(new AggregateException(original), original)));
        Assert.False(accepts(new AggregateException()));
        Assert.False(accepts(new EgressOpaqueAggregate(original)));
        Assert.False(accepts(new AggregateException(original, new InvalidOperationException("Unobserved sibling"))));
        Assert.False(accepts(new AggregateException(Enumerable.Repeat(original, 4097))));
        Exception deep = original; for (var index = 0; index < 4097; index++) deep = new AggregateException(deep);
        Assert.False(accepts(deep));
    }
    private sealed class EgressOpaqueAggregate(Exception cause) : AggregateException(cause);

    private sealed class EgressPolicies : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string app, string action) =>
            new AttachmentFixturePolicies().TryGet(app, action) ?? new HomeAssistantAttachmentEgressActionPolicySource().TryGet(app, action);
    }
    private sealed class EgressFixture
    {
        internal const string Prompt = "Summarize only the selected document.";
        internal const string Selected = "Actual selected registered document for separate disclosure.";
        internal const string Unselected = "Unselected private document must never appear in the request.";
        internal readonly Rig Rig;
        internal EgressTaskGraph Graph = null!; internal AttachmentGraph Attachments = null!;
        internal HomeCanonicalAssistantAttachmentEgressSource Home = null!;
        internal AssistantConversationBinding Binding = null!; internal AssistantModelChoice Model = null!; internal MessageAttachment Attachment = null!;
        internal string Path = "", ReadRequest = "", ImportRequest = ""; internal byte[] Bytes = [];
        internal readonly List<object> Roots = [];
        private NativeFilesWorkspaceService? _files;
        private readonly List<Task> _raw = []; private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        internal readonly EgressHomeObservation? HomeObservation;
        internal EgressFixture(EgressHomeObservation? homeObservation = null)
        {
            HomeObservation = homeObservation;
            Rig = new Rig((_, profiles) => _files = new(Rig!.OriginalStateStore, profiles), originalAdditionalPolicy: new EgressPolicies(),
                configuredTaskFactory: actual => { Graph = new(actual); Roots.Add(Graph); return Graph.Tasks; },
                configuredModelFactory: profiles => new AssistantOriginalModelSelectionOwner(Graph.Registry, Graph.Privacy, Graph.ModelPermissions, profiles, Graph.Primary.ToCompatibilityDescriptor),
                configuredChatFactory: (_, conversations, tasks) => Graph.CreateChat(conversations),
                configuredAttachmentFactory: (actual, production, paths) =>
                {
                    Attachments = new(actual, _files!, production, paths); Roots.Add(Attachments);
                    Graph.Chat.BindOriginalAttachmentInputSource(Attachments.Source); Attachments.Source.BindOriginalInputOwners(Graph.Chat, Graph.Tasks);
                    HomeCanonicalAssistantAttachmentEgressSource? home = null;
                    var resources = new ResourceAuthorizationService(actual.Profiles, [new HomeAssistantAttachmentEgressResourceResolver(() => home!)]);
                    var broker = new HomeResourceOperationBroker(resources, actual.Permissions);
                    Home = home = new(actual.OriginalStateStore, actual.Profiles, resources, broker, actual.Permissions, Attachments.Source); Roots.Add(home);
                    Graph.Cloud.BindOriginalAttachmentEgressSource(Attachments.Source);
                    if (HomeObservation is not null) HomeObservation.Bind(home, Retain);
                    Attachments.Source.BindOriginalEgressOwners(Graph.Cloud, (ICanonicalAttachmentHomeEgressSource?)HomeObservation ?? home);
                    return Attachments.Source;
                }, closeConfiguredAttachments: CloseBorrowers);
        }
        internal void Retain(Task raw) { lock (_raw) _raw.Add(raw); }
        internal async Task Initialize()
        {
            await Rig.InitializeAsync(true, importMemory: false); await Graph.Configure();
            Assert.True(Attachments.Source.HasOriginalEgressComposition(Graph.Cloud));
            Path = await Attachments.RegisterExistingTextAsync("selected-egress.txt", Selected); Bytes = await File.ReadAllBytesAsync(Path, Token);
            OtherPath = await RegisterAdditionalText();
            await PrepareConversation();
        }
        private string OtherPath = "";
        internal async Task PrepareConversation()
        {
            Graph.Provider.TargetConversation = Guid.Empty;
            var definition = Binding is not null ? Binding.Definition : await Rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant, new() { Name = "Actual disclosure controls", Memory = new(false) }, Guid.NewGuid(), Token);
            Binding = await Rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision, Guid.NewGuid(), "Individual disclosure", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var rootBranch = Attachments.EnsureActualRootBranchAsync(Binding); Retain(rootBranch);
            await rootBranch; rootBranch.GetAwaiter().GetResult();
            var branch = Assert.Single((await Rig.Bridge.ReadConversationAsync(Binding, Token)).Branches, value => value.IsCurrent).Id;
            await Rig.Bridge.SaveConversationDraftAsync(Binding, branch, Prompt, [], Token);
            var importing = Rig.Bridge.ImportAttachmentOriginalAsync(Binding, Path, branch, Token); Retain(importing);
            ReadRequest = await DecideAttachment(Rig, importing, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
            ImportRequest = await DecideAttachment(Rig, importing, HomeCanonicalAssistantAttachmentImportSource.ImportAction, HomeApprovalChoice.Accept); Attachment = await importing;
            var other = Rig.Bridge.ImportAttachmentOriginalAsync(Binding, OtherPath, branch, Token); Retain(other);
            await DecideAttachment(Rig, other, HomeCanonicalAssistantAttachmentReadSource.ReadAction, HomeApprovalChoice.Accept);
            await DecideAttachment(Rig, other, HomeCanonicalAssistantAttachmentImportSource.ImportAction, HomeApprovalChoice.Accept); await other;
            await Rig.Bridge.SaveConversationDraftAsync(Binding, branch, Prompt, [Attachment.Id], Token);
            Model = Assert.Single(await Rig.Bridge.ListAvailableModelsAsync(Binding, Token), value => value.ProviderId == Graph.Provider.Id);
            Graph.Provider.ResetTarget(Binding.Conversation.Id);
        }
        private async Task<string> RegisterAdditionalText()
        {
            // Reuse the existing configured workspace; ConfigureNew is create-only.
            var authority = new NativeFilesWorkspaceAuthority(_files!, Rig.Profiles, Rig.Authority);
            var workspace = Assert.IsType<NativeFilesWorkspace>(await authority.GetCurrentAsync(Token));
            var bytes = Encoding.UTF8.GetBytes(Unselected);
            var path = System.IO.Path.Combine(workspace.Configuration.RootDirectory, "unselected-egress.txt");
            await File.WriteAllBytesAsync(path, bytes, Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var id = HostedItemId.New(); var revision = new FilesRevisionId(Guid.NewGuid()); var now = DateTimeOffset.UtcNow;
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(id, null, "unselected-egress.txt", "text/plain", revision, null,
                workspace.Actor.ActorId, now, bytes.Length, hash, "registered-fixture/" + id + "/" + revision), Token)).IsSuccess);
            await workspace.Materializations.RegisterValidatedAsync(path, new(id, revision, hash, bytes.Length, now), SyncAvailability.AvailableOffline, Token);
            return path;
        }
        internal async Task<ActualHomeRequest> WaitForDisclosure(Task actual)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < until)
            {
                var requests = await Rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
                var request = requests.PendingRequests.SingleOrDefault(value => value.Scope.ActionName == HomeCanonicalAssistantAttachmentEgressSource.DiscloseAction);
                if (request is not null) return request;
                if (actual.IsCompleted) await actual;
                await Task.Delay(10, Token);
            }
            throw new TimeoutException("The actual Home disclosure request did not arrive.");
        }
        internal void ExpectRefusal(Exception? cause)
        {
            Assert.NotNull(cause); var original = Assert.Single(Leaves(cause!).Distinct<Exception>(ReferenceEqualityComparer.Instance));
            Assert.IsType<UnauthorizedAccessException>(original); Assert.Null(original.InnerException); Assert.Equal("Home declined or withdrew this request's attachment disclosure.", original.Message); _expected.Add(original);
        }
        internal static IEnumerable<Exception> Leaves(Exception cause) => cause.GetType() == typeof(AggregateException) && cause is AggregateException { InnerExceptions.Count: > 0 } group ? group.InnerExceptions.SelectMany(Leaves) : [cause];
        internal void ExpectOnlyOriginal(Exception cause, Exception original)
        {
            var leaves = Leaves(cause).ToArray(); Assert.NotEmpty(leaves);
            Assert.All(leaves, value => Assert.Same(original, value)); _expected.Add(original);
        }
        private bool Expected(Exception cause) => _expected.Count != 0 && Leaves(cause).All(_expected.Contains);
        private Task? _borrowers;
        private Task CloseBorrowers() => _borrowers ??= CloseBorrowersBody();
        private async Task CloseBorrowersBody()
        {
            var errors = new List<Exception>();
            async Task<bool> Join(Func<Task> factory)
            {
                Task? raw = null;
                try { raw = factory(); Retain(raw); await raw; return true; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); return false; }
            }
            Graph.Authority.RequestOriginalAdmissionSeal();
            var tasksHealthy = await Join(Graph.Tasks.CloseAndSuspendOriginalProducersAsync);
            var framesHealthy = await Join(Graph.Cloud.CloseOriginalAttachmentFramesAndDrainAsync);
            if (framesHealthy && await Join(Attachments.CloseAsync)) _ = await Join(Home.CloseAndDrainOriginalAsync);
            if (tasksHealthy && framesHealthy) _ = await Join(Graph.CloseDependencies);
            // Failed Task originals remain a barrier; Rig retains global stores.
            if (errors.Count != 0) throw new AggregateException("Actual Task/disclosure dependencies remain retained.", errors);
        }
        internal async Task Close()
        {
            var errors = new List<Exception>(); Task? close = null;
            try { Home?.RequestOriginalPendingReviewWithdrawals(); } catch (Exception cause) { errors.Add(cause); }
            try { close = Rig.CloseAsync(); Retain(close); await close; }
            catch (Exception cause) { var actual = close?.Exception ?? cause; if (!Expected(actual)) errors.Add(actual); }
            Task[] raw; lock (_raw) raw = _raw.ToArray();
            foreach (var actual in raw) try { await actual; } catch (Exception cause) { var error = actual.Exception ?? cause; if (!Expected(error)) errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Unexpected disclosure-control or independent close failure.", errors);
        }
    }
    private sealed class EgressTaskGraph
    {
        internal TaskExecutionCoordinator Tasks { get; } = null!;
        internal TaskRunPermissionAuthority Authority { get; }
        internal TaskRunConfiguredCloudAdmissionSource Cloud { get; }
        internal PermissionDecisionEngine Policy { get; } = new();
        internal EgressControlledProvider Provider { get; }
        internal ModelProviderRegistry Registry { get; }
        internal PrivacyPreferenceStore Privacy { get; }
        internal ModelPermissionEvaluator ModelPermissions { get; }
        internal ProviderRoutingModelClient Primary { get; }
        internal ChatSessionService Chat { get; private set; } = null!;
        private readonly ResilientProviderRoutingModelClient _routing;
        private readonly ProviderConfigurationStore _configurations;
        private readonly ExecutionEventHub _events;
        private readonly TaskRunOriginalFrameOwner _frames;
        private readonly WorkspaceTaskRunToolActionOwner _tools;
        private readonly List<Task> _raw = [];
        private Task? _close;
        internal EgressTaskGraph(Rig rig)
        {
            var paths = new Paths(rig.Root); var actors = new HostLocalTaskActorSource();
            _configurations = new(paths); Privacy = new(paths);
            ModelPermissions = new(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths)));
            Provider = new(id => Tasks.GetByContextAsync(id, Token)); Registry = new([Provider]);
            _events = new(new ExecutionEventRepository(rig.Database));
            var modelUse = new TaskRunCentralCloudUsePermissionSource(actors, Policy);
            Cloud = new(actors, _configurations, new EgressTestCredentials(), Privacy, rig.OriginalConversations,
                (task, run, attempt, token) => Tasks.TryGetIssuedAttemptAsync(task, run, attempt, token), cloudUsePermission: modelUse);
            var receipts = new WorkspaceTaskRunReceiptAuthority();
            Authority = new(actors, Registry, _configurations, Privacy, ModelPermissions, Cloud, receipts);
            _frames = new((task, run, attempt, token) => Tasks.TryGetIssuedAttemptAsync(task, run, attempt, token));
            var effects = new WorkspaceTaskRunEffectAuthority(Authority, Policy, Policy);
            _tools = new(() => Tasks, _frames, new Workspace(), new CapabilityRegistryService(new CapabilityRepository(rig.Database)),
                effects, CapabilityPlatform.Linux, receipts);
            Tasks = new(new TaskExecutionRepository(rig.Database), _events, admissionAuthority: Authority,
                runtimeSettlement: _frames, toolActionOwner: _tools);
            Primary = new(new NoModelCalls(), Registry, Privacy);
            _routing = new(Primary, Registry, _configurations, Privacy, executionEvents: _events,
                taskCoordinator: Tasks, routeCapture: Authority, originalFrames: _frames,
                modelPermissions: ModelPermissions, taskContextAuthority: Cloud);
        }
        internal async Task Configure()
        {
            await _configurations.UpsertAsync(new(Provider.Id, Provider.Kind, "TEST-ONLY remote transport", "https://attachment-test.invalid",
                true, false, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow), Token);
            await Privacy.UpdateAsync(Privacy.Current with { LocalOnlyMode = false }, Token);
        }
        internal ChatSessionService CreateChat(IConversationRepository conversations) => Chat = new(conversations, _routing,
            new CapabilityPreflightService(), new Safety(), new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()),
            executionEvents: _events, modelPermissions: ModelPermissions, taskCoordinator: Tasks, taskToolOwner: _tools, taskProviderContextCapture: Cloud);
        internal Task CloseDependencies() => _close ??= CloseDependenciesBody();
        private async Task CloseDependenciesBody()
        {
            async Task Join(Func<Task> acquire)
            {
                var actual = acquire(); _raw.Add(actual);
                try { await actual; } catch (Exception cause) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual.Exception ?? cause).Throw(); throw; }
            }
            await Join(_frames.CloseAndDrainAsync); await Join(() => _events.DisposeAsync().AsTask());
            await Join(Registry.CloseOriginalCataloguesAndDrainAsync); _configurations.Dispose();
        }
    }
    private sealed class EgressTestCredentials : IProviderSecretStore
    {
        // Explicitly TEST-ONLY credential presence; no real key or network exists.
        public Task<string?> GetAsync(string provider, string name, CancellationToken token) => Task.FromResult<string?>("TEST-ONLY-INERT-CREDENTIAL");
        public Task SetAsync(string provider, string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string provider, string name, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class EgressControlledProvider(Func<Guid, Task<TaskExecutionSnapshot?>> observeTask) : IModelProvider
    {
        public string Id => "attachment-controlled"; public string DisplayName => "TEST-ONLY attachment transport";
        public bool IsLocal => false; public bool CanManageModels => false; public ModelProviderKind Kind => ModelProviderKind.OpenAI;
        internal ProviderModelDescriptor Model { get; } = new("attachment-controlled", false,
            new("controlled-text", 0, "TEST-ONLY", "", "", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming }, DateTimeOffset.UnixEpoch));
        internal Guid TargetConversation; internal int Starts; internal OllamaChatRequest? ActualRequest;
        internal TaskCompletionSource CatalogueReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseCatalogue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ResetTarget(Guid conversation)
        {
            TargetConversation = conversation; _held = 0;
            CatalogueReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ReleaseCatalogue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private int _held;
        public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
        {
            if (TargetConversation != Guid.Empty && await observeTask(TargetConversation) is not null && Interlocked.CompareExchange(ref _held, 1, 0) == 0)
            { CatalogueReached.TrySetResult(); await ReleaseCatalogue.Task.WaitAsync(token); }
            return [Model];
        }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "TEST-ONLY", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Starts++; ActualRequest = request;
            await Task.Yield(); yield return "Controlled transport received the explicitly approved selection.";
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("The controlled source permits only its exact stream path.");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("No tool is granted by document disclosure.");
    }
}
#endif
