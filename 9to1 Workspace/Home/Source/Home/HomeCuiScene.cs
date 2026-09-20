using NineToOne.Cui.Markup;

namespace HavenOS.Home;

public enum HomeCuiAction
{
    InstallAllUpdates,
}

/// <summary>
/// Loads the authored Home surface and exposes only typed intent and state for a future CUI host.
/// Rendering is intentionally outside this domain assembly.
/// </summary>
public sealed class HomeCuiSurface
{
    private readonly Queue<HomeCuiAction> _actions = new();

    public HomeCuiSurface(CuiDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }
    public HomeDashboardSnapshot? Snapshot { get; private set; }
    public bool CanInstallAll { get; private set; }
    public string OperationStatus { get; private set; } = string.Empty;

    public static HomeCuiSurface LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "Home.cui");
        return new HomeCuiSurface(new CuiMarkupLoader().Load(path));
    }

    public void ApplySnapshot(HomeDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        CanInstallAll = snapshot.CanInstallAll;
        OperationStatus = $"{snapshot.LastOperation.State}: {snapshot.LastOperation.Message}";
    }

    public bool RequestInstallAll()
    {
        if (!CanInstallAll)
            return false;

        _actions.Enqueue(HomeCuiAction.InstallAllUpdates);
        return true;
    }

    public bool TryDequeueAction(out HomeCuiAction action) => _actions.TryDequeue(out action);
}

/// <summary>Maps authored CUI intent to the Home domain service and projects observed state.</summary>
public sealed class HomeCuiController(HomeDashboard dashboard, HomeCuiSurface? surface = null)
{
    private readonly HomeDashboard _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));

    public HomeCuiSurface Surface { get; } = surface ?? HomeCuiSurface.LoadDefault();

    public HomeDashboardSnapshot ShowCurrent()
    {
        var snapshot = _dashboard.Current;
        Surface.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _dashboard.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Surface.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> ExecuteAsync(
        HomeCuiAction action,
        CancellationToken cancellationToken = default)
    {
        var snapshot = action switch
        {
            HomeCuiAction.InstallAllUpdates => await _dashboard.InstallAllAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        Surface.ApplySnapshot(snapshot);
        return snapshot;
    }
}
