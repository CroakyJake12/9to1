using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Mesh;

/// <summary>
/// The Mesh page's synchronization section. Every row here comes from real per-destination state: a device that
/// was unreachable stays listed as waiting, a refused share says it was refused, and a conflict shows both
/// revisions and both content hashes rather than implying one side won. Nothing in this view is a placeholder
/// or a promise that sharing happened.
/// </summary>
internal sealed partial class MeshHavenScene
{
    private const int SynchronizationRowLimit = 12;

    private void BuildSynchronizationContent(Container parent)
    {
        var card = Card("Mesh.Synchronization");
        card.Add(Heading("Synchronization", TextLevel.H2));
        card.Add(Muted("A resource is shared only when you allow it on both devices. Each destination keeps its own result, so a device that was unreachable stays visibly waiting rather than being counted as synchronized."));

        if (_viewModel.SyncDeliveries.Count == 0 && _viewModel.PendingSyncs.Count == 0 && _viewModel.SyncConflicts.Count == 0)
        {
            card.Add(Muted("Nothing has been synchronized with this device yet."));
            parent.Add(card);
            return;
        }

        if (_viewModel.SyncConflicts.Count > 0)
        {
            var conflicts = _viewModel.SyncConflicts.Count;
            card.Add(Heading($"{conflicts} unresolved conflict{(conflicts == 1 ? string.Empty : "s")}", TextLevel.H3));
            foreach (var conflict in _viewModel.SyncConflicts.Take(SynchronizationRowLimit))
                card.Add(Muted($"{conflict.ResourceType} · {Short(conflict.ResourceId)} · this device holds revision {conflict.Current.Revision} ({ShortHash(conflict.Current.ContentHash)}) · received revision {conflict.Incoming.Revision} ({ShortHash(conflict.Incoming.ContentHash)}) · both copies kept, neither applied"));
            if (conflicts > SynchronizationRowLimit)
                card.Add(Muted($"Showing {SynchronizationRowLimit} of {conflicts}. Resolve the rest through the owning app rather than from this list."));
        }

        if (_viewModel.PendingSyncs.Count > 0)
        {
            var waiting = _viewModel.PendingSyncs.Count;
            card.Add(Heading($"Waiting for {waiting} destination{(waiting == 1 ? string.Empty : "s")}", TextLevel.H3));
            foreach (var intent in _viewModel.PendingSyncs.Take(SynchronizationRowLimit))
                card.Add(Muted($"{intent.ResourceType} · {Short(intent.ResourceId)} · {PeerName(intent.PeerDeviceId)} · {intent.Detail}"));
            card.Add(Muted("A waiting item is re-offered with the content the owning app holds at that moment, never an older copy kept here."));
        }

        card.Add(Heading("Synchronization results", TextLevel.H3));
        foreach (var delivery in _viewModel.SyncDeliveries.Take(SynchronizationRowLimit))
            card.Add(Muted($"{delivery.ResourceType} · {Short(delivery.ResourceId)} · revision {delivery.Revision} · {PeerName(delivery.PeerDeviceId)} · {delivery.State} · {delivery.Detail}"));
        if (_viewModel.SyncDeliveries.Count > SynchronizationRowLimit)
            card.Add(Muted($"Showing the most recent {SynchronizationRowLimit} of {_viewModel.SyncDeliveries.Count} results."));

        parent.Add(card);
    }

    private string PeerName(Guid deviceId) =>
        _viewModel.Devices.FirstOrDefault(device => device.Peer.DeviceId == deviceId)?.Peer.DisplayName ?? Short(deviceId);

    private static string Short(Guid value) => value.ToString("N")[..8];

    private static string ShortHash(string hash) =>
        string.IsNullOrWhiteSpace(hash) ? "no content hash" : hash[..Math.Min(12, hash.Length)];
}
