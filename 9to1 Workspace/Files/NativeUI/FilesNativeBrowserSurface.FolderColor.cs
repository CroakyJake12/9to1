using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private bool _folderColorVisible;
    private string _folderColorDraft = "#4477AA";
    private FilesFolderPresentation? _folderPresentation;
    private FilesNativeBrowserPage? _folderColorPage;
    private HostedItemMetadata? _folderColorSelection;
    private string _folderColorStatus = "Choose a folder to view its Files colour.";

    public string FolderColorDraft
    {
        get => _folderColorDraft;
        set
        {
            if (_folderColorDraft == value) return;
            _folderColorDraft = value;
            Changed();
        }
    }

    private bool TryGetFolderColorValue(string path, out object? value)
    {
        value = path switch
        {
            "FolderColorVisible" => _folderColorVisible,
            "CanOpenFolderColor" => CanOpenFolderColor,
            "CanSetFolderColor" => CanSetFolderColor,
            "CanResetFolderColor" => CanResetFolderColor,
            "FolderColorStatus" => _folderColorStatus,
            "FolderColorCurrent" => _folderPresentation?.ColorHex ?? "Default appearance",
            "FolderColorDraft" => FolderColorDraft,
            "FolderColorSelection" => _folderColorSelection?.Name ?? "No folder selected",
            _ => null,
        };
        return path is "FolderColorVisible" or "CanOpenFolderColor" or "CanSetFolderColor" or "CanResetFolderColor"
            or "FolderColorStatus" or "FolderColorCurrent" or "FolderColorDraft" or "FolderColorSelection";
    }

    private bool CanOpenFolderColor => Available && _pendingMutation is null && _browser.HasNativeMutationOwner && _page is { IsTrash: false }
        && _items.SelectedItem is HostedItemMetadata { Kind: HostedItemKind.Folder } folder
        && _page.Items.Any(item => ReferenceEquals(item, folder));
    private bool CanSetFolderColor => Available && _folderColorVisible && _folderColorPage is not null
        && _pendingMutation is null && _folderColorSelection is not null && IsCurrentFolderColorSelection();
    private bool CanResetFolderColor => CanSetFolderColor && _folderPresentation is not null;

    private bool IsFolderColorCommand(string command) => command is
        "9to1.Files.FolderColor.Open" or "9to1.Files.FolderColor.Preset.Blue" or "9to1.Files.FolderColor.Preset.Teal"
        or "9to1.Files.FolderColor.Preset.Green" or "9to1.Files.FolderColor.Preset.Amber"
        or "9to1.Files.FolderColor.Preset.Rose" or "9to1.Files.FolderColor.Apply" or "9to1.Files.FolderColor.Reset"
        or "9to1.Files.FolderColor.Close";

    private bool? FolderColorActionAvailable(string command) => command switch
    {
        "9to1.Files.FolderColor.Open" => CanOpenFolderColor,
        "9to1.Files.FolderColor.Preset.Blue" or "9to1.Files.FolderColor.Preset.Teal"
            or "9to1.Files.FolderColor.Preset.Green" or "9to1.Files.FolderColor.Preset.Amber"
            or "9to1.Files.FolderColor.Preset.Rose" => CanSetFolderColor,
        "9to1.Files.FolderColor.Apply" => CanSetFolderColor,
        "9to1.Files.FolderColor.Reset" => CanResetFolderColor,
        "9to1.Files.FolderColor.Close" => Available && _folderColorVisible,
        _ => false,
    };

    private void ClearFolderColor()
    {
        _folderColorVisible = false;
        _folderColorPage = null;
        _folderColorSelection = null;
        _folderPresentation = null;
        _folderColorStatus = "Choose a folder to view its Files colour.";
    }

    private bool IsCurrentFolderColorSelection() => _folderColorVisible && _folderColorPage is not null
        && ReferenceEquals(_page, _folderColorPage) && _folderColorSelection is not null
        && ReferenceEquals(_items.SelectedItem, _folderColorSelection)
        && _folderColorPage.Items.Any(item => ReferenceEquals(item, _folderColorSelection));

    private Task InvokeFolderColorFromNativeEventAsync(string command)
        => TrackOriginalAsync(() => InvokeFolderColorOriginalAsync(command, _lifetime.Token));

    private async Task InvokeFolderColorOriginalAsync(string command, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (FolderColorActionAvailable(command) != true) return;
        var page = _page ?? throw new InvalidOperationException("The Files browser is not open.");
        var folder = _items.SelectedItem as HostedItemMetadata;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        try
        {
            if (FolderColorActionAvailable(command) != true) return;
            if (command == "9to1.Files.FolderColor.Close")
            {
                ClearFolderColor(); Changed(); return;
            }
            RequireFolderColorPage(page);
            _busy = true; Changed();
            try
            {
                await RequireReadyAsync(linked.Token);
                await _browser.RevalidateAsync(page, _originalActor, linked.Token);
                RequireFolderColorPage(page);
                if (command == "9to1.Files.FolderColor.Open")
                {
                    if (folder is not { Kind: HostedItemKind.Folder }) throw new InvalidOperationException("Select a folder.");
                    if (!ReferenceEquals(_items.SelectedItem, folder))
                        throw new UnauthorizedAccessException("The selected folder changed while its colour panel was opening.");
                    _folderColorVisible = true;
                    _folderColorPage = page;
                    _folderColorSelection = folder;
                    _folderPresentation = await _browser.ReadNativeFolderPresentationAsync(page, folder, _originalActor,
                        () => OriginalAlive() && ReferenceEquals(_page, page) && ReferenceEquals(_items.SelectedItem, folder), linked.Token);
                    RequireFolderColorSelection(page, folder);
                    _folderColorDraft = _folderPresentation?.ColorHex ?? "#4477AA";
                    _folderColorStatus = _folderPresentation is null ? "This folder uses the default Files appearance."
                        : $"Current Files colour: {_folderPresentation.ColorHex}.";
                    return;
                }
                var folderSelection = _folderColorSelection
                    ?? throw new UnauthorizedAccessException("The original folder-colour selection retired.");
                RequireFolderColorSelection(page, folderSelection);
                string? color = command switch
                {
                    "9to1.Files.FolderColor.Preset.Blue" => "#4477AA",
                    "9to1.Files.FolderColor.Preset.Teal" => "#2A9D8F",
                    "9to1.Files.FolderColor.Preset.Green" => "#5A9B55",
                    "9to1.Files.FolderColor.Preset.Amber" => "#D99A27",
                    "9to1.Files.FolderColor.Preset.Rose" => "#C75B75",
                    "9to1.Files.FolderColor.Reset" => null,
                    _ => FolderColorDraft,
                };
                if (command != "9to1.Files.FolderColor.Reset") _folderColorDraft = color!;
                _folderColorStatus = "Requesting Home review…";
                Changed();
                var prepared = await _browser.PrepareNativeFolderColorAsync(page, folderSelection, color,
                    _originalActor, () => OriginalAlive() && ReferenceEquals(_page, page)
                        && ReferenceEquals(_folderColorPage, page) && ReferenceEquals(_folderColorSelection, folderSelection)
                        && ReferenceEquals(_items.SelectedItem, folderSelection), _mutationSession, linked.Token);
                _pendingMutation = prepared;
                RequireFolderColorSelection(page, folderSelection);
                _folderColorStatus = prepared.Approval.IsAllowed ? "Home approved this request. Choose Apply to commit it."
                    : prepared.Approval.State == HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.PendingApproval
                        ? "Review this folder-colour change in Home, then choose Apply."
                        : prepared.Approval.Message;
            }
            finally { _busy = false; Changed(); }
        }
        finally { _operations.Release(); }
    }

    private void RequireFolderColorSelection(FilesNativeBrowserPage page, HostedItemMetadata folder)
    {
        RequireFolderColorPage(page);
        if (!ReferenceEquals(_items.SelectedItem, folder)
            || !_folderColorVisible || !ReferenceEquals(_folderColorPage, page)
            || !ReferenceEquals(_folderColorSelection, folder))
            throw new UnauthorizedAccessException("The original folder colour selection changed while the operation was pending.");
    }

    private void RequireFolderColorPage(FilesNativeBrowserPage page)
    {
        if (!OriginalAlive() || !ReferenceEquals(_page, page))
            throw new UnauthorizedAccessException("The original Files page retired while folder colour was pending.");
    }
}
