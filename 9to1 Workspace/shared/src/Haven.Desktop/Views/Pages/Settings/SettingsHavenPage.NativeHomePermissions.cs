#if !ANDROID
using Haven.UI.Components;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Settings;

public sealed partial class SettingsHavenPage
{
    private Haven.UI.Components.Button? _nativeHomePermissionsButton;
    private void InitializeNativeHomePermissions()
    {
        if (!OperatingSystem.IsWindows()) return;
        _nativeHomePermissionsButton = new() { Name = "Settings.NativeHomePermissions", Content = "Home permissions", Variant = ButtonVariant.Secondary };
        _nativeHomePermissionsButton.Accessibility.AccessibleName = "Review genuine Home permission requests and trusted access";
        _nativeHomePermissionsButton.Invoked += OnNativeHomePermissionsInvoked;
        _route.Content.Add(_nativeHomePermissionsButton);
    }
    private void OnNativeHomePermissionsInvoked(object? sender, EventArgs args)
    {
        if (_disposed || App.IsOriginalDesktopRetiring) return;
        var original = App.Services?.GetService<HomeNativeApprovalWindowOwner>()
            ?? throw new InvalidOperationException("The original Windows Home review owner is unavailable.");
        _ = original.OpenOriginalAsync(); // Request only; SAME open is retained by the owning review cohort.
    }
    private void DetachNativeHomePermissions()
    {
        if (_nativeHomePermissionsButton is { } button) button.Invoked -= OnNativeHomePermissionsInvoked;
        _nativeHomePermissionsButton = null;
    }
}
#endif
