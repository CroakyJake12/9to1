using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-3 typed popup-platform fixture. The headless platform's <see cref="IWindowImpl.CreatePopup"/>
/// returns null, so <c>OverlayPopupHost.CreatePopupHost</c> would fall back to an overlay host and no
/// <see cref="PopupRoot"/> (and therefore no native BeforeShow/transparency seam) would exist. This
/// fixture builds a real <see cref="Window"/> whose platform implementation delegates every window
/// aspect to a genuine headless window but overrides <see cref="IWindowImpl.CreatePopup"/> to return a
/// real <see cref="IPopupImpl"/>, so the production popup open/close path produces a real PopupRoot.
/// It is a test-only typed fixture: it invents no signatures, alters no shipping behaviour, and relies
/// only on public contracts (<see cref="Window(IWindowImpl)"/>, <c>PlatformManager.CreateWindow()</c>).
/// </summary>
internal static class CuiNativePopupFixture
{
    /// <summary>
    /// Creates a real window backed by the native-popup platform implementation.
    /// </summary>
    public static Window New(object? content, double width, double height)
    {
        var window = new Window(new NativePopupWindowImpl())
        {
            Content = content,
            Width = width,
            Height = height,
        };
        return window;
    }

    private sealed class NativePopupWindowImpl : IWindowImpl
    {
        private readonly IWindowImpl _inner;

        public NativePopupWindowImpl()
        {
            _inner = PlatformManager.CreateWindow();
        }

        // ITopLevelImpl
        public void Dispose() => _inner.Dispose();
        public double DesktopScaling => _inner.DesktopScaling;
        public IPlatformHandle? Handle => _inner.Handle;
        public Size ClientSize => _inner.ClientSize;
        public double RenderScaling => _inner.RenderScaling;
        public IPlatformRenderSurface[] Surfaces => _inner.Surfaces;

        public Action<RawInputEventArgs>? Input
        {
            get => _inner.Input;
            set => _inner.Input = value;
        }

        public Action<Rect>? Paint
        {
            get => _inner.Paint;
            set => _inner.Paint = value;
        }

        public Action<Size, WindowResizeReason>? Resized
        {
            get => _inner.Resized;
            set => _inner.Resized = value;
        }

        public Action<double>? ScalingChanged
        {
            get => _inner.ScalingChanged;
            set => _inner.ScalingChanged = value;
        }

        public Action<WindowTransparencyLevel>? TransparencyLevelChanged
        {
            get => _inner.TransparencyLevelChanged;
            set => _inner.TransparencyLevelChanged = value;
        }

        public Compositor Compositor => _inner.Compositor;
        public void SetInputRoot(IInputRoot inputRoot) => _inner.SetInputRoot(inputRoot);
        public Point PointToClient(PixelPoint point) => _inner.PointToClient(point);
        public PixelPoint PointToScreen(Point point) => _inner.PointToScreen(point);
        public void SetCursor(ICursorImpl? cursor) => _inner.SetCursor(cursor);

        public Action? Closed
        {
            get => _inner.Closed;
            set => _inner.Closed = value;
        }

        public Action? LostFocus
        {
            get => _inner.LostFocus;
            set => _inner.LostFocus = value;
        }

        public IPopupImpl? CreatePopup()
        {
            // A real IPopupImpl enables OverlayPopupHost.CreatePopupHost to build a PopupRoot instead of
            // falling back to an overlay host. Each popup open gets its own fresh destination impl.
            var popup = PlatformManager.CreateWindow();
            return popup as IPopupImpl
                ?? throw new InvalidOperationException(
                    "The headless window platform must produce an IPopupImpl for the native popup fixture.");
        }

        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
            => _inner.SetTransparencyLevelHint(transparencyLevels);

        public WindowTransparencyLevel TransparencyLevel => _inner.TransparencyLevel;
        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels => _inner.AcrylicCompensationLevels;
        public void SetFrameThemeVariant(PlatformThemeVariant themeVariant) => _inner.SetFrameThemeVariant(themeVariant);
        public object? TryGetFeature(Type featureType) => _inner.TryGetFeature(featureType);

        // IWindowBaseImpl
        public Size? FrameSize => _inner.FrameSize;
        public void Show(bool activate, bool isDialog) => _inner.Show(activate, isDialog);
        public void Hide() => _inner.Hide();
        public PixelPoint Position => _inner.Position;

        public Action<PixelPoint>? PositionChanged
        {
            get => _inner.PositionChanged;
            set => _inner.PositionChanged = value;
        }

        public void Activate() => _inner.Activate();

        public Action? Deactivated
        {
            get => _inner.Deactivated;
            set => _inner.Deactivated = value;
        }

        public Action? Activated
        {
            get => _inner.Activated;
            set => _inner.Activated = value;
        }

        public Size MaxAutoSizeHint => _inner.MaxAutoSizeHint;
        public void SetTopmost(bool value) => _inner.SetTopmost(value);

        // IWindowImpl
        public WindowState WindowState
        {
            get => _inner.WindowState;
            set => _inner.WindowState = value;
        }

        public bool WindowStateGetterIsUsable => _inner.WindowStateGetterIsUsable;

        public Action<WindowState>? WindowStateChanged
        {
            get => _inner.WindowStateChanged;
            set => _inner.WindowStateChanged = value;
        }

        public void SetTitle(string? title) => _inner.SetTitle(title);
        public void SetParent(IWindowImpl? parent) => _inner.SetParent(parent);
        public void SetEnabled(bool enable) => _inner.SetEnabled(enable);

        public Action? GotInputWhenDisabled
        {
            get => _inner.GotInputWhenDisabled;
            set => _inner.GotInputWhenDisabled = value;
        }

        public void SetWindowDecorations(WindowDecorations enabled) => _inner.SetWindowDecorations(enabled);
        public void SetIcon(IWindowIconImpl? icon) => _inner.SetIcon(icon);
        public void ShowTaskbarIcon(bool value) => _inner.ShowTaskbarIcon(value);
        public void CanResize(bool value) => _inner.CanResize(value);
        public void SetCanMinimize(bool value) => _inner.SetCanMinimize(value);
        public void SetCanMaximize(bool value) => _inner.SetCanMaximize(value);

        public Func<WindowCloseReason, bool>? Closing
        {
            get => _inner.Closing;
            set => _inner.Closing = value;
        }

        public bool IsClientAreaExtendedToDecorations => _inner.IsClientAreaExtendedToDecorations;

        public Action<bool>? ExtendClientAreaToDecorationsChanged
        {
            get => _inner.ExtendClientAreaToDecorationsChanged;
            set => _inner.ExtendClientAreaToDecorationsChanged = value;
        }

        public bool NeedsManagedDecorations => _inner.NeedsManagedDecorations;
        public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => _inner.RequestedDrawnDecorations;
        public Thickness ExtendedMargins => _inner.ExtendedMargins;
        public Thickness OffScreenMargin => _inner.OffScreenMargin;
        public void BeginMoveDrag(PointerPressedEventArgs e) => _inner.BeginMoveDrag(e);
        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e) => _inner.BeginResizeDrag(edge, e);

        public void Resize(Size clientSize, WindowResizeReason reason = WindowResizeReason.Application)
            => _inner.Resize(clientSize, reason);

        public void Move(PixelPoint point) => _inner.Move(point);
        public void SetMinMaxSize(Size minSize, Size maxSize) => _inner.SetMinMaxSize(minSize, maxSize);
        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
            => _inner.SetExtendClientAreaToDecorationsHint(extendIntoClientAreaHint);
        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
            => _inner.SetExtendClientAreaTitleBarHeightHint(titleBarHeight);
        public PlatformAllowedWindowActions AllowedWindowActions => _inner.AllowedWindowActions;

        public Action<PlatformAllowedWindowActions>? AllowedWindowActionsChanged
        {
            get => _inner.AllowedWindowActionsChanged;
            set => _inner.AllowedWindowActionsChanged = value;
        }
    }
}
