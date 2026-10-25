using System.Globalization;

namespace CakeOS.Apps.Boards.Contract;

// ============================================================
// Schema v3 semantic model: styles, images, graphs, formatting.
// All additions are optional-with-defaults so v2 payloads migrate
// without loss. Persisted types stay free of UI framework objects.
// ============================================================

public enum HavenRichBaseline
{
    Normal = 0,
    Subscript = 1,
    Superscript = 2
}

public enum HavenRichAlignment
{
    Inherit = 0,
    Left = 1,
    Center = 2,
    Right = 3,
    Justify = 4
}

public enum HavenRichCellVerticalAlignment
{
    Inherit = 0,
    Top = 1,
    Middle = 2,
    Bottom = 3
}

public enum HavenRichBorderStyle
{
    Inherit = 0,
    None = 1,
    Solid = 2,
    Dashed = 3,
    Dotted = 4
}

public enum HavenRichInkTool
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
    Selector = 3
}

// ---------------- Styles ----------------

/// <summary>
/// A named, reusable formatting definition owned by the board document.
/// Direct block/run properties always override the resolved style.
/// </summary>
public sealed class HavenRichStyle
{
    public string Id { get; set; } = "style-" + Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "Custom style";
    public bool IsBuiltIn { get; set; }
    public HavenRichBlockKind BlockKind { get; set; } = HavenRichBlockKind.Paragraph;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }
    public HavenRichBaseline Baseline { get; set; } = HavenRichBaseline.Normal;
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public HavenRichAlignment Alignment { get; set; } = HavenRichAlignment.Inherit;
    public double LineSpacing { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public int IndentLevel { get; set; } = -1;
}

public static class HavenRichStyles
{
    public const string NormalId = "normal";

    public static List<HavenRichStyle> BuiltIns() =>
    [
        new() { Id = NormalId, Name = "Paragraph", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Paragraph, FontSize = 14, LineSpacing = 1.25, SpaceAfter = 8 },
        new() { Id = "title", Name = "Title", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 32, SpaceAfter = 12 },
        new() { Id = "subtitle", Name = "Subtitle", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, FontSize = 20, Foreground = "#FF9AA0A6", SpaceAfter = 10 },
        new() { Id = "heading-1", Name = "Header 1", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 26, SpaceBefore = 16, SpaceAfter = 8 },
        new() { Id = "heading-2", Name = "Header 2", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 21, SpaceBefore = 12, SpaceAfter = 6 },
        new() { Id = "heading-3", Name = "Header 3", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 17, SpaceBefore = 10, SpaceAfter = 4 },
        new() { Id = "heading-4", Name = "Header 4", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 15, SpaceBefore = 8, SpaceAfter = 4 },
        new() { Id = "heading-5", Name = "Header 5", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 13, SpaceBefore = 6, SpaceAfter = 3 },
        new() { Id = "heading-6", Name = "Header 6", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Heading, Bold = true, FontSize = 12, SpaceBefore = 4, SpaceAfter = 2 },
        new() { Id = "quote", Name = "Quote", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Paragraph, Italic = true, Foreground = "#FF9AA0A6", IndentLevel = 1, SpaceBefore = 8, SpaceAfter = 8 },
        new() { Id = "code", Name = "Code", IsBuiltIn = true, BlockKind = HavenRichBlockKind.Paragraph, FontFamily = "Cascadia Mono", FontSize = 13, Foreground = "#FF24292E", Background = "#FFF1F3F4", SpaceBefore = 8, SpaceAfter = 8 },
    ];

    public static void ValidateStyles(HavenRichNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var style in notes.Styles)
        {
            if (string.IsNullOrWhiteSpace(style.Id) || style.Id.Length > 128)
                throw new InvalidOperationException("Style IDs must contain 1 to 128 characters.");
            if (!ids.Add(style.Id))
                throw new InvalidOperationException($"Style ID '{style.Id}' is duplicated.");
            if (string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > 128)
                throw new InvalidOperationException("Style names must contain 1 to 128 characters.");
            if (style.FontSize is < 0 or > 256)
                throw new InvalidOperationException("Style font size is outside the supported range.");
        }
        if (!ids.Contains(NormalId))
            throw new InvalidOperationException("Board styles must include the built-in Paragraph style.");
    }
}

/// <summary>Resolves effective formatting: style defaults, overridden by direct block/run values.</summary>
public static class HavenRichStyleResolver
{
    public static HavenRichStyle Resolve(HavenRichNotes notes, HavenRichBlock block)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(block);
        return notes.Styles.FirstOrDefault(s => s.Id == block.StyleId)
            ?? notes.Styles.First(s => s.Id == HavenRichStyles.NormalId);
    }

    public static double EffectiveFontSize(HavenRichStyle style, HavenRichTextRun run) =>
        run.FontSize > 0 ? run.FontSize : style.FontSize > 0 ? style.FontSize : 14;

    public static string EffectiveFontFamily(HavenRichStyle style, HavenRichTextRun run) =>
        !string.IsNullOrEmpty(run.FontFamily) ? run.FontFamily
        : !string.IsNullOrEmpty(style.FontFamily) ? style.FontFamily : "Montserrat";

    public static HavenRichAlignment EffectiveAlignment(HavenRichStyle style, HavenRichBlock block) =>
        block.Alignment != HavenRichAlignment.Inherit ? block.Alignment
        : style.Alignment != HavenRichAlignment.Inherit ? style.Alignment : HavenRichAlignment.Left;
}

// ---------------- Image / divider ----------------

public sealed class HavenRichImage
{
    public string Id { get; set; } = "img-" + Guid.NewGuid().ToString("N")[..12];
    public string DisplayName { get; set; } = "image";
    public string MediaType { get; set; } = "image/png";
    public string? DataBase64 { get; set; }
    /// <summary>Sidecar reference ("sidecar:&lt;sha256&gt;") for payloads above the embed limit.</summary>
    public string? LocalReference { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Display width in device-independent pixels; 0 preserves natural size.</summary>
    public double Width { get; set; }
    /// <summary>Optional display height; 0 derives it from the cropped source aspect ratio.</summary>
    public double Height { get; set; }
    public bool KeepAspectRatio { get; set; } = true;
    /// <summary>Non-destructive quarter-turn or arbitrary display rotation in degrees.</summary>
    public double RotationDegrees { get; set; }
    /// <summary>Normalized source crop edges, each in the 0..1 range.</summary>
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    /// <summary>Renderer policy: contain, cover, fill or natural.</summary>
    public string FitMode { get; set; } = "contain";
    public HavenRichAlignment Alignment { get; set; } = HavenRichAlignment.Inherit;
    public string AltText { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string LinkUri { get; set; } = string.Empty;

    public static void Validate(HavenRichImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (string.IsNullOrWhiteSpace(image.MediaType) || !image.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image media type must be an image/* type.");
        if (string.IsNullOrEmpty(image.DataBase64) && string.IsNullOrEmpty(image.LocalReference))
            throw new InvalidOperationException("Images must carry embedded bytes or a sidecar reference.");
        if (!double.IsFinite(image.Width) || !double.IsFinite(image.Height) ||
            image.Width is < 0 or > 8000 || image.Height is < 0 or > 8000)
            throw new InvalidOperationException("Image dimensions are outside the supported range.");
        if (!double.IsFinite(image.RotationDegrees) || image.RotationDegrees is < -360 or > 360)
            throw new InvalidOperationException("Image rotation is outside the supported range.");
        if (!double.IsFinite(image.CropLeft) || !double.IsFinite(image.CropTop) ||
            !double.IsFinite(image.CropRight) || !double.IsFinite(image.CropBottom) ||
            image.CropLeft is < 0 or > 0.9 || image.CropRight is < 0 or > 0.9 ||
            image.CropTop is < 0 or > 0.9 || image.CropBottom is < 0 or > 0.9 ||
            image.CropLeft + image.CropRight >= 1 || image.CropTop + image.CropBottom >= 1)
            throw new InvalidOperationException("Image crop bounds are invalid.");
        if (image.FitMode is not ("contain" or "cover" or "fill" or "natural"))
            throw new InvalidOperationException("Image fit mode is unsupported.");
        if ((image.LinkUri?.Length ?? 0) > 2048)
            throw new InvalidOperationException("Image link metadata is too long.");
        if ((image.Caption?.Length ?? 0) > 2048 || (image.AltText?.Length ?? 0) > 2048)
            throw new InvalidOperationException("Image caption or accessibility text is too long.");
    }
}

public sealed class HavenRichDivider
{
    public double Thickness { get; set; } = 1;
    public HavenRichBorderStyle LineStyle { get; set; } = HavenRichBorderStyle.Solid;
    public string Color { get; set; } = "#FF5F6368";
}

// ---------------- Graph ----------------

public sealed class HavenRichGraphExpression
{
    public string Id { get; set; } = "expr-" + Guid.NewGuid().ToString("N")[..12];
    public string Text { get; set; } = "y = x";
    public bool Visible { get; set; } = true;
    public string Color { get; set; } = "#FF1A73E8";
    public double LineWidth { get; set; } = 2;
    public double? DomainMin { get; set; }
    public double? DomainMax { get; set; }
}

public sealed class HavenRichGraphViewport
{
    public double XMin { get; set; } = -10;
    public double XMax { get; set; } = 10;
    public double YMin { get; set; } = -10;
    public double YMax { get; set; } = 10;
}

public sealed class HavenRichGraphPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Persisted slider definition; the current value remains per-view-session.</summary>
public sealed class HavenRichGraphSlider
{
    public string VariableId { get; set; } = "variable-" + Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "a";
    public double Minimum { get; set; } = -5;
    public double Maximum { get; set; } = 5;
    public double Step { get; set; } = 0.1;
    public double DefaultValue { get; set; }
}

/// <summary>First-class graphing block: expressions and viewport persist as data, never as an image.</summary>
public sealed class HavenRichGraph
{
    public string GraphId { get; set; } = "graph-" + Guid.NewGuid().ToString("N")[..12];
    public List<HavenRichGraphExpression> Expressions { get; set; } = [new HavenRichGraphExpression()];
    public HavenRichGraphViewport Viewport { get; set; } = new();
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = true;
    public string XLabel { get; set; } = "x";
    public string YLabel { get; set; } = "y";
    public List<HavenRichGraphPoint> Points { get; set; } = [];
    public List<HavenRichGraphSlider> Sliders { get; set; } = [];
}

public static class HavenRichGraphValidator
{
    public static void Validate(HavenRichGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (string.IsNullOrWhiteSpace(graph.GraphId) || graph.GraphId.Length > 128)
            throw new InvalidOperationException("GraphID must contain 1 to 128 characters.");
        if (graph.Expressions.Count > 32)
            throw new InvalidOperationException("Graphs support at most 32 expressions.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in graph.Expressions)
        {
            if (string.IsNullOrWhiteSpace(expression.Id) || expression.Id.Length > 128)
                throw new InvalidOperationException("Graph expression IDs must contain 1 to 128 characters.");
            if (!ids.Add(expression.Id))
                throw new InvalidOperationException($"Graph expression ID '{expression.Id}' is duplicated.");
            if (string.IsNullOrWhiteSpace(expression.Text) || expression.Text.Length > 512)
                throw new InvalidOperationException("Graph expressions must contain 1 to 512 characters.");
            if (expression.LineWidth is <= 0 or > 32)
                throw new InvalidOperationException("Graph line width is outside the supported range.");
            if (expression.DomainMin.HasValue && expression.DomainMax.HasValue &&
                expression.DomainMin.Value >= expression.DomainMax.Value)
                throw new InvalidOperationException("Graph domain minimum must be below the maximum.");
        }
        if (graph.Sliders.Count > 16)
            throw new InvalidOperationException("Graphs support at most 16 sliders.");
        var sliderIds = new HashSet<string>(StringComparer.Ordinal);
        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "x", "pi", "e", "sin", "cos", "tan", "sqrt", "abs", "exp", "ln", "log" };
        foreach (var slider in graph.Sliders)
        {
            if (string.IsNullOrWhiteSpace(slider.VariableId) || slider.VariableId.Length > 128 || !sliderIds.Add(slider.VariableId))
                throw new InvalidOperationException("Graph VariableIDs must be unique and contain 1 to 128 characters.");
            if (string.IsNullOrWhiteSpace(slider.Name) || slider.Name.Length > 16 ||
                !char.IsAsciiLetter(slider.Name[0]) || slider.Name.Any(character => !char.IsAsciiLetter(character)) ||
                reservedNames.Contains(slider.Name) || !variableNames.Add(slider.Name))
                throw new InvalidOperationException("Graph slider names must be unique letters and cannot replace x, constants, or functions.");
            if (!double.IsFinite(slider.Minimum) || !double.IsFinite(slider.Maximum) ||
                !double.IsFinite(slider.Step) || !double.IsFinite(slider.DefaultValue) ||
                slider.Minimum >= slider.Maximum || slider.Step <= 0 ||
                slider.DefaultValue < slider.Minimum || slider.DefaultValue > slider.Maximum ||
                Math.Abs((slider.DefaultValue - slider.Minimum) / slider.Step -
                    Math.Round((slider.DefaultValue - slider.Minimum) / slider.Step)) > 1e-9 ||
                (slider.Maximum - slider.Minimum) / slider.Step > 10_000 ||
                slider.Maximum - slider.Minimum > 1e9)
                throw new InvalidOperationException("Graph slider range, step, or default value is invalid.");
        }
        var viewport = graph.Viewport;
        if (!double.IsFinite(viewport.XMin) || !double.IsFinite(viewport.XMax) ||
            !double.IsFinite(viewport.YMin) || !double.IsFinite(viewport.YMax) ||
            viewport.XMin >= viewport.XMax || viewport.YMin >= viewport.YMax ||
            Math.Abs(viewport.XMax - viewport.XMin) > 1e12 || Math.Abs(viewport.YMax - viewport.YMin) > 1e12)
            throw new InvalidOperationException("Graph viewport is invalid.");
        if (graph.Points.Count > 4096)
            throw new InvalidOperationException("Graphs support at most 4096 points.");
        foreach (var point in graph.Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new InvalidOperationException("Graph points must be finite.");
        }
    }
}

/// <summary>
/// Deterministic local math evaluator for graph expressions.
/// Ported from the in-repo Haven GenUI plot parser (recursive descent, no AI,
/// no network): + - * / ^, parentheses, x, pi, e, sin/cos/tan/sqrt/abs/exp/ln/log,
/// plus implicit multiplication (2x, 3(x+1)) and an optional "y =" prefix.
/// </summary>
public static class HavenGraphExpression
{
    /// <summary>Recognizes a single explicit y-versus-function inequality. The
    /// returned function is evaluated by the same bounded graph expression parser.</summary>
    public static bool TryGetInequality(string expression, out string function, out bool shadeAbove)
    {
        function = string.Empty;
        shadeAbove = false;
        var text = (expression ?? string.Empty).Trim();
        foreach (var relation in new[] { "<=", ">=", "<", ">" })
        {
            var index = text.IndexOf(relation, StringComparison.Ordinal);
            if (index <= 0 || index + relation.Length >= text.Length ||
                text.IndexOf(relation, index + relation.Length, StringComparison.Ordinal) >= 0)
                continue;
            var left = text[..index].Trim();
            var right = text[(index + relation.Length)..].Trim();
            var leftIsY = left is "y" or "Y";
            var rightIsY = right is "y" or "Y";
            if (leftIsY == rightIsY)
                continue;
            function = leftIsY ? right : left;
            if (function.Length == 0)
                continue;
            var pointsUp = relation[0] == '>';
            shadeAbove = leftIsY ? pointsUp : !pointsUp;
            return true;
        }
        return false;
    }

    public static bool TryEvaluate(string expression, double x, out double value,
        IReadOnlyDictionary<string, double>? variables = null)
    {
        try
        {
            value = new Parser(StripFunctionPrefix(expression), x, variables).Parse();
            if (!double.IsFinite(value))
            {
                value = double.NaN;
                return false;
            }
            return true;
        }
        catch
        {
            value = double.NaN;
            return false;
        }
    }

    public static string StripFunctionPrefix(string expression)
    {
        var text = (expression ?? string.Empty).Trim();
        var equals = text.IndexOf('=');
        if (equals >= 0)
        {
            var left = text[..equals].Trim();
            if (left is "y" or "Y" or "f(x)" or "f (x)")
                return text[(equals + 1)..].Trim();
        }
        return text;
    }

    /// <summary>Samples an expression across a range; non-finite samples are omitted (curve breaks).</summary>
    public static List<(double X, double Y)> Sample(
        string expression, double? domainMin, double? domainMax,
        double rangeMin, double rangeMax, int count, IReadOnlyDictionary<string, double>? variables = null)
    {
        var body = StripFunctionPrefix(expression);
        var result = new List<(double X, double Y)>();
        count = Math.Clamp(count, 2, 4096);
        for (var i = 0; i < count; i++)
        {
            var x = rangeMin + ((rangeMax - rangeMin) * i / (count - 1));
            if (domainMin.HasValue && x < domainMin.Value) continue;
            if (domainMax.HasValue && x > domainMax.Value) continue;
            if (TryEvaluate(body, x, out var y, variables))
                result.Add((x, y));
        }
        return result;
    }

    private sealed class Parser(string source, double x, IReadOnlyDictionary<string, double>? variables)
    {
        private int _index;

        public double Parse()
        {
            var value = AddSubtract();
            Skip();
            if (_index != source.Length) throw new FormatException();
            return value;
        }

        private double AddSubtract()
        {
            var value = MultiplyDivide();
            while (true)
            {
                Skip();
                if (Take('+')) value += MultiplyDivide();
                else if (Take('-')) value -= MultiplyDivide();
                else return value;
            }
        }

        private double MultiplyDivide()
        {
            var value = Power();
            while (true)
            {
                Skip();
                if (Take('*')) value *= Power();
                else if (Take('/')) value /= Power();
                else if (StartsFactor()) value *= Power();
                else return value;
            }
        }

        private double Power()
        {
            var value = Unary();
            Skip();
            if (Take('^')) value = Math.Pow(value, Power());
            return value;
        }

        private double Unary()
        {
            Skip();
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            return Primary();
        }

        private double Primary()
        {
            Skip();
            if (Take('('))
            {
                var value = AddSubtract();
                if (!Take(')')) throw new FormatException();
                return value;
            }
            if (_index < source.Length && (char.IsDigit(source[_index]) || source[_index] == '.'))
            {
                var start = _index;
                while (_index < source.Length && (char.IsDigit(source[_index]) || source[_index] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    if (_index > start && source[_index] is '+' or '-' && source[_index - 1] is not ('e' or 'E')) break;
                    _index++;
                }
                return double.Parse(source[start.._index], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            var nameStart = _index;
            while (_index < source.Length && char.IsLetter(source[_index])) _index++;
            var name = source[nameStart.._index].ToLowerInvariant();
            if (name == "x") return x;
            if (name == "pi") return Math.PI;
            if (name == "e") return Math.E;
            if (variables is not null)
            {
                foreach (var pair in variables)
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                        return pair.Value;
            }
            if (name.Length == 0) throw new FormatException();
            if (!Take('(')) throw new FormatException();
            var arg = AddSubtract();
            if (!Take(')')) throw new FormatException();
            return name switch
            {
                "sin" => Math.Sin(arg),
                "cos" => Math.Cos(arg),
                "tan" => Math.Tan(arg),
                "sqrt" => Math.Sqrt(arg),
                "abs" => Math.Abs(arg),
                "exp" => Math.Exp(arg),
                "ln" or "log" => Math.Log(arg),
                _ => throw new FormatException()
            };
        }

        private bool StartsFactor()
        {
            if (_index >= source.Length) return false;
            var ch = source[_index];
            return char.IsDigit(ch) || ch == '.' || ch == '(' || char.IsLetter(ch);
        }

        private bool Take(char expected)
        {
            Skip();
            if (_index >= source.Length || source[_index] != expected) return false;
            _index++;
            return true;
        }

        private void Skip()
        {
            while (_index < source.Length && char.IsWhiteSpace(source[_index])) _index++;
        }
    }
}

public enum HavenAttachmentStatus
{
    Available = 0,
    Missing = 1,
    Sidecar = 2
}

/// <summary>Outcome of resolving an attachment payload for display.</summary>
public sealed record HavenAttachmentResolution(
    string AttachmentId,
    HavenAttachmentStatus Status,
    byte[]? EmbeddedBytes,
    string? SidecarPath,
    string Message);

// ---------------- v3 ops (partial) ----------------

public static partial class HavenRichNotesOps
{
    // ----- Styles -----

    public static HavenRichStyle CreateCustomStyle(HavenRichNotes notes, string name)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 128)
            throw new ArgumentException("Style names must contain 1 to 128 characters.", nameof(name));
        var style = new HavenRichStyle { Name = name.Trim() };
        notes.Styles.Add(style);
        TouchNotes(notes);
        return style;
    }

    public static HavenRichStyle DuplicateStyle(HavenRichNotes notes, string styleId, string? newName = null)
    {
        var source = RequireStyle(notes, styleId);
        var copy = new HavenRichStyle
        {
            Name = (string.IsNullOrWhiteSpace(newName) ? source.Name + " copy" : newName.Trim())[..Math.Min(128, (string.IsNullOrWhiteSpace(newName) ? source.Name + " copy" : newName.Trim()).Length)],
            IsBuiltIn = false,
            BlockKind = source.BlockKind,
            Bold = source.Bold,
            Italic = source.Italic,
            Underline = source.Underline,
            StrikeThrough = source.StrikeThrough,
            Baseline = source.Baseline,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Foreground = source.Foreground,
            Background = source.Background,
            Alignment = source.Alignment,
            LineSpacing = source.LineSpacing,
            SpaceBefore = source.SpaceBefore,
            SpaceAfter = source.SpaceAfter,
            IndentLevel = source.IndentLevel
        };
        notes.Styles.Add(copy);
        TouchNotes(notes);
        return copy;
    }

    public static void UpdateStyle(HavenRichNotes notes, string styleId, Action<HavenRichStyle> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var style = RequireStyle(notes, styleId);
        update(style);
        if (string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > 128)
            throw new InvalidOperationException("Style names must contain 1 to 128 characters.");
        if (style.FontSize is < 0 or > 256)
            throw new InvalidOperationException("Style font size is outside the supported range.");
        TouchNotes(notes);
    }

    public static void DeleteCustomStyle(HavenRichNotes notes, string styleId)
    {
        var style = RequireStyle(notes, styleId);
        if (style.IsBuiltIn)
            throw new InvalidOperationException("Built-in styles cannot be deleted.");
        notes.Styles.Remove(style);
        // Content using the deleted style falls back to Paragraph.
        foreach (var block in notes.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks))
        {
            if (block.StyleId == styleId)
                block.StyleId = HavenRichStyles.NormalId;
        }
        TouchNotes(notes);
    }

    public static void ApplyStyleToBlock(HavenRichNotes notes, string pageId, string blockId, string styleId)
    {
        RequireStyle(notes, styleId);
        RequireBlock(notes, pageId, blockId).StyleId = styleId;
        TouchNotes(notes);
    }

    private static HavenRichStyle RequireStyle(HavenRichNotes notes, string styleId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Styles.FirstOrDefault(s => s.Id == styleId)
            ?? throw new KeyNotFoundException("Style was not found.");
    }

    // ----- Runs -----

    public static void SetRunBaseline(HavenRichNotes notes, string pageId, string blockId, int runIndex, HavenRichBaseline baseline)
    {
        var run = RequireRun(notes, pageId, blockId, runIndex);
        run.Baseline = baseline;
        TouchNotes(notes);
    }

    public static void SetRunFont(HavenRichNotes notes, string pageId, string blockId, int runIndex, string? family, double size)
    {
        var run = RequireRun(notes, pageId, blockId, runIndex);
        if (size is < 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(size));
        run.FontFamily = family?.Trim() ?? string.Empty;
        run.FontSize = size;
        TouchNotes(notes);
    }

    public static void SetRunColors(HavenRichNotes notes, string pageId, string blockId, int runIndex, string? foreground, string? background)
    {
        var run = RequireRun(notes, pageId, blockId, runIndex);
        run.Foreground = foreground?.Trim() ?? string.Empty;
        run.Background = background?.Trim() ?? string.Empty;
        TouchNotes(notes);
    }

    public static void ClearRunFormatting(HavenRichNotes notes, string pageId, string blockId, int runIndex)
    {
        var run = RequireRun(notes, pageId, blockId, runIndex);
        run.Bold = run.Italic = run.Underline = run.StrikeThrough = false;
        run.Baseline = HavenRichBaseline.Normal;
        run.FontFamily = run.Foreground = run.Background = string.Empty;
        run.FontSize = 0;
        TouchNotes(notes);
    }

    private static HavenRichTextRun RequireRun(HavenRichNotes notes, string pageId, string blockId, int runIndex)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (runIndex < 0 || runIndex >= block.Runs.Count)
            throw new ArgumentOutOfRangeException(nameof(runIndex));
        return block.Runs[runIndex];
    }

    // ----- Paragraph -----

    public static void SetBlockParagraph(HavenRichNotes notes, string pageId, string blockId,
        HavenRichAlignment alignment, double lineSpacing, double spaceBefore, double spaceAfter, int indentLevel)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (lineSpacing is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(lineSpacing));
        if (indentLevel is < -1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(indentLevel));
        block.Alignment = alignment;
        block.LineSpacing = lineSpacing;
        block.SpaceBefore = Math.Clamp(spaceBefore, 0, 1000);
        block.SpaceAfter = Math.Clamp(spaceAfter, 0, 1000);
        block.IndentLevel = indentLevel;
        TouchNotes(notes);
    }

    // ----- Lists -----

    public static bool RemoveListItem(HavenRichNotes notes, string pageId, string blockId, string itemId)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var removed = block.Items.RemoveAll(i => i.Id == itemId) > 0;
        if (removed) TouchNotes(notes);
        return removed;
    }

    public static HavenRichListItem InsertListItem(HavenRichNotes notes, string pageId, string blockId, int index, string text)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind is not (HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList))
            throw new InvalidOperationException("List items require a list block.");
        var item = new HavenRichListItem { Text = text ?? string.Empty };
        block.Items.Insert(Math.Clamp(index, 0, block.Items.Count), item);
        TouchNotes(notes);
        return item;
    }

    public static bool SetListItemLevel(HavenRichNotes notes, string pageId, string blockId, string itemId, int level)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var item = block.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return false;
        item.Level = Math.Clamp(level, 0, 8);
        TouchNotes(notes);
        return true;
    }

    public static bool SetListItemFormatting(HavenRichNotes notes, string pageId, string blockId, string itemId,
        bool? bold = null, bool? italic = null, bool? underline = null, bool? strike = null,
        HavenRichBaseline? baseline = null, string? foreground = null)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var item = block.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return false;
        if (bold.HasValue) item.Bold = bold.Value;
        if (italic.HasValue) item.Italic = italic.Value;
        if (underline.HasValue) item.Underline = underline.Value;
        if (strike.HasValue) item.StrikeThrough = strike.Value;
        if (baseline.HasValue) item.Baseline = baseline.Value;
        if (foreground is not null) item.Foreground = foreground;
        TouchNotes(notes);
        return true;
    }

    // ----- Tables -----

    public static void InsertTableRow(HavenRichNotes notes, string pageId, string blockId, int index)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table))
            throw new InvalidOperationException("Split merged cells before adding a table row.");
        var width = table.Rows[0].Cells.Count;
        var row = new HavenRichTableRow();
        for (var c = 0; c < width; c++)
            row.Cells.Add(new HavenRichTableCell());
        table.Rows.Insert(Math.Clamp(index, 0, table.Rows.Count), row);
        table.RowHeights.Insert(Math.Clamp(index, 0, table.RowHeights.Count), 0);
        TouchNotes(notes);
    }

    public static bool DeleteTableRow(HavenRichNotes notes, string pageId, string blockId, int index)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table)) return false;
        if (table.Rows.Count <= 1 || index < 0 || index >= table.Rows.Count) return false;
        table.Rows.RemoveAt(index);
        if (index < table.RowHeights.Count) table.RowHeights.RemoveAt(index);
        TouchNotes(notes);
        return true;
    }

    /// <summary>Moves a complete stable table row, including its cells and row sizing, to a new index.</summary>
    public static bool MoveTableRow(HavenRichNotes notes, string pageId, string blockId, int fromIndex, int toIndex)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table)) return false;
        if (fromIndex < 0 || fromIndex >= table.Rows.Count || toIndex < 0 || toIndex >= table.Rows.Count)
            return false;
        if (fromIndex == toIndex) return false;
        while (table.RowHeights.Count < table.Rows.Count) table.RowHeights.Add(0);
        var row = table.Rows[fromIndex];
        table.Rows.RemoveAt(fromIndex);
        table.Rows.Insert(toIndex, row);
        MoveParallelValue(table.RowHeights, fromIndex, toIndex);
        TouchNotes(notes);
        return true;
    }

    public static void InsertTableColumn(HavenRichNotes notes, string pageId, string blockId, int index)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table))
            throw new InvalidOperationException("Split merged cells before adding a table column.");
        var width = table.Rows[0].Cells.Count;
        if (width >= 50) throw new InvalidOperationException("Tables support at most 50 columns.");
        var at = Math.Clamp(index, 0, width);
        foreach (var row in table.Rows)
            row.Cells.Insert(at, new HavenRichTableCell());
        table.ColumnWidths.Insert(Math.Clamp(at, 0, table.ColumnWidths.Count), 0);
        TouchNotes(notes);
    }

    public static bool DeleteTableColumn(HavenRichNotes notes, string pageId, string blockId, int index)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table)) return false;
        var width = table.Rows[0].Cells.Count;
        if (width <= 1 || index < 0 || index >= width) return false;
        foreach (var row in table.Rows)
            row.Cells.RemoveAt(index);
        if (index < table.ColumnWidths.Count) table.ColumnWidths.RemoveAt(index);
        TouchNotes(notes);
        return true;
    }

    /// <summary>Moves a complete stable table column, including every cell and its width, to a new index.</summary>
    public static bool MoveTableColumn(HavenRichNotes notes, string pageId, string blockId, int fromIndex, int toIndex)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (HasMergedCells(table)) return false;
        if (table.Rows.Count == 0) return false;
        var columnCount = table.Rows[0].Cells.Count;
        if (fromIndex < 0 || fromIndex >= columnCount || toIndex < 0 || toIndex >= columnCount)
            return false;
        if (fromIndex == toIndex) return false;
        if (table.Rows.Any(row => row.Cells.Count != columnCount))
            throw new InvalidOperationException("Cannot reorder columns in a non-rectangular table.");
        while (table.ColumnWidths.Count < columnCount) table.ColumnWidths.Add(0);
        foreach (var row in table.Rows)
        {
            var cell = row.Cells[fromIndex];
            row.Cells.RemoveAt(fromIndex);
            row.Cells.Insert(toIndex, cell);
        }
        MoveParallelValue(table.ColumnWidths, fromIndex, toIndex);
        TouchNotes(notes);
        return true;
    }

    /// <summary>True only when the selected cell can absorb the adjacent blank column segment.</summary>
    public static bool CanMergeTableCellRight(HavenRichNotes notes, string pageId, string blockId, int row, int column) =>
        CanMergeTableCell(notes, pageId, blockId, row, column, mergeRight: true);

    /// <summary>True only when the selected cell can absorb the adjacent blank row segment.</summary>
    public static bool CanMergeTableCellDown(HavenRichNotes notes, string pageId, string blockId, int row, int column) =>
        CanMergeTableCell(notes, pageId, blockId, row, column, mergeRight: false);

    private static bool CanMergeTableCell(HavenRichNotes notes, string pageId, string blockId,
        int row, int column, bool mergeRight)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (!TryGetTableCell(table, row, column, out var anchor) || anchor.CoveredByCellId is not null)
            return false;
        var lastRow = row + anchor.RowSpan;
        var lastColumn = column + anchor.ColumnSpan;
        if (lastRow > table.Rows.Count || lastColumn > table.Rows[0].Cells.Count)
            return false;
        if (mergeRight ? lastColumn >= table.Rows[0].Cells.Count : lastRow >= table.Rows.Count)
            return false;

        if (mergeRight)
        {
            for (var targetRow = row; targetRow < lastRow; targetRow++)
                if (!CanAbsorb(table.Rows[targetRow].Cells[lastColumn], anchor.Id)) return false;
        }
        else
        {
            for (var targetColumn = column; targetColumn < lastColumn; targetColumn++)
                if (!CanAbsorb(table.Rows[lastRow].Cells[targetColumn], anchor.Id)) return false;
        }
        return true;
    }

    private static bool CanAbsorb(HavenRichTableCell cell, string anchorId) =>
        cell.CoveredByCellId is null && cell.Id != anchorId && cell.RowSpan == 1 && cell.ColumnSpan == 1 &&
        cell.Text.Length == 0 && cell.Runs.Count == 0;

    public static bool MergeTableCellRight(HavenRichNotes notes, string pageId, string blockId, int row, int column)
    {
        if (!CanMergeTableCellRight(notes, pageId, blockId, row, column)) return false;
        var table = RequireTable(notes, pageId, blockId);
        var anchor = table.Rows[row].Cells[column];
        var targetColumn = column + anchor.ColumnSpan;
        for (var targetRow = row; targetRow < row + anchor.RowSpan; targetRow++)
            table.Rows[targetRow].Cells[targetColumn].CoveredByCellId = anchor.Id;
        anchor.ColumnSpan++;
        TouchNotes(notes);
        return true;
    }

    public static bool MergeTableCellDown(HavenRichNotes notes, string pageId, string blockId, int row, int column)
    {
        if (!CanMergeTableCellDown(notes, pageId, blockId, row, column)) return false;
        var table = RequireTable(notes, pageId, blockId);
        var anchor = table.Rows[row].Cells[column];
        var targetRow = row + anchor.RowSpan;
        for (var targetColumn = column; targetColumn < column + anchor.ColumnSpan; targetColumn++)
            table.Rows[targetRow].Cells[targetColumn].CoveredByCellId = anchor.Id;
        anchor.RowSpan++;
        TouchNotes(notes);
        return true;
    }

    public static bool SplitTableCell(HavenRichNotes notes, string pageId, string blockId, int row, int column)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (!TryGetTableCell(table, row, column, out var anchor) || anchor.CoveredByCellId is not null ||
            anchor.RowSpan == 1 && anchor.ColumnSpan == 1)
            return false;
        foreach (var covered in table.Rows.SelectMany(currentRow => currentRow.Cells)
                     .Where(cell => string.Equals(cell.CoveredByCellId, anchor.Id, StringComparison.Ordinal)))
            covered.CoveredByCellId = null;
        anchor.RowSpan = 1;
        anchor.ColumnSpan = 1;
        TouchNotes(notes);
        return true;
    }

    private static bool TryGetTableCell(HavenRichTable table, int row, int column, out HavenRichTableCell cell)
    {
        cell = null!;
        if (row < 0 || row >= table.Rows.Count || column < 0 || column >= table.Rows[row].Cells.Count)
            return false;
        cell = table.Rows[row].Cells[column];
        return true;
    }

    private static bool HasMergedCells(HavenRichTable table) => table.Rows.SelectMany(row => row.Cells)
        .Any(cell => cell.CoveredByCellId is not null || cell.RowSpan > 1 || cell.ColumnSpan > 1);

    private static void MoveParallelValue(List<double> values, int fromIndex, int toIndex)
    {
        if (fromIndex >= values.Count) return;
        var value = values[fromIndex];
        values.RemoveAt(fromIndex);
        values.Insert(Math.Min(toIndex, values.Count), value);
    }

    public static void SetColumnWidth(HavenRichNotes notes, string pageId, string blockId, int column, double width)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (column < 0 || column >= table.Rows[0].Cells.Count)
            throw new ArgumentOutOfRangeException(nameof(column));
        if (width is < 0 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(width));
        while (table.ColumnWidths.Count < table.Rows[0].Cells.Count)
            table.ColumnWidths.Add(0);
        table.ColumnWidths[column] = width;
        TouchNotes(notes);
    }

    public static void SetRowHeight(HavenRichNotes notes, string pageId, string blockId, int row, double height)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (row < 0 || row >= table.Rows.Count)
            throw new ArgumentOutOfRangeException(nameof(row));
        if (height is < 0 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(height));
        while (table.RowHeights.Count < table.Rows.Count)
            table.RowHeights.Add(0);
        table.RowHeights[row] = height;
        TouchNotes(notes);
    }

    public static void SetTableStyle(HavenRichNotes notes, string pageId, string blockId,
        string? background = null, string? borderColor = null, double? borderWidth = null,
        HavenRichBorderStyle? borderStyle = null, double? cornerRadius = null, bool? alternatingRows = null,
        bool? hasHeaderRow = null, bool? hasHeaderColumn = null, double? width = null,
        HavenRichAlignment? alignment = null)
    {
        var table = RequireTable(notes, pageId, blockId);
        if (background is not null) table.Background = background;
        if (borderColor is not null) table.BorderColor = borderColor;
        if (borderWidth.HasValue)
        {
            if (borderWidth.Value is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(borderWidth));
            table.BorderWidth = borderWidth.Value;
        }
        if (borderStyle.HasValue) table.BorderStyle = borderStyle.Value;
        if (cornerRadius.HasValue)
        {
            if (cornerRadius.Value is < 0 or > 128) throw new ArgumentOutOfRangeException(nameof(cornerRadius));
            table.CornerRadius = cornerRadius.Value;
        }
        if (alternatingRows.HasValue) table.AlternatingRows = alternatingRows.Value;
        if (hasHeaderRow.HasValue) table.HasHeaderRow = hasHeaderRow.Value;
        if (hasHeaderColumn.HasValue) table.HasHeaderColumn = hasHeaderColumn.Value;
        if (width.HasValue)
        {
            if (!double.IsFinite(width.Value) || width.Value is < 0 or > 5000)
                throw new ArgumentOutOfRangeException(nameof(width));
            table.Width = width.Value;
        }
        if (alignment.HasValue)
        {
            if (!Enum.IsDefined(alignment.Value)) throw new ArgumentOutOfRangeException(nameof(alignment));
            table.Alignment = alignment.Value;
        }
        TouchNotes(notes);
    }

    public static bool SetCellStyle(HavenRichNotes notes, string pageId, string blockId, string cellId,
        HavenRichAlignment? alignH = null, HavenRichCellVerticalAlignment? alignV = null,
        string? background = null, string? foreground = null,
        bool? bold = null, bool? italic = null, bool? underline = null, HavenRichBaseline? baseline = null)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var cell = block.Table?.Rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return false;
        if (alignH.HasValue) cell.AlignmentH = alignH.Value;
        if (alignV.HasValue) cell.AlignmentV = alignV.Value;
        if (background is not null) cell.Background = background;
        if (foreground is not null) cell.Foreground = foreground;
        if (bold.HasValue) cell.Bold = bold.Value;
        if (italic.HasValue) cell.Italic = italic.Value;
        if (underline.HasValue) cell.Underline = underline.Value;
        if (baseline.HasValue) cell.Baseline = baseline.Value;
        TouchNotes(notes);
        return true;
    }

    public static bool SetCellRuns(HavenRichNotes notes, string pageId, string blockId, string cellId, List<HavenRichTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var block = RequireBlock(notes, pageId, blockId);
        var cell = block.Table?.Rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return false;
        cell.Runs = runs;
        cell.Text = string.Concat(runs.Select(r => r.Text));
        TouchNotes(notes);
        return true;
    }

    private static HavenRichTable RequireTable(HavenRichNotes notes, string pageId, string blockId)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind != HavenRichBlockKind.Table || block.Table is null)
            throw new InvalidOperationException("Block is not a table block.");
        return block.Table;
    }

    // ----- Images / dividers -----

    public static HavenRichBlock AddImageBlock(HavenRichNotes notes, string pageId, HavenRichImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        HavenRichImage.Validate(image);
        var page = RequirePage(notes, pageId);
        var block = new HavenRichBlock { Kind = HavenRichBlockKind.Image, Image = image, Order = page.Blocks.Count };
        page.Blocks.Add(block);
        TouchNotes(notes);
        return block;
    }

    public static bool UpdateImage(HavenRichNotes notes, string pageId, string blockId, Action<HavenRichImage> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Image is null) return false;
        update(block.Image);
        HavenRichImage.Validate(block.Image);
        TouchNotes(notes);
        return true;
    }

    public static HavenRichBlock AddDividerBlock(HavenRichNotes notes, string pageId)
    {
        var page = RequirePage(notes, pageId);
        var block = new HavenRichBlock
        {
            Kind = HavenRichBlockKind.Divider,
            Divider = new HavenRichDivider(),
            Order = page.Blocks.Count
        };
        page.Blocks.Add(block);
        TouchNotes(notes);
        return block;
    }

    public static bool UpdateDivider(HavenRichNotes notes, string pageId, string blockId, double thickness, HavenRichBorderStyle style, string color)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Divider is null) return false;
        if (thickness is <= 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(thickness));
        block.Divider.Thickness = thickness;
        block.Divider.LineStyle = style;
        block.Divider.Color = color ?? string.Empty;
        TouchNotes(notes);
        return true;
    }

    public static bool RemoveBlock(HavenRichNotes notes, string pageId, string blockId)
    {
        var page = RequirePage(notes, pageId);
        var removed = page.Blocks.RemoveAll(b => b.Id == blockId) > 0;
        if (removed)
        {
            for (var order = 0; order < page.Blocks.Count; order++)
                page.Blocks[order].Order = order;
            TouchNotes(notes);
        }
        return removed;
    }

    public static bool RemoveAttachmentFromBlock(HavenRichNotes notes, string pageId, string blockId)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Attachment is null) return false;
        block.Attachment = null;
        TouchNotes(notes);
        return true;
    }

    // ----- Graphs -----

    public static HavenRichBlock AddGraphBlock(HavenRichNotes notes, string pageId)
    {
        var page = RequirePage(notes, pageId);
        var block = new HavenRichBlock
        {
            Kind = HavenRichBlockKind.Graph,
            Graph = new HavenRichGraph(),
            Order = page.Blocks.Count
        };
        page.Blocks.Add(block);
        TouchNotes(notes);
        return block;
    }

    public static HavenRichGraphExpression AddGraphExpression(HavenRichNotes notes, string pageId, string blockId, string text)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        if (graph.Expressions.Count >= 32)
            throw new InvalidOperationException("Graphs support at most 32 expressions.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512)
            throw new ArgumentException("Expressions must contain 1 to 512 characters.", nameof(text));
        var expression = new HavenRichGraphExpression { Text = text.Trim() };
        graph.Expressions.Add(expression);
        TouchNotes(notes);
        return expression;
    }

    public static bool UpdateGraphExpression(HavenRichNotes notes, string pageId, string blockId, string expressionId,
        string? text = null, bool? visible = null, string? color = null, double? lineWidth = null,
        double? domainMin = null, double? domainMax = null, bool clearDomain = false)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        var expression = graph.Expressions.FirstOrDefault(e => e.Id == expressionId);
        if (expression is null) return false;
        if (text is not null)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512)
                throw new ArgumentException("Expressions must contain 1 to 512 characters.", nameof(text));
            expression.Text = text.Trim();
        }
        if (visible.HasValue) expression.Visible = visible.Value;
        if (color is not null) expression.Color = color;
        if (lineWidth.HasValue)
        {
            if (lineWidth.Value is <= 0 or > 32) throw new ArgumentOutOfRangeException(nameof(lineWidth));
            expression.LineWidth = lineWidth.Value;
        }
        if (clearDomain) { expression.DomainMin = null; expression.DomainMax = null; }
        else
        {
            var nextMin = domainMin.HasValue ? domainMin : expression.DomainMin;
            var nextMax = domainMax.HasValue ? domainMax : expression.DomainMax;
            if (nextMin.HasValue && nextMax.HasValue && nextMin.Value >= nextMax.Value)
                throw new InvalidOperationException("Graph domain minimum must be below the maximum.");
            if (domainMin.HasValue) expression.DomainMin = domainMin;
            if (domainMax.HasValue) expression.DomainMax = domainMax;
        }
        TouchNotes(notes);
        return true;
    }

    public static bool RemoveGraphExpression(HavenRichNotes notes, string pageId, string blockId, string expressionId)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        var removed = graph.Expressions.RemoveAll(e => e.Id == expressionId) > 0;
        if (removed) TouchNotes(notes);
        return removed;
    }

    public static void SetGraphViewport(HavenRichNotes notes, string pageId, string blockId, HavenRichGraphViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        var graph = RequireGraph(notes, pageId, blockId);
        var candidate = new HavenRichGraph
        {
            GraphId = graph.GraphId,
            Expressions = graph.Expressions,
            Viewport = viewport,
            ShowGrid = graph.ShowGrid,
            ShowAxes = graph.ShowAxes,
            XLabel = graph.XLabel,
            YLabel = graph.YLabel,
            Points = graph.Points,
            Sliders = graph.Sliders
        };
        HavenRichGraphValidator.Validate(candidate);
        graph.Viewport = viewport;
        TouchNotes(notes);
    }

    public static void PanGraphViewport(HavenRichNotes notes, string pageId, string blockId, double dx, double dy)
    {
        var viewport = RequireGraph(notes, pageId, blockId).Viewport;
        var width = viewport.XMax - viewport.XMin;
        var height = viewport.YMax - viewport.YMin;
        SetGraphViewport(notes, pageId, blockId, new HavenRichGraphViewport
        {
            XMin = viewport.XMin + dx * width,
            XMax = viewport.XMax + dx * width,
            YMin = viewport.YMin + dy * height,
            YMax = viewport.YMax + dy * height
        });
    }

    public static void ZoomGraphViewport(HavenRichNotes notes, string pageId, string blockId, double factor, double centerX = 0, double centerY = 0)
    {
        if (factor is <= 0 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(factor));
        var viewport = RequireGraph(notes, pageId, blockId).Viewport;
        var width = (viewport.XMax - viewport.XMin) / factor;
        var height = (viewport.YMax - viewport.YMin) / factor;
        SetGraphViewport(notes, pageId, blockId, new HavenRichGraphViewport
        {
            XMin = centerX - (width / 2),
            XMax = centerX + (width / 2),
            YMin = centerY - (height / 2),
            YMax = centerY + (height / 2)
        });
    }

    public static void SetGraphOptions(HavenRichNotes notes, string pageId, string blockId,
        bool? showGrid = null, bool? showAxes = null, string? xLabel = null, string? yLabel = null)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        if (showGrid.HasValue) graph.ShowGrid = showGrid.Value;
        if (showAxes.HasValue) graph.ShowAxes = showAxes.Value;
        if (xLabel is not null) graph.XLabel = xLabel;
        if (yLabel is not null) graph.YLabel = yLabel;
        TouchNotes(notes);
    }

    public static void SetGraphSliders(HavenRichNotes notes, string pageId, string blockId,
        IReadOnlyList<HavenRichGraphSlider> sliders)
    {
        ArgumentNullException.ThrowIfNull(sliders);
        var graph = RequireGraph(notes, pageId, blockId);
        var candidateSliders = sliders.Select(slider => new HavenRichGraphSlider
        {
            VariableId = slider.VariableId,
            Name = slider.Name.Trim(),
            Minimum = slider.Minimum,
            Maximum = slider.Maximum,
            Step = slider.Step,
            DefaultValue = slider.DefaultValue,
        }).ToList();
        var candidate = new HavenRichGraph
        {
            GraphId = graph.GraphId,
            Expressions = graph.Expressions,
            Viewport = graph.Viewport,
            ShowGrid = graph.ShowGrid,
            ShowAxes = graph.ShowAxes,
            XLabel = graph.XLabel,
            YLabel = graph.YLabel,
            Points = graph.Points,
            Sliders = candidateSliders,
        };
        HavenRichGraphValidator.Validate(candidate);
        graph.Sliders = candidateSliders;
        TouchNotes(notes);
    }

    public static HavenRichGraphPoint AddGraphPoint(HavenRichNotes notes, string pageId, string blockId, double x, double y, string? label = null)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        if (graph.Points.Count >= 4096)
            throw new InvalidOperationException("Graphs support at most 4096 points.");
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException("Graph points must be finite.");
        var point = new HavenRichGraphPoint { X = x, Y = y, Label = label ?? string.Empty };
        graph.Points.Add(point);
        TouchNotes(notes);
        return point;
    }

    public static bool RemoveGraphPoint(HavenRichNotes notes, string pageId, string blockId, int index)
    {
        var graph = RequireGraph(notes, pageId, blockId);
        if (index < 0 || index >= graph.Points.Count) return false;
        graph.Points.RemoveAt(index);
        TouchNotes(notes);
        return true;
    }

    private static HavenRichGraph RequireGraph(HavenRichNotes notes, string pageId, string blockId)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind != HavenRichBlockKind.Graph || block.Graph is null)
            throw new InvalidOperationException("Block is not a graph block.");
        return block.Graph;
    }

    // ----- Canvas objects -----

    public static bool MoveCanvasObject(HavenRichNotes notes, string pageId, string objectId, double x, double y)
    {
        var page = RequirePage(notes, pageId);
        var box = page.Canvas.FirstOrDefault(o => o.Id == objectId);
        if (box is null) return false;
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentException("Canvas coordinates must be finite.");
        box.X = Math.Max(0, x);
        box.Y = Math.Max(0, y);
        page.CanvasWidth = Math.Max(page.CanvasWidth, box.X + box.Width + 40);
        page.CanvasHeight = Math.Max(page.CanvasHeight, box.Y + box.Height + 40);
        TouchNotes(notes);
        return true;
    }

    public static bool ResizeCanvasObject(HavenRichNotes notes, string pageId, string objectId, double width, double height)
    {
        var page = RequirePage(notes, pageId);
        var box = page.Canvas.FirstOrDefault(o => o.Id == objectId);
        if (box is null) return false;
        box.Width = Math.Clamp(width, 24, 5000);
        box.Height = Math.Clamp(height, 24, 5000);
        page.CanvasWidth = Math.Max(page.CanvasWidth, box.X + box.Width + 40);
        page.CanvasHeight = Math.Max(page.CanvasHeight, box.Y + box.Height + 40);
        TouchNotes(notes);
        return true;
    }

    public static bool UpdateCanvasObjectText(HavenRichNotes notes, string pageId, string objectId, string? text)
    {
        var page = RequirePage(notes, pageId);
        var box = page.Canvas.FirstOrDefault(o => o.Id == objectId);
        if (box is null) return false;
        box.Text = text?.Trim() ?? string.Empty;
        TouchNotes(notes);
        return true;
    }

    public static bool RemoveCanvasObject(HavenRichNotes notes, string pageId, string objectId)
    {
        var page = RequirePage(notes, pageId);
        var removed = page.Canvas.RemoveAll(o => o.Id == objectId) > 0;
        if (removed) TouchNotes(notes);
        return removed;
    }

    // ----- Ink tools -----

    public static bool SetInkStrokeTool(HavenRichNotes notes, string pageId, int strokeIndex, HavenRichInkTool tool)
    {
        var page = RequirePage(notes, pageId);
        if (strokeIndex < 0 || strokeIndex >= page.Ink.Count) return false;
        page.Ink[strokeIndex].Tool = tool;
        TouchNotes(notes);
        return true;
    }

    public static bool SetInkStrokeSelection(HavenRichNotes notes, string pageId, int strokeIndex, bool selected)
    {
        var page = RequirePage(notes, pageId);
        if (strokeIndex < 0 || strokeIndex >= page.Ink.Count) return false;
        page.Ink[strokeIndex].Selected = selected;
        TouchNotes(notes);
        return true;
    }

    public static bool RemoveInkStroke(HavenRichNotes notes, string pageId, int strokeIndex)
    {
        var page = RequirePage(notes, pageId);
        if (strokeIndex < 0 || strokeIndex >= page.Ink.Count) return false;
        page.Ink.RemoveAt(strokeIndex);
        TouchNotes(notes);
        return true;
    }

    /// <summary>Eraser hit-test: removes strokes passing within <paramref name="radius"/> of a point.</summary>
    public static int EraseInkAt(HavenRichNotes notes, string pageId, double x, double y, double radius = 8)
    {
        var page = RequirePage(notes, pageId);
        var removed = page.Ink.RemoveAll(stroke =>
            stroke.Points.Any(point =>
                Math.Abs(point.X - x) <= radius && Math.Abs(point.Y - y) <= radius));
        if (removed > 0) TouchNotes(notes);
        return removed;
    }

    public static void SetInkView(HavenRichNotes notes, string pageId, double panX, double panY, double zoom)
    {
        var page = RequirePage(notes, pageId);
        if (!double.IsFinite(panX) || !double.IsFinite(panY))
            throw new ArgumentException("Ink pan must be finite.");
        if (zoom is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        page.InkView = new HavenRichInkView { PanX = panX, PanY = panY, Zoom = zoom };
        TouchNotes(notes);
    }
}

// ---------------- v2 -> v3 migration ----------------

public static class HavenRichNotesV3Migration
{
    /// <summary>
    /// Upgrades a v2 payload in place: seeds built-in styles (plus heading-1 for RC1),
    /// repairs unknown style references to Paragraph, and validates the result.
    /// Returns the number of repaired style references.
    /// </summary>
    public static int Upgrade(HavenRichNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Styles.Count == 0)
            notes.Styles = HavenRichStyles.BuiltIns();
        else
        {
            foreach (var builtIn in HavenRichStyles.BuiltIns())
            {
                if (!notes.Styles.Any(s => s.Id == builtIn.Id))
                    notes.Styles.Add(builtIn);
            }
        }
        var known = new HashSet<string>(notes.Styles.Select(s => s.Id), StringComparer.Ordinal);
        var repaired = 0;
        foreach (var block in notes.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks))
        {
            if (!known.Contains(block.StyleId))
            {
                block.StyleId = HavenRichStyles.NormalId;
                repaired++;
            }
        }
        HavenRichNotesValidator.Validate(notes);
        HavenRichStyles.ValidateStyles(notes);
        return repaired;
    }
}
