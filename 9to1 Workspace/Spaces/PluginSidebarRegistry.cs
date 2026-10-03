using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Apps.Spaces;

public enum PluginSidebarTargetKind { Page, AppSurface, CuiSurface, Automation, GenerativePage }

/// <summary>A declared, stable Plugin contribution; labels and positions never act as identity.</summary>
public sealed record PluginSidebarContribution(
    string PluginId, string SidebarItemId, string Label, string IconKey,
    PluginSidebarTargetKind TargetKind, string TargetId);

public sealed record PluginSidebarVisibility(Guid SpaceId, string PluginId, string SidebarItemId, bool Visible, long Revision);
public sealed record PluginSidebarState(int SchemaVersion, PluginSidebarVisibility[] Items);

/// <summary>The host revalidates installation, Space enablement and resource permissions on every operation.</summary>
public interface IPluginSidebarAuthority
{
    Task<IReadOnlyList<PluginSidebarContribution>> GetDeclaredAsync(Guid spaceId, CancellationToken cancellationToken);
    Task<bool> MayManageAsync(Guid spaceId, CancellationToken cancellationToken);
    Task<bool> MayOpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken);
    Task OpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken);
}

/// <summary>
/// Independent per-Space visibility for declared Plugin items. Global installation never implies
/// visibility. Host startup must use one settings instance per profile; gates are shared by that
/// instance. This settings contract does not provide cross-process compare-and-swap.
/// </summary>
public sealed class PluginSidebarRegistry(IVersionedSettingsStore settings, IPluginSidebarAuthority authority)
{
    private const string Key = "spaces.plugin-sidebar.visibility";
    private static readonly ConditionalWeakTable<IVersionedSettingsStore, SemaphoreSlim> Gates = new();
    private readonly SemaphoreSlim _gate = Gates.GetValue(settings, _ => new(1, 1));

    public async Task<IReadOnlyList<PluginSidebarContribution>> GetVisibleAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        RequireSpace(spaceId);
        var declared = await GetDeclarationsAsync(spaceId, cancellationToken).ConfigureAwait(false);
        PluginSidebarState state;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { state = await ReadAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
        var visible = new List<PluginSidebarContribution>();
        foreach (var item in declared)
            if (state.Items.Any(row => row.SpaceId == spaceId && row.PluginId == item.PluginId &&
                row.SidebarItemId == item.SidebarItemId && row.Visible) &&
                await authority.MayOpenAsync(spaceId, item, cancellationToken).ConfigureAwait(false)) visible.Add(item);
        return visible;
    }

    public async Task<PluginSidebarVisibility> SetVisibilityAsync(Guid spaceId, string pluginId, string sidebarItemId,
        bool visible, long expectedRevision, CancellationToken cancellationToken = default)
    {
        RequireSpace(spaceId);
        if (!await authority.MayManageAsync(spaceId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Space Plugin sidebar settings are not authorised.");
        var declarations = await GetDeclarationsAsync(spaceId, cancellationToken).ConfigureAwait(false);
        if (!declarations.Any(item => item.PluginId == pluginId && item.SidebarItemId == sidebarItemId))
            throw new InvalidOperationException("The Plugin item is not declared and enabled in this Space.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var current = state.Items.SingleOrDefault(row => row.SpaceId == spaceId && row.PluginId == pluginId && row.SidebarItemId == sidebarItemId);
            if ((current?.Revision ?? 0) != expectedRevision) throw new InvalidOperationException("Sidebar visibility revision conflict.");
            var next = new PluginSidebarVisibility(spaceId, pluginId, sidebarItemId, visible, checked(expectedRevision + 1));
            await settings.SetAsync(Key, state with { Items = [.. state.Items.Where(row => row != current), next] }, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    public async Task OpenAsync(Guid spaceId, string pluginId, string sidebarItemId, CancellationToken cancellationToken = default)
    {
        var visible = await GetVisibleAsync(spaceId, cancellationToken).ConfigureAwait(false);
        var item = visible.SingleOrDefault(row => row.PluginId == pluginId && row.SidebarItemId == sidebarItemId)
            ?? throw new UnauthorizedAccessException("Plugin item is hidden, unavailable or unauthorised in this Space.");
        // The authoritative host rechecks resource permissions when executing the declared target.
        await authority.OpenAsync(spaceId, item, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PluginSidebarContribution>> GetDeclarationsAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        var items = (await authority.GetDeclaredAsync(spaceId, cancellationToken).ConfigureAwait(false)).ToArray();
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.PluginId) || string.IsNullOrWhiteSpace(item.SidebarItemId) ||
                string.IsNullOrWhiteSpace(item.Label) || string.IsNullOrWhiteSpace(item.TargetId) || !Enum.IsDefined(item.TargetKind))
                throw new InvalidDataException("Invalid Plugin sidebar declaration.");
            if (item.TargetKind == PluginSidebarTargetKind.Page &&
                (!Uri.TryCreate(item.TargetId, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
                throw new InvalidDataException("Plugin page targets require an HTTP or HTTPS address.");
        }
        if (items.GroupBy(item => (item.PluginId, item.SidebarItemId)).Any(group => group.Count() != 1))
            throw new InvalidDataException("Duplicate stable Plugin sidebar identities.");
        return items;
    }

    private async Task<PluginSidebarState> ReadAsync(CancellationToken cancellationToken)
    {
        var state = await settings.GetAsync<PluginSidebarState>(Key, cancellationToken).ConfigureAwait(false) ?? new(1, []);
        if (state.SchemaVersion != 1 || state.Items is null || state.Items.Any(item => item is null || item.SpaceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(item.PluginId) || string.IsNullOrWhiteSpace(item.SidebarItemId) || item.Revision < 1) ||
            state.Items.GroupBy(item => (item.SpaceId, item.PluginId, item.SidebarItemId)).Any(group => group.Count() != 1))
            throw new InvalidDataException("Stored Plugin sidebar visibility is invalid; it was preserved.");
        return state;
    }

    private static void RequireSpace(Guid spaceId)
    {
        if (spaceId == Guid.Empty) throw new ArgumentException("A stable Space identity is required.", nameof(spaceId));
    }
}
