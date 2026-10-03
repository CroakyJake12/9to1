using Avalonia.Controls;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Desktop.Views.Pages.Maps;

/// <summary>Actual Maps host companion for the original Home-owned Journey library.
/// Existing Maps search/tiles/Data-backed places remain the same owning page.</summary>
public sealed class MapsNativeJourneyHost : UserControl, IDisposable
{
    private readonly Control _legacy;
    private readonly HomeMapsLibraryOwner? _owner;
    private readonly IAuthenticatedResourceActorSource? _actors;
    private readonly Func<bool> _isOriginalHostCurrent;
    private readonly Func<string, AuthenticatedResourceActor, Task> _openHome;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _opening = new(1, 1);
    private readonly object _tasks = new();
    private readonly HashSet<Task> _accepted = new();
    private readonly ContentControl _view = new();
    private MapsJourneyLibraryPanel? _journeys;
    private MapsLibraryDisplay? _originalDisplay;
    private AuthenticatedResourceActor? _originalActor;
    private int _disposed;
    public Button JourneyButton { get; } = new() { Content = "Journey library" };
    public Button MapButton { get; } = new() { Content = "Map" };
    public TextBlock Status { get; } = new();
    public MapsJourneyLibraryPanel? JourneyPanel => _journeys;
    public Control LegacyMapsPage => _legacy;
    public bool IsRetired
    {
        get
        {
            if (Volatile.Read(ref _disposed) != 0) return true;
            try { return !_isOriginalHostCurrent(); } catch { return true; }
        }
    }
    public MapsNativeJourneyHost(Control legacyMap, HomeMapsLibraryOwner? originalOwner,
        IAuthenticatedResourceActorSource? originalActors, Func<bool> isOriginalHostCurrent,
        Func<string, AuthenticatedResourceActor, Task> openActualHomeReview)
    {
        _legacy = legacyMap ?? throw new ArgumentNullException(nameof(legacyMap));
        _owner = originalOwner; _actors = originalActors;
        _isOriginalHostCurrent = isOriginalHostCurrent ?? throw new ArgumentNullException(nameof(isOriginalHostCurrent));
        _openHome = openActualHomeReview ?? throw new ArgumentNullException(nameof(openActualHomeReview));
        var tools = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        tools.Children.Add(MapButton); tools.Children.Add(JourneyButton); tools.Children.Add(Status);
        var layout = new DockPanel(); DockPanel.SetDock(tools, Dock.Top); layout.Children.Add(tools);
        _view.Content = _legacy; layout.Children.Add(_view); Content = layout;
        JourneyButton.IsEnabled = originalOwner is not null && originalActors is not null;
        if (!JourneyButton.IsEnabled) Status.Text = "Journey library unavailable.";
        JourneyButton.Click += (_, _) => Accept(OpenJourneysAsync);
        MapButton.Click += (_, _) => { if (!IsRetired) _view.Content = _legacy; else Dispose(); };
    }
    public Task WhenActionsIdleAsync() { lock (_tasks) return Task.WhenAll(_accepted.ToArray()); }
    private void Accept(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_tasks) _accepted.Add(completion.Task);
        _ = RunAsync();
        async Task RunAsync()
        {
            try { await action(); }
            catch (OperationCanceledException) when (IsRetired) { }
            catch (Exception error) { if (IsRetired) Dispose(); else Status.Text = error.Message; }
            finally { completion.TrySetResult(); lock (_tasks) _accepted.Remove(completion.Task); }
        }
    }
    public async Task OpenJourneysAsync()
    {
        var ct = _lifetime.Token;
        RequireHost();
        if (_owner is null || _actors is null) throw new NotSupportedException("Actual Journey owner unavailable.");
        // Capture actual actor, owner and original physical settings display BEFORE queuing the native turn.
        var actor = await _actors.GetCurrentAsync(ct) ?? throw new UnauthorizedAccessException("Original Maps actor unavailable.");
        await RequireActorAsync(actor, ct);
        var display = _originalDisplay is null
            ? await _owner.LoadForDisplayAsync(actor, ct, () => !IsRetired)
            : await _owner.ReloadForOriginalDisplayAsync(_originalDisplay.Selection, ct);
        var transferred = false; var entered = false;
        MapsJourneyLibraryPanel? candidate = null;
        try
        {
            await RequireActorAsync(actor, ct);
            if (_originalActor is not null && _originalActor != actor)
                throw new UnauthorizedAccessException("Original Journey host actor changed.");
            await _opening.WaitAsync(ct); entered = true;
            await RequireActorAsync(actor, ct);
            // Existing panel remains bound to its exact original settings/token. This read cannot adopt another root.
            if (_journeys is not null)
            {
                _view.Content = _journeys; return;
            }
            candidate = await MapsJourneyLibraryPanel.CreateForOriginalDisplayAsync(_owner, display.Selection,
                OpenOriginalHomeAsync, ct);
            await RequireActorAsync(actor, ct);
            _originalDisplay = display; _originalActor = actor; transferred = true;
            _journeys = candidate; candidate = null;
            _view.Content = _journeys; Status.Text = "Journey library";
        }
        finally
        {
            candidate?.Dispose();
            if (!transferred) display.Selection.Dispose();
            if (entered) _opening.Release();
        }
    }
    private async Task OpenOriginalHomeAsync(string requestID)
    {
        RequireHost();
        var actor = _originalActor ?? throw new UnauthorizedAccessException("Original Journey actor unavailable.");
        await RequireActorAsync(actor, _lifetime.Token);
        // Navigate the actual Home request only. Home's individual decision surface owns all approval.
        await _openHome(requestID, actor);
    }
    private void RequireHost()
    {
        if (IsRetired) { Dispose(); throw new ObjectDisposedException(nameof(MapsNativeJourneyHost)); }
    }
    private async Task RequireActorAsync(AuthenticatedResourceActor original, CancellationToken ct)
    {
        RequireHost();
        if (_actors is null || await _actors.GetCurrentAsync(ct) != original)
            throw new UnauthorizedAccessException("Original Maps actor changed.");
        RequireHost(); ct.ThrowIfCancellationRequested();
    }
    public void RefreshAvailability() { if (IsRetired) Dispose(); }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _journeys?.Dispose(); _originalDisplay?.Selection.Dispose();
        if (_legacy is IDisposable disposable) disposable.Dispose();
        JourneyButton.IsEnabled = false; MapButton.IsEnabled = false; _view.Content = null;
        // Retain accepted task/request handles; closing never implies Home approval or audit completion.
    }
}
