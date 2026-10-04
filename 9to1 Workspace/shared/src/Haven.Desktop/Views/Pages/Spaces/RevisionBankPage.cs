using System.Runtime.ExceptionServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>The acquired page owns its original operation and native publication tasks.
/// External retirement must join CloseAndDrainAsync before disposing the tab or provider.</summary>
public sealed class RevisionBankPage : UserControl, IActivatablePage, IDisposable, IAsyncDisposable
{
    private readonly object _taskGate = new();
    private readonly OwnedSpacesSession _originalOwner;
    private readonly Func<OwnedSpacesSession?> _currentOwner;
    private readonly RevisionBankScene _scene;
    private readonly List<Task> _originalTasks = [];
    private readonly int _maximumOriginalTasks;
    private readonly Guid _spaceId;
    private volatile bool _retiring;
    private volatile bool _active = true;
    private Task? _close;
    private RevisionBankSnapshot? _snapshot;

    public RevisionBankPage(OwnedSpacesSession originalOwner, Func<OwnedSpacesSession?> currentOwner,
        Guid spaceId, int maximumOriginalTasks = 64)
    {
        _originalOwner = originalOwner ?? throw new ArgumentNullException(nameof(originalOwner));
        _currentOwner = currentOwner ?? throw new ArgumentNullException(nameof(currentOwner));
        if (spaceId == Guid.Empty) throw new ArgumentException("A canonical Space ID is required.", nameof(spaceId));
        if (maximumOriginalTasks is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumOriginalTasks));
        _spaceId = spaceId;
        _maximumOriginalTasks = maximumOriginalTasks;
        RequireOriginalOwner();
        _scene = new RevisionBankScene(IsOriginalPublicationCurrent);
        Scene = new HavenSceneControl { Root = _scene.Root };
        try
        {
            AutomationProperties.SetAutomationId(this, "HavenNativeRevisionBankPage");
            AutomationProperties.SetName(this, "Revision Bank");
            Content = Scene;
            _scene.RefreshRequested += OnRefresh;
            _scene.MutationRequested += OnMutation;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { _scene.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Scene.Root = null; } catch (Exception error) { Add(failures, error); }
            Settle(failures);
            throw;
        }
    }

    public HavenSceneControl Scene { get; }
    public bool IsRetiring => _retiring;
    public Task? LastOriginalTask { get; private set; }
    public Task<RevisionBankMutationResult>? LastOriginalMutationTask { get; private set; }
    public RevisionBankMutationResult? LastAcknowledgedOriginalMutation { get; private set; }
    internal RevisionBankScene OriginalScene => _scene;
    internal RevisionBankSnapshot? OriginalSnapshot => _snapshot;
    internal IReadOnlyList<Task> OriginalTasks { get { lock (_taskGate) return _originalTasks.ToArray(); } }

    public Task ActivateAsync(CancellationToken cancellationToken) => RunOriginal(async () =>
    {
        _active = true;
        await RefreshCoreAsync(cancellationToken).ConfigureAwait(true);
    });

    public void Deactivate()
    {
        _active = false;
        _scene.WithdrawFrame();
    }

    public Task RefreshOriginalAsync(CancellationToken token = default) =>
        RunOriginal(() => RefreshCoreAsync(token));

    public Task MutateOriginalAsync(RevisionBankMutation request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Capture mutable caller arrays before admission or the first asynchronous boundary.
        var captured = request with { CategoryIds = request.CategoryIds?.ToArray() };
        return RunOriginal(async () =>
        {
            if (captured.SpaceId != _spaceId)
                throw new UnauthorizedAccessException("The mutation belongs to a different acquired Space.");
            RequireOriginalOwner();
            var mutation = _originalOwner.Registry.MutateRevisionBankAsync(captured, token);
            LastOriginalMutationTask = mutation;
            var acknowledged = await mutation.ConfigureAwait(false);
            // Keep the exact known result even when presentation has been withdrawn or replaced.
            LastAcknowledgedOriginalMutation = acknowledged;
            if (_retiring) return;
            RequireOriginalOwner();
            await RefreshCoreAsync(token).ConfigureAwait(false);
            await PublishOriginalAsync(() => _scene.SetStatus("Revision Bank saved.")).ConfigureAwait(false);
        });
    }

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        RequireOriginalOwner();
        var space = await _originalOwner.Registry.ReadExistingAsync(_spaceId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The acquired Space is unavailable.");
        var bank = await _originalOwner.Registry.ReadRevisionBankAsync(_spaceId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The acquired Revision Bank is unavailable.");
        if (space.Revision != bank.SpaceRevision)
            throw new SpaceRevisionConflictException(_spaceId, space.Revision, bank.SpaceRevision);
        if (_retiring) return;
        RequireOriginalOwner();
        _snapshot = bank;
        await PublishOriginalAsync(() => _scene.Render(space, bank)).ConfigureAwait(false);
    }

    private Task RunOriginal(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Task original;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_taskGate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(RevisionBankPage));
            _originalTasks.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_originalTasks.Count >= _maximumOriginalTasks)
                throw new InvalidOperationException("Revision Bank original task capacity is full; pending or failed originals were retained.");
            original = RunCoreAsync(start.Task, body);
            _originalTasks.Add(original);
            LastOriginalTask = original;
        }
        start.SetResult();
        return original;
    }

    private async Task RunCoreAsync(Task start, Func<Task> body)
    {
        await start.ConfigureAwait(true);
        try
        {
            if (_retiring) throw new ObjectDisposedException(nameof(RevisionBankPage));
            RequireOriginalOwner();
            if (_retiring) throw new ObjectDisposedException(nameof(RevisionBankPage));
            await body().ConfigureAwait(false);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                await PublishOriginalAsync(() => _scene.SetStatus(primary.Message)).ConfigureAwait(false);
            }
            catch (Exception diagnostic) { Add(failures, diagnostic); }
            Settle(failures);
        }
    }

    private async Task PublishOriginalAsync(Action publication)
    {
        void Publish()
        {
            if (!IsOriginalPublicationCurrent()) return;
            publication();
            _ = IsOriginalPublicationCurrent();
        }
        if (Dispatcher.UIThread.CheckAccess()) Publish();
        else await Dispatcher.UIThread.InvokeAsync(Publish);
    }

    private bool IsOriginalPublicationCurrent()
    {
        if (_retiring || !_active) return false;
        RequireOriginalOwner();
        return !_retiring && _active;
    }

    private void RequireOriginalOwner()
    {
        if (!ReferenceEquals(_currentOwner(), _originalOwner))
            throw new UnauthorizedAccessException("The acquired original Spaces owner was replaced.");
        _originalOwner.RequireCurrent();
        if (!ReferenceEquals(_currentOwner(), _originalOwner))
            throw new UnauthorizedAccessException("The acquired original Spaces owner changed during observation.");
    }

    private void OnRefresh(object? sender, EventArgs args) => _ = RefreshOriginalAsync();
    private void OnMutation(object? sender, RevisionBankMutation request) => _ = MutateOriginalAsync(request);

    public void RequestRetirement()
    {
        _retiring = true;
        _active = false;
    }

    public Task CloseAndDrainAsync()
    {
        Task originalClose;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_taskGate)
        {
            if (_close is not null) return _close;
            originalClose = CloseCoreAsync(start.Task);
            _close = originalClose; // Publish the same close before withdrawal or any native callbacks.
            RequestRetirement();
        }
        start.SetResult();
        return originalClose;
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task[] originals;
        lock (_taskGate) originals = _originalTasks.ToArray();
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            try { await original.ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error); }
        }
        await AttemptAsync(() => { _scene.RefreshRequested -= OnRefresh; _scene.MutationRequested -= OnMutation; });
        await AttemptAsync(_scene.Dispose);
        await AttemptAsync(() => Scene.Root = null);
        await AttemptAsync(() => Content = null);
        lock (_taskGate) _originalTasks.RemoveAll(task => task.IsCompletedSuccessfully);
        Settle(failures);

        async Task AttemptAsync(Action cleanup)
        {
            try
            {
                if (Dispatcher.UIThread.CheckAccess()) cleanup();
                else await Dispatcher.UIThread.InvokeAsync(cleanup);
            }
            catch (Exception error) { Add(failures, error); }
        }
    }

    private static void Add(List<Exception> failures, Exception error)
    {
        if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
    }

    private static void Settle(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
