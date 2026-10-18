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
    private bool _disposed;
    public ResourceDictionary Resources { get; } = new();

    public CuiSceneThemeBinding(CuiNativeScene scene, Func<bool>? canPublish = null,
        Action<ResourceDictionary>? updated = null)
    {
        _scene = scene;
        _canPublish = canPublish;
        _updated = updated;
        Resources.MergedDictionaries.Add(CreateSnapshot());
        if (scene.ThemePreference is { } context) context.Changed += PreferenceChanged;
    }

    private ResourceDictionary CreateSnapshot()
    {
        var resolution = _scene.ThemePreference?.Current;
        var appearance = _scene.Appearance ?? resolution?.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
        return CuiSceneVisualResources.Create(_scene.Surface, appearance, resolution, CuiSceneVisualResources.AccentAppId(_scene));
    }

    private void PreferenceChanged(object? sender, EventArgs args)
    {
        void Publish()
        {
            if (_disposed || _canPublish?.Invoke() == false) return;
            // Build off-tree and replace one provider: observers never see a
            // partially populated palette. Failed callbacks remain owned by the
            // canonical context's retry/error tracking, not a detached UI task.
            var snapshot = CreateSnapshot();
            if (_disposed || _canPublish?.Invoke() == false) return;
            Resources.MergedDictionaries[0] = snapshot;
            if (!_disposed && _canPublish?.Invoke() != false) _updated?.Invoke(Resources);
        }
        if (Dispatcher.UIThread.CheckAccess()) Publish();
        else Dispatcher.UIThread.Invoke(Publish);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_scene.ThemePreference is { } context) context.Changed -= PreferenceChanged;
    }
}
