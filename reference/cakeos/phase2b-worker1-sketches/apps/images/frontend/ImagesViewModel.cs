using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CakeOS.Images.Interop;

namespace CakeOS.Images.Frontend;

public sealed class ImagesViewModel : INotifyPropertyChanged
{
    private readonly ILoupeBackend _backend;
    private readonly IAiVisionClient _aiVision;
    private readonly IPersistenceLayer _persistence;
    private IntPtr _currentHandle = IntPtr.Zero;
    private string? _currentPath;
    private double _zoomLevel = 1.0;
    private double _panX = 0.0;
    private double _panY = 0.0;
    private LoupeBackendInterop.ImageInfo _currentInfo;

    public ImagesViewModel(ILoupeBackend backend, IAiVisionClient aiVision, IPersistenceLayer persistence)
    {
        _backend = backend;
        _aiVision = aiVision;
        _persistence = persistence;

        OpenFileCommand = new RelayCommand(async () => await OpenFileAsync());
        ZoomInCommand = new RelayCommand(() => Zoom(1.25));
        ZoomOutCommand = new RelayCommand(() => Zoom(0.8));
        FitCommand = new RelayCommand(() => FitToViewport());
        RotateCommand = new RelayCommand(async () => await RotateAsync());
        AnalyzeCommand = new RelayCommand(async () => await AnalyzeAsync());
        ShowMetadataCommand = new RelayCommand(() => ShowMetadata());
    }

    public ICommand OpenFileCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand FitCommand { get; }
    public ICommand RotateCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand ShowMetadataCommand { get; }

    public IntPtr CurrentHandle => _currentHandle;
    public string? CurrentPath => _currentPath;
    public LoupeBackendInterop.ImageInfo CurrentInfo => _currentInfo;

    public double ZoomLevel
    {
        get => _zoomLevel;
        private set
        {
            _zoomLevel = Math.Clamp(value, 0.1, 10.0);
            OnPropertyChanged();
        }
    }

    public double PanX
    {
        get => _panX;
        set { _panX = value; OnPropertyChanged(); }
    }

    public double PanY
    {
        get => _panY;
        set { _panY = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task OpenFileAsync()
    {
        // In a real implementation, this would use a file dialog
        // For now, we'll use a test path or environment variable
        var testPath = Environment.GetEnvironmentVariable("CAKEOS_IMAGES_TEST_FILE");
        if (!string.IsNullOrEmpty(testPath) && File.Exists(testPath))
        {
            await LoadImageAsync(testPath);
        }
    }

    public async Task LoadImageAsync(string path)
    {
        if (_currentHandle != IntPtr.Zero)
        {
            _backend.Free(_currentHandle);
        }

        var status = _backend.Decode(path, out var info);
        if (status != LoupeBackendInterop.Status.Ok)
        {
            throw new InvalidOperationException($"Failed to decode image: {status}");
        }

        _currentHandle = info.Handle;
        _currentPath = path;
        _currentInfo = info;
        _zoomLevel = 1.0;
        _panX = 0.0;
        _panY = 0.0;

        OnPropertyChanged(nameof(CurrentHandle));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CurrentInfo));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(PanX));
        OnPropertyChanged(nameof(PanY));

        // Load persistence (annotations, tags, etc.)
        await _persistence.LoadAsync(path).ConfigureAwait(false);
    }

    private void Zoom(double factor)
    {
        ZoomLevel *= factor;
    }

    private void FitToViewport()
    {
        // Calculate fit zoom based on viewport size
        ZoomLevel = 1.0; // Placeholder
    }

    private async Task RotateAsync()
    {
        if (_currentHandle == IntPtr.Zero) return;

        var status = _backend.Transform(_currentHandle, LoupeBackendInterop.TransformOp.Rotate90, out var result);
        if (status == LoupeBackendInterop.Status.Ok)
        {
            _backend.Free(_currentHandle);
            _currentHandle = result.NewHandle;
            OnPropertyChanged(nameof(CurrentHandle));
        }
    }

    private async Task AnalyzeAsync()
    {
        if (_currentHandle == IntPtr.Zero || string.IsNullOrEmpty(_currentPath)) return;

        // Encode current viewport to JPEG and send to AI Vision
        // This is a placeholder for the actual gRPC call
        var analysis = await _aiVision.AnalyzeAsync(_currentPath).ConfigureAwait(false);
        
        // Persist AI tags
        await _persistence.SaveAiTagsAsync(_currentPath, analysis.Tags).ConfigureAwait(false);
    }

    private void ShowMetadata()
    {
        if (_currentHandle == IntPtr.Zero) return;

        var status = _backend.ReadMetadata(_currentHandle, out var metadata);
        if (status == LoupeBackendInterop.Status.Ok)
        {
            // Show metadata dialog
            Console.WriteLine($"Metadata: {LoupeBackendInterop.PtrToStringUtf8(metadata.ExifJson)}");
            LoupeBackendInterop.FreeString(metadata.ExifJson);
            LoupeBackendInterop.FreeString(metadata.XmpJson);
            LoupeBackendInterop.FreeString(metadata.IptcJson);
            LoupeBackendInterop.FreeString(metadata.ColorProfile);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

public static class CommandManager
{
    public static event EventHandler? RequerySuggested;
    public static void InvalidateRequerySuggested() => RequerySuggested?.Invoke(null, EventArgs.Empty);
}