using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public sealed partial class BoardsViewModel
{
    internal void ApplyFontFamilyAttributes(string family)
    {
        var runFamily = CurrentTextRange() is not null && string.IsNullOrEmpty(family) ? DefaultAttributeFont() : family;
        TryApplyTextAttributes(run => run.FontFamily = runFamily, block => block.FontFamily = family);
    }

    internal void ApplyFontSizeAttributes(double size) =>
        TryApplyTextAttributes(run => run.FontSize = size, block => block.FontSize = size);

    internal void ApplyColorAttributes(string foreground, string background)
    {
        var selected = CurrentTextRange() is not null;
        var runForeground = selected && string.IsNullOrEmpty(foreground) ? DefaultAttributeForeground() : foreground;
        var runBackground = selected && string.IsNullOrEmpty(background) ? "#00000000" : background;
        TryApplyTextAttributes(run => { run.Foreground = runForeground; run.Background = runBackground; },
            block => { block.Foreground = foreground; block.Background = background; });
    }

    internal void ApplyForegroundAttributes(string foreground)
    {
        var runForeground = CurrentTextRange() is not null && string.IsNullOrEmpty(foreground) ? DefaultAttributeForeground() : foreground;
        TryApplyTextAttributes(run => run.Foreground = runForeground, block => block.Foreground = foreground);
    }

    internal void ApplyBackgroundAttributes(string background)
    {
        // An empty run override inherits the native input's background. In a uniformly
        // highlighted paragraph that is precisely the colour the user is clearing.
        var runBackground = CurrentTextRange() is not null && string.IsNullOrEmpty(background) ? "#00000000" : background;
        TryApplyTextAttributes(run => run.Background = runBackground, block => block.Background = background);
    }

    internal void ApplyBaselineAttributes(string baseline)
    {
        var desired = baseline == "subscript" ? HavenRichBaseline.Subscript : HavenRichBaseline.Superscript;
        var block = SelectedBlockOrNull();
        if (block is null) return;
        var selection = CurrentTextRange();
        var runs = selection is null ? EffectiveAttributeRuns(block) :
            BoardTextRanges.Selected(block, Math.Min(selection.Anchor, selection.Caret), Math.Max(selection.Anchor, selection.Caret));
        var next = runs.Count > 0 && runs.All(run => run.Baseline == desired) ? HavenRichBaseline.Normal : desired;
        TryApplyTextAttributes(run => run.Baseline = next, current => current.Baseline = next.ToString().ToLowerInvariant());
    }

    internal void ScaleFontAttributes(double delta)
    {
        if (!double.IsFinite(delta)) return;
        var block = SelectedBlockOrNull();
        if (block is null) return;
        var style = StyleList.FirstOrDefault(style => style.Id == block.StyleId);
        var inherited = block.FontSize > 0 ? block.FontSize : style?.FontSize > 0 ? style.FontSize : 14;
        var explicitRuns = block.Runs.Count > 0;
        TryApplyTextAttributes(run => run.FontSize = Math.Clamp((run.FontSize > 0 ? run.FontSize : inherited) + delta, 8, 96),
            current =>
            {
                // Changing the block default tells the canonical merge to apply
                // that single size to every run. Existing runs already carry
                // their individually scaled sizes, so retain their default.
                if (!explicitRuns) current.FontSize = Math.Clamp(inherited + delta, 8, 96);
            });
    }

    internal void ClearTextAttributes()
    {
        var normal = StyleList.FirstOrDefault(style => style.Id == "normal");
        var selection = CurrentTextRange();
        // An explicit selected run must override its block's font/colour defaults too.
        // Whole-block clearing can use the ordinary empty/zero inheritance values.
        var partial = selection is not null;
        TryApplyTextAttributes(run =>
        {
            run.Bold = run.Italic = run.Underline = run.StrikeThrough = false;
            run.Baseline = HavenRichBaseline.Normal;
            run.FontFamily = partial ? DefaultAttributeFont() : string.Empty;
            run.FontSize = partial ? normal?.FontSize > 0 ? normal.FontSize : 14 : 0;
            run.Foreground = partial ? DefaultAttributeForeground() : string.Empty;
            run.Background = partial ? !string.IsNullOrEmpty(normal?.Background) ? normal.Background : "#00000000" : string.Empty;
        }, block =>
        {
            block.Bold = block.Italic = block.Underline = block.Strike = false;
            block.Baseline = "normal";
            block.FontFamily = block.Foreground = block.Background = string.Empty;
            block.FontSize = 0;
        });
    }

    private string DefaultAttributeFont()
    {
        var normal = StyleList.FirstOrDefault(style => style.Id == "normal");
        if (!string.IsNullOrEmpty(normal?.FontFamily)) return normal.FontFamily;
        var surface = (Control?)_textRangeControl ?? _attachedRoot ?? _themeSurface;
        return surface is not null && surface.TryFindResource("CuiInterfaceFontFamily", out var resource) && resource is FontFamily family
            ? family.Name : FontFamily.Default.Name;
    }

    private string DefaultAttributeForeground()
    {
        var normal = StyleList.FirstOrDefault(style => style.Id == "normal");
        return !string.IsNullOrEmpty(normal?.Foreground) ? normal.Foreground : BoardsTheme.ContrastText(null).ToString() ?? string.Empty;
    }

    private void TryApplyTextAttributes(Action<HavenRichTextRun> change, Action<RichBoardBlock> changeBlock)
    {
        if (!RequireEditMode() || _disposed || _textRangeDetached || _textRangeBusy) return;
        var block = SelectedBlockOrNull();
        if (block is null || !BoardTextRanges.Matches(block)) return;
        var control = _textRangeControl;
        var selection = CurrentTextRange();
        if (control is not null && (control.IsReadOnly || !control.IsEffectivelyEnabled ||
            TopLevel.GetTopLevel(control) is null || _textRangePage != SelectedPageId || _textRangeBlock != block.Id)) return;
        if (control is not null && control.SelectionStart != control.SelectionEnd && selection is null) return;
        if (selection is not null && (selection.Text != block.Text ||
            _session is ContractSessionAdapter adapter && selection.Revision != adapter.DocumentVersion)) return;
        var top = control is null ? null : TopLevel.GetTopLevel(control);
        var previousFocus = top?.FocusManager?.GetFocusedElement() as Control;
        var restoreFocus = control?.IsKeyboardFocusWithin == true || IsAttributeToolbarControl(previousFocus);
        var epoch = Interlocked.Read(ref _textRangeEpoch);
        var runs = EffectiveAttributeRuns(block);
        if (selection is not null)
        {
            var start = Math.Min(selection.Anchor, selection.Caret);
            var end = Math.Max(selection.Anchor, selection.Caret);
            var (left, rest) = BoardTextRanges.SplitAt(runs, block.Text, start);
            var (middle, right) = BoardTextRanges.SplitAt(rest, block.Text[start..], end - start);
            foreach (var run in middle) change(run);
            runs = [.. left, .. middle, .. right];
        }
        else
        {
            foreach (var run in runs) change(run);
        }
        // No asynchronous gap: the exact current selection and revision above own
        // this projection edit. MarkDirty merges it into the existing canonical owner.
        if (_textRangeDetached || Interlocked.Read(ref _textRangeEpoch) != epoch) return;
        block.Runs = runs;
        if (selection is null) changeBlock(block);
        _session.MarkDirty();
        PushDocumentToBindings();
        if (restoreFocus && control is not null && TopLevel.GetTopLevel(control) is { } currentTop)
        {
            var focused = currentTop.FocusManager?.GetFocusedElement() as Control;
            // Binding updates may replace the toolbar control which invoked the action.
            // Restore its captured editor, but retain an explicit new focus destination.
            if (focused is null || ReferenceEquals(focused, previousFocus) || control.IsKeyboardFocusWithin)
                control.Focus();
        }
        RefreshSharedToolbar();
        // Whole-block/default changes also own the native editor's base font and
        // caret metrics, including an empty paragraph with no runs to present.
        // Return toolbar focus first so a render captures the intended editor.
        if (selection is null) RequestRebuild();
    }

    private static bool IsAttributeToolbarControl(Control? control) => control is not null &&
        control.GetVisualAncestors().OfType<Control>()
            .Concat(control.GetLogicalAncestors().OfType<Control>()).Prepend(control)
            .Any(owner => AutomationProperties.GetAutomationId(owner) is "ToolbarHost" or "AdaptiveToolbar" ||
                owner.Name is "ToolbarHost" or "AdaptiveToolbar");

    private List<HavenRichTextRun> EffectiveAttributeRuns(RichBoardBlock block)
    {
        if (block.Runs.Count > 0) return BoardTextRanges.Copy(block.Runs);
        if (block.Text.Length == 0) return [];
        var style = StyleList.FirstOrDefault(style => style.Id == block.StyleId);
        return [new HavenRichTextRun
        {
            Text = block.Text, Bold = block.Bold || style?.Bold == true,
            Italic = block.Italic || style?.Italic == true, Underline = block.Underline || style?.Underline == true,
            StrikeThrough = block.Strike || style?.Strike == true,
            Baseline = (block.Baseline != "normal" ? block.Baseline : style?.Baseline) switch
            { "subscript" => HavenRichBaseline.Subscript, "superscript" => HavenRichBaseline.Superscript, _ => HavenRichBaseline.Normal },
            FontFamily = !string.IsNullOrEmpty(block.FontFamily) ? block.FontFamily : style?.FontFamily ?? string.Empty,
            FontSize = block.FontSize > 0 ? block.FontSize : style?.FontSize ?? 0,
            Foreground = !string.IsNullOrEmpty(block.Foreground) ? block.Foreground : style?.Foreground ?? string.Empty,
            Background = !string.IsNullOrEmpty(block.Background) ? block.Background : style?.Background ?? string.Empty
        }];
    }
}
