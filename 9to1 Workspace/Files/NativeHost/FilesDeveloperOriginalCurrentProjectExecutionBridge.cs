using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

/// <summary>One configured fresh command READ/Dev execution borrower. Saved metadata,
/// a closed project input and old setup ACKs issue no binding. Home separately reviews
/// the genuine preparation; the actual native pin is checked again at Process.Start.</summary>
public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
    : IDeveloperWorkspaceOriginalProjectExecutionTrustService,
      IDeveloperWorkspaceOriginalProjectExecutionBindingSource,
      IDeveloperWorkspaceOriginalExecutionCommitBindingSource,
      IDeveloperWorkspaceOriginalExecutionPinCustodySource, IOriginalScopedCanonicalResourceAccessResolver, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IDeveloperOriginalProjectCommandReadSource _reads;
    private readonly IDeveloperOriginalCurrentProjectNativeSource _native;
    private readonly Func<IWorkspaceOriginalProcessStartConsentSource> _consents;
    private readonly ITaskRunToolActionOwner _tools;
    private IWorkspaceOriginalProcessStartConsentSource? _actualConsents;
    private readonly AsyncLocal<Original?> _executing = new();
    private readonly AsyncLocal<CommandFrame?> _command = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalCurrentProjectExecutionBridge, int>? _physical;
    private readonly HashSet<Original> _originals = [];
    private readonly HashSet<Binding> _bindings = [];
    private readonly HashSet<Pin> _pins = [];
    private bool _retiring;
    private Task? _close;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FilesDeveloperOriginalCurrentProjectExecutionBridge(
        IDeveloperOriginalProjectCommandReadSource originalReads,
        IDeveloperOriginalCurrentProjectNativeSource originalNative,
        Func<IWorkspaceOriginalProcessStartConsentSource> originalConsents,
        ITaskRunToolActionOwner originalToolOwner)
    {
        ArgumentNullException.ThrowIfNull(originalReads); ArgumentNullException.ThrowIfNull(originalNative);
        ArgumentNullException.ThrowIfNull(originalConsents); ArgumentNullException.ThrowIfNull(originalToolOwner);
        (_reads, _native, _consents, _tools) = (originalReads, originalNative, originalConsents, originalToolOwner);
    }

    private sealed class Original(Original? parent)
    {
        internal Original? Parent => parent;
        internal Task Driver = null!;
        internal bool Live;
        internal bool CallbackFailed;
        internal readonly List<Task> Raw = [];
    }
    private sealed record CommandFrame(IDeveloperOriginalProjectCommandRead Read,
        DeveloperProjectReference Project, DeveloperCanonicalActionContext Context);

    /// <summary>Caller-owned exact Dev Task and its finite publication failure. The caller
    /// joins this before closing command READ, then drains Dev before this bridge. The
    /// bridge never joins borrowed Dev commands from its own source close.</summary>
    public sealed class OriginalCommandInvocation
    {
        private readonly Exception[] _publication;
        private readonly Action _demandJoin;
        private readonly object _joinGate = new();
        private Task<DeveloperOperationResult<DeveloperActionObservation>>? _join;
        internal OriginalCommandInvocation(Task<DeveloperOperationResult<DeveloperActionObservation>> actual,
            Exception[] publication, Action demandJoin) => (OriginalCommandTask, _publication, _demandJoin) = (actual, publication, demandJoin);
        public Task<DeveloperOperationResult<DeveloperActionObservation>> OriginalCommandTask { get; }
        public IReadOnlyList<Exception> OriginalPublicationCauses => Array.AsReadOnly(_publication);
        public Task<DeveloperOperationResult<DeveloperActionObservation>> JoinOriginalAsync()
        {
            _demandJoin();
            lock (_joinGate) return _join ??= Join();
        }
        private async Task<DeveloperOperationResult<DeveloperActionObservation>> Join()
        {
            var errors = new List<Exception>(_publication);
            DeveloperOperationResult<DeveloperActionObservation>? result = null;
            try { result = await OriginalCommandTask.ConfigureAwait(false); }
            catch (Exception error) { AddTask(errors, OriginalCommandTask, error); }
            if (OriginalCommandTask.IsCanceled && _publication.Length == 0 && errors.All(Canceled))
                ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors);
            return result ?? throw new InvalidOperationException("The actual Dev command returned no observation.");
        }
    }

    /// <summary>Finite context attachment only. The actual session/Dev factory retains
    /// its own Task and business lifetime. Any failure after that Task exists is returned
    /// with it, so permission callbacks cannot orphan the already admitted command.</summary>
    public OriginalCommandInvocation CaptureOriginalCommandWithinSource(
        IDeveloperOriginalProjectCommandRead sameRead, DeveloperProjectReference sameProject,
        DeveloperCanonicalActionContext sameAction,
        Func<Task<DeveloperOperationResult<DeveloperActionObservation>>> originalCommand,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalCommand);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? actual = null;
        var errors = new List<Exception>();
        try
        {
            Scope(() => FilesOriginalSourceCallbackScope.Invoke(() =>
            {
                token.ThrowIfCancellationRequested();
                lock (_gate) if (_retiring) throw new ObjectDisposedException(nameof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
                if (!_reads.IsIssuedOriginalCommandRead(sameRead))
                    throw new UnauthorizedAccessException("No SAME current privately issued command READ exists.");
                DemandDescriptor(sameRead.OriginalDescriptor, sameProject, sameAction);
                var previous = _command.Value;
                _command.Value = new(sameRead, sameProject, sameAction);
                try
                {
                    actual = originalCommand() ?? throw new InvalidOperationException("The actual Dev command returned no Task.");
                    retainOriginalTask(actual);
                }
                finally { _command.Value = previous; }
            }, originalSynchronousScope));
        }
        catch (Exception error) { Add(errors, error); }
        if (actual is null) { Throw(errors); throw new InvalidOperationException("No actual Dev command was acquired."); }
        return new(actual, errors.ToArray(), () =>
        { DemandExternalOriginalExecutionTrustJoin(); sameRead.DemandExternalOriginalJoin(); });
    }

    public bool IsBoundToOriginalToolOwner(ITaskRunToolActionOwner sameOwner) => ReferenceEquals(_tools, sameOwner);
    public Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(false); }
    public void RequestOriginalExecutionRetirement() { lock (_gate) _retiring = true; }
    public void DemandExternalOriginalExecutionBindingJoin()
    {
        if (_physical?.ContainsKey(this) == true)
            throw new InvalidOperationException("An actual current-project execution callback cannot join its bridge.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live))
                throw new InvalidOperationException("A live current-project execution original cannot join its own bridge.");
    }
    public void DemandExternalOriginalExecutionTrustJoin()
    {
        DemandExternalOriginalExecutionBindingJoin();
        _native.DemandExternalOriginalJoin();
        Volatile.Read(ref _actualConsents)?.DemandExternalOriginalProcessStartConsentJoin();
    }
    private void Scope(Action callback)
    {
        var active = _physical ??= []; active.TryGetValue(this, out var depth); active[this] = depth + 1;
        try { callback(); } finally { if (depth == 0) active.Remove(this); else active[this] = depth; }
    }
    private Task<T> Start<T>(Action<Action> parentScope, Action<Task> parentRetain,
        Func<Original, FilesOriginalReadSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
        Original original; FilesOriginalParentOperation pair; TaskCompletionSource start; Task<T> driver;
        lock (_gate)
        {
            if (_retiring || _originals.Count >= 512)
                throw new InvalidOperationException("Current-project execution custody is sealed or full.");
            original = new(_executing.Value);
            pair = new(Scope, task => { lock (_gate) original.Raw.Add(task); }, parentScope, parentRetain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Driver = driver = Drive(); _originals.Add(original);
        }
        pair.Publish(driver); start.TrySetResult(); return driver;
        async Task<T> Drive()
        {
            await start.Task.ConfigureAwait(false);
            var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
            var errors = new List<Exception>(); Task<T>? actual = null; T value = default!;
            // Productive admission belongs to the actual factory callback. Parent may
            // seal us before invoking it. Raw enrollment and fixed owed cleanup retain
            // the physical-only pair and remain independently reachable after sealing.
            var productive = new FilesOriginalReadSourceScope(callback =>
            {
                try
                {
                    DemandProductive();
                    pair.Sources.OriginalSynchronousScope(() => { DemandProductive(); callback(); });
                }
                catch { Volatile.Write(ref original.CallbackFailed, true); throw; }
            }, task =>
            {
                try { pair.Sources.RetainOriginalTask(task); }
                catch { Volatile.Write(ref original.CallbackFailed, true); throw; }
            });
            try
            {
                try { pair.DemandPublication(); value = await productive.Observe(() => actual = body(original, productive)).ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, actual, error); }
                Task[] raw; lock (_gate) raw = original.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                foreach (var task in raw) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
                if (actual?.IsCanceled == true && !Volatile.Read(ref original.CallbackFailed) && raw.All(task => !task.IsFaulted) && errors.All(Canceled))
                    ExceptionDispatchInfo.Capture(errors[0]).Throw();
                Throw(errors); return value;
            }
            finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
        }
    }
    private void DemandProductive()
    {
        lock (_gate) if (_retiring)
            throw new ObjectDisposedException(nameof(FilesDeveloperOriginalCurrentProjectExecutionBridge));
    }
    private static void DemandDescriptor(IDeveloperOriginalCurrentProjectDescriptor descriptor,
        DeveloperProjectReference project, DeveloperCanonicalActionContext action)
    {
        if (descriptor.WorkspaceId != project.WorkspaceId || descriptor.WorkspaceRevision != project.WorkspaceRevision ||
            descriptor.ProjectId != project.ProjectId || descriptor.ProjectRevision != project.ProjectRevision ||
            descriptor.RootId != project.RootId || descriptor.RepositoryBindingId != project.RepositoryBindingId ||
            JsonSerializer.Deserialize<DeveloperProjectReference>(descriptor.ExactProjectReferenceJson, Json) != project ||
            descriptor.OriginalConversation.Id != action.ContextId || descriptor.OriginalConversation.ContainerId != descriptor.OriginalContainer.Id ||
            descriptor.OriginalContainer.RootPath != descriptor.RegisteredProjectRoot || action.TaskId == Guid.Empty ||
            action.ExecutionId == Guid.Empty || action.AttemptId == Guid.Empty || action.ActionId == Guid.Empty || action.PersistenceRevision < 1)
            throw new UnauthorizedAccessException("The command READ/project/actual Task conversation do not form one original request.");
    }
    private static bool Canceled(Exception error) => error is OperationCanceledException;
    private static void AddTask(List<Exception> errors, Task? actual, Exception observed)
    {
        Add(errors, observed);
        if (actual?.Exception is { } group)
            foreach (var error in group.InnerExceptions) Add(errors, error);
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var cause in group.InnerExceptions) Add(errors, cause); }
        else if (!errors.Any(cause => ReferenceEquals(cause, error))) errors.Add(error);
    }
    private static void Throw(List<Exception> errors)
    { if (errors.Count != 0) throw new AggregateException("Original current-project execution sources did not settle.", errors); }
}
