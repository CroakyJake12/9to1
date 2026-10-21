using Avalonia.Threading;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Installed-Chromium Linux engine producer. Only explicit Chromium requests are
/// admitted; Gecko requests are refused and never silently switched. Availability
/// is truthful: if no executable Chromium is installed, both engines report
/// unsupported and CreateAsync throws before any process or view is created. The
/// engine surface (a Control) is constructed on the UI thread — the process/CDP
/// work may run on any thread, but the control must be created and owned by the
/// UI thread.
/// </summary>
public sealed class ChromiumEngineFactory : IBrowseEngineHostFactory, IBrowseEngineAvailability
{
    private readonly ChromiumRuntimeResolver _resolver;
    private readonly ChromiumProfilePool _pool;
    private readonly Func<Func<ChromiumEngineTab>, Task<ChromiumEngineTab>> _uiMarshal;

    public ChromiumEngineFactory() : this(ChromiumRuntimeResolver.Instance) { }

    internal ChromiumEngineFactory(ChromiumRuntimeResolver resolver)
        : this(resolver, new ChromiumProfilePool(resolver: resolver)) { }

    internal ChromiumEngineFactory(ChromiumRuntimeResolver resolver, ChromiumProfilePool pool)
        : this(resolver, pool, DefaultUiMarshal) { }

    internal ChromiumEngineFactory(ChromiumRuntimeResolver resolver, ChromiumProfilePool pool,
        Func<Func<ChromiumEngineTab>, Task<ChromiumEngineTab>> uiMarshal)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _pool = pool;
        _uiMarshal = uiMarshal ?? throw new ArgumentNullException(nameof(uiMarshal));
    }

    // Preserve existing test-injection overloads that inject a ChromiumLocator.
    internal ChromiumEngineFactory(ChromiumLocator locator)
        : this(ChromiumRuntimeResolver.FromInstalled(locator)) { }

    internal ChromiumEngineFactory(ChromiumLocator locator, ChromiumProfilePool pool)
        : this(ChromiumRuntimeResolver.FromInstalled(locator), pool) { }

    internal ChromiumEngineFactory(ChromiumLocator locator, ChromiumProfilePool pool,
        Func<Func<ChromiumEngineTab>, Task<ChromiumEngineTab>> uiMarshal)
        : this(ChromiumRuntimeResolver.FromInstalled(locator), pool, uiMarshal) { }

    /// <summary>Marshals control construction onto the Avalonia UI thread so the
    /// approved-fork surface is never created on a pool thread.</summary>
    private static Task<ChromiumEngineTab> DefaultUiMarshal(Func<ChromiumEngineTab> create)
    {
        if (Dispatcher.UIThread.CheckAccess()) return Task.FromResult(create());
        return Dispatcher.UIThread.InvokeAsync(create).GetTask();
    }

    public bool IsSupported => _resolver.Find() is not null;

    public string UnsupportedReason => _resolver.Find() is not null
        ? "Chromium is installed but could not be started for this tab."
        : "No Chromium runtime is available: the bundled stage did not verify and no installed Chromium was found on this Linux system.";

    public bool IsEngineSupported(BrowseEngineKind engine) => IsSupported && engine == BrowseEngineKind.Chromium;

    public string UnsupportedReasonFor(BrowseEngineKind engine) => engine switch
    {
        BrowseEngineKind.Gecko => "The Firefox (Gecko) engine is not installed on this Linux system. Chromium remains a separate, explicitly chosen backend.",
        BrowseEngineKind.Chromium => "Installed Chromium was not found or could not start on this Linux system. Its tab identity, preference and profile are retained.",
        _ => "The requested browser engine has no Linux supplier."
    };

    public async Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The installed-Chromium engine is Linux-only.");
        if (!IsEngineSupported(request.Engine)) throw new PlatformNotSupportedException(UnsupportedReasonFor(request.Engine));
        if (request.TabId == Guid.Empty) throw new ArgumentException("A canonical tab id is required.", nameof(request));
        if (!Path.IsPathFullyQualified(request.ProfileDirectory)) throw new ArgumentException("The tab must supply an absolute engine-isolated profile.", nameof(request));

        var lease = await _pool.AcquireAsync(request.ProfileDirectory, cancellationToken).ConfigureAwait(false);
        ChromiumEngineTab? tab = null;
        try
        {
            // Construct on the UI thread; the tab's surface is a Control.
            tab = await _uiMarshal(() => new ChromiumEngineTab(request, lease)).ConfigureAwait(false);
            await tab.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return tab;
        }
        catch
        {
            // A failed tab never publishes. Retire its owned target and release its
            // profile lease (the pool only closes the shared process once no sibling
            // tab holds it), and never leave an event subscription behind.
            if (tab is not null)
            {
                try { await tab.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort: the original failure is rethrown */ }
            }
            else
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }
}
