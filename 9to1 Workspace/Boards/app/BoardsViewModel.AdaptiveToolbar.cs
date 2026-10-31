using CakeOS.Apps.Boards.Contract;
using Avalonia;
using Avalonia.Controls;
using System.Globalization;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    // One selection contract drives both the authored controls and their command
    // guards. List text currently has whole-row attributes, not text runs.
    internal readonly record struct ToolbarCapabilities(bool Editable, bool Text, bool ListRow,
        bool Typography, bool ParagraphStyle, bool ParagraphLayout, bool ListConversion, bool Properties);

    internal ToolbarCapabilities AdaptiveCapabilities()
    {
        var block = FindBlock(SelectedBlockId);
        var editable = CanEdit && IsEditMode;
        var row = block?.Kind is "bulleted" or "numbered" or "checklist";
        var paragraph = block?.Kind is "paragraph" or "heading";
        var text = !IsDrawMode && (row || paragraph);
        return new(editable, text, row, editable && text, editable && text && paragraph,
            editable && text && paragraph, editable && text && paragraph,
            editable && !IsDrawMode && (text || block?.Kind is "table" or "image" or "divider"));
    }

    internal void SetAdaptiveProperties(bool visible) =>
        Set("AdaptivePropertiesVisible", visible);

    private BoardsViewModel? _adaptiveContextTarget;
    internal void PublishAdaptiveContext(BoardsViewModel target)
    {
        if (ReferenceEquals(target, this))
        {
            if (_adaptiveContextTarget is not null) { _adaptiveContextTarget = null; RefreshCuiContext(); }
            return;
        }
        _adaptiveContextTarget = target;
        foreach (var pair in target._properties.Where(pair => pair.Key.StartsWith("Context", StringComparison.Ordinal)).ToArray())
            Set(pair.Key, pair.Value);
    }

    internal (string Font, string Size) AdaptiveFontValues()
    {
        var block = FindBlock(SelectedBlockId);
        var style = StyleList.FirstOrDefault(item => item.Id == block?.StyleId);
        var family = !string.IsNullOrWhiteSpace(block?.FontFamily) ? block.FontFamily
            : !string.IsNullOrWhiteSpace(style?.FontFamily) ? style.FontFamily : AdaptiveDefaultFont();
        var size = block?.FontSize > 0 ? block.FontSize : style?.FontSize > 0 ? style.FontSize : 14;
        var selection = CurrentTextRange();
        if (block is null || block.Runs.Count == 0)
            return (family, size.ToString("0.##", CultureInfo.InvariantCulture));
        var (start, end) = AdaptiveReadBounds(block, selection);
        var offset = 0;
        var families = new HashSet<string>(StringComparer.Ordinal);
        var sizes = new HashSet<double>();
        foreach (var run in block.Runs)
        {
            if (offset < end && offset + run.Text.Length > start)
            {
                families.Add(string.IsNullOrWhiteSpace(run.FontFamily) ? family : run.FontFamily);
                sizes.Add(run.FontSize > 0 ? run.FontSize : size);
            }
            offset += run.Text.Length;
        }
        return (families.Count > 1 ? "Mixed" : families.FirstOrDefault() ?? family,
            sizes.Count > 1 ? "Mixed" : (sizes.FirstOrDefault(size)).ToString("0.##", CultureInfo.InvariantCulture));
    }

    internal (string Foreground, string Background, bool ForegroundMixed, bool BackgroundMixed) AdaptiveColorValues()
    {
        var block = FindBlock(SelectedBlockId);
        var style = StyleList.FirstOrDefault(item => item.Id == block?.StyleId);
        var foreground = !string.IsNullOrEmpty(block?.Foreground) ? block.Foreground
            : !string.IsNullOrEmpty(style?.Foreground) ? style.Foreground : DefaultAttributeForeground();
        var background = !string.IsNullOrEmpty(block?.Background) ? block.Background
            : !string.IsNullOrEmpty(style?.Background) ? style.Background : "#00000000";
        var selection = CurrentTextRange();
        if (block is null || block.Runs.Count == 0) return (foreground, background, false, false);
        var (start, end) = AdaptiveReadBounds(block, selection);
        static string Normalized(string color) => Avalonia.Media.Color.TryParse(color, out var parsed) ? parsed.ToString() : color;
        var foregrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backgrounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        foreach (var run in block.Runs)
        {
            if (offset < end && offset + run.Text.Length > start)
            {
                foregrounds.Add(Normalized(string.IsNullOrEmpty(run.Foreground) ? foreground : run.Foreground));
                backgrounds.Add(Normalized(string.IsNullOrEmpty(run.Background) ? background : run.Background));
            }
            offset += run.Text.Length;
        }
        return (foregrounds.Count > 1 ? "#00000000" : foregrounds.FirstOrDefault() ?? foreground,
            backgrounds.Count > 1 ? "#00000000" : backgrounds.FirstOrDefault() ?? background,
            foregrounds.Count > 1, backgrounds.Count > 1);
    }

    private (int Start, int End) AdaptiveReadBounds(RichBoardBlock block, BoardTextSelection? selection)
    {
        if (selection is not null) return (Math.Min(selection.Anchor, selection.Caret), Math.Max(selection.Anchor, selection.Caret));
        var input = _textRangeControl;
        if (!_disposed && !EditorLifetimeEnded && !_textRangeDetached && input is not null &&
            !input.IsReadOnly && input.IsEffectivelyEnabled && TopLevel.GetTopLevel(input) is not null &&
            input.SelectionStart == input.SelectionEnd && string.Equals(input.Text ?? string.Empty, block.Text, StringComparison.Ordinal) &&
            _textRangePage == SelectedPageId && _textRangeBlock == SelectedBlockId && block.Id == SelectedBlockId)
        {
            var caret = Math.Clamp(input.CaretIndex, 0, block.Text.Length);
            var start = caret > 0 ? caret - 1 : 0;
            return (start, Math.Min(start + 1, block.Text.Length));
        }
        return (0, block.Text.Length);
    }

    private string AdaptiveDefaultFont()
    {
        if (_textRangeControl is { } editor) return editor.FontFamily.Name;
        var surface = _attachedRoot ?? _themeSurface;
        if (surface is not null && surface.TryFindResource("CuiInterfaceFontFamily", out var resource) &&
            resource is Avalonia.Media.FontFamily family) return family.Name;
        return Avalonia.Media.FontFamily.Default.Name;
    }

    internal (string Text, int Anchor, int Caret)? AdaptiveSelectionCoordinates()
    {
        var input = _textRangeControl;
        var block = FindBlock(SelectedBlockId);
        if (_disposed || EditorLifetimeEnded || _textRangeDetached || input is null || block is null ||
            input.IsReadOnly || !input.IsEffectivelyEnabled || TopLevel.GetTopLevel(input) is null ||
            _textRangePage != SelectedPageId || _textRangeBlock != SelectedBlockId ||
            !string.Equals(input.Text ?? string.Empty, block.Text, StringComparison.Ordinal)) return null;
        return (block.Text, input.SelectionStart, input.SelectionEnd);
    }

    internal void SetAdaptiveSpacing(double spacing)
    {
        if (!AdaptiveCapabilities().Typography || !double.IsFinite(spacing) || spacing is < 0 or > 10) return;
        var block = FindBlock(SelectedBlockId);
        if (block is null || Math.Abs(block.LineSpacing - spacing) < 0.001) return;
        RestoreAdaptiveEditorFocus(block);
        block.LineSpacing = spacing;
        CommitViewEdit();
    }

    internal void SetAdaptiveParagraphSpace(bool before, double space)
    {
        if (!AdaptiveCapabilities().ParagraphLayout || !double.IsFinite(space) || space is < 0 or > 1000) return;
        var block = FindBlock(SelectedBlockId);
        if (block is null || Math.Abs((before ? block.SpaceBefore : block.SpaceAfter) - space) < 0.001) return;
        RestoreAdaptiveEditorFocus(block);
        if (before) block.SpaceBefore = space; else block.SpaceAfter = space;
        CommitViewEdit();
    }

    private void RestoreAdaptiveEditorFocus(RichBoardBlock block)
    {
        var root = _attachedRoot ?? _themeSurface;
        if (root is null) return;
        var name = IsListKind(block.Kind) ? "chk_" + block.Id.Replace(':', '_') : "t_" + block.Id;
        var editor = FindControls<TextBox>(root).FirstOrDefault(input => input.Name == name);
        if (editor is null) return;
        var focused = TopLevel.GetTopLevel(editor)?.FocusManager?.GetFocusedElement() as Control;
        // Explicit focus in another editor or outside the toolbar belongs to
        // that control. Only a formatting command hands focus back to its text.
        if (IsAttributeToolbarControl(focused)) editor.Focus();
    }

    internal void ChangeAdaptiveCase(string mode)
    {
        if (!RequireEditMode()) return;
        var block = FindBlock(SelectedBlockId);
        if (block is null || block.Kind is not ("paragraph" or "heading" or "bulleted" or "numbered" or "checklist")) return;
        var selection = CurrentTextRange();
        if (_textRangeControl is not null && selection is null && !AdaptiveCapabilities().ListRow) return;
        var start = selection is null || selection.Anchor == selection.Caret ? 0 : Math.Min(selection.Anchor, selection.Caret);
        var end = selection is null || selection.Anchor == selection.Caret ? block.Text.Length : Math.Max(selection.Anchor, selection.Caret);
        RestoreAdaptiveEditorFocus(block);
        string Convert(string text) => mode == "upper" ? text.ToUpperInvariant() : text.ToLowerInvariant();
        if (block.Runs.Count == 0)
            block.Text = block.Text[..start] + Convert(block.Text[start..end]) + block.Text[end..];
        else
        {
            var offset = 0;
            foreach (var run in block.Runs)
            {
                var length = run.Text.Length;
                var a = Math.Clamp(start - offset, 0, length);
                var b = Math.Clamp(end - offset, 0, length);
                if (b > a) run.Text = run.Text[..a] + Convert(run.Text[a..b]) + run.Text[b..];
                offset += length;
            }
            block.Text = string.Concat(block.Runs.Select(run => run.Text));
        }
        CommitViewEdit();
    }
}
