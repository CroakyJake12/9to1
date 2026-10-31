using CakeOS.Apps.Boards.Contract;
using Avalonia;
using Avalonia.Controls;
using System.Globalization;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
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

    internal void SetAdaptiveSpacing(double spacing)
    {
        if (!RequireEditMode() || !double.IsFinite(spacing) || spacing is < 0 or > 10) return;
        var block = FindBlock(SelectedBlockId);
        if (block is null || Math.Abs(block.LineSpacing - spacing) < 0.001) return;
        block.LineSpacing = spacing;
        CommitViewEdit();
    }

    internal void ChangeAdaptiveCase(string mode)
    {
        if (!RequireEditMode()) return;
        var block = FindBlock(SelectedBlockId);
        if (block is null || block.Kind is not ("paragraph" or "heading" or "bulleted" or "numbered" or "checklist")) return;
        var selection = CurrentTextRange();
        if (_textRangeControl is not null && selection is null) return;
        var start = selection is null || selection.Anchor == selection.Caret ? 0 : Math.Min(selection.Anchor, selection.Caret);
        var end = selection is null || selection.Anchor == selection.Caret ? block.Text.Length : Math.Max(selection.Anchor, selection.Caret);
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
