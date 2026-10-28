using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Mesh;

/// <summary>Thin Avalonia host for the Haven-native Mesh devices and Work Mode scene.</summary>
public sealed class MeshPage : UserControl, IDisposable, IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly MeshHavenScene _scene;
    private readonly MeshPageViewModel _viewModel;
    private bool _disposed;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _constructed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _initialization, _latestAction;
    private bool _initialized;
    private int _pendingBodies;
    private long _generation;
    private Action<Action>? _originalParent;
    private Func<bool>? _canAdmit;
    [ThreadStatic] private static List<MeshPage>? _physicalOwners;

    public MeshPage(MeshPageViewModel viewModel, Action<MeshPage>? captureOriginalOwner = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _work = new(StopOriginalAsync, CleanupOriginalAsync);
        try
        {
        InvokeOriginal(() => { captureOriginalOwner?.Invoke(this); return true; });
        _work.DemandAdmission();
        _viewModel.BindOriginalPresentation(RunOriginalAsync, () => CanAdmit);
        _scene = new MeshHavenScene(viewModel, RunOriginalAsync);
        _scene.ClipboardSendRequested += OnClipboardSendRequested;
        _scene.FileSendRequested += OnFileSendRequested;
        _scene.ClipboardApplyRequested += OnClipboardApplyRequested;
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeMeshPage");
        AutomationProperties.SetName(this, "9to1 Mesh and Work Mode");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeMeshScene");
        AutomationProperties.SetName(Scene, "Mesh device and AI team management");
        Content = Scene;
        Loaded += OnLoaded;
        _work.DemandAdmission();
        }
        catch { _work.RequestRetirement(); throw; }
        finally { _constructed.TrySetResult(); }
    }

    public HavenSceneControl Scene { get; }
    internal MeshHavenScene HavenScene => _scene;

    private void OnClipboardSendRequested(Guid deviceId) => Observe(RunOriginalAsync(async token =>
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { SetStatus("The platform clipboard is unavailable."); return; }
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard) ?? string.Empty;
            await _viewModel.SendClipboardAsync(deviceId, text, token);
            _scene.RenderCurrent();
        }
        catch (Exception ex) { SetStatus("Could not send clipboard: " + ex.Message); throw; }
    }));

    private void OnFileSendRequested(Guid deviceId) => Observe(RunOriginalAsync(async token =>
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) { SetStatus("The platform file picker is unavailable."); return; }
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Send file with 9to1 Mesh", AllowMultiple = false });
            var file = files.FirstOrDefault();
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek)
            {
                await _viewModel.SendFileAsync(deviceId, file.Name, stream, token);
            }
            else
            {
                var temporary = Path.Combine(Path.GetTempPath(), "haven-mesh-send-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await using (var buffered = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[64 * 1024];
                        long total = 0;
                        while (true)
                        {
                            var read = await stream.ReadAsync(buffer, token);
                            if (read <= 0) break;
                            total += read;
                            if (total > MeshCoordinator.MaximumFileBytes)
                                throw new InvalidDataException($"Mesh files are limited to {MeshCoordinator.MaximumFileBytes / 1024 / 1024} MiB.");
                            await buffered.WriteAsync(buffer.AsMemory(0, read), token);
                        }
                        buffered.Position = 0;
                        await _viewModel.SendFileAsync(deviceId, file.Name, buffered, token);
                    }
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            _scene.RenderCurrent();
        }
        catch (Exception ex) { SetStatus("Could not send file: " + ex.Message); throw; }
    }));

    private void OnClipboardApplyRequested(string text) => Observe(RunOriginalAsync(async token =>
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { SetStatus("The platform clipboard is unavailable."); return; }
            await clipboard.SetTextAsync(text);
            SetStatus("Copied the received Mesh value to this device's clipboard.");
        }
        catch (Exception ex) { SetStatus("Could not update the clipboard: " + ex.Message); throw; }
    }));

    private void SetStatus(string status)
    {
        _viewModel.SetStatus(status);
        _scene.RenderCurrent();
    }

    internal Task? OriginalClose => _work.OriginalClose;
    internal bool CanRetireFailedBrowseInitialization(Task? originalInitialization, Task actualClose) =>
        ReferenceEquals(originalInitialization, _initialization) &&
        _work.CanRetireAfterFailedInitialization(originalInitialization, actualClose);
    internal Task? LatestOriginalAction => _latestAction;
    internal void BindOriginalBrowsePresentation(Action<Action> parent, Func<bool> canAdmit)
    {
        ArgumentNullException.ThrowIfNull(parent); ArgumentNullException.ThrowIfNull(canAdmit);
        if (_constructed.Task.IsCompleted || _originalParent is not null)
            throw new InvalidOperationException("Bind the Mesh Browse parent once during constructor capture.");
        _originalParent = parent; _canAdmit = canAdmit;
    }
    private bool CanAdmit => !_disposed && !_work.IsRetiring && _canAdmit?.Invoke() != false;
    private T InvokeOriginal<T>(Func<T> source)
    {
        var owners = _physicalOwners ??= []; owners.Add(this);
        try
        {
            T result = default!;
            if (_originalParent is { } parent) parent(() => result = source());
            else result = source();
            return result;
        }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    private Task RunOriginalAsync(Func<CancellationToken, Task> body)
    {
        if (!CanAdmit) throw new ObjectDisposedException(nameof(MeshPage));
        if (_work.Executing is { } current) return RunBodyAsync(current, body);
        return _work.RunAsync(original => RunBodyAsync(original, body), actual => _latestAction = actual);
    }
    private async Task RunBodyAsync(DesktopOriginalWorkLifetime.Original original, Func<CancellationToken, Task> body)
    {
        original.BindPublicationGuard(() => CanAdmit);
        Interlocked.Increment(ref _pendingBodies); Interlocked.Increment(ref _generation);
        try
        {
            var dispatch = InvokeOriginal(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
            {
                original.DemandPublication();
                return InvokeOriginal(() => body(original.Token));
            }).GetTask());
            var actual = await original.AwaitAsync(dispatch);
            await original.AwaitAsync(actual);
        }
        finally { Interlocked.Decrement(ref _pendingBodies); }
    }
    private static async void Observe(Task actual)
    { try { await actual; } catch { /* The original page lifetime retains the failure. */ } }
    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Observe(InitializeAsync());
    public Task InitializeAsync(CancellationToken token = default) => _initialization ??= RunOriginalAsync(async originalToken =>
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, originalToken);
        await _viewModel.InitialiseAsync(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        _work.Executing!.DemandPublication();
        _scene.RenderCurrent(); _initialized = true;
    });
    internal void DemandOriginalInitializedDocument()
    {
        if (!CanAdmit || !_initialized || _initialization is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("Mesh has not finished its original initialization.");
    }
    internal void PublishOriginalDocumentTab(Action publish)
    {
        DemandOriginalInitializedDocument();
        InvokeOriginal(() => { publish(); return true; });
    }
    public Task<bool> PrepareToCloseAsync(string reason, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        return Task.FromResult(CanAdmit && _initialized && Volatile.Read(ref _pendingBodies) == 0);
    }
    private sealed record CloseFence(MeshPage Owner, long Generation);
    internal object? CaptureOriginalBrowseCloseFence() => CanAdmit && _initialized && Volatile.Read(ref _pendingBodies) == 0
        ? new CloseFence(this, Volatile.Read(ref _generation)) : null;
    internal bool IsOriginalBrowseCloseFenceCurrent(object value) => value is CloseFence fence &&
        ReferenceEquals(fence.Owner, this) && fence.Generation == Volatile.Read(ref _generation) &&
        CanAdmit && _initialized && Volatile.Read(ref _pendingBodies) == 0;
    internal Task BeginOriginalPreparedBrowseClose(object fence)
    {
        DemandExternalOriginalRetirementJoin();
        if (!IsOriginalBrowseCloseFenceCurrent(fence))
            throw new InvalidOperationException("Mesh admitted another operation after close preparation.");
        _work.RequestRetirement(); return _work.OriginalClose!;
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalOwners?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An active Mesh callback cannot join its own page retirement.");
        _work.DemandExternalClose();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task StopOriginalAsync()
    {
        await _constructed.Task;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Loaded -= OnLoaded;
            if (_scene is not null)
            {
                _scene.ClipboardSendRequested -= OnClipboardSendRequested;
                _scene.FileSendRequested -= OnFileSendRequested;
                _scene.ClipboardApplyRequested -= OnClipboardApplyRequested;
            }
            _viewModel.Dispose(); // Unsubscribe only; the shared MeshCoordinator stays alive.
        }).GetTask();
    }
    private async Task CleanupOriginalAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _disposed = true;
            if (Scene is not null) Scene.Root = null;
            _scene?.Dispose(); Content = null;
        }).GetTask();
    }
}
