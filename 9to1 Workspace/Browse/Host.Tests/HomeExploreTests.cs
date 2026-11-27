using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using Haven.Infrastructure;

namespace HavenOS.Browse.Host.Tests;

public sealed class HomeExploreTests
{
    private const string WriteIdentity = "mode:a0000000-0000-0000-0000-000000000017";
    private const string OriginalBrief = "Make a game about a lost island and a document explaining its story.";
    private static ModeDefinition WriteMode => BuiltInModeSeed.Modes.Single(mode => mode.Key == "write");
    private static IReadOnlyList<HomeAppItem> Catalogue => HomeAppHubCatalogue.Catalogue([
        new("write", "Write", "Documents", "write", CanonicalId: WriteMode.Id, Source: ModeSource.BuiltIn)]);
    private static string TwoChoiceJson => JsonSerializer.Serialize(new
    {
        message = "These are two distinct deliverables.",
        choices = new[]
        {
            new { appId = "reference:games", deliverable = "The island game", reason = "Games owns interactive projects." },
            new { appId = WriteIdentity, deliverable = "The story document", reason = "Write owns documents." }
        }
    });

    [Fact]
    public async Task Planner_uses_the_selected_provider_named_stream_and_exact_reasoning_without_tools_or_fallback()
    {
        var selected = new Provider("selected-provider", TwoChoiceJson);
        var unused = new Provider("catalogue-first", "{}");
        var local = new Client(["{}"]);
        var bridge = new ProviderRoutingModelClient(local, new ModelProviderRegistry([unused, selected]), new Privacy());
        var planner = new HomeExplorePlanner(bridge, Selection);

        var result = await planner.PlanAsync([new("user", OriginalBrief)], Catalogue, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Plan!.Choices.Count);
        Assert.Equal("reference:games", result.Plan.Choices[0].AppIdentity);
        Assert.False(Catalogue.Single(app => app.StableIdentity == "reference:games").IsAvailable);
        var request = Assert.IsType<OllamaChatRequest>(selected.Request);
        Assert.Equal("selected-model:512k", request.Model);
        Assert.Equal("high", selected.ReasoningId);
        Assert.Equal("high", request.ReasoningId);
        Assert.Equal("selected-provider", request.ReasoningTargetProviderId);
        Assert.Equal("selected-model:512k", request.ReasoningTargetModelName);
        Assert.False(request.EnableTools);
        Assert.Equal(0, request.Options!.ActionLimit);
        Assert.Equal(false, request.Options.RequestedRoutingConstraints!.AllowFallback);
        Assert.Equal(false, request.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.Equal(OriginalBrief, Assert.Single(request.Messages).Content);
        Assert.Equal(1, selected.NamedStreams); Assert.Equal(0, selected.OrdinaryStreams);
        Assert.Equal(0, selected.Completions); Assert.Equal(0, selected.Tools);
        Assert.Equal(0, unused.NamedStreams + unused.OrdinaryStreams + unused.Completions + unused.Tools);
        Assert.Equal(0, local.Streams + local.Completions + local.Tools + local.CatalogueReads);
    }

    [Fact]
    public async Task Missing_selection_never_discovers_or_substitutes_an_arbitrary_model()
    {
        var client = new Client([TwoChoiceJson]);
        var planner = new HomeExplorePlanner(client, (_, _) => Task.FromResult<HavenModelProjectionSnapshot?>(null));
        var result = await planner.PlanAsync([new("user", OriginalBrief)], Catalogue, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Plan);
        Assert.Contains("Choose", result.Message);
        Assert.Equal(0, client.Streams + client.Completions + client.Tools + client.CatalogueReads);
    }

    [Fact]
    public async Task Selected_provider_failure_is_reported_without_trying_another_provider()
    {
        var selected = new Provider("selected-provider", TwoChoiceJson) { Failure = new HttpRequestException("Selected provider is offline.") };
        var alternate = new Provider("ollama", TwoChoiceJson);
        var local = new Client([TwoChoiceJson]);
        var bridge = new ProviderRoutingModelClient(local, new ModelProviderRegistry([alternate, selected]), new Privacy());
        var result = await new HomeExplorePlanner(bridge, Selection).PlanAsync([new("user", OriginalBrief)], Catalogue, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Plan);
        Assert.Contains("Selected provider is offline", result.Message);
        Assert.Equal(1, selected.NamedStreams);
        Assert.Equal(0, alternate.NamedStreams + alternate.OrdinaryStreams + alternate.Completions + alternate.Tools);
        Assert.Equal(0, local.Streams + local.Completions + local.Tools + local.CatalogueReads);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("route")]
    [InlineData("actor")]
    [InlineData("authentication")]
    [InlineData("reasoning")]
    public async Task A_selection_change_during_generation_rejects_the_response(string change)
    {
        var reads = 0;
        var before = Snapshot();
        var after = change switch
        {
            "model" => before with { ModelId = "other-model" },
            "route" => before with { RouteRevision = before.RouteRevision + 1 },
            "actor" => before with { Actor = before.Actor! with { ProfileId = "other-profile" } },
            "authentication" => before with { AuthenticationRevision = "auth-2" },
            _ => before with { SelectedReasoningId = "low" }
        };
        var client = new Client([TwoChoiceJson]);
        var planner = new HomeExplorePlanner(client, (_, _) => Task.FromResult<HavenModelProjectionSnapshot?>(++reads == 1 ? before : after));
        var result = await planner.PlanAsync([new("user", OriginalBrief)], Catalogue, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Null(result.Plan);
        Assert.Contains("changed", result.Message); Assert.Equal(1, client.Streams);
        Assert.Equal("selected-provider:selected-model:512k", client.Request!.Model);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[],\"route\":\"9to1://write/private\"}")]
    [InlineData("{\"message\":\"First\",\"message\":\"Second\",\"choices\":[]}")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[{\"appId\":\"unknown\",\"deliverable\":\"A game\",\"reason\":\"A game owner\"}]}")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[{\"appId\":\"reference:games\",\"deliverable\":\"A game\",\"reason\":\"A game owner\"},{\"appId\":\"reference:games\",\"deliverable\":\"A game\",\"reason\":\"Duplicate\"}]}")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[{\"appId\":\"reference:games\",\"appId\":\"reference:games\",\"deliverable\":\"A game\",\"reason\":\"Duplicated field\"}]}")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[{\"appId\":\"reference:games\",\"deliverable\":\"A game\",\"reason\":\"A game owner\",\"command\":\"run\"}]}")]
    [InlineData("{\"message\":\"Ready\",\"choices\":[{\"appId\":\"reference:games\",\"deliverable\":42,\"reason\":\"Wrong type\"}]}")]
    [InlineData("{\"message\":\" \" ,\"choices\":[]}")]
    [InlineData("```json\n{\"message\":\"Ready\",\"choices\":[]}\n```")]
    public void Parser_rejects_unknown_duplicate_executable_and_malformed_response_shapes(string response)
    {
        var result = HomeExplorePlanner.Parse(response, Catalogue);
        Assert.False(result.Succeeded); Assert.Null(result.Plan);
    }

    [Fact]
    public void Parser_preserves_multiple_outputs_for_the_same_app_and_allows_a_clarifying_question()
    {
        var response = JsonSerializer.Serialize(new
        {
            message = "Write owns both documents.",
            choices = new[]
            {
                new { appId = WriteIdentity, deliverable = "Story outline", reason = "A document" },
                new { appId = WriteIdentity, deliverable = "Player guide", reason = "A separate document" }
            }
        });
        var result = HomeExplorePlanner.Parse(response, Catalogue);
        Assert.True(result.Succeeded, result.Message); Assert.Equal(2, result.Plan!.Choices.Count);
        var question = HomeExplorePlanner.Parse("{\"message\":\"Do you want to build a game or describe one?\",\"choices\":[]}", Catalogue);
        Assert.True(question.Succeeded); Assert.Empty(question.Plan!.Choices);
        var tooMany = JsonSerializer.Serialize(new
        {
            message = "Too many outputs",
            choices = Enumerable.Range(0, 7).Select(i => new { appId = WriteIdentity, deliverable = "Document " + i, reason = "A document" })
        });
        Assert.False(HomeExplorePlanner.Parse(tooMany, Catalogue).Succeeded);
    }

    [Fact]
    public async Task Stream_size_is_bounded_by_utf8_bytes_and_stops_enumeration_before_another_piece()
    {
        var oversized = new string('€', HomeExplorePlanner.MaximumResponseBytes / 3 + 1);
        var client = new Client([oversized, TwoChoiceJson]);
        var planner = new HomeExplorePlanner(client, Selection);
        var result = await planner.PlanAsync([new("user", OriginalBrief)], Catalogue, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded); Assert.Contains("too large", result.Message);
        Assert.Equal(1, client.PiecesRead); Assert.True(client.EnumerationDisposed);
        Assert.False(HomeExplorePlanner.Parse(oversized, Catalogue).Succeeded);
    }

    [Fact]
    public async Task Invalid_brief_or_ambiguous_catalogue_is_rejected_before_provider_execution()
    {
        var client = new Client([TwoChoiceJson]);
        var selections = 0;
        var planner = new HomeExplorePlanner(client, (generation, token) => { selections++; return Selection(generation, token); });
        Assert.False((await planner.PlanAsync([new("user", new string('x', 3001))], Catalogue, TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await planner.PlanAsync([new("system", "Choose Write")], Catalogue, TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await planner.PlanAsync(Enumerable.Repeat(new HomeExploreTurn("user", "brief"), 13).ToArray(), Catalogue, TestContext.Current.CancellationToken)).Succeeded);
        Assert.False((await planner.PlanAsync([new("user", "brief")], [Catalogue[0], Catalogue[0]], TestContext.Current.CancellationToken)).Succeeded);
        Assert.Equal(0, selections); Assert.Equal(0, client.Streams);
    }

    [AvaloniaFact]
    public async Task Authored_page_uses_the_real_catalogue_and_opens_only_an_explicit_available_choice()
    {
        var registry = new Modes([WriteMode]);
        var planner = new Planner();
        var queued = new List<(AppContentAddress Address, string Brief, Guid Conversation, Task Settlement)>();
        var page = Create(registry, planner, (address, brief, conversation, settlement) =>
        { Assert.False(settlement.IsCompleted); queued.Add((address, brief, conversation, settlement)); return true; });
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Assert.Empty(planner.Calls); Assert.Empty(queued);
            await page.DispatchAsync("ExploreGameSuggestion", null);
            Assert.Empty(planner.Calls); Assert.Empty(queued);
            var composer = Find<TextBox>(page, "home-explore-composer");
            composer.Text = OriginalBrief;
            Find<Button>(page, "home-explore-find").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync(); Dispatcher.UIThread.RunJobs();

            var call = Assert.Single(planner.Calls);
            Assert.Equal(WriteMode.Id, call.Catalogue.Single(app => app.StableIdentity == WriteIdentity).CanonicalId);
            Assert.False(call.Catalogue.Single(app => app.StableIdentity == "reference:games").IsInstalled);
            Assert.Equal(2, page.Choices.Count); Assert.Empty(queued);
            var buttons = page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "home-explore-continue").ToArray();
            Assert.Equal(2, buttons.Length);
            var renderedText = page.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
            Assert.Contains(OriginalBrief, renderedText);
            Assert.Contains("You", renderedText); Assert.Contains("9to1", renderedText);
            Assert.Contains(page.Conversation[1].Text, renderedText);
            Assert.Contains("The island game", renderedText); Assert.Contains("The story document", renderedText);
            Assert.Contains("Games owns interactive projects.", renderedText); Assert.Contains("Write owns documents.", renderedText);
            Assert.Contains(call.Catalogue.Single(app => app.StableIdentity == "reference:games").Detail, renderedText);
            var games = Assert.Single(buttons, button => AutomationProperties.GetName(button)!.Contains("Games", StringComparison.Ordinal));
            var write = Assert.Single(buttons, button => AutomationProperties.GetName(button)!.Contains("Write", StringComparison.Ordinal));
            Assert.False(games.IsEnabled); Assert.True(write.IsEnabled);
            write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync();
            var navigation = Assert.Single(queued); await navigation.Settlement;
            Assert.Equal("9to1://write", navigation.Address.Value);
            Assert.Empty(navigation.Address.ResourceSegments);
            Assert.Equal(OriginalBrief, navigation.Brief); Assert.True(page.OwnsBrief(navigation.Conversation));
            Assert.Single(planner.Calls); Assert.Equal(0, registry.Mutations);
            Assert.Equal(2, page.Conversation.Count);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("key")]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("duplicate-key")]
    public async Task Continue_rechecks_the_actual_catalogue_and_refuses_a_changed_destination(string change)
    {
        var registry = new Modes([WriteMode]);
        var available = true; var queued = 0;
        var page = Create(registry, new Planner(), (_, _, _, _) => { queued++; return true; }, available: _ => available);
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Find<TextBox>(page, "home-explore-composer").Text = OriginalBrief;
            await page.DispatchAsync("ExploreFind", null);
            var target = page.Choices.Single(choice => choice.Choice.AppIdentity == WriteIdentity);
            switch (change)
            {
                case "removed": registry.Current = []; break;
                case "disabled": registry.Current = [WriteMode with { IsEnabled = false }]; break;
                case "key": registry.Current = [WriteMode with { Key = "other-owner" }]; break;
                case "source": registry.Current = [WriteMode with { Source = ModeSource.Created }]; break;
                case "owner": available = false; break;
                default: registry.Current = [WriteMode, WriteMode with { Id = Guid.NewGuid(), Source = ModeSource.Created }]; break;
            }
            await page.DispatchAsync("ExploreContinue", target);
            Assert.Equal(0, queued); Assert.Empty(page.Choices);
            Assert.Equal(OriginalBrief, page.OriginalBrief);
            Assert.Contains("changed or became unavailable", Find<TextBlock>(page, "home-explore-status").Text);
            Assert.Equal(0, registry.Mutations);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Refinement_and_a_failed_request_keep_the_original_brief_conversation_and_current_choices()
    {
        var planner = new Planner();
        var page = Create(new Modes([WriteMode]), planner);
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var composer = Find<TextBox>(page, "home-explore-composer");
            composer.Text = OriginalBrief; await page.DispatchAsync("ExploreFind", null);
            composer.Text = "The document should be a player guide."; await page.DispatchAsync("ExploreFind", null);
            Assert.Equal(3, planner.Calls[1].Conversation.Count);
            Assert.Equal(OriginalBrief, planner.Calls[1].Conversation[0].Text);
            Assert.Equal("The document should be a player guide.", planner.Calls[1].Conversation[^1].Text);
            Assert.Equal(OriginalBrief, page.OriginalBrief);
            var conversation = page.Conversation.ToArray(); var choices = page.Choices.ToArray();
            planner.Respond = (_, _, _) => Task.FromResult(new HomeExplorePlanResult(false, "Selected model is unavailable. Your brief is kept."));
            composer.Text = "Keep the game suitable for younger players.";
            await page.DispatchAsync("ExploreFind", null);
            Assert.Equal("Keep the game suitable for younger players.", composer.Text);
            Assert.Equal(conversation, page.Conversation); Assert.Equal(choices, page.Choices);
            Assert.Equal(OriginalBrief, page.OriginalBrief);
            Assert.Contains("unavailable", Find<TextBlock>(page, "home-explore-status").Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Held_admission_claims_the_action_once_and_preserves_a_newer_draft()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = false;
        var planner = new Planner();
        var page = Create(new Modes([WriteMode]), planner, admit: _ => hold ? gate.Task : Task.CompletedTask);
        var window = Show(page);
        Task? action = null;
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var composer = Find<TextBox>(page, "home-explore-composer");
            composer.Text = OriginalBrief; hold = true;
            action = page.DispatchAsync("ExploreFind", null).AsTask();
            Assert.False(action.IsCompleted);
            Assert.Equal(false, page.IsActionAvailable("ExploreFind"));
            Assert.Equal(false, page.IsActionAvailable("ExploreNewBrief"));
            Assert.Throws<InvalidOperationException>(() => { _ = page.DispatchAsync("ExploreFind", null); });
            composer.Text = "A new draft written after the first click.";
            gate.SetResult(); await action;
            Assert.Equal(OriginalBrief, Assert.Single(Assert.Single(planner.Calls).Conversation).Text);
            Assert.Equal(OriginalBrief, page.OriginalBrief);
            Assert.Equal("A new draft written after the first click.", composer.Text);
        }
        finally { gate.TrySetResult(); if (action is not null) await action; await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Preflight_joins_held_inference_and_cancels_only_the_waiter()
    {
        var response = new TaskCompletionSource<HomeExplorePlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var planner = new Planner { Respond = (_, _, _) => response.Task };
        var page = Create(new Modes([WriteMode]), planner);
        var window = Show(page);
        using var waiter = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task? action = null;
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Find<TextBox>(page, "home-explore-composer").Text = OriginalBrief;
            action = page.DispatchAsync("ExploreFind", null).AsTask();
            var call = Assert.Single(planner.Calls);
            var first = page.TryPrepareToCloseAsync(waiter.Token);
            Assert.False(first.IsCompleted); waiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.False(call.Token.IsCancellationRequested); Assert.True(page.IsAlive);
            Assert.False(action.IsCompleted);
            var second = page.TryPrepareToCloseAsync(TestContext.Current.CancellationToken); Assert.False(second.IsCompleted);
            response.SetResult(Plan()); await action;
            Assert.True(await second); Assert.Equal(OriginalBrief, page.OriginalBrief);
        }
        finally { response.TrySetResult(Plan()); if (action is not null) await action; await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Revoked_admission_is_a_genuine_preflight_failure_and_retains_the_draft()
    {
        var allowed = true;
        var planner = new Planner();
        var page = Create(new Modes([WriteMode]), planner, admit: _ => allowed ? Task.CompletedTask :
            Task.FromException(new UnauthorizedAccessException("The active profile was revoked.")));
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var composer = Find<TextBox>(page, "home-explore-composer"); composer.Text = OriginalBrief;
            allowed = false;
            await Assert.ThrowsAnyAsync<Exception>(() => page.DispatchAsync("ExploreFind", null).AsTask());
            Assert.Empty(planner.Calls); Assert.Equal(OriginalBrief, composer.Text);
            await Assert.ThrowsAnyAsync<Exception>(() => page.TryPrepareToCloseAsync(TestContext.Current.CancellationToken));
            Assert.True(page.IsAlive); Assert.NotNull(page.Content);
        }
        finally { await Assert.ThrowsAnyAsync<Exception>(() => page.CloseAndDrainAsync()); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Deactivation_prevents_late_inference_from_publishing_a_conversation_or_choice()
    {
        var response = new TaskCompletionSource<HomeExplorePlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var planner = new Planner { Respond = (_, _, _) => response.Task };
        var page = Create(new Modes([WriteMode]), planner);
        var window = Show(page);
        Task? action = null;
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Find<TextBox>(page, "home-explore-composer").Text = OriginalBrief;
            action = page.DispatchAsync("ExploreFind", null).AsTask();
            Assert.Single(planner.Calls); page.Deactivate();
            response.SetResult(Plan()); await action;
            Assert.Empty(page.Conversation); Assert.Empty(page.Choices); Assert.Equal(string.Empty, page.OriginalBrief);
            Assert.Equal(OriginalBrief, Find<TextBox>(page, "home-explore-composer").Text);
        }
        finally { response.TrySetResult(Plan()); if (action is not null) await action; await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task New_brief_retires_the_old_conversation_identity_without_creating_or_navigating_anything()
    {
        var queued = new List<Guid>();
        var planner = new Planner();
        var page = Create(new Modes([WriteMode]), planner, (_, _, id, _) => { queued.Add(id); return false; });
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            Find<TextBox>(page, "home-explore-composer").Text = OriginalBrief;
            await page.DispatchAsync("ExploreFind", null);
            await page.DispatchAsync("ExploreContinue", page.Choices.Single(choice => choice.Choice.AppIdentity == WriteIdentity));
            var oldId = Assert.Single(queued); Assert.True(page.OwnsBrief(oldId));
            Assert.Contains("busy", Find<TextBlock>(page, "home-explore-status").Text);
            Assert.Equal(2, page.Choices.Count); Assert.Equal(OriginalBrief, page.OriginalBrief);
            await page.DispatchAsync("ExploreNewBrief", null);
            Assert.False(page.OwnsBrief(oldId)); Assert.Empty(page.Conversation); Assert.Empty(page.Choices);
            Assert.Equal(string.Empty, page.OriginalBrief); Assert.Equal(string.Empty, Find<TextBox>(page, "home-explore-composer").Text);
            Assert.Single(planner.Calls); Assert.Single(queued);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Glow")]
    [InlineData("Professional")]
    public async Task Authored_explore_choices_render_in_both_approved_themes_at_720_and_1440(string name)
    {
        using var theme = new CuiThemePreferenceContext(new ThemeSource(name));
        var page = Create(new Modes([WriteMode]), new Planner(), theme: theme);
        var window = Show(page);
        try
        {
            await page.ActivateAsync(TestContext.Current.CancellationToken);
            var composer = Find<TextBox>(page, "home-explore-composer");
            Assert.Equal("What would you like to make?", Find<TextBlock>(page, "home-explore-heading").Text);
            composer.Text = OriginalBrief;
            await page.DispatchAsync("ExploreFind", null);
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
            {
                window.Width = size.Width; window.Height = size.Height; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal("Explore", Find<TextBlock>(page, "home-explore-heading").Text);
                Assert.Same(composer, Find<TextBox>(page, "home-explore-composer"));
                composer.Text = "Keep this refinement ready while I compare the choices.";
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var footer = Find<Control>(page, "home-explore-footer");
                var footerPosition = footer.TranslatePoint(default, page);
                Assert.NotNull(footerPosition);
                Assert.InRange(footer.Bounds.Width, 1, 820.1);
                AssertFullyWithin(composer, page);
                AssertFullyWithin(Find<Button>(page, "home-explore-find"), page);
                AssertFullyWithin(Find<Button>(page, "home-explore-new"), page);
                foreach (var control in page.GetVisualDescendants().OfType<Control>().Where(control =>
                    control.Name is "home-explore-composer" or "home-explore-find" or "home-explore-new" or "home-explore-continue"))
                {
                    var point = control.TranslatePoint(default, page); Assert.NotNull(point);
                    Assert.True(control.Bounds.Width > 0);
                    Assert.True(point.Value.X >= -1 && point.Value.X + control.Bounds.Width <= page.Bounds.Width + 1,
                        control.Name + " exceeded the viewport at " + size.Width);
                }
                var choices = page.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "home-explore-continue").ToArray();
                Assert.Equal(2, choices.Length);
                var scroll = Assert.Single(choices[^1].GetVisualAncestors().OfType<ScrollViewer>());
                Assert.True(scroll.Viewport.Height > 0, "The real conversation presenter must have a finite viewport.");
                if (size.Width == 720) Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                choices[^1].BringIntoView();
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AssertFullyWithin(choices[^1], page);
                var lastChoicePosition = choices[^1].TranslatePoint(default, page);
                Assert.NotNull(lastChoicePosition);
                Assert.True(lastChoicePosition.Value.Y + choices[^1].Bounds.Height <= footerPosition.Value.Y + 1,
                    "The final Continue action must remain above the fixed composer.");
                Assert.Equal(footerPosition, footer.TranslatePoint(default, page));
                Assert.Equal("Keep this refinement ready while I compare the choices.", composer.Text);
                AssertFullyWithin(composer, page);
                AssertFullyWithin(Find<Button>(page, "home-explore-find"), page);
                scroll.Offset = Vector.Zero;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var capture = window.CaptureRenderedFrame(); Assert.NotNull(capture);
                    capture.Save(Path.Combine(directory, $"home-explore-{name}-{size.Width:0}x{size.Height:0}.png"));
                }
            }
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    private static Task<HavenModelProjectionSnapshot?> Selection(long generation, CancellationToken token) =>
        Task.FromResult<HavenModelProjectionSnapshot?>(Snapshot() with { Generation = generation });
    private static HavenModelProjectionSnapshot Snapshot() => new(
        new("explore-actor", "explore-profile", null, null, "auth-1"), "auth-1", "User", "explore-profile",
        "explore-chat", 4, 1, "selected-provider", "selected-model:512k", true, true, ["low", "high"], "high");
    private static HomeExplorePlanResult Plan() => HomeExplorePlanner.Parse(TwoChoiceJson, Catalogue);
    private static HomeExploreDestinationPage Create(Modes modes, Planner planner,
        Func<AppContentAddress, string, Guid, Task, bool>? queue = null,
        Func<CancellationToken, Task>? admit = null, Func<ModeDefinition, bool>? available = null,
        CuiThemePreferenceContext? theme = null) => new(modes, planner, admit ?? (_ => Task.CompletedTask),
            available ?? (_ => true), queue ?? ((_, _, _, _) => throw new InvalidOperationException("This test did not authorize navigation.")), theme);
    private static Window Show(Control page)
    { var window = new Window { Content = page, Width = 1200, Height = 900 }; window.Show(); return window; }
    private static T Find<T>(Control page, string id) where T : Control =>
        Assert.Single(page.GetVisualDescendants().OfType<T>(), control => control.Name == id);

    private static void AssertFullyWithin(Control control, Control page)
    {
        var point = control.TranslatePoint(default, page);
        Assert.NotNull(point);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.True(point.Value.X >= -1 && point.Value.X + control.Bounds.Width <= page.Bounds.Width + 1 &&
                    point.Value.Y >= -1 && point.Value.Y + control.Bounds.Height <= page.Bounds.Height + 1,
            control.Name + " must remain reachable inside the viewport: " + point + ", " + control.Bounds);
    }

    private sealed class Planner : IHomeExplorePlanner
    {
        internal sealed record Call(IReadOnlyList<HomeExploreTurn> Conversation, IReadOnlyList<HomeAppItem> Catalogue, CancellationToken Token);
        internal List<Call> Calls { get; } = [];
        internal Func<IReadOnlyList<HomeExploreTurn>, IReadOnlyList<HomeAppItem>, CancellationToken, Task<HomeExplorePlanResult>> Respond =
            (_, _, _) => Task.FromResult(Plan());
        public Task<HomeExplorePlanResult> PlanAsync(IReadOnlyList<HomeExploreTurn> conversation, IReadOnlyList<HomeAppItem> catalogue, CancellationToken token)
        { Calls.Add(new(conversation.ToArray(), catalogue.ToArray(), token)); return Respond(conversation, catalogue, token); }
    }

    private sealed class Modes(IReadOnlyList<ModeDefinition> current) : IModeRegistry
    {
        internal IReadOnlyList<ModeDefinition> Current = current;
        internal int Mutations;
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult(Current);
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult(Current.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult(Current.SingleOrDefault(mode => mode.Key == key));
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) { Mutations++; throw new NotSupportedException(); }
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) { Mutations++; throw new NotSupportedException(); }
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) { Mutations++; throw new NotSupportedException(); }
    }

    private sealed class Client(string[] pieces) : IProviderModelClient
    {
        internal OllamaChatRequest? Request;
        internal int Streams, Completions, Tools, CatalogueReads, PiecesRead;
        internal bool EnumerationDisposed;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token)
        { CatalogueReads++; return Task.FromResult<IReadOnlyList<ModelDescriptor>>([]); }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            Request = request; Streams++; await Task.Yield();
            try { foreach (var piece in pieces) { token.ThrowIfCancellationRequested(); PiecesRead++; yield return piece; } }
            finally { EnumerationDisposed = true; }
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { Completions++; throw new InvalidOperationException("Explore must use the selected stream."); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { Tools++; throw new InvalidOperationException("Explore must not dispatch tools."); }
    }

    private sealed class Provider(string id, string response) : INamedReasoningProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        internal OllamaChatRequest? Request;
        internal string? ReasoningId;
        internal Exception? Failure;
        internal int NamedStreams, OrdinaryStreams, Completions, Tools;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "Fixture", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new(id, true,
                new("selected-model:512k", 524288, "fixture", "1B", "Q4", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        { OrdinaryStreams++; await Task.Yield(); yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { Completions++; throw new InvalidOperationException("Explore must use the selected named stream."); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        { Tools++; throw new InvalidOperationException("Explore must not dispatch tools."); }
        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(OllamaChatRequest request, string reasoningId,
            [EnumeratorCancellation] CancellationToken token)
        {
            NamedStreams++; Request = request; ReasoningId = reasoningId; await Task.Yield(); token.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            var half = response.Length / 2; yield return response[..half]; yield return response[half..];
        }
        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId, CancellationToken token)
        { Tools++; throw new InvalidOperationException("Explore must not dispatch tools."); }
    }

    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current => PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class ThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "explore-render-test", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }
}
