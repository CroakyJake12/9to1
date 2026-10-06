using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeCloudflareServiceOwner
{
    private readonly object _hostGate = new();
    private readonly List<HostWork> _hostWork = [];
    private readonly CloudflareOriginalTaskLedger _hostClosing = new();
    private readonly List<CloudflareOriginalTaskLedger> _hostSources = [];
    private readonly Dictionary<Action<Action>, Action<Action>> _hostCallers = [];
    private Action<Action>? _hostOwnCaller;
    private bool _hostRetiring;
    private Task? _hostClose;
    private Exception? _hostCapacityFailure;
    private Func<bool>? _originalHostStarted;
    private readonly AsyncLocal<HostWork?> _currentHostWork = new();
    private sealed class HostWork
    {
        internal Task Original = null!;
        internal object? Subject;
        internal readonly List<CloudflareOriginalTaskLedger> Sources = [];
    }

    /// <summary>Trusted host pairing only. Successful bootstrap is a deny-only prerequisite;
    /// it issues no account, resource, tool, installed-app or credential permission.</summary>
    public void BindOriginalHostStartup(Func<bool> sameOriginalStartSucceeded)
    {
        ArgumentNullException.ThrowIfNull(sameOriginalStartSucceeded);
        lock (_hostGate)
        {
            if (_hostRetiring || _hostWork.Count != 0 || _originalHostStarted is not null)
                throw new InvalidOperationException("Bind the original host before Cloudflare work admission.");
            _originalHostStarted = sameOriginalStartSucceeded;
        }
    }
    private void RunOriginalHostCallback(CloudflareOriginalTaskLedger stages, Action body)
    { stages.Invoke(() => { body(); return true; }); }
    private void RetainOriginalHostTask(CloudflareOriginalTaskLedger stages, Task actual) { _ = stages.Track(actual); }
    private void DemandOriginalHostAdmission()
    {
        lock (_hostGate)
        {
            if (_hostRetiring) throw new ObjectDisposedException("Original Home Cloudflare admission");
            _hostWork.RemoveAll(work => work.Original.IsCompletedSuccessfully && work.Sources.All(source => source.OriginalErrors.Count == 0 && source.OriginalTasks.All(task => task.IsCompletedSuccessfully)));
            if (_hostWork.Count >= 512) _hostCapacityFailure ??= new InvalidOperationException("Original host work custody is full.");
            if (_hostCapacityFailure is { } cause) throw new AggregateException("Original Home Cloudflare custody is full.", cause);
            if (_originalHostStarted is not null && !CloudflareOriginalExecutionGuard.InvokeOriginal(this, _originalHostStarted))
                throw new CloudflareSetupRequiredException(CloudflareSetupStage.HomeProfileRequired,
                    "CF_ORIGINAL_HOME_STARTUP_REQUIRED", "Start and join the configured original Home host before Cloudflare setup or dispatch.");
        }
    }
    private Action<Action> OriginalHostCaller(Action<Action>? parent)
    {
        lock (_hostGate)
        {
            if (parent is null) return _hostOwnCaller ??= BindCaller(null);
            if (ReferenceEquals(parent, _hostOwnCaller) || _hostCallers.Values.Contains(parent)) return parent;
            if (_hostCallers.TryGetValue(parent, out var existing)) return existing;
            if (_hostCallers.Count >= 512) throw new InvalidOperationException("Original host caller custody is full.");
            var original = BindCaller(parent); _hostCallers.Add(parent, original); return original;
        }
        Action<Action> BindCaller(Action<Action>? actualParent) => body =>
        {
            int active = 1, invoked = 0; int thread = Environment.CurrentManagedThreadId;
            void Run()
            {
                if (Volatile.Read(ref active) == 0 || Environment.CurrentManagedThreadId != thread ||
                    Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                    throw new InvalidOperationException("The original host callback requires its active synchronous issuing thread and one use.");
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
            }
            try
            {
                if (actualParent is null) Run(); else actualParent(Run);
                if (Volatile.Read(ref invoked) == 0) throw new InvalidOperationException("The original host callback was not invoked.");
            }
            finally { Volatile.Write(ref active, 0); }
        };
    }
    private CloudflareOriginalTaskLedger CreateOriginalHostSources(Action<Action>? parent = null)
    {
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalCallerCallback(OriginalHostCaller(parent));
        AttachOriginalHostSources(sources); return sources;
    }
    private void AttachOriginalHostSources(CloudflareOriginalTaskLedger sources)
    {
        lock (_hostGate)
        {
            if (!_hostSources.Contains(sources))
            {
                if (_hostSources.Count >= 4096)
                {
                    _hostCapacityFailure ??= new InvalidOperationException("Original host raw source custody is full.");
                    throw new AggregateException("Original host source refused before its callbacks.", _hostCapacityFailure);
                }
                _hostSources.Add(sources);
            }
            var work = _currentHostWork.Value;
            if (work is not null && !work.Sources.Contains(sources)) work.Sources.Add(sources);
        }
    }
    private void RetainExistingOriginalHostTask(Task actual, CloudflareOriginalTaskLedger sources, object subject)
    {
        lock (_hostGate)
        {
            if (_hostWork.Any(work => ReferenceEquals(work.Original, actual))) return;
            var work = new HostWork { Original = actual, Subject = subject }; work.Sources.Add(sources); _hostWork.Add(work);
        }
    }
    private Task<T> StartOriginalHostAsync<T>(Func<Task<T>> body)
    {
        lock (_hostGate)
        {
            DemandOriginalHostAdmission();
            _hostWork.RemoveAll(work => work.Original.IsCompletedSuccessfully && work.Sources.All(source =>
                source.OriginalErrors.Count == 0 && source.OriginalTasks.All(task => task.IsCompletedSuccessfully)));
            if (_hostWork.Count >= 512)
            {
                _hostCapacityFailure ??= new InvalidOperationException("Retained original Home Cloudflare work is full.");
                throw new AggregateException("Original host admission refused before callbacks.", _hostCapacityFailure);
            }
            var work = new HostWork(); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = RunOriginalHostPublishedAsync(begin.Task, work, body); work.Original = actual;
            _hostWork.Add(work); begin.SetResult(); return actual;
        }
    }
    private Task StartOriginalHostAsync(Func<Task> body) => StartOriginalHostAsync(async () => { await body().ConfigureAwait(false); return true; });
    private async Task<T> RunOriginalHostPublishedAsync<T>(Task begin, HostWork work, Func<Task<T>> body)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var prior = _currentHostWork.Value; _currentHostWork.Value = work;
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        T value = default!; Task<T>? actualBody = null; Exception? observed = null;
        try
        {
            try { _ = originals.Invoke(() => { actualBody = body(); return actualBody; }); }
            catch (Exception cause) { observed = cause; originals.Retain(cause); }
            if (actualBody is not null)
                try { value = await actualBody.ConfigureAwait(false); }
                catch (Exception cause) { observed ??= cause; originals.Capture(actualBody, cause); }
            CloudflareOriginalTaskLedger[] children; lock (_hostGate) children = work.Sources.ToArray();
            foreach (var child in children)
            {
                await child.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in child.OriginalErrors) originals.Retain(cause);
            }
            await originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (actualBody?.IsCanceled == true && observed is OperationCanceledException &&
                children.All(child => child.OriginalTasks.All(task => !task.IsFaulted)) &&
                originals.OriginalErrors.All(cause => cause is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observed).Throw();
            if (originals.OriginalErrors.Count == 1 && originals.OriginalErrors[0] is not OperationCanceledException)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originals.OriginalErrors[0]).Throw();
            if (originals.OriginalErrors.Count != 0) throw new AggregateException("Actual Home Cloudflare host work failed.", originals.OriginalErrors);
            DemandOriginalHostAdmission(); return value;
        }
        finally { _currentHostWork.Value = prior; }
    }
    public void RequestOriginalHostRetirement()
    {
        lock (_hostGate) _hostRetiring = true;
        RequestOriginalStagingRetirement(); RequestOriginalRecoveryRetirement();
    }
    public void DemandExternalOriginalHostJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        object[] subjects; lock (_hostGate) subjects = _hostWork.Select(work => work.Subject).OfType<object>().Distinct(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var subject in subjects) CloudflareOriginalExecutionGuard.DemandExternalJoin(subject);
        DemandExternalOriginalStagingJoin(); DemandExternalOriginalRecoveryJoin();
        Permission[] originals; lock (_sync) originals = _originals.Values.ToArray();
        foreach (var original in originals) CloudflareOriginalExecutionGuard.DemandExternalJoin(original);
    }
    public Task CloseAndDrainOriginalHostAsync()
    {
        DemandExternalOriginalHostJoin();
        lock (_hostGate)
        {
            if (_hostClose is not null) return _hostClose;
            _hostRetiring = true; var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _hostClose = CloseOriginalHostPublishedAsync(begin.Task); begin.SetResult(); return _hostClose;
        }
    }
    private async Task CloseOriginalHostPublishedAsync(Task begin)
    {
        await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var closes = _hostClosing; closes.BindOriginalOwner(this);
        RequestOriginalHostRetirement();
        // Release actual held entries independently BEFORE public source drivers that
        // can be waiting on the SAME Home store gate. Home itself remains borrowed.
        Permission[] permissions; lock (_sync) permissions = _originals.Values.ToArray();
        foreach (var permission in permissions)
            try { _ = closes.Invoke(() => permission.DisposeAsync().AsTask()); } catch (Exception cause) { closes.Retain(cause); }
        try { _ = closes.Invoke(CloseOriginalHostStagingAsync); } catch (Exception cause) { closes.Retain(cause); }
        try { _ = closes.Invoke(CloseOriginalHostRecoveriesAsync); } catch (Exception cause) { closes.Retain(cause); }
        HostWork[] work; lock (_hostGate) work = _hostWork.ToArray();
        foreach (var original in work)
            try { await closes.AwaitAsync(original.Original).ConfigureAwait(false); } catch (Exception cause) { closes.Retain(cause); }
        await closes.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        CloudflareOriginalTaskLedger[] sources; lock (_hostGate) sources = _hostSources.ToArray();
        foreach (var source in sources)
        {
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in source.OriginalErrors) closes.Retain(cause);
        }
        if (_hostCapacityFailure is { } capacity) closes.Retain(capacity);
        if (closes.OriginalErrors.Count != 0) throw new AggregateException("Actual Home Cloudflare host retirement failed.", closes.OriginalErrors);
    }
}
