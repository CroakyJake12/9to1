using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenerativeUiContractTests
{
    [Fact]
    public void TrustedHavenUiDocumentWithStableIdsIsAccepted()
    {
        var document = Document(Button("calculate", GenUiRouteKind.Local, CapabilityRiskClass.Low));

        Assert.Empty(GenerativeUiContractValidator.Validate(document));
    }

    [Fact]
    public void ArbitraryRenderingAndConsequentialLocalActionsAreRejected()
    {
        var root = Button("run", GenUiRouteKind.Local, CapabilityRiskClass.Consequential) with
        {
            ComponentType = "RawHtml",
            Properties = new Dictionary<string, JsonElement>
            {
                ["javascript"] = JsonSerializer.SerializeToElement("doSomething()")
            }
        };

        var errors = GenerativeUiContractValidator.Validate(Document(root));

        Assert.Contains(errors, error => error.Contains("trusted HavenUI", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("forbidden", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("cannot own consequential", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateComponentIdentityIsRejected()
    {
        var child = Text("same", "One");
        var root = new GenUiComponent(
            "same", "HavenStack", EmptyProperties(), [], [child]);

        Assert.Contains(
            GenerativeUiContractValidator.Validate(Document(root)),
            error => error.Contains("Duplicate component ID", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleActionsRequireAndResolveAnExplicitDeclaredSelector()
    {
        var component = Button("multi", GenUiRouteKind.Local, CapabilityRiskClass.Low) with
        {
            Actions =
            [
                new GenUiActionBinding("first", GenUiRouteKind.Local, "first", CapabilityRiskClass.Low, false),
                new GenUiActionBinding("second", GenUiRouteKind.Local, "second", CapabilityRiskClass.Low, false)
            ]
        };

        Assert.Contains(
            GenerativeUiContractValidator.Validate(Document(component)),
            error => error.Contains("requires an actionId selector", StringComparison.Ordinal));

        component = component with
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["label"] = JsonSerializer.SerializeToElement("Run"),
                ["actionId"] = JsonSerializer.SerializeToElement("second")
            }
        };

        Assert.Empty(GenerativeUiContractValidator.Validate(Document(component)));
        Assert.Equal("second", GenerativeUiContractValidator.SelectActionBinding(component)!.ActionId);
    }

    [Fact]
    public async Task DeniedPermissionNeverDispatchesActionOrAppliesPatches()
    {
        var document = Document(Button("delete", GenUiRouteKind.Capability, CapabilityRiskClass.Consequential) with
        {
            Actions = [new GenUiActionBinding("delete", GenUiRouteKind.Capability, "boards.delete", CapabilityRiskClass.Consequential, true)]
        });
        var store = new GenUiInstanceStore();
        store.Register(document);
        var called = false;
        var capability = new GenUiCapabilityEventHandler();
        capability.Register("boards.delete", (evt, _, _) =>
        {
            called = true;
            return Task.FromResult(GenerativeUiEventRouter.Result(
                evt,
                GenUiActionStatus.Completed,
                "Deleted",
                patches: [new GenUiStatePatch(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace,
                    "state", "result", JsonSerializer.SerializeToElement(1), DateTimeOffset.UtcNow)]));
        });
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([capability], audit, store, new DeniedPermissionDecisionEngine());
        var semanticEvent = Event(document.Origin, "delete", "delete");

        var result = await router.RouteAsync(semanticEvent, document.Root.Actions.Single(), CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Denied, result.Status);
        Assert.False(called);
        Assert.Equal(0, store.TryGet(document.Origin.InstanceId)!.State["result"].GetInt32());
        Assert.Single(audit.Snapshot());
        Assert.Equal(GenUiActionStatus.Denied, audit.Snapshot()[0].Status);
    }

    [Fact]
    public async Task PermissionSensitiveDestinationFailsClosedWithoutPermissionService()
    {
        var document = Document(Button("save", GenUiRouteKind.App, CapabilityRiskClass.Low) with
        {
            Actions = [new GenUiActionBinding("save", GenUiRouteKind.App, "write.save", CapabilityRiskClass.Low, false)]
        });
        var store = new GenUiInstanceStore();
        store.Register(document);
        var called = false;
        var app = new GenUiAppEventHandler();
        app.Register("write.save", (_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("Must not run without the permission service.");
        });
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([app], audit, store);

        var result = await router.RouteAsync(
            Event(document.Origin, "save", "save"),
            document.Root.Actions.Single(),
            CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Unavailable, result.Status);
        Assert.False(called);
        Assert.Single(audit.Snapshot());
    }

    [Fact]
    public async Task UndeclaredActionCannotBeDispatched()
    {
        var document = Document(Button("save", GenUiRouteKind.Local, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var called = false;
        var local = new GenUiLocalActionRegistry();
        local.Register("attacker.target", (_, _) =>
        {
            called = true;
            throw new InvalidOperationException("Undeclared action must not run.");
        });
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([local], audit, store);
        var forged = document.Root.Actions.Single() with { TargetKey = "attacker.target" };

        var result = await router.RouteAsync(Event(document.Origin, "save", "save"), forged, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Denied, result.Status);
        Assert.False(called);
        Assert.Single(audit.Snapshot());
    }

    [Fact]
    public async Task NonCompletedHandlerResultCannotApplyStatePatches()
    {
        var document = Document(Button("calculate", GenUiRouteKind.Local, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var local = new GenUiLocalActionRegistry();
        local.Register("calculator.evaluate", (evt, _) => Task.FromResult(GenerativeUiEventRouter.Result(
            evt, GenUiActionStatus.Failed, "Calculation failed.",
            patches: [new GenUiStatePatch(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace,
                "state", "result", JsonSerializer.SerializeToElement(99), DateTimeOffset.UtcNow)])));
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(
            Event(document.Origin, "calculate", "calculate"), document.Root.Actions.Single(), CancellationToken.None));
        Assert.Equal(0, store.TryGet(document.Origin.InstanceId)!.State["result"].GetInt32());
    }

    [Fact]
    public async Task LocalEventRoutesWithoutModelAndPatchesItsOriginatingInstanceOnce()
    {
        var document = Document(Button("calculate", GenUiRouteKind.Local, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var local = new GenUiLocalActionRegistry();
        var patchId = Guid.NewGuid();
        local.Register("calculator.evaluate", (semanticEvent, _) => Task.FromResult(
            GenerativeUiEventRouter.Result(
                semanticEvent,
                GenUiActionStatus.Completed,
                "Calculated locally.",
                JsonSerializer.SerializeToElement(new { result = 4 }),
                [new GenUiStatePatch(
                    patchId,
                    semanticEvent.Origin.InstanceId,
                    GenUiPatchOperation.Replace,
                    "state",
                    "result",
                    JsonSerializer.SerializeToElement(4),
                    DateTimeOffset.UtcNow)])));
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([local], audit, store);
        var semanticEvent = Event(document.Origin, "calculate", "calculate");
        var binding = document.Root.Actions.Single();

        var result = await router.RouteAsync(semanticEvent, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        Assert.Equal(4, store.TryGet(document.Origin.InstanceId)!.State["result"].GetInt32());
        Assert.False(store.ApplyPatch(result.Patches.Single()));
        Assert.Single(audit.Snapshot());
    }

    [Fact]
    public async Task MissingDestinationReturnsStructuredUnavailableResult()
    {
        var document = Document(Button("explain", GenUiRouteKind.Agent, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var router = new GenerativeUiEventRouter([], new BoundedGenUiEventAuditSink(), store);
        var semanticEvent = Event(document.Origin, "explain", "explain");

        var result = await router.RouteAsync(semanticEvent, document.Root.Actions.Single(), CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Unavailable, result.Status);
        Assert.Equal(semanticEvent.EventId, result.EventId);
        Assert.Equal(document.Origin, result.Origin);
    }

    private static GenUiDocument Document(GenUiComponent root)
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        return new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            origin,
            "Test",
            "chat",
            root,
            new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(0) },
            DateTimeOffset.UtcNow);
    }

    private static GenUiComponent Button(string id, GenUiRouteKind route, CapabilityRiskClass risk) => new(
        id,
        "HavenButton",
        new Dictionary<string, JsonElement> { ["label"] = JsonSerializer.SerializeToElement("Run") },
        [new GenUiActionBinding(id, route, route == GenUiRouteKind.Local ? "calculator.evaluate" : "agent.explain", risk, RequiresPermission: false)],
        []);

    private static GenUiComponent Text(string id, string value) => new(
        id,
        "HavenText",
        new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement(value) },
        [],
        []);

    private static GenUiEvent Event(GenUiOrigin origin, string component, string action) => new(
        Guid.NewGuid(),
        GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow,
        origin,
        component,
        action,
        null,
        null,
        null,
        JsonSerializer.SerializeToElement(new { }),
        GenUiEventSource.User,
        "User activated a generated control.");

    private static IReadOnlyDictionary<string, JsonElement> EmptyProperties() =>
        new Dictionary<string, JsonElement>();

    private sealed class DeniedPermissionDecisionEngine : IPermissionDecisionEngine
    {
        public HavenPermissionPolicy Policy => HavenPermissionPolicy.AlwaysAsk;
        public IReadOnlySet<string> Grants => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PermissionDecision Evaluate(string scope, CapabilityRiskClass risk, bool requiresPermission, string reason) =>
            new(PermissionDecisionKind.Denied, scope, "This action is denied by policy.");

        public void SetPolicy(HavenPermissionPolicy policy) => throw new NotSupportedException();
        public void Grant(string scope) => throw new NotSupportedException();
        public void Revoke(string scope) => throw new NotSupportedException();
    }
}
