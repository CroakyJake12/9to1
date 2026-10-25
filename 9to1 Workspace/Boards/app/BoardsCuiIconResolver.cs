using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CakeOS.Cui.Themes;
using Haven.UI;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CakeOS.Apps.Boards.App;

/// <summary>Resolves authored CUI icon keys from Haven's maintained vector catalog.</summary>
public static class BoardsCuiIconResolver
{
    private const string BoardsBrandSource = "ownerbrand:boards";
    private const string BoardsBrandResourceRoot = "avares://CakeOS.Apps.Boards.Embedding/Assets/OwnerAppIcons/";
    private static readonly HashSet<string> FilledSilhouetteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "more", "grip", "align-left", "align-center", "align-right", "bulleted-list",
    };
    private static readonly Dictionary<string, Bitmap> BoardsBrandBitmaps = new(StringComparer.Ordinal);
    private static readonly object BoardsBrandBitmapGate = new();
    private static readonly ConditionalWeakTable<Image, BoardsBrandAppearanceObserver> BoardsBrandObservers = new();

    public static IImage? Resolve(string source, Control target)
    {
        if (string.Equals(source, BoardsBrandSource, StringComparison.Ordinal))
        {
            var image = ResolveBoardsBrandImage(target);
            if (target is Image imageControl)
                BoardsBrandObservers.GetValue(imageControl, static control => new BoardsBrandAppearanceObserver(control)).Start();
            return image;
        }
        if (!source.StartsWith("icon:", StringComparison.Ordinal)) return null;
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("A Boards catalog icon requires the scoped CUI text brush.");
        return CreateIcon(source[5..], brush);
    }

    private static IImage? ResolveBoardsBrandImage(Control target)
    {
        var folder = AppearanceFolder(target);
        if (TryLoadBoardsBrandBitmap(folder, out var bitmap)) return bitmap;
        Trace.TraceWarning($"Boards owner-brand image '{folder}/boards.png' could not be loaded; using the canonical vector fallback.");
        return ResolveBoardsVectorFallback(target);
    }

    private static bool TryLoadBoardsBrandBitmap(string folder, out Bitmap? bitmap)
    {
        lock (BoardsBrandBitmapGate)
        {
            if (BoardsBrandBitmaps.TryGetValue(folder, out var cached))
            {
                bitmap = cached;
                return true;
            }

            var resourceUri = BoardsBrandResourceRoot + folder + "/boards.png";
            try
            {
                using var stream = AssetLoader.Open(new Uri(resourceUri, UriKind.Absolute));
                bitmap = new Bitmap(stream);
                BoardsBrandBitmaps.Add(folder, bitmap);
                return true;
            }
            catch (Exception exception) when (exception is FileNotFoundException or IOException
                                              or InvalidOperationException or ArgumentException)
            {
                Trace.TraceWarning($"Unable to load packaged Boards brand resource '{resourceUri}': {exception.Message}");
                bitmap = null;
                return false;
            }
        }
    }

    private static string AppearanceFolder(Control target)
    {
        if (!target.TryFindResource("CuiAppearance", out var raw))
            return AppearanceFolder(CuiThemeScopeApplier.DetectAppearance());

        if (raw is CuiAppearance direct)
        {
            if (!Enum.IsDefined(direct))
                throw new ArgumentException("Undefined CuiAppearance resource value.", nameof(target));
            return AppearanceFolder(direct);
        }

        if (raw is string text && Enum.TryParse<CuiAppearance>(text, true, out var parsed) && Enum.IsDefined(parsed))
            return AppearanceFolder(parsed);

        throw new ArgumentException("The CuiAppearance resource must contain a defined appearance.", nameof(target));
    }

    private static string AppearanceFolder(CuiAppearance appearance) => appearance switch
    {
        CuiAppearance.Bright or CuiAppearance.SuperBright => "light",
        CuiAppearance.Dark or CuiAppearance.SuperDark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Undefined CuiAppearance value."),
    };

    private static IImage? ResolveBoardsVectorFallback(Control target)
    {
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
        {
            Trace.TraceError("Boards brand image is unavailable and no scoped CUI text brush exists for its vector fallback.");
            return null;
        }
        return CreateIcon("boards", brush);
    }

    private sealed class BoardsBrandAppearanceObserver : IObserver<object?>
    {
        private readonly Image _target;
        private IDisposable? _subscription;
        private bool _started;

        public BoardsBrandAppearanceObserver(Image target) => _target = target;

        public void Start()
        {
            if (_started) return;
            _started = true;
            _target.AttachedToVisualTree += OnAttached;
            _target.DetachedFromVisualTree += OnDetached;
            if (_target.IsAttachedToVisualTree()) Attach();
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args) => Attach();

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Attach()
        {
            _subscription?.Dispose();
            _subscription = _target.GetResourceObservable("CuiAppearance").Subscribe(this);
            Refresh();
        }

        public void OnNext(object? value) => Refresh();

        public void OnError(Exception error)
        {
            Trace.TraceError($"Boards brand image appearance subscription failed: {error.Message}");
            _target.Source = ResolveBoardsVectorFallback(_target) as IImage;
        }

        public void OnCompleted()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        private void Refresh()
        {
            try
            {
                _target.Source = ResolveBoardsBrandImage(_target);
            }
            catch (ArgumentException exception)
            {
                Trace.TraceError($"Boards brand image appearance is invalid: {exception.Message}");
                _target.Source = ResolveBoardsVectorFallback(_target);
            }
        }
    }

    public static DrawingImage CreateIcon(string key, IBrush brush)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(brush);
        var filledSilhouette = FilledSilhouetteKeys.Contains(key.Trim());
        var source = HavenIconCatalog.Resolve(key);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.SetFillRule(source.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in source.Path.Figures)
            {
                writer.BeginFigure(new Point(figure.Start.X, figure.Start.Y), filledSilhouette && figure.Closed);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case HavenLineSegment line:
                            writer.LineTo(new Point(line.End.X, line.End.Y), true);
                            break;
                        case HavenQuadraticBezierSegment quadratic:
                            writer.QuadraticBezierTo(new Point(quadratic.Control.X, quadratic.Control.Y),
                                new Point(quadratic.End.X, quadratic.End.Y), true);
                            break;
                        case HavenCubicBezierSegment cubic:
                            writer.CubicBezierTo(new Point(cubic.Control1.X, cubic.Control1.Y),
                                new Point(cubic.Control2.X, cubic.Control2.Y), new Point(cubic.End.X, cubic.End.Y), true);
                            break;
                        case HavenArcSegment arc:
                            writer.ArcTo(new Point(arc.End.X, arc.End.Y),
                                new Size(arc.Radius.Width, arc.Radius.Height), arc.RotationDegrees, arc.IsLargeArc,
                                arc.SweepDirection == HavenSweepDirection.Clockwise
                                    ? SweepDirection.Clockwise
                                    : SweepDirection.CounterClockwise, true);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported Boards icon segment '{segment.GetType().Name}'.");
                    }
                }
                writer.EndFigure(figure.Closed);
            }
        }
        return new DrawingImage(new GeometryDrawing
        {
            Geometry = geometry,
            Brush = filledSilhouette ? brush : null,
            Pen = filledSilhouette ? null : new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        })
        {
            Viewbox = source.ViewBox is { } viewBox
                ? new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height)
                : new Rect(0, 0, 24, 24),
        };
    }
}
