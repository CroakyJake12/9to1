using Android.App;
using Android.App.Roles;
using Android.Content;
using Android.Content.PM;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private const int DefaultHomeRoleRequest = 8103;
    private bool _defaultHomeRolePending, _defaultHomeRoleInFlight;
    private AlertDialog? _launcherSettingsDialog;
    private void CloseLauncherSettingsDialog()
    { _launcherSettingsDialog?.Dismiss(); _launcherSettingsDialog = null; }
    private (bool Supported, bool Available, bool Held, bool ResolvedHere) ObserveDefaultHomeRole()
    {
        var available = false;
        var held = false;
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var manager = GetSystemService(RoleService) as RoleManager;
            available = manager?.IsRoleAvailable(RoleManager.RoleHome) == true;
            held = manager?.IsRoleHeld(RoleManager.RoleHome) == true;
        }
        using var home = new Intent(Intent.ActionMain); home.AddCategory(Intent.CategoryHome);
        var resolved = OperatingSystem.IsAndroidVersionAtLeast(33)
            ? PackageManager?.ResolveActivity(home, global::Android.Content.PM.PackageManager.ResolveInfoFlags.Of((long)PackageInfoFlags.MatchDefaultOnly))?.ActivityInfo
            : PackageManager?.ResolveActivity(home, PackageInfoFlags.MatchDefaultOnly)?.ActivityInfo;
        var actual = ComponentName;
        var here = actual is not null && resolved is not null && resolved.PackageName == actual.PackageName && resolved.Name == actual.ClassName;
        return (OperatingSystem.IsAndroidVersionAtLeast(29), available, held, here);
    }
    private async Task RequestDefaultHomeForOriginalAsync(LauncherSessionSnapshot original, Func<bool> originalSettingsCurrent)
    {
        var ownsAdmission = false;
        try
        {
            if (_defaultHomeRolePending || _defaultHomeRoleInFlight || !originalSettingsCurrent()) return;
            _defaultHomeRoleInFlight = true; ownsAdmission = true;
            await WidgetSessions.RequireOriginalActorAsync(original, _launcherLifetime.Token);
            if (_defaultHomeRolePending || !originalSettingsCurrent()) return;
            var state = ObserveDefaultHomeRole();
            if (state.ResolvedHere && (!state.Supported || state.Held))
            { Toast.MakeText(this, "9to1 Launcher is already your default Home.", ToastLength.Short)?.Show(); return; }
            Intent request;
            if (OperatingSystem.IsAndroidVersionAtLeast(29) && state.Available && GetSystemService(RoleService) is RoleManager manager)
                request = manager.CreateRequestRoleIntent(RoleManager.RoleHome);
            else
                request = new Intent(global::Android.Provider.Settings.ActionHomeSettings);
            _defaultHomeRolePending = true;
            try { StartActivityForResult(request, DefaultHomeRoleRequest); }
            catch { _defaultHomeRolePending = false; throw; }
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
            global::Android.Content.ActivityNotFoundException or global::Java.Lang.SecurityException or global::Java.Lang.IllegalStateException)
        { if (originalSettingsCurrent()) Toast.MakeText(this, "Android could not open default Home selection: " + ex.Message, ToastLength.Long)?.Show(); }
        finally { if (ownsAdmission) _defaultHomeRoleInFlight = false; }
    }
    private bool CompleteDefaultHomeRoleRequest(int requestCode)
    {
        if (requestCode != DefaultHomeRoleRequest) return false;
        if (!_defaultHomeRolePending) return true;
        _defaultHomeRolePending = false;
        try
        {
            // A result code is not a role grant. Reobserve actual OS role and exact resolved HOME component.
            var actual = ObserveDefaultHomeRole();
            if (!_launcherLifetime.IsCancellationRequested && !IsDestroyed && !IsFinishing)
                Toast.MakeText(this, actual.ResolvedHere && (!actual.Supported || actual.Held)
                    ? "9to1 Launcher is your default Home."
                    : "Android did not confirm 9to1 Launcher as default Home.", ToastLength.Short)?.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::Java.Lang.SecurityException or global::Java.Lang.IllegalStateException)
        { if (!_launcherLifetime.IsCancellationRequested && !IsDestroyed) Toast.MakeText(this, "Android default Home could not be checked: " + ex.Message, ToastLength.Long)?.Show(); }
        return true;
    }
}
