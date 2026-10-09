using System.Collections;
using System.Reflection;
using Haven.Infrastructure.Native.Windows;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual platform absence and escaped finite caller custody; no installed
/// Windows host, signer, package, Root response, route or permission is fabricated.</summary>
public sealed class NativeWindowsHomeRootHostVerifierOriginalScopeTests
{
    private static readonly List<object> RetainedFailedOwners = [];

    [NonWindowsFact]
    public async Task Joined_and_pruned_absence_keeps_its_actual_late_callback_failure_in_verifier_custody()
    {
        var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var file = Path.Combine(Path.GetTempPath(), "root-host-scoped-uncreated-" + Guid.NewGuid().ToString("N") + ".json");
        var owner = new NativeWindowsHomeRootHostVerifier(file, lifetime.Token);
        var receipts = new List<Task>(); var receiptGate = new object();
        Action? saved = null; Task? close = null; Exception? primary = null;
        InvalidOperationException? exactLateCause = null;
        // An expected failed owner remains rooted with its real lifetime and receipts.
        lock (RetainedFailedOwners) RetainedFailedOwners.Add(new object[] { owner, lifetime, receipts, file });
        void Retain(Task same) { lock (receiptGate) if (!receipts.Any(raw => ReferenceEquals(raw, same))) receipts.Add(same); }
        try
        {
            var first = owner.ObserveOriginalEndpointWithinSourceAsync(callback =>
            { saved ??= callback; callback(); }, Retain, lifetime.Token);
            Retain(first); Assert.Null(await first);
            var firstInvocation = Assert.Single(CurrentInvocations(owner));
            var second = owner.ObserveOriginalEndpointWithinSourceAsync(callback => callback(), Retain, lifetime.Token);
            Retain(second); Assert.Null(await second);
            var secondInvocation = Assert.Single(CurrentInvocations(owner));
            Assert.NotSame(firstInvocation, secondInvocation); // The real healthy first occurrence was pruned.
            var actualSaved = Assert.IsType<Action>(saved);
            exactLateCause = Assert.Throws<InvalidOperationException>(() => actualSaved());
            Assert.Equal("The original host source callback is late, repeated or on another thread.", exactLateCause.Message);
            Assert.Null(await first); Assert.True(first.IsCompletedSuccessfully);
            var admission = Assert.Throws<AggregateException>(() =>
            { _ = owner.ObserveOriginalEndpointWithinSourceAsync(callback => callback(), Retain, lifetime.Token); });
            AssertOnlyKnown(admission, exactLateCause);
            Assert.Same(secondInvocation, Assert.Single(CurrentInvocations(owner))); // No new driver was admitted.
            close = owner.CloseAndDrainOriginalAsync(); Retain(close);
            Exception? observedClose = null;
            try { await close; } catch (Exception cause) { observedClose = cause; }
            Assert.NotNull(observedClose); AssertOnlyKnown(observedClose!, exactLateCause);
            Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
            Assert.Same(close, owner.OriginalClose); Assert.Same(close, owner.CloseAndDrainOriginalAsync());
            AssertOnlyKnown(Assert.IsType<AggregateException>(close.Exception), exactLateCause);
            Assert.False(File.Exists(file));
        }
        catch (Exception cause) { primary = cause; }
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary);
        // Acquire the SAME cached owner close before independently observing every receipt.
        try { close ??= owner.CloseAndDrainOriginalAsync(); Retain(close); }
        catch (Exception cause) { failures.Add(cause); }
        Task[] originals; lock (receiptGate) originals = receipts.ToArray();
        foreach (var same in originals)
            try { await same; }
            catch (Exception cause)
            {
                var payload = same.Exception ?? cause;
                if (ReferenceEquals(same, close) && exactLateCause is not null)
                    try { AssertOnlyKnown(payload, exactLateCause); }
                    catch (Exception unknown) { failures.Add(new AggregateException(unknown, payload)); }
                else failures.Add(payload);
            }
        if (failures.Count != 0) throw new AggregateException("Actual verifier scope fixture/source/close failed.", failures);
    }

    [Fact]
    public async Task Scoped_endpoint_observation_rejects_absent_callbacks_before_admitting_a_driver()
    {
        var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var owner = new NativeWindowsHomeRootHostVerifier(Path.Combine(Path.GetTempPath(),
            "root-host-null-scoped-" + Guid.NewGuid().ToString("N") + ".json"), lifetime.Token);
        Exception? primary = null; Task? close = null;
        try
        {
            Assert.Throws<ArgumentNullException>(() =>
            { _ = owner.ObserveOriginalEndpointWithinSourceAsync(null!, _ => { }, lifetime.Token); });
            Assert.Throws<ArgumentNullException>(() =>
            { _ = owner.ObserveOriginalEndpointWithinSourceAsync(body => body(), null!, lifetime.Token); });
            Assert.Empty(CurrentInvocations(owner));
        }
        catch (Exception cause) { primary = cause; }
        var failures = new List<Exception>(); if (primary is not null) failures.Add(primary);
        try { close = owner.CloseAndDrainOriginalAsync(); }
        catch (Exception cause) { failures.Add(cause); }
        if (close is not null)
            try { await close; Assert.Same(close, owner.OriginalClose); }
            catch (Exception cause) { failures.Add(close.Exception ?? cause); }
        if (failures.Count != 0)
        {
            lock (RetainedFailedOwners) RetainedFailedOwners.Add(new object[] { owner, lifetime, failures });
            throw new AggregateException("Actual null scoped admission/owner close failed.", failures);
        }
        lifetime.Dispose();
    }

    private static object[] CurrentInvocations(NativeWindowsHomeRootHostVerifier owner)
    {
        var field = typeof(NativeWindowsHomeRootHostVerifier).GetField("_originals", BindingFlags.NonPublic | BindingFlags.Instance);
        return Assert.IsAssignableFrom<IList>(Assert.IsAssignableFrom<FieldInfo>(field).GetValue(owner)).Cast<object>().ToArray();
    }
    private static void AssertOnlyKnown(Exception actual, Exception same)
    {
        if (ReferenceEquals(actual, same)) return;
        var group = Assert.IsType<AggregateException>(actual);
        Assert.NotEmpty(group.InnerExceptions);
        foreach (var child in group.InnerExceptions) AssertOnlyKnown(child, same);
    }
    private sealed class NonWindowsFactAttribute : FactAttribute
    { public NonWindowsFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "This actual absence control supplies no installed Windows/Root proof."; } }
}
