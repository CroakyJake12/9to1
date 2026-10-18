using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces.Builder;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>A retained canonical details content surface. The caller supplies actual Home
/// readiness and authorization. This page grants no resource access and owns no registry.
/// Its session owns routing and umbrella preparation. Standalone/web delivery and the full
/// Builder remain separate acceptance requirements.</summary>
public sealed partial class SpaceDetailsPage : UserControl, IDesktopOriginalRetirementParticipant,
    IDesktopOriginalRetirementJoinGuard, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SpaceDetailsController _controller;
    private readonly SpaceDetailsCuiBindings _bindings;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiSceneHost _editorHost;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly Func<Task, bool>? _queueBack;
    private Task? _activation;
    private Task? _editorClose;
    private Task<CuiSceneAvailability>? _editorShow;
    private bool _live = true;
    private bool _activated;
    private long _editEpoch;
    private int _controllerOperations;
    private ClosePreparation? _lease;
    private Task<ClosePreparation?>? _preparation;
    private Decision? _decision;
    private readonly List<Decision> _decisions = [];

    public SpaceDetailsPage(SpaceRegistry registry, Guid spaceId, ICuiSceneReadiness readiness)
        : this(registry, spaceId, readiness, null) { }

    public SpaceDetailsPage(SpaceRegistry registry, Guid spaceId, ICuiSceneReadiness readiness, Func<Task, bool>? queueBack)
    {
        Dispatcher.UIThread.VerifyAccess();
        _controller = new(registry, spaceId);
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _queueBack = queueBack;
        var document = SpaceDetailsCuiDocument.Load();
        _editorHost = new();
        _work = new(StopAsync, CleanupAsync);
        _bindings = new(() => _controller.State, (field, value) => TryEdit(field, value),
            DispatchEditorAsync, CanMutate, () => _queueBack is not null);
        _controller.StateChanged += OnStateChanged;
        Document = document;
        Content = _editorHost;
    }

    public CuiDocument Document { get; }
    public SpaceDetailsState State => _controller.State;
    public Task? OriginalClose => _work.OriginalClose;
    internal CuiSceneHost EditorHost => _editorHost;
    internal Task<CuiSceneAvailability>? EditorShow => _editorShow;
    internal CuiSceneHost? DecisionHost { get { lock (_gate) return _decision?.Host; } }
    internal Task<CuiSceneAvailability>? DecisionShow { get { lock (_gate) return _decision?.Show; } }

    // Publication lifetime deliberately does not depend on a reversible mutation lease.
    // This predicate reads no native property and acquires no owner locks.
    private bool IsPresentationCurrent() => Volatile.Read(ref _live);
    private bool CanMutate()
    {
        lock (_gate) return _live && _activated && !_work.IsRetiring && _lease is null;
    }

    public Task ActivateAsync(CancellationToken token = default)
    {
        lock (_gate)
        {
            _work.DemandAdmission();
            return _activation ??= _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, original.Token);
                await original.AwaitAsync(Source(() => _controller.LoadAsync(linked.Token))).ConfigureAwait(false);
                Task<CuiSceneAvailability>? show = null;
                var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
                {
                    original.DemandPublication();
                    lock (_gate) _activated = true;
                    _bindings.Refresh();
                    show = _editorShow = Source(() => _editorHost.ShowAsync(new("spaces.details", "Space details", "Space details",
                        Document, _bindings, _bindings, _readiness) { IsPublicationCurrent = IsPresentationCurrent }, linked.Token));
                }).GetTask();
                try { await original.AwaitAsync(dispatch).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(dispatch, error); }
                if (show is not null)
                    try { await original.AwaitAsync(show).ConfigureAwait(false); }
                    catch (Exception error) { original.Capture(show, error); }
                original.ThrowRetained();
            });
        }
    }

    /// <summary>The only synchronous controller mutation admission; the controller itself
    /// is not exposed. Ordinary saves continue accepting later edits.</summary>
    public bool TryEdit(SpaceDetailsField field, string value)
    {
        lock (_gate)
        {
            if (!CanMutate()) return false;
            var accepted = false;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                original.DemandPublication();
                accepted = Source(() => _controller.SetField(field, value));
                if (accepted) _editEpoch++;
            });
            return accepted;
        }
    }

    public Task<bool> SaveAsync(CancellationToken token = default) => RunControllerAsync(_controller.SaveAsync, token);
    public Task<bool> DiscardChangesAsync(CancellationToken token = default) => RunControllerAsync(_controller.DiscardChangesAsync, token);
    public Task<bool> ReloadAsync(CancellationToken token = default) => RunControllerAsync(_controller.LoadAsync, token);

    private Task<bool> RunControllerAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken caller)
    {
        lock (_gate)
        {
            if (!CanMutate()) return Task.FromResult(false);
            _controllerOperations++;
            try { return _work.RunAsync(async original =>
            {
                try
                {
                    original.BindPublicationGuard(IsPresentationCurrent);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token);
                    return await original.AwaitAsync(Source(() => operation(linked.Token))).ConfigureAwait(false);
                }
                finally { lock (_gate) _controllerOperations--; }
            }); }
            catch { _controllerOperations--; throw; }
        }
    }

    private async ValueTask DispatchEditorAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == "space-details.back")
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task back;
            lock (_gate)
            {
                if (!CanMutate() || _queueBack is null) throw new InvalidOperationException("This editor cannot return to Spaces.");
                back = _work.RunAsync(async original =>
                {
                    await start.Task.ConfigureAwait(false);
                    original.BindPublicationGuard(IsPresentationCurrent);
                    Task? pipeline = null;
                    await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        original.DemandPublication();
                        pipeline = _editorHost.CaptureOriginalActionSettlement(_editorShow!);
                    }).GetTask()).ConfigureAwait(false);
                    var settlement = Task.WhenAll(pipeline!, original.Task);
                    if (!_queueBack(settlement)) throw new InvalidOperationException("The session refused the return to Spaces.");
                });
            }
            start.SetResult(); // Actual callbacks run after atomic owner admission releases its gate.
            await back.ConfigureAwait(false);
            return;
        }
        var actual = command switch
        {
            "space-details.save" => SaveAsync(token),
            "space-details.discard" => DiscardChangesAsync(token),
            "space-details.reload" => ReloadAsync(token),
            _ => throw new InvalidOperationException("This details action is not owned by this page.")
        };
        await actual.ConfigureAwait(false); // false remains visible in the canonical controller status.
    }

    private void OnStateChanged(SpaceDetailsState state) => RefreshBindings();

    private void RefreshBindings() => _ = RefreshBindingsAsync();

    private Task RefreshBindingsAsync()
    {
        lock (_gate)
        {
            if (!_live || _work.IsRetiring) return Task.CompletedTask;
            return _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(IsPresentationCurrent);
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() =>
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        _bindings.Refresh();
                        _decision?.Bindings.Refresh();
                    })).GetTask()).ConfigureAwait(false);
            }); // The lifetime retains and joins this exact refresh; it is not an unowned post.
        }
    }

    private T Source<T>(Func<T> acquire) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, acquire);

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        _work.DemandExternalClose();
    }

    /// <summary>Unconditional original-work cleanup for an abandoned acquisition/shutdown.
    /// This is not user approval to discard details; user navigation must commit a valid lease.</summary>
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate) return _work.CloseAndDrainAsync();
    }
    public void RequestRetirement() { lock (_gate) _work.RequestRetirement(); }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private Task StopAsync()
    {
        lock (_gate)
        {
            Volatile.Write(ref _live, false);
            _controller.StateChanged -= OnStateChanged;
            _lease = null;
            _decision?.Answer.TrySetResult(false);
            return _editorClose ??= Source(_editorHost.CloseOriginalAsync);
        }
    }

    private async Task CleanupAsync()
    {
        List<Exception> failures = [];
        if (_editorClose is { } editorClose)
            try { await editorClose.ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
        foreach (var decision in _decisions.ToArray())
        {
            try { await CloseDecisionAsync(decision).ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException("The original details surfaces did not close cleanly.", failures);
        await Source(() => Dispatcher.UIThread.InvokeAsync(() => Content = null).GetTask()).ConfigureAwait(false);
    }
}
