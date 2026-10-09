using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

public sealed partial class MainWindow
{
    private PictureCompositionTarget? _selectedLayer, _selectedVector;
    private Guid? _selectedVectorPath, _selectedVectorSubpath, _selectedVectorNode;
    private const int VectorNodePageSize = 32;
    private int _vectorNodeOffset;
    private readonly Dictionary<string, decimal?> _vectorCoordinateBaselines = [];

    private void ConfigureCompositionInputs()
    {
        ConfigureVectorTransformInputs();
        ConfigureVectorStrokeInputs();
        ConfigureVectorFillInputs();
        ConfigureVectorAnchorInputs();
        ConfigureVectorLayerTransferInputs();
        var pointCount = Find<NumericUpDown>("PrimitivePointCountBox");
        pointCount.Minimum = 3; pointCount.Maximum = DocumentVectorPrimitives.MaximumPointCount;
        pointCount.FormatString = "0";
        foreach (var name in new[] { "VectorXBox", "VectorYBox", "VectorWidthBox", "VectorHeightBox" })
        {
            var input = Find<NumericUpDown>(name);
            input.Minimum = name is "VectorWidthBox" or "VectorHeightBox" ? .001m : -32768;
            input.Maximum = 32768; input.FormatString = "0.###";
        }
        foreach (var name in new[] { "VectorNodeXBox", "VectorNodeYBox", "VectorControl1XBox", "VectorControl1YBox", "VectorControl2XBox", "VectorControl2YBox" })
        {
            var input = Find<NumericUpDown>(name);
            input.Minimum = decimal.MinValue; input.Maximum = decimal.MaxValue; input.FormatString = "G29";
        }
    }

    private void AddPrimitive(DocumentVectorPrimitive primitive)
    {
        var points = primitive is DocumentVectorPrimitive.Polygon or DocumentVectorPrimitive.Star
            ? Find<NumericUpDown>("PrimitivePointCountBox").Value ?? 5 : 5;
        ChangeComposition("Insert " + primitive.ToString().ToLowerInvariant(), owner =>
        {
            if (points != decimal.Truncate(points)) throw new ArgumentException("Enter a whole number of polygon sides or star points.");
            var shape = DocumentVectorPrimitives.Create(primitive, checked((int)points));
            return owner.AddSharedVector(new HomeVectorShapeObjectHandler().Project(shape), _selectedLayer);
        });
    }

    private void RefreshCompositionRows()
    {
        var document = _session?.Document;
        var page = document?.CompositionState is null ? null : PictureCompositionAdapter.Read(document).Pages[0];
        var canCompose = document is not null && _initialized && !_busy && !_commandActive && !_closing && !_retiring;
        PictureCompositionTarget? RefreshSelection(PictureCompositionTarget? target)
        {
            if (target is null || _session is null || document is null || page is null ||
                !ReferenceEquals(target.Owner, _session) || target.DocumentId != document.DocumentId) return null;
            var exists = target.Kind == PictureCompositionTargetKind.Layer
                ? page.Layers.Any(layer => layer.LayerId == target.TargetId)
                : page.Objects.Any(item => item.ObjectId == target.TargetId && item.ObjectTypeId == "drawing.vector");
            // The UI maintains this owner's actual selection, obtaining a fresh
            // source-issued receipt after its own edit. External stale receipts
            // still fail the session's exact revision and owner checks.
            return exists ? _session.CaptureComposition(target.TargetId, target.Kind) : null;
        }
        _selectedLayer = RefreshSelection(_selectedLayer);
        _selectedVector = RefreshSelection(_selectedVector);
        _layout.Set("CanCompose", canCompose);
        _layout.Set("LayerRows", page is null ? Array.Empty<Dictionary<string, object?>>() : page.LayerOrder.Select(id =>
        {
            var layer = page.Layers.Single(layer => layer.LayerId == id);
            return new Dictionary<string, object?> { ["Key"] = document!.DocumentId.ToString("N") + ":" + document.Revision + ":" + id.ToString("D"),
                ["Label"] = layer.Name + (layer.IsVisible ? " · visible" : " · hidden") + (layer.IsLocked ? " · locked" : ""),
                ["Target"] = _session!.CaptureComposition(id, PictureCompositionTargetKind.Layer) };
        }).ToArray());
        var chosenLayer = _selectedLayer is null ? null : page!.Layers.Single(layer => layer.LayerId == _selectedLayer.TargetId);
        var layerIndex = chosenLayer is null ? -1 : page!.LayerOrder.IndexOf(chosenLayer.LayerId);
        _layout.Set("CanChangeLayer", canCompose && chosenLayer is not null);
        _layout.Set("CanInsertShape", canCompose && chosenLayer?.IsLocked != true);
        _layout.Set("LayerVisibilityLabel", chosenLayer?.IsVisible == false ? "Show selected layer" : "Hide selected layer");
        _layout.Set("LayerLockLabel", chosenLayer?.IsLocked == true ? "Unlock selected layer" : "Lock selected layer");
        _layout.Set("CanMoveLayerEarlier", canCompose && layerIndex > 0);
        _layout.Set("CanMoveLayerLater", canCompose && layerIndex >= 0 && layerIndex < page!.LayerOrder.Count - 1);
        var vectors = page?.ObjectOrder.Select(id => page.Objects.Single(item => item.ObjectId == id))
            .Where(item => item.ObjectTypeId == "drawing.vector").ToArray() ?? [];
        _layout.Set("VectorRows", vectors.Select(item => new Dictionary<string, object?> { ["Key"] = document!.DocumentId.ToString("N") + ":" + document.Revision + ":" + item.ObjectId.ToString("D"),
            ["Label"] = "Shape: " + HomeVectorShapeObjectHandler.ReadCanonical(PictureCompositionAdapter.ReadVector(item).Content, item.ObjectId).Name,
            ["Target"] = _session!.CaptureComposition(item.ObjectId, PictureCompositionTargetKind.Vector) }).ToArray());
        var chosenVector = _selectedVector is null ? null : vectors.Single(item => item.ObjectId == _selectedVector.TargetId);
        var chosenShape = chosenVector is null ? null : HomeVectorShapeObjectHandler.ReadCanonical(
            PictureCompositionAdapter.ReadVector(chosenVector).Content, chosenVector.ObjectId);
        if (chosenShape is null || !chosenShape.Paths.Any(path => path.Id == _selectedVectorPath)) _selectedVectorPath = null;
        var canChangeVector = canCompose && chosenVector is not null && !page!.Layers.Single(layer => layer.LayerId == chosenVector.LayerId).IsLocked;
        _layout.Set("CanChangeVector", canChangeVector);
        RefreshVectorTransformFields(chosenShape, canChangeVector);
        RefreshVectorLayerTransferFields(canChangeVector);
        _layout.Set("CanChangeVectorPath", canChangeVector && _selectedVectorPath is not null);
        _layout.Set("SelectedVectorLabel", chosenShape is null ? "Select a shape to edit its placement and paths." : "Selected: " + chosenShape.Name);
        _layout.Set("VectorPathRows", chosenShape is null ? Array.Empty<Dictionary<string, object?>>() : chosenShape.Paths.Select((path, index) => new Dictionary<string, object?>
        { ["Key"] = document!.DocumentId.ToString("N") + ":" + document.Revision + ":" + path.Id.ToString("D"), ["Label"] = "Path " + (index + 1) + (_selectedVectorPath == path.Id ? " · selected" : ""),
            ["Target"] = new PictureVectorPathTarget(_selectedVector!, path.Id) }).ToArray());
        var chosenPath = chosenShape?.Paths.SingleOrDefault(path => path.Id == _selectedVectorPath);
        RefreshVectorStrokeFields(chosenPath, canChangeVector && chosenPath is not null);
        RefreshVectorFillFields(chosenPath, canChangeVector && chosenPath is not null);
        var chosenSubpath = chosenPath?.Subpaths.SingleOrDefault(subpath => subpath.Id == _selectedVectorSubpath);
        var chosenNode = chosenSubpath?.Nodes.SingleOrDefault(node => node.Id == _selectedVectorNode);
        if (chosenNode is null) { _selectedVectorSubpath = null; _selectedVectorNode = null; }
        var nodeRows = new List<Dictionary<string, object?>>();
        var orderedNodes = chosenPath?.Subpaths.SelectMany((subpath, subpathIndex) => subpath.Nodes.Select((node, nodeIndex) =>
            (Subpath: subpath, SubpathIndex: subpathIndex, Node: node, NodeIndex: nodeIndex))).ToArray() ?? [];
        _vectorNodeOffset = orderedNodes.Length == 0 ? 0 : Math.Clamp(_vectorNodeOffset, 0,
            ((orderedNodes.Length - 1) / VectorNodePageSize) * VectorNodePageSize);
        var capturedNodes = chosenPath is null ? new Dictionary<(Guid, Guid), PictureVectorNodeTarget>() :
            _session!.CaptureVectorNodes(_selectedVector!, chosenPath.Id).ToDictionary(target => (target.SubpathId, target.NodeId));
        if (chosenNode is not null && (!CanDisplayNode(chosenNode) || !capturedNodes.ContainsKey((chosenSubpath!.Id, chosenNode.Id))))
        { chosenNode = null; _selectedVectorSubpath = _selectedVectorNode = null; }
        foreach (var entry in orderedNodes.Skip(_vectorNodeOffset).Take(VectorNodePageSize))
        {
            var node = entry.Node; var subpath = entry.Subpath;
            var unambiguous = capturedNodes.TryGetValue((subpath.Id, node.Id), out var target);
            var representable = CanDisplayNode(node);
            nodeRows.Add(new() {
                ["Key"] = document!.DocumentId.ToString("N") + ":" + document.Revision + ":" + chosenPath!.Id + ":" + subpath.Id + ":" + node.Id,
                ["Label"] = "Node " + (entry.SubpathIndex + 1) + "." + (entry.NodeIndex + 1) + (node.Id == _selectedVectorNode && subpath.Id == _selectedVectorSubpath ? " · selected" : "") +
                    (unambiguous ? "" : " · reused ID; retained") + (representable ? "" : " · coordinates outside editor range; retained"),
                ["CanChoose"] = canChangeVector && unambiguous && representable, ["Target"] = target });
        }
        _layout.Set("VectorNodeRows", nodeRows.ToArray());
        _layout.Set("VectorNodePageLabel", orderedNodes.Length == 0 ? "No path selected" :
            "Nodes " + (_vectorNodeOffset + 1) + "–" + Math.Min(_vectorNodeOffset + VectorNodePageSize, orderedNodes.Length) + " of " + orderedNodes.Length);
        _layout.Set("CanPreviousVectorNodePage", canChangeVector && _vectorNodeOffset > 0);
        _layout.Set("CanNextVectorNodePage", canChangeVector && _vectorNodeOffset + VectorNodePageSize < orderedNodes.Length);
        var hasIncomingSegment = chosenNode is not null && chosenSubpath!.Nodes[0].Id != chosenNode.Id;
        _layout.Set("CanChangeVectorNode", canChangeVector && chosenNode is not null);
        RefreshVectorAnchorFields(chosenNode, canChangeVector && chosenNode is not null, chosenSubpath?.Nodes.Count ?? 0);
        RefreshVectorSubpathFields(chosenSubpath, canChangeVector && chosenNode is not null);
        _layout.Set("CanConvertVectorSegmentLine", canChangeVector && hasIncomingSegment && chosenNode!.IncomingSegment != DocumentVectorSegmentKind.Line);
        _layout.Set("CanConvertVectorSegmentQuadratic", canChangeVector && hasIncomingSegment && chosenNode!.IncomingSegment != DocumentVectorSegmentKind.Quadratic);
        _layout.Set("CanConvertVectorSegmentCubic", canChangeVector && hasIncomingSegment && chosenNode!.IncomingSegment != DocumentVectorSegmentKind.Cubic);
        _layout.Set("CanChangeVectorControl1", canChangeVector && hasIncomingSegment && chosenNode!.IncomingSegment is DocumentVectorSegmentKind.Quadratic or DocumentVectorSegmentKind.Cubic);
        _layout.Set("CanChangeVectorControl2", canChangeVector && hasIncomingSegment && chosenNode!.IncomingSegment == DocumentVectorSegmentKind.Cubic);
        _layout.Set("SelectedVectorNodeLabel", chosenNode is null ? "Select a path and node to edit its local coordinates." :
            "Selected node · " + chosenNode.IncomingSegment + " incoming segment. Coordinates use this shape’s view box.");
        var retained = vectors.SelectMany(item => new HomeVectorShapeObjectHandler().Render(PictureCompositionAdapter.ReadVector(item))
            .RetainedUnsupportedProperties.Select(property => item.Accessibility.Name + ": " + property)).ToArray();
        _layout.Set("CompositionRetainedProperties", retained.Length == 0 ? "" : "Preserved fields outside this rendering slice: " + string.Join(", ", retained));
        _layout.Set("CompositionStatus", page is null ? "Add a shape or layer to begin a shared composition." :
            page.Layers.Count + " layers · " + vectors.Length + " editable shapes. Positions use original image pixels before raster edits.");
    }

    private PictureCompositionTarget SelectedLayer() => _selectedLayer ?? throw new InvalidOperationException("Select a current layer.");
    private PictureCompositionTarget SelectedVector() => _selectedVector ?? throw new InvalidOperationException("Select a current shape.");

    private void SelectLayer(PictureCompositionTarget target)
    {
        DemandSameSelection(target, PictureCompositionTargetKind.Layer);
        _selectedLayer = target;
        var layer = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Layers.Single(layer => layer.LayerId == target.TargetId);
        Find<TextBox>("LayerNameBox").Text = layer.Name;
    }

    private void SelectVector(PictureCompositionTarget target)
    {
        DemandSameSelection(target, PictureCompositionTargetKind.Vector);
        _selectedVector = target; _selectedVectorPath = _selectedVectorSubpath = _selectedVectorNode = null; _vectorNodeOffset = 0;
        var item = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Objects.Single(item => item.ObjectId == target.TargetId);
        Find<NumericUpDown>("VectorXBox").Value = (decimal)item.Geometry.X;
        Find<NumericUpDown>("VectorYBox").Value = (decimal)item.Geometry.Y;
        Find<NumericUpDown>("VectorWidthBox").Value = (decimal)item.Geometry.Width;
        Find<NumericUpDown>("VectorHeightBox").Value = (decimal)item.Geometry.Height;
    }

    private void SelectVectorPath(PictureVectorPathTarget target)
    {
        DemandSameSelection(target.Vector, PictureCompositionTargetKind.Vector);
        if (_selectedVector?.TargetId != target.Vector.TargetId) throw new InvalidOperationException("Select this current shape first.");
        var item = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(PictureCompositionAdapter.ReadVector(item).Content, item.ObjectId);
        var path = shape.Paths.Single(path => path.Id == target.PathId);
        _selectedVectorPath = path.Id; _selectedVectorSubpath = _selectedVectorNode = null; _vectorNodeOffset = 0;
    }

    private void MoveVectorNodePage(int direction)
    {
        _vectorNodeOffset = Math.Max(0, checked(_vectorNodeOffset + direction * VectorNodePageSize));
    }

    private void SelectVectorNode(PictureVectorNodeTarget target)
    {
        DemandSameSelection(target.Vector, PictureCompositionTargetKind.Vector);
        if (_selectedVector?.TargetId != target.Vector.TargetId || _selectedVectorPath != target.PathId)
            throw new InvalidOperationException("Select this current shape and path first.");
        var item = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var node = PictureVectorNodeEditor.ReadNode(PictureCompositionAdapter.ReadVector(item), target.PathId, target.SubpathId, target.NodeId);
        if (!CanDisplayNode(node)) throw new NotSupportedException("The retained coordinates exceed this editor’s numeric range.");
        _selectedVectorSubpath = target.SubpathId; _selectedVectorNode = target.NodeId;
        void Display(string name, double value)
        {
            var input = Find<NumericUpDown>(name); input.Value = (decimal)value;
            _vectorCoordinateBaselines[name] = input.Value;
        }
        Display("VectorNodeXBox", node.X); Display("VectorNodeYBox", node.Y);
        Display("VectorControl1XBox", node.Control1?.X ?? 0); Display("VectorControl1YBox", node.Control1?.Y ?? 0);
        Display("VectorControl2XBox", node.Control2?.X ?? 0); Display("VectorControl2YBox", node.Control2?.Y ?? 0);
    }

    private static bool CanDisplayNode(DocumentVectorNode node)
    {
        static bool Fits(double value) => double.IsFinite(value) && value > (double)decimal.MinValue && value < (double)decimal.MaxValue && (value == 0 || (decimal)value != 0);
        return Fits(node.X) && Fits(node.Y) && (node.Control1 is null || (Fits(node.Control1.X) && Fits(node.Control1.Y))) &&
            (node.Control2 is null || (Fits(node.Control2.X) && Fits(node.Control2.Y)));
    }

    private PictureVectorNodeTarget SelectedVectorNode() => _session!.CaptureVectorNode(SelectedVector(),
        _selectedVectorPath ?? throw new InvalidOperationException("Select a shape path."),
        _selectedVectorSubpath ?? throw new InvalidOperationException("Select a shape subpath."),
        _selectedVectorNode ?? throw new InvalidOperationException("Select a shape node."));

    private void MoveSelectedVectorNode(int? controlIndex)
    {
        var target = SelectedVectorNode();
        var prefix = controlIndex is { } index ? "VectorControl" + index : "VectorNode";
        var item = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Objects.Single(item => item.ObjectId == target.Vector.TargetId);
        var node = PictureVectorNodeEditor.ReadNode(PictureCompositionAdapter.ReadVector(item), target.PathId, target.SubpathId, target.NodeId);
        var originalPoint = controlIndex is null ? node.Point : controlIndex == 1 ? node.Control1 : node.Control2;
        if (originalPoint is null) throw new InvalidOperationException("This node has no selected curve control.");
        double ReadCoordinate(string name, double original)
        {
            var value = Find<NumericUpDown>(name).Value ?? throw new ArgumentException("Enter a finite local coordinate.");
            // Numeric input is decimal. An untouched coordinate must retain the
            // canonical double, even when display conversion rounds its last digits.
            return _vectorCoordinateBaselines.TryGetValue(name, out var baseline) && baseline == value ? original : (double)value;
        }
        var x = ReadCoordinate(prefix + "XBox", originalPoint.X);
        var y = ReadCoordinate(prefix + "YBox", originalPoint.Y);
        ChangeComposition(controlIndex is null ? "Move shape node" : "Move curve control", owner => controlIndex is { } control
            ? owner.MoveVectorControlPoint(target, control, x, y) : owner.MoveVectorNode(target, x, y));
    }

    private void DemandSameSelection(PictureCompositionTarget target, PictureCompositionTargetKind kind)
    {
        if (_session is null) throw new InvalidOperationException("Open a Picture document first.");
        var current = _session.CaptureComposition(target.TargetId, kind);
        if (!ReferenceEquals(target.Owner, current.Owner) || target.Kind != kind || target.DocumentId != current.DocumentId ||
            target.DocumentRevision != current.DocumentRevision || target.GraphRevision != current.GraphRevision)
            throw new InvalidOperationException("Revision conflict: select this current composition again.");
    }

    private CanvasRect ReadVectorPlacement() => new((double)(Find<NumericUpDown>("VectorXBox").Value ?? 0),
        (double)(Find<NumericUpDown>("VectorYBox").Value ?? 0), (double)(Find<NumericUpDown>("VectorWidthBox").Value ?? 0),
        (double)(Find<NumericUpDown>("VectorHeightBox").Value ?? 0));

    private void ToggleLayerVisibility()
    {
        var target = SelectedLayer();
        var layer = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Layers.Single(layer => layer.LayerId == target.TargetId);
        ChangeComposition("Change layer visibility", owner => owner.SetLayerVisibility(target, !layer.IsVisible));
    }
    private void ToggleLayerLock()
    {
        var target = SelectedLayer();
        var layer = PictureCompositionAdapter.Read(_session!.Document).Pages[0].Layers.Single(layer => layer.LayerId == target.TargetId);
        ChangeComposition("Change layer lock", owner => owner.SetLayerLocked(target, !layer.IsLocked));
    }
    private void MoveSelectedLayer(int offset)
    {
        var target = SelectedLayer();
        var index = PictureCompositionAdapter.Read(_session!.Document).Pages[0].LayerOrder.IndexOf(target.TargetId);
        ChangeComposition("Reorder layer", owner => owner.MoveLayer(target, checked(index + offset)));
    }
    private void ChangeComposition(string label, Func<PictureEditorSession, Bitmap> change)
    {
        if (_session is null) return;
        try { _compareButton.IsChecked = false; SetPreview(change(_session)); _statusText.Text = label + " · original source retained."; }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        { ShowError(label + " failed: " + error.Message); }
    }
}
