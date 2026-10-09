#if !ANDROID
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Apps;
using HomeAppsSnapshot = HavenOS.Home.Apps.HomeAppsSnapshot;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Home;

// This page borrows finite source observations. The SAME App/Root owns pending
// launch business; a closed page never claims that launch or cancels its child.
internal sealed class OriginalInstalledHomeAppsPage : UserControl,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly App.OriginalInstalledHomeAppsPort? _source;
    private readonly Func<HomeNativeStartupObservation?> _startupObservation;
    private readonly Window _window;
    private readonly CancellationToken _appLifetime, _windowLifetime;
    private readonly CancellationTokenSource _viewLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _construction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBlock _detail = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _search = new() { PlaceholderText = "Search installed apps" };
    private readonly StackPanel _rows = new() { Spacing = 12 };
    private readonly Button _refresh = new() { Content = "Refresh installed apps" };
    private readonly Button _next = new() { Content = "Next page", IsVisible = false };
    private readonly List<HomePackageEntry> _currentRows = [];
    private readonly List<Exception> _callbackFailures = [];
    private HomeAppsSnapshot? _snapshot;
    private App.OriginalInstalledHomeLaunch? _launch;
    private string? _nextPage;
    private bool _busy;
    private Exception? _constructionFailure;
    private Task? _initialization, _release;
    private CancellationTokenRegistration _retirement;
    private bool _subscribed;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task? OriginalInitialization => _initialization;
    internal string Detail => _detail.Text ?? string.Empty;
    internal int OriginalRowCount => _currentRows.Count;

    private OriginalInstalledHomeAppsPage(App.OriginalInstalledHomeAppsPort? source,
        Func<HomeNativeStartupObservation?> startupObservation, Window window, CancellationToken appLifetime,
        CancellationToken windowLifetime, Action<OriginalInstalledHomeAppsPage> capture)
    {
        _source = source; _startupObservation = startupObservation; _window = window;
        _appLifetime = appLifetime; _windowLifetime = windowLifetime;
        _viewLifetime = CancellationTokenSource.CreateLinkedTokenSource(appLifetime, windowLifetime);
        _work = new(StopAsync, CloseViewAsync);
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                capture(this); // Before constructor callbacks or visible publication.
                DemandPublication();
                _refresh.Click += OnRefresh; _next.Click += OnNext;
                _window.Closed += OnWindowClosed; _subscribed = true;
                var body = new StackPanel { Spacing = 16, Margin = new Thickness(24), MaxWidth = 980,
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                body.Children.Add(new TextBlock { Text = "Installed apps", FontSize = 26 });
                body.Children.Add(new TextBlock { Text = "Open a current installed app after its individual Home review. Data access and actions inside that app require their own permission.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                body.Children.Add(_detail); body.Children.Add(_search);
                body.Children.Add(_refresh); body.Children.Add(_rows); body.Children.Add(_next);
                Content = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
                _retirement = _viewLifetime.Token.Register(static value => ((OriginalInstalledHomeAppsPage)value!).RequestRetirement(), this);
                DemandPublication(); return true;
            });
        }
        catch (Exception cause) { _constructionFailure = cause; throw; }
        finally { _construction.TrySetResult(); }
    }
    internal static OriginalInstalledHomeAppsPage BindOriginal(App.OriginalInstalledHomeAppsPort? source,
        Func<HomeNativeStartupObservation?> startupObservation, Window window, CancellationToken appLifetime,
        CancellationToken windowLifetime, Action<OriginalInstalledHomeAppsPage> capture)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(startupObservation);
        ArgumentNullException.ThrowIfNull(window); ArgumentNullException.ThrowIfNull(capture);
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled) throw new ArgumentException("Retain the actual native App/window lifetimes.");
        return new(source, startupObservation, window, appLifetime, windowLifetime, capture);
    }
    internal bool HasOriginalBinding(App.OriginalInstalledHomeAppsPort? source, Window window,
        CancellationToken appLifetime, CancellationToken windowLifetime) => ReferenceEquals(_source, source) &&
        ReferenceEquals(_window, window) && _appLifetime == appLifetime && _windowLifetime == windowLifetime;
    internal Task InitializeAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return _initialization ??= RefreshOriginalAsync(false);
    }
    internal void PublishOriginalTab(Action publish) => _work.RunSynchronous(original => Acquire(original, () =>
    { DemandPublication(); publish(); DemandPublication(); return true; }));
    private void OnRefresh(object? sender, Avalonia.Interactivity.RoutedEventArgs args) { _ = RefreshOriginalAsync(false); }
    private void OnNext(object? sender, Avalonia.Interactivity.RoutedEventArgs args) { _ = RefreshOriginalAsync(true); }
    internal Task RefreshOriginalAsync(bool next) => _work.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        original.BindPublicationGuard(() => !_work.IsRetiring && _window.IsVisible &&
            ReferenceEquals(TopLevel.GetTopLevel(this), _window));
        if (_busy) return;
        _busy = true; _refresh.IsEnabled = _next.IsEnabled = false;
        try
        {
            var startup = Acquire(original, _startupObservation);
            if (_source is null)
            {
                Publish(original, () =>
                {
                    _currentRows.Clear(); _rows.Children.Clear(); _next.IsVisible = false;
                    _detail.Text = startup is null
                        ? "Installed Apps is unavailable. Start the enrolled Home through its installed Root to read this catalogue."
                        : startup.Message + " Compatibility does not grant local data or product actions.";
                });
                return;
            }
            var result = _launch is { } accepted ? Acquire(original, () => _source.ObserveLaunch(accepted)) : null;
            var query = Acquire(original, () => new HomeAppsQuery(_search.Text, PageToken: next ? _nextPage : null));
            var raw = Acquire(original, () => _source.ReadAsync(query, CancellationToken.None));
            var snapshot = await original.AwaitAsync(raw);
            Publish(original, () =>
            {
                _snapshot = snapshot; _nextPage = snapshot.NextPageToken;
                _currentRows.Clear(); _currentRows.AddRange(snapshot.Packages); _rows.Children.Clear();
                _detail.Text = _launch is not null && result is null
                    ? "Waiting for the individual Home review. You can close this page; Home keeps the accepted launch operation."
                    : result is not null ? result.Message : snapshot.Error?.Message ?? (snapshot.State switch
                    { HomeAppsDataState.Empty => "No installed apps match this view.", HomeAppsDataState.Available => "Choose Launch to request Home approval.",
                        HomeAppsDataState.Partial => "Current app information is incomplete. Refresh before requesting a launch.", _ => "The installed app catalogue is unavailable." });
                foreach (var row in _currentRows)
                {
                    var button = new Button { Content = "Launch", IsEnabled = !snapshot.IsStale &&
                        snapshot.State == HomeAppsDataState.Available && row.SupportedActions.Contains(HomePackageAction.Launch) &&
                        (_launch is null || result?.State is HomePackageOperationState.Succeeded or HomePackageOperationState.Rejected) };
                    button.Click += (_, _) => { _ = LaunchOriginalRowAsync(snapshot, row); };
                    var text = new StackPanel { Spacing = 4 };
                    text.Children.Add(new TextBlock { Text = row.Name, FontSize = 18 });
                    text.Children.Add(new TextBlock { Text = "Installed version: " + (row.InstalledVersion ?? "unavailable"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    text.Children.Add(new TextBlock { Text = row.CompatibilityMessage ?? "Compatibility will be checked at launch.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    text.Children.Add(button); _rows.Children.Add(text);
                }
                _next.IsVisible = _nextPage is not null;
            });
        }
        finally
        {
            _busy = false;
            if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(() =>
            { _refresh.IsEnabled = true; _next.IsEnabled = _nextPage is not null; });
        }
    });
    internal Task LaunchOriginalRowAsync(HomeAppsSnapshot snapshot, HomePackageEntry row) => _work.RunAsync(original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        original.BindPublicationGuard(() => !_work.IsRetiring && _window.IsVisible && ReferenceEquals(TopLevel.GetTopLevel(this), _window));
        if (_busy || !ReferenceEquals(snapshot, _snapshot) || !_currentRows.Any(actual => ReferenceEquals(actual, row)))
            throw new UnauthorizedAccessException("Choose the SAME currently rendered installed app row.");
        var source = _source ?? throw new InvalidOperationException("The installed Root source is unavailable.");
        _launch = Acquire(original, () => source.StartLaunch(snapshot, row));
        Publish(original, () => { _detail.Text = "Waiting for Home approval. Refresh to observe the launch outcome.";
            foreach (var button in _rows.GetVisualDescendants().OfType<Button>()) button.IsEnabled = false; });
        return Task.CompletedTask;
    });
    private T Acquire<T>(DesktopOriginalWorkLifetime.Original original, Func<T> body)
    {
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, body); }
        catch (Exception cause) { lock (_callbackFailures) _callbackFailures.Add(cause); original.Retain(cause); throw; }
    }
    private void Publish(DesktopOriginalWorkLifetime.Original original, Action body)
    { if (original.IsAcceptedPublicationCurrent) original.RunAcceptedPublicationCallback(() =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; })); }
    private void DemandPublication()
    { Dispatcher.UIThread.VerifyAccess(); _work.DemandAdmission(); _viewLifetime.Token.ThrowIfCancellationRequested();
        if (!_window.IsVisible) throw new ObjectDisposedException("The installed Apps window is closed."); }
    public void DemandExternalOriginalRetirementJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopAsync() { _viewLifetime.Cancel(); return Task.CompletedTask; }
    private void OnWindowClosed(object? sender, EventArgs args) => RequestRetirement();
    private async Task CloseViewAsync()
    {
        await _construction.Task;
        if (_constructionFailure is { } cause) throw cause;
        lock (_callbackFailures) if (_callbackFailures.Count != 0)
            throw new AggregateException("Installed Apps presentation callbacks failed; the actual view remains retained.", _callbackFailures);
        _release ??= Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            _refresh.Click -= OnRefresh; _next.Click -= OnNext;
            if (_subscribed) { _window.Closed -= OnWindowClosed; _subscribed = false; }
            Content = null; _retirement.Dispose(); _viewLifetime.Dispose(); return true;
        })).GetTask();
        await _release;
    }
}
#endif
