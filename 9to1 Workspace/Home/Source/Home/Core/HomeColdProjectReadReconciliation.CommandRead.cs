using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation : IDeveloperOriginalProjectCommandReadSource
{
    private CommandRead RequireCommandRead(IDeveloperOriginalProjectCommandRead value, bool live)
    {
        lock (_gate)
            return value is CommandRead actual && ReferenceEquals(actual.Owner, this) &&
                ReferenceEquals(actual.Original._command, actual) && _issued.TryGetValue(actual.Original, out _) &&
                actual.Original.Driver?.IsCompletedSuccessfully == true &&
                (!live || !_retiring && actual.Original.ReadReady && actual.Original.CommandCanUse)
                ? actual : throw new UnauthorizedAccessException("SAME private current command READ required.");
    }
    public Task<IDeveloperOriginalProjectCommandRead> AcquireOriginalCommandReadWithinSourceAsync(
        Conversation sameConversation, ContainerDefinition sameActualContainer, string exactProjectReferenceJson,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        var original = Reserve(null);
        var command = new CommandRead(this, original);
        lock (_gate) original._command = command;
        return original.StartCommandRead(command, sameConversation, sameActualContainer,
            exactProjectReferenceJson, originalSynchronousScope, retainOriginalTask, token);
    }
    public bool IsOwnedOriginalCommandRead(IDeveloperOriginalProjectCommandRead value)
    {
        lock (_gate) return value is CommandRead actual && ReferenceEquals(actual.Owner, this) &&
            ReferenceEquals(actual.Original._command, actual) && _issued.TryGetValue(actual.Original, out _) &&
            actual.Original.Driver?.IsCompletedSuccessfully == true;
    }
    public bool IsIssuedOriginalCommandRead(IDeveloperOriginalProjectCommandRead value)
    {
        lock (_gate) return !_retiring && value is CommandRead actual && ReferenceEquals(actual.Owner, this) &&
            ReferenceEquals(actual.Original._command, actual) && _issued.TryGetValue(actual.Original, out _) &&
            actual.Original.Driver?.IsCompletedSuccessfully == true && actual.Original.ReadReady && actual.Original.CommandCanUse;
    }
    public Task ValidateOriginalCommandReadWithinSourceAsync(IDeveloperOriginalProjectCommandRead value,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var command = RequireCommandRead(value, true);
        return command.Original.StartValidation(scope, retain,
            sources => command.Original.ValidateReadAsync(sources, token));
    }
    public Task<IDeveloperOriginalProjectCommandNativeRead> CaptureOriginalCommandNativeReadWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead value, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var command = RequireCommandRead(value, true);
        return command.Original.StartCommandOperation(scope, retain, async sources =>
        {
            await command.Original.ValidateReadAsync(sources, token).ConfigureAwait(false);
            var product = await command.Original.CaptureCommandNativeAsync(command, sources, token).ConfigureAwait(false);
            await ValidateCommandNativeAsync(command, product, sources, token).ConfigureAwait(false);
            return (IDeveloperOriginalProjectCommandNativeRead)product;
        });
    }
    private CommandNativeRead RequireCommandNative(CommandRead command,
        IDeveloperOriginalProjectCommandNativeRead value)
    {
        lock (command.Original.CommandGate)
            return value is CommandNativeRead actual && ReferenceEquals(actual.Command, command) &&
                command.Original._commandNatives.Contains(actual)
                ? actual : throw new UnauthorizedAccessException("SAME privately retained command native capture required.");
    }
    public bool IsOwnedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead command,
        IDeveloperOriginalProjectCommandNativeRead value)
    {
        if (!IsOwnedOriginalCommandRead(command) || command is not CommandRead actual) return false;
        lock (actual.Original.CommandGate) return value is CommandNativeRead product && ReferenceEquals(product.Command, actual) &&
            actual.Original._commandNatives.Contains(product);
    }
    public bool IsIssuedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead command,
        IDeveloperOriginalProjectCommandNativeRead value)
    {
        if (!IsIssuedOriginalCommandRead(command) || command is not CommandRead actual) return false;
        CommandNativeRead product;
        lock (actual.Original.CommandGate)
        {
            if (value is not CommandNativeRead candidate || !ReferenceEquals(candidate.Command, actual) ||
                !actual.Original._commandNatives.Contains(candidate)) return false;
            product = candidate;
        }
        return CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => CloudflareOriginalExecutionGuard.InvokeOriginal(actual.Original,
                () => _actualNative!.IsIssuedOriginalRead(actual.Original.Selection, product.Capture, product.Read)));
    }
    public Task ValidateOriginalCommandNativeReadWithinSourceAsync(IDeveloperOriginalProjectCommandRead command,
        IDeveloperOriginalProjectCommandNativeRead value, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireCommandRead(command, true); var product = RequireCommandNative(actual, value);
        return actual.Original.StartCommandOperation(scope, retain, async sources =>
        { await ValidateCommandNativeAsync(actual, product, sources, token).ConfigureAwait(false); return true; });
    }
    private async Task ValidateCommandNativeAsync(CommandRead command, CommandNativeRead product, Context sources, CancellationToken token)
    {
        _ = RequireCommandRead(command, true);
        if (!sources.Invoke(() => _actualNative!.IsIssuedOriginalRead(command.Original.Selection, product.Capture, product.Read)))
            throw new UnauthorizedAccessException("Command native capture retired or is no longer current.");
        await sources.Await(sources.Invoke(() => _actualNative!.ValidateOriginalReadWithinSourceAsync(
            command.Original.Selection, product.Capture, product.Read, sources.Scope, sources.Retain, token))).ConfigureAwait(false);
        // Actual current Files, profile and manual READ checks all precede an execution Home entry.
        await command.Original.ValidateReadAsync(sources, token).ConfigureAwait(false);
        sources.Invoke(() => { _ = RequireCommandRead(command, true); product.Read.DemandOriginalExecutionBinding(); return true; });
    }
    public bool IsClosedOriginalCommandNativeRead(IDeveloperOriginalProjectCommandRead command,
        IDeveloperOriginalProjectCommandNativeRead value, Task sameActualNativeCloseTask)
    {
        if (!IsOwnedOriginalCommandNativeRead(command, value) || command is not CommandRead actual || value is not CommandNativeRead product) return false;
        return CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => CloudflareOriginalExecutionGuard.InvokeOriginal(actual.Original,
                () => _actualNative!.IsClosedOriginalRead(actual.Original.Selection, product.Capture, product.Read, sameActualNativeCloseTask)));
    }
    private sealed class CommandRead(HomeColdProjectReadReconciliation owner, Work original) : IDeveloperOriginalProjectCommandRead
    {
        internal HomeColdProjectReadReconciliation Owner => owner;
        internal Work Original => original;
        public IDeveloperOriginalCurrentProjectSelection OriginalSelection => original.Selection;
        public IDeveloperProjectOriginalReadAdmission OriginalReadAdmission => original;
        public IDeveloperOriginalCurrentProjectDescriptor OriginalDescriptor => original.CommandDescriptor;
        public TaskRunColdProjectIdentity OriginalIdentity => original.Identity;
        public Task OriginalPreparation => original.OriginalPreparation;
        public void DemandExternalOriginalJoin() { owner.DemandExternalOriginalJoin(); original.DemandExternalOriginalJoin(); }
        public void RequestOriginalRetirement() { original.RequestOriginalRetirement(); _ = original.CancelOriginalPendingAsync(); }
        public Task CloseAndDrainOriginalAsync() { DemandExternalOriginalJoin(); return original.CloseOwned(); }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
    private sealed class CommandNativeRead(CommandRead command, Task<IDeveloperOriginalCurrentProjectNativeRead> capture,
        IDeveloperOriginalCurrentProjectNativeRead read) : IDeveloperOriginalProjectCommandNativeRead
    {
        internal CommandRead Command => command;
        internal Task<IDeveloperOriginalCurrentProjectNativeRead> Capture => capture;
        internal IDeveloperOriginalCurrentProjectNativeRead Read => read;
        public IDeveloperOriginalProjectCommandRead OriginalCommandRead => command;
        public Task<IDeveloperOriginalCurrentProjectNativeRead> OriginalCaptureTask => capture;
        public IDeveloperOriginalCurrentProjectNativeRead OriginalRead => read;
    }
    private sealed partial class Work
    {
        internal CommandRead? _command;
        private bool _commandFailed;
        internal object CommandGate => _gate;
        internal IDeveloperOriginalCurrentProjectDescriptor CommandDescriptor => _descriptor;
        internal bool CommandCanUse { get { lock (_gate) return !_commandFailed && !_validations.Any(raw => raw.IsFaulted || raw.IsCanceled); } }
        internal readonly List<CommandNativeRead> _commandNatives = [];
        internal Task<IDeveloperOriginalProjectCommandRead> StartCommandRead(CommandRead command,
            Conversation conversation, ContainerDefinition container, string reference,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Conversation = conversation; Container = container;
            return Start<IDeveloperOriginalProjectCommandRead>(scope, retain, token, async sources =>
            {
                await PrepareReadAsync(sources, reference, null, null, token).ConfigureAwait(false);
                await AcquireNativeAndHeldAsync(sources, token).ConfigureAwait(false);
                var identity = BuildIdentity(); DemandOriginalCommit();
                await ReleaseBorrowersAsync().ConfigureAwait(false);
                Identity = identity; DemandLive(); return command;
            });
        }
        internal Task<T> StartCommandOperation<T>(Action<Action> scope, Action<Task> retain, Func<Context, Task<T>> body)
        {
            DemandLive(); var sources = new Context(owner, this, scope, retain, true);
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
            lock (_gate)
            {
                if (_retired || _close is not null) throw new ObjectDisposedException("Original command READ");
                if (_validations.Count >= 4096) throw new InvalidOperationException("Original command READ custody is full.");
                actual = RunCommandPublishedAsync(begin.Task, sources, body);
                _validations.Add(actual); _contexts.Add(sources);
            }
            try { sources.Publish(actual); } catch (Exception cause) { sources.Errors.Retain(cause); }
            finally { begin.SetResult(); } return actual;
        }
        private async Task<T> RunCommandPublishedAsync<T>(Task begin, Context sources, Func<Context, Task<T>> body)
        {
            Task<T>? actual = null;
            try
            {
                actual = RunPublishedAsync(begin, sources, body, CancellationToken.None, false);
                return await actual.ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                using var ownerPhase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                lock (_gate) _commandFailed = true;
                Exception? cleanup = null;
                try { await ReleaseCommandNativeReadsAsync().ConfigureAwait(false); }
                catch (Exception closeCause) { cleanup = closeCause; }
                var primary = (Exception?)actual?.Exception ?? cause;
                if (cleanup is not null) throw new AggregateException("Command original acquisition/validation and independent native cleanup failed.", primary, cleanup);
                if (actual?.IsCanceled == true) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
                throw new AggregateException("Actual command READ source driver failed.", primary);
            }
        }
        internal async Task<CommandNativeRead> CaptureCommandNativeAsync(CommandRead command, Context sources, CancellationToken token)
        {
            Task<IDeveloperOriginalCurrentProjectNativeRead>? actualCapture = null; CommandNativeRead? product = null;
            var native = owner._actualNative!;
            var read = await sources.Capture(() =>
            {
                actualCapture = native.CaptureOriginalWithinSourceAsync(Selection, this, sources.Scope, sources.Retain, token);
                return actualCapture;
            }, actual =>
            {
                if (actualCapture is null || !native.IsOwnedOriginalRead(Selection, actualCapture, actual))
                    throw new UnauthorizedAccessException("Returned command native object lacks exact historical issuer custody.");
                product = new CommandNativeRead(command, actualCapture, actual);
                lock (_gate) _commandNatives.Add(product);
            }).ConfigureAwait(false);
            if (product is null || !ReferenceEquals(product.Read, read))
                throw new UnauthorizedAccessException("SAME retained actual command capture product required.");
            return product;
        }
        // Every genuine late/undisclosed product is retained before a live-use check. Work
        // close first joins all actual drivers, then independently closes this entire cohort.
        private async Task ReleaseCommandNativeReadsAsync()
        {
            if (_command is null) return;
            var sources = new Context(owner, this, body => body(), _ => { }, false);
            CommandNativeRead[] products; lock (_gate) { products = _commandNatives.ToArray(); _contexts.Add(sources); }
            var closes = new List<Task>(); var actualCloses = new List<(CommandNativeRead Product, Task Close)>();
            foreach (var product in products)
                sources.AcquireClose(() =>
                {
                    var close = product.Read.DisposeAsync().AsTask(); actualCloses.Add((product, close)); return close;
                }, closes);
            foreach (var close in closes) try { await sources.Await(close).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Retain(cause); }
            foreach (var actual in actualCloses)
                try
                {
                    sources.Physical(() => owner._actualNative!.IsClosedOriginalRead(Selection, actual.Product.Capture,
                        actual.Product.Read, actual.Close) ? true : throw new UnauthorizedAccessException("Command native close lacks its exact healthy issuer proof."));
                }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            await sources.Settle().ConfigureAwait(false);
        }
    }
}
