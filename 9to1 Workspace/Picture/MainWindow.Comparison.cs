using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private sealed class PictureComparisonPublicationFailure(Exception original)
        : Exception("Picture could not publish its retained comparison sources.", original) { }
    private enum PictureComparisonMode { Current, Original, SideBySide }
    private PictureComparisonMode _comparisonMode;
    private readonly Image _originalComparisonImage = new() { Stretch = Stretch.Uniform };
    private Bitmap? _originalComparisonBitmap;
    private PictureEditorSession? _originalComparisonSession;
    private CheckBox _sideBySideButton = null!;
    private bool _updatingComparison;
    private readonly List<Bitmap> _retainedComparisonBitmaps = [];
    private void ConfigureComparison()
    {
        _sideBySideButton = Find<CheckBox>("SideBySideButton");
        _compareButton.IsCheckedChanged += OnComparisonChanged;
        _sideBySideButton.IsCheckedChanged += OnComparisonChanged;
        _originalComparisonImage.RenderTransform = _imageTransform;
        _originalComparisonImage.RenderTransformOrigin = Avalonia.RelativePoint.Center;
        _originalComparisonImage.PointerWheelChanged += OnPreviewPointerWheelChanged;
        _originalComparisonImage.PointerPressed += OnPreviewPointerPressed;
        _originalComparisonImage.PointerMoved += OnPreviewPointerMoved;
        _originalComparisonImage.PointerReleased += OnPreviewPointerReleased;
        _originalComparisonImage.PointerCaptureLost += (_, _) => CompletePan();
    }
    private async void OnComparisonChanged(object? sender, EventArgs args)
    {
        if (_updatingComparison || !_initialized || _retiring) return;
        var mode = ReferenceEquals(sender, _sideBySideButton)
            ? _sideBySideButton.IsChecked == true ? PictureComparisonMode.SideBySide : PictureComparisonMode.Current
            : _compareButton.IsChecked == true ? PictureComparisonMode.Original : PictureComparisonMode.Current;
        try { await DispatchAsync("9to1.Picture.ComparisonMode", mode); }
        catch (Exception error)
        {
            // SAME accepted command remains in this window's original ledger.
            if (_initialized && !_retiring) ShowError("Comparison unavailable: " + error.Message);
            System.Diagnostics.Trace.TraceError("Picture comparison retained its original command: {0}", error);
        }
    }
    private void SelectComparisonMode(PictureComparisonMode mode)
    {
        if (!Enum.IsDefined(mode) || _session is null || _bitmap is null)
            throw new InvalidOperationException("Open a Picture document to compare its source and current edit.");
        if (mode != PictureComparisonMode.Current) EnsureComparisonOriginal();
        _comparisonMode = mode;
        PublishComparisonViews(); RefreshComparisonControls();
        _statusText.Text = mode switch
        {
            PictureComparisonMode.Original => "Original · edits remain intact.",
            PictureComparisonMode.SideBySide => "Original and current · edits remain intact.",
            _ => "Current edit"
        };
    }
    private void EnsureComparisonOriginal()
    {
        if (_session is null) throw new InvalidOperationException("No Picture document is open.");
        if (_originalComparisonBitmap is not null && ReferenceEquals(_originalComparisonSession, _session)) return;
        // SAME maintained decoder/hash check. This is retained source-revision pixels,
        // never a replacement document, a history action or a Files write grant.
        if (_originalComparisonBitmap is not null) throw new InvalidOperationException("The prior Picture comparison source has not retired.");
        var original = PictureCropService.OpenVerifiedSource(_session.Document);
        _originalComparisonBitmap = original; _originalComparisonSession = _session;
    }
    private void PublishComparisonViews()
    {
        if (_bitmap is null) return;
        if (_comparisonMode != PictureComparisonMode.Current) EnsureComparisonOriginal();
        _previewImage.Source = _comparisonMode == PictureComparisonMode.Original ? _originalComparisonBitmap : _bitmap;
        _originalComparisonImage.Source = _comparisonMode == PictureComparisonMode.SideBySide ? _originalComparisonBitmap : null;
        _originalComparisonImage.IsVisible = _comparisonMode == PictureComparisonMode.SideBySide;
        RefreshComparisonLayout();
    }
    private void RefreshComparisonControls()
    {
        var document = _session?.Document;
        var enabled = IsActionAvailable("comparison") == true && document is not null
            && (document.Operations.Count > 0 || document.CompositionState is not null || _comparisonMode != PictureComparisonMode.Current);
        _updatingComparison = true;
        try
        {
            _compareButton.IsEnabled = _sideBySideButton.IsEnabled = enabled;
            _compareButton.IsChecked = _comparisonMode == PictureComparisonMode.Original;
            _sideBySideButton.IsChecked = _comparisonMode == PictureComparisonMode.SideBySide;
        }
        finally { _updatingComparison = false; }
        RefreshComparisonLayout();
    }
    private void RefreshComparisonLayout()
    {
        var side = _comparisonMode == PictureComparisonMode.SideBySide;
        _layout.Set("ComparisonColumns", side ? "*,*" : "*");
        _layout.Set("CurrentPreviewColumn", side ? 1 : 0);
        _layout.Set("HasComparisonPreview", _bitmap is not null);
        _layout.Set("HasSideBySideComparison", side);
        _layout.Set("ComparisonLeftLabel", _comparisonMode == PictureComparisonMode.Current ? "Current" : "Original");
        _layout.Set("ComparisonRightLabel", "Current");
    }
    private void ResetComparisonForReplacement()
    {
        _comparisonMode = PictureComparisonMode.Current;
        _updatingComparison = true;
        try { _compareButton.IsChecked = _sideBySideButton.IsChecked = false; }
        finally { _updatingComparison = false; }
        // Detach both actual control consumers before retiring their shared source.
        _originalComparisonImage.Source = null; _originalComparisonImage.IsVisible = false;
        _previewImage.Source = _bitmap;
        if (_originalComparisonBitmap is { } original)
        {
            try { original.Dispose(); }
            catch (Exception cause) { throw new PictureComparisonPublicationFailure(cause); }
            _originalComparisonBitmap = null; _originalComparisonSession = null;
        }
        RefreshComparisonLayout();
    }
    private void CloseComparisonBitmaps()
    {
        _originalComparisonImage.Source = null;
        if (_originalComparisonBitmap is { } original)
        { original.Dispose(); _originalComparisonBitmap = null; _originalComparisonSession = null; }
        foreach (var retained in _retainedComparisonBitmaps) retained.Dispose();
        _retainedComparisonBitmaps.Clear();
    }
}
