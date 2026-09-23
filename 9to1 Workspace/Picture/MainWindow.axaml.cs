using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace HavenOS.Images;

public sealed partial class MainWindow : Window
{
    private readonly Button _openButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _zoomOutButton;
    private readonly Button _zoomInButton;
    private readonly Button _fitButton;
    private readonly TextBlock _statusText;
    private readonly TextBlock _fileNameText;
    private readonly TextBlock _metadataText;
    private readonly TextBlock _zoomText;
    private readonly Image _previewImage;
    private readonly StackPanel _emptyState;
    private readonly ImageViewportState _viewport = new();
    private readonly ScaleTransform _scaleTransform = new();
    private readonly TranslateTransform _translateTransform = new();
    private readonly TransformGroup _imageTransform = new();

    private Bitmap? _bitmap;
    private ImageNavigationSession? _navigation;
    private bool _isPanning;
    private Point _lastPanPosition;

    public MainWindow()
    {
        InitializeComponent();

        _openButton = this.FindControl<Button>("OpenButton") ?? throw new InvalidOperationException("OpenButton was not created from XAML.");
        _previousButton = this.FindControl<Button>("PreviousButton") ?? throw new InvalidOperationException("PreviousButton was not created from XAML.");
        _nextButton = this.FindControl<Button>("NextButton") ?? throw new InvalidOperationException("NextButton was not created from XAML.");
        _zoomOutButton = this.FindControl<Button>("ZoomOutButton") ?? throw new InvalidOperationException("ZoomOutButton was not created from XAML.");
        _zoomInButton = this.FindControl<Button>("ZoomInButton") ?? throw new InvalidOperationException("ZoomInButton was not created from XAML.");
        _fitButton = this.FindControl<Button>("FitButton") ?? throw new InvalidOperationException("FitButton was not created from XAML.");
        _statusText = this.FindControl<TextBlock>("StatusText") ?? throw new InvalidOperationException("StatusText was not created from XAML.");
        _fileNameText = this.FindControl<TextBlock>("FileNameText") ?? throw new InvalidOperationException("FileNameText was not created from XAML.");
        _metadataText = this.FindControl<TextBlock>("MetadataText") ?? throw new InvalidOperationException("MetadataText was not created from XAML.");
        _zoomText = this.FindControl<TextBlock>("ZoomText") ?? throw new InvalidOperationException("ZoomText was not created from XAML.");
        _previewImage = this.FindControl<Image>("PreviewImage") ?? throw new InvalidOperationException("PreviewImage was not created from XAML.");
        _emptyState = this.FindControl<StackPanel>("EmptyState") ?? throw new InvalidOperationException("EmptyState was not created from XAML.");

        _imageTransform.Children.Add(_scaleTransform);
        _imageTransform.Children.Add(_translateTransform);
        _previewImage.RenderTransform = _imageTransform;
        _previewImage.RenderTransformOrigin = RelativePoint.Center;

        _openButton.Click += OpenButton_Click;
        _previousButton.Click += PreviousButton_Click;
        _nextButton.Click += NextButton_Click;
        _zoomOutButton.Click += (_, _) => ZoomAtViewportCenter(1 / 1.25);
        _zoomInButton.Click += (_, _) => ZoomAtViewportCenter(1.25);
        _fitButton.Click += (_, _) =>
        {
            _viewport.Reset();
            ApplyViewport();
        };
        _previewImage.PointerWheelChanged += OnPreviewPointerWheelChanged;
        _previewImage.PointerPressed += OnPreviewPointerPressed;
        _previewImage.PointerMoved += OnPreviewPointerMoved;
        _previewImage.PointerReleased += OnPreviewPointerReleased;
        _previewImage.PointerCaptureLost += (_, _) => CompletePan();
        Closed += (_, _) => _bitmap?.Dispose();
        ApplyViewport();
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ImageFilePolicy.PickerPatterns,
                    },
                ],
            });

            if (files.Count == 0)
            {
                return;
            }

            var selected = files[0];
            if (!selected.Path.IsFile)
            {
                ShowError("This first Images slice opens local files only.");
                return;
            }

            LoadLocalPath(selected.Path.LocalPath);
        }
        catch (Exception exception)
        {
            ShowError($"The image picker could not be opened: {exception.Message}");
        }
    }

    private void PreviousButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = _navigation?.MovePrevious();
        if (path is not null)
        {
            LoadLocalPath(path);
        }
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = _navigation?.MoveNext();
        if (path is not null)
        {
            LoadLocalPath(path);
        }
    }

    private void LoadLocalPath(string path)
    {
        if (!ImageFilePolicy.IsSupportedPath(path))
        {
            ShowError("Choose a PNG, JPEG, BMP, GIF, or WebP image.");
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var nextBitmap = new Bitmap(stream);
            var previousBitmap = _bitmap;

            _bitmap = nextBitmap;
            _previewImage.Source = nextBitmap;
            previousBitmap?.Dispose();
            _viewport.Reset();
            ApplyViewport();

            _navigation = ImageNavigationSession.FromSelection(path);
            _previewImage.IsVisible = true;
            _emptyState.IsVisible = false;
            _fileNameText.Text = Path.GetFileName(path);
            _metadataText.Text = $"{nextBitmap.PixelSize.Width} × {nextBitmap.PixelSize.Height} · {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()}";
            _statusText.Text = path;
            UpdateNavigationButtons();
        }
        catch (Exception exception)
        {
            ShowError($"Images could not decode this file: {exception.Message}");
        }
    }

    private void UpdateNavigationButtons()
    {
        _previousButton.IsEnabled = _navigation?.CanMovePrevious == true;
        _nextButton.IsEnabled = _navigation?.CanMoveNext == true;
    }

    private void ZoomAtViewportCenter(double factor)
    {
        var width = _previewImage.Bounds.Width;
        var height = _previewImage.Bounds.Height;
        _viewport.ZoomAt(factor, width / 2, height / 2, width, height);
        ApplyViewport();
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap is null || Math.Abs(e.Delta.Y) < 0.001)
            return;

        var point = e.GetPosition(_previewImage);
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _viewport.ZoomAt(factor, point.X, point.Y, _previewImage.Bounds.Width, _previewImage.Bounds.Height);
        ApplyViewport();
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap is null || !e.GetCurrentPoint(_previewImage).Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _lastPanPosition = e.GetPosition(_previewImage);
        _previewImage.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(_previewImage);
        e.Handled = true;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning)
            return;

        var point = e.GetPosition(_previewImage);
        _viewport.PanBy(point.X - _lastPanPosition.X, point.Y - _lastPanPosition.Y);
        _lastPanPosition = point;
        ApplyViewport();
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
            return;

        CompletePan();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CompletePan()
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        _previewImage.Cursor = null;
    }

    private void ApplyViewport()
    {
        _scaleTransform.ScaleX = _viewport.Scale;
        _scaleTransform.ScaleY = _viewport.Scale;
        _translateTransform.X = _viewport.OffsetX;
        _translateTransform.Y = _viewport.OffsetY;
        _zoomText.Text = $"{_viewport.Scale:P0}";
        _zoomInButton.IsEnabled = _bitmap is not null && _viewport.CanZoomIn;
        _zoomOutButton.IsEnabled = _bitmap is not null && _viewport.CanZoomOut;
        _fitButton.IsEnabled = _bitmap is not null && !_viewport.IsDefault;
    }

    private void ShowError(string message)
    {
        _statusText.Text = message;
        UpdateNavigationButtons();
    }
}
