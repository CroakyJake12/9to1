namespace HavenOS.Apps.Browse;

/// <summary>
/// Composes the installed-locator and the bundled-stage locator into a single
/// effective runtime resolution used both for availability reporting and actual
/// process launch. The bundled private-engine bundle wins when verified; otherwise
/// it falls back to the installed native candidate. This is the single wiring point
/// so the packaged browser is actually usable, while preserving the separate
/// installed-default candidates and their unchanged test-injection semantics.
/// </summary>
public class ChromiumRuntimeResolver
{
    public static readonly ChromiumRuntimeResolver Instance = new();

    private readonly ChromiumLocator _installed;
    private readonly BundledChromiumLocator _bundled;
    private readonly bool _useExplicitRuntime;

    protected ChromiumRuntimeResolver(ChromiumLocator? installed = null,
                                      BundledChromiumLocator? bundled = null)
    {
        _useExplicitRuntime = installed is null && bundled is null;
        _installed = installed ?? ChromiumLocator.Instance;
        _bundled = bundled ?? BundledChromiumLocator.Instance;
    }

    /// <summary>An explicit "no bundled" locator so an injected installed-only
    /// resolver never consults the global bundled default.</summary>
    private sealed class NoBundled : BundledChromiumLocator
    {
        public override string CandidateLauncher => "/nonexistent/bundle/launcher";
        public override string? Find() => null;
    }

    private static readonly BundledChromiumLocator NoBundledInstance = new NoBundled();

    /// <summary>Build a resolver whose installed part is a specific locator and
    /// whose bundled part is explicitly disabled — this preserves the existing
    /// test-injection semantics where only the injected installed locator is
    /// consulted. Production uses <see cref="Instance"/> (bundle + installed).</summary>
    public static ChromiumRuntimeResolver FromInstalled(ChromiumLocator installed)
        => new(installed, NoBundledInstance);

    /// <summary>Effective runtime path (bundled launcher, else installed native).</summary>
    public virtual string? Find()
    {
        if (!OperatingSystem.IsLinux()) return null;
        return (_useExplicitRuntime ? ExplicitChromiumRuntime.FindConfigured() : null) ?? _bundled.Find() ?? _installed.Find();
    }
}
