using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeOriginalSetupBrokerOccurrenceTests
{
    [Fact]
    public async Task Actual_cancelled_prepared_gate_and_independent_post_callback_OCE_remain_faulted_with_both_occurrences()
    {
        var root = Path.Combine(Path.GetTempPath(), "home-setup-occurrence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var actor = await profiles.GetCurrentAsync(lifetime.Token) ?? throw new InvalidOperationException("Actual Home profile unavailable.");
        var permissions = new HomePermissionTrustService(store, new HomeCapabilityCatalogueInitializationActionPolicySource().TryGet);
        var resources = new ResourceAuthorizationService(profiles, []);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        var prepared = broker.PrepareReviewForActor(actor, "assistants", HomeCapabilityCatalogueInitializationWriteSource.WriteAction,
            [new ResourceScope("canonical.capabilities.setup", "negative-no-mutation", "fixture", ResourceAccess.Write)],
            JsonSerializer.SerializeToElement(new { OperationId = Guid.NewGuid() }), "No operation can pass this cancelled gate", null, "source-occurrence-fixture");
        var foreign = new OperationCanceledException("Independent synchronous source callback", CancellationToken.None);
        Task? cancelledGate = null; var raw = new List<Task>(); var gate = new object(); var injected = 0;
        void Retain(Task actual)
        {
            lock (gate) { raw.Add(actual); if (actual.IsCanceled) cancelledGate ??= actual; }
        }
        void Scope(Action body)
        {
            body();
            if (cancelledGate is not null && Interlocked.Exchange(ref injected, 1) == 0) throw foreign;
        }
        var original = broker.AuthorizePreparedReviewWithinOriginalSourceAsync(prepared, Scope, Retain,
            cancelled.Token, originalPermissionSources: true);
        Exception? expected = null; var failures = new List<Exception>();
        try
        {
            expected = await Assert.ThrowsAnyAsync<Exception>(() => original);
            Assert.True(original.IsFaulted); Assert.False(original.IsCanceled); Assert.NotNull(cancelledGate);
            Assert.True(cancelledGate!.IsCanceled);
            Assert.Contains(foreign, References(expected));
            Assert.Contains(References(expected), cause => cause is OperationCanceledException occurrence &&
                occurrence.CancellationToken == cancelled.Token);
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: lifetime.Token)).PendingRequests);
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            // Join the SAME body first, then its complete retained child cohort. The
            // exact controlled cancelled gate is the only expected cancelled Task.
            try { await original; } catch (Exception cause) { if (expected is null) failures.Add(original.Exception ?? cause); }
            Task[] children; lock (gate) children = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            var known = expected is null ? new HashSet<Exception>(ReferenceEqualityComparer.Instance)
                : new HashSet<Exception>(References(expected), ReferenceEqualityComparer.Instance);
            foreach (var child in children)
                try { await child; }
                catch (Exception cause)
                {
                    if (ReferenceEquals(child, cancelledGate) && child.IsCanceled && cause is OperationCanceledException own &&
                        own.CancellationToken == cancelled.Token) continue;
                    if (!AllKnown(child.Exception ?? cause, known)) failures.Add(child.Exception ?? cause);
                }
        }
        // This deliberate unknown-source negative preserves its exclusive fixture;
        // joining an expected test occurrence is not a production retirement receipt.
        if (failures.Count != 0) throw new AggregateException("Actual mixed Home source assertions/joins failed; fixture retained at " + root, failures);
    }
    private static IEnumerable<Exception> References(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var actual in References(child)) yield return actual;
    }
    private static bool AllKnown(Exception cause, HashSet<Exception> known) => known.Contains(cause) ||
        cause is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(child => AllKnown(child, known));
}
