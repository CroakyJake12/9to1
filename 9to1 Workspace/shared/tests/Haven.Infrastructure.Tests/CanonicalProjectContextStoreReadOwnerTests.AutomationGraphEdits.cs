using System.Text.Json;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Authored NOT RUN. Core projections execute the existing deterministic engine only;
// they do not create an AutomationRun, issue Home/Task permission or exercise native UI.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [Fact]
    public async Task Versioned_core_nodes_project_into_existing_engine_without_another_graph_runtime()
    {
        var schemas = CanonicalAutomationCoreNodes.CreateRegistry();
        Assert.True(CanonicalAutomationCoreNodes.IsRegistered(schemas));
        var graph = new GraphDocument(GraphDocument.CurrentSchemaVersion, Guid.NewGuid(), 1,
            CanonicalAutomationCoreNodes.ProfileId, GraphRevisionState.Draft, [], []);
        var editor = new NodeGraphDraftEditor(graph, schemas);
        var trigger = CanonicalAutomationCoreNodes.CreateNode(schemas, CanonicalAutomationCoreNodes.ManualType);
        var emit = CanonicalAutomationCoreNodes.CreateNode(schemas, CanonicalAutomationCoreNodes.EmitType);
        Assert.True(editor.AddNode(1, trigger).Succeeded);
        Assert.True(editor.AddNode(2, emit).Succeeded);
        Assert.True(editor.SetConfiguration(3, emit.NodeId, JsonSerializer.SerializeToElement(new { value = "a genuine deterministic output" })).Succeeded);
        var edge = new GraphConnection(Guid.NewGuid(), trigger.Ports.Single().PortId,
            emit.Ports.Single(port => port.Key == "in").PortId);
        Assert.True(editor.Connect(4, edge).Succeeded);
        var projection = new NodeGraphAutomationAdapter(schemas, CanonicalAutomationCoreNodes.Bindings()).Compile(editor.Current);
        Assert.Equal(graph.GraphId, projection.GraphId); Assert.Equal(5L, projection.SourceRevision);
        var actual = await new AutomationGraphRunner([new BuiltInAutomationActionNodeExecutor()]).RunAsync(
            projection.Definition, AutomationGraphRunMode.Real, TestContext.Current.CancellationToken);
        Assert.True(actual.Succeeded, actual.FailureMessage);
        Assert.Contains(actual.Trace, value => value.NodeId == emit.NodeId && value.Output == "a genuine deterministic output");
        Assert.True(editor.Disconnect(5, edge.ConnectionId).Succeeded);
        Assert.Equal(2, editor.Current.Nodes.Count); Assert.Empty(editor.Current.Connections);
    }
    [Fact]
    public void Empty_profile_is_not_widened_and_core_nodes_require_the_exact_owner_schema()
    {
        var schemas = CanonicalAutomationCoreNodes.CreateRegistry();
        Assert.Empty(schemas.ListNodeTypes(CanonicalAutomationCoreNodes.EmptyProfileId));
        var node = CanonicalAutomationCoreNodes.CreateNode(schemas, CanonicalAutomationCoreNodes.EmitType);
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, CanonicalAutomationCoreNodes.ProfileId,
            GraphRevisionState.Draft, [node], []);
        Assert.Empty(schemas.Validate(graph));
        Assert.NotEmpty(schemas.Validate(graph with { CapabilityProfileId = CanonicalAutomationCoreNodes.EmptyProfileId }));
        Assert.NotEmpty(schemas.Validate(graph with { Nodes = [node with { AppId = "unregistered-other-owner" }] }));
        Assert.NotEmpty(schemas.Validate(graph with { Nodes = [node with { TypeVersion = 2 }] }));
        Assert.NotEmpty(schemas.Validate(graph with { Nodes = [node with { Configuration = JsonSerializer.SerializeToElement(new { value = "{{input}}" }) }] }));
    }
    [LinuxOriginalStoreFact]
    public Task Caller_implemented_graph_session_is_refused_before_its_fields_or_sources_are_touched() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        await WithAutomationWriter(rig, repository, graph =>
        {
            var caller = new CallerGraphSession();
            Assert.False(graph.Writer.IsIssuedOriginalGraphEditSession(caller));
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = graph.Writer.EditOriginalGraphWithinSourceAsync(caller, 1,
                    new(CanonicalAutomationGraphEditKind.AddNode, CanonicalAutomationCoreNodes.ManualType), rig.Scope, graph.Retain, rig.Token);
            });
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = graph.Writer.StartOriginalGraphPublicationWithinSourceAsync(caller, 1, GraphPublicationKind.SaveDraft,
                    rig.Scope, graph.Retain, rig.Token);
            });
            Assert.Equal(0, caller.FieldsRead);
            return Task.CompletedTask;
        });
    });
    [LinuxOriginalStoreFact]
    public Task Actual_saved_graph_publication_reviews_graph_and_binding_separately_preserving_identity_history_and_source_guards() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var graph = new ActualSavedGraphPublisher(rig, repository);
        var failures = new List<Exception>();
        try
        {
            var first = await graph.Page();
            var reservation = await rig.Keep(graph.Writer.CaptureOriginalCreationWithinSourceAsync(first,
                "Genuine saved graph publication", rig.Scope, rig.Retain, rig.Token));
            var creation = await rig.Keep(graph.Writer.StartOriginalCreationWithinSourceAsync(reservation, rig.Scope, rig.Retain, rig.Token));
            var created = rig.Keep(creation.WaitOriginalCompletionAsync(rig.Token));
            await graph.Accept(created, "automations.graph.save-draft");
            await graph.Accept(created, HomeCanonicalAutomationDefinitionWriteSource.CreateAction);
            Assert.Equal(CanonicalAutomationCreationCompletionKind.CreatedDisabled, (await created).Kind);
            await rig.Keep(creation.CloseAndDrainOriginalAsync());
            var before = await graph.Select(reservation.AutomationId);
            var session = await rig.Keep(graph.Writer.OpenOriginalGraphEditWithinSourceAsync(before.Page, before.Row,
                rig.Scope, rig.Retain, rig.Token));
            foreach (var type in new[] { CanonicalAutomationCoreNodes.ManualType, CanonicalAutomationCoreNodes.EmitType })
                Assert.True((await rig.Keep(graph.Writer.EditOriginalGraphWithinSourceAsync(session, session.LocalRevision,
                    new(CanonicalAutomationGraphEditKind.AddNode, type), rig.Scope, rig.Retain, rig.Token))).Succeeded);
            var manual = session.CurrentDraft.Nodes.Single(node => node.TypeId == CanonicalAutomationCoreNodes.ManualType);
            var emit = session.CurrentDraft.Nodes.Single(node => node.TypeId == CanonicalAutomationCoreNodes.EmitType);
            Assert.True((await rig.Keep(graph.Writer.EditOriginalGraphWithinSourceAsync(session, session.LocalRevision,
                new(CanonicalAutomationGraphEditKind.Connect, OutputPortId: manual.Ports.Single().PortId,
                    InputPortId: emit.Ports.Single(port => port.Key == "in").PortId), rig.Scope, rig.Retain, rig.Token))).Succeeded);
            var delivery = await rig.Keep(graph.Writer.StartOriginalGraphPublicationWithinSourceAsync(session, session.LocalRevision,
                GraphPublicationKind.SaveDraft, rig.Scope, rig.Retain, rig.Token));
            Assert.True(graph.Writer.IsIssuedOriginalGraphPublicationObservation(delivery));
            var completion = rig.Keep(delivery.WaitOriginalCompletionAsync(rig.Token));
            var graphReview = await graph.Accept(completion, "automations.graph.save-draft");
            var bindingReview = await graph.Wait(completion, HomeCanonicalAutomationDefinitionWriteSource.BindGraphDraftAction);
            Assert.NotEqual(graphReview, bindingReview.RequestId);
            // Home graph publication alone has not rebound or rewritten the SQL row.
            var intermediate = await graph.Select(reservation.AutomationId);
            Assert.Equal(before.Row.RetainedProtectedDescriptors.Count, intermediate.Row.RetainedProtectedDescriptors.Count);
            foreach (var pair in before.Row.RetainedProtectedDescriptors) Assert.Equal(pair.Value, intermediate.Row.RetainedProtectedDescriptors[pair.Key]);
            Assert.Equal(before.Row.Value.GraphBinding, intermediate.Row.Value.GraphBinding);
            Assert.True((await rig.Keep(graph.Permissions.DecideAsync(bindingReview.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token))).Succeeded);
            var done = await completion;
            Assert.True(delivery.IsIssuedOriginalCompletion(done));
            Assert.Equal(CanonicalAutomationGraphPublicationCompletionKind.SavedDisabled, done.Kind);
            await rig.Keep(delivery.CloseAndDrainOriginalAsync());
            var saved = await graph.Select(reservation.AutomationId);
            Assert.Equal(reservation.AutomationId, saved.Row.Value.Id);
            Assert.Equal(new AutomationDefinitionGraphBinding(reservation.GraphId, 2, null), saved.Row.Value.GraphBinding);
            Assert.False(saved.Row.Value.IsEnabled); Assert.Null(saved.Row.Value.NextRunAt);
            foreach (var pair in before.Row.RetainedProtectedDescriptors.Where(pair => pair.Key is not ("instruction" or "updated_at")))
                Assert.Equal(pair.Value, saved.Row.RetainedProtectedDescriptors[pair.Key]);
            var reopened = await rig.Keep(graph.Writer.ReadOriginalSavedGraphWithinSourceAsync(saved.Page, saved.Row,
                rig.Scope, rig.Retain, rig.Token));
            Assert.NotNull(reopened); Assert.Equal(reservation.GraphId, reopened.GraphId); Assert.Equal(2, reopened.Nodes.Count);
            // A later, genuinely SQL-bound revision must not poison unrelated
            // inception-recovery scans, nor be offered as creation replay.
            Assert.Empty(await rig.Keep(graph.Writer.ReadOriginalCreationRecoveryWithinSourceAsync(saved.Page, rig.Scope, rig.Retain, rig.Token)));
            var activateSession = await rig.Keep(graph.Writer.OpenOriginalGraphEditWithinSourceAsync(saved.Page, saved.Row,
                rig.Scope, rig.Retain, rig.Token));
            var activate = await rig.Keep(graph.Writer.StartOriginalGraphPublicationWithinSourceAsync(activateSession,
                activateSession.LocalRevision, GraphPublicationKind.Activate, rig.Scope, rig.Retain, rig.Token));
            var active = rig.Keep(activate.WaitOriginalCompletionAsync(rig.Token));
            await graph.Accept(active, "automations.activate");
            await graph.Accept(active, HomeCanonicalAutomationDefinitionWriteSource.BindGraphActiveAction);
            Assert.Equal(CanonicalAutomationGraphPublicationCompletionKind.ActiveGraphDisabled, (await active).Kind);
            await rig.Keep(activate.CloseAndDrainOriginalAsync());
            var bound = await graph.Select(reservation.AutomationId);
            Assert.Equal(new AutomationDefinitionGraphBinding(reservation.GraphId, 2, 2), bound.Row.Value.GraphBinding);
            Assert.False(bound.Row.Value.IsEnabled); Assert.Null(bound.Row.Value.NextRunAt);
            Assert.Empty(await rig.Keep(repository.GetRunsAsync(reservation.AutomationId, 8, rig.Token)));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
        }
        catch (Exception cause) { failures.Add(cause); }
        foreach (var close in new Func<Task>[] { graph.Writer.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
            try { await rig.Keep(close()); } catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual graph publication/control/owner close failed; original fixtures retained.", failures);
    });
    private sealed class ActualSavedGraphPublisher
    {
        private readonly Rig _rig;
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Library;
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Writer;
        internal readonly HomeCanonicalAutomationDefinitionWriteSource Writes;
        internal readonly HomePermissionTrustService Permissions;
        internal ActualSavedGraphPublisher(Rig rig, AutomationRepository repository)
        {
            _rig = rig; Library = ActualAutomationReadSource(rig, repository); Writer = new(Library);
            var sqlPolicy = new HomeCanonicalAutomationDefinitionActionPolicySource();
            var graphPolicy = new HomeAutomationCreationGraphActionPolicySource();
            Permissions = new(rig.Home, (app, action) => sqlPolicy.TryGet(app, action) ?? graphPolicy.TryGet(app, action));
            HomeCanonicalAutomationDefinitionWriteSource? writes = null;
            var registry = new HomeGraphPublicationResourceRegistry();
            var resources = new ResourceAuthorizationService(rig.Profiles,
                [new HomeCanonicalAutomationDefinitionResourceResolver(() => writes ?? throw new InvalidOperationException("Actual SQL owner unavailable.")), registry]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Writes = writes = new(rig.Home, rig.Profiles, resources, broker, Permissions, Writer); Writer.BindOriginalHomeWriteSource(Writes);
            var schemas = CanonicalAutomationCoreNodes.CreateRegistry(); var graphs = new HomeVersionedNodeGraphRepository(rig.Home, schemas);
            var ownership = new HomeResourceStoreOwnershipAuthority(rig.Ownership, rig.Profiles);
            var publisher = new HomeGraphPublicationOwner(rig.Home, rig.Profiles, ownership, graphs, broker, Permissions, [Writer], registry);
            var association = new HomeGraphCreationAssociationSource(rig.Home, rig.Profiles, ownership, graphs, publisher, broker, Writes, Writer);
            Writer.BindOriginalCreationGraphSource(association, schemas);
        }
        internal Task<ICanonicalAutomationLibraryOriginalObservation> Page() => _rig.Keep(((ICanonicalAutomationLibraryOriginalReadSource)Library)
            .ReadOriginalLibraryWithinSourceAsync(_rig.Actor, new(Limit: 8), _rig.Scope, _rig.Retain, _rig.Token));
        internal async Task<(ICanonicalAutomationLibraryOriginalObservation Page, AutomationOwnerRead<AutomationDefinition> Row)> Select(Guid id)
        { var page = await Page(); return (page, Assert.Single(page.Definitions, row => row.Value.Id == id)); }
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> Wait(Task actual, string action)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                var matches = (await _rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: _rig.Token))).PendingRequests
                    .Where(request => request.Scope.ActionName == action).ToArray();
                if (matches.Length != 0) { var request = Assert.Single(matches); Assert.True(request.Policy.RequiresPerActionApproval); return request; }
                if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The original operation completed before its expected review."); }
                await Task.Delay(10, _rig.Token);
            }
            throw new TimeoutException("No genuine graph/definition review arrived.");
        }
        internal async Task<string> Accept(Task actual, string action)
        {
            var request = await Wait(actual, action);
            Assert.True((await _rig.Keep(Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: _rig.Token))).Succeeded);
            return request.RequestId;
        }
    }
    private sealed class CallerGraphSession : ICanonicalAutomationOriginalGraphEditSession
    {
        internal int FieldsRead;
        private T Refuse<T>() { FieldsRead++; throw new InvalidOperationException("Caller metadata must not be inspected before private issuance."); }
        public Guid AutomationId => Refuse<Guid>();
        public AuthenticatedResourceActor Actor => Refuse<AuthenticatedResourceActor>();
        public GraphDocument SavedDraft => Refuse<GraphDocument>();
        public GraphDocument CurrentDraft => Refuse<GraphDocument>();
        public long LocalRevision => Refuse<long>();
        public bool CanEdit => Refuse<bool>();
        public bool HasUnsubmittedChanges => Refuse<bool>();
        public IReadOnlyList<GraphNodeType> RegisteredNodeTypes => Refuse<IReadOnlyList<GraphNodeType>>();
    }
}
