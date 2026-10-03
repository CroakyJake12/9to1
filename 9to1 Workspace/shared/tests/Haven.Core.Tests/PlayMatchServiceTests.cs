using System.Text.Json;
using Haven.Application;
using Haven.Application.Play;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlayMatchServiceTests
{
    [Fact]
    public async Task NaturalLanguageMathsPromptCreatesPinnedInspectableGameAndTenSessionOpponents()
    {
        var settings = new MemorySettingsStore();
        var service = new PlayMatchService(settings);
        var generated = await service.GenerateGameAsync("Make a 15-question A-Level Maths quiz where I compete against 10 AI contestants", CancellationToken.None);

        Assert.True(generated.Succeeded, generated.Error?.Message);
        var game = Assert.IsType<PlayGameDefinition>(generated.Value);
        Assert.Equal(15, game.Questions.Count);
        Assert.Equal(11, game.MaximumContestants);
        Assert.Equal(PlayTimingModel.Simultaneous, game.Timing);
        Assert.True(game.Scoring.Deterministic);
        Assert.All(game.Capabilities, capability => Assert.Equal(PlayCapabilityAccess.Denied, capability.Access));

        var matchResult = await service.StartMatchAsync(game.GameDefinitionId, new PlayMatchConfiguration(SessionAgentCount: 10), CancellationToken.None);
        Assert.True(matchResult.Succeeded, matchResult.Error?.Message);
        var match = Assert.IsType<PlayMatchSnapshot>(matchResult.Value);
        Assert.Equal(game.Revision, match.GameRevision);
        Assert.Equal(11, match.Contestants.Count);
        Assert.All(match.Contestants.Where(item => item.Kind == PlayContestantKind.SessionAgent), item =>
        {
            Assert.Null(item.AgentId);
            Assert.NotNull(item.SessionProfile);
        });

        var publicRead = await service.GetMatchAsync(match.MatchId, CancellationToken.None);
        Assert.True(publicRead.Succeeded);
        Assert.All(publicRead.Value!.PinnedGame.Questions, question => Assert.Equal(-1, question.CorrectOption));
        Assert.Empty(publicRead.Value.MatchState.Submissions);
        Assert.All(publicRead.Value.PinnedGame.Questions.Skip(1), question =>
        {
            Assert.Empty(question.Prompt);
            Assert.Empty(question.Options);
        });
        var futureRound = await service.GetRoundAsync(match.MatchId, 1, match.Contestants[0].ContestantId, CancellationToken.None);
        Assert.False(futureRound.Succeeded);
        Assert.Equal("ContestantViewDenied", futureRound.Error!.Code);
        var contestantView = await service.GetContestantViewAsync(match.MatchId, match.Contestants[0].ContestantId, CancellationToken.None);
        Assert.True(contestantView.Succeeded);
        Assert.NotNull(contestantView.Value!.Prompt);
        Assert.Equal(game.Questions[0].Options, contestantView.Value.Options);
        Assert.DoesNotContain("CorrectOption", JsonSerializer.Serialize(contestantView.Value));
    }

    [Fact]
    public async Task PersistentAgentReferenceIsPreservedAndIsNotClonedIntoSessionProfile()
    {
        var service = new PlayMatchService(new MemorySettingsStore());
        var game = await CreateGameAsync(service, minimum: 2, maximum: 2);
        var agentId = Guid.NewGuid();
        var started = await service.StartMatchAsync(game.GameDefinitionId,
            new PlayMatchConfiguration(ManualContestants: [new(PlayContestantKind.DulcheAgent, "Canonical Agent", agentId, 4)]), CancellationToken.None);

        Assert.True(started.Succeeded, started.Error?.Message);
        var agent = Assert.Single(started.Value!.Contestants, item => item.Kind == PlayContestantKind.DulcheAgent);
        Assert.Equal(agentId, agent.AgentId);
        Assert.Equal(4, agent.AgentDefinitionRevision);
        Assert.Null(agent.SessionProfile);
    }

    [Fact]
    public async Task SimultaneousAnswersStaySealedUntilAllSubmitThenRevealDeterministicScores()
    {
        var service = new PlayMatchService(new MemorySettingsStore());
        var game = await CreateGameAsync(service, minimum: 2, maximum: 2);
        var started = await service.StartMatchAsync(game.GameDefinitionId,
            new PlayMatchConfiguration(ManualContestants: [new(PlayContestantKind.SessionAgent, "Opponent")]), CancellationToken.None);
        Assert.True(started.Succeeded, started.Error?.Message);
        var match = started.Value!;
        var human = Assert.Single(match.Contestants, item => item.Kind == PlayContestantKind.Human);
        var opponent = Assert.Single(match.Contestants, item => item.Kind == PlayContestantKind.SessionAgent);

        var first = await service.SubmitAnswerAsync(match.MatchId, human.ContestantId, 1, CancellationToken.None);
        Assert.True(first.Succeeded, first.Error?.Message);
        Assert.Equal(PlayRoundPhase.AwaitingSubmissions, first.Value!.MatchState.Phase);
        var hiddenView = await service.GetContestantViewAsync(match.MatchId, opponent.ContestantId, CancellationToken.None);
        Assert.True(hiddenView.Succeeded);
        Assert.False(hiddenView.Value!.HasSubmitted);
        var publicMatch = await service.GetMatchAsync(match.MatchId, CancellationToken.None);
        Assert.Empty(publicMatch.Value!.MatchState.Submissions);

        var earlyResolve = await service.ResolveRoundAsync(match.MatchId, CancellationToken.None);
        Assert.False(earlyResolve.Succeeded);
        Assert.Equal("ResolutionFailed", earlyResolve.Error!.Code);
        var stillSealed = await service.GetMatchAsync(match.MatchId, CancellationToken.None);
        Assert.Equal(PlayRoundPhase.AwaitingSubmissions, stillSealed.Value!.MatchState.Phase);

        var second = await service.SubmitAnswerAsync(match.MatchId, opponent.ContestantId, 1, CancellationToken.None);
        Assert.True(second.Succeeded, second.Error?.Message);
        var reveal = await service.ResolveRoundAsync(match.MatchId, CancellationToken.None);
        Assert.True(reveal.Succeeded, reveal.Error?.Message);
        Assert.Single(reveal.Value!.MatchState.Reveals);
        Assert.All(reveal.Value.MatchState.Reveals[0].CorrectByContestant.Values, Assert.True);
        Assert.All(reveal.Value.Contestants, item => Assert.Equal(1, item.Score));
    }

    [Fact]
    public async Task IllegalSubmissionDoesNotMutateMatchAndCheckpointReplaySurviveReload()
    {
        var settings = new MemorySettingsStore();
        var service = new PlayMatchService(settings);
        var game = await CreateGameAsync(service, minimum: 1, maximum: 1);
        var started = await service.StartMatchAsync(game.GameDefinitionId, new PlayMatchConfiguration(SessionAgentCount: 0), CancellationToken.None);
        Assert.True(started.Succeeded);
        var match = started.Value!;
        var human = Assert.Single(match.Contestants);

        var invalid = await service.SubmitAnswerAsync(match.MatchId, human.ContestantId, 99, CancellationToken.None);
        Assert.False(invalid.Succeeded);
        Assert.Equal("IllegalAction", invalid.Error!.Code);
        var unchanged = await service.GetMatchAsync(match.MatchId, CancellationToken.None);
        Assert.Equal(PlayRoundPhase.Active, unchanged.Value!.MatchState.Phase);
        Assert.Empty(unchanged.Value.MatchState.Submissions);

        var submitted = await service.SubmitAnswerAsync(match.MatchId, human.ContestantId, game.Questions[0].CorrectOption, CancellationToken.None);
        Assert.True(submitted.Succeeded);
        var resolved = await service.ResolveRoundAsync(match.MatchId, CancellationToken.None);
        Assert.True(resolved.Succeeded);
        var checkpoint = await service.CreateCheckpointAsync(match.MatchId, CancellationToken.None);
        Assert.True(checkpoint.Succeeded);
        Assert.Empty(checkpoint.Value!.Snapshot.MatchState.Submissions);

        var reloaded = new PlayMatchService(settings);
        var recovered = await reloaded.GetMatchAsync(match.MatchId, CancellationToken.None);
        Assert.True(recovered.Succeeded);
        Assert.Equal(PlayMatchStatus.Completed, recovered.Value!.Status);
        Assert.Single(recovered.Value.MatchState.Reveals);
        var replay = await reloaded.GetReplayAsync(match.MatchId, CancellationToken.None);
        Assert.True(replay.Succeeded);
        Assert.Contains(replay.Value!, item => item.EventType == "MatchStarted");
        Assert.Contains(replay.Value!, item => item.EventType == "SubmissionLocked");
        Assert.Contains(replay.Value!, item => item.EventType == "RoundRevealed");
    }

    [Fact]
    public async Task TeamPrivateStateIsVisibleOnlyToTeamAndSpectatorViewDoesNotExposeIt()
    {
        var service = new PlayMatchService(new MemorySettingsStore());
        var game = await CreateGameAsync(service, minimum: 2, maximum: 2, allowsTeams: true);
        var homeTeam = Guid.NewGuid();
        var otherTeam = Guid.NewGuid();
        var started = await service.StartMatchAsync(game.GameDefinitionId,
            new PlayMatchConfiguration(SessionAgentCount: 1, TeamIds: [homeTeam, otherTeam]), CancellationToken.None);
        Assert.True(started.Succeeded);
        var match = started.Value!;
        var home = Assert.Single(match.Contestants, item => item.TeamId == homeTeam);
        var opponent = Assert.Single(match.Contestants, item => item.TeamId == otherTeam);
        var saved = await service.SetTeamPrivateStateAsync(match.MatchId, home.ContestantId, "private clue", CancellationToken.None);
        Assert.True(saved.Succeeded);

        var homeView = await service.GetContestantViewAsync(match.MatchId, home.ContestantId, CancellationToken.None);
        var opposingView = await service.GetContestantViewAsync(match.MatchId, opponent.ContestantId, CancellationToken.None);
        var spectatorView = await service.GetSpectatorViewAsync(match.MatchId, CancellationToken.None);
        Assert.Equal("private clue", homeView.Value!.TeamPrivateState);
        Assert.Null(opposingView.Value!.TeamPrivateState);
        Assert.Null(spectatorView.Value!.TeamPrivateState);
    }

    [Fact]
    public async Task RevisionConflictAndInvalidPaginationReturnStructuredErrors()
    {
        var service = new PlayMatchService(new MemorySettingsStore());
        var game = await CreateGameAsync(service, minimum: 1, maximum: 1);
        var updated = await service.UpdateGameAsync(game.GameDefinitionId, game with { Title = "Updated" }, game.Revision, CancellationToken.None);
        Assert.True(updated.Succeeded);
        var conflict = await service.UpdateGameAsync(game.GameDefinitionId, game, game.Revision, CancellationToken.None);
        Assert.False(conflict.Succeeded);
        Assert.Equal("RevisionConflict", conflict.Error!.Code);
        var invalidPage = await service.ListMatchesAsync(-1, 500, CancellationToken.None);
        Assert.False(invalidPage.Succeeded);
        Assert.Equal("InvalidPage", invalidPage.Error!.Code);
    }

    [Fact]
    public async Task Caller_mutable_definition_and_returned_authoring_game_cannot_change_pinned_rules_or_scoring()
    {
        var settings = new MemorySettingsStore(); var service = new PlayMatchService(settings);
        var options = new List<string> { "3", "4", "5" };
        var questions = new List<PlayQuestionDefinition> { new("What is 2 + 2?", options, 1, "Four") };
        var capabilities = new List<PlayCapabilityRule> { new("calculator", PlayCapabilityAccess.Denied) };
        var now = DateTimeOffset.UtcNow;
        var definition = new PlayGameDefinition(Guid.NewGuid(), 1, "Pinned quiz", "Quiz", 1, 1, false, true,
            PlayTimingModel.Simultaneous, new("deterministic.maths.v1", true, false, false, "Exact"),
            capabilities, questions, now, now);
        var created = (await service.CreateGameAsync(definition, default)).Value!;
        var match = (await service.StartMatchAsync(created.GameDefinitionId, new(), default)).Value!;
        // All three lists were supplied by the caller; none can now rewrite canonical rules.
        options[1] = "999"; questions[0] = questions[0] with { CorrectOption = 0 };
        capabilities[0] = capabilities[0] with { Access = PlayCapabilityAccess.Allowed };
        if (created.Questions is IList<PlayQuestionDefinition> returnedQuestions)
            returnedQuestions[0] = returnedQuestions[0] with { CorrectOption = 2, Prompt = "Changed via result" };
        if (created.Capabilities is IList<PlayCapabilityRule> returnedCapabilities)
            returnedCapabilities[0] = returnedCapabilities[0] with { Access = PlayCapabilityAccess.Allowed };
        var contestant = match.Contestants.Single().ContestantId;
        var view = (await service.GetContestantViewAsync(match.MatchId, contestant, default)).Value!;
        Assert.Equal("What is 2 + 2?", view.Prompt); Assert.Equal("4", view.Options[1]);
        Assert.False((await service.RequestCapabilityAsync(match.MatchId, contestant, "calculator", default)).Value);
        Assert.True((await service.SubmitAnswerAsync(match.MatchId, contestant, 1, default)).Succeeded);
        Assert.True((await service.ResolveRoundAsync(match.MatchId, default)).Succeeded);
        Assert.True((await service.GetLeaderboardAsync(match.MatchId, default)).Value![contestant] > 0);
        var reopened = new PlayMatchService(settings);
        Assert.Equal("4", (await reopened.GetMatchAsync(match.MatchId, default)).Value!.PinnedGame.Questions[0].Options[1]);
        Assert.Equal((await service.GetLeaderboardAsync(match.MatchId, default)).Value![contestant],
            (await reopened.GetLeaderboardAsync(match.MatchId, default)).Value![contestant]);
    }

    [Fact]
    public async Task Contestant_spectator_and_match_views_cannot_mutate_canonical_question_or_each_other()
    {
        var service = new PlayMatchService(new MemorySettingsStore());
        var game = await CreateGameAsync(service, 2, 2);
        var match = (await service.StartMatchAsync(game.GameDefinitionId, new(SessionAgentCount: 1), default)).Value!;
        var first = match.Contestants[0].ContestantId; var second = match.Contestants[1].ContestantId;
        var view = (await service.GetContestantViewAsync(match.MatchId, first, default)).Value!;
        if (view.Options is IList<string> options) options[1] = "Contestant injected";
        var spectator = (await service.GetSpectatorViewAsync(match.MatchId, default)).Value!;
        Assert.Equal("4", spectator.Options[1]);
        if (spectator.Options is IList<string> spectatorOptions) spectatorOptions[1] = "Spectator injected";
        var read = (await service.GetMatchAsync(match.MatchId, default)).Value!;
        if (read.PinnedGame.Questions[0].Options is IList<string> matchOptions) matchOptions[1] = "Match result injected";
        var other = (await service.GetContestantViewAsync(match.MatchId, second, default)).Value!;
        Assert.Equal("4", other.Options[1]);
        Assert.False(other.HasSubmitted);
        Assert.DoesNotContain("CorrectOption", JsonSerializer.Serialize(other));
        Assert.Empty((await service.GetMatchAsync(match.MatchId, default)).Value!.MatchState.Submissions);
        Assert.Equal("4", (await service.GetGameAsync(game.GameDefinitionId, null, default)).Value!.Questions[0].Options[1]);
    }

    [Fact]
    public async Task Definition_revision_update_and_later_input_mutation_leave_running_match_and_new_revision_pinned()
    {
        var settings = new MemorySettingsStore(); var service = new PlayMatchService(settings);
        var game = await CreateGameAsync(service, 1, 1);
        var existing = (await service.StartMatchAsync(game.GameDefinitionId, new(), default)).Value!;
        var options = new List<string> { "8", "9" };
        var questions = new List<PlayQuestionDefinition> { new("Three squared?", options, 1, "Nine") };
        var updatedInput = game with { Questions = questions };
        var updated = (await service.UpdateGameAsync(updatedInput, game.Revision, default)).Value!;
        options[1] = "External rewrite";
        questions[0] = questions[0] with { CorrectOption = 0, Prompt = "External rewrite" };
        var next = (await service.StartMatchAsync(game.GameDefinitionId, new(), default)).Value!;
        Assert.Equal(game.Revision, existing.GameRevision); Assert.Equal(updated.Revision, next.GameRevision);
        Assert.Equal("What is 2 + 2?", (await service.GetContestantViewAsync(existing.MatchId, existing.Contestants[0].ContestantId, default)).Value!.Prompt);
        var nextView = (await service.GetContestantViewAsync(next.MatchId, next.Contestants[0].ContestantId, default)).Value!;
        Assert.Equal("Three squared?", nextView.Prompt); Assert.Equal("9", nextView.Options[1]);
        Assert.True((await service.SubmitAnswerAsync(next.MatchId, next.Contestants[0].ContestantId, 1, default)).Succeeded);
        Assert.True((await service.ResolveRoundAsync(next.MatchId, default)).Succeeded);
        Assert.True((await service.GetLeaderboardAsync(next.MatchId, default)).Value![next.Contestants[0].ContestantId] > 0);
        Assert.Equal("9", (await new PlayMatchService(settings).GetMatchAsync(next.MatchId, default)).Value!.PinnedGame.Questions[0].Options[1]);
    }

    [Fact]
    public async Task Round_view_mutation_cannot_rewrite_other_contestant_context_or_persisted_canonical_scoring()
    {
        var settings = new MemorySettingsStore(); var service = new PlayMatchService(settings);
        var game = await CreateGameAsync(service, 2, 2);
        var match = (await service.StartMatchAsync(game.GameDefinitionId, new(SessionAgentCount: 1), default)).Value!;
        var first = match.Contestants[0].ContestantId; var second = match.Contestants[1].ContestantId;
        var round = (await service.GetRoundAsync(match.MatchId, null, first, default)).Value!;
        Assert.IsAssignableFrom<IList<string>>(round.Options)[1] = "Forged round content";
        Assert.Equal("4", (await service.GetRoundAsync(match.MatchId, null, second, default)).Value!.Options[1]);
        Assert.Equal("4", (await service.GetSpectatorViewAsync(match.MatchId, default)).Value!.Options[1]);
        Assert.Equal("4", (await service.GetContestantViewAsync(match.MatchId, second, default)).Value!.Options[1]);
        Assert.True((await service.SubmitAnswerAsync(match.MatchId, first, 1, default)).Succeeded);
        Assert.False((await service.ResolveRoundAsync(match.MatchId, default)).Succeeded);
        Assert.True((await service.SubmitAnswerAsync(match.MatchId, second, 1, default)).Succeeded);
        Assert.True((await service.ResolveRoundAsync(match.MatchId, default)).Succeeded);
        var reopened = new PlayMatchService(settings);
        var scores = (await reopened.GetLeaderboardAsync(match.MatchId, default)).Value!;
        Assert.True(scores[first] > 0); Assert.Equal(scores[first], scores[second]);
        Assert.Equal("4", (await reopened.GetMatchAsync(match.MatchId, default)).Value!.PinnedGame.Questions[0].Options[1]);
    }

    private static async Task<PlayGameDefinition> CreateGameAsync(PlayMatchService service, int minimum, int maximum, bool allowsTeams = false)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = new PlayGameDefinition(Guid.NewGuid(), 1, "Test maths quiz", "Quiz", minimum, maximum, allowsTeams, true,
            PlayTimingModel.Simultaneous, new PlayScoringPolicy("deterministic.maths.v1", true, false, false, "Exact answer checking."),
            [new("web.search", PlayCapabilityAccess.Denied), new("code.execution", PlayCapabilityAccess.Denied), new("calculator", PlayCapabilityAccess.Denied)],
            [new("What is 2 + 2?", ["3", "4", "5"], 1, "2 + 2 = 4.", "Arithmetic")], now, now);
        var result = await service.CreateGameAsync(definition, CancellationToken.None);
        Assert.True(result.Succeeded, result.Error?.Message);
        return Assert.IsType<PlayGameDefinition>(result.Value);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        }
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsExportManifest { Settings = new Dictionary<string, string>(_values) });
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            foreach (var item in manifest.Settings) _values[item.Key] = item.Value;
            return Task.FromResult(new SettingsImportResult(true, new Dictionary<string, string>(_values), "Imported"));
        }
    }
}
