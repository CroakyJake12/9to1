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

    internal sealed class Context : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
    {
        private readonly BoardsViewModel _owner;
        private BoardsViewModel _observed;
        private bool _disposed, _notifying;
        private int _refreshQueued;
        private string? _fontDraft, _sizeDraft;
        private string? _draftIdentity;
        internal CuiControlLoader? Loader;
        public event PropertyChangedEventHandler? PropertyChanged;

        internal Context(BoardsViewModel owner)
        {
            _owner = owner;
            _observed = owner.ToolbarTarget;
            _owner.PropertyChanged += Changed;
            _owner.ToolbarChanged += QueueRefresh;
            if (!ReferenceEquals(_observed, owner)) _observed.PropertyChanged += Changed;
        }

        private BoardsViewModel Target => _owner.ToolbarTarget;
        private bool IsListRow => Target.FindBlock(Target.SelectedBlockId)?.Kind is "checklist" or "bulleted" or "numbered";
        private const string ListSettingUnavailable = "Unavailable for individual list items.";

        private bool RefuseUnsupportedListCommand(string command)
        {
            if (!IsListRow) return false;
            if (command is "ApplyFont" or "ApplySize" or "FontSmaller" or "FontBigger" or
                "ListNone" or "ListBullets" or "ListNumbered" or "ListChecklist" ||
                command.StartsWith("Font", StringComparison.Ordinal) || command.StartsWith("Size", StringComparison.Ordinal) ||
                command.StartsWith("Highlight", StringComparison.Ordinal) || command.StartsWith("Spacing", StringComparison.Ordinal) ||
                command.StartsWith("Align", StringComparison.Ordinal))
            {
                Target.Set("StatusText", ListSettingUnavailable);
                return true;
            }
            return false;
        }
        private void Changed(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
        private void QueueRefresh()
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
            double? lineSpacing = block?.LineSpacing > 0 ? block.LineSpacing
                : style?.LineSpacing > 0 ? style.LineSpacing : null;
            var spacingLabel = lineSpacing?.ToString("0.##", CultureInfo.InvariantCulture) ?? "Auto";
            var listRow = block?.Kind is "checklist" or "bulleted" or "numbered";
            var alignment = block?.Alignment is null or "" or "inherit" ? style?.Alignment : block.Alignment;
            if (alignment is null or "" or "inherit") alignment = "left";
            var indent = listRow ? block!.Level : block?.IndentLevel >= 0 ? block.IndentLevel : Math.Max(0, style?.IndentLevel ?? 0);
            var identity = target.SelectedPageId + "\0" + target.SelectedBlockId;
            if (_draftIdentity != identity) { _draftIdentity = identity; _fontDraft = _sizeDraft = null; }
            var text = !target.IsDrawMode && (block is null || block.Kind is "paragraph" or "heading" or "bulleted" or "numbered" or "checklist");
            value = key switch
            {
                "TextMode" => text,
                "TableMode" => !target.IsDrawMode && block?.Kind == "table",
                "ImageMode" => !target.IsDrawMode && block?.Kind == "image",
                "OtherMode" => !target.IsDrawMode && !text && block?.Kind is not ("table" or "image"),
                "DrawingMode" => target.IsDrawMode,
                "CanEditToolbar" => target.CanEdit && target.IsEditMode,
                "CanUseTypography" => !listRow && target.CanEdit && target.IsEditMode,
                "CanUseParagraphAlignment" => !listRow && target.CanEdit && target.IsEditMode,
                "CanChangeListKind" => !listRow && target.CanEdit && target.IsEditMode,
                "TypographyGroupHint" => listRow ? "Font, style and size unavailable for individual list items." : "Font, paragraph style and size",
                "FontControlHint" => listRow ? ListSettingUnavailable : "Type a font family and press Enter",
                "SizeControlHint" => listRow ? ListSettingUnavailable : "Type a font size from 8 to 96 and press Enter",
                "StyleControlHint" => listRow ? ListSettingUnavailable : "Paragraph style",
                "HighlightControlHint" => listRow ? ListSettingUnavailable : colors.BackgroundMixed ? "Mixed highlight colours" : "Highlight colour " + colors.Background,
                "SpacingControlHint" => listRow ? ListSettingUnavailable : "Line spacing " + spacingLabel,
                "AlignmentControlHint" => listRow ? ListSettingUnavailable : "Alignment: " + alignment,
                "ListControlHint" => listRow ? "List type changes are unavailable for individual list items." : "List type",
                "CanUseProperties" => target.CanEdit && target.IsEditMode,
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
                _ => target.Get(key)
            };
            return true;
        }

        private static Avalonia.Media.IBrush Brush(string? value) =>
            !string.IsNullOrWhiteSpace(value) && BoardsTheme.TryBrush(value, out var brush) ? brush : Avalonia.Media.Brushes.Transparent;

        public bool TrySetValue(string path, object? value)
        {
            if (_disposed || !Target.CanEdit || !Target.IsEditMode) return false;
            var key = path[(path.LastIndexOf('.') + 1)..];
            if (IsListRow && (key is "FontDraft" or "SizeDraft" or "StyleIndex"))
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
            if (!target.CanEdit || !target.IsEditMode || RefuseUnsupportedListCommand(command)) return;
            var block = target.FindBlock(target.SelectedBlockId);
            switch (command)
            {
                case "ApplyFont":
                    var font = (_fontDraft ?? string.Empty).Trim();
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
                    else if (command.StartsWith("Spacing", StringComparison.Ordinal) && double.TryParse(command[7..], NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing)) target.SetAdaptiveSpacing(spacing);
                    else if (command.StartsWith("InkWidth", StringComparison.Ordinal) && double.TryParse(command[8..], out var width)) target.SetInkWidth(width);
                    else if (command.StartsWith("InkColor", StringComparison.Ordinal)) target.SetInkColor(command[8..]);
                    else if (command.StartsWith("TextColor", StringComparison.Ordinal)) target.ApplyForegroundAttributes(command[9..]);
                    else if (command.StartsWith("Highlight", StringComparison.Ordinal)) target.ApplyBackgroundAttributes(command[9..] == "None" ? string.Empty : command[9..]);
                    else await target.DispatchAsync(command, parameter, cancellationToken);
                    break;
            }
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
