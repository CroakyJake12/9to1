using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentTextEditTarget { None = 0, SlideTitle = 1, Element = 2 }

internal sealed partial class PresentSlideCanvas
{
    private PresentTextEditTarget _textEditTarget;
    private Guid? _textEditElementId;
    private string _editingText = string.Empty;
    private string _editingOriginal = string.Empty;
    private int _textCaret;
    private int? _textSelectionAnchor;
    private bool _textPointerSelecting;

    public event Action<string>? TitleTextPreviewRequested;
    public event Action<Guid, string>? ElementTextPreviewRequested;
    public event EventHandler? TextEditCommitRequested;
    public event EventHandler? TextEditCancelRequested;

    public bool IsTextEditing => _textEditTarget != PresentTextEditTarget.None;
    internal object TextEditContext => (_textEditTarget, _textEditElementId, _editingText, _textCaret, _textSelectionAnchor);
    public string SelectedText
    {
        get
        {
            if (!TrySelection(out var start, out var end)) return string.Empty;
            return _editingText[start..end];
        }
    }

    private HavenRect TitleRect(HavenRect slideRect) => new(
        slideRect.X + slideRect.Width * .07,
        slideRect.Y + slideRect.Height * .06,
        slideRect.Width * .86,
        slideRect.Height * .14);

    private bool TryBeginTitleEdit(HavenPoint local, HavenInputModifiers modifiers)
    {
        if (_slide is null || _slide.GetTitleText() is not null || string.IsNullOrEmpty(_slide.Title)) return false;
        var rect = TitleRect(SlideRectLocal());
        if (!rect.Contains(local)) return false;
        BeginTextEdit(PresentTextEditTarget.SlideTitle, null, _slide.Title, rect, local, modifiers);
        SelectionSetRequested?.Invoke(Array.Empty<Guid>());
        return true;
    }

    private void BeginElementTextEdit(PresentElement element, HavenPoint local, HavenInputModifiers modifiers)
    {
        var rect = ElementRect(element, SlideRectLocal(), false);
        var point = InverseRotatePoint(local, rect, element.RotationDegrees);
        BeginTextEdit(PresentTextEditTarget.Element, element.Id, element.Text, rect, point, modifiers);
    }

    private void BeginTextEdit(PresentTextEditTarget target, Guid? elementId, string? text, HavenRect rect, HavenPoint local, HavenInputModifiers modifiers)
    {
        _textEditTarget = target;
        _textEditElementId = elementId;
        _editingText = text ?? string.Empty;
        _editingOriginal = _editingText;
        _textCaret = CaretFromPoint(rect, local);
        _textSelectionAnchor = modifiers.Shift ? 0 : null;
        _textPointerSelecting = true;
        ResetDirectGesture();
        InvalidateInteraction();
    }

    private bool TryHandleTextPointerPress(HavenPointerInput input)
    {
        if (!IsTextEditing || !TryEditingRect(out var rect, out var rotation)) return false;
        var point = rotation == 0 ? input.LocalPosition : InverseRotatePoint(input.LocalPosition, rect, rotation);
        if (!rect.Contains(point)) return false;
        var next = CaretFromPoint(rect, point);
        if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) _textSelectionAnchor = next;
        else _textSelectionAnchor ??= _textCaret;
        _textCaret = next;
        _textPointerSelecting = true;
        InvalidateInteraction();
        return true;
    }

    private bool MoveTextPointer(HavenPointerInput input)
    {
        if (!_textPointerSelecting || !IsTextEditing || !TryEditingRect(out var rect, out var rotation)) return false;
        var point = rotation == 0 ? input.LocalPosition : InverseRotatePoint(input.LocalPosition, rect, rotation);
        _textSelectionAnchor ??= _textCaret;
        _textCaret = CaretFromPoint(rect, point);
        InvalidateInteraction();
        return true;
    }

    private bool ReleaseTextPointer()
    {
        if (!_textPointerSelecting) return false;
        _textPointerSelecting = false;
        if (_textSelectionAnchor == _textCaret) _textSelectionAnchor = null;
        InvalidateInteraction();
        return true;
    }

    public bool TextInput(string? text) => InsertText(text);

    bool IHavenKeyboardInputTarget.KeyDown(HavenKeyInput input) => KeyDown(input.Key, ToInputModifiers(input));

    bool IHavenKeyboardInputTarget.KeyUp(HavenKeyInput input) => KeyUp(input.Key);

    public string? Copy() => string.IsNullOrEmpty(SelectedText) ? null : SelectedText;

    public string? Cut()
    {
        var selected = Copy();
        if (selected is not null) DeleteSelection();
        return selected;
    }

    public bool Paste(string? text) => InsertText(text);

    public bool InsertText(string? text)
    {
        if (!IsTextEditing || string.IsNullOrEmpty(text)) return false;
        var replacement = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        DeleteSelectionCore();
        _editingText = _editingText.Insert(_textCaret, replacement);
        _textCaret += replacement.Length;
        _textSelectionAnchor = null;
        PreviewText();
        return true;
    }

    public bool DeleteSelection()
    {
        if (!IsTextEditing || !TrySelection(out _, out _)) return false;
        DeleteSelectionCore();
        PreviewText();
        return true;
    }

    public bool KeyDown(HavenKey key, HavenInputModifiers modifiers)
    {
        if (key == HavenKey.Escape && (HasDirectGesture || _insertionPreset is not null)) { CancelInteraction(); return true; }
        if (!IsTextEditing)
        {
            if (key == HavenKey.Escape && _selectedIds.Count > 0) { SelectionSetRequested?.Invoke(Array.Empty<Guid>()); return true; }
            if (key == HavenKey.Enter && _slide is not null && _selectedIds.Count == 1
                && _slide.Elements.FirstOrDefault(element => _selectedIds.Contains(element.Id)) is { Kind: PresentElementKind.Text or PresentElementKind.Shape, Locked: false } text)
            {
                BeginElementTextEdit(text, Center(ElementRect(text, SlideRectLocal(), false)), default);
                return true;
            }
            return false;
        }

        if (modifiers.Control && key == HavenKey.A)
        {
            _textSelectionAnchor = 0; _textCaret = _editingText.Length; InvalidateInteraction(); return true;
        }
        if (key == HavenKey.Escape) { CommitTextEdit(); return true; }
        if (modifiers.Control && key == HavenKey.Enter) { CommitTextEdit(); return true; }
        if (key == HavenKey.Enter && _textEditTarget == PresentTextEditTarget.SlideTitle) { CommitTextEdit(); return true; }
        switch (key)
        {
            case HavenKey.Left: MoveCaret(-1, modifiers.Shift); return true;
            case HavenKey.Right: MoveCaret(1, modifiers.Shift); return true;
            case HavenKey.Home: SetCaret(0, modifiers.Shift); return true;
            case HavenKey.End: SetCaret(_editingText.Length, modifiers.Shift); return true;
            case HavenKey.Backspace:
                if (DeleteSelection()) return true;
                if (_textCaret <= 0) return true;
                _editingText = _editingText.Remove(--_textCaret, 1); PreviewText(); return true;
            case HavenKey.Delete:
                if (DeleteSelection()) return true;
                if (_textCaret >= _editingText.Length) return true;
                _editingText = _editingText.Remove(_textCaret, 1); PreviewText(); return true;
            case HavenKey.Enter: return InsertText("\n");
            default: return false;
        }
    }

    public bool KeyUp(HavenKey key) => IsTextEditing && key is HavenKey.Left or HavenKey.Right or HavenKey.Home or HavenKey.End or HavenKey.Backspace or HavenKey.Delete or HavenKey.Enter or HavenKey.Escape or HavenKey.A;

    private static HavenInputModifiers ToInputModifiers(HavenKeyInput input) => new(
        Shift: input.Shift,
        Control: input.Control,
        Alt: input.Alt,
        Meta: input.Meta);

    private static HavenInputModifiers ToInputModifiers(HavenKeyModifiers modifiers) => new(
        Shift: modifiers.HasFlag(HavenKeyModifiers.Shift),
        Control: modifiers.HasFlag(HavenKeyModifiers.Control),
        Alt: modifiers.HasFlag(HavenKeyModifiers.Alt),
        Meta: modifiers.HasFlag(HavenKeyModifiers.Meta));

    public void CommitTextEdit()
    {
        if (!IsTextEditing) return;
        var changed = !string.Equals(_editingOriginal, _editingText, StringComparison.Ordinal);
        ClearTextEditState();
        if (changed) TextEditCommitRequested?.Invoke(this, EventArgs.Empty);
        InvalidateInteraction();
    }

    public void CancelTextEdit()
    {
        if (!IsTextEditing) return;
        var changed = !string.Equals(_editingOriginal, _editingText, StringComparison.Ordinal);
        ClearTextEditState();
        if (changed) TextEditCancelRequested?.Invoke(this, EventArgs.Empty);
        InvalidateInteraction();
    }

    private void PreviewText()
    {
        if (_textEditTarget == PresentTextEditTarget.SlideTitle) TitleTextPreviewRequested?.Invoke(_editingText);
        else if (_textEditTarget == PresentTextEditTarget.Element && _textEditElementId is { } id) ElementTextPreviewRequested?.Invoke(id, _editingText);
        InvalidateInteraction();
    }

    private void ClearTextEditState()
    {
        _textEditTarget = PresentTextEditTarget.None;
        _textEditElementId = null;
        _editingText = string.Empty;
        _editingOriginal = string.Empty;
        _textCaret = 0;
        _textSelectionAnchor = null;
        _textPointerSelecting = false;
    }

    private void MoveCaret(int delta, bool extend) => SetCaret(Math.Clamp(_textCaret + delta, 0, _editingText.Length), extend);

    private void SetCaret(int value, bool extend)
    {
        var next = Math.Clamp(value, 0, _editingText.Length);
        if (extend) _textSelectionAnchor ??= _textCaret;
        else _textSelectionAnchor = null;
        _textCaret = next;
        if (_textSelectionAnchor == _textCaret) _textSelectionAnchor = null;
        InvalidateInteraction();
    }

    private bool TrySelection(out int start, out int end)
    {
        if (_textSelectionAnchor is not { } anchor || anchor == _textCaret) { start = end = _textCaret; return false; }
        start = Math.Min(anchor, _textCaret); end = Math.Max(anchor, _textCaret); return true;
    }

    private void DeleteSelectionCore()
    {
        if (!TrySelection(out var start, out var end)) return;
        _editingText = _editingText.Remove(start, end - start);
        _textCaret = start; _textSelectionAnchor = null;
    }

    private bool TryEditingRect(out HavenRect rect, out double rotation)
    {
        rotation = 0;
        if (_textEditTarget == PresentTextEditTarget.SlideTitle) { rect = TitleRect(SlideRectLocal()); return true; }
        if (_textEditTarget == PresentTextEditTarget.Element && _slide?.Elements.FirstOrDefault(element => element.Id == _textEditElementId) is { } element)
        {
            rect = ElementRect(element, SlideRectLocal(), false); rotation = element.RotationDegrees; return true;
        }
        rect = default; return false;
    }

    private readonly Dictionary<ParagraphKey, HavenParagraphLayout> _textLayouts = new();
    private sealed record ParagraphKey(string Text, string Family, double Size, int Weight, bool Italic,
        bool Underline, HavenBrush Color, double Width, HavenParagraphAlignment Alignment);

    private HavenTextLayout TextDescriptor(HavenRect rect, string text, string family, double fontSize,
        int weight, HavenBrush color, bool italic = false, bool underline = false,
        PresentTextHorizontalAlignment horizontal = PresentTextHorizontalAlignment.Left,
        PresentTextVerticalAlignment vertical = PresentTextVerticalAlignment.Top)
    {
        var alignment = horizontal switch
        {
            PresentTextHorizontalAlignment.Center => HavenParagraphAlignment.Center,
            PresentTextHorizontalAlignment.Right => HavenParagraphAlignment.Right,
            PresentTextHorizontalAlignment.Justify => HavenParagraphAlignment.Justify,
            _ => HavenParagraphAlignment.Left
        };
        var key = new ParagraphKey(text, family, fontSize, weight, italic, underline, color, Math.Max(1, rect.Width), alignment);
        if (!_textLayouts.TryGetValue(key, out var paragraph))
        {
            // Reuse shaped paragraphs across paints without retaining old documents indefinitely.
            if (_textLayouts.Count >= 128) _textLayouts.Clear();
            paragraph = HavenSceneControl.ShapeParagraph(
                [new HavenParagraphRun(text, family, fontSize, weight, italic, color, Underline: underline)], key.Width, alignment: alignment);
            _textLayouts.Add(key, paragraph);
        }
        var freeHeight = Math.Max(0, rect.Height - paragraph.Size.Height);
        var y = rect.Y + (vertical == PresentTextVerticalAlignment.Middle ? freeHeight / 2 : vertical == PresentTextVerticalAlignment.Bottom ? freeHeight : 0);
        return new HavenTextLayout(text, family, fontSize, weight, rect.Width, false, italic)
        {
            Paragraph = paragraph, ParagraphOrigin = new HavenPoint(rect.X, y), ParagraphLength = text.Length
        };
    }

    private HavenTextLayout ElementTextDescriptor(PresentElement element, HavenRect rect, string text, double scale = 1) =>
        TextDescriptor(rect, text,
            string.IsNullOrWhiteSpace(element.TextStyle.FontFamily) ? _document?.Theme.BodyFontFamily ?? "Segoe UI" : element.TextStyle.FontFamily,
            element.TextStyle.FontSizePoints * 96d / 72d * scale, element.TextStyle.Bold ? 700 : 400,
            Brush(element.TextStyle.Color, _document?.Theme.Colors.Foreground ?? "#1F1F1F"),
            element.TextStyle.Italic, element.TextStyle.Underline, element.TextStyle.HorizontalAlignment, element.TextStyle.VerticalAlignment);

    private int CaretFromPoint(HavenRect rect, HavenPoint point)
    {
        var documentHeight = (_document?.SlideSize.HeightInches ?? 7.5) * 96;
        var scale = SlideRectLocal().Height / documentHeight;
        var element = _slide?.Elements.FirstOrDefault(value => value.Id == _textEditElementId);
        var layout = _textEditTarget == PresentTextEditTarget.Element && element is not null
            ? ElementTextDescriptor(element, rect, _editingText, scale)
            : TextDescriptor(rect, _editingText, _document?.Theme.HeadingFontFamily ?? "Segoe UI",
                Math.Clamp(documentHeight * .14 * .34, 18, 34) * scale, 700,
                Brush(_document?.Theme.Colors.Foreground, "#1F1F1F"), vertical: PresentTextVerticalAlignment.Middle);
        return layout.Paragraph!.HitTest(new HavenPoint(point.X - layout.ParagraphOrigin.X, point.Y - layout.ParagraphOrigin.Y));
    }

    private void DrawSlideTitle(HavenDrawingContext context, HavenRect slideRect, double opacity)
    {
        if (_slide is null || _slide.GetTitleText() is not null || string.IsNullOrEmpty(_slide.Title)) return;
        var rect = TitleRect(slideRect);
        var text = _textEditTarget == PresentTextEditTarget.SlideTitle ? _editingText : _slide.Title;
        var brush = Brush(_document?.Theme.Colors.Foreground, "#1F1F1F");
        var layout = TextDescriptor(rect, text, _document?.Theme.HeadingFontFamily ?? "Segoe UI",
            Math.Clamp(rect.Height * .34, 18, 34), 700, brush, vertical: PresentTextVerticalAlignment.Middle);
        DrawEditableText(context, rect, layout, brush, opacity, _textEditTarget == PresentTextEditTarget.SlideTitle);
    }

    private void DrawEditableElementText(HavenDrawingContext context, PresentElement element, HavenRect rect, double opacity)
    {
        var editing = _textEditTarget == PresentTextEditTarget.Element && _textEditElementId == element.Id;
        var text = editing ? _editingText : element.Text;
        if (string.IsNullOrWhiteSpace(text) && !editing) return;
        var layout = ElementTextDescriptor(element, rect, text);
        DrawEditableText(context, rect, layout, layout.Paragraph!.Runs[0].Foreground, opacity * element.Opacity, editing);
    }

    private void DrawEditableText(HavenDrawingContext context, HavenRect rect, HavenTextLayout layout, HavenBrush brush, double opacity, bool editing)
    {
        context.Add(new HavenPushClipCommand(rect));
        context.Add(new HavenTextCommand(rect, layout, brush, opacity));
        if (editing)
        {
            if (TrySelection(out var start, out var end))
                context.Add(new HavenTextSelectionCommand(rect, layout, start, end - start, new HavenSolidBrush(88, 56, 132, 255), opacity));
            context.Add(new HavenCaretCommand(rect, layout, new HavenTokenBrush("Accent"), opacity) { FullLayout = layout, CaretIndex = _textCaret });
        }
        context.Add(new HavenPopClipCommand(rect));
    }
}
