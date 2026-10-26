using System.Text.Json;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop.Dashboard;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

internal sealed record DashboardEditOperation(
    string Action,
    string? Key = null,
    int? Column = null,
    int? Row = null,
    int? Width = null,
    int? Height = null,
    string? Title = null);

internal sealed record DashboardEditPlan(
    string Summary,
    IReadOnlyList<DashboardEditOperation> Operations);

internal sealed record DashboardEditPlanResult(
    bool Succeeded,
    string Message,
    DashboardEditPlan? Plan = null);

internal sealed class DashboardEditPlanner(IProviderModelClient models, HomeModelSelectionSource selection)
{
    private const int MaxOperations = 16;
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "show", "hide", "move", "resize", "reset-layout", "rename-page"
    };
    private long _selectionGeneration;

    public async Task<DashboardEditPlanResult> PlanAsync(
        string instruction,
        string pageTitle,
        IReadOnlyList<DashboardWidgetViewState> widgets,
        IReadOnlyList<DashboardWidgetPlacement> layout,
        CancellationToken cancellationToken)
    {
        instruction = instruction.Trim();
        if (instruction.Length == 0)
            return Fail("Describe the dashboard change you want.");
        if (instruction.Length > 1200)
            return Fail("Dashboard edit instructions must be 1,200 characters or fewer.");

        try
        {
            var before = await selection.ResolveAsync(Interlocked.Increment(ref _selectionGeneration), cancellationToken)
                .ConfigureAwait(false);
            if (before?.Actor is null || string.IsNullOrWhiteSpace(before.ProviderId) || string.IsNullOrWhiteSpace(before.ModelId))
                return Fail("No available model is selected by Home's Chat or Active route. Check model discovery and route configuration in Home.");

            var widgetContext = string.Join('\n', widgets.Select(view =>
            {
                var placement = layout.FirstOrDefault(item => item.Key.Equals(view.Definition.Key, StringComparison.OrdinalIgnoreCase));
                var position = placement is null
                    ? "not placed"
                    : $"column={placement.Column}, row={placement.Row}, width={placement.Width}, height={placement.Height}, visible={placement.IsVisible}";
                return $"- key={view.Definition.Key}; title={view.Definition.Title}; {position}";
            }));

            var prompt = string.Join(Environment.NewLine,
            [
                $"Current dashboard page: {pageTitle}",
                $"Grid columns: 0 through {DashboardWidgetLayoutEngine.Columns - 1}",
                "Widgets:",
                widgetContext,
                string.Empty,
                "User request:",
                instruction,
                string.Empty,
                "Return exactly one JSON object with this shape:",
                "{\"summary\":\"short description\",\"operations\":[...]}",
                string.Empty,
                "Allowed operations only:",
                "- {\"action\":\"show\",\"key\":\"widget-key\"}",
                "- {\"action\":\"hide\",\"key\":\"widget-key\"}",
                "- {\"action\":\"move\",\"key\":\"widget-key\",\"column\":0,\"row\":0}",
                "- {\"action\":\"resize\",\"key\":\"widget-key\",\"width\":3,\"height\":2}",
                "- {\"action\":\"reset-layout\"}",
                "- {\"action\":\"rename-page\",\"title\":\"New page title\"}",
                string.Empty,
                "Use only listed widget keys. Never invent data, widgets, actions, code, URLs or navigation."
            ]);
            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    before.ProviderId + ":" + before.ModelId,
                    [new OllamaMessage("user", prompt)],
                    EffortLevel.Low,
                    "You are 9to1's dashboard layout planner. Output strict JSON only. You may only use the explicitly allowed dashboard operations.",
                    Options: new GenerationOptions(0.2, 4096, 0)
                    {
                        RequestedContextConstraints = new ModelRequestContextConstraints(AllowPersistentMemoryRead: false),
                        RequestedRoutingConstraints = new ModelRequestRoutingConstraints(AllowFallback: false)
                    })
                {
                    ReasoningId = before.SelectedReasoningId,
                    ReasoningTargetProviderId = before.ProviderId,
                    ReasoningTargetModelName = before.ModelId,
                },
                cancellationToken).ConfigureAwait(false);

            var after = await selection.ResolveAsync(Interlocked.Increment(ref _selectionGeneration), cancellationToken)
                .ConfigureAwait(false);
            if (!SameSelection(before, after))
                return Fail("The selected Home model route changed while the dashboard edit was being planned. Try again.");

            return ParseAndValidate(response, widgets);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return Fail($"Edit with 9to1 could not create a safe plan: {ex.Message}");
        }
    }

    internal static DashboardEditPlanResult ParseAndValidate(
        string response,
        IReadOnlyList<DashboardWidgetViewState> widgets)
    {
        if (string.IsNullOrWhiteSpace(response)) return Fail("9to1 returned an empty dashboard plan.");
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) return Fail("9to1 did not return a valid dashboard plan.");

        PlannerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PlannerPayload>(response[start..(end + 1)], new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return Fail("9to1 returned malformed dashboard-plan JSON.");
        }

        if (payload?.Operations is null || payload.Operations.Count == 0)
            return Fail("9to1 did not propose any dashboard changes.");
        if (payload.Operations.Count > MaxOperations)
            return Fail($"9to1 proposed too many changes at once. The limit is {MaxOperations}.");

        var knownKeys = widgets.Select(item => item.Definition.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var operations = new List<DashboardEditOperation>(payload.Operations.Count);
        foreach (var item in payload.Operations)
        {
            var action = item.Action?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action) || !AllowedActions.Contains(action))
                return Fail("9to1 proposed an unsupported dashboard operation.");

            var key = item.Key?.Trim();
            if (action is "show" or "hide" or "move" or "resize")
            {
                if (string.IsNullOrWhiteSpace(key) || !knownKeys.Contains(key))
                    return Fail("9to1 referenced a widget that is not on this dashboard.");
            }

            if (action == "move")
            {
                if (item.Column is null or < 0 or >= DashboardWidgetLayoutEngine.Columns || item.Row is null or < 0 or > 200)
                    return Fail("9to1 proposed an invalid widget position.");
            }
            if (action == "resize")
            {
                if (item.Width is null or < 1 or > DashboardWidgetLayoutEngine.Columns || item.Height is null or < 1 or > 6)
                    return Fail("9to1 proposed an invalid widget size.");
            }
            var validatedTitle = item.Title;
            if (action == "rename-page")
            {
                validatedTitle = item.Title?.Trim();
                if (string.IsNullOrWhiteSpace(validatedTitle) || validatedTitle.Length > 60)
                    return Fail("9to1 proposed an invalid dashboard page title.");
            }

            operations.Add(new DashboardEditOperation(action, key, item.Column, item.Row, item.Width, item.Height, validatedTitle));
        }

        var summary = payload.Summary?.Trim();
        if (string.IsNullOrWhiteSpace(summary)) summary = $"Apply {operations.Count} dashboard change{(operations.Count == 1 ? string.Empty : "s")}";
        if (summary.Length > 180) summary = summary[..180];
        return new DashboardEditPlanResult(true, summary, new DashboardEditPlan(summary, operations));
    }

    private static DashboardEditPlanResult Fail(string message) => new(false, message);

    private static bool SameSelection(HavenModelProjectionSnapshot before, HavenModelProjectionSnapshot? after) =>
        after?.Actor is not null && Equals(before.Actor, after.Actor) &&
        StringComparer.Ordinal.Equals(before.AuthenticationRevision, after.AuthenticationRevision) &&
        StringComparer.Ordinal.Equals(before.ScopeName, after.ScopeName) &&
        StringComparer.Ordinal.Equals(before.ScopeId, after.ScopeId) &&
        StringComparer.Ordinal.Equals(before.RouteId, after.RouteId) && before.RouteRevision == after.RouteRevision &&
        StringComparer.Ordinal.Equals(before.ProviderId, after.ProviderId) &&
        StringComparer.Ordinal.Equals(before.ModelId, after.ModelId) &&
        StringComparer.Ordinal.Equals(before.SelectedReasoningId, after.SelectedReasoningId);

    private sealed record PlannerPayload(string? Summary, List<OperationPayload>? Operations);
    private sealed record OperationPayload(
        string? Action,
        string? Key,
        int? Column,
        int? Row,
        int? Width,
        int? Height,
        string? Title);
}
