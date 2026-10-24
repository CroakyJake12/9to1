using Haven.Application;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private FilesNativeBrowserPage? _textPreviewPage;
    private HostedItemMetadata? _textPreviewRow;
    private FilesOriginalTextPreview? _textPreview;
    private long _textPreviewSelectionEpoch;

    private bool TryGetTextPreviewValue(string path, out object? value)
    {
        value = path switch
        {
            "CanPreviewText" => IsActionAvailable("9to1.Files.Preview"),
            "CanCloseTextPreview" => IsActionAvailable("9to1.Files.Preview.Close"),
            "TextPreviewVisible" => _textPreview is not null,
            "TextPreviewTitle" => _textPreview?.Name ?? "",
            "TextPreviewMessage" => _textPreview?.Message ?? "",
            "TextPreviewContent" => _textPreview?.Text ?? "",
            _ => null
        };
        return path is "CanPreviewText" or "CanCloseTextPreview" or "TextPreviewVisible" or "TextPreviewTitle"
            or "TextPreviewMessage" or "TextPreviewContent";
    }
    private bool CanPreviewTextSelection => _browser.HasOriginalTextPreviewOwner && _page is { IsTrash: false } page &&
        _items.SelectedItem is HostedItemMetadata { Kind: HostedItemKind.File, CurrentRevisionId: not null } row &&
        page.Items.Any(item => ReferenceEquals(item, row));
    private bool CanOpenTextSelection => CanPreviewTextSelection &&
        _items.SelectedItem is HostedItemMetadata row && FilesNativeBrowserService.IsTextPreviewCandidate(row);

    private void ClearOriginalTextPreview()
    {
        Interlocked.Increment(ref _textPreviewSelectionEpoch);
        _textPreview = null; _textPreviewPage = null; _textPreviewRow = null;
    }
    private void WithdrawChangedOriginalTextPreview()
    {
        if (_textPreview is not null && (!ReferenceEquals(_page, _textPreviewPage) ||
            !ReferenceEquals(_items.SelectedItem, _textPreviewRow) || !OriginalAlive())) ClearOriginalTextPreview();
    }
    private async Task PreviewOriginalTextAsync(FilesNativeBrowserPage page, HostedItemMetadata row, CancellationToken token)
    {
        var epoch = Interlocked.Read(ref _textPreviewSelectionEpoch);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        var result = await originals.RunToOriginalSettlementAsync(async () =>
        {
            // The selected original row is captured by the actual native action. Source callbacks
            // never inspect UI controls from background threads. A later selection withdraws the
            // result at final UI publication, while genuine source failures still propagate.
            Action<Action> Own = body => originals.Invoke(() => { body(); return true; });
            Action<Task> Retain = actual => originals.Track(actual);
            var preview = await originals.AwaitAsync(originals.Invoke(() => _browser.ReadOriginalTextPreviewWithinSourceAsync(
                page, row, _originalActor, OriginalAlive, Own, Retain, token)));
            await originals.AwaitAsync(originals.Invoke(() => _browser.RevalidateAsync(page, _originalActor, token)));
            var current = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files preview page retired.");
            await originals.AwaitAsync(originals.Invoke(() => RequireReadyAsync(token)));
            await originals.AwaitAsync(originals.Invoke(() => RequireOriginalOwnerAsync(current, token)));
            return preview;
        });
        RequireOriginalAlive(token);
        if (epoch != Interlocked.Read(ref _textPreviewSelectionEpoch) || !ReferenceEquals(_page, page) ||
            !ReferenceEquals(_items.SelectedItem, row) || !page.Items.Any(item => ReferenceEquals(item, row)))
            return; // A changed native selection cannot display an earlier successful result.
        _textPreviewPage = page; _textPreviewRow = row; _textPreview = result;
        _status = result.Message;
    }
}
