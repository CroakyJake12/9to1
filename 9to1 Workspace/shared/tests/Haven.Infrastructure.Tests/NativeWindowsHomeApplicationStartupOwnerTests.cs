using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class NativeWindowsHomeApplicationStartupOwnerTests
{
    [NonWindowsFact]
    public async Task Actual_missing_platform_stays_unready_without_seed_and_same_owner_close_is_joined()
    {
        using var controls = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var locator = Path.Combine(Path.GetTempPath(), "home-native-startup-uncreated-" + Guid.NewGuid().ToString("N") + ".json");
        var owner = new NativeWindowsHomeApplicationStartupOwner(locator,
            new("native-test-absence", "candidate-only", [new("home.core", 1), new("home.state", 1)]), controls.Token);
        var raw = new List<Task>(); var connect = owner.ConnectWithinOriginalSourceAsync(body => body(), actual => raw.Add(actual), controls.Token);
        Task? close = null; Exception? body = null;
        try
        {
            var actual = await connect;
            Assert.Equal(HomeNativeStartupState.Unready, actual.State); Assert.False(actual.CanStartNormally);
            Assert.Null(owner.OriginalStartup); Assert.False(File.Exists(locator));
            Assert.Same(connect, owner.ConnectWithinOriginalSourceAsync(callback => callback(), _ => { }, controls.Token));
            close = owner.CloseAndDrainOriginalAsync(); await close;
            Assert.Same(close, owner.OriginalClose); Assert.Same(close, owner.CloseAndDrainOriginalAsync());
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.RefreshWithinOriginalSourceAsync(callback => callback(), _ => { }, controls.Token); });
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            try { close ??= owner.CloseAndDrainOriginalAsync(); } catch (Exception cause) { body = body is null ? cause : new AggregateException(body, cause); }
            var errors = new List<Exception>(); if (body is not null) errors.Add(body);
            var all = raw.Append(connect).ToList(); if (close is not null) all.Add(close);
            foreach (var same in all.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await same; } catch (Exception cause) { errors.Add(same.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual unavailable startup owner/original cleanup failed.", errors);
        }
    }
    [NonWindowsFact]
    public async Task Close_waits_actual_late_CTS_publication_and_joins_once_cancel_before_connect_settlement()
    {
        using var controls = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var beforeCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permitCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var permitReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var locator = Path.Combine(Path.GetTempPath(), "home-native-startup-late-" + Guid.NewGuid().ToString("N") + ".json");
        var owner = new NativeWindowsHomeApplicationStartupOwner(locator,
            new("native-test-late-absence", "candidate-only", [new("home.core", 1)]), controls.Token);
        var raw = new List<Task>(); var sync = new object(); var calls = 0;
        void Retain(Task same) { lock (sync) raw.Add(same); }
        void Scope(Action body)
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                // This is the actual owner's CTS-creation body, after its driver publication.
                beforeCreation.TrySetResult(); permitCreation.Task.GetAwaiter().GetResult();
                body(); afterCreation.TrySetResult(); permitReturn.Task.GetAwaiter().GetResult();
            }
            else body();
        }
        var connect = owner.ConnectWithinOriginalSourceAsync(Scope, Retain, controls.Token);
        Task? close = null, cancellation = null, actualCancel = null; Exception? bodyFailure = null;
        HashSet<Exception>? expectedConnectLeaves = null;
        try
        {
            if (!ReferenceEquals(await Task.WhenAny(beforeCreation.Task, connect).WaitAsync(controls.Token), beforeCreation.Task))
                await connect; // Surface the SAME early driver fault before pending assertions.
            await beforeCreation.Task.WaitAsync(controls.Token);
            close = owner.CloseAndDrainOriginalAsync(); cancellation = Assert.IsAssignableFrom<Task>(owner.OriginalLifetimeCancellation);
            Assert.False(cancellation.IsCompleted); Assert.False(close.IsCompleted);
            permitCreation.TrySetResult();
            if (!ReferenceEquals(await Task.WhenAny(afterCreation.Task, connect).WaitAsync(controls.Token), afterCreation.Task))
                await connect;
            await afterCreation.Task.WaitAsync(controls.Token);
            // The actual CTS is now captured, while the caller's creation postguard is held.
            await cancellation.WaitAsync(controls.Token); actualCancel = Assert.IsAssignableFrom<Task>(owner.OriginalLifetimeCancel);
            await actualCancel.WaitAsync(controls.Token);
            Assert.False(connect.IsCompleted); Assert.False(close.IsCompleted);
            Assert.Same(close, owner.CloseAndDrainOriginalAsync());
            Assert.Same(cancellation, owner.OriginalLifetimeCancellation); Assert.Same(actualCancel, owner.OriginalLifetimeCancel);
            permitReturn.TrySetResult();
            var connectFailure = await Assert.ThrowsAnyAsync<Exception>(() => connect);
            Assert.True(connect.IsFaulted); // Unknown productive cancellation remains a retained fault.
            expectedConnectLeaves = Leaves(connect.Exception!);
            Assert.NotEmpty(expectedConnectLeaves); Assert.NotNull(connectFailure);
            _ = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(close.IsFaulted);
            Assert.True(OnlySameLeaves(close.Exception!, expectedConnectLeaves));
            Assert.Null(owner.OriginalStartup); Assert.False(File.Exists(locator));
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally
        {
            // Release actual caller gates before independently joining every accepted original.
            permitCreation.TrySetResult(); permitReturn.TrySetResult();
            var errors = new List<Exception>(); if (bodyFailure is not null) errors.Add(bodyFailure);
            try { close ??= owner.CloseAndDrainOriginalAsync(); } catch (Exception cause) { errors.Add(cause); }
            Task[] retained; lock (sync) retained = raw.ToArray();
            var all = retained.Append(connect).ToList();
            if (close is not null) all.Add(close);
            if (cancellation is not null) all.Add(cancellation);
            if (actualCancel is not null) all.Add(actualCancel);
            foreach (var same in all.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await same; }
                catch (Exception cause)
                {
                    // Only the independently asserted SAME original/close occurrences qualify.
                    if ((ReferenceEquals(same, connect) || ReferenceEquals(same, close)) &&
                        expectedConnectLeaves is not null && same.Exception is { } group && OnlySameLeaves(group, expectedConnectLeaves)) continue;
                    errors.Add(same.Exception ?? cause);
                }
            if (errors.Count != 0) throw new AggregateException("Late actual startup lifetime/control cleanup failed.", errors);
        }
        static HashSet<Exception> Leaves(Exception original)
        {
            var leaves = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            void Visit(Exception same)
            {
                if (same is AggregateException group && group.InnerExceptions.Count != 0)
                    foreach (var child in group.InnerExceptions) Visit(child);
                else leaves.Add(same);
            }
            Visit(original); return leaves;
        }
        static bool OnlySameLeaves(Exception same, HashSet<Exception> originals)
        {
            if (same is AggregateException group)
                return group.InnerExceptions.Count != 0 && group.InnerExceptions.All(child => OnlySameLeaves(child, originals));
            return originals.Contains(same);
        }
    }
    [Fact]
    public async Task No_admitted_connect_resolves_the_original_no_CTS_receipt_without_native_startup_or_cancel_replay()
    {
        using var controls = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var locator = Path.Combine(Path.GetTempPath(), "home-native-startup-unused-" + Guid.NewGuid().ToString("N") + ".json");
        var owner = new NativeWindowsHomeApplicationStartupOwner(locator,
            new("native-test-unused", "candidate-only", [new("home.core", 1)]), controls.Token);
        var close = owner.CloseAndDrainOriginalAsync(); var errors = new List<Exception>();
        try
        {
            await close.WaitAsync(controls.Token);
            var cancellation = Assert.IsAssignableFrom<Task>(owner.OriginalLifetimeCancellation);
            await cancellation.WaitAsync(controls.Token);
            Assert.Null(owner.OriginalLifetimeCancel); Assert.Null(owner.OriginalStartup); Assert.False(File.Exists(locator));
            Assert.Same(close, owner.CloseAndDrainOriginalAsync()); Assert.Same(cancellation, owner.OriginalLifetimeCancellation);
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.ConnectWithinOriginalSourceAsync(body => body(), _ => { }, controls.Token); });
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Unused actual startup owner/body cleanup failed.", errors);
    }
    [NonWindowsFact]
    public async Task Late_saved_callback_after_actual_healthy_operation_pruning_remains_an_owner_close_fault()
    {
        using var controls = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var locator = Path.Combine(Path.GetTempPath(), "home-native-startup-late-callback-" + Guid.NewGuid().ToString("N") + ".json");
        var owner = new NativeWindowsHomeApplicationStartupOwner(locator,
            new("native-test-late-callback", "candidate-only", [new("home.core", 1)]), controls.Token);
        Action? saved = null; var raw = new List<Task>(); var sync = new object();
        void Retain(Task same) { lock (sync) raw.Add(same); }
        void Scope(Action body) { saved ??= body; body(); }
        var connect = owner.ConnectWithinOriginalSourceAsync(Scope, Retain, controls.Token);
        Task? refresh = null, close = null; var failures = new List<Exception>();
        var expected = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        try
        {
            Assert.Equal(HomeNativeStartupState.Unready, (await connect).State);
            // This actual admission independently joins/prunes the healthy Connect operation.
            // Absence provides no connection for the fresh Refresh body, whose real fault stays retained.
            refresh = owner.RefreshWithinOriginalSourceAsync(body => body(), Retain, controls.Token);
            _ = await Assert.ThrowsAnyAsync<Exception>(() => refresh);
            Assert.True(refresh.IsFaulted); Remember(refresh.Exception!);
            var actualSaved = Assert.IsAssignableFrom<Action>(saved);
            var late = Assert.Throws<InvalidOperationException>(() => actualSaved()); expected.Add(late);
            var refused = Assert.Throws<AggregateException>(() =>
            { _ = owner.RefreshWithinOriginalSourceAsync(body => body(), Retain, controls.Token); });
            Assert.True(Contains(refused, late));
            close = owner.CloseAndDrainOriginalAsync();
            _ = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(close.IsFaulted); Assert.True(Contains(close.Exception!, late));
            Assert.True(OnlyExpected(close.Exception!));
            Assert.Same(close, owner.CloseAndDrainOriginalAsync()); Assert.False(File.Exists(locator));
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { close ??= owner.CloseAndDrainOriginalAsync(); } catch (Exception cause) { failures.Add(cause); }
            Task[] retained; lock (sync) retained = raw.ToArray(); var all = retained.Append(connect).ToList();
            if (refresh is not null) all.Add(refresh); if (close is not null) all.Add(close);
            foreach (var same in all.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await same; }
                catch (Exception cause)
                {
                    if ((ReferenceEquals(same, refresh) || ReferenceEquals(same, close)) &&
                        same.Exception is { } group && OnlyExpected(group)) continue;
                    failures.Add(same.Exception ?? cause);
                }
        }
        if (failures.Count != 0) throw new AggregateException("Actual saved startup callback/source/close custody failed.", failures);
        void Remember(Exception same)
        {
            if (same is AggregateException group && group.InnerExceptions.Count != 0)
                foreach (var child in group.InnerExceptions) Remember(child);
            else expected.Add(same);
        }
        bool OnlyExpected(Exception same) => same is AggregateException group
            ? group.InnerExceptions.Count != 0 && group.InnerExceptions.All(OnlyExpected) : expected.Contains(same);
        static bool Contains(Exception same, Exception actual) => ReferenceEquals(same, actual) ||
            same is AggregateException group && group.InnerExceptions.Any(child => Contains(child, actual));
    }
    private sealed class NonWindowsFactAttribute : FactAttribute
    { public NonWindowsFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "This absence control supplies no enrolled installed Root/Windows proof."; } }
}
