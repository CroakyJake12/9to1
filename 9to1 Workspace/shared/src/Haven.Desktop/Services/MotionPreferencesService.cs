using Haven.Infrastructure;

namespace Haven.Desktop.Services;

/// <summary>Compatibility facade over the same shared OS-local preference owner and existing file.</summary>
public sealed class MotionPreferencesService
{
    private static readonly Lazy<MotionPreferencesService> LazyCurrent = new(() => new());
    private readonly LocalMotionPreferencesService _shared = LocalMotionPreferencesService.Current;
    private MotionPreferencesService() => _shared.Changed += (_, args) => Changed?.Invoke(this, args);
    public static MotionPreferencesService Current => LazyCurrent.Value;
    public bool ReduceAnimations => _shared.ReduceAnimations;
    public event EventHandler? Changed;
    public void SetReduceAnimations(bool value) => _shared.SetReduceAnimations(value);
}
