using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Floating;

namespace Haven.Desktop.Services;

public sealed partial class DesktopFloatingActivityHost(FloatingActivityStateStore stateStore) : IFloatingActivityHost, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly Dictionary<Guid, Window> _windows = [];
    private bool _disposed;

    public string Platform => "Windows";
    public bool IsAvailable => OperatingSystem.IsWindows();
    public string? UnavailableReason => IsAvailable ? null : "Detached desktop windows require Windows.";
    public event EventHandler<FloatingActivitySnapshot>? StateChanged;

    public Task<FloatingActivitySnapshot> PresentAsync(
        FloatingActivityDefinition definition,
        IFloatingActivityContent content,
        CancellationToken cancellationToken) => RunOriginalFloatingSynchronous(() => PresentOriginalAsync(definition, content, cancellationToken));

    private Task<FloatingActivitySnapshot> PresentOriginalAsync(
        FloatingActivityDefinition definition, IFloatingActivityContent content, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable) throw new PlatformNotSupportedException(UnavailableReason);

        var originalCurrent = CaptureCurrentFloatingWindow(definition.Id);
        DemandOriginalFloatingCurrent(definition.Id, originalCurrent);
        RetainOriginalFloatingWrapper(content); // Retain SAME wrapper before its actual getter/source callback.
        var actualContent = AcquireOriginalFloatingSource(() => content.Content);
        RetainOriginalFloatingContent(actualContent);
        DemandOriginalFloatingCurrent(definition.Id, originalCurrent); // No late or replaced original after getter reentry.
        var activityContent = actualContent as Control
                              ?? new ContentControl { Content = actualContent };
        var window = CreateOriginalFloatingWindow(definition.Id, originalCurrent);
        ConfigureOriginalFloatingWindow(window, definition, originalCurrent);
        var close = new HavenIconButton
        {
            Width = 34,
            Height = 34,
            IsVisible = definition.IsDismissible,
            Content = new TextBlock
            {
                Text = "×",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        AutomationProperties.SetName(close, "Close " + definition.Title);
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> closeClick = (_, _) => RunOriginalFloatingEvent(window.Close);
        close.Click += closeClick;
        AttachOriginalFloatingDetach(() => close.Click -= closeClick);

        var dragBar = new HavenToolbar
        {
            Padding = new Thickness(8, 4, 6, 4),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = definition.Title,
                        FontSize = 13,
                        FontWeight = FontWeight.ExtraBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0)
                    },
                    Column(close, 1)
                }
            }
        };
        AutomationProperties.SetName(dragBar, "Drag " + definition.Title);
        EventHandler<PointerPressedEventArgs> drag = (_, args) => RunOriginalFloatingEvent(() =>
        {
            if (!args.GetCurrentPoint(dragBar).Properties.IsLeftButtonPressed
                || args.Source is Control source
                && (source is Button || source.FindAncestorOfType<Button>() is not null)) return;
            window.BeginMoveDrag(args);
            args.Handled = true;
        });
        dragBar.PointerPressed += drag;
        AttachOriginalFloatingDetach(() => dragBar.PointerPressed -= drag);

        var surface = new HavenFloatingSurface
        {
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children = { dragBar, Row(activityContent, 1) }
            }
        };
        PublishOriginalFloatingEffect(definition.Id, originalCurrent, () => window.Content = surface);

        if (stateStore.Get(definition.Id) is { } previous)
        {
            PublishOriginalFloatingEffect(definition.Id, originalCurrent, () => window.WindowStartupLocation = WindowStartupLocation.Manual);
            PublishOriginalFloatingEffect(definition.Id, originalCurrent, () => window.Width = Math.Max(window.MinWidth, previous.Width));
            PublishOriginalFloatingEffect(definition.Id, originalCurrent, () => window.Height = Math.Max(window.MinHeight, previous.Height));
            PublishOriginalFloatingEffect(definition.Id, originalCurrent, () => window.Position = new PixelPoint((int)Math.Round(previous.X), (int)Math.Round(previous.Y)));
        }

        void PublishState(FloatingActivityState state)
        {
            var snapshot = new FloatingActivitySnapshot(
                definition.Id,
                state,
                Math.Max(window.MinWidth, window.Width),
                Math.Max(window.MinHeight, window.Height),
                window.Position.X,
                window.Position.Y);
            var actualClosedObservation = state == FloatingActivityState.Dismissed && IsActualFloatingClosedObservation(window);
            DemandOriginalFloatingCurrent(definition.Id, originalCurrent, actualClosedObservation);
            stateStore.Set(snapshot);
            DemandOriginalFloatingCurrent(definition.Id, originalCurrent, actualClosedObservation);
            StateChanged?.Invoke(this, snapshot); // Last actual notification may request retirement and return.
        }

        EventHandler<PixelPointEventArgs> position = (_, _) => RunOriginalFloatingEvent(() => PublishState(FloatingActivityState.Presented));
        EventHandler<SizeChangedEventArgs> size = (_, _) => RunOriginalFloatingEvent(() => PublishState(FloatingActivityState.Presented));
        window.PositionChanged += position; window.SizeChanged += size;
        AttachOriginalFloatingDetach(() => window.PositionChanged -= position);
        AttachOriginalFloatingDetach(() => window.SizeChanged -= size);
        AttachOriginalFloatingClosed(window, () =>
        {
            if (!ReferenceEquals(CaptureCurrentFloatingWindow(definition.Id), window)) return;
            _windows.Remove(definition.Id);
            originalCurrent = null;
            PublishState(FloatingActivityState.Dismissed);
        });
        DemandOriginalFloatingCurrent(definition.Id, originalCurrent);
        _windows[definition.Id] = window;
        originalCurrent = window;
        PublishOriginalFloatingEffect(definition.Id, originalCurrent, window.Show);
        PublishState(FloatingActivityState.Presented);
        return Task.FromResult(stateStore.Get(definition.Id)!);
    }

    public Task<FloatingActivitySnapshot> UpdateAsync(FloatingActivitySnapshot snapshot, CancellationToken cancellationToken) =>
        RunOriginalFloatingSynchronous(() => UpdateOriginalAsync(snapshot, cancellationToken));
    private Task<FloatingActivitySnapshot> UpdateOriginalAsync(FloatingActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var originalCurrent = CaptureCurrentFloatingWindow(snapshot.Id);
        if (originalCurrent is { } window)
        {
            PublishOriginalFloatingEffect(snapshot.Id, originalCurrent, () => window.Width = Math.Max(240, snapshot.Width));
            PublishOriginalFloatingEffect(snapshot.Id, originalCurrent, () => window.Height = Math.Max(160, snapshot.Height));
            PublishOriginalFloatingEffect(snapshot.Id, originalCurrent, () => window.Position = new PixelPoint((int)Math.Round(snapshot.X), (int)Math.Round(snapshot.Y)));
        }
        DemandOriginalFloatingCurrent(snapshot.Id, originalCurrent);
        stateStore.Set(snapshot);
        DemandOriginalFloatingCurrent(snapshot.Id, originalCurrent);
        StateChanged?.Invoke(this, snapshot); // Last notification completes its already-admitted original.
        return Task.FromResult(snapshot);
    }

    public Task DismissAsync(Guid activityId, CancellationToken cancellationToken) =>
        RunOriginalFloatingSynchronous(() => DismissOriginalAsync(activityId, cancellationToken));
    private Task DismissOriginalAsync(Guid activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var originalCurrent = CaptureCurrentFloatingWindow(activityId);
        DemandOriginalFloatingCurrent(activityId, originalCurrent);
        if (originalCurrent is { } window)
        {
            window.Close(); // SAME actual Closed callback may remove only its owning current Window.
            if (ReferenceEquals(CaptureCurrentFloatingWindow(activityId), window))
                throw new InvalidOperationException("The actual floating native close did not settle its current owning record.");
        }
        DemandOriginalFloatingCurrent(activityId, null); // A reentrant replacement is not this dismissal's record.
        stateStore.Remove(activityId);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
