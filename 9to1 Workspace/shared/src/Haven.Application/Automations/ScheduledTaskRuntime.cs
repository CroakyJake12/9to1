using System.Globalization;
using System.Linq;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>Editable schedule fields used by Haven Automations and Plan.</summary>
public sealed record ScheduledTaskScheduleDraft(
    DateTimeOffset OnceAt,
    TimeOnly Time,
    DayOfWeek DayOfWeek,
    int IntervalHours,
    int ConditionIntervalMinutes);

/// <summary>Composes and parses the durable schedule JSON used by scheduled Tasks.</summary>
public static class ScheduledTaskScheduleComposer
{
    public static string Compose(AutomationScheduleKind kind, ScheduledTaskScheduleDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var value = kind switch
        {
            AutomationScheduleKind.Once => new Dictionary<string, object?>
            {
                ["at"] = draft.OnceAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            },
            AutomationScheduleKind.Hourly => new Dictionary<string, object?>
            {
                ["intervalHours"] = Math.Clamp(draft.IntervalHours, 1, 168)
            },
            AutomationScheduleKind.Daily => new Dictionary<string, object?> { ["time"] = FormatTime(draft.Time) },
            AutomationScheduleKind.Weekly => new Dictionary<string, object?>
            {
                ["dayOfWeek"] = draft.DayOfWeek.ToString(),
                ["time"] = FormatTime(draft.Time)
            },
            AutomationScheduleKind.ConditionWatch => new Dictionary<string, object?>
            {
                ["intervalMinutes"] = Math.Clamp(draft.ConditionIntervalMinutes, 60, 10_080)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported scheduled Task kind.")
        };
        return JsonSerializer.Serialize(value);
    }

    public static ScheduledTaskScheduleDraft Parse(
        AutomationScheduleKind kind,
        string? scheduleJson,
        DateTimeOffset now)
    {
        var fallback = new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60);
        if (string.IsNullOrWhiteSpace(scheduleJson)) return fallback;
        try
        {
            using var document = JsonDocument.Parse(scheduleJson);
            var root = document.RootElement;
            var once = root.TryGetProperty("at", out var at)
                       && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedAt)
                ? parsedAt.ToLocalTime()
                : fallback.OnceAt;
            var time = root.TryGetProperty("time", out var timeValue)
                       && TimeOnly.TryParse(timeValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
                ? parsedTime
                : fallback.Time;
            var day = root.TryGetProperty("dayOfWeek", out var dayValue)
                      && Enum.TryParse<DayOfWeek>(dayValue.GetString(), true, out var parsedDay)
                ? parsedDay
                : fallback.DayOfWeek;
            var hours = root.TryGetProperty("intervalHours", out var hourValue) && hourValue.TryGetInt32(out var parsedHours)
                ? Math.Clamp(parsedHours, 1, 168)
                : fallback.IntervalHours;
            var minutes = root.TryGetProperty("intervalMinutes", out var minuteValue) && minuteValue.TryGetInt32(out var parsedMinutes)
                ? Math.Clamp(parsedMinutes, 60, 10_080)
                : fallback.ConditionIntervalMinutes;
            return new ScheduledTaskScheduleDraft(once, time, day, hours, minutes);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static string Describe(AutomationScheduleKind kind, ScheduledTaskScheduleDraft draft) => kind switch
    {
        AutomationScheduleKind.Once => $"Once at {draft.OnceAt.ToLocalTime():g}",
        AutomationScheduleKind.Hourly => draft.IntervalHours == 1 ? "Every hour" : $"Every {draft.IntervalHours} hours",
        AutomationScheduleKind.Daily => $"Daily at {FormatTime(draft.Time)}",
        AutomationScheduleKind.Weekly => $"Every {draft.DayOfWeek} at {FormatTime(draft.Time)}",
        AutomationScheduleKind.ConditionWatch => draft.ConditionIntervalMinutes == 60
            ? "Check the condition hourly"
            : $"Check the condition every {draft.ConditionIntervalMinutes} minutes",
        _ => kind.ToString()
    };

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Calculates next-run timestamps without owning a scheduler loop.</summary>
public sealed class ScheduledTaskScheduleCalculator(TimeZoneInfo? timeZone = null)
{
    private readonly TimeZoneInfo _timeZone = timeZone ?? TimeZoneInfo.Local;

    public DateTimeOffset? GetNextRun(AutomationDefinition task, DateTimeOffset after)
    {
        if (!task.IsEnabled) return null;
        using var document = Parse(task.ScheduleJson);
        var root = document.RootElement;
        return task.ScheduleKind switch
        {
            AutomationScheduleKind.Once => ReadDate(root, "at") is { } once && once > after ? once.ToUniversalTime() : null,
            AutomationScheduleKind.Hourly => after.ToUniversalTime().AddHours(Math.Max(1, ReadInt(root, "intervalHours", 1))),
            AutomationScheduleKind.Daily => NextDaily(after, ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.Weekly => NextWeekly(after, ReadDay(root, "dayOfWeek", DayOfWeek.Monday), ReadTime(root, "time", new TimeOnly(8, 0))),
            AutomationScheduleKind.ConditionWatch => after.ToUniversalTime().AddMinutes(Math.Max(60, ReadInt(root, "intervalMinutes", 60))),
            _ => null
        };
    }

    public DateTimeOffset GetInitialRun(AutomationScheduleKind kind, string scheduleJson, DateTimeOffset now)
    {
        var placeholder = new AutomationDefinition(Guid.Empty, string.Empty, HavenMode.Chat, string.Empty, kind, scheduleJson, null, null, true, now, now);
        return GetNextRun(placeholder, now.AddTicks(-1)) ?? now.ToUniversalTime();
    }

    private DateTimeOffset NextDaily(DateTimeOffset after, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var candidate = CreateLocal(localAfter.Date, time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(1), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset NextWeekly(DateTimeOffset after, DayOfWeek day, TimeOnly time)
    {
        var localAfter = TimeZoneInfo.ConvertTime(after, _timeZone);
        var delta = ((int)day - (int)localAfter.DayOfWeek + 7) % 7;
        var candidate = CreateLocal(localAfter.Date.AddDays(delta), time);
        if (candidate <= localAfter) candidate = CreateLocal(localAfter.Date.AddDays(delta + 7), time);
        return candidate.ToUniversalTime();
    }

    private DateTimeOffset CreateLocal(DateTime date, TimeOnly time)
    {
        var unspecified = DateTime.SpecifyKind(date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, _timeZone.GetUtcOffset(unspecified));
    }

    private static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); }
        catch (JsonException exception) { throw new FormatException("The scheduled Task definition contains invalid schedule JSON.", exception); }
    }

    private static int ReadInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static DateTimeOffset? ReadDate(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value)
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static TimeOnly ReadTime(JsonElement root, string property, TimeOnly fallback) =>
        root.TryGetProperty(property, out var value)
        && TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : fallback;

    private static DayOfWeek ReadDay(JsonElement root, string property, DayOfWeek fallback) =>
        root.TryGetProperty(property, out var value) && Enum.TryParse<DayOfWeek>(value.GetString(), true, out var result)
            ? result
            : fallback;
}

public sealed record ScheduledTaskBatchResult(
    int Due, int Started, int Succeeded, int Failed, int Skipped)
{
    /// <summary>Per-Run completion uncertainties, each retaining the exact draft, RunID, execution
    /// outcome and persistence cause. Exposed for inspectable batch reporting.</summary>
    public IReadOnlyList<ScheduledTaskCompletionUncertaintyException> Uncertainties { get; init; }
        = Array.Empty<ScheduledTaskCompletionUncertaintyException>();

    /// <summary>The number of uncertain completions (equals <see cref="Uncertainties"/>.Count).</summary>
    public int Uncertain => Uncertainties.Count;
}
public sealed record ScheduledTaskConditionResult(bool ConditionMet, string Report);

/// <summary>Immutable snapshot of a fully executed run whose terminal persistence is pending or was
/// not acknowledged. It carries the EXACT desired completion payload, next schedule and the original
/// execution failure/cancellation (if any) — never a rebuilt, re-read or mutated variant.</summary>
public sealed record ScheduledTaskCompletionDraft(
    AutomationRun Run,
    DateTimeOffset? NextRunAt,
    Exception? ExecutionFailure);

/// <summary>Typed, inspectable persistence-uncertainty outcome surfaced by the runner when a terminal
/// completion could not be acknowledged. Retains the EXACT desired completion payload and next schedule
/// plus every original failure (execution and persistence), so a same-operation reconciliation can retry
/// the identical payload without re-executing any model/graph/action work.</summary>
public sealed class ScheduledTaskCompletionUncertaintyException : Exception
{
    public ScheduledTaskCompletionDraft Draft { get; }
    public Exception PersistenceCause { get; }

    public ScheduledTaskCompletionUncertaintyException(ScheduledTaskCompletionDraft draft, Exception persistenceCause)
        : base($"The {draft.Run.Status} completion of Run {draft.Run.Id} could not be acknowledged; persistence outcome is unknown ({persistenceCause.Message}).", persistenceCause)
    {
        Draft = draft;
        PersistenceCause = persistenceCause;
    }

    /// <summary>Stable machine-readable error code (spec API/error 120).</summary>
    public string Code => "ScheduledTaskCompletionUncertain";
    /// <summary>The owning Automation identity (target/action identity).</summary>
    public Guid AutomationId => Draft.Run.AutomationId;
    /// <summary>The Run identity whose completion is uncertain.</summary>
    public Guid RunId => Draft.Run.Id;
    /// <summary>This completion is recoverable by replaying only the exact payload through
    /// <see cref="ScheduledTaskRunner.ReconcileCompletionAsync"/> (never a fresh lease or re-execution).</summary>
    public bool IsRecoverable => true;
}

/// <summary>Typed preparation failure raised when schedule planning faults AFTER the terminal execution
/// was captured. It retains the actual success/failure/cancellation outcome and the original planning
/// cause; it never relabels executed work, fabricates a null schedule, or writes a premature completion.</summary>
public sealed class ScheduledTaskSchedulePlanningFailureException : Exception
{
    /// <summary>The exact captured terminal Run (immutable, with the real outcome/token/payload).</summary>
    public AutomationRun Run { get; }
    /// <summary>The original execution failure/cancellation, or null for a genuine success.</summary>
    public Exception? ExecutionFailure { get; }
    /// <summary>The original schedule-planning cause.</summary>
    public Exception PlanningCause { get; }

    public ScheduledTaskSchedulePlanningFailureException(AutomationRun run, Exception? executionFailure, Exception planningCause)
        : base($"The {run.Status} completion of Run {run.Id} could not be planned because its schedule is invalid ({planningCause.Message}).", planningCause)
    {
        Run = run;
        ExecutionFailure = executionFailure;
        PlanningCause = planningCause;
    }
}

/// <summary>Classifies the outcome of reconciling one exact terminal completion through the existing
/// idempotent store operation.</summary>
public enum ScheduledTaskCompletionReconciliationKind
{
    /// <summary>The exact completion operation was ACKNOWLEDGED (void success). This does not assert that
    /// this particular call wrote a row or performed no write; actual SQL mutation/no-write evidence
    /// belongs in the real SQLite tests.</summary>
    Acknowledged,
    /// <summary>Commit status remains unknown/unacknowledged after the attempt; the original exception is
    /// retained unchanged.</summary>
    Unknown
}

/// <summary>Outcome of a bounded completion-reconciliation attempt.</summary>
public sealed record ScheduledTaskCompletionReconciliation(
    ScheduledTaskCompletionReconciliationKind Kind,
    Guid RunId,
    Exception? Cause);

/// <summary>
/// Executes persisted scheduled Tasks on demand. It deliberately owns no timer,
/// OS scheduler registration, worker process, or hidden background loop.
/// </summary>
public sealed class ScheduledTaskRunner(
    IAutomationRepository repository,
    IOllamaClient ollama,
    ScheduledTaskScheduleCalculator schedules,
    DeviceAutomationNodeExecutor? deviceExecutor = null,
    BuiltInAutomationActionNodeExecutor? builtInExecutor = null,
    IAutomationRunContinuationStore? continuationStore = null)
{
    private const int MaximumAttempts = 3;
    private readonly IAutomationRunContinuationStore? _continuationStore = continuationStore;

    public async Task<ScheduledTaskBatchResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await repository.GetDueAsync(now, cancellationToken).ConfigureAwait(false);
        var started = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var uncertainties = new List<ScheduledTaskCompletionUncertaintyException>();
        foreach (var task in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaseToken = Guid.NewGuid().ToString("N");
            if (!await repository.TryAcquireLeaseAsync(task.Id, leaseToken, now.AddMinutes(15), cancellationToken).ConfigureAwait(false))
            {
                skipped++;
                continue;
            }
            started++;
            try
            {
                var run = await CompleteLeasedRunAsync(task, leaseToken, task.NextRunAt ?? now, cancellationToken).ConfigureAwait(false);
                if (run.Status == AutomationRunStatus.Succeeded) succeeded++;
                else if (run.Status == AutomationRunStatus.Failed) failed++;
            }
            catch (ScheduledTaskCompletionUncertaintyException uncertainty)
            {
                // Truthful: the completion is neither an ordinary failed execution nor acknowledged. It is
                // never relaunched here and never counted as a failure; its exact draft/cause is retained
                // so the batch outcome stays inspectable and reconcilable.
                uncertainties.Add(uncertainty);
            }
        }
        return new ScheduledTaskBatchResult(due.Count, started, succeeded, failed, skipped)
        {
            Uncertainties = uncertainties
        };
    }

    public async Task<AutomationRun> RunOneAsync(
        AutomationDefinition task,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        var leaseToken = Guid.NewGuid().ToString("N");
        if (!await repository.TryAcquireLeaseAsync(task.Id, leaseToken, requestedAt.AddMinutes(15), cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("This Task is already running in another 9to1 process.");
        return await CompleteLeasedRunAsync(task, leaseToken, requestedAt, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AutomationRun> CompleteLeasedRunAsync(
        AutomationDefinition task,
        string leaseToken,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        // Scheduled graphs are the durable execution path: a Wait/Approval yield suspends the SAME Run
        // (never collapsed into terminal success/failure text) and a later resume continues it. Non-graph
        // instructions keep the exact pass5 terminal completion semantics below.
        if (ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction))
            return await ExecuteDurableGraphLeasedAsync(task, leaseToken, scheduledFor, cancellationToken).ConfigureAwait(false);

        // Allocate ONE stable Run identity/execution context BEFORE any execution work. It is reused
        // across success, genuine failure/cancellation and every completion-acknowledgement attempt.
        var draft = await ExecuteToTerminalDraftAsync(task, leaseToken, scheduledFor, cancellationToken).ConfigureAwait(false);

        // Persist the single exact terminal payload once. A persistence fault never re-enters execution
        // and never fabricates a different terminal identity. Cancellation while persistence is in flight
        // has uncertain commit semantics until reconciled; it is never assumed to have rolled back and a
        // successful execution is never relabelled Cancelled.
        try
        {
            await repository.CompleteRunAsync(draft.Run, draft.NextRunAt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception persistenceFault)
        {
            throw new ScheduledTaskCompletionUncertaintyException(draft, persistenceFault);
        }

        // Completion is durably acknowledged. Preserve the original observable control-flow semantics:
        // a genuine execution cancellation is still surfaced as an OperationCanceledException, while a
        // real execution failure is returned as the persisted Failed run.
        if (draft.Run.Status == AutomationRunStatus.Cancelled)
        {
            if (draft.ExecutionFailure is OperationCanceledException cancellation) throw cancellation;
            throw new OperationCanceledException(cancellationToken);
        }
        return draft.Run;
    }

    /// <summary>Executes the acquired run exactly once into a single terminal payload, allocating ONE
    /// stable Run identity before any model/graph/action work and reusing it across all outcomes. The
    /// execution phase is governed only by <see cref="ExecuteWithRetryAsync"/>; a persistence error can
    /// never re-enter it. Execution capture, schedule planning and completion persistence are separate
    /// phases: schedule planning runs only AFTER the terminal Run and execution cause are captured, so a
    /// planning fault never relabels executed work or fabricates a null schedule.</summary>
    private async Task<ScheduledTaskCompletionDraft> ExecuteToTerminalDraftAsync(
        AutomationDefinition task,
        string leaseToken,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid(); // ONE stable identity for this execution context
        var startedAt = DateTimeOffset.UtcNow;

        // Phase 1 — capture the terminal Run and execution cause (no schedule planning here).
        var (run, executionCause) = await CaptureTerminalRunAsync(
            task, runId, leaseToken, scheduledFor, startedAt, cancellationToken).ConfigureAwait(false);

        // Phase 2 — plan the next run exactly once, fully separated from execution capture. A planning
        // fault retains the actual terminal outcome and original planning cause in a small typed failure.
        DateTimeOffset? nextRunAt;
        try
        {
            nextRunAt = NextRun(task, run.CompletedAt ?? DateTimeOffset.UtcNow);
        }
        catch (Exception planningCause)
        {
            throw new ScheduledTaskSchedulePlanningFailureException(run, executionCause, planningCause);
        }

        return new ScheduledTaskCompletionDraft(run, nextRunAt, executionCause);
    }

    /// <summary>Captures the single terminal Run and its original execution cause without any schedule
    /// planning. One stable identity is used across success, genuine failure and cancellation.</summary>
    private async Task<(AutomationRun Run, Exception? ExecutionCause)> CaptureTerminalRunAsync(
        AutomationDefinition task,
        Guid runId,
        string leaseToken,
        DateTimeOffset scheduledFor,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteWithRetryAsync(task, cancellationToken).ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            return (new AutomationRun(runId, task.Id, AutomationRunStatus.Succeeded, scheduledFor, startedAt, completedAt, result, null, leaseToken), null);
        }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            var completedAt = DateTimeOffset.UtcNow;
            return (new AutomationRun(runId, task.Id, AutomationRunStatus.Cancelled, scheduledFor, startedAt, completedAt, null, "Cancelled.", leaseToken), cancellation);
        }
        catch (Exception failure)
        {
            var completedAt = DateTimeOffset.UtcNow;
            return (new AutomationRun(runId, task.Id, AutomationRunStatus.Failed, scheduledFor, startedAt, completedAt, null, failure.Message, leaseToken), failure);
        }
    }

    /// <summary>Reconciles ONLY this exact terminal completion through one explicit bounded call of the
    /// EXISTING exact-payload <see cref="IAutomationRepository.CompleteRunAsync"/>. It never re-executes
    /// any model/graph/action work, never acquires a fresh lease or RunID, never enumerates history, and
    /// preserves the exact draft/exception identities. A void success means only that the exact completion
    /// operation was ACKNOWLEDGED — never that this call wrote or performed no write. An unacknowledged
    /// call (any exception under the current void contract, including a post-commit ACK-loss) remains
    /// Unknown with the SAME original exception retained; we never inspect messages or classify by
    /// exception type, so a newer legitimate lease/schedule is never overwritten.</summary>
    public async Task<ScheduledTaskCompletionReconciliation> ReconcileCompletionAsync(
        ScheduledTaskCompletionDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        try
        {
            await repository.CompleteRunAsync(draft.Run, draft.NextRunAt, cancellationToken).ConfigureAwait(false);
            return new ScheduledTaskCompletionReconciliation(ScheduledTaskCompletionReconciliationKind.Acknowledged, draft.Run.Id, null);
        }
        catch (Exception cause)
        {
            return new ScheduledTaskCompletionReconciliation(ScheduledTaskCompletionReconciliationKind.Unknown, draft.Run.Id, cause);
        }
    }

    /// <summary>Executes a scheduled graph payload on the durable path. One stable RunID is allocated
    /// before execution. A terminal graph outcome is persisted as a terminal Run (with the same
    /// operation-only reconciliation semantics); a durable Wait/Approval yield is persisted via
    /// SuspendRunAsync and the Run is returned as a truthful, non-terminal, suspended Run — it is never
    /// collapsed into success/failure text. Missing durable-store capability refuses a waiting workflow
    /// honestly rather than pretending it completed. Condition Watch semantics are preserved.</summary>
    private async Task<AutomationRun> ExecuteDurableGraphLeasedAsync(
        AutomationDefinition task,
        string leaseToken,
        DateTimeOffset scheduledFor,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var (run, nextRunAt) = await ExecuteDurableGraphCaptureAsync(
                task, runId, leaseToken, scheduledFor, startedAt, cancellationToken).ConfigureAwait(false);

            if (run.Status is AutomationRunStatus.Waiting or AutomationRunStatus.ApprovalRequired)
            {
                if (_continuationStore is null)
                    throw new InvalidOperationException("The durable continuation store is unavailable; cannot persist the suspended Run.");
                await _continuationStore.SuspendRunAsync(run, cancellationToken).ConfigureAwait(false);
                return run;
            }

            await PersistTerminalAsync(run, nextRunAt, cancellationToken).ConfigureAwait(false);
            return run;
        }
        catch (ScheduledTaskCompletionUncertaintyException)
        {
            // Preserve the exact uncertainty semantics: a post-commit ACK loss is never relabelled as a
            // failed execution, never relaunched, and never re-enters execution.
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelled = new AutomationRun(runId, task.Id, AutomationRunStatus.Cancelled, scheduledFor, startedAt,
                DateTimeOffset.UtcNow, null, "Cancelled.", leaseToken);
            await PersistTerminalAsync(cancelled, null, cancellationToken).ConfigureAwait(false);
            return cancelled;
        }
        catch (Exception failure)
        {
            var failed = new AutomationRun(runId, task.Id, AutomationRunStatus.Failed, scheduledFor, startedAt,
                DateTimeOffset.UtcNow, null, failure.Message, leaseToken);
            await PersistTerminalAsync(failed, null, cancellationToken).ConfigureAwait(false);
            return failed;
        }
    }

    private async Task PersistTerminalAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        try
        {
            await repository.CompleteRunAsync(run, nextRunAt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception persistenceFault)
        {
            throw new ScheduledTaskCompletionUncertaintyException(new ScheduledTaskCompletionDraft(run, nextRunAt, null), persistenceFault);
        }
    }

    private async Task<(AutomationRun Run, DateTimeOffset? NextRunAt)> ExecuteDurableGraphCaptureAsync(
        AutomationDefinition task,
        Guid runId,
        string leaseToken,
        DateTimeOffset scheduledFor,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(task.Instruction, out var payload))
            throw new InvalidOperationException("The scheduled graph payload is invalid, so 9to1 did not fall back to an instruction run.");
        if (!AutomationGraphCodec.TryDeserialize(payload.GraphJson, out var graph))
            throw new InvalidOperationException("The captured scheduled graph is unreadable.");
        if (!AutomationGraphTriggerScope.TrySelect(graph, payload.TriggerNodeId, out var scoped, out var scopeError))
            throw new InvalidOperationException(scopeError ?? "The scheduled graph trigger is invalid.");

        ScheduledTaskConditionResult? condition = null;
        if (task.ScheduleKind == AutomationScheduleKind.ConditionWatch)
        {
            if (string.IsNullOrWhiteSpace(payload.WatchCondition))
                throw new InvalidOperationException("The scheduled Condition Watch has no condition to evaluate.");
            condition = await EvaluateConditionAsync(payload.WatchCondition, task.Mode, 1, 1, cancellationToken).ConfigureAwait(false);
            if (!condition.ConditionMet)
            {
                var notMet = new AutomationRun(runId, task.Id, AutomationRunStatus.Succeeded, scheduledFor, startedAt,
                    DateTimeOffset.UtcNow, JsonSerializer.Serialize(new
                    {
                        conditionMet = false, report = condition.Report, graphExecuted = false,
                        evaluatedAt = DateTimeOffset.UtcNow
                    }), null, leaseToken);
                return (notMet, NextRun(task, DateTimeOffset.UtcNow));
            }
        }

        var scopedJson = AutomationGraphCodec.Serialize(scoped);
        var workflow = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? task.Name : payload.WorkflowName,
            "Scheduled graph snapshot",
            string.Empty,
            task.ContainerId,
            true,
            task.CreatedAt,
            DateTimeOffset.UtcNow,
            scopedJson);

        var hasIdentity = payload.GraphId is not null && payload.SourceRevision is not null
            && payload.SchemaVersion is not null && !string.IsNullOrWhiteSpace(payload.SourceDigest);
        var identity = (Guid: payload.GraphId.GetValueOrDefault(Guid.Empty), Revision: payload.SourceRevision ?? 0L,
            Schema: payload.SchemaVersion ?? 0, Digest: payload.SourceDigest ?? string.Empty);

        var graphRunner = new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor);
        var outcome = await graphRunner.RunDurableAsync(workflow, continuation: null, identity.Guid, identity.Revision,
            identity.Schema, identity.Digest, permissionGranted: false, cancellationToken).ConfigureAwait(false);

        if (outcome.Outcome is AutomationGraphRunTerminal terminal)
        {
            if (!terminal.Result.Succeeded)
            {
                var message = terminal.Result.FailureMessage ?? "Workflow graph failed.";
                var failed = new AutomationRun(runId, task.Id, AutomationRunStatus.Failed, scheduledFor, startedAt,
                    terminal.Result.CompletedAt, null, message, leaseToken);
                return (failed, NextRun(task, terminal.Result.CompletedAt));
            }
            var succeeded = new AutomationRun(runId, task.Id, AutomationRunStatus.Succeeded, scheduledFor, startedAt,
                terminal.Result.CompletedAt, JsonSerializer.Serialize(new
                {
                    conditionMet = condition?.ConditionMet,
                    conditionReport = condition?.Report,
                    graphExecuted = true,
                    graphSucceeded = true,
                    tracedNodes = terminal.Result.Trace.Count,
                    completedAt = terminal.Result.CompletedAt
                }), null, leaseToken);
            return (succeeded, NextRun(task, terminal.Result.CompletedAt));
        }

        // A durable Wait/Approval yield.
        if (_continuationStore is null)
            throw new InvalidOperationException("This graph needs durable Wait/Approval support, but no durable continuation store is configured; refusing to pretend the workflow completed.");
        if (!hasIdentity)
            throw new InvalidOperationException("A durable Wait/Approval requires canonical graph identity on the scheduled payload; refusing to suspend without it.");

        var continuation = ((AutomationGraphRunYield)outcome.Outcome).Continuation;
        var pending = continuation.PendingYield ?? throw new InvalidOperationException("The yielded continuation is missing its pending yield.");
        var status = pending.Kind == AutomationGraphYieldKind.Wait ? AutomationRunStatus.Waiting : AutomationRunStatus.ApprovalRequired;
        var due = pending.Kind == AutomationGraphYieldKind.Wait
            ? pending.ResumeAt ?? scheduledFor
            : scheduledFor;

        var suspended = new AutomationRun(runId, task.Id, status, due, startedAt, null, null, null, leaseToken)
        {
            Revision = 0,
            DefinitionRevision = identity.Revision,
            PinnedGraph = new AutomationGraphBinding(identity.Guid, identity.Revision),
            ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(continuation),
            Details = BuildRunDetails(payload, startedAt)
        };
        return (suspended, null);
    }

    /// <summary>Resumes a claimed scheduled-graph Run from its persisted continuation. Used by the
    /// continuation coordinator via <see cref="ScheduledGraphContinuationResumer"/>. The SAME Run/pin
    /// is resumed after a store reopen; no new RunID/instruction fallback/restart-from-NodeA. A resolved
    /// Wait completes once and advances; an Approval only advances with a recorded decision. The result
    /// is either a terminal Run (ready for CompleteRunAsync) or a re-suspended Run (ready for
    /// SuspendRunAsync); persistence is owned by the coordinator.</summary>
    public async Task<AutomationRunResumeResult> ResumeScheduledGraphAsync(AutomationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var definitions = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var task = definitions.FirstOrDefault(definition => definition.Id == run.AutomationId);
        if (task is null)
            return new(null, null, false, new(false, AutomationResumeErrorKind.NeedsAttention, "definition.deleted", "The definition is gone; the Run cannot be resumed."));

        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(task.Instruction, out var payload))
            return new(null, null, false, new(false, AutomationResumeErrorKind.WaitResumeFailed, "payload.invalid", "The scheduled graph payload is unreadable; refusing."));
        if (!AutomationGraphCodec.TryDeserialize(payload.GraphJson, out var graph))
            return new(null, null, false, new(false, AutomationResumeErrorKind.WaitResumeFailed, "graph.invalid", "The captured scheduled graph is unreadable; refusing."));
        if (!AutomationGraphTriggerScope.TrySelect(graph, payload.TriggerNodeId, out var scoped, out var scopeError))
            return new(null, null, false, new(false, AutomationResumeErrorKind.WaitResumeFailed, "graph.trigger.invalid", scopeError ?? "The scheduled graph trigger is invalid."));
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var continuation))
            return new(null, null, false, new(false, AutomationResumeErrorKind.WaitResumeFailed, "continuation.invalid", "The persisted continuation is unreadable; refusing."));

        // The Run's persisted pin is authoritative. A newer Active revision must NOT repoint an existing
        // Run; the continuation must match the pin or resume stops before any side effect.
        var pinnedGraphId = run.PinnedGraph?.GraphId ?? Guid.Empty;
        var pinnedRevision = run.PinnedGraph?.Revision ?? run.DefinitionRevision ?? 0L;
        if (continuation.GraphId != pinnedGraphId || continuation.PinnedDefinitionRevision != pinnedRevision)
            return new(null, null, false, new(false, AutomationResumeErrorKind.MigrationRequired, "continuation.pin.mismatch", "The continuation does not match the Run's pinned revision; refusing to resume."));
        if (pinnedGraphId == Guid.Empty || pinnedRevision < 1)
            return new(null, null, false, new(false, AutomationResumeErrorKind.WaitResumeFailed, "continuation.pin.missing", "The Run has no authoritative pinned graph identity; refusing to resume."));

        var workflow = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? task.Name : payload.WorkflowName,
            "Scheduled graph snapshot",
            string.Empty,
            task.ContainerId,
            true,
            task.CreatedAt,
            DateTimeOffset.UtcNow,
            AutomationGraphCodec.Serialize(scoped));

        var graphRunner = new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor);
        var outcome = await graphRunner.RunDurableAsync(workflow, continuation, pinnedGraphId, pinnedRevision,
            continuation.SchemaVersion, continuation.SourceDigest, permissionGranted: false, cancellationToken).ConfigureAwait(false);

        if (outcome.Outcome is AutomationGraphRunTerminal terminal)
        {
            var completedAt = terminal.Result.CompletedAt;
            if (!terminal.Result.Succeeded)
            {
                var updated = run with
                {
                    Revision = run.Revision + 1,
                    Status = AutomationRunStatus.Failed,
                    CompletedAt = completedAt,
                    Error = terminal.Result.FailureMessage ?? "Workflow graph failed.",
                    Result = null
                };
                return new(updated, NextRun(task, completedAt), false, new(true, AutomationResumeErrorKind.None, "resume.failed", "The resumed run failed.", run.Id));
            }
            var updatedOk = run with
            {
                Revision = run.Revision + 1,
                Status = AutomationRunStatus.Succeeded,
                CompletedAt = completedAt,
                Result = JsonSerializer.Serialize(new
                {
                    graphExecuted = true,
                    graphSucceeded = true,
                    tracedNodes = terminal.Result.Trace.Count,
                    completedAt
                }),
                Error = null
            };
            return new(updatedOk, NextRun(task, completedAt), false, new(true, AutomationResumeErrorKind.None, "resume.succeeded", "The resumed run completed.", run.Id));
        }

        if (_continuationStore is null)
            return new(null, null, false, new(false, AutomationResumeErrorKind.NeedsAttention, "durable.unavailable", "The durable continuation store is unavailable; the Run stays suspended."));

        var yielded = ((AutomationGraphRunYield)outcome.Outcome).Continuation;
        var pendingYield = yielded.PendingYield ?? throw new InvalidOperationException("The yielded continuation is missing its pending yield.");
        var status = pendingYield.Kind == AutomationGraphYieldKind.Wait ? AutomationRunStatus.Waiting : AutomationRunStatus.ApprovalRequired;
        var due = pendingYield.Kind == AutomationGraphYieldKind.Wait
            ? pendingYield.ResumeAt ?? run.ScheduledFor
            : run.ScheduledFor;
        var reSuspended = run with
        {
            Revision = run.Revision + 1,
            Status = status,
            ScheduledFor = due,
            CompletedAt = null,
            ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(yielded)
        };
        return new(reSuspended, null, true, new(true, AutomationResumeErrorKind.None, "resume.resuspended", "The run was re-suspended.", run.Id));
    }

    private static AutomationRunDetails BuildRunDetails(ScheduledGraphAutomationPayload payload, DateTimeOffset startedAt) =>
        new(null, null, """{"source":"scheduled-graph"}""", null,
            """[{"nodeId":"trigger","state":"started"}]""",
            null, "[]", "[]", payload.GraphId);

    private DateTimeOffset? NextRun(AutomationDefinition task, DateTimeOffset completedAt) =>
        task.IsEnabled ? schedules.GetNextRun(task, completedAt) : null;

    private async Task<string> ExecuteWithRetryAsync(AutomationDefinition task, CancellationToken cancellationToken)
    {
        var maximumAttempts = ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction) ? 1 : MaximumAttempts;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await ExecuteOnceAsync(task, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt == maximumAttempts) break;
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException($"Task failed after {maximumAttempts} attempt{(maximumAttempts == 1 ? string.Empty : "s")}: {lastError?.Message}", lastError);
    }

    private async Task<string> ExecuteOnceAsync(AutomationDefinition task, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        if (ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction))
        {
            if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(task.Instruction, out var payload))
                throw new InvalidOperationException("The scheduled graph payload is invalid, so 9to1 did not fall back to an instruction run.");
            return await ExecuteGraphPayloadAsync(task, payload, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
        }

        if (task.ScheduleKind == AutomationScheduleKind.ConditionWatch)
        {
            var result = await EvaluateConditionAsync(task.Instruction, task.Mode, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(new { conditionMet = result.ConditionMet, report = result.Report, evaluatedAt = DateTimeOffset.UtcNow });
        }

        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(candidate => candidate.Supports(ToolCapability.Text))
                    ?? throw new InvalidOperationException("No text-capable model is installed.");
        var system = $"You are 9to1 Automations executing a user-approved scheduled instruction. Mode: {task.Mode}. Execute safely and return a concise run report. Never claim external actions unless confirmed. Attempt {attempt} of {maximumAttempts}.";
        return await ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", task.Instruction)],
            EffortLevel.Medium,
            system), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGraphPayloadAsync(AutomationDefinition task, ScheduledGraphAutomationPayload payload, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        if (!AutomationGraphCodec.TryDeserialize(payload.GraphJson, out var graph))
            throw new InvalidOperationException("The captured scheduled graph is unreadable.");
        if (!AutomationGraphTriggerScope.TrySelect(graph, payload.TriggerNodeId, out var scoped, out var scopeError))
            throw new InvalidOperationException(scopeError ?? "The scheduled graph trigger is invalid.");

        ScheduledTaskConditionResult? condition = null;
        if (task.ScheduleKind == AutomationScheduleKind.ConditionWatch)
        {
            if (string.IsNullOrWhiteSpace(payload.WatchCondition))
                throw new InvalidOperationException("The scheduled Condition Watch has no condition to evaluate.");
            condition = await EvaluateConditionAsync(payload.WatchCondition, task.Mode, attempt, maximumAttempts, cancellationToken).ConfigureAwait(false);
            if (!condition.ConditionMet)
                return JsonSerializer.Serialize(new { conditionMet = false, report = condition.Report, graphExecuted = false, evaluatedAt = DateTimeOffset.UtcNow });
        }

        var scopedJson = AutomationGraphCodec.Serialize(scoped);
        var workflow = new ReusableTaskDefinition(
            payload.WorkflowId,
            string.IsNullOrWhiteSpace(payload.WorkflowName) ? task.Name : payload.WorkflowName,
            "Scheduled graph snapshot",
            string.Empty,
            task.ContainerId,
            true,
            task.CreatedAt,
            DateTimeOffset.UtcNow,
            scopedJson);
        var graphRun = await new ReusableDeviceWorkflowRunner(deviceExecutor, builtInExecutor)
            .RunAsync(workflow, permissionGranted: false, cancellationToken)
            .ConfigureAwait(false);
        if (!graphRun.Handled || graphRun.GraphResult is null)
            throw new InvalidOperationException("The scheduled graph could not be executed, and 9to1 did not substitute an instruction run.");
        if (!graphRun.GraphResult.Succeeded)
            throw new InvalidOperationException(graphRun.GraphResult.FailureMessage ?? graphRun.Message);

        return JsonSerializer.Serialize(new
        {
            conditionMet = condition?.ConditionMet,
            conditionReport = condition?.Report,
            graphExecuted = true,
            graphSucceeded = true,
            tracedNodes = graphRun.GraphResult.Trace.Count,
            completedAt = graphRun.GraphResult.CompletedAt
        });
    }

    private async Task<ScheduledTaskConditionResult> EvaluateConditionAsync(string instruction, HavenMode mode, int attempt, int maximumAttempts, CancellationToken cancellationToken)
    {
        var models = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(candidate => candidate.Supports(ToolCapability.Text))
                    ?? throw new InvalidOperationException("No text-capable model is installed.");
        var system = $"You are 9to1 Automations evaluating a scheduled condition. Mode: {mode}. Return one JSON object and no markdown: {{\"conditionMet\":true|false,\"report\":\"concise evidence-based report\"}}. Fail closed when evidence is missing or ambiguous. Never claim an external action unless it was confirmed. Attempt {attempt} of {maximumAttempts}.";
        var raw = await ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", instruction)],
            EffortLevel.Medium,
            system), cancellationToken).ConfigureAwait(false);
        return ScheduledTaskConditionParser.Parse(raw);
    }
}

/// <summary>Adapters <see cref="ScheduledTaskRunner.ResumeScheduledGraphAsync"/> to the coordinator's
/// <see cref="IAutomationRunResumer"/> contract. It runs in a claimed context, revalidates through the
/// owning graph/action paths, and never sets Home's permissionGranted from workflow approval.</summary>
public sealed class ScheduledGraphContinuationResumer(ScheduledTaskRunner runner) : IAutomationRunResumer
{
    public Task<AutomationRunResumeResult> ResumeAsync(AutomationRun run, CancellationToken cancellationToken)
        => runner.ResumeScheduledGraphAsync(run, cancellationToken);
}

public static class ScheduledTaskConditionParser
{
    public static ScheduledTaskConditionResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new(false, "The condition check returned no evidence and was treated as not met.");
        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) throw new JsonException("No JSON object was returned.");
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            var met = root.TryGetProperty("conditionMet", out var condition)
                      && condition.ValueKind is JsonValueKind.True or JsonValueKind.False
                      && condition.GetBoolean();
            var report = root.TryGetProperty("report", out var reportElement) ? reportElement.GetString()?.Trim() : null;
            return new(met, string.IsNullOrWhiteSpace(report)
                ? met ? "The condition was reported as met without further detail." : "The condition was not met."
                : report);
        }
        catch (JsonException)
        {
            var bounded = response.Trim();
            if (bounded.Length > 1000) bounded = bounded[..1000] + "…";
            return new(false, "The condition check returned an unstructured response and was treated as not met. Response: " + bounded);
        }
    }
}
