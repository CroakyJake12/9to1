using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private sealed record OriginalPropertiesObservation(FilesNativeBrowserPage Page, HostedItemMetadata Item,
        FilesNativeItemProperties Properties);
    private OriginalPropertiesObservation? _selectedProperties;

    private async Task LoadSelectedPropertiesAsync(FilesNativeBrowserPage page, HostedItemMetadata item,
        long selectionVersion, CancellationToken token)
    {
        var properties = await _browser.ReadNativeItemPropertiesAsync(page, item, _originalActor, OriginalAlive, token)
            .ConfigureAwait(true);
        await RequireReadyAsync(token).ConfigureAwait(true);
        var owner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files Properties owner retired.");
        await RequireOriginalOwnerAsync(owner, token).ConfigureAwait(true);
        if (selectionVersion != Volatile.Read(ref _sharingSelectionVersion) || !ReferenceEquals(_page, page)
            || !ReferenceEquals(_items.SelectedItem, item))
            throw new InvalidOperationException("The selected Files item changed before its Properties were published.");
        _selectedProperties = new(page, item, properties);
        _showTechnicalDetails = true;
        Changed();
    }

    private void ClearOriginalProperties()
    {
        _selectedProperties = null;
        _showTechnicalDetails = false;
    }

    private string CanonicalItemPath(HostedItemMetadata item)
    {
        if (_selectedProperties is not { } observation || !ReferenceEquals(_page, observation.Page)
            || !ReferenceEquals(_items.SelectedItem, observation.Item))
            return "Path: current canonical path unavailable";
        var names = observation.Properties.Ancestors.Select(folder => folder.Name).Append(item.Name);
        var marker = observation.Properties.AncestorPathIssue is { } issue ? $"Partial path ({issue}): " : "Canonical path: ";
        var path = observation.Properties.AncestorPathIssue is null
            ? $"{observation.Properties.ProviderLocation.Name}/{string.Join("/", names)}"
            : $"{observation.Properties.ProviderLocation.Name}/…/{string.Join("/", names)}";
        return marker + path;
    }
}
