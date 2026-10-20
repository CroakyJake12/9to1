using System.Collections;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
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

        Walk(surface.Document.Components, surface);
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
        return Assert.IsType<T>(value);
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
