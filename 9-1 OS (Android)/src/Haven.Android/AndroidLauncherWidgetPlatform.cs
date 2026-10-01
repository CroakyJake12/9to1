using System.Globalization;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using NineToOne.Launcher;

namespace Haven.Android;

internal sealed class LauncherAppWidgetHost(Context context, int hostId) : AppWidgetHost(context, hostId)
{
    public void ReleaseHostedViews() => ClearViews();
}

internal sealed class AndroidLauncherWidgetPreferenceStorage(ISharedPreferences preferences) : IAndroidLauncherWidgetBindingStorage
{
    private const string Key = "home_widget_bindings_v1";
    public string? Read() => preferences.GetString(Key, null);
    public bool Write(string value) => preferences.Edit()?.PutString(Key, value)?.Commit() == true;
}

/// <summary>Only IDs owned by this real AppWidgetHost and their actual OS profile/provider can be mounted.</summary>
internal sealed class AndroidLauncherWidgetPlatform(Context context, AppWidgetHost host, AppWidgetManager manager)
    : IAndroidLauncherWidgetPlatform<AppWidgetHostView>
{
    public LauncherAndroidWidgetReference? ReadOwnedProvider(int appWidgetId)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || !(host.GetAppWidgetIds() ?? []).Contains(appWidgetId)) return null;
        var info = manager.GetAppWidgetInfo(appWidgetId);
        var component = info?.Provider?.FlattenToString();
        if (info?.Profile is not { } profile || string.IsNullOrWhiteSpace(component)) return null;
        if (context.GetSystemService(Context.UserService) is not UserManager users) return null;
        var serial = users.GetSerialNumberForUser(profile);
        return serial < 0 ? null : new(component, serial.ToString(CultureInfo.InvariantCulture));
    }
    public AppWidgetHostView? CreateView(int appWidgetId)
    {
        if (ReadOwnedProvider(appWidgetId) is null) return null;
        var info = manager.GetAppWidgetInfo(appWidgetId); if (info is null) return null;
        var view = host.CreateView(context, appWidgetId, info);
        view?.SetAppWidget(appWidgetId, info); return view;
    }
    public void ReleaseView(AppWidgetHostView view)
    {
        if (view.Parent is ViewGroup parent) parent.RemoveView(view);
        // AppWidgetHost can still hold this detached Java view until its next ClearViews.
        // Do not destroy the persistent binding or dispose a view still owned by Android.
    }
}
