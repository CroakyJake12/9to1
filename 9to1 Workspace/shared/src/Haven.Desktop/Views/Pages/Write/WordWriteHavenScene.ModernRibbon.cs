using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    private static readonly string[] CommonFonts = ["Montserrat", "Arial", "Calibri", "Cambria", "Georgia", "Times New Roman", "Cascadia Mono"];
    private static readonly string[] CommonSizes = ["8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72"];

    private void OnDocumentSelectionChanged(object? sender, EventArgs e) => OwnOriginalCallback(() => OnDocumentSelectionChangedCore(sender, e));
    private void OnDocumentSelectionChangedCore(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        RebuildRibbon();
        RefreshCommands();
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HomeModern()
    {
        var editor = _editor!;
        var block = editor.SelectedBlock;
        var selectedFamily = editor.SelectedFontFamily;
        var selectedSize = editor.SelectedFontSize;
        var selectedForeground = editor.SelectedForeground;
        var selectedBackground = editor.SelectedBackground;
        var selectedSpacing = editor.SelectedLineSpacing;
        var selectedStyleId = editor.SelectedStyleId;
        var familyMixed = editor.HasTextSelection && selectedFamily is null;
        var sizeMixed = editor.HasTextSelection && selectedSize is null;
        var foregroundMixed = editor.HasTextSelection && selectedForeground is null;
        var backgroundMixed = editor.HasTextSelection && selectedBackground is null;
        var spacingMixed = editor.HasTextSelection && selectedSpacing is null;
        var styleMixed = editor.HasTextSelection && selectedStyleId is null;
        var styles = editor.Document.Styles;
        var styleNames = styles.Select(value => value.Name).ToArray();
        string[] styleChoices = styleMixed ? ["Mixed", .. styleNames] : styleNames;
        var styleIndex = selectedStyleId is null ? -1 : styles.FindIndex(value => value.Id.Equals(selectedStyleId, StringComparison.OrdinalIgnoreCase));
        var style = Choice("Write.Home.Style", "Paragraph style", styleChoices, styleMixed ? 0 : Math.Max(0, styleIndex));
        if (styleMixed) style.Accessibility.Description = "Selected paragraphs use more than one style.";
        style.SelectionChanged += (_, _) =>
        OwnOriginalCallback(() => {
            var selectedIndex = style.SelectedIndex - (styleMixed ? 1 : 0);
            if (selectedIndex >= 0 && selectedIndex < styles.Count) { editor.ApplyStyle(styles[selectedIndex].Id); RebuildRibbon(); DocumentSurface.InvalidateDocument(); }
        });
        RibbonContent.Add(style);

        var currentFamily = familyMixed ? "Mixed" : selectedFamily;
        if (string.IsNullOrWhiteSpace(currentFamily)) currentFamily = CommonFonts[0];
        string[] fontChoices = familyMixed ? ["Mixed", .. CommonFonts]
            : CommonFonts.Any(value => value.Equals(currentFamily, StringComparison.OrdinalIgnoreCase))
                ? CommonFonts : [currentFamily, .. CommonFonts];
        var fontIndex = Array.FindIndex(fontChoices, value => value.Equals(currentFamily, StringComparison.OrdinalIgnoreCase));
        var font = Choice("Write.Home.Font", "Font family", fontChoices, fontIndex);
        if (familyMixed) font.Accessibility.Description = "Selected text uses more than one font family.";
        font.SetValue(HavenProperties.MinWidth, HavenLength.Px(150));
        font.SelectionChanged += (_, _) => OwnOriginalCallback(() => { if (font.SelectedItem is { } family && family != "Mixed") { editor.SetFontFamily(family); RebuildRibbon(); } });
        RibbonContent.Add(font);

        var currentSize = sizeMixed ? "Mixed" : (selectedSize ?? 14).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        string[] sizeChoices = sizeMixed ? ["Mixed", .. CommonSizes]
            : CommonSizes.Contains(currentSize, StringComparer.Ordinal) ? CommonSizes : [currentSize, .. CommonSizes];
        var sizeIndex = Array.IndexOf(sizeChoices, currentSize);
        var size = Choice("Write.Home.Size", "Font size", sizeChoices, sizeIndex);
        if (sizeMixed) size.Accessibility.Description = "Selected text uses more than one font size.";
        size.SetValue(HavenProperties.MinWidth, HavenLength.Px(72));
        size.SelectionChanged += (_, _) => OwnOriginalCallback(() => { if (double.TryParse(size.SelectedItem, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var points)) { editor.SetFontSize(points); RebuildRibbon(); } });
        RibbonContent.Add(size);

        var bold = editor.SelectedCharacterFormat(WriteCharacterFormat.Bold);
        var italic = editor.SelectedCharacterFormat(WriteCharacterFormat.Italic);
        var underline = editor.SelectedCharacterFormat(WriteCharacterFormat.Underline);
        var strike = editor.SelectedCharacterFormat(WriteCharacterFormat.StrikeThrough);
        var boldMixed = editor.HasTextSelection && bold is null;
        var italicMixed = editor.HasTextSelection && italic is null;
        var underlineMixed = editor.HasTextSelection && underline is null;
        var strikeMixed = editor.HasTextSelection && strike is null;
        RibbonContent.Add(Format("Write.Home.Bold", "B", bold == true && !boldMixed, WriteCharacterFormat.Bold, boldMixed));
        RibbonContent.Add(Format("Write.Home.Italic", "I", italic == true && !italicMixed, WriteCharacterFormat.Italic, italicMixed));
        RibbonContent.Add(Format("Write.Home.Underline", "U", underline == true && !underlineMixed, WriteCharacterFormat.Underline, underlineMixed));
        RibbonContent.Add(Format("Write.Home.Strike", "S", strike == true && !strikeMixed, WriteCharacterFormat.StrikeThrough, strikeMixed));

        foreach (var colour in new[] { ("Black", "#FF1C222A"), ("Blue", "#FF246BCE"), ("Red", "#FFD64545"), ("Green", "#FF238B57") })
        {
            var selected = string.Equals(selectedForeground, colour.Item2, StringComparison.OrdinalIgnoreCase);
            var button = Btn("Write.Home.TextColour." + colour.Item1, colour.Item1,
                foregroundMixed ? ButtonVariant.Secondary : selected ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Accessibility.Description = "Set selected text colour to " + colour.Item1;
            if (foregroundMixed) button.Accessibility.Description += ". The current selection contains mixed text colours.";
            else if (selected) button.Accessibility.Description += ". This is the current selection colour.";
            button.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetForeground(colour.Item2); RebuildRibbon(); });
            RibbonContent.Add(button);
        }
        foreach (var colour in new[] { ("No highlight", "#00000000"), ("Yellow", "#FFFFE66D"), ("Mint", "#FFBCEFD0"), ("Pink", "#FFFFC8DD") })
        {
            var selected = string.Equals(selectedBackground, colour.Item2, StringComparison.OrdinalIgnoreCase);
            var button = Btn("Write.Home.Highlight." + colour.Item1.Replace(" ", string.Empty), colour.Item1,
                backgroundMixed ? ButtonVariant.Secondary : selected ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Accessibility.Description = "Set selected text highlight to " + colour.Item1;
            if (backgroundMixed) button.Accessibility.Description += ". The current selection contains mixed highlight colours.";
            else if (selected) button.Accessibility.Description += ". This is the current selection highlight.";
            button.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetBackground(colour.Item2); RebuildRibbon(); });
            RibbonContent.Add(button);
        }

        var selectedAlignment = editor.SelectedAlignment;
        var alignmentMixed = editor.HasTextSelection && selectedAlignment is null;
        foreach (var alignment in new[] { NotesTextAlignment.Left, NotesTextAlignment.Center, NotesTextAlignment.Right, NotesTextAlignment.Justify })
        {
            var button = Btn("Write.Align." + alignment, alignment.ToString(), alignmentMixed ? ButtonVariant.Secondary : selectedAlignment == alignment ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            if (alignmentMixed) button.Accessibility.Description = "Selected paragraphs use more than one alignment. Activating applies this alignment to all selected paragraphs.";
            button.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetAlignment(alignment); RebuildRibbon(); });
            RibbonContent.Add(button);
        }

        var spacingValues = new[] { "1.0", "1.15", "1.5", "2.0" };
        var currentSpacing = spacingMixed ? "Mixed" : (selectedSpacing ?? 1).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        string[] spacingChoices = spacingMixed ? ["Mixed", .. spacingValues]
            : spacingValues.Contains(currentSpacing, StringComparer.Ordinal) ? spacingValues : [currentSpacing, .. spacingValues];
        var spacing = Choice("Write.Home.LineSpacing", "Line spacing", spacingChoices, Array.IndexOf(spacingChoices, currentSpacing));
        if (spacingMixed) spacing.Accessibility.Description = "Selected paragraphs use more than one line spacing value.";
        spacing.SelectionChanged += (_, _) => OwnOriginalCallback(() => { if (double.TryParse(spacing.SelectedItem, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) { editor.SetLineSpacing(value); RebuildRibbon(); } });
        RibbonContent.Add(spacing);
        var outdent = Btn("Write.Home.Outdent", "Outdent"); outdent.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetLeftIndent(Math.Max(0, (block?.Paragraph.IndentLeft ?? 0) - 24)); }); RibbonContent.Add(outdent);
        var indent = Btn("Write.Home.Indent", "Indent"); indent.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetLeftIndent((block?.Paragraph.IndentLeft ?? 0) + 24); }); RibbonContent.Add(indent);
        AddContextualAuthoringControls(block);
    }

    private void InsertModern()
    {
        AddInsert("Paragraph", NotesBlockKind.Paragraph);
        AddInsert("Heading 1", NotesBlockKind.Heading, style: "heading-1");
        AddInsert("Heading 2", NotesBlockKind.Heading, style: "heading-2");
        AddInsert("Quote", NotesBlockKind.Quote);
        AddInsert("Bullets", NotesBlockKind.List, NotesListKind.Bulleted);
        AddInsert("Numbering", NotesBlockKind.List, NotesListKind.Numbered);
        AddInsert("Checklist", NotesBlockKind.List, NotesListKind.Checklist);
        AddTableButton(2, 2); AddTableButton(3, 3); AddTableButton(4, 4);
        var image = Btn("Write.Insert.Image", "Image"); image.Invoked += (_, _) => OwnOriginalCallback(() => { ImageRequested?.Invoke(this, EventArgs.Empty); }); RibbonContent.Add(image);
        var shape = Btn("Write.Insert.CustomShape", "Shape"); shape.Invoked += (_, _) => OwnOriginalCallback(() => { _editor?.InsertCustomShape(DocumentVectorShapes.CreateEditableStarter()); DocumentSurface.InvalidateDocument(); }); RibbonContent.Add(shape);
        AddSharedInkInsertion();
        AddInsert("Equation", NotesBlockKind.Equation);
        AddInsert("Divider", NotesBlockKind.Divider);
        var link = Field("Write.Insert.Link", "Link for selected text", "Paste link…");
        link.SetValue(HavenProperties.Width, HavenLength.Px(180));
        link.Invalidated += (_, _) => OwnOriginalCallback(() => { if (!_suppress && _editor?.HasDocumentSelection == true) _editor.SetLink(link.Text); });
        RibbonContent.Add(link);
    }

    private void AddTableButton(int rows, int columns)
    {
        var button = Btn($"Write.Insert.Table{rows}x{columns}", $"Table {rows}×{columns}");
        button.Invoked += (_, _) =>
        OwnOriginalCallback(() => {
            if (_editor is null) return;
            var block = _editor.InsertBlock(NotesBlockKind.Table);
            while (block.Table!.Rows.Count > rows) _editor.RemoveTableRow();
            while (block.Table.Rows.Count < rows) _editor.AddTableRow();
            while (block.Table.Rows[0].Cells.Count > columns) _editor.RemoveTableColumn();
            while (block.Table.Rows[0].Cells.Count < columns) _editor.AddTableColumn();
            DocumentSurface.InvalidateDocument();
        });
        RibbonContent.Add(button);
    }

    private void LayoutModern()
    {
        var editor = _editor!;
        var a4 = Btn("Write.Layout.A4", "A4"); a4.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetPagePreset("A4"); }); RibbonContent.Add(a4);
        var letter = Btn("Write.Layout.Letter", "Letter"); letter.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetPagePreset("Letter"); }); RibbonContent.Add(letter);
        foreach (var orientation in new[] { "Portrait", "Landscape" })
        {
            var button = Btn("Write.Layout." + orientation, orientation, editor.Document.PageSetup.Orientation.Equals(orientation, StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetOrientation(orientation); }); RibbonContent.Add(button);
        }
        var numbers = new Toggle { Name = "Write.Layout.PageNumbers", IsChecked = editor.Document.PageSetup.ShowPageNumbers }; numbers.Accessibility.AccessibleName = "Show page numbers"; numbers.CheckedChanged += (_, _) => OwnOriginalCallback(() => { if (!_suppress) editor.SetPageNumbers(numbers.IsChecked); }); RibbonContent.Add(numbers);
        foreach (var mode in new[] { NotesLayoutMode.Paginated, NotesLayoutMode.Continuous })
        {
            var button = Btn("Write.Layout.Mode." + mode, mode.ToString(), editor.Document.LayoutMode == mode ? ButtonVariant.Primary : ButtonVariant.Tertiary);
            button.Invoked += (_, _) => OwnOriginalCallback(() => { editor.SetLayout(mode); }); RibbonContent.Add(button);
        }
        var zoomOut = Btn("Write.Zoom.Out", "−"); zoomOut.Accessibility.AccessibleName = "Zoom out"; zoomOut.Invoked += (_, _) => OwnOriginalCallback(() => { DocumentSurface.SetZoom(DocumentSurface.Zoom - .1); }); RibbonContent.Add(zoomOut);
        RibbonContent.Add(Caption($"{Math.Round(DocumentSurface.Zoom * 100)}%"));
        var zoomIn = Btn("Write.Zoom.In", "+"); zoomIn.Accessibility.AccessibleName = "Zoom in"; zoomIn.Invoked += (_, _) => OwnOriginalCallback(() => { DocumentSurface.SetZoom(DocumentSurface.Zoom + .1); }); RibbonContent.Add(zoomIn);
        var fit = Btn("Write.Zoom.Fit", "100%"); fit.Invoked += (_, _) => OwnOriginalCallback(() => { DocumentSurface.SetZoom(1); }); RibbonContent.Add(fit);
    }
}
