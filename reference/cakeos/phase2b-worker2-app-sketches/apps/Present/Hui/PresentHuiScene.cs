using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Present.Hui;

public enum PresentHuiAction
{
    NewSlide,
    DuplicateSlide,
    DeleteSlide,
    PreviousSlide,
    NextSlide,
    StartSlideshow,
    EndSlideshow,
    Save,
    Undo,
    Redo,
}

public sealed class PresentHuiScene
{
    private readonly HuiButton[,] _slideThumbnails;
    private readonly Queue<PresentHuiAction> _actions = new();
    private int _selectedSlideIndex = 0;
    private int _slideCount = 0;
    private bool _isSlideshowMode = false;

    public PresentHuiScene(int maxSlides = 20)
    {
        _slideThumbnails = new HuiButton[maxSlides, 1];

        Root = new Page
        {
            Name = "Present.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "240px 1fr",
            Rows = "Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");

        // Sidebar with slide thumbnails
        var sidebar = new HuiContainer { Name = "Present.Hui.Sidebar", Layout = HavenLayout.Vertical };
        sidebar.SetValue(HavenProperties.Row, 1);
        sidebar.SetValue(HavenProperties.Column, 0);
        sidebar.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebar.SetValue(HavenProperties.Background, "SurfaceVariant");
        sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        sidebar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        sidebar.SetValue(HavenProperties.BorderRight, HavenLength.Px(1));

        var sidebarHeader = new HuiContainer { Name = "Present.Hui.SidebarHeader", Layout = HavenLayout.Horizontal };
        sidebarHeader.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebarHeader.Add(new HuiText("Slides") { Name = "Present.Hui.SlidesTitle", Level = TextLevel.H3 });
        SidebarAddButton = NewActionButton("Present.Hui.AddSlide", "+", PresentHuiAction.NewSlide);
        sidebarHeader.Add(SidebarAddButton);
        sidebar.Add(sidebarHeader);

        ThumbnailContainer = new HuiContainer { Name = "Present.Hui.Thumbnails", Layout = HavenLayout.Vertical };
        ThumbnailContainer.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        ThumbnailContainer.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        for (int i = 0; i < maxSlides; i++)
        {
            var thumb = new HuiButton
            {
                Name = $"Present.Hui.Thumb.{i}",
                Content = $"Slide {i + 1}",
                Variant = ButtonVariant.Ghost,
            };
            thumb.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
            thumb.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            thumb.SetValue(HavenProperties.BorderColor, "Transparent");
            int captured = i;
            thumb.Invoked += (_, _) => SelectSlide(captured);
            _slideThumbnails[i, 0] = thumb;
            ThumbnailContainer.Add(thumb);
        }
        sidebar.Add(ThumbnailContainer);
        Root.Add(sidebar);

        // Main content area
        var mainArea = new HuiContainer { Name = "Present.Hui.MainArea", Layout = HavenLayout.Grid };
        mainArea.SetValue(HavenProperties.Row, 1);
        mainArea.SetValue(HavenProperties.Column, 1);
        mainArea.SetValue(HavenProperties.Columns, "1fr");
        mainArea.SetValue(HavenProperties.Rows, "Auto 1fr Auto");
        mainArea.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px"));
        mainArea.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        // Top toolbar
        var topToolbar = new HuiContainer { Name = "Present.Hui.TopToolbar", Layout = HavenLayout.Horizontal };
        topToolbar.SetValue(HavenProperties.Row, 0);
        topToolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        topToolbar.SetValue(HavenProperties.Alignment, HavenAlignment.End);

        UndoButton = NewActionButton("Present.Hui.Undo", "Undo", PresentHuiAction.Undo);
        RedoButton = NewActionButton("Present.Hui.Redo", "Redo", PresentHuiAction.Redo);
        topToolbar.Add(UndoButton);
        topToolbar.Add(RedoButton);
        mainArea.Add(topToolbar);

        // Slide canvas area
        SlideCanvas = new HuiContainer { Name = "Present.Hui.Canvas", Layout = HavenLayout.Absolute };
        SlideCanvas.SetValue(HavenProperties.Row, 1);
        SlideCanvas.SetValue(HavenProperties.Background, "SurfaceContainer");
        SlideCanvas.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SlideCanvas.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        SlideCanvas.SetValue(HavenProperties.BorderRadius, HavenLength.Px(4));
        SlideCanvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(400));
        mainArea.Add(SlideCanvas);

        // Bottom toolbar
        var bottomToolbar = new HuiContainer { Name = "Present.Hui.BottomToolbar", Layout = HavenLayout.Horizontal };
        bottomToolbar.SetValue(HavenProperties.Row, 2);
        bottomToolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        bottomToolbar.SetValue(HavenProperties.Alignment, HavenAlignment.Center);

        PrevSlideButton = NewActionButton("Present.Hui.PrevSlide", "Previous", PresentHuiAction.PreviousSlide);
        NextSlideButton = NewActionButton("Present.Hui.NextSlide", "Next", PresentHuiAction.NextSlide);
        DuplicateButton = NewActionButton("Present.Hui.Duplicate", "Duplicate", PresentHuiAction.DuplicateSlide);
        DeleteButton = NewActionButton("Present.Hui.Delete", "Delete", PresentHuiAction.DeleteSlide);
        SlideshowButton = NewActionButton("Present.Hui.Slideshow", "Start Slideshow", PresentHuiAction.StartSlideshow);
        SaveButton = NewActionButton("Present.Hui.Save", "Save", PresentHuiAction.Save);

        bottomToolbar.Add(PrevSlideButton);
        bottomToolbar.Add(NextSlideButton);
        bottomToolbar.Add(DuplicateButton);
        bottomToolbar.Add(DeleteButton);
        bottomToolbar.Add(SlideshowButton);
        bottomToolbar.Add(SaveButton);
        mainArea.Add(bottomToolbar);

        Root.Add(mainArea);

        // Status bar
        var statusBar = new HuiContainer { Name = "Present.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 2);
        statusBar.SetValue(HavenProperties.ColumnSpan, 2);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 24px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        StatusText = new HuiText("No presentation open") { Name = "Present.Hui.Status", Level = TextLevel.Body2 };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        SlideCounterText = new HuiText("Slide 0 / 0") { Name = "Present.Hui.SlideCounter", Level = TextLevel.Caption };
        SlideCounterText.SetValue(HavenProperties.Foreground, "TextSecondary");
        statusBar.Add(StatusText);
        statusBar.Add(new HuiContainer { Name = "Present.Hui.Spacer" }); // spacer
        statusBar.Add(SlideCounterText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
        ApplySelectionState();
    }

    public Page Root { get; }
    public HuiContainer ThumbnailContainer { get; }
    public HuiContainer SlideCanvas { get; }
    public HuiButton SidebarAddButton { get; }
    public HuiButton UndoButton { get; }
    public HuiButton RedoButton { get; }
    public HuiButton PrevSlideButton { get; }
    public HuiButton NextSlideButton { get; }
    public HuiButton DuplicateButton { get; }
    public HuiButton DeleteButton { get; }
    public HuiButton SlideshowButton { get; }
    public HuiButton SaveButton { get; }
    public HuiText StatusText { get; }
    public HuiText SlideCounterText { get; }
    public int SelectedSlideIndex => _selectedSlideIndex;
    public int SlideCount => _slideCount;
    public bool IsSlideshowMode => _isSlideshowMode;

    public HuiButton SlideThumbnail(int index)
    {
        if (index < 0 || index >= _slideThumbnails.GetLength(0))
            throw new ArgumentOutOfRangeException(nameof(index));
        return _slideThumbnails[index, 0];
    }

    public bool TryDequeueAction(out PresentHuiAction action) => _actions.TryDequeue(out action);

    public void UpdateSlides(IReadOnlyList<string> slideNames, int currentIndex)
    {
        _slideCount = slideNames.Count;
        _selectedSlideIndex = Math.Clamp(currentIndex, 0, Math.Max(0, _slideCount - 1));

        for (int i = 0; i < _slideThumbnails.GetLength(0); i++)
        {
            var thumb = _slideThumbnails[i, 0];
            if (i < _slideCount)
            {
                thumb.Content = slideNames[i];
                thumb.SetState(HavenElementState.Disabled, false);
                thumb.SetValue(HavenProperties.Opacity, 1d);
            }
            else
            {
                thumb.Content = i == _slideCount ? "+ New Slide" : "";
                thumb.SetState(HavenElementState.Disabled, i > _slideCount);
                thumb.SetValue(HavenProperties.Opacity, i == _slideCount ? 0.6d : 0.2d);
            }
        }

        SlideCounterText.Content = $"Slide {_selectedSlideIndex + 1} / {_slideCount}";
        ApplySelectionState();
        UpdateButtonStates();
    }

    public void SetSlideshowMode(bool enabled)
    {
        _isSlideshowMode = enabled;
        SlideshowButton.Content = enabled ? "End Slideshow" : "Start Slideshow";
        SlideshowButton.SetState(HavenElementState.Selected, enabled);
        UpdateButtonStates();
    }

    public void SetStatus(string status)
    {
        StatusText.Content = status;
    }

    private void SelectSlide(int index)
    {
        if (index < 0 || index >= _slideCount) return;
        _selectedSlideIndex = index;
        ApplySelectionState();
        SlideCounterText.Content = $"Slide {_selectedSlideIndex + 1} / {_slideCount}";
    }

    private void ApplySelectionState()
    {
        for (int i = 0; i < _slideCount; i++)
        {
            var thumb = _slideThumbnails[i, 0];
            var selected = i == _selectedSlideIndex;
            thumb.SetState(HavenElementState.Selected, selected);
            thumb.SetValue(HavenProperties.BorderColor, selected ? "Accent" : "Transparent");
            thumb.SetValue(HavenProperties.BorderWidth, HavenLength.Px(selected ? 2 : 1));
            thumb.SetValue(HavenProperties.Background, selected ? "AccentContainer" : "Transparent");
        }
    }

    private void UpdateButtonStates()
    {
        bool hasSlides = _slideCount > 0;
        bool canNavigate = hasSlides && !_isSlideshowMode;
        bool canEdit = hasSlides && !_isSlideshowMode;

        PrevSlideButton.SetState(HavenElementState.Disabled, !canNavigate || _selectedSlideIndex == 0);
        NextSlideButton.SetState(HavenElementState.Disabled, !canNavigate || _selectedSlideIndex >= _slideCount - 1);
        DuplicateButton.SetState(HavenElementState.Disabled, !canEdit);
        DeleteButton.SetState(HavenElementState.Disabled, !canEdit || _slideCount <= 1);
        SaveButton.SetState(HavenElementState.Disabled, !hasSlides);
        UndoButton.SetState(HavenElementState.Disabled, !canEdit);
        RedoButton.SetState(HavenElementState.Disabled, !canEdit);
    }

    private HuiButton NewActionButton(string name, string content, PresentHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(80));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }
}

public sealed class PresentHuiController(PresentEngine engine, PresentHuiScene? scene = null)
{
    private readonly PresentEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private string? _currentDocumentPath;

    public PresentHuiScene Scene { get; } = scene ?? new PresentHuiScene();

    public async Task<bool> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            _currentDocumentPath = path;
            _engine.Open(path);
            RefreshScene();
            Scene.SetStatus($"Opened: {System.IO.Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Failed to open: {ex.Message}");
            return false;
        }
    }

    public void Close()
    {
        try
        {
            _engine.Close();
            _currentDocumentPath = null;
            Scene.UpdateSlides([], 0);
            Scene.SetStatus("No presentation open");
        }
        catch
        {
            // Ignore close errors
        }
    }

    public async Task ExecuteAsync(PresentHuiAction action, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (action)
            {
                case PresentHuiAction.NewSlide:
                    _engine.AddSlideAfter(Scene.SelectedSlideIndex);
                    break;

                case PresentHuiAction.DuplicateSlide:
                    if (Scene.SlideCount > 0)
                        _engine.DuplicateSlide(Scene.SelectedSlideIndex);
                    break;

                case PresentHuiAction.DeleteSlide:
                    if (Scene.SlideCount > 1)
                        _engine.DeleteSlide(Scene.SelectedSlideIndex);
                    break;

                case PresentHuiAction.PreviousSlide:
                    if (Scene.SelectedSlideIndex > 0)
                        _engine.SetCurrentSlide(Scene.SelectedSlideIndex - 1);
                    break;

                case PresentHuiAction.NextSlide:
                    if (Scene.SelectedSlideIndex < Scene.SlideCount - 1)
                        _engine.SetCurrentSlide(Scene.SelectedSlideIndex + 1);
                    break;

                case PresentHuiAction.StartSlideshow:
                    if (!Scene.IsSlideshowMode && Scene.SlideCount > 0)
                    {
                        _engine.PostUnoCommand(".uno:StartPresentation", "{}", true);
                        Scene.SetSlideshowMode(true);
                    }
                    else if (Scene.IsSlideshowMode)
                    {
                        _engine.PostUnoCommand(".uno:EndPresentation", "{}", true);
                        Scene.SetSlideshowMode(false);
                    }
                    break;

                case PresentHuiAction.EndSlideshow:
                    _engine.PostUnoCommand(".uno:EndPresentation", "{}", true);
                    Scene.SetSlideshowMode(false);
                    break;

                case PresentHuiAction.Save:
                    if (!string.IsNullOrEmpty(_currentDocumentPath))
                        _engine.SaveAs(_currentDocumentPath);
                    break;

                case PresentHuiAction.Undo:
                    _engine.Undo();
                    break;

                case PresentHuiAction.Redo:
                    _engine.Redo();
                    break;
            }

            RefreshScene();
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Error: {ex.Message}");
        }
    }

    private void RefreshScene()
    {
        if (!_engine.IsOpen()) return;

        var slides = _engine.GetSlides();
        var slideNames = new List<string>(slides.Count);
        foreach (var slide in slides)
            slideNames.Add(slide.Name);

        int current = _engine.CurrentSlide();
        Scene.UpdateSlides(slideNames, current);
    }
}