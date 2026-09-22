// BoardsViewModel bridges the CUI Boards surface and IRichBoardSession.
// It is both the ICuiBindingContext (all {Binding ...} paths in Boards.cui)
// and the ICuiActionDispatcher (all action="..." commands in Boards.cui).

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using CakeOS.Cui;

namespace CakeOS.Apps.Boards.App;

public sealed class BoardsViewModel : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    private readonly IRichBoardSession _session;
    private readonly Dictionary<string, object?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedSectionId = "section-1";
    private string _selectedPageId = "page-1";
    private string _selectedBlockId = "block-para";
    private bool _syncing;

    public BoardsViewModel(IRichBoardSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.StatusChanged += (_, status) => Set("StatusText", status);
        PushDocumentToBindings();
        Set("StatusText", _session.Status);
    }

    public IRichBoardSession Session => _session;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Set(string property, object? value, [CallerMemberName] string? caller = null)
    {
        _properties[property] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    public object? Get(string property) =>
        _properties.TryGetValue(property, out var val) ? val : null;

    public bool TryGetValue(string path, out object? value)
    {
        var lastDot = path.LastIndexOf('.');
        var key = lastDot >= 0 ? path[(lastDot + 1)..] : path;
        return _properties.TryGetValue(key, out value);
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case "NewBoard":
                _session.Document.Title = "Untitled board";
                _session.Document.Sections.Clear();
                _session.Document.Sections.Add(InMemoryRichBoardSession.CreateDefault().Sections[0]);
                ReselectDefaults();
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "OpenBoard":
                await _session.OpenAsync(_session.FilePath, cancellationToken);
                ReselectDefaults();
                PushDocumentToBindings();
                break;
            case "SaveBoard":
                await _session.SaveAsync(cancellationToken);
                Set("StatusText", _session.Status);
                break;
            case "SaveAsBoard":
                var basePath = _session.FilePath ?? "memory://boards/default";
                var copyPath = basePath.EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase)
                    ? basePath[..^".9to1board".Length] + " (copy).9to1board"
                    : basePath + "-copy";
                await _session.SaveAsAsync(copyPath, cancellationToken);
                PushDocumentToBindings();
                break;
            case "AddBlock":
                CurrentPage().Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = "New block" });
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "AddSection":
                var section = new RichBoardSection { Title = $"Section {_session.Document.Sections.Count + 1}" };
                section.Pages.Add(new RichBoardPage { Title = "New page" });
                _session.Document.Sections.Add(section);
                _selectedSectionId = section.Id;
                _selectedPageId = section.Pages[0].Id;
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "AddPage":
                var page = new RichBoardPage { Title = $"Page {CurrentSection().Pages.Count + 1}" };
                CurrentSection().Pages.Add(page);
                _selectedPageId = page.Id;
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "ToggleBold":
                SelectedBlock().Bold = !SelectedBlock().Bold;
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "ToggleItalic":
                SelectedBlock().Italic = !SelectedBlock().Italic;
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "ToggleUnderline":
                SelectedBlock().Underline = !SelectedBlock().Underline;
                PushDocumentToBindings();
                _session.MarkDirty();
                break;
            case "AddInkDot":
                AddInkStroke();
                break;
            case "SelectSection1":
                if (_session.Document.Sections.Count > 0)
                {
                    _selectedSectionId = _session.Document.Sections[0].Id;
                    _selectedPageId = CurrentSection().Pages.Count > 0 ? CurrentSection().Pages[0].Id : _selectedPageId;
                    PushDocumentToBindings();
                }
                break;
            case "SelectSection2":
                if (_session.Document.Sections.Count > 1)
                {
                    _selectedSectionId = _session.Document.Sections[1].Id;
                    _selectedPageId = CurrentSection().Pages.Count > 0 ? CurrentSection().Pages[0].Id : _selectedPageId;
                    PushDocumentToBindings();
                }
                break;
            case "SelectPage1":
                if (CurrentSection().Pages.Count > 0)
                {
                    _selectedPageId = CurrentSection().Pages[0].Id;
                    PushDocumentToBindings();
                }
                break;
            case "SelectPage2":
                if (CurrentSection().Pages.Count > 1)
                {
                    _selectedPageId = CurrentSection().Pages[1].Id;
                    PushDocumentToBindings();
                }
                break;
        }
    }

    /// <summary>
    /// Wire live editing: subscribes to named TextBox/CheckBox controls built
    /// by CuiControlLoader so multiline edits update session state (dirty +
    /// debounced autosave). The loader itself only pushes one-way bindings.
    /// </summary>
    public void Attach(Control root)
    {
        foreach (var textBox in FindControls<TextBox>(root))
        {
            var name = textBox.Name ?? string.Empty;
            textBox.TextChanged += (_, _) =>
            {
                if (_syncing)
                    return;
                OnTextEdited(name, textBox.Text ?? string.Empty);
            };
        }
        foreach (var checkBox in FindControls<CheckBox>(root))
        {
            var name = checkBox.Name ?? string.Empty;
            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (_syncing)
                    return;
                OnCheckEdited(name, checkBox.IsChecked == true);
            };
        }
        SyncControlValues(root);
    }

    /// <summary>Push current binding values into live controls without re-firing edits.</summary>
    public void SyncControlValues(Control root)
    {
        _syncing = true;
        try
        {
            foreach (var textBox in FindControls<TextBox>(root))
            {
                var value = Get(BindingForControl(textBox.Name ?? string.Empty))?.ToString();
                if (value is not null && textBox.Text != value)
                    textBox.Text = value;
            }
            foreach (var checkBox in FindControls<CheckBox>(root))
            {
                var value = Get(BindingForControl(checkBox.Name ?? string.Empty))?.ToString();
                if (value is not null && bool.TryParse(value, out var done) && checkBox.IsChecked != done)
                    checkBox.IsChecked = done;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void AddInkStroke(double x = 60, double y = 40)
    {
        if (_session is ContractSessionAdapter adapter)
        {
            _ = CommitSampleInkAsync(adapter);
            return;
        }
        var ink = CurrentPage().Blocks.FirstOrDefault(b => b.Kind == "ink");
        if (ink is null)
        {
            ink = new RichBoardBlock { Kind = "ink", Text = "Sketch" };
            CurrentPage().Blocks.Add(ink);
        }
        ink.InkStrokeCount++;
        PushDocumentToBindings();
        _session.MarkDirty();
    }

    /// <summary>Commits one pointer-drawn stroke as real persisted ink data.</summary>
    public async ValueTask CommitInkStrokeAsync(IReadOnlyList<(double X, double Y)> points)
    {
        if (_session is ContractSessionAdapter adapter)
        {
            await adapter.CommitInkStrokeAsync(points);
            PushDocumentToBindings();
            return;
        }
        AddInkStroke();
    }

    private async Task CommitSampleInkAsync(ContractSessionAdapter adapter)
    {
        await adapter.AddSampleInkStrokeAsync();
        PushDocumentToBindings();
    }

    private void OnTextEdited(string controlName, string text)
    {
        switch (controlName)
        {
            case "BoardTitleBox":
                _session.Document.Title = text;
                Set("BoardTitle", text);
                break;
            case "HeadingBox":
                FindBlock("heading").Text = text;
                Set("HeadingText", text);
                break;
            case "ParaBox":
                FindBlock("paragraph").Text = text;
                Set("ParaText", text);
                _selectedBlockId = FindBlock("paragraph").Id;
                break;
            case "Check1Box":
                FindChecklist(0).Text = text;
                Set("Check1Text", text);
                break;
            case "Check2Box":
                FindChecklist(1).Text = text;
                Set("Check2Text", text);
                break;
            case "Cell00": SetTableCell("0,0", text); Set("Cell00", text); break;
            case "Cell01": SetTableCell("0,1", text); Set("Cell01", text); break;
            case "Cell10": SetTableCell("1,0", text); Set("Cell10", text); break;
            case "Cell11": SetTableCell("1,1", text); Set("Cell11", text); break;
            default:
                return;
        }
        _session.MarkDirty();
    }

    private void OnCheckEdited(string controlName, bool done)
    {
        switch (controlName)
        {
            case "Check1Done": FindChecklist(0).IsChecked = done; Set("Check1Done", done); break;
            case "Check2Done": FindChecklist(1).IsChecked = done; Set("Check2Done", done); break;
            default: return;
        }
        _session.MarkDirty();
    }

    private static string BindingForControl(string controlName) => controlName switch
    {
        "BoardTitleBox" => "BoardTitle",
        "HeadingBox" => "HeadingText",
        "ParaBox" => "ParaText",
        "Check1Box" => "Check1Text",
        "Check1Done" => "Check1Done",
        "Check2Box" => "Check2Text",
        "Check2Done" => "Check2Done",
        "Cell00" => "Cell00",
        "Cell01" => "Cell01",
        "Cell10" => "Cell10",
        "Cell11" => "Cell11",
        _ => controlName,
    };

    private void PushDocumentToBindings()
    {
        var doc = _session.Document;
        var page = CurrentPage();
        var heading = page.Blocks.FirstOrDefault(b => b.Kind == "heading");
        var para = page.Blocks.FirstOrDefault(b => b.Kind == "paragraph");
        var checks = page.Blocks.Where(b => b.Kind == "checklist").ToList();
        var table = page.Blocks.FirstOrDefault(b => b.Kind == "table");
        var ink = page.Blocks.FirstOrDefault(b => b.Kind == "ink");
        var selected = page.Blocks.FirstOrDefault(b => b.Id == _selectedBlockId) ?? para ?? heading;

        if (selected is not null)
            _selectedBlockId = selected.Id;

        _syncing = true;
        try
        {
            Set("BoardTitle", doc.Title);
            Set("FilePath", _session.FilePath ?? "memory://boards/default");
            Set("StatusText", _session.Status);
            Set("SectionCount", doc.Sections.Count.ToString());
            Set("PageCount", CurrentSection().Pages.Count.ToString());
            Set("SectionListText", string.Join("  |  ", doc.Sections.Select(s => s.Title)));
            Set("PageListText", string.Join("  |  ", CurrentSection().Pages.Select(p => p.Title)));
            Set("PageTitle", page.Title);
            Set("HeadingText", heading?.Text ?? string.Empty);
            Set("ParaText", para?.Text ?? string.Empty);
            Set("Check1Text", checks.Count > 0 ? checks[0].Text : string.Empty);
            Set("Check1Done", checks.Count > 0 && checks[0].IsChecked);
            Set("Check2Text", checks.Count > 1 ? checks[1].Text : string.Empty);
            Set("Check2Done", checks.Count > 1 && checks[1].IsChecked);
            Set("Cell00", TableCell(table, "0,0"));
            Set("Cell01", TableCell(table, "0,1"));
            Set("Cell10", TableCell(table, "1,0"));
            Set("Cell11", TableCell(table, "1,1"));
            Set("InkSummary", ink is null ? "No ink yet" : $"{ink.InkStrokeCount} stroke(s) — {ink.Text}");
            Set("SelectedBlockInfo", selected is null ? "No block" : $"{selected.Kind}: {(selected.Text.Length > 40 ? selected.Text[..40] : selected.Text)}");
            Set("BoldState", selected?.Bold == true ? "On" : "Off");
            Set("ItalicState", selected?.Italic == true ? "On" : "Off");
            Set("UnderlineState", selected?.Underline == true ? "On" : "Off");
        }
        finally
        {
            _syncing = false;
        }
    }

    private RichBoardSection CurrentSection() =>
        _session.Document.Sections.FirstOrDefault(s => s.Id == _selectedSectionId)
        ?? _session.Document.Sections.FirstOrDefault()
        ?? throw new InvalidOperationException("Board has no sections.");

    private RichBoardPage CurrentPage()
    {
        var section = CurrentSection();
        return section.Pages.FirstOrDefault(p => p.Id == _selectedPageId)
            ?? section.Pages.FirstOrDefault()
            ?? throw new InvalidOperationException("Section has no pages.");
    }

    private RichBoardBlock SelectedBlock() =>
        CurrentPage().Blocks.FirstOrDefault(b => b.Id == _selectedBlockId)
        ?? CurrentPage().Blocks.FirstOrDefault()
        ?? throw new InvalidOperationException("Page has no blocks.");

    private RichBoardBlock FindBlock(string kind)
    {
        var page = CurrentPage();
        var block = page.Blocks.FirstOrDefault(b => b.Kind == kind);
        if (block is null)
        {
            block = new RichBoardBlock { Kind = kind };
            page.Blocks.Add(block);
        }
        return block;
    }

    private RichBoardBlock FindChecklist(int index)
    {
        var page = CurrentPage();
        var checks = page.Blocks.Where(b => b.Kind == "checklist").ToList();
        while (checks.Count <= index)
        {
            var added = new RichBoardBlock { Kind = "checklist", Text = $"Item {checks.Count + 1}" };
            page.Blocks.Add(added);
            checks.Add(added);
        }
        return checks[index];
    }

    private void SetTableCell(string key, string value)
    {
        var table = FindBlock("table");
        table.TableCells[key] = value;
    }

    private static string TableCell(RichBoardBlock? table, string key) =>
        table is not null && table.TableCells.TryGetValue(key, out var value) ? value : string.Empty;

    private void ReselectDefaults()
    {
        var firstSection = _session.Document.Sections.FirstOrDefault();
        if (firstSection is null)
            return;
        _selectedSectionId = firstSection.Id;
        _selectedPageId = firstSection.Pages.FirstOrDefault()?.Id ?? _selectedPageId;
        var para = firstSection.Pages.FirstOrDefault()?.Blocks.FirstOrDefault(b => b.Kind == "paragraph");
        if (para is not null)
            _selectedBlockId = para.Id;
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        if (root is T match)
            yield return match;
        foreach (var child in LogicalChildren(root))
            foreach (var nested in FindControls<T>(child))
                yield return nested;
    }

    private static IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc)
        {
            if (cc.Content is Control ccChild)
                yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
    }
}
