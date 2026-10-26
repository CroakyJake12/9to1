using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Themes;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// A retained scene's projection of the owning Home theme observation. It owns
/// only its subscription and resource scope; it never owns or disposes Home state.
/// </summary>
internal sealed class CuiSceneThemeBinding : IDisposable
{
    private readonly CuiNativeScene _scene;
    private readonly Func<bool>? _canPublish;
    private readonly Action<ResourceDictionary>? _updated;
    private CuiThemeResolution? _appliedPreference;
    private bool _disposed;
    public ResourceDictionary Resources { get; } = new();

    public CuiSceneThemeBinding(CuiNativeScene scene, Func<bool>? canPublish = null,
        Action<ResourceDictionary>? updated = null)
    {
        _scene = scene;
        _canPublish = canPublish;
        _updated = updated;
        _appliedPreference = scene.ThemePreference?.Current;
        Resources.MergedDictionaries.Add(CreateSnapshot(_appliedPreference));
        if (scene.ThemePreference is { } context) context.Changed += PreferenceChanged;
        else CuiThemeResourceApplier.PaletteChanged += GlobalPaletteChanged;
    }

    private ResourceDictionary CreateSnapshot(CuiThemeResolution? resolution)
    {
        var appearance = _scene.Appearance ?? resolution?.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
        return CuiSceneVisualResources.Create(_scene.Surface, appearance, resolution, CuiSceneVisualResources.AccentAppId(_scene));
    }

    // Called after exact scene ownership is admitted as well as on Home events.
    // Changes observed before admission cannot publish, but must not be lost.
    public void SynchronizeCurrent(bool force = false)
    {
        void Publish()
        {
            if (_disposed || _canPublish?.Invoke() == false) return;
            var resolution = _scene.ThemePreference?.Current;
            if (!force && ReferenceEquals(resolution, _appliedPreference)) return;
            // Build off-tree and replace one provider: observers never see a
            // partially populated palette. Failed callbacks remain owned by the
            // canonical context's retry/error tracking, not a detached UI task.
            var snapshot = CreateSnapshot(resolution);
            if (_disposed || _canPublish?.Invoke() == false) return;
            var previous = _appliedPreference;
            _appliedPreference = resolution; // Reentrant newer updates retain their own identity.
            try
            {
                Resources.MergedDictionaries[0] = snapshot;
                if (!_disposed && _canPublish?.Invoke() != false) _updated?.Invoke(Resources);
            }
            catch
            {
                if (ReferenceEquals(_appliedPreference, resolution)) _appliedPreference = previous;
                throw;
            }
        }
        if (Dispatcher.UIThread.CheckAccess()) Publish();
        else Dispatcher.UIThread.Invoke(Publish);
    }

    private void PreferenceChanged(object? sender, EventArgs args) => SynchronizeCurrent();
    private void GlobalPaletteChanged(object? sender, EventArgs args) => SynchronizeCurrent(force: true);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_scene.ThemePreference is { } context) context.Changed -= PreferenceChanged;
        else CuiThemeResourceApplier.PaletteChanged -= GlobalPaletteChanged;
    }
}
