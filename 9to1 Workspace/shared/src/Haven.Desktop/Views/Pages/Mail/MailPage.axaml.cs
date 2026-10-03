using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Mail;

public sealed partial class MailPage : UserControl, IDisposable
{
    private readonly MailPageViewModel _viewModel;
    private bool _showReadingOnNarrow;
    private bool _updatingComposeFromEditor;
    private bool _disposed;
    private readonly FilesMailCanonicalAttachmentPicker? _canonicalAttachmentPicker;
    private CancellationTokenSource? _attachmentLifetime;
    internal Task PendingAttachmentSelection { get; private set; } = Task.CompletedTask;

    public MailPage() : this(RequireServices().GetRequiredService<IMailService>(),
        RequireServices().GetRequiredService<IProviderModelClient>(),
        RequireServices().GetRequiredService<IVersionedSettingsStore>(),
        FilesMailCanonicalAttachmentPicker.FromServices(RequireServices())) { }

    private static IServiceProvider RequireServices() => App.Services
        ?? throw new InvalidOperationException("Haven services are not initialized.");

    public MailPage(IMailService mail, IProviderModelClient models) : this(mail, models, null) { }

    public MailPage(IMailService mail, IProviderModelClient models, IVersionedSettingsStore? preferences)
        : this(mail, models, preferences, null) { }

    public MailPage(IMailService mail, IProviderModelClient models, IVersionedSettingsStore? preferences,
        FilesMailCanonicalAttachmentPicker? canonicalAttachmentPicker)
    {
        InitializeComponent();
        _viewModel = new MailPageViewModel(mail, models);
        DataContext = _viewModel;
        _canonicalAttachmentPicker = canonicalAttachmentPicker;
        _viewModel.OriginalComposeRetired += CancelOriginalAttachmentSelection;
        _readingPreferences = preferences;
        PendingReadingPreference = LoadReadingPreferenceAsync();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ComposeEditor.ContentChanged += OnComposeEditorContentChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ApplyResponsiveLayout(Bounds.Width);
            if (_viewModel.IsComposeOpen) _ = SyncComposeEditorAsync();
        };
    }

    public string ResponsiveMode { get; private set; } = "wide";

    public void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        ResetReadingPaneGeometry();
        var columns = MailboxGrid.ColumnDefinitions;
        var mode = MailResponsiveLayoutPolicy.Resolve(width);
        if (mode == MailResponsiveMode.Narrow)
        {
            ResponsiveMode = "narrow";
            FolderPanel.IsVisible = false;
            CompactFolderPicker.IsVisible = true;
            BackToListButton.IsVisible = _showReadingOnNarrow;
            MessagePanel.IsVisible = !_showReadingOnNarrow;
            ReadingPanel.IsVisible = _showReadingOnNarrow;
            columns[0].Width = new GridLength(0);
            columns[1].Width = _showReadingOnNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            columns[2].Width = _showReadingOnNarrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ComposePanel.Width = double.NaN;
            ComposePanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            return;
        }

        if (ReadingPanePlacement != MailReadingPanePlacement.Off) _showReadingOnNarrow = false;
        BackToListButton.IsVisible = false;
        MessagePanel.IsVisible = true;
        ReadingPanel.IsVisible = true;
        ComposePanel.Width = 720;
        ComposePanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        if (mode == MailResponsiveMode.Compact)
        {
            ResponsiveMode = "compact";
            FolderPanel.IsVisible = false;
            CompactFolderPicker.IsVisible = true;
            columns[0].Width = new GridLength(0);
            columns[1].Width = new GridLength(330);
            columns[2].Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            ResponsiveMode = "wide";
            FolderPanel.IsVisible = true;
            CompactFolderPicker.IsVisible = false;
            columns[0].Width = new GridLength(220);
            columns[1].Width = new GridLength(390);
            columns[2].Width = new GridLength(1, GridUnitType.Star);
        }
        ApplyChosenReadingPlacement();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void OnMessageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = MessageList.SelectedItems?.OfType<MailMessageSummary>().ToArray() ?? [];
        _viewModel.SetMessageSelection(selected);
        _viewModel.NotifyMessageSelectionChanged();

        if ((ResponsiveMode != "narrow" && ReadingPanePlacement != MailReadingPanePlacement.Off)
            || selected.Length != 1 || _viewModel.SelectedSummary is null) return;
        _showReadingOnNarrow = true;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnFolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => _viewModel.NotifyFolderSelectionChanged();

    private void OnBackToListClick(object? sender, RoutedEventArgs e)
    {
        _showReadingOnNarrow = false;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnComposeTextChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.NotifyComposeChanged();

    private void OnComposeEditorContentChanged(object? sender, MailRichTextChangedEventArgs e)
    {
        _updatingComposeFromEditor = true;
        try { _viewModel.SetComposeRichBody(e.Html, e.PlainText); }
        finally { _updatingComposeFromEditor = false; }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingComposeFromEditor || !_viewModel.IsComposeOpen) return;
        if (e.PropertyName is nameof(MailPageViewModel.IsComposeOpen)
            or nameof(MailPageViewModel.ComposeBody)
            or nameof(MailPageViewModel.ComposeHtmlBody))
            _ = SyncComposeEditorAsync();
    }

    private Task SyncComposeEditorAsync()
        => ComposeEditor.SetContentAsync(_viewModel.ComposeHtmlBody, _viewModel.ComposeBody);

    private async void OnCloseComposeClick(object? sender, RoutedEventArgs e)
    {
        await ComposeEditor.FlushAsync();
        await _viewModel.CloseComposeSafelyAsync();
    }

    private async void OnRequestSendClick(object? sender, RoutedEventArgs e)
    {
        await ComposeEditor.FlushAsync();
        _viewModel.RequestSendAfterEditorFlush();
    }

    private async void OnAddAttachmentClick(object? sender, RoutedEventArgs e)
    {
        if (_disposed || !PendingAttachmentSelection.IsCompleted) return;
        PendingAttachmentSelection = AddAttachmentToOriginalComposeAsync();
        await PendingAttachmentSelection;
    }

    private void CancelOriginalAttachmentSelection() => _attachmentLifetime?.Cancel();

    private async Task AddAttachmentToOriginalComposeAsync()
    {
        var original = _viewModel.CaptureOriginalComposeSelection();
        if (_disposed || original is null) return;
        _attachmentLifetime?.Dispose();
        using var lifetime = new CancellationTokenSource(); _attachmentLifetime = lifetime;
        bool Current() => !_disposed && !lifetime.IsCancellationRequested && original.IsCurrent;
        try
        {
            if (_canonicalAttachmentPicker is not null)
            {
                var selected = await _canonicalAttachmentPicker.PickAsync(TopLevel.GetTopLevel(this) as Window,
                    original.AccountId, original.DraftId, Current, lifetime.Token);
                if (selected is not null && Current())
                    _viewModel.TryAddOriginalComposeAttachment(original, selected.FileName, selected.MimeType, selected.Bytes.ToArray());
                return;
            }
            // Retain the existing platform picker API when no canonical Files picker was explicitly composed.
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Attach files to email", AllowMultiple = true });
            if (!Current()) return;
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync();
                if (!Current()) return;
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, lifetime.Token);
                if (!Current()) return;
                _viewModel.AddComposeAttachment(file.Name, GuessContentType(file.Name), memory.ToArray());
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { if (Current()) _viewModel.SetOriginalComposeAttachmentStatus(original, "The selected attachment is unavailable. Choose it again from Files."); }
        finally { if (ReferenceEquals(_attachmentLifetime, lifetime)) _attachmentLifetime = null; }
    }

    private async void OnDownloadAttachmentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MailAttachmentDescriptor attachment }) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        try
        {
            var bytes = await _viewModel.DownloadAttachmentAsync(attachment, CancellationToken.None);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save mail attachment",
                SuggestedFileName = attachment.FileName,
                ShowOverwritePrompt = true
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch
        {
            // Mailbox state remains authoritative; a picker failure should not crash the surface.
        }
    }

    private static string GuessContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelOriginalAttachmentSelection();
        _viewModel.OriginalComposeRetired -= CancelOriginalAttachmentSelection;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ComposeEditor.ContentChanged -= OnComposeEditorContentChanged;
        ComposeEditor.Dispose();
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
