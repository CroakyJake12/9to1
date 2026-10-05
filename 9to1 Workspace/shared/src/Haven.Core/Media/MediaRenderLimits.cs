namespace Haven.Core.Media;

/// <summary>Explicit host resource policy, independent of codec/hardware capability. Hosts may configure
/// a larger admitted workload only after validating their memory, scheduling and encoder capacity.</summary>
public sealed record MediaRenderLimits(int MaximumWidth = 3840, int MaximumHeight = 2160,
    int MaximumFrameRate = 120, int MaximumClipCount = 1000, int MaximumLayerCount = 64,
    long MaximumTimelineSeconds = 14400)
{
    public void Validate()
    {
        if (MaximumWidth <= 0 || MaximumHeight <= 0 || MaximumFrameRate <= 0 || MaximumClipCount <= 0
            || MaximumLayerCount <= 0 || MaximumTimelineSeconds <= 0 || MaximumTimelineSeconds > 604800)
            throw new ArgumentException("Invalid configured media render resource policy.");
    }
}
