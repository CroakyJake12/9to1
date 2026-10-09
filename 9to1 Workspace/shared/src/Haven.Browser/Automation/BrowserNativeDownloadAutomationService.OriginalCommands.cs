using System.Runtime.ExceptionServices;
using Haven.Application;

namespace Haven.Browser;

public sealed partial class BrowserNativeDownloadAutomationService
{
    private readonly List<OriginalCommand> _originalCommands = [];
    private readonly AsyncLocal<OriginalCommand?> _originalCommand = new();
    private sealed class OriginalCommand(IBrowserNativeDownloadExecution? execution)
    {
        internal readonly IBrowserNativeDownloadExecution? Execution = execution;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> Errors = [];
        internal Task Driver = null!;
    }

    private async Task<T> RunOriginalCommandAsync<T>(IBrowserNativeDownloadExecution? execution, Func<Task<T>> body)
    {
        OriginalCommand original; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalGate)
        {
            ThrowIfDisposed();
            if (_originalCommands.Count >= 128)
                throw new InvalidOperationException("Join unresolved native Browser commands before admitting another operation.");
            original = new(execution);
            original.Driver = Drive(); _originalCommands.Add(original);
        }
        start.SetResult();
        var result = await ((Task<T>)original.Driver).ConfigureAwait(false);
        // This caller independently joined the SAME actual encompassing driver.
        lock (_originalGate) if (original.Errors.Count == 0 && original.Sources.Count == 0)
            _originalCommands.RemoveAll(actual => ReferenceEquals(actual, original));
        return result;

        async Task<T> Drive()
        {
            await start.Task.ConfigureAwait(false);
            var prior = _originalCommand.Value; _originalCommand.Value = original;
            Task<T>? actual = null;
            try
            {
                InvokeOriginalCommand(original, () =>
                {
                    actual = body() ?? throw new InvalidOperationException("The actual Browser command returned no Task.");
                    lock (_originalGate) original.Sources.Add(actual);
                });
                var result = await actual!.ConfigureAwait(false);
                lock (_originalGate) original.Sources.RemoveAll(source => ReferenceEquals(source, actual));
                return result;
            }
            catch (Exception cause) { CaptureOriginalCommandFailure(original, actual, cause); throw; }
            finally { _originalCommand.Value = prior; }
        }
    }

    private void InvokeOriginalCommand(OriginalCommand original, Action actual)
    {
        var physical = _originalPhysical ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(this, out var depth); physical[this] = depth + 1;
        try { actual(); }
        catch (Exception cause) { CaptureOriginalCommandFailure(original, null, cause); throw; }
        finally { if (depth == 0) physical.Remove(this); else physical[this] = depth; }
    }

    private async Task<T> ReadOriginalCommandSourceAsync<T>(Func<Task<T>> factory)
    {
        var original = _originalCommand.Value;
        if (original is null) return await factory().ConfigureAwait(false);
        Task<T>? actual = null;
        try
        {
            InvokeOriginalCommand(original, () =>
            {
                lock (_originalGate) if (original.Sources.Count >= 512)
                    throw new InvalidOperationException("Settle actual native Browser command sources before another acquisition.");
                actual = factory() ?? throw new InvalidOperationException("The actual Browser source returned no Task.");
                lock (_originalGate) original.Sources.Add(actual);
            });
            var result = await actual!.ConfigureAwait(false);
            lock (_originalGate) original.Sources.RemoveAll(source => ReferenceEquals(source, actual));
            return result;
        }
        catch (Exception cause) { CaptureOriginalCommandFailure(original, actual, cause); throw; }
    }
    private async Task ReadOriginalCommandSourceAsync(Func<Task> factory)
    {
        var original = _originalCommand.Value;
        if (original is null) { await factory().ConfigureAwait(false); return; }
        Task? actual = null;
        try
        {
            InvokeOriginalCommand(original, () =>
            {
                lock (_originalGate) if (original.Sources.Count >= 512)
                    throw new InvalidOperationException("Settle actual native Browser command sources before another acquisition.");
                actual = factory() ?? throw new InvalidOperationException("The actual Browser source returned no Task.");
                lock (_originalGate) original.Sources.Add(actual);
            });
            await actual!.ConfigureAwait(false);
            lock (_originalGate) original.Sources.RemoveAll(source => ReferenceEquals(source, actual));
        }
        catch (Exception cause) { CaptureOriginalCommandFailure(original, actual, cause); throw; }
    }
    private void CaptureOriginalCommandFailure(OriginalCommand original, Task? raw, Exception observed)
    {
        lock (_originalGate)
        {
            IEnumerable<Exception> actual = raw?.Exception is { } clr ? clr.InnerExceptions : [observed];
            foreach (var cause in actual)
                if (!original.Errors.Any(prior => ReferenceEquals(prior, cause))) original.Errors.Add(cause);
        }
    }
    private async Task JoinOriginalCommandsAsync(IReadOnlyList<OriginalCommand> originals)
    {
        var errors = new List<Exception>();
        foreach (var original in originals)
        {
            try { await original.Driver.ConfigureAwait(false); }
            catch (Exception cause) { CaptureOriginalCommandFailure(original, original.Driver, cause); }
            Task[] sources; lock (_originalGate) sources = original.Sources.ToArray();
            foreach (var raw in sources)
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { CaptureOriginalCommandFailure(original, raw, cause); }
            lock (_originalGate)
                foreach (var cause in original.Errors)
                    if (!errors.Any(prior => ReferenceEquals(prior, cause))) errors.Add(cause);
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual admitted native Browser command sources did not settle.", errors);
    }
}
