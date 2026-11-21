using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

/// <summary>
/// Simple window shell that hosts MainView with animated tidal background.
/// </summary>
public sealed partial class MainWindow : Window, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly UserPreferencesService? _preferences;
    private MainView? _shell;
    private TidalBackground? _tidalBackground;
    internal bool PreserveWorkspaceSessionOnClose { get; init; }

    public MainWindow() : this(null)
    {
    }

    public MainWindow(UserPreferencesService? preferences)
    {
        _preferences = preferences;
        _originalWindowWork = new(StopOriginalWindowSourcesAsync, CleanupOriginalWindowResourcesAsync);
        // This adapter is OS-only window chrome. The canonical CUI app is mounted
        // through Window.Content below, so no AXAML/XAML document is parsed or
        // precompiled here. Avalonia remains only the underlying windowing layer.
        Width = 1440;
        Height = 920;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Haven/Assets/haven.ico")));
        Title = DesktopProductIdentity.DisplayName;
        DataContextChanged += OnOriginalDataContextChanged;
        // HAV-SHELL: fractional-scale startup can strand the title strip above every screen
        // (observed at exactly minus the centring delta), and on X11 the windowing layer never
        // learns of WM-driven moves - reading Position back is blind to the strand (both live
        // facts, 21 Nov). The reliable lever is an explicit app-side move: unless session
        // restore already placed this window deliberately, re-seat it inside the working area
        // while the WM is still honouring the request. Later checks are verified no-ops.
        Opened += (_, _) =>
        {
            WorkspaceWindowService.EnsureReachablePlacement(this);
            SchedulePlacementSelfHeal();
        };
        PositionChanged += (_, _) =>
        {
            WorkspaceWindowService.NoteExplicitPlacement(this);
            WorkspaceWindowService.EnsureReachablePlacement(this);
        };
        Closed += (_, _) => _placementWindowClosed = true;
    }

    private bool _placementWindowClosed;

    private void SchedulePlacementSelfHeal()
    {
        foreach (var delay in new[] { 350, 1200 })
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_placementWindowClosed) return;
                WorkspaceWindowService.EnsureReachablePlacement(this);
            };
            timer.Start();
        }
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(6000) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            if (_placementWindowClosed) return;
            WorkspaceWindowService.SeatMainWindowOnScreenIfImplicit(this);
        };
        settle.Start();
    }

    private void SetupBackground()
    {
        if (_actualSubscribedShell is { } previous) previous.PropertyChanged -= OnOriginalShellSurfaceChanged;
        _tidalBackground?.Dispose();
        _tidalBackground = new TidalBackground(this,
            _preferences?.Appearance ?? Haven.Core.HavenUiAppearance.SuperDark,
            publishHavenPalette: false);
        if (_preferences is not null)
        {
            _preferences.AppearanceChanged -= OnAppearanceChanged;
            _preferences.AppearanceChanged += OnAppearanceChanged;
        }

        // Surface changes include dedicated apps such as Browse, Imagine and
        // Dashboard, whereas CurrentMode only describes conversation storage.
        if (_shell is not null)
        {
            _actualSubscribedShell = _shell;
            _shell.PropertyChanged += OnOriginalShellSurfaceChanged;

            _tidalBackground.SetSurface(_shell.CurrentSurface);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_originalWindowWork.IsRetiring) return;
        _originalWindowWork.RunSynchronous(original => AcquireOriginalWindowCallback(original, () =>
            _tidalBackground?.SetAppearance(_preferences?.Appearance ?? Haven.Core.HavenUiAppearance.SuperDark)));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_originalWindowWork.IsRetiring)
            _originalWindowWork.RunCloseCallback(() => DeliverOriginalClosingCallback(e));
        else
            _originalWindowWork.RunSynchronous(original => AcquireOriginalWindowCallback(original,
                () => DeliverOriginalClosingCallback(e)));
    }

    private void DeliverOriginalClosingCallback(WindowClosingEventArgs e)
    {
        base.OnClosing(e); // Preserve existing handlers/veto, including the genuine App primary pre-close source.
        if (e.Cancel || _originalWindowWork.OriginalClose is { IsCompletedSuccessfully: true }) return;
        e.Cancel = true;
        if (!_originalWindowWork.IsRetiring) RequestOriginalWindowCloseDelivery(); // Global retirement admits no late delivery borrower.
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_originalWindowWork.OriginalClose is not { IsCompletedSuccessfully: true })
        {
            _actualWindowCloseFailure ??= new InvalidOperationException("The native window closed before its original owned resources drained.");
            RequestRetirement(); // Forced/native exit is not a successful all-drained receipt.
        }
        Exception? actualFailure = null;
        try { base.OnClosed(e); }
        catch (Exception original) { actualFailure = original; }
        // SAME actual synchronous native Closed callback settles only after its
        // genuine framework/event callbacks return. Visibility is no substitute.
        if (actualFailure is not null)
        {
            _actualClosedSettlement.TrySetException(actualFailure);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actualFailure).Throw();
        }
        _actualClosedSettlement.TrySetResult();
        ReleaseOriginalClosedCustody();
    }
}
