using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulcheSwarm
{
    private void ShowRun(JsonNode run)
    {
        _observed = run;
        var state = Str(run, "state");
        var reason = Str(run, "reason");
        var blocked = state is "paused" or "blocked";
        var terminal = state is "stopped" or "completed" or "completed_unverified" or "limit_reached" or "failed";
        Enabled("pause", !terminal && !blocked);
        Enabled("resume", blocked);
        Enabled("review", !terminal);
        Enabled("stop", !terminal);
        Enabled("apply", !terminal);
        var next = run["next_check_in"];
        var remaining = next?["remaining_seconds"] is { } seconds ? Rate(seconds) + " s" : "event-driven";
        Label("progress", $"{Pretty(run, "stage")} · {Pretty(run, "state")} · mentor {Pretty(run, "mentor_state")} · {Str(run, "strategy")} residency\n" +
            $"{Str(run, "active_workers")} active workers · {Str(run, "queued_workers")} queued · {Str(run, "completed_tasks")} tasks complete · {Str(run, "pending_tasks")} pending · {Str(run, "verified_tasks")} verified\n" +
            $"Check-ins {Str(run, "check_ins")} · next timer {remaining}, task threshold {Str(next, "tasks")}, token threshold {Str(next, "tokens")}" + (reason.Length > 0 ? "\n" + reason : ""));
        var config = run["config"];
        var output = config?["limits"]?["max_output_tokens"]?.GetValue<int>() ?? 0;
        var mentor = config?["mentor"];
        var mentorSummary = "Mentor · " + (mentor?["model"]?.ToString() ?? Str(run, "mentor_model")) +
            $" · {Str(mentor, "engine")} · {Str(mentor, "gpu")} · {Str(mentor, "profile")} · {Str(mentor, "context")} context · reasoning {mentor?["reasoning"]?.ToString() ?? "default"}";
        var capacity = Rows(config?["workers"]).Select(w =>
        {
            var context = w["context"]?.GetValue<int>() ?? 0;
            return $"{Str(w, "role")} · {Str(w, "model")} · {Str(w, "gpu")} · {context:N0} context, {Math.Max(0, context - output):N0} input after output reserve · reasoning {w["reasoning"]?.ToString() ?? "default"}";
        });
        var resources = run["resources"];
        var devices = Rows(resources?["gpus"]).Select(g => $"{Str(g, "name")} · {Str(g, "used_mib")}/{Str(g, "total_mib")} MiB · {Str(g, "temperature_c")} °C · {Str(g, "utilization")}% utilisation");
        var ram = resources?["host_available_mib"] is { } available ? $"Host RAM · {available}/{Str(resources, "host_total_mib")} MiB available" : "Host RAM telemetry unavailable.";
        var peak = run["resource_summary"];
        var sampled = peak?["gpus"] is JsonObject hardware ? hardware.Select(entry =>
            $"{entry.Key} peak {Str(entry.Value, "peak_used_mib")} MiB, {Str(entry.Value, "peak_temp_c")} °C · idle {Rate(entry.Value?["sampled_idle_seconds"])} of {Rate(entry.Value?["sampled_seconds"])} sampled s") : [];
        Label("resources", string.Join("\n", new[] { mentorSummary }.Concat(capacity).Append(ram).Concat(devices).Concat(sampled)));
        Label("rates", ThroughputText(run));
        var tasks = Rows(run["tasks"]).Select(t => new Choice(Str(t, "id"), $"{Str(t, "id")} · {Str(t, "role")} · {Pretty(t, "state")}", t)).ToArray();
        Choices("tasks", tasks);
        ShowTask();
        var decisions = Rows(run["decisions"]).TakeLast(4).Select(d => Str(d, "summary") is { Length: > 0 } text ? text : d.ToJsonString());
        var errors = Rows(run["errors"]).TakeLast(3).Select(e => e.ToString());
        var notes = Rows(run["human_observations"]).TakeLast(3).Select(o => "Operator observation · " + Str(o, "note"));
        Label("decisions", string.Join("\n", decisions.Concat(notes).Concat(errors)));
    }
    private void ShowTask()
    {
        var task = Selected("tasks")?.Record;
        Enabled("stop-worker", task is not null && Str(task, "state") is not ("stopped" or "done" or "failed"));
        if (task is null)
        {
            Label("task-detail", "No worker tasks yet.");
            return;
        }
        var detail = new[] { "title", "instruction", "acceptance", "state", "reason", "worker_evidence", "verification_evidence", "result" }.Select(k => Str(task, k)).Where(v => v.Length > 0);
        Label("task-detail", string.Join("\n", detail));
    }
    private static string StrategyComparison(JsonNode evidence)
    {
        var trials = Rows(evidence["trials"]).ToArray();
        var lines = new List<string>();
        if (trials.Length == 0)
            lines.Add("No measured strategy trials.");
        foreach (var trial in trials.TakeLast(8))
            lines.Add($"{Str(trial, "strategy")} · {Str(trial, "verified")}/{Str(trial, "tasks")} verified · {Rate(trial["aggregate_tps"])} aggregate accepted tok/s over {Rate(trial["common_wall_s"])} s · {Rate(trial["verified_tasks_h"])} verified tasks/hour");
        var estimates = Rows(evidence["matched_break_even_estimates"]).ToArray();
        if (estimates.Length == 0)
            lines.Add("No matched break-even estimate.");
        foreach (var estimate in estimates.TakeLast(4))
            lines.Add($"Estimated check-in interval {Rate(estimate["estimated_break_even_cycle_seconds"])} s · matched cohort {Str(estimate, "cohort")}");
        if (Str(evidence, "method") is { Length: > 0 } basis && estimates.Length > 0)
            lines.Add(basis);
        if (Str(evidence, "four_thousand_tps") is { Length: > 0 } note)
            lines.Add("4,000 tok/s claim · " + note);
        return string.Join("\n", lines);
    }

    internal static string ThroughputText(JsonNode run)
    {
        var rolling = run["metrics"]?["rolling"];
        var finished = run["metrics"]?["finished"];
        var common = Rate(rolling?["elapsed_s"]);
        var participants = Rows(rolling?["workers"]).ToArray();
        var measured = participants.Length > 0;
        var group = measured ? Rate(rolling?["aggregate_tps"]) : "awaiting workers";
        var completedWork = Rows(finished?["workers"]).Any(w => (w["completed_requests"]?.GetValue<int>() ?? 0) > 0);
        var completedRate = completedWork ? Rate(finished?["aggregate_tps"]) : "awaiting completed requests";
        var lines = new List<string>{$"Group total · {group} accepted output tok/s over {common} s",
            $"Whole run · {completedRate} accepted output tok/s · {Rate(finished?["verified_tasks_h"])} verified tasks/hour"};
        var measuredWorkers = participants.Where(w => Str(w,"role") != "mentor").ToArray();
        if (measuredWorkers.Length > 0 && measuredWorkers.All(w => w["accepted_tokens"] is not null && w["tps"] is not null) &&
            double.TryParse(rolling?["elapsed_s"]?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var duration) && duration > 0)
        {
            var tokens = measuredWorkers.Sum(w => w["accepted_tokens"]!.GetValue<long>());
            lines.Add($"Workers together · {tokens / duration:0.0} accepted output tok/s over the same {common} s, excluding the mentor.");
        }
        foreach (var worker in participants)
        {
            var name = Str(worker, "worker");
            var total = Rows(finished?["workers"]).FirstOrDefault(w => Str(w, "worker") == name);
            var missing = worker["missing_usage"]?.GetValue<int>() > 0;
            var streaming = worker["tps"] is null ? "waiting for measured output or usage" : Rate(worker["tps"]) + " tok/s";
            lines.Add($"{Str(worker, "role")} · {name}: {streaming} in the same window; completed requests {Rate(total?["individual_tps"])} tok/s" + (missing ? " · incomplete usage" : ""));
        }
        var present = participants.Select(w => Str(w, "role")).ToHashSet(StringComparer.Ordinal);
        foreach (var planned in Rows(run["config"]?["workers"]).Where(w => !present.Contains(Str(w, "role"))))
            lines.Add($"{Str(planned, "role")} · no measured worker output; queue or prefill state unreported.");
        lines.Add("Accepted engine output includes any reported reasoning tokens.");
        if (participants.Length == 0)
            lines.Add("Waiting for the first model response; prefill and queued work have no measured decode rate.");
        return string.Join("\n", lines);
    }
}
