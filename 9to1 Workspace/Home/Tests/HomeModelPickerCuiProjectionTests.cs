using System.Collections;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerCuiProjectionTests
{
    [Fact]
    public void Surface_publishes_boolean_empty_states_and_refuses_partial_binding_matches()
    {
        var surface = Surface();
        Assert.False(Value<bool>(surface, "HasRoutes"));
        Assert.False(Value<bool>(surface, "HasPreview"));
        Assert.False(Value<bool>(surface, "HasTrace"));
        Assert.False(surface.TryGetValue("Routes", out _));
        Assert.False(surface.TryGetValue("SelectedRouteId", out _));
        var notifications = 0;
        surface.PropertyChanged += (_, _) =>
        {
            Assert.True(Value<bool>(surface, "HasRoutes"));
            Assert.Equal("chat.active", Value<string>(surface, "State.SelectedRouteId"));
            notifications++;
        };

        surface.ApplyState(State([Route("chat.active")]));

        Assert.Equal(1, notifications);
        Assert.Equal("global", Value<string>(surface, "Scope"));
        Assert.Equal("chat", Value<string>(surface, "Category"));
        Assert.False(Value<bool>(surface.Projection.Routes.Single(), "HasCandidates"));
    }

    [Fact]
    public void Candidate_view_keys_preserve_provider_model_and_revision_across_order_and_enabled_edits()
    {
        HomeModelRouteCandidate[] candidates =
        [
            new("provider:a", "model", "rev", true, 0),
            new("provider", "a:model", "rev", true, 1),
            new("provider:a", "model", "rev-2", true, 2),
            new("provider:a", "model", null, true, 3),
        ];
        var surface = Surface();
        surface.ApplyState(State([Route("chat.active", candidates)]));
        var before = surface.Projection.Routes.Single().Candidates;
        Assert.Equal(candidates.Length, before.Select(row => row.Identity).Distinct(StringComparer.Ordinal).Count());

        var edited = candidates.Select(candidate => candidate with { Order = 3 - candidate.Order, Enabled = false }).ToArray();
        surface.ApplyState(State([Route("chat.active", candidates)], Route("chat.active", edited)));
        var after = surface.Projection.Routes.Single().Candidates;

        Assert.Equal(before.Select(row => row.Identity).Reverse(), after.Select(row => row.Identity));
        Assert.Equal(new[] { 0, 1, 2, 3 }, after.Select(row => Value<int>(row, "Order")));
        Assert.All(after, row => Assert.False(Value<bool>(row, "Enabled")));
        Assert.True(after[0].TryGetValue("ArtifactRevision", out var revision));
        Assert.Null(revision);
    }

    [Fact]
    public void Selected_route_renders_the_unsaved_draft_without_mutating_the_snapshot_or_other_routes()
    {
        var original = new HomeModelRouteCandidate("provider", "model", "rev", true, 0);
        var active = Route("chat.active", original);
        var background = Route("background", original);
        var draft = active with { Candidates = [original with { Enabled = false }] };
        var surface = Surface();

        surface.ApplyState(State([active, background], draft));

        Assert.False(Value<bool>(surface.Projection.Routes[0].Candidates.Single(), "Enabled"));
        Assert.True(Value<bool>(surface.Projection.Routes[1].Candidates.Single(), "Enabled"));
        Assert.True(surface.State.HasUnsavedChanges);
        Assert.True(surface.State.Snapshot!.Routes[0].Candidates.Single().Enabled);
        Assert.Equal("background", Value<string>(surface.Projection.Routes[1], "RouteId"));
    }

    [Fact]
    public void Preview_retains_duplicate_trace_lines_with_distinct_keys_and_explicit_empty_states()
    {
        var surface = Surface();
        var state = State([Route("chat.active")]);
        var trace = new[] { "same diagnostic", "same diagnostic", "  preserved text  " };
        surface.ApplyState(state with { Preview = new("chat.active", 4, null, "No eligible candidate", trace) });

        Assert.True(Value<bool>(surface, "HasPreview"));
        Assert.True(Value<bool>(surface, "HasTrace"));
        Assert.Equal(trace, surface.Projection.Trace.Select(row => row.Text));
        Assert.Equal(trace.Length, surface.Projection.Trace.Select(row => row.Identity).Distinct(StringComparer.Ordinal).Count());
        surface.ApplyState(state with { Preview = new("chat.active", 4, null, "No candidates", []) });
        Assert.True(Value<bool>(surface, "HasPreview"));
        Assert.False(Value<bool>(surface, "HasTrace"));
        surface.ApplyState(state);
        Assert.False(Value<bool>(surface, "HasPreview"));
        Assert.Empty(surface.Projection.Trace);
    }

    [Fact]
    public void Every_authored_binding_resolves_against_actual_surface_and_nested_projection_rows()
    {
        var surface = HomeModelPickerCuiSurface.LoadDefault();
        var state = State([Route("chat.active",
            new("provider", "model", null, true, 0),
            new("other", "model", "revision", false, 1))]);
        surface.ApplyState(state with { Preview = new("chat.active", 4, null, "Unavailable", ["same", "same"]) });
        surface.ApplyCatalogue(new HomeModelCataloguePage(
            [new("provider", "catalogue-model", null, "Catalogue model", "Provider", true,
                new HashSet<string>(["Chat"], StringComparer.Ordinal), 32768, "Ready", "Local", null)], 0, 1, false, 3),
            "Authorised catalogue loaded.");

        Walk(surface.Document.Components, surface);
        Assert.True(Value<bool>(surface, "HasCatalogue"));
        Assert.Equal("Authorised catalogue loaded.", Value<string>(surface, "CatalogueStatus"));
        surface.ApplyState(State([]));
        Walk(surface.Document.Components, surface);
    }

    private static void Walk(IEnumerable<CuiComponent> components, ICuiBindingContext context)
    {
        foreach (var component in components)
        {
            Assert.Null(component.List);
            foreach (var value in component.Properties.Values) Resolve(value, context);
            foreach (var action in component.Actions.Values)
                Resolve(CuiRichParser.ParseValue(action.Name, action.Span), context);
            if (component.Condition is { } condition)
                Assert.IsType<bool>(Resolve(condition.Test, context));
            if (component.Repeat is { } repeat)
            {
                var items = Assert.IsAssignableFrom<IEnumerable>(Resolve(repeat.Source, context));
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in items)
                {
                    var scope = new RowScope(context, repeat.ItemName, Assert.IsAssignableFrom<ICuiBindingContext>(item));
                    var key = Assert.IsType<string>(Resolve(repeat.Key, scope));
                    Assert.True(keys.Add(key), $"Duplicate view key: {key}");
                    Walk(component.Children, scope);
                }
            }
            else Walk(component.Children, context);
            Walk(component.ElseChildren, context);
        }
    }

    private static object? Resolve(CuiValue value, ICuiBindingContext context)
    {
        if (value is not CuiBindingValue binding) return null;
        Assert.True(context.TryGetValue(binding.Path, out var result), $"Unprovided authored binding: {binding.Path}");
        return result;
    }

    private static T Value<T>(ICuiBindingContext context, string path)
    {
        Assert.True(context.TryGetValue(path, out var value), $"Missing projection value: {path}");
        return Assert.IsAssignableFrom<T>(value);
    }

    [Fact]
    public void Snapshot_availability_distinguishes_unknown_failed_loaded_empty_and_loaded_routes()
    {
        var surface = HomeModelPickerCuiSurface.LoadDefault();

        surface.ApplyState(HomeModelPickerEditorState.Initial);
        Assert.False(Value<bool>(surface, "HasSnapshot"));
        Assert.False(Value<bool>(surface, "HasRoutes"));
        Assert.Equal("NotLoaded", surface.State.StatusCode);
        Assert.Equal("Model routes have not been loaded.", Value<string>(surface, "StatusMessage"));

        var failed = HomeModelPickerEditorState.Initial with
        {
            StatusCode = "HomeServiceUnavailable",
            StatusMessage = "Could not load model routes (HttpRequestException).",
        };
        surface.ApplyState(failed);
        Assert.False(Value<bool>(surface, "HasSnapshot"));
        Assert.False(Value<bool>(surface, "HasRoutes"));
        Assert.Equal("HomeServiceUnavailable", surface.State.StatusCode);
        Assert.Equal("Could not load model routes (HttpRequestException).", Value<string>(surface, "StatusMessage"));

        surface.ApplyState(State([]));
        Assert.True(Value<bool>(surface, "HasSnapshot"));
        Assert.False(Value<bool>(surface, "HasRoutes"));
        Assert.Equal("Ready", surface.State.StatusCode);

        surface.ApplyState(State([Route("chat.active")]));
        Assert.True(Value<bool>(surface, "HasSnapshot"));
        Assert.True(Value<bool>(surface, "HasRoutes"));
        Assert.Single(surface.Projection.Routes);
    }

    [Fact]
    public void Authored_resource_bindings_resolve_for_no_snapshot_and_loaded_empty_states()
    {
        var surface = HomeModelPickerCuiSurface.LoadDefault();

        surface.ApplyState(HomeModelPickerEditorState.Initial);
        Walk(surface.Document.Components, surface);

        surface.ApplyState(HomeModelPickerEditorState.Initial with
        {
            StatusCode = "HomeServiceUnavailable",
            StatusMessage = "Could not load model routes (HttpRequestException).",
        });
        Walk(surface.Document.Components, surface);

        surface.ApplyState(State([]));
        Walk(surface.Document.Components, surface);
    }

    [Fact]
    public void Canonical_named_action_parameters_resolve_to_typed_selection_records()
    {
        var surface = HomeModelPickerCuiSurface.LoadDefault();
        surface.ApplyState(State([Route("chat.active",
            new("provider", "model", null, true, 0),
            new("other", "model", "revision", false, 1))]));

        var scope = Value<HomeModelPickerScopeSelection>(surface, "RefreshSelection");
        Assert.Equal("global", scope.Scope);
        Assert.Equal("chat", scope.Category);

        var current = Value<HomeModelPickerRouteSelection>(surface, "CurrentRouteSelection");
        Assert.Equal("chat.active", current.RouteId);

        var routeRow = surface.Projection.Routes.Single();
        var routeSelection = Value<HomeModelPickerRouteSelection>(routeRow, "Selection");
        Assert.Equal("chat.active", routeSelection.RouteId);

        var first = routeRow.Candidates[0];
        var move = Value<HomeModelPickerCandidateSelection>(first, "Selection");
        Assert.Equal("provider", move.ProviderId);
        Assert.Equal("model", move.ModelId);
        Assert.Null(move.ArtifactRevision);
        var enable = Value<HomeModelPickerCandidateToggleSelection>(first, "EnableSelection");
        Assert.True(enable.Enabled);
        var disable = Value<HomeModelPickerCandidateToggleSelection>(first, "DisableSelection");
        Assert.False(disable.Enabled);

        var second = routeRow.Candidates[1];
        var moveSecond = Value<HomeModelPickerCandidateSelection>(second, "Selection");
        Assert.Equal("revision", moveSecond.ArtifactRevision);
    }

    [Fact]
    public void Effective_candidate_projects_real_advertised_choices_and_selected_label_only()
    {
        var surface = Surface();
        var route = Route("chat.active",
            new("provider", "model", null, true, 0),   // effective
            new("other", "model", null, true, 1));     // non-effective
        surface.ApplyState(State([route], null));
        surface.ApplyEffectiveReasoning(["low", "medium", "high"], "provider", "model", null, "medium");

        var rows = surface.Projection.Routes.Single().Candidates;
        var eff = rows[0];
        Assert.True(Value<bool>(eff, "HasAdvertisedReasoningLevels"));
        Assert.Equal("70%", Value<string>(eff, "SelectedReasoningLabel"));
        var choices = Assert.IsAssignableFrom<IReadOnlyList<HomeModelPickerReasoningChoice>>(Value<object>(eff, "ReasoningLevelChoices"));
        Assert.Equal(3, choices.Count);
        Assert.Equal("35%", choices[0].Label);
        Assert.Equal("70%", choices[1].Label);
        Assert.Equal("100%", choices[2].Label);
        Assert.False(choices[0].IsSelected);
        Assert.True(choices[1].IsSelected); // selected live label corresponds to the effective medium default
        Assert.False(choices[2].IsSelected);

        var other = rows[1];
        Assert.False(Value<bool>(other, "HasAdvertisedReasoningLevels"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<HomeModelPickerReasoningChoice>>(Value<object>(other, "ReasoningLevelChoices")));
    }

    [Fact]
    public void Unknown_advertised_scale_projects_no_invented_choices()
    {
        var surface = Surface();
        var route = Route("chat.active", new HomeModelRouteCandidate("provider", "model", null, true, 0));
        surface.ApplyState(State([route], null));
        surface.ApplyEffectiveReasoning(null, "provider", "model", null, null); // truthful unknown

        var row = surface.Projection.Routes.Single().Candidates.Single();
        Assert.False(Value<bool>(row, "HasAdvertisedReasoningLevels"));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<HomeModelPickerReasoningChoice>>(Value<object>(row, "ReasoningLevelChoices")));
        Assert.True(row.TryGetValue("SelectedReasoningLabel", out var label));
        Assert.Null(label);
    }

    [Fact]
    public void HasPendingApproval_is_strict_boolean_across_empty_loaded_and_pending_states()
    {
        // Canonical CUI <If> conditions require a strict bool. The authored panel binds
        // HasPendingApproval (never the nullable request-id string, which would throw CUIR011).
        var surface = Surface();
        surface.ApplyState(HomeModelPickerEditorState.Initial);
        Assert.False(Value<bool>(surface, "HasPendingApproval"));
        surface.ApplyState(State([]));
        Assert.False(Value<bool>(surface, "HasPendingApproval"));
        surface.ApplyState(State([Route("chat.active")]));
        Assert.False(Value<bool>(surface, "HasPendingApproval"));

        // A retained review surfaces a strict boolean and keeps the exact request id for display.
        var loaded = HomeModelPickerCuiSurface.LoadDefault();
        loaded.ApplyState(State([Route("chat.active")]));
        var pending = new HomeModelPickerCuiProjection(loaded.State, loaded.Scope, loaded.Category,
            null, null, null, null, null, "req-1");
        Assert.True(pending.HasPendingApproval);
        Assert.True(Value<bool>(pending, "HasPendingApproval"));
        Assert.Equal("req-1", Value<string>(pending, "PendingApprovalRequestId"));
        // The authored picker document (including the <If condition="{Binding HasPendingApproval}">) walks
        // cleanly with a strict bool condition; no CUIR011-style non-bool condition is published.
        Walk(loaded.Document.Components, pending);
    }

    private static HomeModelPickerCuiSurface Surface() =>
        new(new CuiRichParser().Parse("<Cui><Page /></Cui>"));

    private static HomeModelRouteContract Route(string id, params HomeModelRouteCandidate[] candidates) =>
        new(id, 4, "global", "chat", null, null, candidates, JsonSerializer.SerializeToElement(new { }));

    private static HomeModelPickerEditorState State(IReadOnlyList<HomeModelRouteContract> routes,
        HomeModelRouteContract? draft = null) =>
        new(false, "Ready", "Model routes are ready.", new(7, "global", "chat", routes),
            routes.FirstOrDefault()?.RouteId, draft ?? routes.FirstOrDefault(), draft is not null, null);

    private sealed class RowScope(ICuiBindingContext parent, string name, ICuiBindingContext row) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value) => path.StartsWith(name + ".", StringComparison.Ordinal)
            ? row.TryGetValue(path[(name.Length + 1)..], out value)
            : parent.TryGetValue(path, out value);
    }
}
