using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace HavenOS.Images;

public sealed partial class MainWindow : Window, ICuiActionDispatcher, ICuiActionAvailability
{
    private Button _previousButton = null!, _nextButton = null!, _zoomOutButton = null!, _zoomInButton = null!, _fitButton = null!;
    private TextBlock _statusText = null!, _fileNameText = null!, _metadataText = null!, _zoomText = null!;
    private TextBox _cropBoundsBox = null!;
    private Button _applyCropButton = null!, _exportCropButton = null!, _infoButton = null!;
    private PictureMetadataExportMode _metadataExportMode;
    private readonly Image _previewImage = new() { Stretch = Stretch.Uniform };
    private StackPanel _emptyState = null!;
    private NumericUpDown _widthBox = null!, _heightBox = null!;
    private CheckBox _compareButton = null!;
    private readonly ImageViewportState _viewport = new();
    private readonly ScaleTransform _scaleTransform = new();
    private readonly TranslateTransform _translateTransform = new();
    private readonly TransformGroup _imageTransform = new();
    private readonly PictureCropService _cropService = new();
    private PictureEditorSession? _session;
    private PictureOperationTarget? _selectedOperation;
    private Bitmap? _bitmap;
    private ImageMetadataSnapshot? _metadata;
    private ImageNavigationSession? _navigation;
    private bool _isPanning, _updatingSize, _updatingCrop, _sizeDraft, _cropDraft, _allowClose, _busy;
    private Point _lastPanPosition;

    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiAppearance? _appearance;
    private readonly CuiViewModel _layout = new();
    private readonly CuiSceneHost _scene;
    private Task<CuiSceneAvailability>? _initialization;
    private bool _initialized, _closing, _retiring, _commandActive;
    private const int MaximumRetainedCommands = 128;
    private readonly List<Task> _originalCommands = [];
    private Task? _originalCommand;
    private Task<bool>? _originalClose;

    /// <summary>A standalone process has no authenticated original Home attachment.</summary>
    public MainWindow() : this(new PictureRequiredHome()) { }

    /// <summary>The installed native host supplies its actual Home readiness.</summary>
    public MainWindow(ICuiSceneReadiness readiness, CuiAppearance? appearance = null)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _appearance = appearance;
        Title = "Picture"; Width = 1180; Height = 800; MinWidth = 360; MinHeight = 520;
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("picture.viewport", _ => _previewImage);
        registry.RegisterObjectRenderer("picture.originalViewport", _ => _originalComparisonImage);
        _scene = new CuiSceneHost(registry);
        Content = _scene;
        UpdateResponsiveLayout();
        RefreshComparisonLayout();
        RefreshOperationRows();
        RefreshCompositionRows();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Closing += OnClosing;
        Closed += (_, _) => { _bitmap?.Dispose(); CloseComparisonBitmaps(); };
        KeyDown += OnKeyDown;
    }

    public CuiSceneHost SceneHost => _scene;
    public Task? OriginalInitialization => _initialization;
    public Task? OriginalCommand => _originalCommand;
    public IReadOnlyList<Task> OriginalCommands => _originalCommands.AsReadOnly();
    public Task? OriginalClose => _originalClose;
    public Task<CuiSceneAvailability> InitializeAsync(CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_initialization is not null) return _initialization;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _initialization = InitializeAfterPublicationAsync(start.Task, token);
        start.SetResult();
        return _initialization;
    }

    private async Task<CuiSceneAvailability> InitializeAfterPublicationAsync(Task start, CancellationToken token)
    {
        await start;
        return await InitializeCoreAsync(token);
    }

    private async Task<CuiSceneAvailability> InitializeCoreAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var scene = new CuiNativeScene("picture", "Picture", "Imagine", PictureCuiDocuments.Load("PictureWorkspace"),
            _layout, this, _readiness) { Appearance = _appearance, IsPublicationCurrent = () => !_retiring };
        var availability = await _scene.ShowAsync(scene, token);
        if (availability.State != CuiSceneAvailabilityState.Ready) return availability;
        _previousButton = Find<Button>("PreviousButton"); _nextButton = Find<Button>("NextButton");
        _zoomOutButton = Find<Button>("ZoomOutButton"); _zoomInButton = Find<Button>("ZoomInButton"); _fitButton = Find<Button>("FitButton");
        _statusText = Find<TextBlock>("StatusText"); _fileNameText = Find<TextBlock>("FileNameText");
        _metadataText = Find<TextBlock>("MetadataText"); _zoomText = Find<TextBlock>("ZoomText");
        _cropBoundsBox = Find<TextBox>("CropBoundsBox"); _applyCropButton = Find<Button>("ApplyCropButton");
        _exportCropButton = Find<Button>("ExportCropButton"); _infoButton = Find<Button>("InfoButton");
        _emptyState = Find<StackPanel>("EmptyState");
        _widthBox = Find<NumericUpDown>("WidthBox"); _heightBox = Find<NumericUpDown>("HeightBox");
        _compareButton = Find<CheckBox>("CompareButton");
        _imageTransform.Children.Add(_scaleTransform); _imageTransform.Children.Add(_translateTransform);
        _previewImage.RenderTransform = _imageTransform; _previewImage.RenderTransformOrigin = RelativePoint.Center;
        _widthBox.Minimum = _heightBox.Minimum = 1;
        _widthBox.Maximum = _heightBox.Maximum = 32768;
        _widthBox.ShowButtonSpinner = _heightBox.ShowButtonSpinner = false;
        _widthBox.FormatString = _heightBox.FormatString = "0";
        ConfigureComparison();
        _widthBox.ValueChanged += (_, _) => OnSizeDraftChanged(true);
        _heightBox.ValueChanged += (_, _) => OnSizeDraftChanged(false);
        // TextChanged is a queued routed event in the pinned backend. Observe
        // the SAME actual text property synchronously before command admission.
        _cropBoundsBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && !_updatingCrop) _cropDraft = true;
        };
        _previewImage.PointerWheelChanged += OnPreviewPointerWheelChanged;
        _previewImage.PointerPressed += OnPreviewPointerPressed;
        _previewImage.PointerMoved += OnPreviewPointerMoved;
        _previewImage.PointerReleased += OnPreviewPointerReleased;
        _previewImage.PointerCaptureLost += (_, _) => CompletePan();
        Find<NumericUpDown>("SelectedRotationTurns").Minimum = 1;
        Find<NumericUpDown>("SelectedRotationTurns").Maximum = 3;
        Find<NumericUpDown>("SelectedRotationTurns").FormatString = "0";
        ConfigureCompositionInputs();
        ConfigureCanvasResizeInputs();
        ConfigureColorInputs();
        ConfigureStraightenInputs();
        ConfigureCropRatioInputs();
        ConfigurePixelationInputs();
        ConfigureBlurInputs();
        _initialized = true;
        // Publish final availability only after the actual CUI controls and
        // input subscriptions are installed. The loader does not cache it.
        RefreshEditor();
        return availability;
    }

    private void UpdateResponsiveLayout()
    {
        var compact = Width < 880;
        _layout.Set("WorkspaceColumns", compact ? "*" : "*,300");
        _layout.Set("WorkspaceRows", compact ? "Auto,Auto" : "Auto");
        _layout.Set("CanvasHeight", compact ? 300d : 520d);
        _layout.Set("InspectorColumn", compact ? 0 : 1);
        _layout.Set("InspectorRow", compact ? 1 : 0);
        _layout.Set("InspectorMargin", compact ? "0,12,0,0" : "12,0,0,0");
    }

    private T Find<T>(string name) where T : Control => this.GetLogicalDescendants().OfType<T>()
        .SingleOrDefault(control => control.Name == name)
        ?? throw new InvalidOperationException($"{name} was not created from the original CUI scene.");

    public bool? IsActionAvailable(string command) => !_retiring && !_closing && _initialized && !_busy && !_commandActive;

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        // Only successful settled sources can leave this owner's custody. A
        // completed fault or cancellation still belongs to the actual close.
        _originalCommands.RemoveAll(original => original.IsCompletedSuccessfully);
        if (_originalCommands.Count >= MaximumRetainedCommands)
            throw new InvalidOperationException("Picture retained its command failures and cannot admit more work. Close or recover this session before continuing.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _commandActive = true;
        // Admission captures the SAME raw command before any picker, modal,
        // refresh observer or keyboard callback can publish another operation.
        var original = RunCommandAsync(start.Task, command, parameter, token);
        _originalCommand = original;
        _originalCommands.Add(original);
        Exception? publicationFailure = null;
        try { RefreshEditor(); }
        catch (Exception error) { publicationFailure = error; }
        finally
        {
            if (publicationFailure is null) start.SetResult();
            else start.SetException(publicationFailure);
        }
        return new ValueTask(original);
    }

    private async Task RunCommandAsync(Task start, string command, object? parameter, CancellationToken token)
    {
        Exception? originalFailure = null;
        try
        {
            await start;
            token.ThrowIfCancellationRequested();
        switch (command)
        {
            case "9to1.Picture.SelectLayer" when parameter is PictureCompositionTarget layer: SelectLayer(layer); break;
            case "9to1.Picture.SelectVector" when parameter is PictureCompositionTarget vector: SelectVector(vector); break;
            case "9to1.Picture.SelectVectorPath" when parameter is PictureVectorPathTarget path: SelectVectorPath(path); break;
            case "9to1.Picture.SelectVectorNode" when parameter is PictureVectorNodeTarget node: SelectVectorNode(node); break;
            case "9to1.Picture.PreviousVectorNodePage": MoveVectorNodePage(-1); break;
            case "9to1.Picture.NextVectorNodePage": MoveVectorNodePage(1); break;
            case "9to1.Picture.ApplyVectorNode": MoveSelectedVectorNode(null); break;
            case "9to1.Picture.ApplyVectorControl1": MoveSelectedVectorNode(1); break;
            case "9to1.Picture.ApplyVectorControl2": MoveSelectedVectorNode(2); break;
            case "9to1.Picture.VectorSegmentLine": ConvertSelectedVectorSegment(Haven.Core.DocumentVectorSegmentKind.Line); break;
            case "9to1.Picture.VectorSegmentQuadratic": ConvertSelectedVectorSegment(Haven.Core.DocumentVectorSegmentKind.Quadratic); break;
            case "9to1.Picture.VectorSegmentCubic": ConvertSelectedVectorSegment(Haven.Core.DocumentVectorSegmentKind.Cubic); break;
            case "9to1.Picture.AddVectorAnchor": AddSelectedVectorAnchor(); break;
            case "9to1.Picture.DeleteVectorAnchor": DeleteSelectedVectorAnchor(); break;
            case "9to1.Picture.ResetVectorAnchor": ResetVectorAnchorDraft(); break;
            case "9to1.Picture.SelectAddedVectorAnchor": SelectAddedVectorAnchor(parameter); break;
            case "9to1.Picture.CloseVectorSubpath": SetSelectedVectorSubpathClosed(parameter, true); break;
            case "9to1.Picture.OpenVectorSubpath": SetSelectedVectorSubpathClosed(parameter, false); break;
            case "9to1.Picture.AddRectangle": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Rectangle); break;
            case "9to1.Picture.AddEllipse": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Ellipse); break;
            case "9to1.Picture.AddPolygon": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Polygon); break;
            case "9to1.Picture.AddStar": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Star); break;
            case "9to1.Picture.AddLine": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Line); break;
            case "9to1.Picture.AddArrow": AddPrimitive(Haven.Application.DocumentVectorPrimitive.Arrow); break;
            case "9to1.Picture.AddSharedVector": ChangeComposition("Insert shape", owner => owner.AddSharedVector(
                new HavenOS.Home.Core.HomeVectorShapeObjectHandler().Project(Haven.Application.DocumentVectorShapes.CreateEditableStarter("Shape")), _selectedLayer)); break;
            case "9to1.Picture.CreateLayer": ChangeComposition("Create layer", owner => owner.CreateLayer(Find<TextBox>("LayerNameBox").Text ?? "Layer")); break;
            case "9to1.Picture.RenameLayer": ChangeComposition("Rename layer", owner => owner.RenameLayer(SelectedLayer(), Find<TextBox>("LayerNameBox").Text ?? "")); break;
            case "9to1.Picture.ToggleLayerVisibility": ToggleLayerVisibility(); break;
            case "9to1.Picture.ToggleLayerLock": ToggleLayerLock(); break;
            case "9to1.Picture.MoveLayerEarlier": MoveSelectedLayer(-1); break;
            case "9to1.Picture.MoveLayerLater": MoveSelectedLayer(1); break;
            case "9to1.Picture.RotateVector": ApplySelectedVectorRotation(); break;
            case "9to1.Picture.MirrorVectorHorizontal": MirrorSelectedVector(true); break;
            case "9to1.Picture.MirrorVectorVertical": MirrorSelectedVector(false); break;
            case "9to1.Picture.ResetVectorRotation": ResetSelectedVectorRotation(); break;
            case "9to1.Picture.ApplyVectorPlacement": ChangeComposition("Move shape", owner => owner.MoveVector(SelectedVector(), ReadVectorPlacement())); break;
            case "9to1.Picture.TransferVectorLayer": ApplySelectedVectorLayerTransfer(parameter); break;
            case "9to1.Picture.ResetVectorLayerDestination": ResetVectorLayerDestination(); break;
            case "9to1.Picture.ApplyVectorStroke": ApplySelectedVectorStroke(); break;
            case "9to1.Picture.ResetVectorStroke": ResetSelectedVectorStroke(); break;
            case "9to1.Picture.ApplyVectorFill": ApplySelectedVectorFill(); break;
            case "9to1.Picture.ResetVectorFill": ResetSelectedVectorFill(); break;
            case "9to1.Picture.SelectEdit" when parameter is PictureOperationTarget target: SelectOperation(target); break;
            case "9to1.Picture.ReplaceSelectedEdit": ChangeSelectedOperation("Modify edit", false, false); break;
            case "9to1.Picture.RemoveSelectedEdit": ChangeSelectedOperation("Remove edit", true, false); break;
            case "9to1.Picture.MoveEditEarlier": ChangeSelectedOperation("Move edit earlier", false, true, -1); break;
            case "9to1.Picture.MoveEditLater": ChangeSelectedOperation("Move edit later", false, true, 1); break;
            case "9to1.Picture.ComparisonMode" when parameter is PictureComparisonMode comparison: SelectComparisonMode(comparison); break;
            case "9to1.Picture.Open": await OpenAsync(); break;
            case "9to1.Picture.New": await NewAsync(); break;
            case "9to1.Picture.Save": await SaveDocumentAsync(false); break;
            case "9to1.Picture.SaveCopy": await SaveDocumentAsync(true); break;
            case "9to1.Picture.Undo": ChangeHistory(false); break;
            case "9to1.Picture.Redo": ChangeHistory(true); break;
            case "9to1.Picture.Previous": await NavigateAsync(false); break;
            case "9to1.Picture.Next": await NavigateAsync(true); break;
            case "9to1.Picture.RotateLeft": Edit("Rotate left", document => document.Rotate(-1)); break;
            case "9to1.Picture.RotateRight": Edit("Rotate right", document => document.Rotate()); break;
            case "9to1.Picture.Straighten": ApplyStraighten(); break;
            case "9to1.Picture.FlipHorizontal": Edit("Flip horizontal", document => document.Flip(true)); break;
            case "9to1.Picture.FlipVertical": Edit("Flip vertical", document => document.Flip(false)); break;
            case "9to1.Picture.Resize":
                if (Edit("Resize image", document => document.Resize((int)(_widthBox.Value ?? 1), (int)(_heightBox.Value ?? 1))))
                    AcknowledgeGeometryDrafts(size: true, crop: false);
                break;
            case "9to1.Picture.Blur": ApplyBlur(); break;
            case "9to1.Picture.ResetBlurDraft": ResetBlurDraft(); RefreshEditor(); break;
            case "9to1.Picture.Pixelate": ApplyPixelation(); break;
            case "9to1.Picture.ApplyColorAdjustment": ApplyColorAdjustment(); break;
            case "9to1.Picture.ResetColorDraft": ResetColorDraft(); break;
            case "9to1.Picture.ResizeCanvas": ApplyCanvasResize(); break;
            case "9to1.Picture.CenterCanvasContent": CenterCanvasContent(); break;
            case "9to1.Picture.Revert":
                Edit("Revert to original", document =>
                {
                    using var original = PictureCropService.OpenVerifiedSource(document);
                    return document.RevertToOriginal(original.PixelSize.Width, original.PixelSize.Height);
                }); break;
            case "9to1.Picture.Crop": ApplyCrop(); break;
            case "9to1.Picture.FitCropRatio": FitCropRatioDraft(); break;
            case "9to1.Picture.Export": await ExportAsync(); break;
            case "9to1.Picture.Info": if (_metadata is not null) await new ImageMetadataWindow(_metadata, _readiness).OpenAsync(this); break;
            case "9to1.Picture.ZoomOut": ZoomAtViewportCenter(1 / 1.25); break;
            case "9to1.Picture.ZoomIn": ZoomAtViewportCenter(1.25); break;
            case "9to1.Picture.Fit": _viewport.Reset(); ApplyViewport(); break;
            case "9to1.Picture.MetadataPreserve": SetMetadataExportMode(PictureMetadataExportMode.Preserve); break;
            case "9to1.Picture.MetadataRemoveLocation": SetMetadataExportMode(PictureMetadataExportMode.RemoveLocation); break;
            case "9to1.Picture.MetadataRemoveAll": SetMetadataExportMode(PictureMetadataExportMode.RemoveAll); break;
            default: throw new InvalidOperationException($"Picture action {command} is not registered.");
        }
        }
        catch (Exception error) { originalFailure = error; }
        _commandActive = false;
        try { if (_initialized && !_retiring) RefreshEditor(); }
        catch (Exception refreshFailure)
        {
            if (originalFailure is not null)
                throw new AggregateException("Picture command retained both its original and refresh failures.", originalFailure, refreshFailure);
            throw;
        }
        if (originalFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
    }

    private void RefreshOperationRows()
    {
        var document = _session?.Document;
        if (_selectedOperation is { } selected && (document is null || selected.DocumentId != document.DocumentId ||
            selected.Revision != document.Revision)) _selectedOperation = null;
        Dictionary<string, object?>[] rows = document is null ? [] : document.Operations.Select((operation, index) =>
            new Dictionary<string, object?>
            {
                ["Key"] = $"{document.DocumentId:N}:{document.Revision}:{index}",
                ["Label"] = $"{index + 1}. {Describe(operation)}",
                ["Target"] = _session!.CaptureOperation(index)
            }).ToArray();
        var canEdit = _initialized && !_busy && !_commandActive && !_closing && !_retiring;
        _layout.Set("OperationRows", rows);
        _layout.Set("CanChooseOperation", canEdit);
        _layout.Set("CanChangeOperation", canEdit && _selectedOperation is not null);
        _layout.Set("CanMoveOperationEarlier", canEdit && _selectedOperation is { Index: > 0 });
        _layout.Set("CanMoveOperationLater", canEdit && _selectedOperation is { } current && current.Index < document!.Operations.Count - 1);
        _layout.Set("SelectedIsRotation", _selectedOperation?.Operation is RotateOperation);
        _layout.Set("SelectedIsStraighten", _selectedOperation?.Operation is StraightenOperation);
        _layout.Set("SelectedIsFlip", _selectedOperation?.Operation is FlipOperation);
        _layout.Set("SelectedIsCanvasResize", _selectedOperation?.Operation is CanvasResizeOperation);
        _layout.Set("SelectedIsColorAdjustment", _selectedOperation?.Operation is ColorAdjustmentOperation);
        _layout.Set("SelectedIsBlur", _selectedOperation?.Operation is BlurOperation);
        _layout.Set("SelectedIsPixelation", _selectedOperation?.Operation is PixelationOperation);
        _layout.Set("SelectedUsesTransformFields", _selectedOperation?.Operation is CropOperation or ResizeOperation);
        _layout.Set("SelectedEditLabel", _selectedOperation is { } chosen
            ? $"Selected: {chosen.Index + 1}. {Describe(chosen.Operation)}" : "Select an edit to modify its values or order.");
    }

    private void SelectOperation(PictureOperationTarget target)
    {
        if (_session is null) return;
        // Re-capture from the SAME session and compare the original revision
        // before reflecting a row into controls. Rows cannot select a foreign
        // or stale document merely because their text/index looks identical.
        var current = _session.CaptureOperation(target.Index);
        if (!ReferenceEquals(current.Owner, target.Owner) || current.DocumentId != target.DocumentId ||
            current.Revision != target.Revision || !ReferenceEquals(current.Operation, target.Operation))
            throw new InvalidOperationException("Revision conflict: select this edit again from the current document.");
        _selectedOperation = target;
        _updatingSize = true;
        try
        {
            switch (target.Operation)
            {
                case CropOperation crop: SelectCropOperationFields(crop); break;
                case ResizeOperation resize when !_sizeDraft: _widthBox.Value = resize.Width; _heightBox.Value = resize.Height; break;
                case StraightenOperation angle when !_straightenDraft: SetStraightenFields(angle); break;
                case ColorAdjustmentOperation color when !_colorDraft: SetColorFields(color.Settings, color.WorkingSpace); break;
                case PixelationOperation pixels: SelectPixelationFields(pixels); break;
                case BlurOperation blur: SelectBlurFields(blur); break;
                case CanvasResizeOperation canvasResize when !_canvasDraft: SetCanvasResizeFields(canvasResize); break;
                case RotateOperation rotation: Find<NumericUpDown>("SelectedRotationTurns").Value = rotation.ClockwiseQuarterTurns; break;
                case FlipOperation flip: Find<CheckBox>("SelectedFlipHorizontal").IsChecked = flip.Horizontal; break;
            }
        }
        finally { _updatingSize = false; }
        RefreshOperationRows();
    }

    private void ChangeSelectedOperation(string name, bool remove, bool move, int direction = 0)
    {
        if (_session is null || _selectedOperation is not { } selected) return;
        try
        {
            Bitmap changed;
            PictureCropRatioFit? fittedCrop = null;
            if (remove) changed = _session.RemoveOperation(selected);
            else if (move) changed = _session.MoveOperation(selected, checked(selected.Index + direction));
            else
            {
                PictureOperation replacement = selected.Operation switch
                {
                    CropOperation => (fittedCrop = ReadRatioCrop(selectedEdit: true)).Crop,
                    ResizeOperation => new ResizeOperation((int)(_widthBox.Value ?? 1), (int)(_heightBox.Value ?? 1)),
                    RotateOperation => new RotateOperation((int)(Find<NumericUpDown>("SelectedRotationTurns").Value ?? 1)),
                    FlipOperation => new FlipOperation(Find<CheckBox>("SelectedFlipHorizontal").IsChecked == true),
                    StraightenOperation => ReadStraightenOperation(),
                    CanvasResizeOperation => ReadCanvasResize(),
                    ColorAdjustmentOperation => ReadColorOperation(),
                    PixelationOperation => ReadPixelationOperation(),
                    BlurOperation => ReadBlurOperation(),
                    _ => throw new InvalidDataException("This edit cannot be modified by the current inspector.")
                };
                changed = _session.ReplaceOperation(selected, replacement);
            }
            SetPreview(changed);
            _selectedOperation = null;
            if (fittedCrop is not null) PublishCropRatioNotice(fittedCrop, applied: true);
            // Only a successful modification consumes its corresponding form.
            // Reorder/remove and unrelated actions preserve pending geometry.
            if (!remove && !move && selected.Operation is StraightenOperation) _straightenDraft = false;
            if (!remove && !move && selected.Operation is CanvasResizeOperation) _canvasDraft = false;
            if (!remove && !move && selected.Operation is ColorAdjustmentOperation) _colorDraft = false;
            if (!remove && !move && selected.Operation is BlurOperation) _blurDraft = false;
            if (!remove && !move && selected.Operation is PixelationOperation) { _pixelationDraft = false; _cropDraft = false; }
            if (!remove && !move)
                AcknowledgeGeometryDrafts(size: selected.Operation is ResizeOperation, crop: selected.Operation is CropOperation);
            else RefreshEditor();
            _statusText.Text = name + " applied · original preserved.";
        }
        catch (Exception error) when (error is not PictureComparisonPublicationFailure) { ShowError(name + " could not be applied: " + error.Message); }
    }

    private CropOperation ReadCropBounds()
    {
        var values = (_cropBoundsBox.Text ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 4 || !int.TryParse(values[0], out var x) || !int.TryParse(values[1], out var y) ||
            !int.TryParse(values[2], out var width) || !int.TryParse(values[3], out var height))
            throw new InvalidDataException("Enter crop bounds as x, y, width, height using image pixels.");
        return new CropOperation(x, y, width, height);
    }

    private void SetMetadataExportMode(PictureMetadataExportMode mode)
    {
        _metadataExportMode = mode;
        Find<TextBlock>("MetadataExportModeText").Text = mode switch
        {
            PictureMetadataExportMode.RemoveLocation => "Remove location metadata",
            PictureMetadataExportMode.RemoveAll => "Remove all metadata",
            _ => "Preserve supported metadata"
        };
    }

    private async Task OpenAsync()
    {
        if (_busy || !await ConfirmLeaveAsync()) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image or Picture document", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Images and Picture documents")
                    { Patterns = [.. ImageFilePolicy.PickerPatterns, "*.picture.json"] }]
            });
            if (files.Count == 0) return;
            if (!files[0].Path.IsFile) { ShowError("Choose a local image or Picture document."); return; }
            var path = files[0].Path.LocalPath;
            if (path.EndsWith(".picture.json", StringComparison.OrdinalIgnoreCase))
            {
                var document = await PictureDocument.OpenAsync(path);
                var rendered = PictureCropService.Render(document);
                SetSession(new PictureEditorSession(document, path), rendered, null, null);
                _statusText.Text = $"Opened {Path.GetFileName(path)} · editable source and operations restored.";
            }
            else LoadLocalPath(path);
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"Could not open: {exception.Message}"); }
    }

    private async Task NewAsync()
    {
        if (_busy || !await ConfirmLeaveAsync()) return;
        try
        {
            var document = PictureDocument.Create((int)(_widthBox.Value ?? 1200), (int)(_heightBox.Value ?? 900));
            SetSession(new PictureEditorSession(document), PictureCropService.Render(document), null, null);
            _statusText.Text = "Created a transparent canvas using the image size in Transform.";
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"Could not create canvas: {exception.Message}"); }
    }

    private async Task NavigateAsync(bool next)
    {
        if (_busy || _navigation is null || !await ConfirmLeaveAsync()) return;
        var path = next ? _navigation.MoveNext() : _navigation.MovePrevious();
        if (path is not null) LoadLocalPath(path);
    }

    private void LoadLocalPath(string path)
    {
        if (!ImageFilePolicy.IsSupportedPath(path)) { ShowError("Choose a PNG, JPEG, BMP, GIF, or WebP image."); return; }
        try
        {
            var document = _cropService.OpenSource(path);
            var rendered = PictureCropService.Render(document);
            var metadata = ImageMetadataSnapshot.Read(path, rendered);
            SetSession(new PictureEditorSession(document), rendered, metadata, ImageNavigationSession.FromSelection(path));
            _statusText.Text = "Image opened. Edits are kept separately from the original; Save creates an editable Picture document.";
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"Picture could not decode this file: {exception.Message}"); }
    }

    private void SetSession(PictureEditorSession session, Bitmap bitmap, ImageMetadataSnapshot? metadata, ImageNavigationSession? navigation)
    {
        _selectedOperation = null;
        _selectedLayer = _selectedVector = null; _selectedVectorPath = _selectedVectorSubpath = _selectedVectorNode = null; _vectorNodeOffset = 0;
        // Only confirmed document replacement retires all old geometry drafts.
        _sizeDraft = _cropDraft = _canvasDraft = _straightenDraft = false;
        ResetComparisonForReplacement();
        _session = session; _metadata = metadata; _navigation = navigation;
        ResetColorDraft(sourceReplacement: true);
        ResetCropRatioInputs();
        ResetPixelationFields();
        ResetBlurDraft();
        SetPreview(bitmap);
        RefreshEditor();
    }

    private void SetPreview(Bitmap bitmap)
    {
        var previous = _bitmap; _bitmap = bitmap;
        try { PublishComparisonViews(); }
        catch (Exception originalFailure)
        {
            if (previous is not null) _retainedComparisonBitmaps.Add(previous);
            throw new PictureComparisonPublicationFailure(originalFailure); // Both accepted render sources stay owned.
        }
        previous?.Dispose();
        _previewImage.IsVisible = true; _emptyState.IsVisible = false;
        _viewport.Reset(); ApplyViewport();
    }

    private bool Edit(string name, Func<PictureDocument, PictureDocument> edit)
    {
        if (_busy || _session is null) return false;
        try
        {
            SetPreview(_session.Apply(name, edit));
            RefreshEditor();
            _statusText.Text = $"{name} applied · original preserved.";
            return true;
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"{name} could not be applied: {exception.Message}"); return false; }
    }

    private void ChangeHistory(bool redo)
    {
        if (_busy || _session is null || (redo ? !_session.CanRedo : !_session.CanUndo)) return;
        try
        {
            SetPreview(redo ? _session.Redo() : _session.Undo()); RefreshEditor();
            _statusText.Text = _session.LastAction;
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"History could not be restored: {exception.Message}"); }
    }

    private void ApplyCrop()
    {
        try
        {
            var fitted = ReadRatioCrop(selectedEdit: false); var crop = fitted.Crop;
            if (Edit("Crop", document => document.Crop(crop.X, crop.Y, crop.Width, crop.Height)))
            {
                PublishCropRatioNotice(fitted, applied: true);
                AcknowledgeGeometryDrafts(size: false, crop: true);
            }
        }
        catch (Exception error) when (error is not PictureComparisonPublicationFailure)
        { ShowError("Crop could not be applied: " + error.Message); }
    }

    private void OnSizeDraftChanged(bool fromWidth)
    {
        if (_updatingSize) return;
        _sizeDraft = true;
        UpdateAspectRatio(fromWidth);
    }

    private void AcknowledgeGeometryDrafts(bool size, bool crop)
    {
        if (size) _sizeDraft = false;
        if (crop) { _cropDraft = false; _cropRatioDraft = false; }
        RefreshEditor();
    }

    private void SetCropField(string value)
    {
        _updatingCrop = true;
        try { _cropBoundsBox.Text = value; }
        finally { _updatingCrop = false; }
    }

    private void UpdateAspectRatio(bool fromWidth)
    {
        if (_updatingSize || _session is null || Find<CheckBox>("AspectLockBox").IsChecked != true) return;
        _updatingSize = true;
        try
        {
            var ratio = _selectedOperation?.Operation is ResizeOperation selectedResize
                ? (decimal)selectedResize.Width / selectedResize.Height
                : (decimal)_session.Document.CanvasWidth / _session.Document.CanvasHeight;
            if (fromWidth) _heightBox.Value = Math.Clamp(decimal.Round((_widthBox.Value ?? 1) / ratio), 1, 32768);
            else _widthBox.Value = Math.Clamp(decimal.Round((_heightBox.Value ?? 1) * ratio), 1, 32768);
        }
        finally { _updatingSize = false; }
    }


    private async Task<bool> SaveDocumentAsync(bool copy)
    {
        if (_busy || _session is null) return false;
        try
        {
            var path = copy ? null : _session.DocumentPath;
            if (path is null)
            {
                var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = copy ? "Save a Picture copy" : "Save editable Picture document",
                    SuggestedFileName = _session.Document.DisplayName + (copy ? "-copy" : "") + ".picture.json",
                    FileTypeChoices = [new FilePickerFileType("Picture document") { Patterns = ["*.picture.json"] }],
                    DefaultExtension = "picture.json"
                });
                if (target is null) return false;
                if (!target.Path.IsFile) { ShowError("Choose a local destination for the Picture document."); return false; }
                path = target.Path.LocalPath;
            }
            _busy = true; RefreshEditor();
            await _session.SaveAsync(path, copy);
            _statusText.Text = copy ? $"Saved independent editable copy to {Path.GetFileName(path)}." : $"Saved {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"Could not save: {exception.Message}"); return false; }
        finally { _busy = false; RefreshEditor(); }
    }

    private async Task ExportAsync()
    {
        if (_busy || _session is null) return;
        try
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export rendered PNG", SuggestedFileName = _session.Document.DisplayName + "-export.png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }], DefaultExtension = "png"
            });
            if (target is null) return;
            if (!target.Path.IsFile) { ShowError("Choose a local destination for PNG export."); return; }
            _busy = true; RefreshEditor();
            _statusText.Text = "Rendering PNG at full source quality…";
            _cropService.ExportPng(_session.Document, target.Path.LocalPath, _metadataExportMode);
            _statusText.Text = $"PNG exported to {target.Name} · editable document preserved.";
        }
        catch (Exception exception) when (exception is not PictureComparisonPublicationFailure) { ShowError($"PNG export failed: {exception.Message}"); }
        finally { _busy = false; RefreshEditor(); }
    }

    private async Task<bool> ConfirmLeaveAsync()
    {
        if (_session?.IsDirty != true) return true;
        using var dialog = new PictureCuiDialog("Save changes?", "PictureSaveConfirmation", _readiness, new CuiViewModel());
        var result = await dialog.OpenAsync(this);
        return result == "discard" || result == "save" && await SaveDocumentAsync(false);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        // A retry observes the SAME original close once acquired. Only an
        // actual successful confirmation refusal permits another close attempt.
        if (_originalClose is { } previous && (!previous.IsCompletedSuccessfully || previous.Result)) return;
        if (_closing || _busy || _originalCommand is { IsCompleted: false }) return;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Original close custody precedes confirmation, cancellation or native
        // notifications. A rejected confirmation leaves this same owner usable.
        _originalClose = CloseAfterPublicationAsync(start.Task);
        _closing = true;
        start.SetResult();
        try { await _originalClose; }
        catch (Exception error)
        {
            if (_initialized) ShowError("Picture could not finish closing: " + error.Message);
            System.Diagnostics.Trace.TraceError("Picture close retained its original scene: {0}", error);
        }
        finally { _closing = false; }
    }

    private async Task<bool> CloseAfterPublicationAsync(Task start)
    {
        await start;
        if (_initialized && !await ConfirmLeaveAsync()) return false;
        // Freeze the actual admitted sources before scene retirement can run
        // callbacks. Failed close retains this ledger and all original tasks.
        var initialization = _initialization;
        var commands = _originalCommands.ToArray();
        _retiring = true;
        var sceneClose = _scene.CloseOriginalAsync();
        var errors = new List<Exception>();
        async Task Join(Task? original)
        {
            if (original is null) return;
            try { await original; }
            catch (Exception error)
            {
                // A canceled task is terminal, but this app owner has no exact
                // source-issued proof that cancellation was its own expected
                // retirement. Retain the actual failure, just like a faulted OCE.
                if (!errors.Any(existing => ReferenceEquals(existing, error))) errors.Add(error);
            }
        }
        await Join(initialization);
        foreach (var original in commands) await Join(original);
        await Join(sceneClose);
        if (errors.Count > 0)
            throw new AggregateException("Picture close retained original initialization, command or scene failures.", errors);
        _allowClose = true;
        Close(); // Only now may Closed release the original rendered bitmap.
        return true;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || IsActionAvailable("keyboard") != true) return;
        var command = e.Key switch
        {
            Key.S => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Picture.SaveCopy" : "9to1.Picture.Save",
            Key.O => "9to1.Picture.Open",
            Key.Z => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Picture.Redo" : "9to1.Picture.Undo",
            Key.Y => "9to1.Picture.Redo",
            _ => null
        };
        if (command is null) return;
        e.Handled = true;
        try { await DispatchAsync(command, null); }
        catch (Exception error)
        {
            // Reporting a keyboard failure never settles or replaces its raw
            // source. The admitted task stays in the same close ledger.
            if (_initialized && !_retiring) ShowError("Picture command could not finish: " + error.Message);
            System.Diagnostics.Trace.TraceError("Picture keyboard retained its original command: {0}", error);
        }
    }

    private void RefreshEditor()
    {
        var document = _session?.Document;
        var canInteract = IsActionAvailable("editor") == true;
        var enabled = document is not null && canInteract;
        Find<Button>("NewButton").IsEnabled = Find<Button>("OpenButton").IsEnabled = canInteract;
        foreach (var name in new[] { "MetadataPreserveButton", "MetadataRemoveLocationButton", "MetadataRemoveAllButton" })
            Find<Button>(name).IsEnabled = canInteract;
        foreach (var name in new[] { "SaveButton", "SaveCopyButton", "ApplyStraightenButton", "RotateLeftButton", "RotateRightButton", "FlipHorizontalButton", "FlipVerticalButton", "ResizeButton", "ResizeCanvasButton", "CenterCanvasContentButton", "ApplyColorAdjustmentButton", "ResetColorDraftButton", "ApplyPixelationButton", "ApplyBlurButton", "ResetBlurDraftButton", "ApplyCropButton", "ExportCropButton" }) Find<Button>(name).IsEnabled = enabled;
        Find<Button>("UndoButton").IsEnabled = enabled && _session!.CanUndo;
        Find<Button>("RedoButton").IsEnabled = enabled && _session!.CanRedo;
        Find<Button>("RevertButton").IsEnabled = enabled && (document!.Operations.Count > 0 || document.CompositionState is not null);
        RefreshComparisonControls();
        _infoButton.IsEnabled = _metadata is not null && canInteract;
        UpdateNavigationButtons();
        ApplyViewport();
        RefreshOperationRows();
        RefreshCompositionRows();
        RefreshCanvasResizeFields();
        RefreshColorFields();
        RefreshStraightenFields();
        RefreshCropRatioControls();
        RefreshPixelationFields();
        RefreshBlurFields(enabled);
        _layout.Set("HistoryState", _session?.LastAction is { } action ? "Latest action: " + action : "History begins with your first edit.");
        var discardedEarlier = _session?.DiscardedEarlierHistoryEntries ?? 0;
        _layout.Set("HasHistoryRetentionNotice", discardedEarlier > 0);
        _layout.Set("HistoryRetentionNotice", discardedEarlier > 0 ?
            "At least " + discardedEarlier + " earlier edits are outside the retained undo history." : "");
        if (document is null) return;
        if (_selectedOperation is null)
        {
            // Admission, busy state and unrelated actions update availability,
            // never replace an actual user draft before its consumer reads it.
            if (!_sizeDraft)
            {
                _updatingSize = true;
                try { _widthBox.Value = document.CanvasWidth; _heightBox.Value = document.CanvasHeight; }
                finally { _updatingSize = false; }
            }
            if (!_cropDraft) SetCropField($"0, 0, {document.CanvasWidth}, {document.CanvasHeight}");
        }
        _fileNameText.Text = document.DisplayName + (_session!.IsDirty ? " · unsaved changes" : "");
        _metadataText.Text = $"{document.CanvasWidth} × {document.CanvasHeight} · revision {document.Revision}";
        Title = document.DisplayName + (_session.IsDirty ? " *" : "") + " — Picture";
        Find<TextBlock>("HistoryText").Text = document.Operations.Count == 0 ? "No raster adjustments" : string.Join("\n", document.Operations.Select((operation, index) => $"{index + 1}. {Describe(operation)}"));
    }

    private static string Describe(PictureOperation operation) => operation switch
    {
        CropOperation crop => $"Crop to {crop.Width} × {crop.Height}", RotateOperation rotate => $"Rotate {rotate.ClockwiseQuarterTurns * 90}°",
        StraightenOperation angle => $"Straighten {angle.ClockwiseDegrees:0.###}° · {(angle.ExpandCanvas ? "fit entire image" : "keep canvas")}",
        BlurOperation blur => $"Blur {blur.Settings.X}, {blur.Settings.Y} · {blur.Settings.Width} × {blur.Settings.Height} · radius {blur.Settings.Radius}",
        PixelationOperation pixels => $"Pixelate {pixels.Settings.X}, {pixels.Settings.Y} · {pixels.Settings.Width} × {pixels.Settings.Height} · {pixels.Settings.BlockSize}-pixel blocks",
        ColorAdjustmentOperation color => $"Colour · sRGB · exposure {color.Settings.ExposureEv:+0.##;-0.##;0} EV · brightness {color.Settings.Brightness:0.##} · contrast {color.Settings.Contrast:0.##} · saturation {color.Settings.Saturation:0.##} · gamma {color.Settings.Gamma:0.##}",
        CanvasResizeOperation canvas => $"Canvas {canvas.Width} × {canvas.Height} · place at {canvas.OffsetX}, {canvas.OffsetY}",
        FlipOperation flip => flip.Horizontal ? "Flip horizontal" : "Flip vertical", ResizeOperation resize => $"Resize to {resize.Width} × {resize.Height}", _ => "Edit"
    };

    private void UpdateNavigationButtons()
    {
        var canInteract = IsActionAvailable("navigation") == true;
        _previousButton.IsEnabled = canInteract && _navigation?.CanMovePrevious == true;
        _nextButton.IsEnabled = canInteract && _navigation?.CanMoveNext == true;
    }

    private void ZoomAtViewportCenter(double factor)
    {
        var width = _previewImage.Bounds.Width;
        var height = _previewImage.Bounds.Height;
        _viewport.ZoomAt(factor, width / 2, height / 2, width, height);
        ApplyViewport();
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_bitmap is null || Math.Abs(e.Delta.Y) < 0.001)
            return;

        var originalViewport = sender as Control ?? _previewImage;
        var point = e.GetPosition(originalViewport);
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        _viewport.ZoomAt(factor, point.X, point.Y, originalViewport.Bounds.Width, originalViewport.Bounds.Height);
        ApplyViewport();
        e.Handled = true;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_bitmap is null || !e.GetCurrentPoint(_previewImage).Properties.IsLeftButtonPressed)
            return;

        _isPanning = true;
        _lastPanPosition = e.GetPosition(_previewImage);
        _previewImage.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(_previewImage);
        e.Handled = true;
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning)
            return;

        var point = e.GetPosition(_previewImage);
        _viewport.PanBy(point.X - _lastPanPosition.X, point.Y - _lastPanPosition.Y);
        _lastPanPosition = point;
        ApplyViewport();
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
            return;

        CompletePan();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CompletePan()
    {
        if (!_isPanning)
            return;

        _isPanning = false;
        _previewImage.Cursor = null;
    }

    private void ApplyViewport()
    {
        _scaleTransform.ScaleX = _viewport.Scale;
        _scaleTransform.ScaleY = _viewport.Scale;
        _translateTransform.X = _viewport.OffsetX;
        _translateTransform.Y = _viewport.OffsetY;
        _zoomText.Text = $"{_viewport.Scale:P0}";
        var canInteract = IsActionAvailable("viewport") == true;
        _zoomInButton.IsEnabled = canInteract && _bitmap is not null && _viewport.CanZoomIn;
        _zoomOutButton.IsEnabled = canInteract && _bitmap is not null && _viewport.CanZoomOut;
        _fitButton.IsEnabled = canInteract && _bitmap is not null && !_viewport.IsDefault;
    }

    private void ShowError(string message)
    {
        _statusText.Text = message;
        UpdateNavigationButtons();
    }
}
