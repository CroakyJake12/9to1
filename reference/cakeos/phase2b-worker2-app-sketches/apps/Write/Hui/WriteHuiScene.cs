using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Write.Hui;

public enum WriteHuiAction
{
    NewDocument,
    OpenDocument,
    SaveDocument,
    SaveAsDocument,
    PrintDocument,
    Undo,
    Redo,
    Bold,
    Italic,
    Underline,
    IncreaseFontSize,
    DecreaseFontSize,
    AlignLeft,
    AlignCenter,
    AlignRight,
    AlignJustify,
}

public sealed class WriteHuiScene
{
    private readonly Queue<WriteHuiAction> _actions = new();

    public WriteHuiScene()
    {
        Root = new Page
        {
            Name = "Write.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");

        // Menu bar
        var menuBar = new HuiContainer { Name = "Write.Hui.MenuBar", Layout = HavenLayout.Horizontal };
        menuBar.SetValue(HavenProperties.Row, 0);
        menuBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        menuBar.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        menuBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        menuBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        menuBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        menuBar.SetValue(HavenProperties.BorderBottom, HavenLength.Px(1));

        var fileMenu = NewMenuButton("File", new[]
        {
            ("New", WriteHuiAction.NewDocument),
            ("Open", WriteHuiAction.OpenDocument),
            ("Save", WriteHuiAction.SaveDocument),
            ("Save As", WriteHuiAction.SaveAsDocument),
            ("Print", WriteHuiAction.PrintDocument),
        });
        var editMenu = NewMenuButton("Edit", new[]
        {
            ("Undo", WriteHuiAction.Undo),
            ("Redo", WriteHuiAction.Redo),
        });
        var formatMenu = NewMenuButton("Format", new[]
        {
            ("Bold", WriteHuiAction.Bold),
            ("Italic", WriteHuiAction.Italic),
            ("Underline", WriteHuiAction.Underline),
        });

        menuBar.Add(fileMenu);
        menuBar.Add(editMenu);
        menuBar.Add(formatMenu);
        Root.Add(menuBar);

        // Toolbar
        var toolbar = new HuiContainer { Name = "Write.Hui.Toolbar", Layout = HavenLayout.Horizontal };
        toolbar.SetValue(HavenProperties.Row, 1);
        toolbar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        toolbar.SetValue(HavenProperties.Background, "SurfaceVariant");
        toolbar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        toolbar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        toolbar.SetValue(HavenProperties.BorderBottom, HavenLength.Px(1));

        BoldButton = NewFormatButton("Write.Hui.Bold", "B", WriteHuiAction.Bold, true);
        ItalicButton = NewFormatButton("Write.Hui.Italic", "I", WriteHuiAction.Italic, true);
        UnderlineButton = NewFormatButton("Write.Hui.Underline", "U", WriteHuiAction.Underline, true);

        toolbar.Add(new HuiContainer { Name = "Write.Hui.Sep1" }); // separator
        toolbar.Add(BoldButton);
        toolbar.Add(ItalicButton);
        toolbar.Add(UnderlineButton);

        toolbar.Add(new HuiContainer { Name = "Write.Hui.Sep2" });

        AlignLeftButton = NewActionButton("Write.Hui.AlignLeft", "◰", WriteHuiAction.AlignLeft);
        AlignCenterButton = NewActionButton("Write.Hui.AlignCenter", "◱", WriteHuiAction.AlignCenter);
        AlignRightButton = NewActionButton("Write.Hui.AlignRight", "◲", WriteHuiAction.AlignRight);
        AlignJustifyButton = NewActionButton("Write.Hui.AlignJustify", "◳", WriteHuiAction.AlignJustify);

        toolbar.Add(AlignLeftButton);
        toolbar.Add(AlignCenterButton);
        toolbar.Add(AlignRightButton);
        toolbar.Add(AlignJustifyButton);

        toolbar.Add(new HuiContainer { Name = "Write.Hui.Sep3" });

        FontSizeDecButton = NewActionButton("Write.Hui.FontSizeDec", "A-", WriteHuiAction.DecreaseFontSize);
        FontSizeIncButton = NewActionButton("Write.Hui.FontSizeInc", "A+", WriteHuiAction.IncreaseFontSize);

        toolbar.Add(FontSizeDecButton);
        toolbar.Add(FontSizeIncButton);

        Root.Add(toolbar);

        // Document area
        DocumentArea = new HuiContainer { Name = "Write.Hui.DocumentArea", Layout = HavenLayout.Absolute };
        DocumentArea.SetValue(HavenProperties.Row, 2);
        DocumentArea.SetValue(HavenProperties.Background, "Surface");
        DocumentArea.SetValue(HavenProperties.Padding, HavenThickness.Parse("48px 72px")); // A4 margins ~1 inch
        DocumentArea.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        
        DocumentContent = new HuiText("") { Name = "Write.Hui.DocumentContent", Level = TextLevel.Body1 };
        DocumentContent.SetValue(HavenProperties.FontFamily, "Serif");
        DocumentContent.SetValue(HavenProperties.LineHeight, HavenLength.Px(24));
        DocumentArea.Add(DocumentContent);
        Root.Add(DocumentArea);

        // Status bar
        var statusBar = new HuiContainer { Name = "Write.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 3);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        PageInfoText = new HuiText("Page 1") { Name = "Write.Hui.PageInfo", Level = TextLevel.Caption };
        PageInfoText.SetValue(HavenProperties.Foreground, "TextSecondary");
        WordCountText = new HuiText("Words: 0") { Name = "Write.Hui.WordCount", Level = TextLevel.Caption };
        WordCountText.SetValue(HavenProperties.Foreground, "TextSecondary");
        CursorPosText = new HuiText("Ln 1, Col 1") { Name = "Write.Hui.CursorPos", Level = TextLevel.Caption };
        CursorPosText.SetValue(HavenProperties.Foreground, "TextSecondary");
        ZoomText = new HuiText("100%") { Name = "Write.Hui.Zoom", Level = TextLevel.Caption };
        ZoomText.SetValue(HavenProperties.Foreground, "TextSecondary");

        statusBar.Add(PageInfoText);
        statusBar.Add(new HuiContainer { Name = "Write.Hui.Spacer" });
        statusBar.Add(WordCountText);
        statusBar.Add(CursorPosText);
        statusBar.Add(ZoomText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
    }

    public Page Root { get; }
    public HuiContainer DocumentArea { get; }
    public HuiText DocumentContent { get; }
    public HuiButton BoldButton { get; }
    public HuiButton ItalicButton { get; }
    public HuiButton UnderlineButton { get; }
    public HuiButton AlignLeftButton { get; }
    public HuiButton AlignCenterButton { get; }
    public HuiButton AlignRightButton { get; }
    public HuiButton AlignJustifyButton { get; }
    public HuiButton FontSizeDecButton { get; }
    public HuiButton FontSizeIncButton { get; }
    public HuiText PageInfoText { get; }
    public HuiText WordCountText { get; }
    public HuiText CursorPosText { get; }
    public HuiText ZoomText { get; }

    public bool TryDequeueAction(out WriteHuiAction action) => _actions.TryDequeue(out action);

    public void SetDocumentContent(string content)
    {
        DocumentContent.Content = content;
    }

    public void AppendDocumentContent(string content)
    {
        DocumentContent.Content += content;
    }

    public void SetPageInfo(int currentPage, int totalPages)
    {
        PageInfoText.Content = $"Page {currentPage} of {totalPages}";
    }

    public void SetWordCount(int count)
    {
        WordCountText.Content = $"Words: {count}";
    }

    public void SetCursorPosition(int line, int column)
    {
        CursorPosText.Content = $"Ln {line}, Col {column}";
    }

    public void SetZoom(int percent)
    {
        ZoomText.Content = $"{percent}%";
    }

    public void SetFormatState(bool bold, bool italic, bool underline)
    {
        BoldButton.SetState(HavenElementState.Selected, bold);
        ItalicButton.SetState(HavenElementState.Selected, italic);
        UnderlineButton.SetState(HavenElementState.Selected, underline);
    }

    private HuiContainer NewMenuButton(string label, (string, WriteHuiAction)[] items)
    {
        var container = new HuiContainer { Name = $"Write.Hui.Menu.{label}", Layout = HavenLayout.Horizontal };
        container.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        
        var button = new HuiButton
        {
            Name = $"Write.Hui.MenuBtn.{label}",
            Content = label,
            Variant = ButtonVariant.Ghost,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        
        // For now, just use the first action
        if (items.Length > 0)
        {
            var action = items[0].Item2;
            button.Invoked += (_, _) => _actions.Enqueue(action);
        }
        
        container.Add(button);
        return container;
    }

    private HuiButton NewFormatButton(string name, string content, WriteHuiAction action, bool toggle)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Ghost,
        };
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.SetValue(HavenProperties.FontWeight, toggle ? "Bold" : "Normal");
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }

    private HuiButton NewActionButton(string name, string content, WriteHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Ghost,
        };
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(32));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }
}

public sealed class WriteHuiController(WriteEngine engine, WriteHuiScene? scene = null)
{
    private readonly WriteEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private string? _currentDocumentPath;

    public WriteHuiScene Scene { get; } = scene ?? new WriteHuiScene();

    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            _currentDocumentPath = path;
            _engine.Open(path);
            RefreshDocument();
            Scene.SetPageInfo(1, _engine.GetPages().Count);
            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public void NewDocument()
    {
        _currentDocumentPath = null;
        _engine.Open(""); // Would create new document
        Scene.SetDocumentContent("");
        Scene.SetWordCount(0);
        Scene.SetPageInfo(1, 1);
    }

    public void Save()
    {
        if (!string.IsNullOrEmpty(_currentDocumentPath))
        {
            _engine.Save();
        }
    }

    public void SaveAs(string path)
    {
        _engine.SaveAs(path);
        _currentDocumentPath = path;
    }

    public async Task ExecuteAsync(WriteHuiAction action)
    {
        try
        {
            switch (action)
            {
                case WriteHuiAction.NewDocument:
                    NewDocument();
                    break;
                case WriteHuiAction.OpenDocument:
                    // Would open file picker
                    break;
                case WriteHuiAction.SaveDocument:
                    Save();
                    break;
                case WriteHuiAction.SaveAsDocument:
                    // Would open file picker
                    break;
                case WriteHuiAction.PrintDocument:
                    _engine.Print();
                    break;
                case WriteHuiAction.Undo:
                    _engine.Undo();
                    break;
                case WriteHuiAction.Redo:
                    _engine.Redo();
                    break;
                case WriteHuiAction.Bold:
                    // Apply bold to selection
                    break;
                case WriteHuiAction.Italic:
                    // Apply italic to selection
                    break;
                case WriteHuiAction.Underline:
                    // Apply underline to selection
                    break;
                case WriteHuiAction.IncreaseFontSize:
                    // Increase font size
                    break;
                case WriteHuiAction.DecreaseFontSize:
                    // Decrease font size
                    break;
                case WriteHuiAction.AlignLeft:
                case WriteHuiAction.AlignCenter:
                case WriteHuiAction.AlignRight:
                case WriteHuiAction.AlignJustify:
                    // Apply alignment
                    break;
            }
            RefreshDocument();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    private void RefreshDocument()
    {
        if (!_engine.IsOpen()) return;
        var info = _engine.GetDocumentInfo();
        Scene.SetPageInfo(1, info.PageCount);
        // Would get actual text content
    }
}