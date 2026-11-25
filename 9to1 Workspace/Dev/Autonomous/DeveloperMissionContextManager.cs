using System.Text.Json;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Manages working-context budgets, exact Dulche tokenizer counting, lossless epoch compaction,
/// and verbatim recall from the hash-chained canonical transcript so long autonomous missions
/// survive hundreds of turns and multiple requirements without context overflow or memory loss.
/// </summary>
public sealed class DeveloperMissionContextManager
{
    private readonly DeveloperAutonomousMissionStore _store;
    private readonly IDeveloperModelGateway _gateway;

    public DeveloperMissionContextManager(
        DeveloperAutonomousMissionStore store,
        IDeveloperModelGateway gateway)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public async Task<int> MeasurePromptTokensAsync(
        string modelId,
        IReadOnlyList<DeveloperChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _gateway.CountTokensAsync(modelId, messages, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or NotSupportedException)
        {
            // Conservative fallback (~3 chars/token + message framing) when tokenizer endpoint is offline.
            var chars = messages.Sum(m => m.Content.Length + m.Role.Length + 16);
            return Math.Max(1, chars / 3);
        }
    }

    public async Task<(IReadOnlyList<DeveloperChatMessage> Messages, int PromptTokens, DeveloperMissionContextEpoch? NewEpoch)>
        BuildWorkingContextAsync(
            DeveloperAutonomousMission mission,
            string systemPrompt,
            string modelId,
            int maxContextTokens,
            int compactThresholdTokens,
            bool forceCompact = false,
            string compactReason = "threshold",
            CancellationToken cancellationToken = default)
    {
        var transcript = _store.ReadTranscript(mission.MissionId);
        DeveloperMissionContextEpoch? activeEpoch = !string.IsNullOrWhiteSpace(mission.ActiveEpochId)
            ? _store.LoadContextEpoch(mission.MissionId, mission.ActiveEpochId)
            : null;

        if (!forceCompact && activeEpoch is not null
            && activeEpoch.ActiveTaskId == mission.ActiveTask?.TaskId
            && activeEpoch.CoveredMessageCount <= transcript.Count)
        {
            var projected = activeEpoch.ProjectedMessages.ToList();
            for (var i = activeEpoch.CoveredMessageCount; i < transcript.Count; i++)
            {
                AppendProjected(projected, transcript[i], maxChars: 16_000);
            }
            var tokens = await MeasurePromptTokensAsync(modelId, projected, cancellationToken).ConfigureAwait(false);
            var safeCeiling = Math.Min(compactThresholdTokens, Math.Max(4_096, maxContextTokens - 8_192));
            if (tokens <= safeCeiling && transcript.Count - activeEpoch.CoveredMessageCount <= 48)
            {
                return (projected, tokens, null);
            }
            compactReason = tokens > safeCeiling
                ? $"prompt tokens {tokens} exceeded threshold {safeCeiling}"
                : "turn window exceeded";
        }
        else if (!forceCompact && activeEpoch is null && transcript.Count <= 40)
        {
            var raw = new List<DeveloperChatMessage> { new("system", systemPrompt) };
            foreach (var msg in transcript)
                AppendProjected(raw, msg, maxChars: 24_000);
            if (raw.Count == 1)
                raw.Add(new("user", BuildMissionLedgerMessage(mission, transcript.Count)));
            var tokens = await MeasurePromptTokensAsync(modelId, raw, cancellationToken).ConfigureAwait(false);
            var safeCeiling = Math.Min(compactThresholdTokens, Math.Max(4_096, maxContextTokens - 8_192));
            if (tokens <= safeCeiling)
                return (raw, tokens, null);
            compactReason = $"initial window tokens {tokens} exceeded threshold {safeCeiling}";
        }

        // Lossless compaction: canonical transcript stays untouched; build a fresh epoch projection.
        var compacted = new List<DeveloperChatMessage>
        {
            new("system", systemPrompt),
            new("user", BuildMissionLedgerMessage(mission, transcript.Count))
        };

        var tailCount = maxContextTokens >= 200_000 ? 8 : 4;
        var tailStart = Math.Max(0, transcript.Count - tailCount);
        for (var i = tailStart; i < transcript.Count; i++)
            AppendProjected(compacted, transcript[i], maxChars: maxContextTokens >= 200_000 ? 12_000 : 4_000);

        if (compacted[^1].Role != "user")
            compacted.Add(new("user", "Continue from the active mission ledger and receipts with one valid JSON action."));

        var compactedTokens = await MeasurePromptTokensAsync(modelId, compacted, cancellationToken).ConfigureAwait(false);
        var hardCeiling = Math.Max(4_096, maxContextTokens - 6_144);
        if (compactedTokens > hardCeiling)
        {
            // Trim tail further while keeping full authoritative ledger & active task.
            compacted =
            [
                new("system", systemPrompt),
                new("user", BuildMissionLedgerMessage(mission, transcript.Count, compactMode: true))
            ];
            if (transcript.Count > 0)
                AppendProjected(compacted, transcript[^1], maxChars: 3_000);
            if (compacted[^1].Role != "user")
                compacted.Add(new("user", "Continue from the active mission ledger with one valid JSON action."));
            compactedTokens = await MeasurePromptTokensAsync(modelId, compacted, cancellationToken).ConfigureAwait(false);
        }

        var epochId = "ep-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "-" + Guid.NewGuid().ToString("N")[..6];
        var chainHead = transcript.Count == 0 ? "" : transcript[^1].ChainSha256;
        var epoch = new DeveloperMissionContextEpoch(
            EpochId: epochId,
            MissionId: mission.MissionId,
            ActiveTaskId: mission.ActiveTask?.TaskId,
            CoveredMessageCount: transcript.Count,
            ProjectedPromptTokens: compactedTokens,
            IndexChainHead: chainHead,
            Reason: compactReason,
            CreatedAt: DateTimeOffset.UtcNow,
            ProjectedMessages: compacted);
        _store.SaveContextEpoch(epoch);
        return (compacted, compactedTokens, epoch);
    }

    public string RecallByIndex(string missionId, int index, int offset = 0, int length = 4000)
    {
        var transcript = _store.ReadTranscript(missionId);
        if (index < 0 || index >= transcript.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Valid transcript indices are 0..{Math.Max(0, transcript.Count - 1)}.");
        offset = Math.Max(0, offset);
        length = Math.Clamp(length, 1, 16_000);
        var msg = transcript[index];
        var slice = offset >= msg.Content.Length
            ? ""
            : msg.Content.Substring(offset, Math.Min(length, msg.Content.Length - offset));
        return JsonSerializer.Serialize(new
        {
            index = msg.Index,
            role = msg.Role,
            sha256 = msg.Sha256,
            offset,
            total_characters = msg.Content.Length,
            content = slice,
            next_offset = offset + slice.Length < msg.Content.Length ? (int?)(offset + slice.Length) : null
        });
    }

    public string RecallByQuery(string missionId, string query, int offset = 0, int maxResults = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var transcript = _store.ReadTranscript(missionId);
        var hits = new List<object>();
        foreach (var msg in transcript)
        {
            var pos = msg.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) continue;
            var start = Math.Max(0, pos - 80);
            var end = Math.Min(msg.Content.Length, pos + query.Length + 180);
            hits.Add(new
            {
                index = msg.Index,
                role = msg.Role,
                operation = msg.Operation,
                excerpt = msg.Content[start..end]
            });
        }
        offset = Math.Max(0, offset);
        var page = hits.Skip(offset).Take(Math.Clamp(maxResults, 1, 20)).ToArray();
        return JsonSerializer.Serialize(new
        {
            query,
            total = hits.Count,
            matches = page,
            next_offset = offset + page.Length < hits.Count ? (int?)(offset + page.Length) : null
        });
    }

    private static void AppendProjected(List<DeveloperChatMessage> target, DeveloperMissionMessage msg, int maxChars)
    {
        var text = msg.Content;
        if (text.Length > maxChars)
            text = text[..maxChars] + $"\n[Truncated in working projection; call {{\"op\":\"recall\",\"index\":{msg.Index},\"offset\":{maxChars}}} for verbatim remainder.]";
        if (msg.Role == "user" && target.Count > 1 && target[^1].Role == "user")
            target[^1] = new("user", target[^1].Content + "\n\n" + text);
        else
            target.Add(new(msg.Role, text));
    }

    private static string BuildMissionLedgerMessage(
        DeveloperAutonomousMission mission,
        int transcriptCount,
        bool compactMode = false)
    {
        var completed = mission.CompletedTasks.Select(t => new
        {
            task_id = t.TaskId,
            module = t.ModuleId,
            records = t.RecordIds,
            state = t.State.ToString(),
            changed = t.ChangedPaths,
            diff_sha256 = t.DiffSha256,
            integrated_commit = t.Integration?.CommitRevision,
            summary = compactMode ? Truncate(t.Summary, 160) : Truncate(t.Summary, 400)
        }).ToArray();

        var blocked = mission.BlockedTasks.Select(t => new
        {
            task_id = t.TaskId,
            module = t.ModuleId,
            records = t.RecordIds,
            reason = Truncate(t.BlockerReason, 240)
        }).ToArray();

        var notes = mission.Notes.TakeLast(compactMode ? 4 : 12).Select(n => new
        {
            task = n.TaskId,
            finding = Truncate(n.Finding, compactMode ? 200 : 500),
            next = Truncate(n.NextAction, 160),
            refs = n.References
        }).ToArray();

        object? active = mission.ActiveTask is not { } task ? null : new
        {
            task_id = task.TaskId,
            module = task.ModuleId,
            title = task.Title,
            records = task.RecordIds,
            state = task.State.ToString(),
            focus = compactMode ? Truncate(task.Focus, 1800) : task.Focus,
            acceptance = task.AcceptanceCriteria,
            allowed_source_paths = task.AllowedSourcePaths,
            allowed_test_paths = task.AllowedTestPaths,
            build_project = task.BuildProject,
            test_project = task.TestProject,
            changed_paths = task.ChangedPaths,
            diff_sha256 = task.DiffSha256,
            diff_preview = Truncate(task.DiffText, compactMode ? 2000 : 6000),
            last_build_ok = task.Verification?.BuildSucceeded,
            last_tests_ok = task.Verification?.TestsSucceeded,
            last_review_approved = task.Review?.Approved,
            last_review_reason = task.Review?.Reason
        };

        var ledger = new
        {
            mission_id = mission.MissionId,
            mission_name = mission.Name,
            objective = mission.Objective,
            integration_branch = mission.IntegrationBranch,
            current_revision = mission.CurrentRevision,
            transcript_messages = transcriptCount,
            checkpoints = mission.Checkpoints.Count,
            completed_tasks = completed,
            blocked_tasks = blocked,
            recent_notes = notes,
            active_task = active
        };

        return "CUMULATIVE MISSION LEDGER (canonical data; use {\"op\":\"recall\",\"query\":\"...\"} or {\"op\":\"recall\",\"index\":N} to read full archived turns):\n"
            + JsonSerializer.Serialize(ledger);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? "";
        return value[..max] + "...";
    }
}
