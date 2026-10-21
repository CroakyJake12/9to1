/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/Automation12RealHomeOwnerTests.cs, a real Haven host test lane.
 * What: Closes the Automations12 REAL-HOME OWNER gaps for the shipping path:
 *   1. HomeAutomationRunResourceResolver performs canonical repository-backed resolution and owner/current-
 *      profile/revision checks (never grants arbitrary ids), fails closed without a canonical catalogue,
 *      and refuses nonexistent/disabled/other-profile objects and stale revisions. A genuine persisted
 *      resumable Run pinned to the exact revision is still resumable after a newer Active revision, while
 *      a fresh stale start stays refused.
 *   2. The durable adapter's ResolveOrCreateDefinitionAsync reuses the SAME maintained Automation for the
 *      SAME graph+trigger across Active revisions (stable id, deduplicated trigger).
 *   3. A terminal multi-node (Trigger -> Action -> Action) graph through the REAL shared registry + REAL
 *      SQLite store reports the truthful Succeeded status and EXACT StepConsumed count.
 *   4. The production shipping composition (real Home composition + DI + AddAutomationShipping) wires and
 *      resolves the gateway, the shared registry, the durable adapter and the REAL Home authority.
 *   5. Real Home authority tests against the REAL broker + permission service + local profile identity:
 *      pending -> approved once then effect completes; decline before effect refused; profile switch before
 *      effect refused; two custodians hold independent per-operation custody; a held audit /
 *      cancel-before-record outcome is retained and recovered by retry/drain.
 * Why: Automations12 reviewer findings 1-5. Uses no SDK/app/network/model calls; tests only, root compiles
 *      and runs the Browse/Host.Tests filter (this lane is NOTRUN pending owner compile). Every new Fact is
 *      registered in the owning csproj and uses TestContext.Current.CancellationToken. The real-Home tests
 *      intentionally do NOT use a scripted authority (see #5).
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Infrastructure.Automations;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class Automation12RealHomeOwnerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;

    // ---- Test 1: resolver grants only the current owner at the current Active revision ----

    [Fact]
    public async Task Resolver_grants_current_owner_current_revision_denies_foreign_object_and_stale()
    {
        var actor = new AuthenticatedResourceActor("local-profile:actor-1", "profile-1", null, null, "rev-1");
        var automationId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var definition = Automation(automationId, graphId, enabled: true) with
        {
            OwnerBinding = new AutomationOwnerBinding(Guid.NewGuid(), "profile-1", "local-profile:actor-1", "rev-1", null, null)
        };
        var repo = new FakeAutomationRepository([definition]);
        var graphs = new FakeGraphRepository(graphId, activeRevision: 12);
        var resolver = new HomeAutomationRunResourceResolver(() => repo, () => graphs);

        var scope = new ResourceScope("automation.run", graphId.ToString("D"), "12", ResourceAccess.Execute);
        var positive = await resolver.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction, scope, token).AsTask().WaitAsync(Bound, token);
        Assert.True(positive.Allowed, positive.Code);

        // Nonexistent object must be denied despite a well-formed id/revision.
        var unknown = await resolver.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction,
            new ResourceScope("automation.run", Guid.NewGuid().ToString("D"), "12", ResourceAccess.Execute), token).AsTask().WaitAsync(Bound, token);
        Assert.False(unknown.Allowed);

        // Other-profile object must be denied even though the id/revision/action are well-formed.
        var foreignDefinition = definition with
        {
            Id = Guid.NewGuid(),
            OwnerBinding = new AutomationOwnerBinding(Guid.NewGuid(), "profile-2", "local-profile:actor-2", "rev-2", null, null)
        };
        var foreignRepo = new FakeAutomationRepository([foreignDefinition]);
        var foreignResolver = new HomeAutomationRunResourceResolver(() => foreignRepo, () => graphs);
        var foreign = await foreignResolver.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction,
            new ResourceScope("automation.run", foreignDefinition.Id.ToString("D"), "12", ResourceAccess.Execute), token).AsTask().WaitAsync(Bound, token);
        Assert.False(foreign.Allowed);

        // Stale revision (not the current Active) must be denied: no persisted pin to fall back on.
        var stale = await resolver.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction,
            new ResourceScope("automation.run", graphId.ToString("D"), "11", ResourceAccess.Execute), token).AsTask().WaitAsync(Bound, token);
        Assert.False(stale.Allowed);

        // No canonical catalogue -> fail closed, never grant.
        var unwired = await new HomeAutomationRunResourceResolver().EvaluateAsync(actor,
            HomeAutomationRunResourceResolver.RunEffectAction, scope, token).AsTask().WaitAsync(Bound, token);
        Assert.False(unwired.Allowed);
    }

    // ---- Test 2: resolver resumes a genuine persisted Waiting pin even after a newer Active revision ----

    [Fact]
    public async Task Resolver_resumes_persisted_pin_after_newer_active_but_fresh_stale_stays_refused()
    {
        var actor = new AuthenticatedResourceActor("local-profile:actor-1", "profile-1", null, null, "rev-1");
        var automationId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var definition = Automation(automationId, graphId, enabled: true) with
        {
            OwnerBinding = new AutomationOwnerBinding(Guid.NewGuid(), "profile-1", "local-profile:actor-1", "rev-1", null, null)
        };
        // Active is V5; a genuine waiting V4 Run holds the exact pin we resume.
        var waitingRun = new AutomationRun(Guid.NewGuid(), automationId, AutomationRunStatus.Waiting,
            new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), null, null, null, null, "lease")
        {
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graphId, 4)
        };
        var graphs = new FakeGraphRepository(graphId, activeRevision: 5);

        // The persisted-run pin (V4) is resumable while V5 is Active.
        var repoWithPin = new FakeAutomationRepository([definition], [waitingRun]);
        var withPin = new HomeAutomationRunResourceResolver(() => repoWithPin, () => graphs);
        var resumePin = await withPin.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction,
            new ResourceScope("automation.run", graphId.ToString("D"), "4", ResourceAccess.Execute), token).AsTask().WaitAsync(Bound, token);
        Assert.True(resumePin.Allowed, resumePin.Code);

        // The SAME scope shape with no persisted pin stays refused: a fresh stale V4 is not current.
        var repoNoPin = new FakeAutomationRepository([definition]);
        var noPinResolver = new HomeAutomationRunResourceResolver(() => repoNoPin, () => graphs);
        var noPin = await noPinResolver.EvaluateAsync(actor, HomeAutomationRunResourceResolver.RunEffectAction,
            new ResourceScope("automation.run", graphId.ToString("D"), "4", ResourceAccess.Execute), token).AsTask().WaitAsync(Bound, token);
        Assert.False(noPin.Allowed);
        Assert.Contains("REVISION_NOT_CURRENT", noPin.Code, StringComparison.Ordinal);
    }

    // ---- Test 3: adapter reuses the SAME maintained Automation across Active revisions ----

    [Fact]
    public async Task Adapter_reuses_same_automation_across_active_revisions()
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var fx = BuildWaitGraph();
        var store = NewStore(now, fx, automationId: Guid.NewGuid(), pinRevision: 4);

        // Fresh activation at V4 creates the maintained Automation and a waiting Run.
        var authority = new ScriptedAuthority("local-profile:actor-1");
        var adapter4 = BuildAdapter(store, fx, activeRevision: 4, historicalRevisions: [], authority);
        var registry4 = BuildRegistry(adapter4, fx, activeRevision: 4);
        var request4 = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:actor-1" };
        var activation4 = await registry4.ActivateAsync(request4, token).AsTask().WaitAsync(Bound, token);
        Assert.True(activation4.Succeeded, activation4.Code);
        var all4 = await store.GetAllAsync(token).WaitAsync(Bound, token);
        Assert.Single(all4);

        // A NEWER Active revision (V5) on the SAME graph+trigger must NOT mint a second AutomationID.
        var adapter5 = BuildAdapter(store, fx, activeRevision: 5, historicalRevisions: [4], authority);
        var registry5 = BuildRegistry(adapter5, fx, activeRevision: 5);
        var request5 = ActivationRequest(fx, expectedRevision: 5) with { ParticipantId = "local-profile:actor-1" };
        var activation5 = await registry5.ActivateAsync(request5, token).AsTask().WaitAsync(Bound, token);
        Assert.True(activation5.Succeeded, activation5.Code);
        var all5 = await store.GetAllAsync(token).WaitAsync(Bound, token);
        var maintained = Assert.Single(all5); // one maintained Automation, stable id
        Assert.Equal(all4[0].Id, maintained.Id);
        Assert.Equal(5L, maintained.GraphBinding!.ActiveRevision);
    }

    // ---- Test 4: terminal multi-node (Trigger->Action->Action) reports EXACT steps + truthful status ----

    [Fact]
    public async Task Terminal_multinode_graph_reports_exact_steps_and_truthful_succeeded_status()
    {
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var fx = BuildTwoActionGraph();
        var automationId = Guid.NewGuid();
        var store = NewStore(now, fx, automationId, pinRevision: 4);
        var authority = new ScriptedAuthority("local-profile:actor-1");
        var adapter = BuildAdapter(store, fx, activeRevision: 4, historicalRevisions: [], authority);
        var registry = BuildRegistry(adapter, fx, activeRevision: 4);
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:actor-1" };

        var activation = await registry.ActivateAsync(request, token).AsTask().WaitAsync(Bound, token);
        Assert.True(activation.Succeeded, activation.Code);
        Assert.Equal(GraphRunOutcome.Completed, activation.EffectiveOutcome);
        // Trigger + Action A + Action B each evaluated once -> exactly three shared budget steps.
        Assert.Equal(3, activation.StepsConsumed);

        var runs = await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var run = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Contains("graphSucceeded\":true", run.Result ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
    }

    // ---- Test 5: production shipping composition wires the real Home authority + gateway + registry ----

    [Fact]
    public async Task Shipping_composition_wires_real_home_gateway_registry_authority_and_adapter()
    {
        var root = Directory.CreateTempSubdirectory("haven-automation12-shipping-").FullName;
        var previousDataDir = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", root);
            var paths = new AppPaths();
            var principal = new ScriptedPrincipal { Current = "test-principal-a" };
            var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
            var fx = BuildWaitGraph();
            var database = new SqliteDatabase(new AppPaths());
            await database.InitializeAsync(token).WaitAsync(Bound, token);
            var repo = new AutomationRepository(database);
            var automationId = Guid.NewGuid();
            var task = WaitDefinition(automationId, fx, now, pinRevision: 4);
            await repo.UpsertAsync(task, token).WaitAsync(Bound, token);

            var (schemas, graphDoc) = BuildGraphDocument(fx, activeRevision: 4);
            var graphs = new StoredGraphRepository(graphDoc, fx.GraphId, activeRevision: 4, historicalRevisions: []);
            var runner = new ScheduledTaskRunner(repo, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: repo);

            // The REAL Home composition wired with the production automation run resolver + action catalogue.
            var home = new HomeNativeWindowsComposition(
                new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                principal,
                paths,
                new HomeNativeWindowsEndpoint("haven-automation12-fixture"),
                originalResourceResolvers: WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers(
                    () => repo, () => graphs),
                originalActionPolicies: WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies());

            var services = new ServiceCollection();
            services.AddSingleton<IAppPaths>(paths);
            services.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
            services.AddSingleton<IAutomationRepository>(repo);
            services.AddSingleton<ScheduledTaskRunner>(runner);
            // Pre-register the canonical graph repository so the composition resolves the SAME test graph.
            services.AddSingleton<IVersionedNodeGraphRepository>(graphs);
            services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
            services.AddAutomationShipping(home);

            var sp = services.BuildServiceProvider();
            var gateway = sp.GetRequiredService<AutomationShippingComposition.IAutomationRunGateway>();
            var registry = sp.GetRequiredService<NodeGraphRuntimeRegistry>();
            var authority = sp.GetRequiredService<IAutomationRunAuthority>();
            Assert.IsType<HomeAutomationRunAuthority>(authority);
            var adapter = Assert.Single(sp.GetServices<INodeGraphRuntimeAdapter>());
            Assert.NotNull(adapter);
            Assert.NotNull(gateway);
            Assert.NotNull(registry);

            // The production schema still builds the canonical binding set (trigger/action/wait/approval).
            var schemasResult = HomeAutomationNodeGraphSchemas.Create();
            Assert.NotEmpty(schemasResult.Bindings);
            Assert.Contains(schemasResult.Bindings, b => b.TypeId == "trigger");
            Assert.Contains(schemasResult.Bindings, b => b.TypeId == "approval");
            Assert.DoesNotContain(schemasResult.Bindings, b => b.TypeId == "trigger" && b.AppId != "automations");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previousDataDir);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    // ---- Test 5b: the registered gateway itself (production caller) + real Home approval, fresh + resume ----

    [Fact]
    public async Task Shipping_real_gateway_production_caller_real_home_approval_fresh_and_resume_B_after_newer_active()
    {
        var root = Directory.CreateTempSubdirectory("haven-automation12-gw-").FullName;
        var previousDataDir = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", root);
            var paths = new AppPaths();
            var principal = new ScriptedPrincipal { Current = "test-principal-a" };
            var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
            var fx = BuildWaitGraph();

            // ---- Fresh composition over the REAL SQLite store ----
            var database = new SqliteDatabase(new AppPaths());
            await database.InitializeAsync(token).WaitAsync(Bound, token);
            var repo = new AutomationRepository(database);
            var automationId = Guid.NewGuid();
            var (schemas4, graphDoc4) = BuildGraphDocument(fx, activeRevision: 4);
            var graphs4 = new StoredGraphRepository(graphDoc4, fx.GraphId, activeRevision: 4, historicalRevisions: []);
            var runner4 = new ScheduledTaskRunner(repo, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: repo);

            var home4 = new HomeNativeWindowsComposition(
                new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                principal, paths, new HomeNativeWindowsEndpoint("haven-automation12-gw-fresh"),
                originalResourceResolvers: WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers(
                    () => repo, () => graphs4),
                originalActionPolicies: WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies());

            var services4 = new ServiceCollection();
            services4.AddSingleton<IAppPaths>(paths);
            services4.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
            services4.AddSingleton<IAutomationRepository>(repo);
            services4.AddSingleton<ScheduledTaskRunner>(runner4);
            services4.AddSingleton<IVersionedNodeGraphRepository>(graphs4);
            services4.AddHavenOwnedWindowsHomeDomain(home4, paths, principal);
            services4.AddAutomationShipping(home4);
            var sp4 = services4.BuildServiceProvider();

            var gateway4 = sp4.GetRequiredService<AutomationShippingComposition.IAutomationRunGateway>();
            var authority4 = (HomeAutomationRunAuthority)sp4.GetRequiredService<IAutomationRunAuthority>();
            var permissions4 = sp4.GetRequiredService<HomePermissionTrustService>();
            var actor = await home4.Profiles.GetCurrentAsync(token).AsTask().WaitAsync(Bound, token)
                ?? throw new InvalidOperationException("Real Home profile unavailable.");

            // Seed the maintained automation owned by the current actor so the real resolver grants it.
            var owned = WaitDefinition(automationId, fx, now, pinRevision: 4) with
            {
                OwnerBinding = new AutomationOwnerBinding(Guid.NewGuid(), actor.ProfileId, actor.ActorId,
                    actor.AuthenticationRevision, null, null)
            };
            await repo.UpsertAsync(owned, token).WaitAsync(Bound, token);

            // Fresh request + the EXACT operation the adapter will admit under real Home authority.
            var freshRequest = ActivationRequest(fx, expectedRevision: 4) with
            {
                ParticipantId = actor.ActorId,
                SessionId = "session-gw-fresh",
                Variables = JsonSerializer.SerializeToElement(new { run = Guid.NewGuid() })
            };
            var freshContext = new AutomationRunAuthorityContext(fx.GraphId, 4,
                HomeAutomationRunResourceResolver.RunEffectAction, freshRequest.SessionId, freshRequest.EntityId,
                freshRequest.ParticipantId,
                [new ResourceScope("automation.run", fx.GraphId.ToString("D"), "4", ResourceAccess.Execute)],
                freshRequest.Variables.Clone());

            // Real Home ADMISSION then a genuine Accept of that original review.
            var admitted = await authority4.AuthorizeAsync(freshContext, token).AsTask().WaitAsync(Bound, token);
            Assert.True(admitted.Allowed, admitted.Code);
            Assert.True((await permissions4.DecideAsync(admitted.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);

            // Invoke the REAL registered gateway: controlled A runs, then durable Wait yields.
            var activation = await gateway4.ActivateAsync(freshRequest, token).AsTask().WaitAsync(Bound, token);
            Assert.True(activation.Succeeded, activation.Code);
            Assert.Equal(GraphRunOutcome.Yielded, activation.EffectiveOutcome);
            // Trigger + Action A evaluated before the Wait yield -> exactly two consumed steps.
            Assert.Equal(2, activation.StepsConsumed);
            var waiting = Assert.Single(await repo.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
            Assert.Equal(AutomationRunStatus.Waiting, waiting.Status);
            Assert.Empty(authority4.PendingAuditFailures);

            // ---- Reopen the SAME store; a NEWER Active V5 now supersedes V4, which is still pinned ----
            var reopened = new AutomationRepository(new SqliteDatabase(new AppPaths()));
            var (schemas5, graphDoc5) = BuildGraphDocument(fx, activeRevision: 5);
            var graphs5 = new StoredGraphRepository(graphDoc5, fx.GraphId, activeRevision: 5, historicalRevisions: [4]);
            var runner5 = new ScheduledTaskRunner(reopened, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: reopened);
            var home5 = new HomeNativeWindowsComposition(
                new FileHomeCoreStateStore(Path.Combine(root, "home5.json")),
                principal, paths, new HomeNativeWindowsEndpoint("haven-automation12-gw-resume"),
                originalResourceResolvers: WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers(
                    () => reopened, () => graphs5),
                originalActionPolicies: WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies());
            var services5 = new ServiceCollection();
            services5.AddSingleton<IAppPaths>(paths);
            services5.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
            services5.AddSingleton<IAutomationRepository>(reopened);
            services5.AddSingleton<ScheduledTaskRunner>(runner5);
            services5.AddSingleton<IVersionedNodeGraphRepository>(graphs5);
            services5.AddHavenOwnedWindowsHomeDomain(home5, paths, principal);
            services5.AddAutomationShipping(home5);
            var sp5 = services5.BuildServiceProvider();
            var gateway5 = sp5.GetRequiredService<AutomationShippingComposition.IAutomationRunGateway>();
            var authority5 = (HomeAutomationRunAuthority)sp5.GetRequiredService<IAutomationRunAuthority>();
            var permissions5 = sp5.GetRequiredService<HomePermissionTrustService>();

            // Resume request/actor identity: same participant/session/variables as the stored run.
            var resumeRequest = ActivationRequest(fx, expectedRevision: 4) with
            {
                ParticipantId = actor.ActorId,
                SessionId = "session-gw-fresh",
                Variables = freshRequest.Variables.Clone()
            };
            var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, resumeRequest).WaitAsync(Bound, token);

            // Pre-approve the resume operation (automation id, pinned V4) under real Home authority.
            var resumeContext = new AutomationRunAuthorityContext(automationId, 4,
                HomeAutomationRunResourceResolver.RunEffectAction, resumeRequest.SessionId, resumeRequest.EntityId,
                resumeRequest.ParticipantId,
                [new ResourceScope("automation.run", automationId.ToString("D"), "4", ResourceAccess.Execute)],
                resumeRequest.Variables.Clone());
            var resumeAdmitted = await authority5.AuthorizeAsync(resumeContext, token).AsTask().WaitAsync(Bound, token);
            Assert.True(resumeAdmitted.Allowed, resumeAdmitted.Code);
            Assert.True((await permissions5.DecideAsync(resumeAdmitted.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);

            // Resume through the REAL gateway: B runs after newer Active, SAME cumulative budget.
            var resume = await gateway5.ResumeAsync(envelope, resumeRequest, token).AsTask().WaitAsync(Bound, token);
            Assert.True(resume.Succeeded, resume.Code);
            Assert.Equal(GraphRunOutcome.Completed, resume.EffectiveOutcome);
            // Trigger + A + Wait + B = exactly four total steps across the durable run.
            Assert.Equal(4, resume.StepsConsumed);
            var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
            Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
            Assert.Empty(authority5.PendingAuditFailures);

            // The production scheduler owns the same gateway instance and drains cleanly (no held audits).
            var scheduler = sp5.GetRequiredService<AutomationShippingScheduler>();
            Assert.Same(scheduler.Gateway, gateway5);
            Assert.Equal(0, await scheduler.DrainPendingAuditsAsync(token).WaitAsync(Bound, token));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previousDataDir);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<GraphRuntimeContinuation> BuildEnvelopeFromStored(
        AutomationRepository store, Guid automationId, GraphFixture fx, GraphActivationRequest request)
    {
        var runs = await store.GetRunsAsync(automationId, 10, CancellationToken.None).ConfigureAwait(false);
        var run = runs.FirstOrDefault(r => r.Status == AutomationRunStatus.Waiting);
        if (run is null)
            throw new InvalidOperationException("Could not locate the suspended Run for envelope derivation.");
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var cont) ||
            cont.PendingYield is not { } pending)
            throw new InvalidOperationException("The stored Run has no pending yield.");

        var nodeId = pending.Kind == AutomationGraphYieldKind.Approval
            ? (pending.ApprovalNodeId ?? Guid.Empty) : (pending.YieldNodeId ?? Guid.Empty);
        var condition = DeriveCondition(cont);
        var ownerState = DeriveOwnerState(cont);
        var kind = pending.Kind == AutomationGraphYieldKind.Approval ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
        var binding = new GraphContinuationAdmission(run.Id.ToString(), kind, nodeId, cont.ResumeAt, condition, ownerState,
            request.StepLimit, cont.ConsumedBudget);

        var (_, graph) = BuildGraphDocument(fx, activeRevision: request.ExpectedRevision);
        var active = graph with { Revision = request.ExpectedRevision };
        var digest = GraphContinuationDigest.Compute(active, request.Variables, binding);

        return new GraphRuntimeContinuation(
            GraphRuntimeContinuation.CurrentProtocolVersion,
            AutomationNodeGraphDurableAdapter.GraphTypeValue,
            run.Id.ToString(),
            fx.GraphId, request.ExpectedRevision, "automations", 1, request.StartNodeId, request.EventType,
            request.SessionId, request.EntityId, request.ParticipantId,
            kind, nodeId, cont.ResumeAt, condition, ownerState, cont.ConsumedBudget, request.StepLimit, digest);
    }

    private static JsonElement DeriveCondition(AutomationGraphContinuation c)
    {
        var condition = c.PendingYield?.Condition;
        if (string.IsNullOrWhiteSpace(condition)) return JsonSerializer.SerializeToElement(new { });
        try { using var doc = JsonDocument.Parse(condition); return doc.RootElement.Clone(); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(new { value = condition }); }
    }

    private static JsonElement DeriveOwnerState(AutomationGraphContinuation c)
    {
        var trace = c.Trace.Select(item => string.Format("{0}:{1}", item.NodeId.ToString("D"), (int)item.Status)).ToArray();
        var frontier = c.Frontier.Select(id => id.ToString("D")).ToArray();
        return JsonSerializer.SerializeToElement(new { trace, frontier });
    }

    // ---- Test 6: real Home authority — pending -> approved once -> effect completes ----

    [Fact]
    public async Task RealHome_authority_pending_to_approved_once_then_effect_completes()
    {
        using var rig = await BuildRealHomeAsync(principal: "test-principal-a");

        // Admission creates a real pending Home permission submission (never auto-granted).
        var admitted = await rig.Authority.AuthorizeAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(admitted.Allowed, admitted.Code);
        Assert.False(string.IsNullOrWhiteSpace(admitted.RequestId));
        var pending = await rig.Permissions.GetAuthorizationAsync(admitted.RequestId, token).WaitAsync(Bound, token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);

        // The single real decision approves the ORIGINAL review; the effect boundary then proceeds.
        Assert.True((await rig.Permissions.DecideAsync(admitted.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);

        var effect = await rig.Authority.AuthorizeEffectAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(effect.Allowed, effect.Code);
        Assert.Equal("HOME_AUTOMATION_RUN_EFFECT_AUTHORIZED", effect.Code);

        var completed = await rig.Authority.CompleteEffectAsync(rig.Context, effect, succeeded: true,
            "graph.automation.completed", "fresh automation ran", token).WaitAsync(Bound, token);
        Assert.True(completed);
        Assert.Empty(rig.Authority.PendingAuditFailures);
    }

    // ---- Test 7: real Home authority — decline before effect refuses, zero capability ----

    [Fact]
    public async Task RealHome_authority_decline_before_effect_refused()
    {
        using var rig = await BuildRealHomeAsync(principal: "test-principal-a");

        var admitted = await rig.Authority.AuthorizeAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(admitted.Allowed, admitted.Code);
        Assert.True((await rig.Permissions.DecideAsync(admitted.RequestId, HomeApprovalChoice.Decline, cancellationToken: token)).Succeeded);

        var effect = await rig.Authority.AuthorizeEffectAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.False(effect.Allowed);
        Assert.Contains("UNAVAILABLE", effect.Code, StringComparison.Ordinal);
        Assert.Empty(rig.Authority.PendingAuditFailures);
    }

    // ---- Test 8: real Home authority — profile switch between admission and effect refuses ----

    [Fact]
    public async Task RealHome_authority_profile_switch_before_effect_refused()
    {
        using var rig = await BuildRealHomeAsync(principal: "test-principal-a");

        var admitted = await rig.Authority.AuthorizeAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(admitted.Allowed, admitted.Code);
        Assert.True((await rig.Permissions.DecideAsync(admitted.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);

        // The operating-system principal changes to a different profile before the effect boundary.
        rig.Principal.Current = "test-principal-b";
        var effect = await rig.Authority.AuthorizeEffectAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.False(effect.Allowed);
        Assert.Contains("ACTOR_CHANGED", effect.Code, StringComparison.Ordinal);
        Assert.Empty(rig.Authority.PendingAuditFailures);
    }

    // ---- Test 9: real Home authority — two custodians hold independent per-operation custody ----

    [Fact]
    public async Task RealHome_authority_two_custodians_independent_operations()
    {
        using var rig = await BuildRealHomeAsync(principal: "test-principal-a");
        var secondContext = new AutomationRunAuthorityContext(rig.Definition.Id, rig.ActiveRevision,
            HomeAutomationRunResourceResolver.RunEffectAction, "session-2", "entity", rig.Actor.ActorId,
            [new ResourceScope("automation.run", rig.GraphId.ToString("D"), rig.ActiveRevision.ToString(), ResourceAccess.Execute)],
            JsonSerializer.SerializeToElement(new { run = Guid.NewGuid() }));

        var a = await rig.Authority.AuthorizeAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        var b = await rig.Authority.AuthorizeAsync(secondContext, token).AsTask().WaitAsync(Bound, token);
        Assert.True(a.Allowed);
        Assert.True(b.Allowed);
        Assert.NotEqual(a.RequestId, b.RequestId); // distinct operations, distinct Home reviews

        Assert.True((await rig.Permissions.DecideAsync(a.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        Assert.True((await rig.Permissions.DecideAsync(b.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);

        var effectA = await rig.Authority.AuthorizeEffectAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        var effectB = await rig.Authority.AuthorizeEffectAsync(secondContext, token).AsTask().WaitAsync(Bound, token);
        Assert.True(effectA.Allowed, effectA.Code);
        Assert.True(effectB.Allowed, effectB.Code);

        // Completing A never disturbs B; both settle independently.
        Assert.True(await rig.Authority.CompleteEffectAsync(rig.Context, effectA, true, "code-a", "a", token));
        Assert.True(await rig.Authority.CompleteEffectAsync(secondContext, effectB, true, "code-b", "b", token));
        Assert.Empty(rig.Authority.PendingAuditFailures);
    }

    // ---- Test 10: real Home authority — held audit (cancel-before-record) recovered by retry/drain ----

    [Fact]
    public async Task RealHome_authority_held_audit_cancel_before_record_recovered_by_drain()
    {
        using var rig = await BuildRealHomeAsync(principal: "test-principal-a");

        var admitted = await rig.Authority.AuthorizeAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(admitted.Allowed, admitted.Code);
        Assert.True((await rig.Permissions.DecideAsync(admitted.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var effect = await rig.Authority.AuthorizeEffectAsync(rig.Context, token).AsTask().WaitAsync(Bound, token);
        Assert.True(effect.Allowed);

        // Cancel-before-record: the completion audit is attempted on a cancelled token, so the broker's
        // terminal recording is not acknowledged; the authority retains the EXACT capability + outcome.
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var settled = await rig.Authority.CompleteEffectAsync(rig.Context, effect, true,
            "graph.automation.completed", "ran", cancelled.Token).WaitAsync(Bound, token);
        Assert.False(settled);

        var retained = Assert.Single(rig.Authority.PendingAuditFailures);
        Assert.Equal("graph.automation.completed", retained.OriginalCause);
        Assert.False(string.IsNullOrWhiteSpace(retained.AuditFailure));

        // Retry/drain replays ONLY the recorded outcome (no graph effect) and settles it.
        Assert.True(await rig.Authority.RetryPendingAuditAsync(retained.OperationKey, token).WaitAsync(Bound, token));
        Assert.Empty(rig.Authority.PendingAuditFailures);
    }

    // ---- helpers ----

    private sealed class ScriptedPrincipal : ITrustedHostPrincipalSource
    {
        public string Current = "test-principal-a";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(Current);
        }
    }

    private sealed class RealHomeFixture : IDisposable
    {
        public string Root = "";
        public ScriptedPrincipal Principal = null!;
        public HomeLocalProfileIdentity Profiles = null!;
        public HomePermissionTrustService Permissions = null!;
        public ResourceAuthorizationService Resources = null!;
        public HomeResourceOperationBroker Broker = null!;
        public HomeAutomationRunAuthority Authority = null!;
        public AuthenticatedResourceActor Actor = null!;
        public AutomationDefinition Definition = null!;
        public Guid GraphId;
        public long ActiveRevision;
        public AutomationRunAuthorityContext Context = null!;
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch (IOException) { } }
    }

    private async Task<RealHomeFixture> BuildRealHomeAsync(string principal)
    {
        var root = Directory.CreateTempSubdirectory("haven-automation12-home-").FullName;
        var scripted = new ScriptedPrincipal { Current = principal };
        var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var profiles = new HomeLocalProfileIdentity(store, scripted);
        var actor = await profiles.GetCurrentAsync(token).AsTask().WaitAsync(Bound, token)
            ?? throw new InvalidOperationException("Real Home profile unavailable.");

        var graphId = Guid.NewGuid();
        const long activeRevision = 4;
        var definition = new AutomationDefinition(Guid.NewGuid(), "Automation", HavenMode.Tasks, "{}",
            AutomationScheduleKind.Once, """{"at":"2000-01-01T00:00:00+00:00"}""", null, null, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            GraphBinding = new AutomationDefinitionGraphBinding(graphId, activeRevision, activeRevision),
            OwnerBinding = new AutomationOwnerBinding(Guid.NewGuid(), actor.ProfileId, actor.ActorId,
                actor.AuthenticationRevision, null, null)
        };
        var repo = new FakeAutomationRepository([definition]);
        var graphs = new FakeGraphRepository(graphId, activeRevision);
        var resolver = new HomeAutomationRunResourceResolver(() => repo, () => graphs);
        var permissions = new HomePermissionTrustService(store, new HomeAutomationRunActionPolicySource().TryGet);
        var resources = new ResourceAuthorizationService(profiles, [resolver]);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        var authority = new HomeAutomationRunAuthority(profiles, broker);
        var arguments = JsonSerializer.SerializeToElement(new { run = Guid.NewGuid() });
        var context = new AutomationRunAuthorityContext(definition.Id, activeRevision,
            HomeAutomationRunResourceResolver.RunEffectAction, "session-1", "entity", actor.ActorId,
            [new ResourceScope("automation.run", graphId.ToString("D"), activeRevision.ToString(), ResourceAccess.Execute)],
            arguments);

        return new RealHomeFixture
        {
            Root = root,
            Principal = scripted,
            Profiles = profiles,
            Permissions = permissions,
            Resources = resources,
            Broker = broker,
            Authority = authority,
            Actor = actor,
            Definition = definition,
            GraphId = graphId,
            ActiveRevision = activeRevision,
            Context = context
        };
    }

    private static AutomationDefinition Automation(Guid id, Guid graphId, bool enabled) =>
        new(id, "Automation", HavenMode.Tasks, "{}", AutomationScheduleKind.Once,
            """{"at":"2000-01-01T00:00:00+00:00"}""", null, null, enabled, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            GraphBinding = new AutomationDefinitionGraphBinding(graphId, 12, 12)
        };

    private sealed class FakeAutomationRepository(IReadOnlyList<AutomationDefinition> definitions, IReadOnlyList<AutomationRun>? runs = null)
        : IAutomationRepository
    {
        private readonly IReadOnlyList<AutomationRun> _runs = runs ?? [];
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken ct) => Task.FromResult(definitions);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken ct) => Task.FromResult(definitions);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken ct) => Task.FromResult(true);
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AutomationRun>>(_runs.Where(r => r.AutomationId == automationId).Take(limit).ToArray());
    }

    private sealed class FakeGraphRepository(Guid graphId, long activeRevision) : IVersionedNodeGraphRepository
    {
        private readonly Guid _graphId = graphId;
        private readonly long _activeRevision = activeRevision;
        public Task<OwnedGraphDefinition?> GetAsync(Guid id, CancellationToken ct)
        {
            if (id != _graphId) return Task.FromResult<OwnedGraphDefinition?>(null);
            var active = new GraphDocument(1, _graphId, _activeRevision, "automations", GraphRevisionState.Active, [], []);
            return Task.FromResult<OwnedGraphDefinition?>(new OwnedGraphDefinition("automations", "entity", active, active));
        }
        public Task<bool> TrySaveDraftAsync(string appId, string entityId, GraphDocument draft, long expected, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryActivateAsync(Guid id, long expected, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ScriptedAuthority(string actorId) : IAutomationRunAuthority
    {
        public ValueTask<AutomationRunAuthorityDecision> AuthorizeAsync(AutomationRunAuthorityContext context, CancellationToken ct)
            => ValueTask.FromResult(string.Equals(context.ParticipantId, actorId, StringComparison.Ordinal)
                ? new AutomationRunAuthorityDecision(true, "OK", actorId, "req")
                : new AutomationRunAuthorityDecision(false, "NO", "", ""));
        public ValueTask<AutomationRunAuthorityDecision> AuthorizeEffectAsync(AutomationRunAuthorityContext context, CancellationToken ct)
            => AuthorizeAsync(context, ct);
        public Task<bool> CompleteEffectAsync(AutomationRunAuthorityContext context, AutomationRunAuthorityDecision d,
            bool succeeded, string code, string message, CancellationToken ct) => Task.FromResult(true);
    }

    // ---- shared fixture/helpers for adapter tests ----

    private sealed record GraphFixture(Guid GraphId, string Digest, AutomationGraphDefinition Graph, Guid TriggerId, Guid ActionAId, Guid WaitId, Guid ActionBId, Guid ApprovalNodeId);

    private static GraphFixture BuildWaitGraph()
    {
        var trigger = Node("Trigger");
        var actionA = Node("Action", ("action", "emit"), ("value", "a"));
        var wait = Node("Wait", ("until", "2000-01-01T00:00:00Z"));
        var actionB = Node("Action", ("action", "emit"), ("value", "b"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [trigger, actionA, wait, actionB],
            [new(trigger.Id, actionA.Id), new(actionA.Id, wait.Id), new(wait.Id, actionB.Id)]);
        return new GraphFixture(Guid.NewGuid(), "d1g3st-aabbccdd", graph, trigger.Id, actionA.Id, wait.Id, actionB.Id, Guid.Empty);
    }

    private static GraphFixture BuildTwoActionGraph()
    {
        var trigger = Node("Trigger");
        var actionA = Node("Action", ("action", "emit"), ("value", "a"));
        var actionB = Node("Action", ("action", "emit"), ("value", "b"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [trigger, actionA, actionB],
            [new(trigger.Id, actionA.Id), new(actionA.Id, actionB.Id)]);
        return new GraphFixture(Guid.NewGuid(), "d1g3st-twoaction", graph, trigger.Id, actionA.Id, Guid.Empty, actionB.Id, Guid.Empty);
    }

    private static AutomationGraphNodeDefinition Node(string category, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        { Title = category };

    private static GraphActivationRequest ActivationRequest(GraphFixture fx, long expectedRevision)
        => new(AutomationNodeGraphDurableAdapter.GraphTypeValue, fx.GraphId, expectedRevision, fx.TriggerId,
            "schedule", "session", "entity", "local-profile:real-owner", JsonSerializer.SerializeToElement(new { }), 1000);

    private static AutomationRepository NewStore(DateTimeOffset now, GraphFixture fx, Guid automationId, long pinRevision)
    {
        var previousDataDir = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", Directory.CreateTempSubdirectory("haven-automation12-").FullName);
            var database = new SqliteDatabase(new AppPaths());
            var store = new AutomationRepository(database);
            var task = WaitDefinition(automationId, fx, now, pinRevision);
            store.UpsertAsync(task, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            return store;
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previousDataDir);
        }
    }

    private static AutomationDefinition WaitDefinition(Guid automationId, GraphFixture fx, DateTimeOffset now, long pinRevision)
    {
        var encoded = ScheduledGraphAutomationPayloadCodec.Serialize(fx.GraphId, fx.TriggerId, "Durable wait", AutomationGraphCodec.Serialize(fx.Graph), null,
            graphId: fx.GraphId, sourceRevision: pinRevision, schemaVersion: 1, sourceDigest: fx.Digest, capabilityProfileId: "automations");
        return new AutomationDefinition(automationId, "Durable wait", HavenMode.Tasks, encoded, AutomationScheduleKind.Once,
            """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddHours(-2), null, true, now, now)
        {
            GraphBinding = new AutomationDefinitionGraphBinding(fx.GraphId, pinRevision, pinRevision)
        };
    }

    private static AutomationNodeGraphDurableAdapter BuildAdapter(AutomationRepository store, GraphFixture fx,
        long activeRevision, long[] historicalRevisions, IAutomationRunAuthority authority)
    {
        var (schemas, graph) = BuildGraphDocument(fx, activeRevision);
        var repo = new StoredGraphRepository(graph, fx.GraphId, activeRevision, historicalRevisions);
        return AutomationNodeGraphDurableAdapter.Create(
            new NodeGraphAutomationAdapter(schemas, BuildBindings()),
            repo, store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store),
            authority: authority);
    }

    private static NodeGraphRuntimeRegistry BuildRegistry(AutomationNodeGraphDurableAdapter adapter, GraphFixture fx, long activeRevision)
    {
        var (schemas, _) = BuildGraphDocument(fx, activeRevision);
        return new NodeGraphRuntimeRegistry(schemas, [adapter]);
    }

    /// <summary>Projects the actual GraphFixture nodes/edges into a schema-valid GraphDocument so the SAME
    /// registry/compile path used in production sees the exact terminal multi-node shapes the tests run.</summary>
    private static (NodeGraphSchemaRegistry Schemas, GraphDocument Graph) BuildGraphDocument(GraphFixture fx, long activeRevision)
    {
        var emptyConfig = JsonSerializer.SerializeToElement(new { });
        var typeIds = new List<GraphNodeType>();
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in fx.Graph.Nodes.Select(node => node.Category).Distinct())
        {
            var typeId = CategoryToType(category);
            typeIds.Add(new GraphNodeType(typeId, 1, "automations", typeId, emptyConfig,
                [new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true),
                 new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true)]));
            profileIds.Add(typeId);
        }
        var schemas = new NodeGraphSchemaRegistry(typeIds,
            [new GraphCapabilityProfile("automations", profileIds, profileIds)]);

        var nodes = new List<GraphNode>();
        for (var i = 0; i < fx.Graph.Nodes.Count; i++)
        {
            var node = fx.Graph.Nodes[i];
            var typeId = CategoryToType(node.Category);
            var inPort = Guid.Parse($"10000000-0000-0000-0000-{(i + 1):D12}");
            var outPort = Guid.Parse($"20000000-0000-0000-0000-{(i + 1):D12}");
            nodes.Add(new GraphNode(node.Id, typeId, 1, "automations", typeId, null, emptyConfig,
                [new GraphPort(inPort, "in", GraphPortDirection.Input, "flow", true),
                 new GraphPort(outPort, "out", GraphPortDirection.Output, "flow", true)]));
        }
        var byId = nodes.ToDictionary(node => node.NodeId);
        var connections = new List<GraphConnection>();
        var conn = 0;
        foreach (var edge in fx.Graph.Edges)
        {
            var fromOut = byId[edge.FromNodeId].Ports.First(port => port.Direction == GraphPortDirection.Output);
            var toIn = byId[edge.ToNodeId].Ports.First(port => port.Direction == GraphPortDirection.Input);
            connections.Add(new GraphConnection(Guid.Parse($"30000000-0000-0000-0000-{++conn:D12}"), fromOut.PortId, toIn.PortId));
        }
        var graphDoc = new GraphDocument(1, fx.GraphId, activeRevision, "automations", GraphRevisionState.Active,
            Array.AsReadOnly(nodes.ToArray()), Array.AsReadOnly(connections.ToArray()));
        return (schemas, graphDoc);
    }

    private static string CategoryToType(string category) => category switch
    {
        "Trigger" => "trigger",
        "Action" => "action",
        "Wait" => "wait",
        "Approval" => "approval",
        _ => category.ToLowerInvariant()
    };

    private static IEnumerable<AutomationNodeGraphBinding> BuildBindings()
    {
        yield return new AutomationNodeGraphBinding("trigger", 1, "automations", "trigger",
            _ => true, node => new AutomationGraphNodeDefinition(node.NodeId, "Trigger", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) { Title = "Trigger" });
        yield return new AutomationNodeGraphBinding("wait", 1, "automations", "wait",
            _ => true, node => new AutomationGraphNodeDefinition(node.NodeId, "Wait", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["until"] = "2000-01-01T00:00:00Z" })
                { Title = "Wait" });
        yield return new AutomationNodeGraphBinding("action", 1, "automations", "action",
            _ => true, node => new AutomationGraphNodeDefinition(node.NodeId, "Action", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) { Title = "Action" });
        yield return new AutomationNodeGraphBinding("approval", 1, "automations", "approval",
            _ => true, node => new AutomationGraphNodeDefinition(node.NodeId, "Approval", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) { Title = "Approval" });
    }

    private sealed class StoredGraphRepository : IVersionedNodeGraphRepository
    {
        private readonly GraphDocument _graph;
        private readonly Guid _graphId;
        private readonly long _activeRevision;
        private readonly long[] _historicalRevisions;
        public StoredGraphRepository(GraphDocument graph, Guid graphId, long activeRevision, long[] historicalRevisions)
        { _graph = graph; _graphId = graphId; _activeRevision = activeRevision; _historicalRevisions = historicalRevisions; }
        public Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken ct)
        {
            if (graphId != _graphId) return Task.FromResult<OwnedGraphDefinition?>(null);
            var active = _graph with { Revision = _activeRevision, State = GraphRevisionState.Active };
            var history = _historicalRevisions.Select(revision => _graph with { Revision = revision, State = GraphRevisionState.Active }).ToArray();
            var owned = new OwnedGraphDefinition("automations", "entity", _graph with { Revision = _activeRevision + 1 }, active)
            { ActivatedRevisions = Array.AsReadOnly(history) };
            return Task.FromResult<OwnedGraphDefinition?>(owned);
        }
        public Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expected, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryActivateAsync(Guid graphId, long expected, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield return response; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => Task.FromResult(response);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }
}
