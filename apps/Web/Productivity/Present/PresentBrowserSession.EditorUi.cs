using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using CakeOS.Cui;

namespace NineToOne.Web.Productivity.Present;

public sealed partial class PresentBrowserSession
{
    internal IPresentCanvasTextCommands? CanvasTextCommands { get; set; }
    private static readonly object MissingUiValue = new();
    private static readonly string[] TransitionKinds = Enum.GetNames<PresentTransitionKind>();
    private static readonly string[] AnimationEffects = Enum.GetNames<PresentAnimationEffect>();
    private static readonly string[] AnimationTriggers = Enum.GetNames<PresentAnimationTrigger>();
    private static readonly string[] TextAlignments = Enum.GetNames<PresentTextHorizontalAlignment>();
    private static readonly string[] TextVerticalAlignments = Enum.GetNames<PresentTextVerticalAlignment>();
    private static readonly string[] SlideSizes = ["Widescreen 16:9", "Standard 4:3", "Portrait 9:16", "Custom"];
    private static readonly string[] PaletteNames = ["Black", "White", "Blue", "Teal", "Green", "Yellow", "Orange", "Red", "Purple"];
    private static readonly string[] PaletteValues = ["#202020", "#ffffff", "#4472c4", "#008080", "#228b22", "#ffd966", "#ed7d31", "#c00000", "#7030a0"];
    private static readonly string[] StandardFonts = ["Aptos", "Arial", "Georgia", "Inter", "Montserrat", "Times New Roman"];
    private double _viewportWidth = 1280, _sidebarWidth = 208, _inspectorWidth = 288;
    private bool _showNotes = true, _showHelp, _showHistory;
    private double _zoom = 1;
    private bool _fitToWindow = true;
    private (PresentEditor Editor, Guid SlideId, string Preset)? _shapeInsertion;
    internal event Action<string, DocumentVectorShape>? BeginShapeInsertion;
    private IReadOnlyList<PresentVersionRow> _versions = [];
    private (Guid Id, string Fill, string Stroke, double Width)? _observedShape;
    public double CanvasZoom => _zoom;
    public bool IsFitToWindow => _fitToWindow;
    internal Func<double>? EffectiveCanvasZoom { get; set; }
    public void SetCanvasZoom(double zoom)
    {
        if (!double.IsFinite(zoom) || IsActionAvailable("SetZoom") != true) return;
        _zoom = Math.Clamp(zoom, .25, 4); _fitToWindow = false; Notify();
    }
    internal Task CompleteShapeInsertionAsync(string preset, double x, double y, double width, double height)
    {
        var insertion = _shapeInsertion; _shapeInsertion = null;
        if (insertion is null || insertion.Value.Preset != preset || !ReferenceEquals(insertion.Value.Editor, Editor)
            || insertion.Value.SlideId != Editor?.Selection.SlideId || IsActionAvailable("AddShape") != true) return Task.CompletedTask;
        return EditAsync(editor => { editor.AddShapeAt(insertion.Value.SlideId, preset, x, y, width, height); return true; });
    }
    private PresentElement? SelectedObject => Editor?.SelectedElements.FirstOrDefault();
    private double SlideWidth => (Editor?.Document.SlideSize.WidthInches ?? 13.333333) * 96;
    private double SlideHeight => (Editor?.Document.SlideSize.HeightInches ?? 7.5) * 96;
    private string CurrentFont => string.IsNullOrWhiteSpace(SelectedTextElement?.TextStyle.FontFamily)
        ? Editor?.Document.Theme.BodyFontFamily ?? "Aptos" : SelectedTextElement!.TextStyle.FontFamily;
    private string[] FontFamilies => StandardFonts.Contains(CurrentFont) ? StandardFonts : [CurrentFont, .. StandardFonts];
    private bool FullToolbar => _viewportWidth >= 820;
    private double SidebarWidth => Math.Min(_sidebarWidth, Math.Max(160, _viewportWidth * .3));
    private double InspectorWidth => _compact ? _viewportWidth : Math.Min(_inspectorWidth,
        Math.Max(256, _viewportWidth - (_showSidebar ? SidebarWidth + 8 : 0) - 360));

    private bool TryGetEditorUiValue(string path, out object? value)
    {
        var selected = SelectedObject;
        value = path switch
        {
            "TextContext" => CanvasTextCommands?.IsTextEditing == true,
            "CanEditSelectedText" => IsActionAvailable("EditSelectedText") == true,
            "CanEditSlideTitle" => IsActionAvailable("EditSlideTitle") == true,
            "ObjectContext" => CanvasTextCommands?.IsTextEditing != true && Editor?.SelectedElements.Count > 0,
            "CanvasContext" => CanvasTextCommands?.IsTextEditing != true && Editor?.SelectedElements.Count == 0,
            "CanCut" => IsActionAvailable("Cut") == true,
            "CanDuplicateObject" => IsActionAvailable("DuplicateObject") == true,
            "WideHeader" => _viewportWidth >= 620,
            "FullToolbar" => FullToolbar,
            "ToolbarClipboard" => _viewportWidth >= 1400,
            "ToolbarFonts" => _viewportWidth >= 1000,
            "ToolbarExpandedText" => _viewportWidth >= 1200,
            "ShowTextToolbar" => Editor is not null && (Editor.SelectedElements.Count == 0
                || SelectedTextElement is { Kind: PresentElementKind.Text }
                || SelectedTextElement is { Kind: PresentElementKind.Shape } && CanvasTextCommands?.IsTextEditing == true),
            "ShowShapeToolbar" => Editor?.SelectedElements.Count > 0
                && SelectedTextElement is not { Kind: PresentElementKind.Text }
                && !(SelectedTextElement is { Kind: PresentElementKind.Shape } && CanvasTextCommands?.IsTextEditing == true),
            "HeaderStatus" => IsBusy ? "Working…" : IsDirty ? "Unsaved changes" : Status,
            "ShowNotes" => _showNotes, "ShowHelp" => _showHelp, "ShowHistory" => _showHistory,
            "Versions" => _versions, "NoVersions" => _versions.Count == 0,
            "CanHistory" => IsActionAvailable("ShowHistory") == true,
            "CanRestoreHistory" => IsActionAvailable("RestoreVersion") == true,
            "ShowEditorChrome" => !_showHistory && !_showHelp && !IsPlaying,
            "SidebarTitle" => _showFiles ? "Presentations" : "Slides",
            "SidebarWidth" => SidebarWidth, "InspectorWidth" => InspectorWidth,
            "ShowSidebarResize" => _showSidebar && !_compact,
            "ShowInspectorResize" => _showInspector && !_compact,
            "WorkspaceColumns" => _compact || !_showSidebar ? "0 0 1fr" : $"{N(SidebarWidth)} 8 1fr",
            "SidebarColumnSpan" => _compact ? 3 : 1,
            "InspectorColumns" => !_showInspector ? "1fr 0 0" : _compact ? "0 0 1fr" : $"1fr 8 {N(InspectorWidth)}",
            "HasShapeSelection" => Editor?.SelectedElements.Any(item => item.Kind == PresentElementKind.Shape) == true,
            "HasSlideSelection" => Editor is not null && Editor.SelectedElements.Count == 0,
            "PropertiesTitle" => selected is null ? "Slide" : Editor!.SelectedElements.Count > 1 ? "Multiple objects" : selected.Kind.ToString(),
            "ObjectX" => selected is null ? 0d : Math.Round((selected.X + selected.Width / 2 - .5) * SlideWidth, 2),
            "ObjectY" => selected is null ? 0d : Math.Round((.5 - selected.Y - selected.Height / 2) * SlideHeight, 2),
            "ObjectWidth" => Math.Round((selected?.Width ?? 0) * SlideWidth, 2),
            "ObjectHeight" => Math.Round((selected?.Height ?? 0) * SlideHeight, 2),
            "ObjectRotation" => selected?.RotationDegrees ?? 0,
            "ObjectOpacity" => (selected?.Opacity ?? 1) * 100,
            "OpacityLabel" => $"{(selected?.Opacity ?? 1) * 100:0}%",
            "ColorPalette" => PaletteNames,
            "FillPaletteIndex" => Array.FindIndex(PaletteValues, color => color.Equals(_fillColor, StringComparison.OrdinalIgnoreCase)),
            "StrokePaletteIndex" => Array.FindIndex(PaletteValues, color => color.Equals(_strokeColor, StringComparison.OrdinalIgnoreCase)),
            "FillPreview" => Avalonia.Media.Color.TryParse(_fillColor, out _) ? _fillColor : "Transparent",
            "StrokePreview" => Avalonia.Media.Color.TryParse(_strokeColor, out _) ? _strokeColor : "Transparent",
            "FontFamilies" => FontFamilies,
            "FontFamilyIndex" => Array.IndexOf(FontFamilies, CurrentFont),
            "FontSize" => SelectedTextElement?.TextStyle.FontSizePoints ?? 22d,
            "IsBold" => SelectedTextElement?.TextStyle.Bold == true,
            "IsItalic" => SelectedTextElement?.TextStyle.Italic == true,
            "IsUnderline" => SelectedTextElement?.TextStyle.Underline == true,
            "TextColor" => SelectedTextElement?.TextStyle.Color ?? string.Empty,
            "TextAlignments" => TextAlignments,
            "TextVerticalAlignments" => TextVerticalAlignments,
            "TextAlignmentIndex" => (int)(SelectedTextElement?.TextStyle.HorizontalAlignment ?? PresentTextHorizontalAlignment.Left),
            "TextVerticalAlignmentIndex" => (int)(SelectedTextElement?.TextStyle.VerticalAlignment ?? PresentTextVerticalAlignment.Top),
            "SlideLayouts" => Editor?.Document.Layouts.Select(layout => layout.Name).ToArray() ?? [],
            "SlideLayoutIndex" => Editor?.Document.Layouts.FindIndex(layout => layout.Id == Editor.SelectedSlide.LayoutId) ?? -1,
            "SlideBackground" => Editor?.SelectedSlide.Background.Color ?? string.Empty,
            "SlideSizes" => SlideSizes, "SlideSizeIndex" => (int)(Editor?.Document.SlideSize.Preset ?? PresentSlideSizePreset.Widescreen16By9),
            "TransitionKinds" => TransitionKinds, "TransitionIndex" => Array.IndexOf(TransitionKinds, _transitionKind),
            "AnimationEffects" => AnimationEffects, "AnimationEffectIndex" => Array.IndexOf(AnimationEffects, _animationEffect),
            "AnimationTriggers" => AnimationTriggers, "AnimationTriggerIndex" => Array.IndexOf(AnimationTriggers, _animationTrigger),
            "IsZoomed" => !_fitToWindow,
            "ZoomLabel" => _fitToWindow ? "Fit" : $"{_zoom * 100:0}%",
            _ => MissingUiValue
        };
        return !ReferenceEquals(value, MissingUiValue);
    }
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static bool Number(object? value, out double number) => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
        NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    private bool? TrySetEditorUiValue(string path, object? value)
    {
        if (path is "SidebarWidth" or "InspectorWidth")
        {
            if (!Number(value, out var width)) return false;
            if (path == "SidebarWidth") _sidebarWidth = Math.Clamp(width, 160, 320);
            else _inspectorWidth = Math.Clamp(width, 256, 420);
            Notify(); return true;
        }
        if (path is not ("ObjectX" or "ObjectY" or "ObjectWidth" or "ObjectHeight" or "ObjectRotation" or "ObjectOpacity"
            or "FontSize" or "FontFamilyIndex" or "TextColor" or "TextAlignmentIndex" or "TextVerticalAlignmentIndex"
            or "FillColor" or "StrokeColor" or "StrokeWidth" or "FillPaletteIndex" or "StrokePaletteIndex" or "SlideLayoutIndex" or "SlideBackground" or "SlideSizeIndex" or "TransitionIndex" or "AnimationEffectIndex" or "AnimationTriggerIndex")) return null;
        if (IsActionAvailable("AddSlide") != true || Editor is null) return false;
        var editor = Editor;
        bool Edit(Func<PresentEditor, bool> operation)
        {
            editor.CommitLiveTextEdit();
            var changed = operation(editor);
            editor.Document.Version = DurableRevision;
            if (changed) Notify();
            // An already-current valid value is accepted without adding an undo
            // entry. Re-entering it must not create a spurious validation error.
            return changed || TryGetEditorUiValue(path, out var current) &&
                (Equals(current, value) || Number(current, out var currentNumber) && Number(value, out var requestedNumber)
                    && currentNumber == requestedNumber);
        }
        if (path is "TextColor" or "SlideBackground" or "FillColor" or "StrokeColor")
        {
            if (value is not string color || !Avalonia.Media.Color.TryParse(color, out _)) return false;
            if (path == "SlideBackground") return Edit(owner => owner.SetSlideBackgroundColor(owner.Selection.SlideId, color));
            if (path == "FillColor") return Edit(owner => owner.SetSelectedShapeAppearance(fill: color));
            if (path == "StrokeColor") return Edit(owner => owner.SetSelectedShapeAppearance(stroke: color));
            return Edit(owner => UpdateTextStyle(owner, style => style.Color = color));
        }
        if (!Number(value, out var number)) return false;
        var index = (int)number;
        switch (path)
        {
            case "FillPaletteIndex" when index >= 0 && index < PaletteValues.Length && IsActionAvailable("ApplyFill") == true:
                return Edit(owner => owner.SetSelectedShapeAppearance(fill: PaletteValues[index]));
            case "StrokePaletteIndex" when index >= 0 && index < PaletteValues.Length && IsActionAvailable("ApplyStroke") == true:
                return Edit(owner => owner.SetSelectedShapeAppearance(stroke: PaletteValues[index]));
            case "StrokeWidth" when number is >= 0 and <= 100:
                return Edit(owner => owner.SetSelectedShapeAppearance(strokeWidth: number));
            case "FontFamilyIndex" when index >= 0 && index < FontFamilies.Length:
                var family = FontFamilies[index]; return Edit(owner => UpdateTextStyle(owner, style => style.FontFamily = family));
            case "FontSize" when number is >= 1 and <= 400:
                return Edit(owner => UpdateTextStyle(owner, style => style.FontSizePoints = number));
            case "TextAlignmentIndex" when index >= 0 && index < TextAlignments.Length:
                return Edit(owner => UpdateTextStyle(owner, style => style.HorizontalAlignment = (PresentTextHorizontalAlignment)index));
            case "TextVerticalAlignmentIndex" when index >= 0 && index < TextVerticalAlignments.Length:
                return Edit(owner => UpdateTextStyle(owner, style => style.VerticalAlignment = (PresentTextVerticalAlignment)index));
            case "SlideLayoutIndex" when index >= 0 && index < editor.Document.Layouts.Count:
                return Edit(owner => owner.SetSlideLayout(owner.Selection.SlideId, owner.Document.Layouts[index].Id));
            case "SlideSizeIndex" when index is >= 0 and < 3:
                return Edit(owner => owner.SetSlideSizePreset((PresentSlideSizePreset)index));
            case "TransitionIndex" when index >= 0 && index < TransitionKinds.Length:
                _transitionKind = TransitionKinds[index]; return true;
            case "AnimationEffectIndex" when index >= 0 && index < AnimationEffects.Length:
                _animationEffect = AnimationEffects[index]; return true;
            case "AnimationTriggerIndex" when index >= 0 && index < AnimationTriggers.Length:
                _animationTrigger = AnimationTriggers[index]; return true;
            case "ObjectOpacity" when number is >= 0 and <= 100:
                return Edit(owner => owner.SetSelectedOpacity(number / 100));
        }
        if (SelectedObject is not { } element || IsActionAvailable("DeleteObject") != true) return false;
        return path switch
        {
            "ObjectX" => Edit(owner => owner.MoveSelection(number / SlideWidth + .5 - element.Width / 2 - element.X, 0)),
            "ObjectY" => Edit(owner => owner.MoveSelection(0, .5 - number / SlideHeight - element.Height / 2 - element.Y)),
            "ObjectWidth" when number > 0 && number <= SlideWidth => Edit(owner => owner.ResizeSelection(number / SlideWidth - element.Width, 0)),
            "ObjectHeight" when number > 0 && number <= SlideHeight => Edit(owner => owner.ResizeSelection(0, number / SlideHeight - element.Height)),
            "ObjectRotation" => Edit(owner => owner.RotateSelection(number - element.RotationDegrees)),
            _ => false
        };
    }
    private static bool UpdateTextStyle(PresentEditor editor, Action<PresentTextStyle> update)
    {
        if (editor.SelectedElements.FirstOrDefault(element => element.Kind is PresentElementKind.Text or PresentElementKind.Shape) is not { } selected) return false;
        var style = JsonSerializer.Deserialize<PresentTextStyle>(JsonSerializer.Serialize(selected.TextStyle))!;
        update(style); return editor.SetSelectedTextStyle(style);
    }
    private void RefreshEditorUi()
    {
        if (SelectedObject is { Kind: PresentElementKind.Shape } shape)
        {
            var vectorPath = shape.VectorShape?.Paths.FirstOrDefault();
            var current = (shape.Id,
                vectorPath is null ? shape.Style.FillColor : vectorPath.Fill.Kind == DocumentVectorFillKind.None ? "#00000000" : vectorPath.Fill.Color,
                vectorPath is null ? shape.Style.StrokeColor : vectorPath.Stroke.Enabled ? vectorPath.Stroke.Color : "#00000000",
                vectorPath is null ? shape.Style.StrokeWidth : vectorPath.Stroke.Enabled ? vectorPath.Stroke.Width : 0);
            if (current != _observedShape)
            {
                _observedShape = current;
                _fillColor = current.Item2; _strokeColor = current.Item3; _strokeWidth = current.Item4;
            }
        }
        else _observedShape = null;
    }
    private async Task ShowHistoryAsync(CancellationToken token) => await Operate(async ct =>
    {
        if (Editor is null || _repository is not IPresentVersionRepository versions)
            return Failure<bool>("UnsupportedFeature", "Saved versions are unavailable from this repository.");
        _versions = (await versions.ReadVersionsAsync(Editor.Document.Id, ct)).Select(document => new PresentVersionRow(document)).ToArray();
        _showHistory = true; return Success(true);
    }, token);
}

public sealed record PresentVersionRow(PresentDocument Document) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Version" => Document.Version, "Title" => $"Version {Document.Version} · {Document.UpdatedAt:dd MMM, HH:mm}",
            "Summary" => $"{Document.Title} · {Document.Slides.Count} slides", _ => null };
        return path is "Version" or "Title" or "Summary";
    }
}
