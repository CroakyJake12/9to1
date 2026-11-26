using System.Text.Json.Serialization;

namespace HavenOS.Apps.Dev.Autonomous;

public enum DeveloperAutonomousMissionState
{
    Idle,
    SelectingTask,
    Implementing,
    Verifying,
    Reviewing,
    Integrating,
    Paused,
    Completed,
    Blocked,
    Failed
}

public enum DeveloperMissionTaskState
{
    Queued,
    Active,
    Implementing,
    Verifying,
    ReviewPending,
    ReworkRequired,
    Approved,
    Integrated,
    Blocked,
    Deferred
}

public enum DeveloperAgentRole
{
    Worker,
    Supervisor,
    DelegatedSubagent
}

public sealed record DeveloperAutonomousMissionOptions
{
    public required string RepositoryRoot { get; init; }
    public string? ProgrammeDirectory { get; init; }
    public string? StoreDirectory { get; init; }
    public string? WorktreesRoot { get; init; }
    public string? MissionName { get; init; }
    public string? Objective { get; init; }
    public string? WorkerModelId { get; init; }
    public string? SupervisorModelId { get; init; }
    public string? PreferredOwner { get; init; }
    public string? BaseRevision { get; init; }
    public string? IntegrationBranch { get; init; }
    public bool AutoAdvance { get; init; } = true;
    public bool GuardedIntegration { get; init; } = true;
    public bool RequireSupervisorReview { get; init; } = true;
    public bool RequireTestFileChange { get; init; } = true;
    public bool EnableReasoning { get; init; } = true;
    public int ContextBudgetTokens { get; init; } = 131_072;
    public int CompactThresholdTokens { get; init; } = 48_000;
    public int MaxCompletionTokens { get; init; } = 8_192;
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxTurnsPerTask { get; init; } = 24;
    public int MaxIdleTurns { get; init; } = 5;
    // Null means no overall task-count limit; explicit bounded runs remain supported.
    public int? MaxTasksPerRun { get; init; }
    public int MemoryFloorMiB { get; init; } = 1100;
    public long DiskFloorGiB { get; init; } = 10;
    public string? SdkLockPath { get; init; }
    public string? GuardedBuildScriptPath { get; init; }
    public string DotNetExecutable { get; init; } = "dotnet";
    public string? DefaultBuildProject { get; init; }
    public string? DefaultTestProject { get; init; }
    public int CommandTimeoutSeconds { get; init; } = 600;
}

public sealed record DeveloperModelInfo(
    string Id,
    string Name,
    string State,
    string? Gpu,
    int ContextLength,
    int Slots,
    string? Engine,
    string? Ownership,
    bool? CanStart = null)
{
    public bool IsLoaded => State.Equals("loaded", StringComparison.OrdinalIgnoreCase)
        || State.Equals("ready", StringComparison.OrdinalIgnoreCase)
        || State.Equals("running", StringComparison.OrdinalIgnoreCase)
        || State.Equals("active", StringComparison.OrdinalIgnoreCase);

    // This is eligibility for the canonical request boundary, not proof of loading.
    // An explicit daemon decision overrides legacy installed-state inference.
    public bool IsAvailableForInference => IsLoaded || CanStart == true
        || (CanStart is null && (State.Equals("installed", StringComparison.OrdinalIgnoreCase)
            || State.Equals("unloaded", StringComparison.OrdinalIgnoreCase)
            || State.Equals("stopped", StringComparison.OrdinalIgnoreCase)
            || State.Equals("idle", StringComparison.OrdinalIgnoreCase)));
}

public sealed record DeveloperGpuTelemetry(
    string Name,
    int UsedMiB,
    int TotalMiB,
    int FreeMiB,
    double? TemperatureC,
    double? UtilizationPercent);

public sealed record DeveloperResourceSnapshot(
    long HostAvailableMiB,
    long HostTotalMiB,
    long DiskFreeBytes,
    double? CpuLoad1m,
    IReadOnlyList<DeveloperGpuTelemetry> Gpus);

public sealed record DeveloperDulcheStatus(
    string Phase,
    bool StopAll,
    IReadOnlyList<DeveloperModelInfo> Models,
    DeveloperResourceSnapshot Resources,
    int ActiveAgentsCount);

public sealed record DeveloperChatMessage(
    string Role,
    string Content);

public sealed record DeveloperCompletionRequest(
    string ModelId,
    IReadOnlyList<DeveloperChatMessage> Messages,
    int MaxTokens = 4096,
    double Temperature = 0.1,
    bool EnableReasoning = true,
    bool RequireJsonObject = false,
    string? Objective = null,
    string? IdempotencyKey = null);

public sealed record DeveloperStreamChunk(
    string? ContentDelta,
    string? ReasoningDelta,
    string? FinishReason,
    int? PromptTokens,
    int? CompletionTokens,
    double? TokensPerSecond,
    string? RunId);

public sealed record DeveloperCompletionResult(
    string RunId,
    string ModelId,
    string Content,
    string ReasoningContent,
    string FinishReason,
    int PromptTokens,
    int CompletionTokens,
    double? TokensPerSecond,
    TimeSpan Elapsed);

public sealed record DeveloperParsedDiagnostic(
    string RelativePath,
    int Line,
    int Column,
    string Severity,
    string Code,
    string Message);

public sealed record DeveloperBuildVerificationReport(
    bool BuildSucceeded,
    bool TestsSucceeded,
    int BuildExitCode,
    int TestExitCode,
    int TestsPassed,
    int TestsFailed,
    int TestsSkipped,
    string BuildProject,
    string? TestProject,
    string? TestFilter,
    string CombinedOutput,
    IReadOnlyList<DeveloperParsedDiagnostic> Diagnostics,
    DateTimeOffset VerifiedAt,
    string SourceDiffSha256,
    double DurationSeconds);

public sealed record DeveloperReviewReceipt(
    string TaskId,
    string ReviewerModelId,
    string WorkerModelId,
    string DiffSha256,
    IReadOnlyList<string> ChangedPaths,
    bool Approved,
    string Reason,
    string BindingDigest,
    DateTimeOffset ReviewedAt);

public sealed record DeveloperIntegrationReceipt(
    string MissionId,
    string TaskId,
    string BranchName,
    string ParentRevision,
    string CommitRevision,
    string DiffSha256,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> RequirementIds,
    string WorkerModelId,
    string? ReviewerModelId,
    DateTimeOffset IntegratedAt);

public sealed record DeveloperDelegatedAgentRun(
    string RunId,
    DeveloperAgentRole Role,
    string ModelId,
    string Objective,
    string Status,
    string LiveContent,
    string LiveReasoning,
    int PromptTokens,
    int CompletionTokens,
    double? TokensPerSecond,
    string? DulcheRunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultSummary);

public sealed record DeveloperMissionMessage(
    int Index,
    string Role,
    string Content,
    string? ReasoningContent,
    string? Operation,
    string? Target,
    DateTimeOffset Timestamp,
    string Sha256,
    string ChainSha256);

public sealed record DeveloperMissionNote(
    int MessageIndex,
    string? TaskId,
    string Finding,
    string? NextAction,
    IReadOnlyList<string> References,
    DateTimeOffset CreatedAt);

public sealed record DeveloperMissionContextEpoch(
    string EpochId,
    string MissionId,
    string? ActiveTaskId,
    int CoveredMessageCount,
    int ProjectedPromptTokens,
    string IndexChainHead,
    string Reason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<DeveloperChatMessage> ProjectedMessages);

public sealed record DeveloperMissionCheckpoint(
    string CheckpointId,
    string MissionId,
    string? TaskId,
    DeveloperAutonomousMissionState MissionState,
    string BaseRevision,
    string CurrentRevision,
    string DiffSha256,
    IReadOnlyList<string> ChangedPaths,
    int HistoryMessageCount,
    string HistoryChainHead,
    int LastPromptTokens,
    string Reason,
    string Summary,
    DateTimeOffset CreatedAt,
    string? WorktreePath);

public sealed record DeveloperMissionEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Kind,
    string Message,
    string? TaskId = null,
    string? ModelId = null,
    string? Details = null);

public sealed record DeveloperMissionTask
{
    public required string TaskId { get; init; }
    public required string ModuleId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<string> RecordIds { get; init; }
    public required string Focus { get; init; }
    public required string AcceptanceCriteria { get; init; }
    public IReadOnlyList<string> AllowedSourcePaths { get; init; } = [];
    public IReadOnlyList<string> AllowedTestPaths { get; init; } = [];
    public string? BuildProject { get; init; }
    public string? TestProject { get; init; }
    public string? TestFilter { get; init; }
    public string? WorktreePath { get; init; }
    public string? BaseRevision { get; init; }
    public string? CurrentRevision { get; init; }
    public DeveloperMissionTaskState State { get; init; } = DeveloperMissionTaskState.Queued;
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];
    public string DiffText { get; init; } = "";
    public string DiffSha256 { get; init; } = "";
    public DeveloperBuildVerificationReport? Verification { get; init; }
    public DeveloperReviewReceipt? Review { get; init; }
    public DeveloperIntegrationReceipt? Integration { get; init; }
    public int TurnCount { get; init; }
    public int IdleTurns { get; init; }
    public IReadOnlyList<string> VisitedActionDigests { get; init; } = [];
    public string? Summary { get; init; }
    public string? BlockerReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record DeveloperAutonomousMission
{
    public const string CurrentSchema = "9to1-build-autonomous-mission-v1";
    public string Schema { get; init; } = CurrentSchema;
    public required string MissionId { get; init; }
    public required string Name { get; init; }
    public required string Objective { get; init; }
    public required string RepositoryRoot { get; init; }
    public string? ProgrammeDirectory { get; init; }
    public required string IntegrationBranch { get; init; }
    public required string BaseRevision { get; init; }
    public required string CurrentRevision { get; init; }
    public string? WorkerModelId { get; init; }
    public string? SupervisorModelId { get; init; }
    public string? PreferredOwner { get; init; }
    public DeveloperAutonomousMissionState State { get; init; } = DeveloperAutonomousMissionState.Idle;
    public bool AutoAdvance { get; init; } = true;
    public bool GuardedIntegration { get; init; } = true;
    public bool RequireSupervisorReview { get; init; } = true;
    public bool RequireTestFileChange { get; init; } = true;
    public bool EnableReasoning { get; init; } = true;
    public int ContextBudgetTokens { get; init; } = 131_072;
    public int CompactThresholdTokens { get; init; } = 48_000;
    public int LastPromptTokens { get; init; }
    public int ProgrammeModuleCursor { get; init; }
    public int TranscriptCount { get; init; }
    public string TranscriptChainHead { get; init; } = "";
    public string? ActiveEpochId { get; init; }
    public string? ActiveDulcheRunId { get; init; }
    public bool CompletionStopUnconfirmed { get; init; }
    public DeveloperMissionTask? ActiveTask { get; init; }
    public IReadOnlyList<DeveloperMissionTask> CompletedTasks { get; init; } = [];
    public IReadOnlyList<DeveloperMissionTask> BlockedTasks { get; init; } = [];
    public IReadOnlyList<DeveloperMissionCheckpoint> Checkpoints { get; init; } = [];
    public IReadOnlyList<DeveloperMissionNote> Notes { get; init; } = [];
    public IReadOnlyList<DeveloperDelegatedAgentRun> DelegatedRuns { get; init; } = [];
    public IReadOnlyList<string> CompletedRecordIds { get; init; } = [];
    public IReadOnlyList<string> BlockedRecordIds { get; init; } = [];
    public string? LastError { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public IReadOnlyList<DeveloperMissionTask> AllTasks
    {
        get
        {
            var list = new List<DeveloperMissionTask>();
            if (ActiveTask is not null) list.Add(ActiveTask);
            list.AddRange(CompletedTasks);
            list.AddRange(BlockedTasks);
            return list;
        }
    }
}
