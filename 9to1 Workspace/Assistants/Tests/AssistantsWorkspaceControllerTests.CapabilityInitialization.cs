using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

public sealed partial class AssistantsWorkspaceControllerTests
{
    // Maintained injected presentation-custody fixture only. Private issuance
    // proves local observation identity; it creates no Home/SQL/Task authority.
    [Fact]
    public async Task Issued_but_unretained_setup_delivery_is_known_refusal_and_controller_closes_cleanly()
    {
        var saved = Definition("setup", "Setup custody", 3);
        var bridge = new Bridge { List = () => Task.FromResult(new AssistantCatalogueObservation([saved], [])),
            Get = _ => Task.FromResult(saved) };
        var controller = new AssistantsWorkspaceController(bridge);
        AssistantOriginalCapabilityInitializationObservation? delivery = null;
        try
        {
            await controller.InitializeAsync(TestContext.Current.CancellationToken);
            await controller.OpenAssistantAsync(saved.Identity, TestContext.Current.CancellationToken);
            var intent = await bridge.PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(saved.Identity,
                saved.Revision, Guid.NewGuid(), body => body(), _ => { }, TestContext.Current.CancellationToken);
            delivery = await bridge.StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(intent,
                body => body(), _ => { }, TestContext.Current.CancellationToken);
            Assert.True(bridge.IsIssuedOriginalConfigurationInitializationObservation(delivery));
            var actual = controller.WaitOriginalConfigurationCapabilityInitializationAsync(delivery, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => actual);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(actual));
            Assert.Equal(0, bridge.InitializationWaitCalls);
            var close = controller.CloseAndDrainAsync(); await close;
            Assert.Same(close, controller.OriginalClose); Assert.Equal(1, bridge.CloseCalls);
        }
        finally
        {
            await controller.CloseAndDrainAsync();
            if (delivery is not null) await delivery.CloseAndDrainOriginalAsync();
        }
    }

    [Fact]
    public async Task Retained_setup_wait_keeps_faulted_cancellation_and_unknown_raw_sibling_on_close()
    {
        var saved = Definition("setup-mixed", "Setup mixed custody", 2);
        var canceledCause = new OperationCanceledException("Actual faulted delivery cause.");
        var unknown = new IOException("Actual delivery sibling.");
        var raw = new TaskCompletionSource<AssistantOriginalCapabilityInitializationCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([canceledCause, unknown]);
        var bridge = new Bridge { List = () => Task.FromResult(new AssistantCatalogueObservation([saved], [])),
            Get = _ => Task.FromResult(saved), InitializationWait = () => raw.Task };
        var controller = new AssistantsWorkspaceController(bridge);
        var expectedClose = false;
        try
        {
            await controller.InitializeAsync(TestContext.Current.CancellationToken);
            await controller.OpenAssistantAsync(saved.Identity, TestContext.Current.CancellationToken);
            var intent = await controller.PrepareOriginalConfigurationCapabilityInitializationAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
            var delivery = await controller.StartOriginalConfigurationCapabilityInitializationAsync(intent, TestContext.Current.CancellationToken);
            var actual = controller.WaitOriginalConfigurationCapabilityInitializationAsync(delivery, TestContext.Current.CancellationToken);
            var error = await Record.ExceptionAsync(() => actual); Assert.NotNull(error);
            Assert.True(Contains(error, canceledCause)); Assert.True(Contains(error, unknown));
            Assert.True(raw.Task.IsFaulted); Assert.False(raw.Task.IsCanceled);
            Assert.False(controller.IsAcknowledgedOriginalCommandRefusal(actual));
            var close = controller.CloseAndDrainAsync(); expectedClose = true;
            var failed = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.True(Contains(failed, canceledCause)); Assert.True(Contains(failed, unknown));
            Assert.Same(close, controller.CloseAndDrainAsync()); Assert.Equal(1, bridge.InitializationWaitCalls);
            Assert.Equal(1, bridge.InitializationCloseCalls); Assert.Equal(1, bridge.CloseCalls);
        }
        finally
        {
            try { await controller.CloseAndDrainAsync(); }
            catch (AggregateException error) when (expectedClose && Contains(error, canceledCause) && Contains(error, unknown)) { }
        }
    }

    private sealed partial class Bridge : IAssistantOriginalCapabilityInitializationOwner
    {
        private AssistantOriginalCapabilityInitializationIntent? _setupIntent;
        private AssistantOriginalCapabilityInitializationObservation? _setupDelivery;
        private Task? _setupDeliveryClose;
        public Func<Task<AssistantOriginalCapabilityInitializationCompletion>>? InitializationWait;
        public int InitializationWaitCalls, InitializationCloseCalls;

        public Task<AssistantOriginalCapabilityInitializationIntent> PrepareOriginalConfigurationCapabilityInitializationWithinSourceAsync(
            AssistantIdentity identity, long revision, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var actual = Get(identity).GetAwaiter().GetResult();
            if (actual.Revision != revision) throw new InvalidOperationException("Fixture definition changed.");
            _setupIntent = new(this, new object(), actual, new("fixture-actor", "fixture-profile", null, null, "fixture-auth"), operationId, [], false);
            return Task.FromResult(_setupIntent);
        }
        public bool IsIssuedOriginalConfigurationInitializationIntent(AssistantOriginalCapabilityInitializationIntent actual) =>
            ReferenceEquals(actual, _setupIntent);
        public Task RevalidateOriginalConfigurationInitializationIntentWithinSourceAsync(AssistantOriginalCapabilityInitializationIntent actual,
            Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            IsIssuedOriginalConfigurationInitializationIntent(actual) ? Task.CompletedTask : throw new UnauthorizedAccessException("Foreign fixture intent.");
        public Task<AssistantOriginalCapabilityInitializationObservation> StartOriginalConfigurationCapabilityInitializationWithinSourceAsync(
            AssistantOriginalCapabilityInitializationIntent actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            if (!IsIssuedOriginalConfigurationInitializationIntent(actual)) throw new UnauthorizedAccessException("Foreign fixture intent.");
            _setupDelivery = new(this, new object(), actual, _ =>
            {
                InitializationWaitCalls++;
                return InitializationWait?.Invoke() ?? Task.FromResult(new AssistantOriginalCapabilityInitializationCompletion(
                    _setupDelivery!, CapabilityOriginalInitializationCompletionKind.ObservationRetired, [], false));
            }, () => { }, () => { }, () =>
            {
                if (_setupDeliveryClose is null) { InitializationCloseCalls++; _setupDeliveryClose = Task.CompletedTask; }
                return _setupDeliveryClose;
            }, () => _setupDeliveryClose);
            return Task.FromResult(_setupDelivery);
        }
        public bool IsIssuedOriginalConfigurationInitializationObservation(AssistantOriginalCapabilityInitializationObservation actual) =>
            ReferenceEquals(actual, _setupDelivery);
    }
}
