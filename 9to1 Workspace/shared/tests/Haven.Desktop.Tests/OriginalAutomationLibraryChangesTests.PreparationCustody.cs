#if !ANDROID
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    // The READ/profile/store are real. Only the finite non-authority preparation
    // product is injected to exercise its foreign getter; it issues no Home claim,
    // SQL intent or permission and never dispatches the real process writer.
    [Theory(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    [InlineData(false)]
    [InlineData(true)]
    public Task Automation_preparation_product_getter_keeps_physical_guard_when_it_restores_a_neutral_context(bool fromDeclined) =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var rows = await SeedSavedAutomationLibrary(rig, repository, 2);
            await WithActualAutomationChanges(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source), async graph =>
            {
                OriginalAutomationLibraryBindings? bindings = null;
                var neutral = ExecutionContext.Capture() ?? throw new InvalidOperationException("No fixture execution context.");
                var getterRefusals = new List<Exception>();
                var getter = new ReentrantPreparation(() =>
                {
                    ExecutionContext.Run(neutral, _ =>
                    {
                        try { _ = bindings!.CloseAndDrainAsync(); }
                        catch (InvalidOperationException refused) { getterRefusals.Add(refused); }
                    }, null);
                }, fromDeclined);
                var actualSource = new PreparationGetterSource(graph.Writer, getter);
                bindings = new(source, rig.Home.Profiles, actualSource);
                var failures = new List<Exception>(); Task? close = null;
                try
                {
                    await rig.Keep(bindings.InitializeAsync());
                    Assert.True(bindings.TryGetValue("Rows", out var actualRows));
                    var row = Assert.IsAssignableFrom<IReadOnlyList<OriginalAutomationLibraryBindings.LibraryRow>>(actualRows)
                        .Single(value => value.Name == rows[1].Name);
                    await rig.Keep(bindings.DispatchAsync("select", row.Target, rig.Token).AsTask());
                    var before = await graph.SelectedRaw(rows[1].Id);
                    var actual = rig.Keep(bindings.DispatchAsync("disable", null, rig.Token).AsTask()); await actual;
                    Assert.True(actual.IsCompletedSuccessfully); Assert.True(getter.GetterCalls > 0);
                    Assert.Equal(getter.GetterCalls, getterRefusals.Count);
                    Assert.Null(bindings.OriginalClose); Assert.Null(bindings.OriginalChangeObservation);
                    Assert.Equal(before, await graph.SelectedRaw(rows[1].Id));
                    Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
                }
                catch (Exception cause) { failures.Add(cause); }
                finally
                {
                    try { close = rig.Keep(bindings.CloseAndDrainAsync()); await close; Assert.Same(close, bindings.OriginalClose); }
                    catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
                }
                if (failures.Count != 0) throw new AggregateException("The actual getter invocation and all accepted binding originals are retained.", failures);
            });
        });

    private sealed class ReentrantPreparation(Action callback, bool fromDeclined) : ICanonicalAutomationDefinitionOriginalProcessPreparation
    {
        internal int GetterCalls;
        public bool IsDeclinedBeforeEffect
        { get { if (fromDeclined) { GetterCalls++; callback(); } return true; } }
        public ICanonicalAutomationDefinitionOriginalChangeObservation? Observation
        { get { if (!fromDeclined) { GetterCalls++; callback(); } return null; } }
    }
    private sealed class PreparationGetterSource(ICanonicalAutomationDefinitionOriginalProcessSource actual,
        ICanonicalAutomationDefinitionOriginalProcessPreparation preparation) : ICanonicalAutomationDefinitionOriginalProcessSource
    {
        public Task<ICanonicalAutomationDefinitionOriginalProcessPreparation> PrepareOriginalChangeProcessWithinSourceAsync(
            ICanonicalAutomationLibraryOriginalObservation observation, AutomationOwnerRead<AutomationDefinition> row,
            CanonicalAutomationOriginalChangeKind kind, Guid operation, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<ICanonicalAutomationDefinitionOriginalProcessPreparation>? raw = null;
            scope(() => { token.ThrowIfCancellationRequested(); raw = Task.FromResult(preparation); retain(raw); });
            return raw ?? throw new InvalidOperationException("The actual finite fixture source was not invoked.");
        }
        public bool IsIssuedOriginalProcessPreparation(ICanonicalAutomationDefinitionOriginalProcessPreparation same) => ReferenceEquals(preparation, same);
        public bool IsIssuedOriginalChangeObservation(ICanonicalAutomationDefinitionOriginalChangeObservation same) => actual.IsIssuedOriginalChangeObservation(same);
        public void RequestOriginalPendingReviewWithdrawals() => actual.RequestOriginalPendingReviewWithdrawals();
        public Task<ICanonicalAutomationDefinitionOriginalChangeObservation> StartOriginalChangeProcessWithinSourceAsync(
            ICanonicalAutomationDefinitionOriginalChangeIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            actual.StartOriginalChangeProcessWithinSourceAsync(intent, scope, retain, token);
        public Task<ICanonicalAutomationDefinitionOriginalChangeIntent> PrepareOriginalChangeWithinSourceAsync(
            ICanonicalAutomationLibraryOriginalObservation observation, AutomationOwnerRead<AutomationDefinition> row,
            CanonicalAutomationOriginalChangeKind kind, Guid operation, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            actual.PrepareOriginalChangeWithinSourceAsync(observation, row, kind, operation, scope, retain, token);
        public bool IsIssuedOriginalChangeIntent(ICanonicalAutomationDefinitionOriginalChangeIntent same) => actual.IsIssuedOriginalChangeIntent(same);
        public string GetOriginalChangeIntentDigest(ICanonicalAutomationDefinitionOriginalChangeIntent same) => actual.GetOriginalChangeIntentDigest(same);
        public Task RevalidateOriginalChangeIntentWithinSourceAsync(ICanonicalAutomationDefinitionOriginalChangeIntent same, Action<Action> scope,
            Action<Task> retain, CancellationToken token) => actual.RevalidateOriginalChangeIntentWithinSourceAsync(same, scope, retain, token);
        public void DemandOriginalChangeCommit(ICanonicalAutomationDefinitionOriginalChangeIntent same) => actual.DemandOriginalChangeCommit(same);
        public Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> CommitOriginalChangeWithinSourceAsync(
            ICanonicalAutomationDefinitionOriginalChangeIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            actual.CommitOriginalChangeWithinSourceAsync(intent, scope, retain, token);
        public bool IsOriginalAtomicChangeTask(ICanonicalAutomationDefinitionOriginalChangeIntent same,
            Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> task) => actual.IsOriginalAtomicChangeTask(same, task);
        public bool IsOwnedOriginalChangeAcknowledgment(ICanonicalAutomationDefinitionOriginalChangeIntent same,
            ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment, Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> task) =>
            actual.IsOwnedOriginalChangeAcknowledgment(same, acknowledgment, task);
        public bool IsOwnedOriginalChangeNativeRelease(ICanonicalAutomationDefinitionOriginalChangeIntent same,
            Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? task) => actual.IsOwnedOriginalChangeNativeRelease(same, task);
        public bool IsAcknowledgedOriginalChangeSourceRefusal(Task same) => actual.IsAcknowledgedOriginalChangeSourceRefusal(same);
    }
}
#endif
