using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Dulche.Training;

namespace HavenOS.AIStudio;

/// <summary>Native window/file-picker/lifetime adapter. Studio.cui owns all presentation.</summary>
public sealed class MainWindow : Window, IStudioFileDialogs
{
    private readonly CuiSceneHost _host = new();
    private readonly StudioCuiBindings _bindings;
    private readonly CancellationTokenSource _view = new();
    private Task? _closingTask;
    private bool _closed;
    private string? _registrationError;
    public Task Initialization { get; private set; } = Task.CompletedTask;

    public MainWindow() : this(CreateDefaultApi()) { }
    public MainWindow(AIStudioApi api, StudioEngineCheckController? engine = null)
    {
        Title = "9to1 AI Studio"; Width = 1440; Height = 900; MinWidth = 960; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _bindings = new(api, engine ?? CreateEngine(), this, publish =>
        {
            if (_closingTask is not null) return;
            Dispatcher.UIThread.Post(() => { if (_closingTask is null) publish(); });
        });
        Content = _host;
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private static AIStudioApi CreateDefaultApi()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "9to1", "AIStudio");
        return new(new JsonStudioProjectStore(path), new UnavailableStudioRuntimeAdapter(),
            new UnavailableCanonicalAgentBuilderAdapter(), new JsonStudioResourceStore(path),
            new UnavailableStudioPermissionSimulationAdapter(), new UnavailableStudioContextInspectorAdapter(),
            new UnavailableStudioReplayAdapter());
    }

    private StudioEngineCheckController CreateEngine()
    {
        var setup = StudioWorkerRegistration.CreateController();
        _registrationError = setup.Error;
        return setup.Controller;
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        Initialization = InitializeSceneAsync();
        try { await Initialization; }
        catch (Exception error)
        { Console.Error.WriteLine("Studio CUI initialization failed: " + error); Title = "9to1 AI Studio — presentation unavailable"; }
    }

    private async Task InitializeSceneAsync()
    {
        using var input = typeof(MainWindow).Assembly.GetManifestResourceStream("HavenOS.AIStudio.Studio.cui")
            ?? throw new InvalidDataException("Studio CUI resource is missing.");
        using var reader = new StreamReader(input);
        var parser = new CuiRichParser();
        var document = parser.Parse(await reader.ReadToEndAsync(_view.Token), "Studio.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Studio CUI document is invalid.");
        var scene = new CuiNativeScene("app.studio", "9to1 AI Studio", "Studio", document, _bindings, _bindings,
            new PresentationReadiness()) { IsPublicationCurrent = () => _closingTask is null };
        var readiness = await _host.ShowAsync(scene, _view.Token);
        if (readiness.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(readiness.Message);
        await _bindings.InitializeAsync(_view.Token);
        if (_registrationError is not null) _bindings.ReportHostNotice("Self-check unavailable", _registrationError);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_closed) return;
        args.Cancel = true;
        if (_closingTask is not null) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _closingTask = CloseSceneAsync(start.Task);
        start.SetResult();
    }

    private async Task CloseSceneAsync(Task start)
    {
        await start;
        try
        {
            // Retain the engine's cooperative checkpoint grace before ending its transport.
            await _bindings.DisposeAsync();
            _view.Cancel();
            await _host.CloseOriginalAsync();
            try { await Initialization; }
            catch (Exception error) { Console.Error.WriteLine("Studio initialization did not settle successfully: " + error.GetType().Name); }
            _host.Dispose();
            _view.Dispose();
            _closed = true;
            Close();
        }
        catch (Exception error)
        { Console.Error.WriteLine("Studio close/drain failed: " + error); Title = "9to1 AI Studio — close needs attention"; }
    }

    public async Task<byte[]?> ImportAsync(CancellationToken cancellationToken)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Open an AI Studio project", AllowMultiple = false, FileTypeFilter = [new("AI Studio project") { Patterns = ["*.json"] }] });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("The project bundle exceeds the 16 MiB import limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    public async Task<bool> ExportAsync(string suggestedName, byte[] bytes, CancellationToken cancellationToken)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export AI Studio project", SuggestedFileName = suggestedName, DefaultExtension = "json",
          FileTypeChoices = [new("AI Studio project") { Patterns = ["*.json"] }] });
        if (file is null) return false;
        await using var output = await file.OpenWriteAsync();
        await StudioProjectExport.WriteAsync(output, bytes, cancellationToken);
        return true;
    }

    private sealed class PresentationReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "StudioPresentation",
                "Studio presentation can load. Runtime and self-check readiness are verified independently."));
        }
    }
}
