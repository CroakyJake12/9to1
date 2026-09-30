using System.Globalization;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Haven.Application;

namespace Haven.Android;

internal enum AndroidLauncherProfileAvailability { Available, Quiet, Inaccessible, Unobserved }

internal sealed record AndroidLauncherPlatformActivity(string PackageIdentity, string Entrypoint,
    string Label, bool Enabled, Drawable? BadgedIcon);

internal sealed record AndroidLauncherPlatformProfile(string PlatformUserSerial, string Label,
    string? UserType, bool IsCurrentProfile, AndroidLauncherProfileAvailability Availability,
    IReadOnlyList<AndroidLauncherPlatformActivity> Activities, bool IsCompleteObservation);

/// <summary>Trusted Android observations for Home's installed-application registry.
/// No package database, installer or persisted launcher state belongs to this adapter.</summary>
internal sealed class AndroidLauncherPlatformCatalog(Context context) : IInstalledApplicationObservationProvider
{
    internal const string ProviderId = "android.launcherapps";
    string IInstalledApplicationObservationProvider.ProviderId => ProviderId;

    public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken cancellationToken)
    {
        var observations = await Task.Run(() => Observe(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return observations.Select(profile =>
        {
            // Stable launch keys describe observed navigation identity only. They are
            // scoped by Home to provider, OS profile and package; they confer no trust.
            var counts = profile.Activities.GroupBy(activity => activity.PackageIdentity, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return new InstalledApplicationProfileObservation(profile.PlatformUserSerial, profile.Label,
                profile.UserType == "android.os.usertype.profile.MANAGED",
                profile.IsCompleteObservation && profile.Availability == AndroidLauncherProfileAvailability.Available,
                profile.Activities.Select(activity => new InstalledApplicationObservation(activity.PackageIdentity,
                    activity.Entrypoint, activity.Label, null, activity.Enabled)
                {
                    StableLaunchIdentity = profile.IsCompleteObservation && counts[activity.PackageIdentity] == 1
                        ? "android.sole-launcher" : "android.component:" + activity.Entrypoint
                }).ToArray());
        }).ToArray();
    }

    public IReadOnlyList<AndroidLauncherPlatformProfile> Observe(bool loadIcons = false)
    {
        var launcher = context.GetSystemService(Context.LauncherAppsService) as LauncherApps
            ?? throw new InvalidOperationException("Android LauncherApps service is unavailable.");
        var users = context.GetSystemService(Context.UserService) as UserManager
            ?? throw new InvalidOperationException("Android user-profile service is unavailable.");
        var current = global::Android.OS.Process.MyUserHandle();
        var snapshots = new List<AndroidLauncherPlatformProfile>();
        foreach (var profile in AccessibleProfiles(launcher, users))
        {
            var serial = users.GetSerialNumberForUser(profile);
            // Unknown serial cannot establish a stable profile identity. Omitting
            // that scope preserves Home's prior observations rather than deleting them.
            if (serial < 0) continue;
            var key = serial.ToString(CultureInfo.InvariantCulture);
            var isCurrent = profile.Equals(current);
            string? userType = null;
            if (OperatingSystem.IsAndroidVersionAtLeast(35))
            {
                try { userType = launcher.GetLauncherUserInfo(profile)?.UserType; }
                catch (Java.Lang.SecurityException) { }
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(30) && isCurrent && users.IsManagedProfile)
                userType = "android.os.usertype.profile.MANAGED";
            var label = isCurrent ? "Current Android profile" : $"Android profile {key}";
            try
            {
                if (users.IsQuietModeEnabled(profile))
                {
                    snapshots.Add(new(key, label, userType, isCurrent,
                        AndroidLauncherProfileAvailability.Quiet, [], false));
                    continue;
                }
                if (isCurrent && !users.IsUserUnlocked)
                {
                    snapshots.Add(new(key, label, userType, true,
                        AndroidLauncherProfileAvailability.Inaccessible, [], false));
                    continue;
                }
                var activities = (launcher.GetActivityList(null, profile) ?? [])
                    .Where(activity => activity.ComponentName is not null)
                    .Select(activity => new AndroidLauncherPlatformActivity(
                        "android:" + activity.ComponentName!.PackageName,
                        activity.ComponentName.FlattenToString()!,
                        activity.Label ?? activity.ComponentName.PackageName ?? "Android application",
                        launcher.IsActivityEnabled(activity.ComponentName, profile),
                        loadIcons ? activity.GetBadgedIcon((int)(context.Resources?.DisplayMetrics?.DensityDpi ?? 0)) : null))
                    .DistinctBy(activity => activity.Entrypoint)
                    .OrderBy(activity => activity.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
                // Android exposes no public cross-profile unlocked query on the
                // minimum supported API. An empty remote-profile result is not
                // proof of uninstall; Home must preserve prior entries unavailable.
                var complete = isCurrent || activities.Length > 0;
                snapshots.Add(new(key, label, userType, isCurrent,
                    complete ? AndroidLauncherProfileAvailability.Available : AndroidLauncherProfileAvailability.Unobserved,
                    activities, complete));
            }
            catch (Java.Lang.SecurityException)
            {
                snapshots.Add(new(key, label, userType, isCurrent,
                    AndroidLauncherProfileAvailability.Inaccessible, [], false));
            }
        }
        return snapshots;
    }

    /// <summary>Called only after Home resolves the canonical application ID and revision.
    /// Re-resolves the current Android profile and component; never falls back to the personal profile.</summary>
    public void Launch(string platformUserSerial, string entrypoint)
    {
        if (!long.TryParse(platformUserSerial, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) || serial < 0)
            throw new InvalidOperationException("The canonical Android profile identity is invalid.");
        var component = ComponentName.UnflattenFromString(entrypoint)
            ?? throw new InvalidOperationException("The canonical Android entrypoint is invalid.");
        var launcher = context.GetSystemService(Context.LauncherAppsService) as LauncherApps
            ?? throw new InvalidOperationException("Android LauncherApps service is unavailable.");
        var users = context.GetSystemService(Context.UserService) as UserManager
            ?? throw new InvalidOperationException("Android user-profile service is unavailable.");
        var profile = AccessibleProfiles(launcher, users).SingleOrDefault(user => users.GetSerialNumberForUser(user) == serial)
            ?? throw new InvalidOperationException("The application's owning Android profile is unavailable.");
        if (users.IsQuietModeEnabled(profile) || !launcher.IsActivityEnabled(component, profile))
            throw new InvalidOperationException("The application is disabled or its Android profile is paused.");
        launcher.StartMainActivity(component, profile, null, null);
    }

    public IReadOnlyList<AndroidPlatformShortcut> ListShortcuts(string platformUserSerial, string entrypoint)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(25)) throw new InvalidOperationException("This Android version does not support application shortcuts.");
        var (launcher, profile, component) = ShortcutTarget(platformUserSerial, entrypoint);
        var result = new List<AndroidPlatformShortcut>();
        foreach (var shortcut in QueryShortcuts(launcher, profile, component))
        {
            result.Add(new(shortcut.Id!, shortcut.ShortLabel ?? shortcut.Id!));
            if (result.Count == 64) break;
        }
        return result;
    }

    public void LaunchShortcut(string platformUserSerial, string entrypoint, string shortcutId)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(25)) throw new InvalidOperationException("This Android version does not support application shortcuts.");
        var (launcher, profile, component) = ShortcutTarget(platformUserSerial, entrypoint);
        ShortcutInfo? selected = null;
        foreach (var shortcut in QueryShortcuts(launcher, profile, component))
        {
            if (shortcut.Id != shortcutId) continue;
            if (selected is not null) throw new InvalidOperationException("Android returned an ambiguous shortcut identity.");
            selected = shortcut;
        }
        if (selected is null) throw new InvalidOperationException("This shortcut is no longer available. Open the app or refresh its shortcuts.");
        launcher.StartShortcut(selected, null, null);
    }

    private (LauncherApps Launcher, UserHandle Profile, ComponentName Component) ShortcutTarget(string platformUserSerial, string entrypoint)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(25)) throw new InvalidOperationException("This Android version does not support application shortcuts.");
        if (!long.TryParse(platformUserSerial, NumberStyles.None, CultureInfo.InvariantCulture, out var serial) || serial < 0)
            throw new InvalidOperationException("The canonical Android profile identity is invalid.");
        var component = ComponentName.UnflattenFromString(entrypoint)
            ?? throw new InvalidOperationException("The canonical Android entrypoint is invalid.");
        var launcher = context.GetSystemService(Context.LauncherAppsService) as LauncherApps
            ?? throw new InvalidOperationException("Android LauncherApps service is unavailable.");
        var users = context.GetSystemService(Context.UserService) as UserManager
            ?? throw new InvalidOperationException("Android user-profile service is unavailable.");
        if (!launcher.HasShortcutHostPermission)
            throw new UnauthorizedAccessException("Android has not granted this launcher access to application shortcuts. Select it as the default home app in Android settings.");
        var profile = AccessibleProfiles(launcher, users).SingleOrDefault(user => users.GetSerialNumberForUser(user) == serial)
            ?? throw new InvalidOperationException("The application's owning Android profile is unavailable.");
        if (users.IsQuietModeEnabled(profile) || !launcher.IsActivityEnabled(component, profile))
            throw new InvalidOperationException("The application is disabled or its Android profile is paused.");
        return (launcher, profile, component);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android25.0")]
    private static IEnumerable<ShortcutInfo> QueryShortcuts(LauncherApps launcher, UserHandle profile, ComponentName component)
    {
        var query = new LauncherApps.ShortcutQuery();
        query.SetPackage(component.PackageName);
        query.SetQueryFlags(LauncherAppsShortcutQueryFlags.MatchDynamic | LauncherAppsShortcutQueryFlags.MatchManifest | LauncherAppsShortcutQueryFlags.MatchPinned);
        return (launcher.GetShortcuts(query, profile) ?? []).Where(shortcut => shortcut.IsEnabled &&
            shortcut.Package == component.PackageName && shortcut.UserHandle?.Equals(profile) == true && !string.IsNullOrWhiteSpace(shortcut.Id));
    }

    private static IEnumerable<UserHandle> AccessibleProfiles(LauncherApps launcher, UserManager users) =>
        OperatingSystem.IsAndroidVersionAtLeast(26) ? launcher.Profiles ?? [] : users.UserProfiles ?? [];
}
