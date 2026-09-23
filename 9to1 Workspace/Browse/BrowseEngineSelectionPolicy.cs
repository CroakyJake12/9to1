namespace HavenOS.Apps.Browse;

/// <summary>Names an engine choice without implying that its native renderer is installed.</summary>
public enum BrowseEngineKind
{
    Gecko,
    Chromium
}

/// <summary>
/// Resolves the preferred engine for a tab. This policy has no renderer, persistence,
/// or network side effects; the platform host seam remains responsible for availability.
/// </summary>
public sealed class BrowseEngineSelectionPolicy
{
    private readonly Dictionary<string, BrowseEngineKind> _siteOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, BrowseEngineKind> _tabOverrides = [];

    public BrowseEngineKind DefaultEngine { get; private set; } = BrowseEngineKind.Gecko;

    public BrowseEngineKind Resolve(Uri address, Guid tabId)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (tabId != Guid.Empty && _tabOverrides.TryGetValue(tabId, out var tabEngine))
            return tabEngine;

        if (HasWebHost(address) && _siteOverrides.TryGetValue(NormalizeHost(address), out var siteEngine))
            return siteEngine;

        return DefaultEngine;
    }

    public void SetDefault(BrowseEngineKind engine) => DefaultEngine = Validate(engine);

    public void SetSiteOverride(Uri address, BrowseEngineKind? engine)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!HasWebHost(address))
            throw new ArgumentException("A site override requires an HTTP or HTTPS address.", nameof(address));

        var host = NormalizeHost(address);
        if (engine is { } selected) _siteOverrides[host] = Validate(selected);
        else _siteOverrides.Remove(host);
    }

    public void SetTabOverride(Guid tabId, BrowseEngineKind? engine)
    {
        if (tabId == Guid.Empty) throw new ArgumentException("A tab override requires a tab ID.", nameof(tabId));
        if (engine is { } selected) _tabOverrides[tabId] = Validate(selected);
        else _tabOverrides.Remove(tabId);
    }

    private static bool HasWebHost(Uri address) =>
        address.IsAbsoluteUri && (address.Scheme is "http" or "https") && !string.IsNullOrWhiteSpace(address.Host);

    private static string NormalizeHost(Uri address) => address.IdnHost.TrimEnd('.');

    private static BrowseEngineKind Validate(BrowseEngineKind engine) => Enum.IsDefined(engine)
        ? engine
        : throw new ArgumentOutOfRangeException(nameof(engine), engine, "The Browse engine choice is not recognized.");
}
