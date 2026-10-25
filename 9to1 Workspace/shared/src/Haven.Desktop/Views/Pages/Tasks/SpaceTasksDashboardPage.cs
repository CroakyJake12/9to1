using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>
/// The authored CUI Tasks scene projected from actual conversations in one existing Space.
/// The coordinator/repositories and genuine new-work/open factories are borrowed. This
/// page never creates a context, Task, Run, route, permission or provider. Closing withdraws
/// its own admission and joins actual reads/open callbacks; it does not cancel business work.
/// Accepted CUI action and loader-terminal cohorts are joined before detaching the actual
/// root. These joins do not claim native frame, input or independent acceptance.
/// Constructor-before-return acquisition remains the shell owner's responsibility.
/// </summary>
public sealed class SpaceTasksDashboardPage : UserControl, IActivatablePage, IDisposable,
    IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly SpaceTaskWorkspaceService _source;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly Guid _spaceId;
    private readonly Func<SpaceTaskObservation, Task> _openExisting;
    private readonly Func<string, Task>? _startNew;
    private readonly Func<Task>? _openBlank;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly SpaceTasksDashboardCuiScene _scene;
    private readonly object _gate = new();
    [ThreadStatic] private static List<SpaceTasksDashboardPage>? _physicalSources;
    private IReadOnlyList<SpaceTaskObservation> _observations = [];
    private bool _active, _subscribed, _newWorkPending;
    private long _generation, _readSequence;
    internal Task? ActualNewWork { get; private set; }

    public SpaceTasksDashboardPage(SpaceTaskWorkspaceService source, TaskExecutionCoordinator canonical,
        Guid actualSpaceId, Func<SpaceTaskObservation, Task> openExistingTask,
        Func<string, Task>? startExplicitNewTask = null, Func<Task>? openBlankTask = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        if (actualSpaceId == Guid.Empty) throw new ArgumentException("An existing Space ID is required.", nameof(actualSpaceId));
        _spaceId = actualSpaceId;
        _openExisting = openExistingTask ?? throw new ArgumentNullException(nameof(openExistingTask));
        _startNew = startExplicitNewTask; _openBlank = openBlankTask;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        _scene = new(actualSpaceId, OwnSceneCallback, DemandOriginalSceneWrite);
        Scene = _scene;
        AutomationProperties.SetAutomationId(this, "SpaceCanonicalTaskDashboard");
        AutomationProperties.SetName(this, "Existing Space tasks");
        AutomationProperties.SetAutomationId(Scene, "SpaceCanonicalTaskDashboardScene");
        AutomationProperties.SetName(Scene, "Tasks, recorded progress and recovery");
        Content = Scene;
        _scene.DelegateRequested += OnDelegateRequested;
        _scene.NewBlankTaskRequested += OnBlankRequested;
        _scene.RecentTaskRequested += OnOpenRequested;
        _scene.RefreshRequested += OnRefreshRequested;
    }

    public SpaceTasksDashboardCuiScene Scene { get; }
    internal SpaceTasksDashboardCuiScene OriginalScene => _scene;
    public IReadOnlyList<SpaceTaskObservation> OriginalObservations => _observations;
    private bool IsCurrent(long generation)
    { lock (_gate) return _active && !_work.IsRetiring && _generation == generation; }
    private void DemandOriginalSceneWrite()
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No actual dashboard original owns this scene write.");
        original.DemandPublication();
        if (!IsCurrent(_generation)) throw new InvalidOperationException("The original Space task presentation was withdrawn.");
    }
    private void OwnSceneCallback(Action callback)
    {
        long generation; lock (_gate) generation = _generation;
        InvokePhysicalSource(() =>
        {
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); callback();
            });
            return true;
        });
    }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await DispatchOriginalAsync(original, _scene.Initialize).ConfigureAwait(false);
            original.DemandPublication();
            lock (_gate)
            {
                original.DemandPublication();
                if (!_subscribed)
                { _canonical.SnapshotChanged += OnSnapshotChanged; _subscribed = true; }
            }
            await ReadOriginalPageAsync(original, generation, scope.Token).ConfigureAwait(false);
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }
    internal Task RefreshOriginalAsync(CancellationToken cancellationToken = default)
    {
        long generation; lock (_gate) generation = _generation;
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await ReadOriginalPageAsync(original, generation, scope.Token).ConfigureAwait(false);
        });
    }
    private async Task ReadOriginalPageAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        long sequence; lock (_gate) sequence = ++_readSequence;
        try
        {
            original.DemandPublication();
            var rows = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadPageAsync(_spaceId, 40, token, OwnNestedOriginalSource))).ConfigureAwait(false);
            await DispatchOriginalAsync(original, () =>
            {
                original.DemandPublication();
                lock (_gate) { if (_readSequence != sequence) return; }
                _observations = rows; // Capture actual source observations before presentation callbacks.
                void DemandRead()
                {
                    original.DemandPublication();
                    lock (_gate) if (_readSequence != sequence) throw new InvalidOperationException("The original dashboard read was superseded.");
                }
                _scene.SetRecent(rows, DemandRead);
                _scene.SetStartAvailability(!_newWorkPending && _startNew is not null, !_newWorkPending && _openBlank is not null, DemandRead);
                _scene.SetStatus(_startNew is null || _openBlank is null
                    ? "Some new-task entrypoints are unconfigured. Existing tasks open with their recorded Task/Run; saved state is not a live worker witness."
                    : "Up to 40 task conversations in this Space. Each tile opens the same task and run. Saved state does not establish live work or verification.", DemandRead);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            original.Retain(error);
            if (IsCurrent(generation))
            {
                try { await DispatchOriginalAsync(original, () =>
                    { lock (_gate) { if (_readSequence != sequence) return; } _scene.SetStatus("The actual Space task observation failed. Refresh before interpreting saved state or opening a task."); }).ConfigureAwait(false); }
                catch (Exception publicationError) { original.Retain(publicationError); }
            }
            original.ThrowRetained();
        }
    }
    private void OnSnapshotChanged(object? sender, TaskExecutionSnapshot snapshot)
    {
        long generation;
        lock (_gate) { if (!_active || _work.IsRetiring) return; generation = _generation; }
        // A canonical ACK is an observation. Fresh Space membership comes from the real read.
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
        });
    }
    private void OnRefreshRequested(object? sender, EventArgs args) => _ = RefreshOriginalAsync();
    private void OnOpenRequested(object? sender, SpaceTaskObservation captured)
    {
        if (!_observations.Any(row => ReferenceEquals(row, captured))) throw new InvalidOperationException("This task tile changed before opening.");
        var contextId = captured.Conversation.Id;
        long generation; lock (_gate) generation = _generation;
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            try
            {
                var current = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadAsync(_spaceId, contextId, original.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                if (current.Snapshot?.TaskId != captured.Snapshot?.TaskId || current.Snapshot?.ExecutionId != captured.Snapshot?.ExecutionId)
                    throw new InvalidOperationException("The recorded Task/Run changed; refresh this dashboard before opening it.");
                original.DemandPublication();
                // The genuine shell callback receives SAME observed identities; it must not call
                // ConfigureTaskMode/Submit to reopen, create a new task, or relabel this result.
                await original.AwaitAsync(InvokePhysicalSource(() => _openExisting(current))).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                {
                    try { await DispatchOriginalAsync(original, () => _scene.SetStatus("The original task could not open. Refresh its identities; this did not create replacement work.")).ConfigureAwait(false); }
                    catch (Exception publicationError) { original.Retain(publicationError); }
                }
                original.ThrowRetained();
            }
        });
    }
    private void OnDelegateRequested(object? sender, string instruction)
    {
        if (_startNew is null) throw new InvalidOperationException("The genuine explicit new-task owner is unconfigured.");
        StartOriginalNewWork(() => _startNew(instruction), true);
    }
    private void OnBlankRequested(object? sender, EventArgs e)
    {
        if (_openBlank is null) throw new InvalidOperationException("The genuine blank-task opener is unconfigured.");
        StartOriginalNewWork(_openBlank, false);
    }
    private void StartOriginalNewWork(Func<Task> source, bool clearInstruction)
    {
        var instructionVersion = _scene.InstructionVersion;
        long generation; lock (_gate) generation = _generation;
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            ActualNewWork = original.Task; // Same prepublished owner driver, never an ACK proxy.
            lock (_gate)
            {
                if (_newWorkPending) throw new InvalidOperationException("An original new-work opener is already pending.");
                _newWorkPending = true;
            }
            try
            {
                await DispatchOriginalAsync(original, () => _scene.SetBusy(true)).ConfigureAwait(false);
                original.DemandPublication();
                // The original callback is caller/business owned, with its original token.
                // This page does not substitute a view token or claim provider completion.
                await original.AwaitAsync(InvokePhysicalSource(source)).ConfigureAwait(false);
                await DispatchOriginalAsync(original, () =>
                {
                    original.DemandPublication();
                    if (clearInstruction) _scene.ClearInstruction(instructionVersion);
                    _scene.SetStatus("The original new-work opener returned; this is not a durable task-completion witness.");
                }).ConfigureAwait(false);
                await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                {
                    try { await DispatchOriginalAsync(original, () => _scene.SetStatus("The actual new-work opener failed. Its original cause is retained; no task completion or retry is inferred.")).ConfigureAwait(false); }
                    catch (Exception publicationError) { original.Retain(publicationError); }
                }
            }
            finally
            {
                lock (_gate) _newWorkPending = false;
                if (IsCurrent(generation))
                {
                    try
                    {
                        await DispatchOriginalAsync(original, () =>
                        {
                            original.DemandPublication(); _scene.SetBusy(false);
                            _scene.SetStartAvailability(_startNew is not null, _openBlank is not null);
                        }).ConfigureAwait(false);
                    }
                    catch (Exception error) { original.Retain(error); }
                }
            }
            original.ThrowRetained();
        });
    }
    private Task DispatchOriginalAsync(DesktopOriginalWorkLifetime.Original original, Action callback)
    {
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokePhysicalSource(() => { callback(); return true; })).GetTask());
        return original.AwaitAsync(actual);
    }
    private void OwnNestedOriginalSource(Action callback) =>
        InvokePhysicalSource(() => { callback(); return true; });
    private T InvokePhysicalSource<T>(Func<T> source)
    {
        var stack = _physicalSources ??= []; stack.Add(this);
        try { return source(); } finally { stack.RemoveAt(stack.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual dashboard source callback cannot join its encompassing close.");
        _work.DemandExternalClose();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; }
        if (_subscribed) { _canonical.SnapshotChanged -= OnSnapshotChanged; _subscribed = false; }
        _scene.DelegateRequested -= OnDelegateRequested;
        _scene.NewBlankTaskRequested -= OnBlankRequested;
        _scene.RecentTaskRequested -= OnOpenRequested;
        _scene.RefreshRequested -= OnRefreshRequested;
        return InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokePhysicalSource(() => { _scene.RequestRetirement(); return true; })).GetTask());
    }
    private async Task DetachOriginalPresentationAsync()
    {
        await InvokePhysicalSource(_scene.CloseAndDrainAsync).ConfigureAwait(false);
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() => InvokePhysicalSource(() =>
        {
            // Factor cleanup runs after independently joining all actual source/dispatcher tasks.
            if (ReferenceEquals(Content, Scene)) Content = null;
            return true;
        })).GetTask());
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) { if (actual.Exception is { InnerExceptions.Count: > 0 } causes) throw causes; System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); throw; }
    }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
