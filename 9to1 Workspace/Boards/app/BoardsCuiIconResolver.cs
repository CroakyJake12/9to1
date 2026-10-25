using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Haven.UI;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CakeOS.Apps.Boards.App;

/// <summary>Resolves authored CUI icon keys from Haven's maintained vector catalog.</summary>
public static class BoardsCuiIconResolver
{
    private const string BoardsBrandSource = "ownerbrand:boards";
    private const string BoardsBrandResourceRoot = "CakeOS.Apps.Boards.Embedding.Assets.OwnerAppIcons.";
    private static readonly HashSet<string> FilledSilhouetteKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "more", "grip", "section", "boards", "folder", "file", "chat", "study", "calendar",
        "target", "bookmark", "shield", "play", "align-left", "align-center", "align-right", "bulleted-list",
    };
    private static readonly Dictionary<string, Bitmap> BoardsBrandBitmaps = new(StringComparer.Ordinal);
    private static readonly object BoardsBrandBitmapGate = new();
    private static readonly ConditionalWeakTable<Image, BoardsBrandAppearanceObserver> BoardsBrandObservers = new();
    private static readonly ConditionalWeakTable<Image, BoardsSemanticIconObserver> BoardsSemanticIconObservers = new();

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
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush)
            throw new InvalidOperationException("A Boards catalog icon requires the scoped CUI text brush.");
        var key = source[5..];
        if (target is Image semanticIcon)
            ObserveSemanticIcon(semanticIcon, key);
        return CreateIcon(key, SemanticBrush(key, target));
    }

    internal static void ObserveSemanticIcon(Image target, string key, string? colorOverride = null) =>
        BoardsSemanticIconObservers.GetValue(target, static control => new BoardsSemanticIconObserver(control)).Start(key, colorOverride);

    /// <summary>Color an icon by its action meaning, using the live CUI palette and contrast state.</summary>
    public static IBrush SemanticBrush(string key, Control? target = null, string? colorOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = key.Trim().ToLowerInvariant();
        var text = FindBrush("CuiTextBrush", target) ?? BoardsTheme.TextBrush;
        if (IsHighContrast(target)) return text;
        if (target is { IsEffectivelyEnabled: false })
            return FindBrush("CuiMutedBrush", target) ?? BoardsTheme.SecondaryTextBrush;

        var role = normalized switch
        {
            "plus" or "add" or "save" or "check" or "insert" => "CuiSuccessBrush",
            "pen" or "edit" or "bold" or "italic" or "underline" or "strikethrough" or
                "undo" or "redo" or "subscript" or "superscript" => "CuiAccentSecondaryBrush",
            "file" or "notes" or "section" or "paperclip" or "image" or "folder" => "CuiAccentTertiaryBrush",
            "lock" or "unlock" => "HavenAttentionBrush",
            "table" or "chart" or "bulleted-list" or "numbered-list" or "align-left" or
                "align-center" or "align-right" or "indent-increase" or "indent-decrease" or
                "search" or "info" or "chevron-left" or "chevron-right" => "CuiInformationBrush",
            "more" or "ellipsis" or "grip" or "chevron-down" or "chevron-up" => "CuiTextSoftBrush",
            "delete" or "trash" or "remove" => "BoardsDangerIconBrush",
            _ => "CuiTextBrush",
        };
        var palette = BoardsTheme.SharedPalette;
        var backgrounds = BackgroundColors(target, palette.Panel2);
        var background = backgrounds[0];
        var desired = colorOverride is not null
            ? Color.Parse(colorOverride)
            : role == "BoardsDangerIconBrush"
            ? Color.Parse(BoardsTheme.Mode == BoardsThemeMode.Dark ? "#FFFF918A" : "#FFB3261E")
            : FindBrush(role, target) is { } brush
                ? RepresentativeColor(brush, background)
                : PaletteColor(normalized, palette);
        return new SolidColorBrush(ContrastSafeColor(desired, backgrounds, 3d));
    }

    private static IBrush? FindBrush(string name, Control? target)
    {
        if (target?.TryFindResource(name, out var scoped) == true && scoped is IBrush scopedBrush)
            return scopedBrush;
        return Application.Current?.TryGetResource(name, null, out var global) == true && global is IBrush globalBrush
            ? globalBrush
            : null;
    }

    private static bool IsHighContrast(Control? target)
    {
        if (target?.TryFindResource("CuiHighContrast", out var scoped) == true && scoped is bool scopedValue)
            return scopedValue;
        return Application.Current?.TryGetResource("CuiHighContrast", null, out var global) == true && global is true;
    }

    private static IReadOnlyList<Color> BackgroundColors(Control? target, Color fallback)
    {
        var colors = new List<Color>();
        foreach (var resourceName in new[]
                 {
                     "CuiPanel2Brush", "CuiFloatingSurfaceMaterial", "CuiSurfaceMaterial",
                     "CuiWorkspaceMaterial", "CuiShellMaterial"
                 })
        {
            if (FindBrush(resourceName, target) is { } brush)
                AddBrushColors(colors, brush);
        }
        if (colors.Count == 0) colors.Add(fallback);
        return colors.Distinct().ToArray();
    }

    private static Color RepresentativeColor(IBrush brush, Color background)
    {
        if (brush is ISolidColorBrush solid) return solid.Color;
        if (brush is IGradientBrush gradient && gradient.GradientStops.Count > 0)
            return gradient.GradientStops
                .Select(stop => stop.Color)
                .OrderByDescending(color => CuiContrast.Ratio(color, background))
                .First();
        return background;
    }

    private static void AddBrushColors(List<Color> colors, IBrush brush)
    {
        switch (brush)
        {
            case ISolidColorBrush solid:
                colors.Add(solid.Color);
                break;
            case IGradientBrush gradient:
                colors.AddRange(gradient.GradientStops.Select(stop => stop.Color));
                break;
        }
    }

    private static Color ContrastSafeColor(Color desired, IReadOnlyList<Color> backgrounds, double minimumRatio)
    {
        var candidates = new List<Color> { desired, Colors.Black, Colors.White };
        candidates.AddRange(backgrounds.Select(background =>
            CuiContrast.EnsureForegroundContrast(desired, background, minimumRatio)));
        var best = candidates
            .Distinct()
            .Select(candidate => (Color: candidate, MinimumRatio: backgrounds.Min(background => CuiContrast.Ratio(candidate, background))))
            .OrderByDescending(candidate => candidate.MinimumRatio)
            .First();
        return best.MinimumRatio >= minimumRatio ? best.Color : desired;
    }

    private static Color PaletteColor(string key, CuiPalette palette) => key switch
    {
        "plus" or "add" or "save" or "check" or "insert" => Color.Parse(BoardsTheme.Mode == BoardsThemeMode.Dark ? "#FF70D5A0" : "#FF238A5A"),
        "delete" or "trash" or "remove" => Color.Parse(BoardsTheme.Mode == BoardsThemeMode.Dark ? "#FFFF918A" : "#FFB3261E"),
        "file" or "notes" or "section" or "paperclip" or "image" or "folder" => palette.AccentStrong,
        "table" or "chart" or "bulleted-list" or "numbered-list" or "align-left" or "align-center" or
            "align-right" or "indent-increase" or "indent-decrease" or "search" or "info" => palette.AccentSecondary,
        "more" or "ellipsis" or "grip" or "chevron-down" or "chevron-up" => palette.TextSoft,
        "lock" or "unlock" => palette.Attention,
        _ => palette.Accent,
    };

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

            var manifestName = BoardsBrandResourceRoot + folder + ".boards.png";
            try
            {
                using var stream = typeof(BoardsCuiIconResolver).Assembly.GetManifestResourceStream(manifestName)
                    ?? throw new FileNotFoundException("The Boards owner-brand resource is missing from its embedding assembly.", manifestName);
                bitmap = new Bitmap(stream);
                BoardsBrandBitmaps.Add(folder, bitmap);
                return true;
            }
            catch (Exception exception) when (exception is FileNotFoundException or IOException
                                              or InvalidOperationException or ArgumentException)
            {
                Trace.TraceWarning($"Unable to load packaged Boards brand resource '{manifestName}': {exception.Message}");
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

    private sealed class BoardsSemanticIconObserver : IObserver<object?>
    {
        private static readonly string[] ResourceKeys =
        [
            "CuiTheme", "CuiAppearance", "CuiHighContrast", "CuiAccentSecondaryBrush", "CuiAccentTertiaryBrush",
            "CuiInformationBrush", "CuiSuccessBrush", "HavenAttentionBrush", "CuiTextSoftBrush", "CuiMutedBrush",
            "CuiPanel2Brush", "CuiTextBrush",
        ];
        private readonly Image _target;
        private readonly List<IDisposable> _subscriptions = [];
        private string _key = string.Empty;
        private string? _colorOverride;
        private bool _started;
        private bool _attached;
        private bool _observingEnabled;

        public BoardsSemanticIconObserver(Image target) => _target = target;

        public void Start(string key, string? colorOverride)
        {
            _key = key;
            _colorOverride = colorOverride;
            if (!_started)
            {
                _started = true;
                _target.AttachedToVisualTree += OnAttached;
                _target.DetachedFromVisualTree += OnDetached;
            }
            if (_target.IsAttachedToVisualTree()) Attach();
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args) => Attach();

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) => Detach();

        private void Attach()
        {
            Detach();
            _attached = true;
            BoardsTheme.Changed += OnThemeChanged;
            _target.PropertyChanged += OnTargetPropertyChanged;
            _observingEnabled = true;
            foreach (var key in ResourceKeys)
                _subscriptions.Add(_target.GetResourceObservable(key).Subscribe(this));
            Refresh();
        }

        private void Detach()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            if (_attached) BoardsTheme.Changed -= OnThemeChanged;
            if (_observingEnabled) _target.PropertyChanged -= OnTargetPropertyChanged;
            _observingEnabled = false;
            _attached = false;
        }

        private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (ReferenceEquals(args.Property, InputElement.IsEffectivelyEnabledProperty))
                Refresh();
        }

        private void OnThemeChanged() => Refresh();

        public void OnNext(object? value) => Refresh();

        public void OnError(Exception error)
        {
            Trace.TraceError($"Boards icon theme observer failed: {error.Message}");
            Detach();
        }

        public void OnCompleted() => Detach();

        private void Refresh()
        {
            try
            {
                _target.Source = CreateIcon(_key, SemanticBrush(_key, _target, _colorOverride));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                Trace.TraceError($"Boards icon '{_key}' could not update for the active theme: {exception.Message}");
                if (_target.TryFindResource("CuiTextBrush", out var resource) && resource is IBrush brush)
                    _target.Source = CreateIcon(_key, brush);
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
            // Keep the authored open details visible on filled silhouettes (for example the
            // dividers in the Boards mark and the lines in the section glyph).
            Pen = new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        })
        {
            Viewbox = source.ViewBox is { } viewBox
                ? new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height)
                : new Rect(0, 0, 24, 24),
        };
    }
}
