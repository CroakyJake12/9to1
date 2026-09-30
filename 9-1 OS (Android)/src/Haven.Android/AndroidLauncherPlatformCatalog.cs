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
        return observations.Select(profile => new InstalledApplicationProfileObservation(
            profile.PlatformUserSerial, profile.Label,
            profile.UserType == "android.os.usertype.profile.MANAGED",
            profile.IsCompleteObservation && profile.Availability == AndroidLauncherProfileAvailability.Available,
            profile.Activities.Select(activity => new InstalledApplicationObservation(activity.PackageIdentity,
                activity.Entrypoint, activity.Label, null, activity.Enabled)).ToArray())).ToArray();
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

    private static IEnumerable<UserHandle> AccessibleProfiles(LauncherApps launcher, UserManager users) =>
        OperatingSystem.IsAndroidVersionAtLeast(26) ? launcher.Profiles ?? [] : users.UserProfiles ?? [];
}
