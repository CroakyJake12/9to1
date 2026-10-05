using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace AvaloniaHome;

/// <summary>Owns the exact native-host work and explicit process shutdown; window close is separate.</summary>
internal sealed class HomeHostOriginalLifetime
{
    private sealed class OriginalWork
    {
        internal Task Task = null!;
        internal Task? Callback;
    }

    private readonly object _sync = new();
    private readonly List<OriginalWork> _originals = new();
    private readonly Func<Task> _closeCore;
    private readonly Func<int, Task> _exitDesktop;
    private Task? _shutdown;
    private Task? _coreClose;
    private Task? _desktopExit;

    internal HomeHostOriginalLifetime(Func<Task> closeCore, Func<int, Task> exitDesktop)
    {
        _closeCore = closeCore ?? throw new ArgumentNullException(nameof(closeCore));
        _exitDesktop = exitDesktop ?? throw new ArgumentNullException(nameof(exitDesktop));
    }

    internal Task? OriginalShutdown { get { lock (_sync) return _shutdown; } }
    internal Task? OriginalCoreClose { get { lock (_sync) return _coreClose; } }
    internal Task? OriginalDesktopExit { get { lock (_sync) return _desktopExit; } }

    internal Task? TryRunOriginal(Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var start = new TaskCompletionSource(); // Release inline only after the original frame is published.
        OriginalWork work;
        lock (_sync)
        {
            if (_shutdown is not null) return null;
            _originals.RemoveAll(item => item.Task.IsCompletedSuccessfully);
            if (_originals.Count >= 128)
                throw new InvalidOperationException("Retain the original native-host failures before admitting more work.");
            work = new OriginalWork();
            work.Task = RunOriginalAsync(start.Task, work, callback);
            _originals.Add(work);
        }
        start.SetResult(); // Publish the SAME original task before any native callback.
        return work.Task;
    }

    private async Task RunOriginalAsync(Task start, OriginalWork work, Func<Task> callback)
    {
        await start.ConfigureAwait(false);
        var original = callback() ?? throw new InvalidOperationException("The original native-host callback returned no task.");
        lock (_sync) work.Callback = original;
        await original.ConfigureAwait(false);
    }

    internal Task RequestShutdownAsync()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task original;
        OriginalWork[] admitted;
        lock (_sync)
        {
            if (_shutdown is not null) return _shutdown;
            admitted = _originals.ToArray();
            original = _shutdown = ShutdownOriginalAsync(start.Task, admitted);
        }
        start.SetResult(); // Seal admission and publish the SAME close before callbacks or reentry.
        return original;
    }

    private async Task ShutdownOriginalAsync(Task start, OriginalWork[] admitted)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        foreach (var work in admitted)
        {
            try { await work.Task.ConfigureAwait(false); }
            catch (Exception error) { Add(error); }
        }

        try
        {
            var original = _closeCore() ?? throw new InvalidOperationException("The original Core close returned no task.");
            lock (_sync) _coreClose = original;
            await original.ConfigureAwait(false);
        }
        catch (Exception error) { Add(error); }

        // Actual work and Core close have settled, including faults, before requesting process exit.
        // Cleanup faults produce a nonzero exit and retain their original exceptions.
        try
        {
            var original = _exitDesktop(errors.Count == 0 ? 0 : 1)
                ?? throw new InvalidOperationException("The original desktop exit returned no task.");
            lock (_sync) _desktopExit = original;
            await original.ConfigureAwait(false);
        }
        catch (Exception error) { Add(error); }

        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException("Original native-host work, Core close and desktop exit failed.", errors);

        void Add(Exception error)
        {
            if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
        }
    }
}
