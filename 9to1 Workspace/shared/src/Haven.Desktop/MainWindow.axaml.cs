using Avalonia.Controls;
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
        InitializeComponent();
        Title = DesktopProductIdentity.DisplayName;
        DataContextChanged += OnOriginalDataContextChanged;
    }

    private void SetupBackground()
    {
        if (_actualSubscribedShell is { } previous) previous.PropertyChanged -= OnOriginalShellSurfaceChanged;
        _tidalBackground?.Dispose();
        _tidalBackground = new TidalBackground(this, _preferences?.Appearance ?? Haven.Core.HavenUiAppearance.SuperDark);
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
    }
}
