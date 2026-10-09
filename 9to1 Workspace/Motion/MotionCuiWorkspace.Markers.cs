using System.Globalization;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Motion;

public sealed partial class MotionCuiWorkspace
{
    private Guid? _markerId;
    private bool _snapEnabled = true;
    private MotionProject? _markerSnapshot;
    private Guid _markerSnapshotSequenceId;
    private MarkerRow[] _markerRows = [];
    private MotionMarker? SelectedMarker => (Sequence.Markers ?? []).SingleOrDefault(marker => marker.MarkerId == _markerId);
    private MarkerRow[] OriginalMarkerRows
    {
        get
        {
            if (!ReferenceEquals(_markerSnapshot, Project) || _markerSnapshotSequenceId != _sequenceId)
            {
                _markerSnapshot = Project; _markerSnapshotSequenceId = _sequenceId;
                _markerRows = (Sequence.Markers ?? []).OrderBy(marker => marker.Frame).ThenBy(marker => marker.MarkerId)
                    .Select(marker => new MarkerRow(this, Project, _sequenceId, marker)).ToArray();
            }
            return _markerRows;
        }
    }
    private sealed class MarkerRow(MotionCuiWorkspace owner, MotionProject project, Guid sequenceId, MotionMarker marker)
    {
        internal MotionCuiWorkspace Owner { get; } = owner;
        internal MotionProject Snapshot { get; } = project;
        internal Guid SequenceId { get; } = sequenceId;
        internal MotionMarker Original { get; } = marker;
        internal string Key => Original.MarkerId.ToString("N");
        internal string Label => $"{Original.Frame} · {Original.Name}";
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not MarkerRow row || !IsCurrentMarkerRow(row)) return false;
        value = path switch { "Key" => row.Key, "Label" => row.Label, "Target" => row, _ => null };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    private bool IsCurrentMarkerRow(MarkerRow row)
        => !_retiring && !_disposed && ReferenceEquals(row.Owner, this) && ReferenceEquals(row.Snapshot, Project)
            && row.SequenceId == _sequenceId && OriginalMarkerRows.Any(original => ReferenceEquals(original, row))
            && (Sequence.Markers ?? []).Any(marker => ReferenceEquals(marker, row.Original));
    private MarkerRow DemandMarkerRow(object? value)
        => value is MarkerRow row && IsCurrentMarkerRow(row) ? row
            : throw new InvalidOperationException("The marker selection changed. Select the current marker again.");
    private bool ValidateMarkerParameter(string command, object? value)
        => command is "9to1.Motion.SelectMarker" or "9to1.Motion.JumpMarker" or "9to1.Motion.UpdateMarker" or "9to1.Motion.DeleteMarker"
            ? value is MarkerRow row && IsCurrentMarkerRow(row)
                && (command is not ("9to1.Motion.UpdateMarker" or "9to1.Motion.DeleteMarker") || row.Original.MarkerId == _markerId)
            : value is null;
    private bool TryGetMarkerValue(string path, out object? value)
    {
        value = path switch
        {
            "Markers" => OriginalMarkerRows,
            "SelectedMarker" => OriginalMarkerRows.SingleOrDefault(row => row.Original.MarkerId == _markerId),
            "MarkerSelection" => SelectedMarker is { } marker ? $"{marker.Name} · frame {marker.Frame}" : "Choose a marker to edit it.",
            "SnapEnabled" => _snapEnabled,
            "SnappingLabel" => _snapEnabled ? "Snapping on" : "Snapping off",
            _ => null
        };
        return path is "Markers" or "SelectedMarker" or "MarkerSelection" or "SnapEnabled" or "SnappingLabel";
    }
    public long SnapTimelineFrame(long frame, long toleranceFrames)
    {
        if (frame < 0 || toleranceFrames < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        if (_retiring || _disposed || !_snapEnabled) return frame;
        long? ReadRange(string key) => long.TryParse(_drafts[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        return MotionMarkers.SnapFrame(Sequence, frame, toleranceFrames, ReadRange("RangeStart"), ReadRange("RangeEnd"));
    }
    private bool? MarkerActionAvailability(string command) => command switch
    {
        "9to1.Motion.SelectMarker" or "9to1.Motion.JumpMarker" => OriginalMarkerRows.Length > 0,
        "9to1.Motion.UpdateMarker" => SelectedMarker is not null && ValidMarkerDraft(),
        "9to1.Motion.DeleteMarker" => SelectedMarker is not null,
        "9to1.Motion.PreviousMarker" => (Sequence.Markers ?? []).Any(marker => marker.Frame < _playhead),
        "9to1.Motion.NextMarker" => (Sequence.Markers ?? []).Any(marker => marker.Frame > _playhead),
        "9to1.Motion.AddMarker" => (Sequence.Markers?.Count ?? 0) < MotionMarkers.MaximumMarkersPerSequence && _playhead < long.MaxValue && ValidMarkerName(_drafts["MarkerName"]),
        "9to1.Motion.ToggleSnapping" or "9to1.Motion.SnapPlayhead" or "9to1.Motion.MarkIn" => true,
        "9to1.Motion.MarkOut" => _playhead < long.MaxValue,
        _ => null
    };
    private static bool ValidMarkerName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl);
    private bool ValidMarkerDraft() => ValidMarkerName(_drafts["MarkerName"])
        && long.TryParse(_drafts["MarkerFrame"], NumberStyles.None, CultureInfo.InvariantCulture, out var frame) && frame >= 0 && frame < long.MaxValue;
    private bool DispatchMarker(string command, object? parameter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        void Edit(Func<MotionProject, MotionProject> edit) { using (EnterPhysicalOriginal()) _session.Apply(edit); }
        switch (command)
        {
            case "9to1.Motion.SelectMarker":
                var selection = DemandMarkerRow(parameter); _markerId = selection.Original.MarkerId; LoadMarkerDrafts(); break;
            case "9to1.Motion.JumpMarker": _playhead = DemandMarkerRow(parameter).Original.Frame; break;
            case "9to1.Motion.AddMarker":
                var previousIds = (Sequence.Markers ?? []).Select(marker => marker.MarkerId).ToHashSet();
                Edit(p => _store.AddMarker(p, p.Revision, _sequenceId, _playhead, _drafts["MarkerName"]));
                _markerId = Sequence.Markers!.Single(marker => !previousIds.Contains(marker.MarkerId)).MarkerId;
                LoadMarkerDrafts(); break;
            case "9to1.Motion.UpdateMarker":
                var update = DemandMarkerRow(parameter).Original;
                var frame = long.Parse(_drafts["MarkerFrame"], NumberStyles.None, CultureInfo.InvariantCulture);
                Edit(p => _store.UpdateMarker(p, p.Revision, _sequenceId, update.MarkerId, update.Revision, frame, _drafts["MarkerName"]));
                LoadMarkerDrafts(); break;
            case "9to1.Motion.DeleteMarker":
                var remove = DemandMarkerRow(parameter).Original;
                Edit(p => _store.DeleteMarker(p, p.Revision, _sequenceId, remove.MarkerId, remove.Revision));
                _markerId = null; LoadMarkerDrafts(); break;
            case "9to1.Motion.PreviousMarker": _playhead = Sequence.Markers!.Where(marker => marker.Frame < _playhead).Max(marker => marker.Frame); break;
            case "9to1.Motion.NextMarker": _playhead = Sequence.Markers!.Where(marker => marker.Frame > _playhead).Min(marker => marker.Frame); break;
            case "9to1.Motion.ToggleSnapping": _snapEnabled = !_snapEnabled; break;
            case "9to1.Motion.SnapPlayhead": _playhead = SnapTimelineFrame(_playhead, 3); break;
            case "9to1.Motion.MarkIn": _drafts["RangeStart"] = _playhead.ToString(CultureInfo.InvariantCulture); break;
            case "9to1.Motion.MarkOut": _drafts["RangeEnd"] = checked(_playhead + 1).ToString(CultureInfo.InvariantCulture); break;
            default: return false;
        }
        return true;
    }
    private void LoadMarkerDrafts()
    {
        _drafts["MarkerName"] = SelectedMarker?.Name ?? "";
        _drafts["MarkerFrame"] = (SelectedMarker?.Frame ?? _playhead).ToString(CultureInfo.InvariantCulture);
        AcknowledgeDrafts("MarkerName", "MarkerFrame");
    }
}
