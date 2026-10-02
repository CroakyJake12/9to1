using System.Text;
using System.Text.Json;
using NineToOne.Launcher;

namespace Haven.Android;

internal sealed record AndroidLauncherWidgetBinding(int AppWidgetId, string HomeAuthorityId, Guid? PlacementId,
    LauncherAndroidWidgetReference Provider);
internal interface IAndroidLauncherWidgetBindingStorage
{
    string? Read();
    bool Write(string value);
}

/// <summary>Device-local OS binding cache only. Home owns geometry/profile membership; a cache entry grants neither.</summary>
internal sealed class AndroidLauncherWidgetBindings(IAndroidLauncherWidgetBindingStorage storage)
{
    private const int MaximumBytes = 1024 * 1024;
    private sealed record Document(int Version, IReadOnlyList<AndroidLauncherWidgetBinding> Bindings);
    private readonly object _gate = new();
    public IReadOnlyList<AndroidLauncherWidgetBinding> Read()
    { lock (_gate) return ReadCore().ToArray(); }
    public AndroidLauncherWidgetBinding? Find(string authority, Guid placementId)
        => Read().SingleOrDefault(binding => binding.HomeAuthorityId == authority && binding.PlacementId == placementId);
    public void Associate(AndroidLauncherWidgetBinding binding)
    {
        lock (_gate)
        {
            var values = ReadCore().ToList(); var old = values.SingleOrDefault(value => value.AppWidgetId == binding.AppWidgetId);
            if (old is not null && old.HomeAuthorityId != binding.HomeAuthorityId)
                throw new UnauthorizedAccessException("This Android widget belongs to a different Home profile.");
            values.RemoveAll(value => value.AppWidgetId == binding.AppWidgetId); values.Add(binding); WriteCore(values);
        }
    }
    public void Forget(AndroidLauncherWidgetBinding expected)
    {
        lock (_gate)
        {
            var values = ReadCore().ToList();
            if (!values.Remove(expected)) throw new IOException("The saved widget binding changed. Refresh before removing it.");
            WriteCore(values);
        }
    }
    private IReadOnlyList<AndroidLauncherWidgetBinding> ReadCore()
    {
        var raw = storage.Read(); if (string.IsNullOrEmpty(raw)) return [];
        if (Encoding.UTF8.GetByteCount(raw) > MaximumBytes) throw new InvalidDataException("Android widget bindings require recovery; existing data was preserved.");
        Document value;
        try { value = JsonSerializer.Deserialize<Document>(raw) ?? throw new JsonException(); }
        catch (JsonException error) { throw new InvalidDataException("Android widget bindings require recovery; existing data was preserved.", error); }
        if (value.Version != 1) throw new InvalidDataException("These Android widget bindings require a compatible Launcher version.");
        Validate(value.Bindings); return value.Bindings;
    }
    private void WriteCore(IReadOnlyList<AndroidLauncherWidgetBinding> values)
    {
        Validate(values); var json = JsonSerializer.Serialize(new Document(1, values));
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes || !storage.Write(json))
            throw new IOException("Android could not save the device widget binding. The Home placement remains recoverable.");
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Any(char.IsControl);
    private static void Validate(IReadOnlyList<AndroidLauncherWidgetBinding>? values)
    {
        if (values is null || values.Count > 1024 || values.Any(value => value is null || value.AppWidgetId <= 0 ||
            !Text(value.HomeAuthorityId) || value.PlacementId == Guid.Empty || value.Provider is null ||
            !Text(value.Provider.ProviderComponent) || !Text(value.Provider.PlatformProfileId)) ||
            values.Select(value => value.AppWidgetId).Distinct().Count() != values.Count ||
            values.Where(value => value.PlacementId is not null).GroupBy(value => (value.HomeAuthorityId, value.PlacementId)).Any(group => group.Count() != 1))
            throw new InvalidDataException("Android widget binding identity is invalid; existing data was preserved.");
    }
}

internal interface IAndroidLauncherWidgetPlatform<TView> where TView : class
{
    LauncherAndroidWidgetReference? ReadOwnedProvider(int appWidgetId);
    TView? CreateView(int appWidgetId);
    void ReleaseView(TView view);
}

/// <summary>Fresh Home session and actual OS-owned binding checks surround view construction. No caller package/profile is a grant.</summary>
internal sealed class AndroidLauncherWidgetViews<TView>(HomeLauncherSession sessions, AndroidLauncherWidgetBindings bindings,
    IAndroidLauncherWidgetPlatform<TView> platform) where TView : class
{
    public async Task<TView?> CreateAsync(LauncherSessionSnapshot snapshot, Guid placementId, CancellationToken ct = default)
    {
        var read = await sessions.ReadWidgetAsync(snapshot, placementId, ct);
        if (read?.Placement.Android is not { } provider) return null;
        var binding = bindings.Find(snapshot.Layout.AuthorityId, placementId);
        if (binding is null || binding.Provider != provider || platform.ReadOwnedProvider(binding.AppWidgetId) != provider ||
            !await sessions.IsCurrentAsync(snapshot, ct)) return null;
        ct.ThrowIfCancellationRequested();
        var view = platform.CreateView(binding.AppWidgetId); if (view is null) return null;
        var accepted = false;
        try
        {
            if (bindings.Find(snapshot.Layout.AuthorityId, placementId) != binding || platform.ReadOwnedProvider(binding.AppWidgetId) != provider || !await sessions.IsCurrentAsync(snapshot, ct)) return null;
            accepted = true; return view;
        }
        finally { if (!accepted) platform.ReleaseView(view); }
    }
}

/// <summary>Activity-local platform callback phase only; never a Home or binding authority.</summary>
internal sealed class AndroidLauncherWidgetSelectionPhase
{
    private int _allocatedId;
    private bool _configuration;
    public void Begin(int allocatedId)
    {
        if (allocatedId <= 0 || _allocatedId != 0) throw new InvalidOperationException("Widget selection is already active or invalid.");
        _allocatedId = allocatedId; _configuration = false;
    }
    public bool IsExpected(int allocatedId, bool configuration)
        => allocatedId > 0 && allocatedId == _allocatedId && configuration == _configuration;
    public void BeginConfiguration(int allocatedId)
    {
        if (!IsExpected(allocatedId, false)) throw new InvalidOperationException("Widget configuration does not belong to the current picker.");
        _configuration = true;
    }
    public void Complete(int allocatedId)
    {
        if (allocatedId != _allocatedId) return;
        _allocatedId = 0; _configuration = false;
    }
}
