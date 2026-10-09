using Haven.Application;
using Xunit;
namespace Haven.Infrastructure.Tests;

public sealed partial class HomeDeveloperWorkspaceExecutionConsentTests
{
    [Fact] public Task Missing_scoped_native_pin_source_refuses_before_tool_validation_or_Home_review() => RunNativePinControl(async rig =>
    {
        await rig.Prepare(); rig.CurrentBindings = new LegacyBindingSource(rig.Bindings);
        var actual = rig.Acquire(); rig.Expect(actual); rig.ExpectedClose = true;
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(Leaves(error), cause => cause is InvalidOperationException && cause.Message.StartsWith("DEV_EXECUTION_NATIVE_PIN_SETUP_REQUIRED", StringComparison.Ordinal));
        var known = Assert.Single(Leaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance)); rig.ExpectedPinCauses.Add(known);
        Assert.Equal(0, rig.Tools.Validations); Assert.Empty((await rig.Own(rig.Permissions.GetSnapshotAsync())).PendingRequests); Assert.Equal(0, rig.Starts);
    });

    [Fact] public Task Genuine_late_native_pin_after_consent_retirement_is_historically_owned_and_closed() => RunNativePinControl(async rig =>
    {
        var consent = await rig.Accept(); var held = new TaskCompletionSource<IDeveloperWorkspaceOriginalExecutionCommitPin>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Bindings.PinFactory = () => { reached.TrySetResult(); return held.Task; };
        var actual = rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        try { await reached.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(actual.IsCompleted); rig.Source.RequestOriginalExecutionRetirement(); }
        finally { held.TrySetResult(rig.Bindings.CreatePin()); rig.Expect(actual); rig.ExpectedClose = true; }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Contains(Leaves(error), cause => cause is UnauthorizedAccessException or ObjectDisposedException);
        var known = Assert.Single(Leaves(error).Distinct<Exception>(ReferenceEqualityComparer.Instance)); rig.ExpectedPinCauses.Add(known);
        Assert.NotNull(rig.Bindings.LastPin); Assert.True(rig.Bindings.LastPin!.Closed);
        Assert.True(rig.Bindings.IsOwnedOriginalExecutionPin(rig.Binding!, rig.Bindings.LastPin)); Assert.Equal(0, rig.Starts);
    });

    [Fact] public Task Pure_native_pin_mutation_refuses_exact_finite_Start_and_releases_held_Home() => RunNativePinControl(async rig =>
    {
        var consent = await rig.Accept(); var entry = await rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        var pin = rig.Bindings.LastPin; Assert.NotNull(pin); pin!.Current = false;
        var error = Assert.Throws<UnauthorizedAccessException>(() => entry.RunOriginalProcessStart(rig.Binding!.CanonicalRoot, "synthetic-fixed-shell", new('a', 64), () => { rig.Starts++; return true; }));
        Assert.Same(pin.InvalidCurrentCause, error); rig.ExpectedPinCauses.Add(error); Assert.Equal(0, rig.Starts);
        var read = rig.Own(rig.Permissions.GetSnapshotAsync()); Assert.False(read.IsCompleted);
        var close = rig.Own(entry.DisposeAsync().AsTask()); rig.Expect(close); rig.ExpectedClose = true;
        var cleanup = await Assert.ThrowsAsync<AggregateException>(() => close); Assert.Contains(error, Leaves(cleanup));
        await read.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(pin.Closed);
    });

    [Fact] public Task Held_faulted_native_pin_close_cannot_skip_actual_Home_release_or_direct_siblings() => RunNativePinControl(async rig =>
    {
        var consent = await rig.Accept(); var entry = await rig.Own(rig.Source.EnterOriginalProcessStartAsync(consent, rig.Tools.Preparation!, default));
        var pin = rig.Bindings.LastPin; Assert.NotNull(pin);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); pin!.CloseTask = held.Task;
        var read = rig.Own(rig.Permissions.GetSnapshotAsync()); Assert.False(read.IsCompleted);
        var close = rig.Own(entry.DisposeAsync().AsTask()); rig.Expect(close); rig.ExpectedClose = true;
        var first = new OperationCanceledException("faulted native descriptor cleanup"); var second = new IOException("independent native descriptor sibling");
        try { await read.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(close.IsCompleted); Assert.True(pin.Closed); }
        finally { held.TrySetException([first, second]); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(first, Leaves(error)); Assert.Contains(second, Leaves(error));
        Assert.All(Leaves(error), cause => Assert.True(ReferenceEquals(cause, first) || ReferenceEquals(cause, second)));
        rig.ExpectedPinCauses.Add(first); rig.ExpectedPinCauses.Add(second); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.Equal(0, rig.Starts);
    });

    private sealed partial class Rig
    { internal readonly HashSet<Exception> ExpectedPinCauses = new(ReferenceEqualityComparer.Instance); }
    private static async Task RunNativePinControl(Func<Rig, Task> body)
    {
        Rig? rig = null; Exception? primary = null; var errors = new List<Exception>(); Task? close = null;
        try { rig = new Rig(); await body(rig); } catch (Exception cause) { primary = cause; }
        if (rig is not null)
        {
            try { close = rig.Source.CloseAndDrainOriginalExecutionsAsync(); } catch (Exception cause) { Add(cause); }
            foreach (var actual in rig.Originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await actual; } catch (Exception cause) { Add((Exception?)actual.Exception ?? cause); }
            if (close is not null) try { await close; } catch (Exception cause) { Add((Exception?)close.Exception ?? cause); }
            try { Directory.Delete(rig.Root, true); } catch (Exception cause) { Add(cause); }
        }
        if (primary is not null) errors.Insert(0, primary);
        if (errors.Count != 0) throw new AggregateException("Actual native-pin control/body and independently joined cleanup failed.", errors);
        void Add(Exception cause)
        {
            foreach (var leaf in Leaves(cause))
                if (rig?.ExpectedPinCauses.Contains(leaf) != true && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }

    private sealed partial class BindingSource : IDeveloperWorkspaceOriginalExecutionScopedBindingSource,
        IDeveloperWorkspaceOriginalExecutionCommitBindingSource, IDeveloperWorkspaceOriginalExecutionPinCustodySource
    {
        private readonly HashSet<SyntheticNativePin> _pins = new(ReferenceEqualityComparer.Instance);
        internal SyntheticNativePin? LastPin; internal Func<Task<IDeveloperWorkspaceOriginalExecutionCommitPin>>? PinFactory;
        internal SyntheticNativePin CreatePin() { var pin = new SyntheticNativePin(this, Binding!); _pins.Add(pin); LastPin = pin; return pin; }
        public bool IsOwnedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin) =>
            pin is SyntheticNativePin actual && ReferenceEquals(actual.Source, this) && ReferenceEquals(actual.Binding, binding) && _pins.Contains(actual);
        public bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin) =>
            IsOwnedOriginalExecutionPin(binding, pin) && IsIssuedOriginalBinding(binding) && pin is SyntheticNativePin { Closed: false, Current: true };
        public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalBinding(binding)) throw new UnauthorizedAccessException();
            return PinFactory?.Invoke() ?? Task.FromResult<IDeveloperWorkspaceOriginalExecutionCommitPin>(CreatePin());
        }
        public Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => Within(() => RevalidateOriginalAsync(binding, actor, token), scope, retain);
        public async Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding binding,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? actual = null; IDeveloperWorkspaceOriginalExecutionCommitPin? result = null; var errors = new List<Exception>();
            try { scope(() => { actual = AcquireOriginalExecutionPinAsync(binding, token); retain(actual); }); } catch (Exception cause) { errors.Add(cause); }
            if (actual is not null) try { result = await actual; } catch (Exception cause) { errors.Add((Exception?)actual.Exception ?? cause); }
            if (errors.Count != 0)
            {
                if (result is not null && IsOwnedOriginalExecutionPin(binding, result))
                    try { await result.DisposeAsync(); } catch (Exception cause) { errors.Add(cause); }
                throw new AggregateException("Controlled original pin scope/acquisition failed.", errors);
            }
            return result ?? throw new InvalidOperationException("Controlled original pin was not acquired.");
        }
        private static async Task Within(Func<Task> factory, Action<Action> scope, Action<Task> retain)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { scope(() => { actual = factory(); retain(actual); }); } catch (Exception cause) { errors.Add(cause); }
            if (actual is not null) try { await actual; } catch (Exception cause) { errors.Add((Exception?)actual.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Controlled original binding scope/raw validation failed.", errors);
            if (actual is null) throw new InvalidOperationException("Controlled validation callback was not invoked.");
        }
    }
    private sealed class SyntheticNativePin(BindingSource source, Binding binding) : IDeveloperWorkspaceOriginalExecutionCommitPin
    {
        internal BindingSource Source => source; internal Binding Binding => binding;
        internal bool Current = true, Closed; internal Task? CloseTask;
        internal readonly UnauthorizedAccessException InvalidCurrentCause = new("controlled original native identity changed");
        public void DemandOriginalExecutionBinding() { if (!Current || Closed || !source.IsIssuedOriginalBinding(binding)) throw InvalidCurrentCause; }
        public ValueTask DisposeAsync() { Closed = true; return new(CloseTask ?? Task.CompletedTask); }
    }
    private sealed class LegacyBindingSource(BindingSource source) : IDeveloperWorkspaceOriginalExecutionBindingSource
    {
        public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding binding) => source.IsIssuedOriginalBinding(binding);
        public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor, CancellationToken token) => source.RevalidateOriginalAsync(binding, actor, token);
        public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding binding) => source.GetOriginalExecutionScopes(binding);
        public void DemandExternalOriginalExecutionBindingJoin() => source.DemandExternalOriginalExecutionBindingJoin();
    }
}
