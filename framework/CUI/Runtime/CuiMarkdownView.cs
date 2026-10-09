using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

public enum CuiMarkdownCodeAction
{
    Copy,
    AskToRun,
    AskToApply
}

/// <summary>One exact rendered code action, not code execution or resource authority.</summary>
public sealed class CuiMarkdownCodeActionRequest
{
    internal CuiMarkdownCodeActionRequest(CuiMarkdownView owner, long generation, CuiMarkdownCodeAction action, string language, string code)
    { Owner = owner; Generation = generation; Action = action; Language = language; Code = code; }
    internal CuiMarkdownView Owner { get; }
    internal long Generation { get; }
    public CuiMarkdownCodeAction Action { get; }
    public string Language { get; }
    public string Code { get; }
}

/// <summary>The maintained native production Markdown parser and view. Hosts own
/// explicit action effects; this renderer opens no URI, clipboard or executable.</summary>
public class CuiMarkdownView : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<CuiMarkdownView, string>(nameof(Text), string.Empty);
    private static readonly Regex InlinePattern = new(
        "(`[^`]+`|\\*\\*[^*]+\\*\\*|(?<!\\*)\\*[^*]+\\*(?!\\*)|\\[[^\\]]+\\]\\([^)]+\\)|\\$[^$\\n]+\\$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OrderedList = new("^\\s*(\\d+)[.)]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TaskList = new("^\\s*[-*+]\\s+\\[([ xX])\\]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BulletList = new("^\\s*[-*+]\\s+(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly StackPanel _root = new() { Spacing = 8 };
    private long _renderGeneration;
    public bool IsCurrentOriginalCodeAction(CuiMarkdownCodeActionRequest request) =>
        request is not null && ReferenceEquals(request.Owner, this) && request.Generation == _renderGeneration;
    protected virtual Border CreateSurface() => new();
    protected virtual TextBox CreateCodeInput() => new();
    protected virtual Button CreateCodeActionButton() => new();
    protected virtual CheckBox CreateTaskCheckbox() => new();

    public CuiMarkdownView()
    {
        Content = _root;
        this.GetObservable(TextProperty).Subscribe(new TextObserver(this));
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public event Action<CuiMarkdownCodeActionRequest>? CodeActionRequested;

    private void Rebuild(string? value)
    {
        _renderGeneration = checked(_renderGeneration + 1);
        _root.Children.Clear();
        var text = (value ?? string.Empty).ReplaceLineEndings("\n");
        if (text.Length == 0) return;
        var lines = text.Split('\n');
        var paragraph = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                var language = line[3..].Trim();
                var code = new StringBuilder();
                index++;
                while (index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                {
                    code.AppendLine(lines[index]);
                    index++;
                }
                _root.Children.Add(BuildCodeBlock(language, code.ToString().TrimEnd()));
                continue;
            }
            if (line.Trim() == "$$")
            {
                FlushParagraph(paragraph);
                var equation = new StringBuilder();
                index++;
                while (index < lines.Length && lines[index].Trim() != "$$")
                {
                    equation.AppendLine(lines[index]);
                    index++;
                }
                _root.Children.Add(BuildEquation(equation.ToString().Trim()));
                continue;
            }
            if (TryBuildTable(lines, ref index, out var table))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(table);
                continue;
            }
            if (TryBuildHeading(line, out var heading))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(heading);
                continue;
            }
            if (line.StartsWith('>'))
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildQuote(line.TrimStart('>', ' ')));
                continue;
            }
            if (TaskList.Match(line) is { Success: true } task)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildTask(task.Groups[2].Value, task.Groups[1].Value != " "));
                continue;
            }
            if (OrderedList.Match(line) is { Success: true } ordered)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildListItem(ordered.Groups[1].Value + ".", ordered.Groups[2].Value));
                continue;
            }
            if (BulletList.Match(line) is { Success: true } bullet)
            {
                FlushParagraph(paragraph);
                _root.Children.Add(BuildListItem("•", bullet.Groups[1].Value));
                continue;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph);
                continue;
            }
            paragraph.Add(line);
        }
        FlushParagraph(paragraph);
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        _root.Children.Add(BuildInlineText(string.Join("\n", paragraph), 14, FontWeight.Normal));
        paragraph.Clear();
    }

    private static bool TryBuildHeading(string line, out Control heading)
    {
        var count = line.TakeWhile(character => character == '#').Count();
        if (count is < 1 or > 6 || line.Length <= count || line[count] != ' ')
        {
            heading = null!;
            return false;
        }
        var size = count switch { 1 => 25, 2 => 21, 3 => 18, 4 => 16, _ => 14 };
        heading = BuildInlineText(line[(count + 1)..].Trim(), size, FontWeight.SemiBold);
        return true;
    }

    private Control BuildQuote(string text)
    {
        var surface = CreateSurface();
        surface.BorderThickness = new Thickness(3, 0, 0, 0);
        surface.CornerRadius = new CornerRadius(0, 8, 8, 0);
        surface.Padding = new Thickness(12, 8);
        surface.Child = BuildInlineText(text, 13, FontWeight.Normal, FontStyle.Italic);
        return ApplySemanticResources(surface, (Border.BorderBrushProperty, "HavenAccentSecondaryBrush"),
            (Border.BackgroundProperty, "HavenPanel2Brush"));
    }

    private Control BuildTask(string text, bool isChecked)
    {
        var checkbox = CreateTaskCheckbox();
        checkbox.IsChecked = isChecked; checkbox.IsEnabled = false;
        checkbox.VerticalAlignment = VerticalAlignment.Top;
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8,
            Children = { checkbox, WithColumn(BuildInlineText(text, 13, FontWeight.Normal), 1) }
        };
    }

    private static Control BuildListItem(string marker, string text) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("28,*"),
        ColumnSpacing = 4,
        Children =
        {
            ApplySemanticResources(new TextBlock { Text = marker, HorizontalAlignment = HorizontalAlignment.Right },
                (TextBlock.ForegroundProperty, "HavenAccentSecondaryBrush")),
            WithColumn(BuildInlineText(text, 13, FontWeight.Normal), 1)
        }
    };

    private Border BuildCodeBlock(string language, string code)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        header.Children.Add(ApplySemanticResources(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "code" : language,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        }, (TextBlock.ForegroundProperty, "HavenMutedBrush")));
        header.Children.Add(ActionButton("Copy", 1, CuiMarkdownCodeAction.Copy, language, code));
        header.Children.Add(ActionButton("Ask to run", 2, CuiMarkdownCodeAction.AskToRun, language, code));
        header.Children.Add(ActionButton("Ask to apply", 3, CuiMarkdownCodeAction.AskToApply, language, code));
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(header);
        var codeBox = CreateCodeInput();
        codeBox.Text = code; codeBox.IsReadOnly = true; codeBox.AcceptsReturn = true;
        codeBox.TextWrapping = TextWrapping.NoWrap;
        codeBox.FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace");
        codeBox.FontSize = 12; codeBox.MinHeight = 42; codeBox.MaxHeight = 500;
        ScrollViewer.SetHorizontalScrollBarVisibility(
            codeBox,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(
            codeBox,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        stack.Children.Add(codeBox);
        var surface = CreateSurface();
        surface.BorderThickness = new Thickness(1); surface.CornerRadius = new CornerRadius(12);
        surface.Padding = new Thickness(10); surface.Child = stack;
        return ApplySemanticResources(surface, (Border.BackgroundProperty, "HavenPanel3Brush"),
            (Border.BorderBrushProperty, "HavenLineStrongBrush"));
    }

    private Button ActionButton(string label, int column, CuiMarkdownCodeAction action, string language, string code)
    {
        var button = CreateCodeActionButton();
        button.Content = label; button.FontSize = 10; button.Padding = new Thickness(8, 4);
        Grid.SetColumn(button, column);
        var request = new CuiMarkdownCodeActionRequest(this, _renderGeneration, action, language, code);
        button.Click += (_, _) =>
        {
            if (IsCurrentOriginalCodeAction(request)) CodeActionRequested?.Invoke(request);
        };
        return button;
    }

    private Control BuildEquation(string latex)
    {
        var display = FormatLatex(latex);
        var surface = CreateSurface();
        surface.CornerRadius = new CornerRadius(10); surface.Padding = new Thickness(12, 9);
        surface.Child = new SelectableTextBlock
        {
            Text = display,
            FontFamily = new FontFamily("Cambria Math, STIX Two Math, serif"),
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        return ApplySemanticResources(surface, (Border.BackgroundProperty, "HavenPanel2Brush"));
    }

    private bool TryBuildTable(string[] lines, ref int index, out Control table)
    {
        table = null!;
        if (index + 1 >= lines.Length || !lines[index].Contains('|', StringComparison.Ordinal)) return false;
        var separatorCells = SplitTableRow(lines[index + 1]);
        if (separatorCells.Count == 0 || separatorCells.Any(cell => !Regex.IsMatch(cell.Trim(), "^:?-{3,}:?$", RegexOptions.CultureInvariant))) return false;
        var rows = new List<IReadOnlyList<string>> { SplitTableRow(lines[index]) };
        index += 2;
        while (index < lines.Length && lines[index].Contains('|', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(lines[index]))
        {
            rows.Add(SplitTableRow(lines[index]));
            index++;
        }
        index--;
        var columns = Math.Max(1, rows.Max(row => row.Count));
        var grid = ApplySemanticResources(new Grid { ColumnSpacing = 1, RowSpacing = 1 },
            (Panel.BackgroundProperty, "HavenLineStrongBrush"));
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        for (var row = 0; row < rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var column = 0; column < columns; column++)
            {
                var text = column < rows[row].Count ? rows[row][column].Trim() : string.Empty;
                var cell = CreateSurface();
                cell.Padding = new Thickness(8, 6);
                cell.Child = BuildInlineText(text, 12, row == 0 ? FontWeight.SemiBold : FontWeight.Normal);
                ApplySemanticResources(cell, (Border.BackgroundProperty, row == 0 ? "HavenPanel3Brush" : "HavenPanel2Brush"));
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }
        table = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid
        };
        return true;
    }

    private static IReadOnlyList<string> SplitTableRow(string line) => line.Trim().Trim('|').Split('|').Select(value => value.Trim()).ToArray();

    private static TextBlock BuildInlineText(string text, double size, FontWeight weight, FontStyle style = FontStyle.Normal)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight, FontStyle = style, TextWrapping = TextWrapping.Wrap };
        // Keep ordinary text in Text rather than manufacturing a single Run.
        // This gives accessibility APIs, copying, and headless rendering the
        // actual content while reserving Inlines for genuinely mixed styling.
        if (!InlinePattern.IsMatch(text))
        {
            block.Text = text;
            return block;
        }
        var position = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > position) block.Inlines!.Add(new Run(text[position..match.Index]));
            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
                block.Inlines!.Add(new Run(token[2..^2]) { FontWeight = FontWeight.SemiBold });
            else if (token.StartsWith('*') && token.EndsWith('*'))
                block.Inlines!.Add(new Run(token[1..^1]) { FontStyle = FontStyle.Italic });
            else if (token.StartsWith('`') && token.EndsWith('`'))
                block.Inlines!.Add(ApplySemanticResources(new Run(token[1..^1]) { FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace") },
                    (TextElement.BackgroundProperty, "HavenPanel3Brush")));
            else if (token.StartsWith('$') && token.EndsWith('$'))
                block.Inlines!.Add(new Run(FormatLatex(token[1..^1])) { FontFamily = new FontFamily("Cambria Math, STIX Two Math, serif") });
            else
            {
                var close = token.IndexOf("](", StringComparison.Ordinal);
                var label = token[1..close];
                var url = token[(close + 2)..^1];
                block.Inlines!.Add(ApplySemanticResources(new Run(label) { TextDecorations = TextDecorations.Underline },
                    (TextElement.ForegroundProperty, "HavenAccentSecondaryBrush")));
                block.Inlines!.Add(ApplySemanticResources(new Run(" (" + url + ")"), (TextElement.ForegroundProperty, "HavenMutedBrush")));
            }
            position = match.Index + match.Length;
        }
        if (position < text.Length) block.Inlines!.Add(new Run(text[position..]));
        return block;
    }

    private static string FormatLatex(string latex)
    {
        var value = latex.Trim();
        value = Regex.Replace(value, "\\\\frac\\{([^{}]+)\\}\\{([^{}]+)\\}", "$1⁄$2", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\\\sqrt\\{([^{}]+)\\}", "√($1)", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "\\^\\{([^{}]+)\\}", "^($1)", RegexOptions.CultureInvariant);
        value = Regex.Replace(value, "_\\{([^{}]+)\\}", "_($1)", RegexOptions.CultureInvariant);
        return value
            .Replace("\\times", "×", StringComparison.Ordinal)
            .Replace("\\cdot", "·", StringComparison.Ordinal)
            .Replace("\\leq", "≤", StringComparison.Ordinal)
            .Replace("\\geq", "≥", StringComparison.Ordinal)
            .Replace("\\neq", "≠", StringComparison.Ordinal)
            .Replace("\\rightarrow", "→", StringComparison.Ordinal)
            .Replace("\\leftarrow", "←", StringComparison.Ordinal)
            .Replace("\\infty", "∞", StringComparison.Ordinal)
            .Replace("\\sum", "∑", StringComparison.Ordinal)
            .Replace("\\int", "∫", StringComparison.Ordinal)
            .Replace("\\alpha", "α", StringComparison.Ordinal)
            .Replace("\\beta", "β", StringComparison.Ordinal)
            .Replace("\\gamma", "γ", StringComparison.Ordinal)
            .Replace("\\delta", "δ", StringComparison.Ordinal)
            .Replace("\\theta", "θ", StringComparison.Ordinal)
            .Replace("\\lambda", "λ", StringComparison.Ordinal)
            .Replace("\\pi", "π", StringComparison.Ordinal)
            .Replace("\\sigma", "σ", StringComparison.Ordinal)
            .Replace("\\omega", "ω", StringComparison.Ordinal);
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T ApplySemanticResources<T>(T target, params (AvaloniaProperty Property, string Key)[] resources) where T : StyledElement
    {
        // Follow the actual logical theme scope and subsequent theme changes.
        // No application-global brush lookup or independent palette is used.
        foreach (var (property, key) in resources) target.Bind(property, target.GetResourceObservable(key));
        return target;
    }

    private sealed class TextObserver(CuiMarkdownView owner) : IObserver<string>
    {
        /// <summary>
        /// Handles the completed event raised by the UI or runtime.
        /// </summary>
        public void OnCompleted() { }
        /// <summary>
        /// Handles the error event raised by the UI or runtime.
        /// </summary>
        public void OnError(Exception error) { }
        /// <summary>
        /// Handles the next event raised by the UI or runtime.
        /// </summary>
        public void OnNext(string value) => owner.Rebuild(value);
    }
}
