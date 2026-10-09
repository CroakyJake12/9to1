using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    private sealed class Pin(FilesDeveloperOriginalCurrentProjectExecutionBridge owner, Binding binding,
        IDeveloperOriginalProjectCommandNativeRead capture, Original original) : IDeveloperWorkspaceOriginalExecutionCommitPin
    {
        internal FilesDeveloperOriginalCurrentProjectExecutionBridge Owner => owner;
        internal Binding Binding => binding;
        internal IDeveloperOriginalProjectCommandNativeRead Capture => capture;
        internal Original Original => original;
        internal bool Sealed;
        internal Task? Close;
        internal readonly List<Task> Cleanup = [];
        public void DemandOriginalExecutionBinding()
        {
            lock (owner._gate)
                if (owner._retiring || Sealed || !owner._pins.Contains(this) ||
                    !owner._bindings.Contains(binding) || !original.Driver.IsCompletedSuccessfully)
                    throw new UnauthorizedAccessException("The SAME successful private fresh execution pin has retired.");
            if (!owner._reads.IsIssuedOriginalCommandRead(binding.Frame.Read))
                throw new UnauthorizedAccessException("The SAME command READ retired before the finite execution check.");
            // Pure private READ metadata and native probe outside the bridge gate. No productive Home,
            // profile, policy, Files, store or caller callback occurs beneath held Home.
            capture.OriginalRead.DemandOriginalExecutionBinding();
            lock (owner._gate)
                if (owner._retiring || Sealed) throw new UnauthorizedAccessException("The private native execution pin retired during its probe.");
        }
        public ValueTask DisposeAsync() => new(owner.ClosePin(this));
    }
    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(
        IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token)
        => AcquireOriginalExecutionPinWithinSourceAsync(binding, action => action(), _ => { }, token);
    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start<IDeveloperWorkspaceOriginalExecutionCommitPin>(scope, retain, async (original, sources) =>
        {
            var binding = sources.Invoke(() => RequireBinding(sameBinding));
            await sources.ObserveVoid(() => _reads.ValidateOriginalCommandReadWithinSourceAsync(binding.Frame.Read,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            IDeveloperOriginalProjectCommandNativeRead? captured = null;
            Task<IDeveloperOriginalProjectCommandNativeRead>? actual = null;
            Pin? pin = null; var errors = new List<Exception>();
            try
            {
                captured = await Capture(binding.Frame, original, sources, task => actual = task, value => captured = value, token).ConfigureAwait(false);
                sources.Invoke(() =>
                {
                    DemandResolvedDocument(captured.OriginalRead, binding.Project);
                    if (captured.OriginalRead.OriginalWorkspaceDocumentSha256 != binding.DocumentSha ||
                        captured.OriginalRead.OriginalRegisteredRootFingerprint != binding.RootFingerprint)
                        throw new UnauthorizedAccessException("The original document/root changed before the genuine execution pin was acquired.");
                    captured.OriginalRead.DemandOriginalExecutionBinding();
                    lock (_gate)
                    {
                        if (_pins.Count >= 128) throw new InvalidOperationException("Native execution pin custody is full.");
                        pin = new(this, binding, captured, original); _pins.Add(pin);
                    }
                    return true;
                });
                sources.Invoke(() => { token.ThrowIfCancellationRequested(); RequireBinding(binding); return true; });
                return pin!;
            }
            catch (Exception error) { AddTask(errors, actual, error); }
            if (pin is not null)
            {
                Task? close = null;
                try { Scope(() => { close = ClosePin(pin, acquisitionCleanup: true); lock (_gate) original.Raw.Add(close); }); }
                catch (Exception error) { Add(errors, error); }
                if (close is not null)
                {
                    try { sources.RetainOriginalTask(close); } catch (Exception error) { Add(errors, error); }
                    try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
                }
            }
            else if (captured is not null)
                await CloseOwnedNative(binding.Frame, captured, original, sources, errors).ConfigureAwait(false);
            if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); throw new UnauthorizedAccessException("No original native execution pin was returned.");
        });
    public bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin)
    {
        lock (_gate) return !_retiring && samePin is Pin pin && ReferenceEquals(pin.Owner, this) && !pin.Sealed &&
            ReferenceEquals(pin.Binding, sameBinding) && _pins.Contains(pin) && _bindings.Contains(pin.Binding) &&
            pin.Original.Driver.IsCompletedSuccessfully;
    }
    public bool IsOwnedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionCommitPin samePin)
    {
        lock (_gate) return samePin is Pin pin && ReferenceEquals(pin.Owner, this) &&
            ReferenceEquals(pin.Binding, sameBinding) && _pins.Contains(pin) && _bindings.Contains(pin.Binding);
    }
    private Task ClosePin(Pin pin, bool acquisitionCleanup = false)
    {
        if (acquisitionCleanup)
        {
            if (!ReferenceEquals(_executing.Value, pin.Original))
                throw new InvalidOperationException("Only the SAME actual acquiring driver owns unpublished pin cleanup.");
        }
        else
        {
            if (_physical?.ContainsKey(this) == true)
                throw new InvalidOperationException("An actual execution-pin callback cannot join its pin cleanup.");
            for (var original = _executing.Value; original is not null; original = original.Parent)
                if (Volatile.Read(ref original.Live))
                    throw new InvalidOperationException("A live execution original cannot join its native pin cleanup.");
            _native.DemandExternalOriginalJoin();
        }
        TaskCompletionSource start; Task close;
        lock (_gate)
        {
            if (!ReferenceEquals(pin.Owner, this) || !_pins.Contains(pin))
                throw new UnauthorizedAccessException("Unknown native pin cleanup was refused.");
            if (pin.Close is not null) return pin.Close;
            pin.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pin.Close = close = Drain();
        }
        start.TrySetResult(); return close;
        async Task Drain()
        {
            await start.Task.ConfigureAwait(false);
            var previous = _executing.Value;
            var cleanup = new Original(previous) { Driver = pin.Close! };
            _executing.Value = cleanup; Volatile.Write(ref cleanup.Live, true);
            var errors = new List<Exception>(); Task? raw = null;
            try
            {
                try
                {
                    Scope(() =>
                    {
                        if (!OwnsNative(pin.Binding.Frame, pin.Capture))
                            throw new UnauthorizedAccessException("The exact pin lacks private historical native cleanup ownership.");
                        raw = pin.Capture.OriginalRead.DisposeAsync().AsTask(); lock (_gate) pin.Cleanup.Add(raw);
                    });
                }
                catch (Exception error) { Add(errors, error); }
                if (raw is not null)
                {
                    try { await raw.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, raw, error); }
                    try
                    {
                        Scope(() =>
                        {
                            if (!_reads.IsClosedOriginalCommandNativeRead(pin.Binding.Frame.Read, pin.Capture, raw) ||
                                !_native.IsClosedOriginalRead(pin.Binding.Frame.Read.OriginalSelection,
                                    pin.Capture.OriginalCaptureTask, pin.Capture.OriginalRead, raw))
                                throw new UnauthorizedAccessException("The genuine execution descriptors did not close successfully.");
                        });
                    }
                    catch (Exception error) { Add(errors, error); }
                }
                Throw(errors);
            }
            finally { Volatile.Write(ref cleanup.Live, false); _executing.Value = previous; }
        }
    }
    public Task CloseAndDrainOriginalExecutionAsync()
    {
        DemandExternalOriginalExecutionTrustJoin();
        TaskCompletionSource start; Task actual; Original[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _originals.ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = actual = Drain();
        }
        start.TrySetResult(); return actual;
        async Task Drain()
        {
            await start.Task.ConfigureAwait(false);
            var errors = new List<Exception>(); var closes = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            void RequestPins()
            {
                Pin[] pins; lock (_gate) pins = _pins.ToArray();
                foreach (var pin in pins)
                    try { closes.Add(ClosePin(pin)); } catch (Exception error) { Add(errors, error); }
            }
            RequestPins();
            foreach (var original in originals)
            {
                try { await original.Driver.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, original.Driver, error); }
                Task[] raw; lock (_gate) raw = original.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                foreach (var task in raw) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
            }
            RequestPins();
            foreach (var close in closes) try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
            Throw(errors);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalExecutionAsync());
}
