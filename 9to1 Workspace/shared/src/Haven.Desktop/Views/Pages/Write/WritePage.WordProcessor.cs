using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Write;

public sealed partial class WritePage
{
    private INotesAttachmentStore? _wordAttachments;

    private void OnWordDocumentChanged(object? sender, EventArgs e)
    {
        if (!CanPublishOriginal) return;
        if (_route.Document is { } current) Document = current;
        MarkDirty();
    }

    private void OnWordImageRequested(object? sender, EventArgs e) => RunOriginalEvent(() => OnWordImageRequestedCoreAsync(sender, e));

    private async Task OnWordImageRequestedCoreAsync(object? sender, EventArgs e)
    {
        if (_wordAttachments is null) { Publish(() => _route.SetStatus("Image insertion is unavailable because the Notes attachment store is not registered.")); return; }
        var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) { Publish(() => _route.SetStatus("Image insertion isn’t available from this platform surface.")); return; }
        var files = await ObserveOriginalAsync(() => top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Insert image", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp"] }] })); var file = files.FirstOrDefault(); if (file is null || !CanPublishOriginal) return;
        var local = file.TryGetLocalPath(); string? temporary = null;
        try
        {
            if (string.IsNullOrWhiteSpace(local)) { temporary = Path.Combine(Path.GetTempPath(), "haven-write-image-" + Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name)); var source = await ObserveOriginalAsync(() => file.OpenReadAsync()); await using var sourceDisposal = OwnOriginalStreamDisposal(source); var target = File.Create(temporary); await using var targetDisposal = OwnOriginalStreamDisposal(target); await ObserveOriginalAsync(() => source.CopyToAsync(target)); local = temporary; }
            var media = await ObserveOriginalAsync(() => _wordAttachments.ImportAsync(local!, CancellationToken.None)); Publish(() => _route.InsertMedia(media)); Publish(() => _route.SetStatus("Inserted " + file.Name));
        }
        catch (Exception ex) {
            _work.Executing?.Retain(ex); Publish(() => _route.SetStatus("Couldn’t insert this image: " + ex.Message)); }
        finally { if (temporary is not null) DeleteTemporaryFile(temporary); }
    }
}
