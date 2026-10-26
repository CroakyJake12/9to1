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
    private long _globalPaletteVersion;
    private long _appliedGlobalPaletteVersion;
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
    public void SynchronizeCurrent()
    {
        void Publish()
        {
            if (_disposed || _canPublish?.Invoke() == false) return;
            var resolution = _scene.ThemePreference?.Current;
            var globalVersion = _scene.ThemePreference is null ? Interlocked.Read(ref _globalPaletteVersion) : 0;
            var globalPaletteChanged = _scene.ThemePreference is null &&
                globalVersion != Interlocked.Read(ref _appliedGlobalPaletteVersion);
            if (!globalPaletteChanged && ReferenceEquals(resolution, _appliedPreference)) return;
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
                var admitted = !_disposed && _canPublish?.Invoke() != false;
                if (admitted) _updated?.Invoke(Resources);
                if (admitted && !_disposed && _canPublish?.Invoke() != false && _scene.ThemePreference is null)
                    MarkGlobalPaletteApplied(globalVersion);
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

    private void GlobalPaletteChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref _globalPaletteVersion);
        SynchronizeCurrent();
    }

    private void MarkGlobalPaletteApplied(long version)
    {
        while (true)
        {
            var applied = Interlocked.Read(ref _appliedGlobalPaletteVersion);
            if (applied >= version || Interlocked.CompareExchange(ref _appliedGlobalPaletteVersion, version, applied) == applied)
                return;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_scene.ThemePreference is { } context) context.Changed -= PreferenceChanged;
        else CuiThemeResourceApplier.PaletteChanged -= GlobalPaletteChanged;
    }
}
