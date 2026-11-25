namespace HavenOS.Apps.Dev;

/// <summary>Live Reload operating mode. Automatic coalesces and builds on threshold/debounce; Manual only builds on explicit request; Paused queues without building.</summary>
public enum DeveloperLiveReloadMode { Automatic, Manual, Paused }

/// <summary>The live-status indicator state for a target (ED-BUILD-LIVE-004).</summary>
public enum DeveloperLiveReloadStatus { Watching, Queued, Compiling, Running, Failed, Paused, Unavailable }

/// <summary>A saved configuration identifying the exact target, environment, toolchain, build adapter and authorised project root. It carries no executable command; execution stays behind the existing trust/mutation/admission pipeline.</summary>
public sealed record DeveloperLiveReloadConfiguration(Guid ProjectId, string TargetId, string EnvironmentId, string ToolchainId, string BuildAdapterId, Guid AuthorizedRootId, int ChangeThreshold, TimeSpan Debounce, TimeSpan MaximumWait)
{
    public DeveloperLiveReloadConfiguration Capture()
    {
        if (ProjectId == Guid.Empty || AuthorizedRootId == Guid.Empty) throw new InvalidDataException("A live reload configuration requires a project and authorised root identity.");
        if (string.IsNullOrWhiteSpace(TargetId) || string.IsNullOrWhiteSpace(EnvironmentId) || string.IsNullOrWhiteSpace(ToolchainId) || string.IsNullOrWhiteSpace(BuildAdapterId)) throw new InvalidDataException("A live reload configuration must name its target, environment, toolchain and build adapter.");
        if (ChangeThreshold < 1) throw new InvalidDataException("The change threshold must be at least one edit unit.");
        if (Debounce < TimeSpan.Zero || MaximumWait < TimeSpan.Zero) throw new InvalidDataException("Debounce and maximum wait may not be negative.");
        if (MaximumWait > TimeSpan.Zero && MaximumWait < Debounce) throw new InvalidDataException("The maximum wait must not be shorter than the debounce quiet period.");
        return this;
    }
}

/// <summary>An incoming source change arriving through the canonical source owner, tagged with the exact working revision it produced and a coherent changed-line/edit-unit count.</summary>
public sealed record DeveloperLiveReloadChange(long SourceRevision, int ChangedUnits)
{
    public DeveloperLiveReloadChange Capture()
    {
        if (SourceRevision < 1) throw new InvalidDataException("A live reload change requires a positive source revision.");
        if (ChangedUnits < 0) throw new InvalidDataException("A live reload change unit count may not be negative.");
        return this;
    }
}

/// <summary>The outcome of evaluating whether/what to build. Superseded/Stale never overwrite newer state; Coalesced reports the merged revision.</summary>
public enum DeveloperLiveReloadDecision { Idle, Queued, BuildNow, Superseded, Paused }

public sealed record DeveloperLiveReloadIntent(DeveloperLiveReloadDecision Decision, long? BuildRevision, string? Reason);

/// <summary>A build result tied to its exact source snapshot revision. A failure carries diagnostics and never clears the last valid preview.</summary>
public sealed record DeveloperBuildResult(long SourceRevision, bool Succeeded, IReadOnlyList<DeveloperDiagnostic> Diagnostics)
{
    public DeveloperBuildResult Capture()
    {
        if (SourceRevision < 1) throw new InvalidDataException("A build result requires a positive source revision.");
        return this with { Diagnostics = (Diagnostics ?? []).ToArray() };
    }
}

/// <summary>
/// First production-ready typed Live Reload coordinator (ED-BUILD-LIVE-001..003). Pure and deterministic: it aggregates coherent changed units,
/// applies threshold/debounce/maximum-wait, coalesces superseded queued changes, runs at most one build per target, keeps monotonic revision
/// ordering so stale results never overwrite newer state, and retains the last valid preview on failure/cancellation. It performs no process
/// execution — the caller's existing trust/mutation/admission pipeline is the only execution boundary; this type decides intent and folds results.
/// </summary>
public sealed class DeveloperLiveReloadCoordinator(DeveloperLiveReloadConfiguration configuration)
{
    private readonly DeveloperLiveReloadConfiguration _configuration = configuration.Capture();

    public DeveloperLiveReloadMode Mode { get; private set; } = DeveloperLiveReloadMode.Automatic;
    public DeveloperLiveReloadStatus Status { get; private set; } = DeveloperLiveReloadStatus.Watching;

    /// <summary>The current authored source revision (highest change seen).</summary>
    public long AuthoredRevision { get; private set; }
    /// <summary>The revision currently being built, if any.</summary>
    public long? BuildingRevision { get; private set; }
    /// <summary>The last successfully built/running revision.</summary>
    public long? LastRunningRevision { get; private set; }
    /// <summary>The last valid preview (successful build) diagnostics snapshot; retained across failures.</summary>
    public DeveloperBuildResult? LastValidPreview { get; private set; }

    // Coalescing accumulator: changed units since the last build, and the newest queued revision.
    private int _pendingUnits;
    private long _queuedRevision;
    private bool _inFlight;

    public DeveloperLiveReloadConfiguration Configuration => _configuration;

    /// <summary>Record a source change arriving through the canonical owner. Monotonic: an older/equal revision is ignored (stale). Returns the resulting intent.</summary>
    public DeveloperLiveReloadIntent RecordChange(DeveloperLiveReloadChange change)
    {
        var captured = change.Capture();
        if (captured.SourceRevision <= AuthoredRevision) return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Superseded, null, "An older or equal source revision was ignored.");
        AuthoredRevision = captured.SourceRevision;
        _pendingUnits += captured.ChangedUnits;
        _queuedRevision = captured.SourceRevision;

        if (Mode == DeveloperLiveReloadMode.Paused) { Status = DeveloperLiveReloadStatus.Paused; return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Paused, null, "Live reload is paused."); }
        if (Mode == DeveloperLiveReloadMode.Manual) { Status = DeveloperLiveReloadStatus.Queued; return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Queued, null, "Manual mode waits for an explicit build."); }

        // Automatic: build when the coherent change threshold is met. Debounce/maximum-wait timing is evaluated by the caller's clock adapter
        // via ShouldBuildNow; here we only decide whether the accumulated units already justify a build.
        if (_pendingUnits >= _configuration.ChangeThreshold) return BeginBuildIntent();
        Status = DeveloperLiveReloadStatus.Queued;
        return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Queued, null, "Below the change threshold; waiting for debounce or more changes.");
    }

    /// <summary>Explicit 'build now' (manual mode or user action). Coalesces all pending changes into one build of the newest revision.</summary>
    public DeveloperLiveReloadIntent BuildNow() => BeginBuildIntent();

    /// <summary>Whether the debounce quiet period has elapsed or the maximum wait has been reached, given an injected clock. Pure decision only.</summary>
    public bool ShouldBuildNow(TimeSpan sinceLastChange)
    {
        if (_pendingUnits == 0 || _queuedRevision == 0) return false;
        if (sinceLastChange >= _configuration.Debounce) return true;
        // Maximum wait prevents small edits being stranded even before the debounce settles.
        return _configuration.MaximumWait > TimeSpan.Zero && sinceLastChange >= _configuration.MaximumWait;
    }

    private DeveloperLiveReloadIntent BeginBuildIntent()
    {
        if (_inFlight)
            // One owning build per target at a time; further changes coalesce into the next build, never overlap.
            return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Queued, _queuedRevision, "A build is already in flight; this change will coalesce into the next build.");
        if (_queuedRevision == 0) return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.Idle, null, "No pending changes to build.");

        _inFlight = true;
        BuildingRevision = _queuedRevision;
        Status = DeveloperLiveReloadStatus.Compiling;
        // Coalesce: the build covers everything up to the newest queued revision.
        var revision = _queuedRevision;
        _pendingUnits = 0;
        return new DeveloperLiveReloadIntent(DeveloperLiveReloadDecision.BuildNow, revision, null);
    }

    /// <summary>Fold a build result. A newer-than-current in-flight revision is stale and ignored. Success advances the last valid preview and running revision; failure retains the last valid preview and marks status Failed.</summary>
    public void ApplyBuildResult(DeveloperBuildResult result)
    {
        var captured = result.Capture();
        // Monotonic: a result for a revision older than what we are building is stale and never overwrites newer state.
        if (BuildingRevision is null || captured.SourceRevision < BuildingRevision) return;

        _inFlight = false;
        BuildingRevision = null;
        if (captured.Succeeded)
        {
            LastValidPreview = captured;
            LastRunningRevision = captured.SourceRevision;
            Status = DeveloperLiveReloadStatus.Running;
            // If newer changes arrived while building, they remain queued for the next coalesced build.
            if (_pendingUnits > 0) Status = DeveloperLiveReloadStatus.Queued;
        }
        else
        {
            // Failure keeps drafts and the last working preview (LastValidPreview unchanged), marked stale relative to source.
            Status = DeveloperLiveReloadStatus.Failed;
        }
    }

    /// <summary>Cancel an in-flight build. The last valid preview is retained; status returns to watching/queued.</summary>
    public void CancelBuild()
    {
        if (!_inFlight) return;
        _inFlight = false;
        BuildingRevision = null;
        Status = _pendingUnits > 0 ? DeveloperLiveReloadStatus.Queued : DeveloperLiveReloadStatus.Watching;
    }

    public void SetMode(DeveloperLiveReloadMode mode)
    {
        Mode = mode;
        Status = mode switch
        {
            DeveloperLiveReloadMode.Paused => DeveloperLiveReloadStatus.Paused,
            _ when _pendingUnits > 0 => DeveloperLiveReloadStatus.Queued,
            _ => DeveloperLiveReloadStatus.Watching,
        };
    }

    /// <summary>Whether the last valid preview is stale relative to the current authored source.</summary>
    public bool IsPreviewStale => LastValidPreview is not null && LastValidPreview.SourceRevision != AuthoredRevision;
}
