#if !ANDROID
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using HavenOS.Apps.Browse;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        /// <summary>Only the actual App-owned primary window supplies these callbacks.</summary>
        internal sealed class UnifiedWindowControls(UnifiedWorkspaceHost owner) : IBrowseNativeWindowControls
        {
            private EventHandler? _changed;
            private WindowDecorations? _originalDecorations;
            public bool IsAvailable => owner.IsCurrent && ReferenceEquals(owner._app._actualPrimaryWindow, owner._actualWindow);
            public bool CanMinimize => IsAvailable && owner._actualWindow.CanMinimize;
            public bool CanMaximize => IsAvailable && owner._actualWindow.CanMaximize &&
                owner._actualWindow.WindowState != WindowState.FullScreen;
            public bool IsMaximized => owner._actualWindow.WindowState == WindowState.Maximized;
            public event EventHandler? StateChanged
            {
                add
                {
                    if (_changed is null) owner._actualWindow.PropertyChanged += OnWindowChanged;
                    _changed += value;
                }
                remove
                {
                    _changed -= value;
                    if (_changed is null) owner._actualWindow.PropertyChanged -= OnWindowChanged;
                }
            }
            private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == Window.WindowStateProperty || args.Property == Window.CanMinimizeProperty ||
                    args.Property == Window.CanMaximizeProperty) _changed?.Invoke(this, EventArgs.Empty);
            }
            public void SetCaptionReady(bool ready)
            {
                Dispatcher.UIThread.VerifyAccess();
                if (ready && IsAvailable)
                {
                    _originalDecorations ??= owner._actualWindow.WindowDecorations;
                    // Preserve the native resize border and OS shortcuts. Only the duplicate
                    // native title row is replaced by the mounted, authored caption controls.
                    owner._actualWindow.WindowDecorations = WindowDecorations.BorderOnly;
                }
                else if (_originalDecorations is { } decorations)
                {
                    owner._actualWindow.WindowDecorations = decorations;
                    _originalDecorations = null;
                }
            }
            public void Minimize()
            {
                Dispatcher.UIThread.VerifyAccess();
                if (CanMinimize) owner._actualWindow.WindowState = WindowState.Minimized;
            }
            public void ToggleMaximize()
            {
                Dispatcher.UIThread.VerifyAccess();
                if (CanMaximize) owner._actualWindow.WindowState = IsMaximized ? WindowState.Normal : WindowState.Maximized;
            }
            public void BeginMoveDrag(PointerPressedEventArgs args)
            {
                Dispatcher.UIThread.VerifyAccess();
                if (IsAvailable) owner._actualWindow.BeginMoveDrag(args);
            }
            public void RequestClose()
            {
                Dispatcher.UIThread.VerifyAccess();
                if (IsAvailable) owner._app.RequestOriginalPrimaryUserClose(owner._actualWindow);
            }
        }
    }
}
#endif
