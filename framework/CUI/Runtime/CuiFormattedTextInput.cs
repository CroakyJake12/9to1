using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace CakeOS.Cui.Runtime;

/// <summary>Immutable presentation of one contiguous UTF-16 text run. Null font
/// properties and zero size inherit the input's font; colours are immutable values.
/// This contains no document, selection, editing commands or undo history.</summary>
public sealed record CuiFormattedTextRun(
    string Text,
    FontWeight? FontWeight = null,
    FontStyle? FontStyle = null,
    string? FontFamily = null,
    double FontSize = 0,
    bool Underline = false,
    bool StrikeThrough = false,
    BaselineAlignment Baseline = BaselineAlignment.Baseline,
    Color? Foreground = null,
    Color? Background = null);

/// <summary>Owned, immutable run snapshot bound to EXACT text. The native input
/// ignores this snapshot whenever its current text differs (including between
/// independent text/presentation binding notifications).</summary>
public sealed class CuiFormattedText
{
    private readonly CuiFormattedTextRun[] _runs;
    private readonly int[] _starts;
    public string Text { get; }
    public IReadOnlyList<CuiFormattedTextRun> Runs { get; }

    public CuiFormattedText(string text, IReadOnlyList<CuiFormattedTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(runs);
        var count = runs.Count;
        // Every valid nonempty run consumes at least one input code unit.
        // Refuse an impossible count before allocating snapshot storage.
        if (count < 0 || count > text.Length)
            throw new ArgumentException("The run count exceeds the supplied text.", nameof(runs));
        _runs = new CuiFormattedTextRun[count];
        _starts = new int[count];
        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            var run = runs[i] ?? throw new ArgumentException("A formatted run cannot be null.", nameof(runs));
            if (run.Text is null || !double.IsFinite(run.FontSize) || run.FontSize < 0 ||
                !Enum.IsDefined(run.Baseline))
                throw new ArgumentException("A formatted run has invalid text, size or baseline.", nameof(runs));
            if (run.Text.Length == 0)
                throw new ArgumentException("Formatted runs must be nonempty; an empty input uses no runs.", nameof(runs));
            if (offset > text.Length - run.Text.Length ||
                !text.AsSpan(offset, run.Text.Length).SequenceEqual(run.Text.AsSpan()))
                throw new ArgumentException("Formatted runs must exactly partition the supplied text.", nameof(runs));
            _starts[i] = offset;
            offset += run.Text.Length;
            if (offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]))
                throw new ArgumentException("A formatted run cannot split a surrogate pair.", nameof(runs));
            _runs[i] = run;
        }
        if (offset != text.Length)
            throw new ArgumentException("Formatted runs must cover the supplied text.", nameof(runs));
        Text = text;
        Runs = Array.AsReadOnly(_runs);
    }

    internal CuiFormattedTextRun? RunAt(int offset)
    {
        if (_runs.Length == 0) return null;
        offset = Math.Clamp(offset, 0, Text.Length - 1);
        var index = Array.BinarySearch(_starts, offset);
        return _runs[index >= 0 ? index : ~index - 1];
    }
}

/// <summary>Typed one-way CUI Input FormattedText port. Native TextBox remains the
/// sole input/selection/IME owner; this changes only its actual shaping layout.</summary>
public sealed class CuiFormattedTextInput : AvaloniaObject
{
    public static readonly AttachedProperty<CuiFormattedText?> PresentationProperty =
        AvaloniaProperty.RegisterAttached<CuiFormattedTextInput, TextBox, CuiFormattedText?>("Presentation");

    public static CuiFormattedText? GetPresentation(TextBox input) => input.GetValue(PresentationProperty);
    public static void SetPresentation(TextBox input, CuiFormattedText? value) => input.SetValue(PresentationProperty, value);
}

/// <summary>CUI template backend. Uses the original presenter's layout for drawing,
/// pointer hit testing, caret movement, selection and IME. No second text layer.</summary>
public sealed class CuiFormattedTextPresenter : TextPresenter
{
    private bool _refreshCaret;
    public static readonly StyledProperty<CuiFormattedText?> PresentationProperty =
        AvaloniaProperty.Register<CuiFormattedTextPresenter, CuiFormattedText?>(nameof(Presentation));

    public CuiFormattedText? Presentation
    {
        get => GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PresentationProperty)
        {
            _refreshCaret = true;
            InvalidateTextLayout();
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        if (_refreshCaret || Presentation is not null)
        {
            _refreshCaret = false;
            // Reuse native affinity and composition position without assigning
            // CaretIndex/selection or resetting the input method's preedit text.
            RefreshCaretBounds();
        }
        return arranged;
    }

    protected override TextLayout CreateTextLayoutInternal(Size constraint, string? text, Typeface typeface,
        IReadOnlyList<ValueSpan<TextRunProperties>>? textStyleOverrides)
    {
        var presentation = Presentation;
        var original = Text ?? string.Empty;
        var preedit = PreeditText ?? string.Empty;
        var caret = Math.Clamp(CaretIndex, 0, original.Length);
        // Password rendering is always the stock path, even when revealed. Never
        // substitute a logical rich snapshot for the presenter's masked text.
        if (presentation is null || PasswordChar != default ||
            !string.Equals(original, presentation.Text, StringComparison.Ordinal) ||
            !string.Equals(text ?? string.Empty, original.Insert(caret, preedit), StringComparison.Ordinal))
            return base.CreateTextLayoutInternal(constraint, text, typeface, textStyleOverrides);

        // TextLayout accepts ordered, non-overlapping overrides. Split at both rich
        // run boundaries and native IME/selection boundaries, then merge properties
        // rather than letting the native foreground override erase bold/decorations.
        var boundaries = new SortedSet<int> { 0, original.Length + preedit.Length, caret, caret + preedit.Length };
        var offset = 0;
        foreach (var run in presentation.Runs)
        {
            offset += run.Text.Length;
            boundaries.Add(offset < caret ? offset : offset + preedit.Length);
        }
        var selectionStart = Math.Clamp(Math.Min(SelectionStart, SelectionEnd), 0, original.Length);
        var selectionEnd = Math.Clamp(Math.Max(SelectionStart, SelectionEnd), 0, original.Length);
        var selecting = preedit.Length == 0 && ShowSelectionHighlight && selectionStart < selectionEnd;
        if (selecting) { boundaries.Add(selectionStart); boundaries.Add(selectionEnd); }
        if (textStyleOverrides is not null)
            foreach (var overlay in textStyleOverrides)
            {
                boundaries.Add(Math.Clamp(overlay.Start, 0, original.Length + preedit.Length));
                boundaries.Add(Math.Clamp(overlay.Start + overlay.Length, 0, original.Length + preedit.Length));
            }
        var edges = boundaries.ToArray();
        var merged = new List<ValueSpan<TextRunProperties>>(Math.Max(0, edges.Length - 1));
        for (var i = 0; i + 1 < edges.Length; i++)
        {
            var start = edges[i];
            var length = edges[i + 1] - start;
            if (length <= 0) continue;
            var composing = preedit.Length > 0 && start >= caret && start < caret + preedit.Length;
            // Composition inherits the run immediately preceding its insertion,
            // or the first run at offset zero; no persisted text/run is mutated.
            var sourceOffset = composing ? Math.Max(0, caret - 1) : start >= caret + preedit.Length ? start - preedit.Length : start;
            var run = presentation.RunAt(sourceOffset);
            var family = run?.FontFamily;
            var face = new Typeface(string.IsNullOrEmpty(family) ? typeface.FontFamily : new FontFamily(family),
                run?.FontStyle ?? typeface.Style, run?.FontWeight ?? typeface.Weight, typeface.Stretch);
            IBrush? foreground = run?.Foreground is { } colour ? new ImmutableSolidColorBrush(colour) : Foreground;
            IBrush? background = run?.Background is { } highlight ? new ImmutableSolidColorBrush(highlight) : null;
            // Stock TextPresenter paints selection BEFORE the glyph layout. A
            // selected run's own highlight must not cover that native selection.
            if (selecting && SelectionBrush is not null && start >= selectionStart && start < selectionEnd) background = null;
            var decorations = new TextDecorationCollection();
            if (run?.Underline == true || composing) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (run?.StrikeThrough == true) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            if (!composing && textStyleOverrides is not null)
                foreach (var overlay in textStyleOverrides)
                    if (start >= overlay.Start && start < overlay.Start + overlay.Length)
                        foreground = overlay.Value.ForegroundBrush ?? foreground;
            merged.Add(new ValueSpan<TextRunProperties>(start, length, new GenericTextRunProperties(face,
                run is { FontSize: > 0 } ? run.FontSize : FontSize, decorations.Count > 0 ? decorations : null,
                foreground, background, run?.Baseline ?? BaselineAlignment.Baseline, fontFeatures: FontFeatures)));
        }
        return base.CreateTextLayoutInternal(constraint, text, typeface, merged);
    }
}
