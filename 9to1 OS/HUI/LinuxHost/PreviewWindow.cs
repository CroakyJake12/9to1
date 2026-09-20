using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using CakeOS.Canvas.App;
using CakeOS.Hui.Renderer;
using CakeOS.Platform;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiPage = Haven.UI.Components.Page;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiLinuxHost;

public sealed class PreviewWindow : Window
{
    private readonly HuiAppSurface? _surface;
    private readonly CanvasRoot? _canvasRoot;
    private readonly HuiButton? _demoAction;
    private readonly HuiText? _demoStatus;
    private readonly CakeTheme? _theme;
    private SvgImage? _frameImage;
    private SvgSource? _frameSource;
    private long _frameVersion = -1;
    private string? _frameSvg;

    public PreviewWindow() : this((IRootElement?)null)
    {
    }

    public PreviewWindow(IRootElement? root)
    {
        if (root is not null)
            throw new NotSupportedException("The Linux HUI host cannot render an unadapted platform root.");

        _theme = CakeTheme.Load();
        var scene = HuiDemoScene.Build(
            "CAKEOS / HUI LINUX BACKEND",
            "A real HUI scene, rendered as a Linux desktop window.",
            "GNOME and Mutter are untouched. This preview is an ordinary unprivileged process translating HUI draw commands through the shared renderer.");
        _demoAction = scene.Action;
        _demoStatus = scene.Status;
        var backend = new HuiBackendServices(_theme, _ => null);
        _surface = new HuiAppSurface(scene.Root,
            new HuiSurfaceServices(backend), HavenPlatform.Linux);

        Title = "CakeOS HUI Linux Preview";
        Width = 960;
        Height = 600;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(_theme.Resolve("Background"));
        Content = _surface;
        Closed += (_, _) => _surface.Dispose();
    }

    /// <summary>Hosts the real Canvas application (HUI tree + Rnote engine, shared with Windows).</summary>
    public PreviewWindow(CanvasController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _theme = CakeTheme.Load();
        _canvasRoot = new CanvasRoot(controller,
            new TopLevelFileDialogs(this), CanvasPaths.DefaultDocumentsDir());
        var backend = new HuiBackendServices(_theme, ImageFor);
        _surface = new HuiAppSurface(_canvasRoot.Page,
            new HuiSurfaceServices(backend,
                pointerPressed: e => _canvasRoot.OnPointerPressed(e),
                pointerMoved: e => _canvasRoot.OnPointerMoved(e),
                pointerReleased: e => _canvasRoot.OnPointerReleased(e),
                keyDown: (key, ctrl) => _canvasRoot.TryShortcut(key, ctrl)),
            HavenPlatform.Linux);
        _canvasRoot.Changed += () => _surface.InvalidateVisual();
        _canvasRoot.TitleChanged += () => Title = _canvasRoot.Controller.WindowTitle;
        Title = _canvasRoot.Controller.WindowTitle;
        Width = 1100;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        Background = new SolidColorBrush(_theme.Resolve("Background"));
        Content = _surface;
        _surface.LayoutUpdated += (_, _) => _canvasRoot.RefreshViewportSize();
        Closing += async (_, e) =>
        {
            if (!await _canvasRoot.TryCloseAsync().ConfigureAwait(true))
                e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            if (Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_SAVE_ON_CLOSE") == "1")
                _canvasRoot.SaveOnClose();
            _frameImage = null;
            _frameSource?.Dispose();
            _frameSource = null;
            _surface.Dispose();
            _canvasRoot.Dispose();
        };
    }

    public void RunInputSelfTest()
    {
        if (_canvasRoot is not null)
        {
            _canvasRoot.RunSelfTest();
            Title = $"{_canvasRoot.Controller.WindowTitle} — input passed";
            return;
        }
        if (_surface is null || _demoAction is null || _demoStatus is null)
            throw new InvalidOperationException("Input self-test requires the preview scene.");
        _surface.RerunLayout();
        var center = new HavenPoint(
            _demoAction.Bounds.X + _demoAction.Bounds.Width / 2d,
            _demoAction.Bounds.Y + _demoAction.Bounds.Height / 2d);
        _demoAction.SetState(HavenElementState.Selected, false);
        _demoAction.Accessibility.Selected = false;
        _surface.Input.PointerPressed(center);
        if (!_surface.Input.PointerReleased(center) || _demoAction.Accessibility.Selected != true)
            throw new InvalidOperationException("HUI pointer activation self-test failed.");
        Title = "CakeOS HUI Linux Preview - input passed";
    }

    private IImage? ImageFor(string source)
    {
        if (_canvasRoot is null || !string.Equals(source, CanvasRoot.ViewportImageSource, StringComparison.Ordinal))
            return null;
        var controller = _canvasRoot.Controller;
        var svg = controller.CurrentFrameSvg;
        if (svg is null)
            return null;
        if (controller.FrameVersion != _frameVersion || !string.Equals(svg, _frameSvg, StringComparison.Ordinal))
        {
            var decoded = SvgSource.LoadFromSvg(svg);
            if (decoded.Picture is null)
            {
                decoded.Dispose();
                return null;
            }
            var image = new SvgImage { Source = decoded };
            _frameSource?.Dispose();
            _frameSource = decoded;
            _frameImage = image;
            _frameSvg = svg;
            _frameVersion = controller.FrameVersion;
        }
        return _frameImage;
    }
}
