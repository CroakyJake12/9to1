using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

/// <summary>Loads the authored strip; this adapter projects existing state and routes existing operations.</summary>
internal static class AdaptiveToolbar
{
    private static readonly ConditionalWeakTable<Control, Context> Contexts = new();

    internal static Control Build(BoardsViewModel owner)
    {
        var context = new Context(owner);
        var loader = BoardsCuiDocuments.CreateLoader();
        loader.SetBindingContext(context);
        loader.SetActionDispatcher(context);
        try
        {
            var (root, diagnostics) = loader.TryLoad(BoardsCuiDocuments.Load("BoardsAdaptiveToolbar.cui"));
            if (root is null) throw new InvalidOperationException(diagnostics.FirstOrDefault()?.Message ?? "Boards toolbar did not load.");
            loader.WireBindings(root);
            context.Loader = loader;
            Contexts.Add(root, context);
            if (BoardsCuiControlFinder.FindByAutomationId<Button>(root, "AdaptiveAdd") is { } insert)
                ToolbarBuilder.AttachInsertMenu(insert, owner.ToolbarTarget);
            root.DetachedFromVisualTree += (_, _) => context.Dispose();
            return root;
        }
        catch { context.Dispose(); loader.Dispose(); throw; }
    }

    internal static void Dispose(Control root)
    {
        if (Contexts.TryGetValue(root, out var context)) context.Dispose();
    }

    internal static bool TryReuse(Control root, BoardsViewModel owner)
    {
        if (!Contexts.TryGetValue(root, out var context) || !context.CanReuse(owner)) return false;
        context.QueueRefresh();
        return true;
    }

    internal sealed class Context : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
    {
        private readonly BoardsViewModel _owner;
        private readonly BoardsViewModel _insertTarget;
        private readonly string _insertPage;
        private readonly RichBoardDocument _insertDocument;
        private BoardsViewModel _observed;
        private bool _disposed, _notifying;
        private int _refreshQueued;
        private string? _fontDraft, _sizeDraft;
        private string? _draftIdentity;
        private (string Text, int Anchor, int Caret)? _draftSelection;
        internal CuiControlLoader? Loader;
        public event PropertyChangedEventHandler? PropertyChanged;

        internal Context(BoardsViewModel owner)
        {
            _owner = owner;
            _observed = owner.ToolbarTarget;
            _insertTarget = _observed;
            _insertPage = _observed.SelectedPageId;
            _insertDocument = _observed.Session.Document;
            _owner.PropertyChanged += Changed;
            _owner.ToolbarChanged += QueueRefresh;
            if (!ReferenceEquals(_observed, owner)) _observed.PropertyChanged += Changed;
        }

        private BoardsViewModel Target => _owner.ToolbarTarget;
        internal bool CanReuse(BoardsViewModel owner) => !_disposed && ReferenceEquals(_owner, owner) &&
            ReferenceEquals(_insertTarget, owner.ToolbarTarget) && _insertPage == _insertTarget.SelectedPageId &&
            ReferenceEquals(_insertDocument, _insertTarget.Session.Document);
        private bool IsListRow => Target.AdaptiveCapabilities().ListRow;
        private const string ListSettingUnavailable = "Unavailable for individual list items.";

        private bool RefuseUnsupportedCommand(string command)
        {
            var capability = Target.AdaptiveCapabilities();
            var supported = command switch
            {
                "ListNone" or "ListBullets" or "ListNumbered" or "ListChecklist" => capability.ListConversion,
                "AlignLeft" or "AlignCenter" or "AlignRight" or "AlignJustify" => capability.ParagraphLayout,
                "ApplyFont" or "ApplySize" or "ToggleBold" or "ToggleItalic" or "ToggleUnderline" or
                    "ToggleStrike" or "ToggleSub" or "ToggleSup" or "ClearFormat" or "CaseUpper" or "CaseLower" or
                    "IndentInc" or "IndentDec" => capability.Typography,
                "Properties" => capability.Properties,
                _ when command.StartsWith("SpaceBefore", StringComparison.Ordinal) || command.StartsWith("SpaceAfter", StringComparison.Ordinal) => capability.ParagraphLayout,
                _ when command.StartsWith("Font", StringComparison.Ordinal) || command.StartsWith("Size", StringComparison.Ordinal) ||
                    command.StartsWith("Highlight", StringComparison.Ordinal) || command.StartsWith("Spacing", StringComparison.Ordinal) ||
                    command.StartsWith("TextColor", StringComparison.Ordinal) => capability.Typography,
                _ => true
            };
            if (!supported)
            {
                Target.Set("StatusText", capability.ListRow ? ListSettingUnavailable : "Unavailable for the current selection.");
                return true;
            }
            return false;
        }
        private void Changed(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
        internal void QueueRefresh()
        {
            if (_disposed || _notifying || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
            // One text edit publishes many VM properties. Apply the final snapshot once,
            // after the current input work, instead of resetting every authored binding
            // for each intermediate property. Actions still read the live target directly.
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!_disposed) Refresh();
            }, DispatcherPriority.Background);
        }
        private void Refresh()
        {
            if (_disposed || _notifying) return;
            _notifying = true;
            try
            {
                var target = Target;
                if (!ReferenceEquals(_observed, target))
                {
                    if (!ReferenceEquals(_observed, _owner)) _observed.PropertyChanged -= Changed;
                    _observed = target;
                    if (!ReferenceEquals(target, _owner)) target.PropertyChanged += Changed;
                }
                _owner.PublishAdaptiveContext(target);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
            finally { _notifying = false; }
        }

        public bool TryGetValue(string path, out object? value)
        {
            var target = Target;
            var block = target.FindBlock(target.SelectedBlockId);
            var style = target.StyleList.FirstOrDefault(item => item.Id == block?.StyleId);
            var key = path[(path.LastIndexOf('.') + 1)..];
            var fontValues = target.AdaptiveFontValues();
            var colors = target.AdaptiveColorValues();
            var capability = target.AdaptiveCapabilities();
            double? lineSpacing = block?.LineSpacing > 0 ? block.LineSpacing
                : style?.LineSpacing > 0 ? style.LineSpacing : null;
            var spacingLabel = lineSpacing?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Auto";
            var listRow = capability.ListRow;
            var alignment = block?.Alignment is null or "" or "inherit" ? style?.Alignment : block.Alignment;
            if (alignment is null or "" or "inherit") alignment = "left";
            var indent = listRow ? block!.Level : block?.IndentLevel >= 0 ? block.IndentLevel : Math.Max(0, style?.IndentLevel ?? 0);
            SynchronizeDraftIdentity(target);
            var text = !target.IsDrawMode && (block is null || block.Kind is "paragraph" or "heading" or "bulleted" or "numbered" or "checklist");
            value = key switch
            {
                "TextMode" => text,
                "TableMode" => !target.IsDrawMode && block?.Kind == "table",
                "ImageMode" => !target.IsDrawMode && block?.Kind == "image",
                "OtherMode" => !target.IsDrawMode && !text && block?.Kind is not ("table" or "image"),
                "DrawingMode" => target.IsDrawMode,
                "CanEditToolbar" => target.CanEdit && target.IsEditMode,
                "CanUseTypography" => capability.Typography,
                "CanUseParagraphStyle" => capability.ParagraphStyle,
                "CanUseParagraphAlignment" => capability.ParagraphLayout,
                "CanUseParagraphSpacing" => capability.ParagraphLayout,
                "CanChangeListKind" => capability.ListConversion,
                "TypographyGroupHint" => listRow ? "Font and size apply to the entire list item." : "Font, paragraph style and size",
                "FontControlHint" => listRow ? "Type a font family for the entire list item and press Enter" : "Type a font family and press Enter",
                "SizeControlHint" => listRow ? "Type a font size from 8 to 96 for the entire list item and press Enter" : "Type a font size from 8 to 96 and press Enter",
                "StyleControlHint" => listRow ? ListSettingUnavailable : "Paragraph style",
                "HighlightControlHint" => listRow ? "Highlight the entire list item: " + colors.Background : colors.BackgroundMixed ? "Mixed highlight colours" : "Highlight colour " + colors.Background,
                "SpacingControlHint" => (listRow ? "Line spacing for the entire list item: " : "Line spacing ") + spacingLabel,
                "ParagraphSpacingHint" => listRow ? "Spacing before and after applies to a paragraph, not an individual list item." : "Space before and after this paragraph, in pixels",
                "SpaceBeforeLabel" => "Before: " + (block?.SpaceBefore ?? 0).ToString("0.##", CultureInfo.InvariantCulture) + " px",
                "SpaceAfterLabel" => "After: " + (block?.SpaceAfter ?? 0).ToString("0.##", CultureInfo.InvariantCulture) + " px",
                "AlignmentControlHint" => listRow ? ListSettingUnavailable : "Alignment: " + alignment,
                "ListControlHint" => listRow ? "List type changes are unavailable for individual list items." : "List type",
                "CanUseProperties" => capability.Properties,
                "PropertiesSelected" => _owner.Get("AdaptivePropertiesVisible") is true,
                "StyleNames" => target.StyleList.Select(item => item.Id == "normal" ? "Paragraph" : item.Id.StartsWith("heading-", StringComparison.Ordinal) ? "Heading " + item.Id[8..] : item.Name).ToArray(),
                "StyleIndex" => target.StyleList.ToList().FindIndex(item => item.Id == block?.StyleId),
                "FontDraft" => _fontDraft ?? fontValues.Font,
                "SizeDraft" => _sizeDraft ?? fontValues.Size,
                "TextColor" => Brush(colors.Foreground),
                "HighlightColor" => Brush(colors.Background),
                "TextColorMixed" => colors.ForegroundMixed,
                "TextColorUniform" => !colors.ForegroundMixed,
                "HighlightColorMixed" => colors.BackgroundMixed,
                "HighlightColorUniform" => !colors.BackgroundMixed,
                "TextColorLabel" => colors.ForegroundMixed ? "Mixed text colours" : "Text colour " + colors.Foreground,
                "HighlightColorLabel" => colors.BackgroundMixed ? "Mixed highlight colours" : "Highlight colour " + colors.Background,
                "AlignIcon" => alignment switch { "center" => "icon:align-center", "right" => "icon:align-right", "justify" => "icon:align-justify", _ => "icon:align-left" },
                "ListIcon" => block?.Kind switch { "numbered" => "icon:numbered-list", "checklist" => "icon:check", _ => "icon:bulleted-list" },
                "CompactSpacing" => spacingLabel + " ↕",
                "AlignLabel" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(alignment) + " ▾",
                "AlignLeftSelected" => alignment == "left",
                "AlignCenterSelected" => alignment == "center",
                "AlignRightSelected" => alignment == "right",
                "AlignJustifySelected" => alignment == "justify",
                "SpacingAutoSelected" => !listRow && block?.LineSpacing is null or <= 0,
                "Spacing1Selected" => lineSpacing == 1,
                "Spacing125Selected" => lineSpacing == 1.25,
                "Spacing15Selected" => lineSpacing == 1.5,
                "Spacing2Selected" => lineSpacing == 2,
                "ListLabel" => (block?.Kind switch { "bulleted" => "Bullets", "numbered" => "Numbered", "checklist" => "Checklist", _ => "List" }) + " ▾",
                "SpacingLabel" => "Spacing " + spacingLabel + " ▾",
                "IndentLabel" => "Indent " + indent.ToString(CultureInfo.InvariantCulture) + " ▾",
                "ObjectLabel" => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(block?.Kind ?? "Block"),
                "ImageFitLabel" => "Fit: " + (block?.Image?.FitMode ?? "contain"),
                "InkLabel" => target.InkTool + " ▾",
                "InkWidthLabel" => target.InkWidth.ToString("0.#", CultureInfo.InvariantCulture) + " px ▾",
                "InkColor" => Brush(target.InkColor),
                "CanUndo" => target.Session.CanUndo,
                "CanRedo" => target.Session.CanRedo,
                _ when key.StartsWith("SpaceBefore", StringComparison.Ordinal) && key.EndsWith("Selected", StringComparison.Ordinal) => IsSpaceSelected(key[11..^8], block?.SpaceBefore ?? 0),
                _ when key.StartsWith("SpaceAfter", StringComparison.Ordinal) && key.EndsWith("Selected", StringComparison.Ordinal) => IsSpaceSelected(key[10..^8], block?.SpaceAfter ?? 0),
                _ => target.Get(key)
            };
            return true;
        }

        private void SynchronizeDraftIdentity(BoardsViewModel target)
        {
            var identity = target.SelectedPageId + "\0" + target.SelectedBlockId;
            var selection = target.AdaptiveSelectionCoordinates();
            if (_draftIdentity != identity)
            {
                _draftIdentity = identity;
                _draftSelection = selection;
                _fontDraft = _sizeDraft = null;
            }
            else if (selection is { } currentSelection)
            {
                // A transient detached editor during a render has no new
                // selection. A real caret/range change exposes its actual style.
                if (_draftSelection != currentSelection) _fontDraft = _sizeDraft = null;
                _draftSelection = currentSelection;
            }
        }

        private static Avalonia.Media.IBrush Brush(string? value) =>
            !string.IsNullOrWhiteSpace(value) && BoardsTheme.TryBrush(value, out var brush) ? brush : Avalonia.Media.Brushes.Transparent;

        private static bool IsSpaceSelected(string points, double current) =>
            double.TryParse(points, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(current - value) < 0.001;

        public bool TrySetValue(string path, object? value)
        {
            if (_disposed || !Target.CanEdit || !Target.IsEditMode) return false;
            var key = path[(path.LastIndexOf('.') + 1)..];
            SynchronizeDraftIdentity(Target);
            var capability = Target.AdaptiveCapabilities();
            if ((key is "FontDraft" or "SizeDraft") && !capability.Typography || key == "StyleIndex" && !capability.ParagraphStyle)
            {
                Target.Set("StatusText", ListSettingUnavailable);
                return false;
            }
            switch (key)
            {
                case "FontDraft": _fontDraft = value as string; return true;
                case "SizeDraft": _sizeDraft = value as string; return true;
                case "StyleIndex" when value is int index && index >= 0 && index < Target.StyleList.Count:
                    _ = Target.ApplyStyleToSelectedAsync(Target.StyleList[index].Id); return true;
                default: return Target.TrySetValue(key, value);
            }
        }

        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            if (_disposed) return;
            var target = Target;
            if (!target.CanEdit || !target.IsEditMode || RefuseUnsupportedCommand(command)) return;
            SynchronizeDraftIdentity(target);
            var block = target.FindBlock(target.SelectedBlockId);
            switch (command)
            {
                case "ApplyFont":
                    if (_fontDraft is null) break;
                    var font = _fontDraft.Trim();
                    if (font.Length <= 100 && !font.Any(char.IsControl)) target.SetSelectedFontFamily(font == "Default" ? string.Empty : font);
                    _fontDraft = null; break;
                case "ApplySize":
                    if (double.TryParse(_sizeDraft, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size is >= 8 and <= 96 && double.IsFinite(size)) target.SetSelectedFontSize(size);
                    _sizeDraft = null; break;
                case "ToggleDrawing":
                    if (target.IsDrawMode) target.ExitDrawingMode(); else await target.ActivateDrawingAsync(); break;
                case "Properties": _owner.SetAdaptiveProperties(_owner.Get("AdaptivePropertiesVisible") is not true); break;
                case "CaseUpper": target.ChangeAdaptiveCase("upper"); break;
                case "CaseLower": target.ChangeAdaptiveCase("lower"); break;
                case "IndentInc" when IsListRow: target.ChecklistIndent(target.SelectedBlockId, 1); target.RequestRebuild(); break;
                case "IndentDec" when IsListRow: target.ChecklistIndent(target.SelectedBlockId, -1); target.RequestRebuild(); break;
                case "ListNone": await target.ConvertSelectedKindAsync("paragraph"); break;
                case "ListBullets": await target.ConvertSelectedKindAsync("bulleted"); break;
                case "ListNumbered": await target.ConvertSelectedKindAsync("numbered"); break;
                case "ListChecklist": await target.ConvertSelectedKindAsync("checklist"); break;
                case "FontDefault": target.SetSelectedFontFamily(string.Empty); break;
                case "FontInter": target.SetSelectedFontFamily("Inter"); break;
                case "FontGeorgia": target.SetSelectedFontFamily("Georgia"); break;
                case "FontSegoe": target.SetSelectedFontFamily("Segoe UI"); break;
                case "FontTimes": target.SetSelectedFontFamily("Times New Roman"); break;
                case "FontMono": target.SetSelectedFontFamily("Cascadia Mono"); break;
                case "InkZoomIn": await target.SetInkZoomAsync(target.InkZoom * 1.25); break;
                case "InkZoomOut": await target.SetInkZoomAsync(target.InkZoom / 1.25); break;
                case "InkResetView": await target.ResetInkViewAsync(); break;
                case "InkPen": target.SetInkTool("Pen"); break;
                case "InkHighlighter": target.SetInkTool("Highlighter"); break;
                case "InkEraser": target.SetInkTool("Eraser"); break;
                case "InkSelect": target.SetInkTool("Select"); break;
                default:
                    if (command.StartsWith("Size", StringComparison.Ordinal) && double.TryParse(command[4..], out var points)) target.SetSelectedFontSize(points);
                    else if (command.StartsWith("SpaceBefore", StringComparison.Ordinal) && double.TryParse(command[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var before)) target.SetAdaptiveParagraphSpace(true, before);
                    else if (command.StartsWith("SpaceAfter", StringComparison.Ordinal) && double.TryParse(command[10..], NumberStyles.Float, CultureInfo.InvariantCulture, out var after)) target.SetAdaptiveParagraphSpace(false, after);
                    else if (command.StartsWith("Spacing", StringComparison.Ordinal) && double.TryParse(command[7..], NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing)) target.SetAdaptiveSpacing(spacing);
                    else if (command.StartsWith("InkWidth", StringComparison.Ordinal) && double.TryParse(command[8..], out var width)) target.SetInkWidth(width);
                    else if (command.StartsWith("InkColor", StringComparison.Ordinal)) target.SetInkColor(command[8..]);
                    else if (command.StartsWith("TextColor", StringComparison.Ordinal)) target.ApplyForegroundAttributes(command[9..]);
                    else if (command.StartsWith("Highlight", StringComparison.Ordinal)) target.ApplyBackgroundAttributes(command[9..] == "None" ? string.Empty : command[9..]);
                    else await target.DispatchAsync(command, parameter, cancellationToken);
                    break;
            }
            if (command is "FontDefault" or "FontInter" or "FontGeorgia" or "FontSegoe" or "FontTimes" or "FontMono")
                _fontDraft = null;
            if (command is "FontSmaller" or "FontBigger" || command.StartsWith("Size", StringComparison.Ordinal))
                _sizeDraft = null;
            QueueRefresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.PropertyChanged -= Changed;
            _owner.ToolbarChanged -= QueueRefresh;
            if (!ReferenceEquals(_observed, _owner)) _observed.PropertyChanged -= Changed;
            Loader?.Dispose();
        }
    }
}
