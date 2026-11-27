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
    private CuiScenePresentationScope _presentationScope;
    private CuiScenePresentationScope _appliedPresentationScope;
    public CuiScenePresentationScope PresentationScope => _presentationScope;
    private long _globalPaletteVersion;
    private long _appliedGlobalPaletteVersion;
    private long _publicationSerial;
    private bool _publicationDirty;
    private bool _disposed;
    public ResourceDictionary Resources { get; } = new();

    public CuiSceneThemeBinding(CuiNativeScene scene, Func<bool>? canPublish = null,
        Action<ResourceDictionary>? updated = null)
    {
        _scene = scene;
        _canPublish = canPublish;
        _updated = updated;
        _appliedPreference = scene.ThemePreference?.Current;
        _presentationScope = _appliedPresentationScope = CuiSceneVisualResources.PresentationScope(scene);
        Resources.MergedDictionaries.Add(CreateSnapshot(_appliedPreference, _presentationScope));
        if (scene.ThemePreference is { } context) context.Changed += PreferenceChanged;
        else CuiThemeResourceApplier.PaletteChanged += GlobalPaletteChanged;
    }

    private ResourceDictionary CreateSnapshot(CuiThemeResolution? resolution, CuiScenePresentationScope presentation)
    {
        var appearance = _scene.Appearance ?? resolution?.Appearance ?? CuiThemeScopeApplier.DetectAppearance();
        return CuiSceneVisualResources.Create(presentation.Surface, appearance, resolution, presentation.AppId);
    }

    public void UpdatePresentationScope(CuiScenePresentationScope presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (_disposed || _canPublish?.Invoke() == false) return;
        _presentationScope = presentation;
        SynchronizeCurrent();
    }

    // Called after exact scene ownership is admitted as well as on Home events.
    // Changes observed before admission cannot publish, but must not be lost.
    public void SynchronizeCurrent()
    {
        void Publish()
        {
            if (_disposed || _canPublish?.Invoke() == false) return;
            var resolution = _scene.ThemePreference?.Current;
            var presentation = _presentationScope;
            var globalVersion = _scene.ThemePreference is null ? Interlocked.Read(ref _globalPaletteVersion) : 0;
            var globalPaletteChanged = _scene.ThemePreference is null &&
                globalVersion != Interlocked.Read(ref _appliedGlobalPaletteVersion);
            if (!_publicationDirty && !globalPaletteChanged && ReferenceEquals(resolution, _appliedPreference) &&
                presentation == _appliedPresentationScope) return;
            // Build off-tree and replace one provider: observers never see a
            // partially populated palette. Failed callbacks remain owned by the
            // canonical context's retry/error tracking, not a detached UI task.
            var snapshot = CreateSnapshot(resolution, presentation);
            if (_disposed || _canPublish?.Invoke() == false) return;
            var previous = _appliedPreference;
            var previousPresentation = _appliedPresentationScope;
            var publication = ++_publicationSerial;
            // Cache the in-flight identity to suppress same-revision reentry. If this attempt
            // loses admission after replacing the provider, the dirty marker forces a real
            // repaint on readmission even when the requested identity is the previous one.
            _publicationDirty = false;
            _appliedPresentationScope = presentation;
            _appliedPreference = resolution; // Reentrant newer updates retain their own identity.
            try
            {
                Resources.MergedDictionaries[0] = snapshot;
                var admitted = !_disposed && _canPublish?.Invoke() != false;
                if (admitted) _updated?.Invoke(Resources);
                if (!admitted || _disposed || _canPublish?.Invoke() == false)
                {
                    // Resource notifications and the scene callback can revoke
                    // ownership synchronously. Keep this revision retryable on
                    // readmission without rolling back a newer reentrant update.
                    if (_publicationSerial == publication)
                    { _appliedPreference = previous; _appliedPresentationScope = previousPresentation; _publicationDirty = true; }
                }
                else if (_scene.ThemePreference is null)
                    MarkGlobalPaletteApplied(globalVersion);
            }
            catch
            {
                if (_publicationSerial == publication)
                { _appliedPreference = previous; _appliedPresentationScope = previousPresentation; _publicationDirty = true; }
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
