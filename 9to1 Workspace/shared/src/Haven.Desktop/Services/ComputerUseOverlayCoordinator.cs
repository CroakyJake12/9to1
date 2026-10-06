using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>
/// Owns the topmost Computer Use safety banner and Haven's visual pointer. The
/// banner reflects the same controller that gates and cancels real tool actions.
/// </summary>
public sealed partial class ComputerUseOverlayCoordinator : IDisposable, IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly IComputerUseSessionController _controller;
    private readonly Window _banner;
    private readonly Window _cursor;
    private readonly Window _status;
    private readonly TextBlock _detail;
    private readonly TextBlock _actionCount;
    private readonly Button _pause;
    private bool _disposed;

    public ComputerUseOverlayCoordinator(IComputerUseSessionController controller)
        : this(controller, actual => Dispatcher.UIThread.InvokeAsync(actual).GetTask()) { }
    internal ComputerUseOverlayCoordinator(IComputerUseSessionController controller, Func<Action, Task> actualDispatcher, Func<Window>? actualWindowFactory = null)
    {
        _controller = controller;
        _dispatchOriginalComputerUse = actualDispatcher ?? throw new ArgumentNullException(nameof(actualDispatcher));
        _createOriginalComputerUseWindow = actualWindowFactory ?? (() => new Window());
        _originalComputerUseWork = new(StopOriginalComputerUseAsync, CleanupOriginalComputerUseAsync);
        var construction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _originalComputerUseWork.RunAsync(original => original.AwaitAsync(construction.Task));
        Exception? constructionFailure = null;
        (_synchronousComputerUseSources ??= []).Add(this);
        try
        {
            _detail = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Text = "Preparing computer use"
            };
            _actionCount = new TextBlock
            {
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Text = "Action: 0/30"
            };
            _pause = SafetyButton(
                "Pause",
                new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
                Brushes.White);
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> pause = (_, _) => RunOriginalComputerUseEvent(_controller.TogglePause);
            _pause.Click += pause; _originalComputerUseDetaches.Add(() => _pause.Click -= pause);

            var stop = SafetyButton("Stop", new SolidColorBrush(Color.Parse("#FF1118")), Brushes.White);
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> stopClick = (_, _) => RunOriginalComputerUseEvent(_controller.Stop);
            stop.Click += stopClick; _originalComputerUseDetaches.Add(() => stop.Click -= stopClick);

            var labels = new TextBlock
            {
                Text = "Haven is using your Computer",
                FontSize = 34,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            var layout = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 14,
                Margin = new Thickness(30, 20)
            };
            layout.Children.Add(labels);
            Grid.SetColumn(_pause, 1);
            layout.Children.Add(_pause);
            Grid.SetColumn(stop, 2);
            layout.Children.Add(stop);

            _banner = AcquireOriginalComputerUseWindow();
            PublishOriginalComputerUseEffect(() => _banner.Title = "Haven Computer Use controls");
            PublishOriginalComputerUseEffect(() => _banner.Width = 1400);
            PublishOriginalComputerUseEffect(() => _banner.Height = 136);
            PublishOriginalComputerUseEffect(() => _banner.Content = new HavenAdaptiveSurface
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#178BFF"), 0),
                        new GradientStop(Color.Parse("#315CF7"), 0.48),
                        new GradientStop(Color.Parse("#6424FF"), 1)
                    }
                },
                CornerRadius = new CornerRadius(28),
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 34,
                    OffsetY = 14,
                    Color = Color.FromArgb(74, 38, 92, 255)
                }),
                Child = layout
            });

            _status = AcquireOriginalComputerUseWindow();
            PublishOriginalComputerUseEffect(() => _status.Title = "Haven Computer Use status");
            PublishOriginalComputerUseEffect(() => _status.Width = 250);
            PublishOriginalComputerUseEffect(() => _status.Height = 102);
            PublishOriginalComputerUseEffect(() => _status.IsHitTestVisible = false);
            PublishOriginalComputerUseEffect(() => _status.Content = new HavenAdaptiveSurface
            {
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#168DFF"), 0),
                        new GradientStop(Color.Parse("#2577FA"), 1)
                    }
                },
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(18, 14),
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 28,
                    OffsetY = 10,
                    Color = Color.FromArgb(70, 23, 116, 255)
                }),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 4,
                    Children = { _actionCount, _detail }
                }
            });

            _cursor = AcquireOriginalComputerUseWindow();
            PublishOriginalComputerUseEffect(() => _cursor.Title = "Haven virtual cursor");
            PublishOriginalComputerUseEffect(() => _cursor.Width = 106);
            PublishOriginalComputerUseEffect(() => _cursor.Height = 110);
            PublishOriginalComputerUseEffect(() => _cursor.IsHitTestVisible = false);
            PublishOriginalComputerUseEffect(() => _cursor.Content = BuildCursor());

            PublishOriginalComputerUseEffect(() => _controller.StateChanged += OnStateChanged);
        }
        catch (Exception original)
        {
            constructionFailure = original;
            RequestRetirement(); // Same close waits the admitted constructor, including its final bookkeeping.
            throw new ComputerUseOverlayAcquisitionException(original, _originalComputerUseWork.OriginalClose!);
        }
        finally
        {
            _synchronousComputerUseSources.RemoveAt(_synchronousComputerUseSources.Count - 1);
            if (constructionFailure is { } original) construction.TrySetException(original);
            else construction.TrySetResult();
        }
    }

    private void ConfigureOriginalComputerUseWindow(Window actual)
    {
        PublishOriginalComputerUseEffect(() => actual.WindowDecorations = WindowDecorations.None);
        PublishOriginalComputerUseEffect(() => actual.ShowInTaskbar = false);
        PublishOriginalComputerUseEffect(() => actual.ShowActivated = false);
        PublishOriginalComputerUseEffect(() => actual.CanResize = false);
        PublishOriginalComputerUseEffect(() => actual.Topmost = true);
        PublishOriginalComputerUseEffect(() => actual.Background = Brushes.Transparent);
        PublishOriginalComputerUseEffect(() => actual.TransparencyBackgroundFallback = Brushes.Transparent);
        PublishOriginalComputerUseEffect(() => actual.TransparencyLevelHint = [WindowTransparencyLevel.Transparent]);
    }

    private static Button SafetyButton(string label, IBrush background, IBrush foreground) => new()
    {
        Content = label,
        MinWidth = 138,
        MinHeight = 68,
        Padding = new Thickness(26, 14),
        CornerRadius = new CornerRadius(34),
        Background = background,
        Foreground = foreground,
        BorderThickness = new Thickness(0),
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Control BuildCursor()
    {
        const string cursorGeometry = "M 12,8 L 88,54 Q 94,58 87,64 L 44,96 Q 36,102 33,91 Z";
        var glow = new ShapePath
        {
            Data = Geometry.Parse(cursorGeometry),
            Fill = new SolidColorBrush(Color.FromArgb(55, 113, 171, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(45, 85, 103, 255)),
            StrokeThickness = 18,
            Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                Color = Color.Parse("#8EADFF"),
                Opacity = 0.8
            }
        };
        var triangle = new ShapePath
        {
            Data = Geometry.Parse(cursorGeometry),
            Fill = new SolidColorBrush(Color.Parse("#838AF4")),
            Stroke = new SolidColorBrush(Color.Parse("#5D42FF")),
            StrokeThickness = 5
        };
        return new Grid
        {
            IsHitTestVisible = false,
            Children = { glow, triangle }
        };
    }

    private void OnStateChanged(object? sender, ComputerUseSessionState state)
    {
        if (_disposed || _originalComputerUseWork.IsRetiring) return;
        _ = _originalComputerUseWork.RunAsync(original =>
        {
            var actual = AcquireOriginalComputerUseSource(() => _dispatchOriginalComputerUse(() =>
            {
                if (!_disposed && !_originalComputerUseWork.IsRetiring)
                    AcquireOriginalComputerUseSource(() => { Apply(state); return true; });
            }) ?? throw new InvalidOperationException("The actual Computer Use dispatcher supplied no original Task."));
            return original.AwaitAsync(actual); // SAME raw dispatcher driver, acquired once and always joined.
        }, actual => LastOriginalUpdate = actual);
    }

    private void Apply(ComputerUseSessionState state)
    {
        if (_disposed)
        {
            return;
        }

        if (!state.IsActive)
        {
            PublishOriginalComputerUseEffect(_cursor.Hide);
            PublishOriginalComputerUseEffect(_status.Hide);
            PublishOriginalComputerUseEffect(_banner.Hide);
            return;
        }

        PublishOriginalComputerUseEffect(() => _detail.Text = state.Action);
        PublishOriginalComputerUseEffect(() => _actionCount.Text = $"Action: {state.ActionNumber}/{state.ActionLimit}");
        PublishOriginalComputerUseEffect(() => _pause.Content = state.IsPaused ? "Resume" : "Pause");
        PositionOverlays(state.CursorX, state.CursorY);
        if (!_banner.IsVisible)
        {
            PublishOriginalComputerUseEffect(_banner.Show);
        }
        if (!_status.IsVisible)
        {
            PublishOriginalComputerUseEffect(_status.Show);
        }

        if (state.CursorX is int x && state.CursorY is int y)
        {
            PublishOriginalComputerUseEffect(() => _cursor.Position = new PixelPoint(x - 50, y - 94));
            if (!_cursor.IsVisible)
            {
                PublishOriginalComputerUseEffect(_cursor.Show);
            }
        }
        else
        {
            PublishOriginalComputerUseEffect(_cursor.Hide);
        }
    }

    private void PositionOverlays(int? cursorX, int? cursorY)
    {
        var screens = _banner.Screens;
        var screen = cursorX is int x && cursorY is int y
            ? screens.ScreenFromPoint(new PixelPoint(x, y))
            : null;
        screen ??= screens.ScreenFromWindow(_banner);
        screen ??= screens.ScreenFromPoint(_banner.Position);
        screen ??= screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = Math.Max(640, area.Width - 20);
        PublishOriginalComputerUseEffect(() => _banner.Width = width / screen.Scaling);
        PublishOriginalComputerUseEffect(() => _banner.Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + 10));
        PublishOriginalComputerUseEffect(() => _status.Position = new PixelPoint(
            area.X + 18,
            area.Bottom - (int)Math.Round(_status.Height * screen.Scaling) - 22));
    }

    public void Dispose() => RequestRetirement(); // Request-only; external host joins while UI/controller remain alive.
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
