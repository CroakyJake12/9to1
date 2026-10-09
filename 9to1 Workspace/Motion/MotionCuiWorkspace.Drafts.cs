namespace HavenOS.Apps.Motion;

public sealed partial class MotionCuiWorkspace
{
    private readonly Dictionary<string, string> _acknowledgedDrafts = new()
    { ["Start"] = "0", ["SourceIn"] = "0", ["SourceOut"] = "150", ["RangeStart"] = "0", ["RangeEnd"] = "30", ["CaptionText"] = "", ["SubtitleText"] = "", ["MarkerName"] = "", ["MarkerFrame"] = "0" };
    public bool HasUnsavedChanges => _drafts.Any(entry => entry.Value != _acknowledgedDrafts[entry.Key]);
    private void AcknowledgeDrafts(params string[] keys)
    { foreach (var key in keys) _acknowledgedDrafts[key] = _drafts[key]; }
    private void RestoreAcknowledgedDrafts()
    { foreach (var entry in _acknowledgedDrafts) _drafts[entry.Key] = entry.Value; }
    private bool CanRunWithPendingDrafts(string command)
    {
        if (!HasUnsavedChanges) return true;
        if (command is "9to1.Motion.DiscardDrafts" or "9to1.Motion.PreviousFrame" or "9to1.Motion.NextFrame" or "9to1.Motion.PlaySource" or "9to1.Motion.Pause" or "9to1.Motion.JumpMarker" or "9to1.Motion.PreviousMarker" or "9to1.Motion.NextMarker" or "9to1.Motion.ToggleSnapping" or "9to1.Motion.SnapPlayhead" or "9to1.Motion.MarkIn" or "9to1.Motion.MarkOut") return true;
        string[] accepted = command switch
        {
            "9to1.Motion.AddMarker" => ["MarkerName"],
            "9to1.Motion.UpdateMarker" => ["MarkerName", "MarkerFrame"],
            "9to1.Motion.Insert" or "9to1.Motion.Overwrite" => ["Start", "SourceIn", "SourceOut"],
            "9to1.Motion.Trim" => ["SourceIn", "SourceOut"],
            "9to1.Motion.Move" or "9to1.Motion.MoveToTrack" or "9to1.Motion.Slide" => ["Start"],
            "9to1.Motion.Slip" or "9to1.Motion.RippleTrimStart" => ["SourceIn"],
            "9to1.Motion.RippleTrimEnd" => ["SourceOut"],
            "9to1.Motion.Extract" or "9to1.Motion.Lift" => ["RangeStart", "RangeEnd"],
            "9to1.Motion.AddCaption" or "9to1.Motion.SaveCaption" => ["CaptionText", "RangeStart", "RangeEnd"],
            "9to1.Motion.ImportSrt" or "9to1.Motion.ImportWebVtt" => ["SubtitleText"],
            _ => []
        };
        return _drafts.All(entry => entry.Value == _acknowledgedDrafts[entry.Key] || accepted.Contains(entry.Key));
    }
    private void AcknowledgeAcceptedDrafts(string command)
    {
        switch (command)
        {
            case "9to1.Motion.Insert": case "9to1.Motion.Overwrite": case "9to1.Motion.Trim":
                AcknowledgeDrafts("Start", "SourceIn", "SourceOut"); break;
            case "9to1.Motion.Move": case "9to1.Motion.MoveToTrack": case "9to1.Motion.Slide":
                AcknowledgeDrafts("Start"); break;
            case "9to1.Motion.Slip": case "9to1.Motion.RippleTrimStart": AcknowledgeDrafts("SourceIn"); break;
            case "9to1.Motion.RippleTrimEnd": AcknowledgeDrafts("SourceOut"); break;
            case "9to1.Motion.Extract": case "9to1.Motion.Lift": AcknowledgeDrafts("RangeStart", "RangeEnd"); break;
            case "9to1.Motion.AddCaption": case "9to1.Motion.SaveCaption": AcknowledgeDrafts("CaptionText", "RangeStart", "RangeEnd"); break;
            case "9to1.Motion.ImportSrt": case "9to1.Motion.ImportWebVtt": case "9to1.Motion.ExportSrt": case "9to1.Motion.ExportWebVtt": AcknowledgeDrafts("SubtitleText"); break;
        }
    }
}
