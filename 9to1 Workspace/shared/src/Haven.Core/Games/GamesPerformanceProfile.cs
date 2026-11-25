namespace Haven.Core.Games;

/// <summary>How an expensive compile/render job is executed when the local device cannot afford it.
/// RemoteUnavailable keeps the capability visible rather than silently approximating or dropping work.</summary>
public enum GamesRemoteExecutorMode { LocalOnly, RemoteIfAvailable, RemoteRequired }

/// <summary>A declared compatible remote executor for expensive jobs. It is a descriptor only: no live backend is claimed here.</summary>
public sealed record GamesRemoteExecutor(string ExecutorID, string Platform, bool Compatible)
{
    public GamesRemoteExecutor Capture()
    {
        if (string.IsNullOrWhiteSpace(ExecutorID)) throw new InvalidDataException("Games remote executor identity is required.");
        if (string.IsNullOrWhiteSpace(Platform)) throw new InvalidDataException("Games remote executor platform is required.");
        return this;
    }
}

/// <summary>A pure authored low-power / low-memory profile. It reduces preview resolution, scene-detail/work scheduling and cache residency while
/// preserving authored assets and full project semantics (the profile never carries asset data). Values are finite and bounded.</summary>
public sealed record GamesPerformanceProfile(string ProfileID, string Name, double PreviewResolutionScale, int DetailBudget,
    int WorkSchedulingBudget, int CacheResidencyMB, bool AllowBackgroundRendering, GamesRemoteExecutorMode RemoteExecutorMode)
{
    public const int MaximumDetailBudget = 1_000_000;
    public const int MaximumWorkSchedulingBudget = 1_000_000;
    public const int MaximumCacheResidencyMB = 1_048_576;

    public GamesPerformanceProfile Capture()
    {
        if (string.IsNullOrWhiteSpace(ProfileID)) throw new InvalidDataException("Games performance profile identity is required.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Games performance profile name is required.");
        if (!double.IsFinite(PreviewResolutionScale) || PreviewResolutionScale <= 0 || PreviewResolutionScale > 1)
            throw new InvalidDataException("Games preview resolution scale must be finite and within (0, 1].");
        if (DetailBudget < 1 || DetailBudget > MaximumDetailBudget) throw new InvalidDataException("Games detail budget is out of range.");
        if (WorkSchedulingBudget < 1 || WorkSchedulingBudget > MaximumWorkSchedulingBudget) throw new InvalidDataException("Games work scheduling budget is out of range.");
        if (CacheResidencyMB < 0 || CacheResidencyMB > MaximumCacheResidencyMB) throw new InvalidDataException("Games cache residency is out of range.");
        if (!Enum.IsDefined(RemoteExecutorMode)) throw new InvalidDataException("Games remote executor mode is unsupported.");
        return this;
    }

    /// <summary>A conservative low-power preset that reduces resolution/detail/cache but keeps full project semantics.</summary>
    public static GamesPerformanceProfile LowPower(string profileID = "low-power") =>
        new(profileID, "Low Power", 0.5, 32, 2, 128, AllowBackgroundRendering: false, RemoteExecutorMode.RemoteIfAvailable).Capture();

    public static GamesPerformanceProfile FullQuality(string profileID = "full-quality") =>
        new(profileID, "Full Quality", 1.0, 4096, 16, 4096, AllowBackgroundRendering: true, RemoteExecutorMode.LocalOnly).Capture();
}

/// <summary>The availability of a single local capability. Unavailable capabilities are reported explicitly (with an optional reason)
/// rather than being silently approximated or hidden.</summary>
public sealed record GamesCapability(string CapabilityID, bool AvailableLocally, string? UnavailableReason)
{
    public GamesCapability Capture()
    {
        if (string.IsNullOrWhiteSpace(CapabilityID)) throw new InvalidDataException("Games capability identity is required.");
        if (!AvailableLocally && string.IsNullOrWhiteSpace(UnavailableReason))
            throw new InvalidDataException("An unavailable local capability must state why it is unavailable.");
        return this;
    }
}

/// <summary>The authored performance/capability document. It selects a profile and records declared remote executors and per-capability availability.
/// Revision-aware immutable transactions never mutate the original and never touch authored assets.</summary>
public sealed record GamesPerformanceDocument(int Revision, string SelectedProfileID, IReadOnlyList<GamesPerformanceProfile> Profiles,
    IReadOnlyList<GamesRemoteExecutor> RemoteExecutors, IReadOnlyList<GamesCapability> Capabilities)
{
    public const int CurrentSchemaVersion = 1;

    public GamesPerformanceDocument Capture()
    {
        if (Revision < 1) throw new InvalidDataException("Games performance document revision must be positive.");
        if (Profiles is null || RemoteExecutors is null || Capabilities is null) throw new InvalidDataException("Games performance document structure is missing.");
        var profiles = Profiles.Select(p => p?.Capture() ?? throw new InvalidDataException("Games performance profile is missing.")).ToArray();
        if (profiles.Select(p => p.ProfileID).Distinct(StringComparer.Ordinal).Count() != profiles.Length) throw new InvalidDataException("Games performance profiles contain duplicate identities.");
        if (!profiles.Any(p => string.Equals(p.ProfileID, SelectedProfileID, StringComparison.Ordinal)))
            throw new InvalidOperationException("GamesSelectedProfileNotFound");
        var executors = RemoteExecutors.Select(e => e?.Capture() ?? throw new InvalidDataException("Games remote executor is missing.")).ToArray();
        if (executors.Select(e => e.ExecutorID).Distinct(StringComparer.Ordinal).Count() != executors.Length) throw new InvalidDataException("Games remote executors contain duplicate identities.");
        var capabilities = Capabilities.Select(c => c?.Capture() ?? throw new InvalidDataException("Games capability is missing.")).ToArray();
        if (capabilities.Select(c => c.CapabilityID).Distinct(StringComparer.Ordinal).Count() != capabilities.Length) throw new InvalidDataException("Games capabilities contain duplicate identities.");
        return this with { Profiles = profiles, RemoteExecutors = executors, Capabilities = capabilities };
    }

    public static GamesPerformanceDocument Create(IEnumerable<GamesPerformanceProfile> profiles, string selectedProfileID)
        => new(1, selectedProfileID, profiles.ToArray(), Array.Empty<GamesRemoteExecutor>(), Array.Empty<GamesCapability>()).Capture();

    private GamesPerformanceDocument Next(int expectedRevision, string selectedProfileID, IReadOnlyList<GamesPerformanceProfile> profiles,
        IReadOnlyList<GamesRemoteExecutor> executors, IReadOnlyList<GamesCapability> capabilities)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesPerformanceRevisionConflict");
        return new GamesPerformanceDocument(checked(Revision + 1), selectedProfileID, profiles, executors, capabilities).Capture();
    }

    public GamesPerformanceDocument SelectProfile(string profileID, int expectedRevision)
        => Next(expectedRevision, profileID, Profiles, RemoteExecutors, Capabilities);

    public GamesPerformanceDocument AddProfile(GamesPerformanceProfile profile, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (Profiles.Any(existing => string.Equals(existing.ProfileID, profile.ProfileID, StringComparison.Ordinal)))
            throw new InvalidOperationException("GamesPerformanceProfileAlreadyPresent");
        return Next(expectedRevision, SelectedProfileID, Profiles.Append(profile).ToArray(), RemoteExecutors, Capabilities);
    }

    public GamesPerformanceDocument DeclareExecutor(GamesRemoteExecutor executor, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (RemoteExecutors.Any(existing => string.Equals(existing.ExecutorID, executor.ExecutorID, StringComparison.Ordinal)))
            throw new InvalidOperationException("GamesRemoteExecutorAlreadyPresent");
        return Next(expectedRevision, SelectedProfileID, Profiles, RemoteExecutors.Append(executor).ToArray(), Capabilities);
    }

    public GamesPerformanceDocument SetCapability(GamesCapability capability, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(capability);
        var exists = Capabilities.Any(existing => string.Equals(existing.CapabilityID, capability.CapabilityID, StringComparison.Ordinal));
        var capabilities = exists
            ? Capabilities.Select(existing => string.Equals(existing.CapabilityID, capability.CapabilityID, StringComparison.Ordinal) ? capability : existing).ToArray()
            : Capabilities.Append(capability).ToArray();
        return Next(expectedRevision, SelectedProfileID, Profiles, RemoteExecutors, capabilities);
    }

    /// <summary>Resolve the currently selected profile.</summary>
    public GamesPerformanceProfile ResolveSelected() =>
        Profiles.Single(profile => string.Equals(profile.ProfileID, SelectedProfileID, StringComparison.Ordinal));
}
