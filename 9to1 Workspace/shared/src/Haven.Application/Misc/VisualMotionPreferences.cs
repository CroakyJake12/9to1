namespace Haven.Application;

/// <summary>Actual current shared visual-motion preference observation. When unavailable, callers retain
/// a static/paused presentation rather than treating a failed read as permission to animate.</summary>
public sealed record VisualMotionPreferenceSnapshot(bool ReduceAnimations, bool IsAvailable);
public interface IMotionPreferenceSource
{
    bool ReduceAnimations { get; }
    event EventHandler? Changed;
    ValueTask<VisualMotionPreferenceSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
public interface IMotionPreferences : IMotionPreferenceSource
{
    void SetReduceAnimations(bool value);
}
