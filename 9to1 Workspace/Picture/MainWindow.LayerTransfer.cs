using Avalonia.Controls;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private sealed class OriginalVectorLayerDestination(PictureCompositionTarget vector, PictureCompositionTarget layer, string label)
    {
        internal PictureCompositionTarget Vector { get; } = vector;
        internal PictureCompositionTarget Layer { get; } = layer;
        public override string ToString() => label;
    }
    private sealed class OriginalVectorLayerTransfer(OriginalVectorLayerDestination destination)
    {
        internal OriginalVectorLayerDestination Destination { get; } = destination;
    }
    private OriginalVectorLayerDestination[] _originalVectorLayerDestinations = [];
    private OriginalVectorLayerTransfer? _originalVectorLayerTransfer;
    private PictureEditorSession? _vectorLayerDestinationOwner;
    private Guid _vectorLayerDestinationDocument, _vectorLayerDestinationVector;
    private long _vectorLayerDestinationRevision;
    private bool _updatingVectorLayerDestination, _hasVectorLayerDestinationDraft, _invalidVectorLayerDestination;

    private void ConfigureVectorLayerTransferInputs()
    {
        Find<ComboBox>("VectorDestinationLayerBox").PropertyChanged += (_, changed) =>
        {
            if (changed.Property != ComboBox.SelectedItemProperty || _updatingVectorLayerDestination) return;
            _hasVectorLayerDestinationDraft = true;
            var selected = Find<ComboBox>("VectorDestinationLayerBox").SelectedItem;
            _invalidVectorLayerDestination = selected is not OriginalVectorLayerDestination row
                || !_originalVectorLayerDestinations.Any(original => ReferenceEquals(original, row));
            _originalVectorLayerTransfer = !_invalidVectorLayerDestination && selected is OriginalVectorLayerDestination actual
                ? new(actual) : null;
            RefreshEditor();
        };
    }
    private bool IsCurrentVectorLayerDestinationEpoch() => _session is not null && _selectedVector is { } selected
        && ReferenceEquals(_vectorLayerDestinationOwner, _session) && _vectorLayerDestinationDocument == _session.Document.DocumentId
        && _vectorLayerDestinationRevision == _session.Document.Revision && _vectorLayerDestinationVector == selected.TargetId;
    private void PublishCurrentVectorLayerDestinations()
    {
        _updatingVectorLayerDestination = true;
        try
        {
            _originalVectorLayerTransfer = null; _hasVectorLayerDestinationDraft = _invalidVectorLayerDestination = false;
            _vectorLayerDestinationOwner = _session; _vectorLayerDestinationDocument = _session?.Document.DocumentId ?? Guid.Empty;
            _vectorLayerDestinationRevision = _session?.Document.Revision ?? -1; _vectorLayerDestinationVector = _selectedVector?.TargetId ?? Guid.Empty;
            var owner = _session; var page = owner?.Document.CompositionState is null ? null : PictureCompositionAdapter.Read(owner.Document).Pages[0];
            _originalVectorLayerDestinations = owner is null || page is null || _selectedVector is not { } vector ? [] :
                page.LayerOrder.Select(id =>
                {
                    var layer = page.Layers.Single(layer => layer.LayerId == id);
                    return new OriginalVectorLayerDestination(vector, owner.CaptureComposition(id, PictureCompositionTargetKind.Layer),
                        layer.Name + (layer.IsVisible ? " · visible" : " · hidden") + (layer.IsLocked ? " · locked" : ""));
                }).ToArray();
            var chooser = Find<ComboBox>("VectorDestinationLayerBox"); chooser.ItemsSource = _originalVectorLayerDestinations; chooser.SelectedItem = null;
        }
        finally { _updatingVectorLayerDestination = false; }
    }
    private bool CanTransferOriginalVectorLayer(OriginalVectorLayerTransfer? transfer)
    {
        if (!IsCurrentVectorLayerDestinationEpoch() || transfer is null || !ReferenceEquals(transfer, _originalVectorLayerTransfer)) return false;
        var destination = transfer.Destination;
        if (!_originalVectorLayerDestinations.Any(original => ReferenceEquals(original, destination))
            || !ReferenceEquals(Find<ComboBox>("VectorDestinationLayerBox").SelectedItem, destination)) return false;
        var document = _session!.Document; var graph = PictureCompositionAdapter.Read(document); var page = graph.Pages[0];
        if (!ReferenceEquals(destination.Vector.Owner, _session) || !ReferenceEquals(destination.Layer.Owner, _session)
            || destination.Vector.DocumentRevision != document.Revision || destination.Layer.DocumentRevision != document.Revision
            || destination.Vector.GraphRevision != destination.Layer.GraphRevision || destination.Vector.GraphRevision != graph.RevisionId
            || destination.Vector.DocumentId != document.DocumentId || destination.Layer.DocumentId != document.DocumentId
            || destination.Vector.TargetId != _selectedVector!.TargetId) return false;
        var item = page.Objects.SingleOrDefault(item => item.ObjectId == destination.Vector.TargetId);
        var layer = page.Layers.SingleOrDefault(layer => layer.LayerId == destination.Layer.TargetId);
        return item is not null && layer is not null && item.LayerId != layer.LayerId && !layer.IsLocked
            && page.Layers.Single(source => source.LayerId == item.LayerId).IsLocked == false;
    }
    private void RefreshVectorLayerTransferFields(bool canChangeVector)
    {
        if (_initialized && !_hasVectorLayerDestinationDraft && !IsCurrentVectorLayerDestinationEpoch()) PublishCurrentVectorLayerDestinations();
        var current = IsCurrentVectorLayerDestinationEpoch();
        _layout.Set("CanChooseVectorLayerDestination", canChangeVector && current);
        _layout.Set("CanResetVectorLayerDestination", canChangeVector);
        _layout.Set("CanTransferVectorLayer", canChangeVector && !_invalidVectorLayerDestination && CanTransferOriginalVectorLayer(_originalVectorLayerTransfer));
        _layout.Set("OriginalVectorLayerTransferTarget", _originalVectorLayerTransfer);
        _layout.Set("VectorLayerTransferNotice", _hasVectorLayerDestinationDraft && !current
            ? "This destination belongs to a previous shape or edit. Choose a destination for the current shape to continue."
            : _invalidVectorLayerDestination ? "Choose a layer from this shape’s current destination list."
            : "Moves the editable shape without changing its geometry or source. Locked layers cannot receive it.");
    }
    private void ResetVectorLayerDestination()
    {
        DemandSameSelection(SelectedVector(), PictureCompositionTargetKind.Vector); PublishCurrentVectorLayerDestinations(); RefreshCompositionRows();
    }
    private void ApplySelectedVectorLayerTransfer(object? parameter)
    {
        if (parameter is not OriginalVectorLayerTransfer original || !CanTransferOriginalVectorLayer(original))
            throw new InvalidOperationException("Choose the current shape’s original unlocked destination layer again.");
        var row = original.Destination;
        DemandSameSelection(row.Vector, PictureCompositionTargetKind.Vector);
        DemandSameSelection(row.Layer, PictureCompositionTargetKind.Layer);
        ChangeComposition("Move shape to layer", owner =>
        {
            var result = owner.MoveVectorToLayer(row.Vector, row.Layer);
            _hasVectorLayerDestinationDraft = _invalidVectorLayerDestination = false; _originalVectorLayerTransfer = null;
            return result;
        });
    }
}
