namespace HavenOS.Apps.Dev;

/// <summary>Lifecycle state of an owned process handle. Fake/deterministic so isolation is provable without a real OS process.</summary>
public enum DeveloperProcessState { Running, Stopping, Exited }

/// <summary>A single owned process handle within a session's tree. It carries its own PID and parent link so Stop can target exactly the subtree
/// spawned by one run configuration rather than every process sharing a binary name.</summary>
public sealed record DeveloperProcessHandle(int Pid, int? ParentPid, string Label, DeveloperProcessState State)
{
    public DeveloperProcessHandle Capture()
    {
        if (Pid <= 0) throw new InvalidDataException("An owned process handle requires a positive PID.");
        if (ParentPid is { } parent && parent <= 0) throw new InvalidDataException("A parent PID must be positive when present.");
        if (string.IsNullOrWhiteSpace(Label)) throw new InvalidDataException("An owned process handle requires a label.");
        return this;
    }
}

/// <summary>The result of stopping an owned process tree: exactly which PIDs were terminated and the session's real outcome.</summary>
public sealed record DeveloperProcessStopResult(Guid SessionId, IReadOnlyList<int> StoppedPids, string RealResult)
{
    public DeveloperProcessStopResult Capture()
    {
        if (SessionId == Guid.Empty) throw new InvalidDataException("A stop result requires a session identity.");
        var pids = (StoppedPids ?? Array.Empty<int>()).ToArray();
        if (pids.Distinct().Count() != pids.Length) throw new InvalidOperationException("A stop result lists a PID more than once.");
        return this with { StoppedPids = Array.AsReadOnly(pids) };
    }
}

/// <summary>An immutable owned-process tree for one run configuration. RegisterChild attaches only descendants spawned within the session; Stop
/// terminates exactly that subtree and leaves other sessions' processes alive. Handles are deterministic fakes — no real OS process is launched.</summary>
public sealed record DeveloperProcessSession(Guid SessionId, string RunConfigurationId, int RootPid, IReadOnlyList<DeveloperProcessHandle> Processes)
{
    public const int MaximumProcesses = 1024;

    public DeveloperProcessSession Capture()
    {
        if (SessionId == Guid.Empty) throw new InvalidDataException("A process session requires an identity.");
        if (string.IsNullOrWhiteSpace(RunConfigurationId)) throw new InvalidDataException("A process session requires its owning run configuration.");
        var handles = (Processes ?? Array.Empty<DeveloperProcessHandle>()).Select(h => h?.Capture() ?? throw new InvalidDataException("An owned process handle is missing.")).ToArray();
        if (handles.Length > MaximumProcesses) throw new InvalidDataException("The owned process tree exceeds the configured limit.");
        if (handles.Select(h => h.Pid).Distinct().Count() != handles.Length) throw new InvalidOperationException("Duplicate PID in an owned process tree.");
        if (!handles.Any(h => h.Pid == RootPid)) throw new InvalidOperationException("An owned process tree must contain its root PID.");
        var pids = handles.Select(h => h.Pid).ToHashSet();
        foreach (var handle in handles)
            if (handle.ParentPid is { } parent && !pids.Contains(parent)) throw new InvalidOperationException("An owned process references a parent outside its session tree.");
        return this with { Processes = Array.AsReadOnly(handles) };
    }

    public static DeveloperProcessSession Start(Guid sessionId, string runConfigurationId, int rootPid, string label)
        => new(sessionId, runConfigurationId, rootPid, [new DeveloperProcessHandle(rootPid, null, label, DeveloperProcessState.Running)]).Capture();

    /// <summary>Attach a child process spawned within this session. The parent must already belong to the tree so an unrelated PID cannot be adopted.</summary>
    public DeveloperProcessSession RegisterChild(int pid, int parentPid, string label)
    {
        var captured = Capture();
        if (pid <= 0) throw new InvalidDataException("A child PID must be positive.");
        if (captured.Processes.Any(h => h.Pid == pid)) throw new InvalidOperationException("That PID is already owned by this session.");
        if (!captured.Processes.Any(h => h.Pid == parentPid && h.State == DeveloperProcessState.Running)) throw new InvalidOperationException("A child may only attach to a running process in this session.");
        return (captured with { Processes = [.. captured.Processes, new DeveloperProcessHandle(pid, parentPid, label, DeveloperProcessState.Running)] }).Capture();
    }

    /// <summary>Terminate exactly this session's owned subtree. Returns the PIDs stopped and the real per-process result; other sessions are untouched.</summary>
    public DeveloperProcessStopResult Stop(string realResult)
    {
        var captured = Capture();
        if (string.IsNullOrWhiteSpace(realResult)) throw new InvalidDataException("A stop must report the run's real result.");
        var stopped = captured.Processes.Where(h => h.State == DeveloperProcessState.Running).Select(h => h.Pid).ToArray();
        return new DeveloperProcessStopResult(captured.SessionId, Array.AsReadOnly(stopped), realResult).Capture();
    }

    /// <summary>Whether a given PID belongs to this session's tree (used to prove another session stays alive).</summary>
    public bool Owns(int pid) => Processes.Any(h => h.Pid == pid);
}
