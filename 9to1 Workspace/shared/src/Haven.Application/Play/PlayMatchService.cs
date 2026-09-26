using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;

namespace Haven.Application.Play;

public enum PlayContestantKind { Human, DulcheAgent, SessionAgent }
public enum PlayMatchStatus { Lobby, Active, Paused, Completed, Cancelled }
public enum PlayRoundPhase { Pending, Active, AwaitingSubmissions, Resolving, Revealed, Completed }
public enum PlayTimingModel { Sequential, Simultaneous, RealTimeLike }
public enum PlayCapabilityAccess { Allowed, Denied, Constrained }
public enum PlayOperationRisk { Low, Elevated, High }

public sealed record PlayCapabilityRule(string CapabilityId, PlayCapabilityAccess Access, string? Constraint = null);
public sealed record PlayQuestionDefinition(string Prompt, IReadOnlyList<string> Options, int CorrectOption, string Explanation, string? Topic = null);
public sealed record PlayScoringPolicy(string PolicyId, bool Deterministic, bool SpeedAffectsScore, bool BlindJudging, string Description);
public sealed record PlayGameDefinition(
    Guid GameDefinitionId,
    int Revision,
    string Title,
    string Category,
    int MinimumContestants,
    int MaximumContestants,
    bool AllowsTeams,
    bool AllowsSpectators,
    PlayTimingModel Timing,
    PlayScoringPolicy Scoring,
    IReadOnlyList<PlayCapabilityRule> Capabilities,
    IReadOnlyList<PlayQuestionDefinition> Questions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? GenerationPrompt = null,
    bool IsFavourite = false);

public sealed record PlaySessionAgentProfile(string Name, string Strategy, string? ModelPolicy = null);
public sealed record PlayContestant(
    Guid ContestantId,
    PlayContestantKind Kind,
    string DisplayName,
    Guid? AgentId,
    int? AgentDefinitionRevision,
    PlaySessionAgentProfile? SessionProfile,
    Guid? TeamId,
    int Score,
    bool IsEligible,
    bool IsHumanParticipant);

public sealed record PlaySubmission(Guid ContestantId, int SelectedOption, DateTimeOffset SubmittedAt, bool IsSealed);
public sealed record PlayQuestionReveal(int QuestionIndex, IReadOnlyDictionary<Guid, bool> CorrectByContestant, DateTimeOffset RevealedAt);
public sealed record PlayRoundSnapshot(Guid MatchId, int RoundIndex, PlayRoundPhase Phase, string? Prompt, IReadOnlyList<string> Options,
    DateTimeOffset? PresentedAt, DateTimeOffset? Deadline, int SealedSubmissionCount, bool IsRevealed);
public sealed record PlayMatchState(
    int RoundIndex,
    PlayRoundPhase Phase,
    DateTimeOffset? PresentedAt,
    DateTimeOffset? Deadline,
    IReadOnlyDictionary<Guid, PlaySubmission> Submissions,
    IReadOnlyDictionary<Guid, string> TeamPrivateState,
    IReadOnlyList<PlayQuestionReveal> Reveals,
    string? CompletionReason = null,
    DateTimeOffset? PausedAt = null,
    TimeSpan? RemainingOnPause = null,
    TimeSpan? SubmissionLimit = null);

public sealed record PlayMatchSnapshot(
    Guid MatchId,
    Guid GameDefinitionId,
    int GameRevision,
    PlayGameDefinition PinnedGame,
    IReadOnlyList<PlayContestant> Contestants,
    PlayMatchState MatchState,
    PlayMatchStatus Status,
    Guid ActionGraphId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool SpectatorOnly,
    long CheckpointRevision);

public sealed record PlayMatchConfiguration(
    int SessionAgentCount = 0,
    bool IncludeHuman = true,
    bool SpectatorOnly = false,
    IReadOnlyList<Guid>? TeamIds = null,
    TimeSpan? SubmissionLimit = null,
    string SessionAgentStrategy = "balanced",
    string? ModelPolicy = null,
    IReadOnlyList<PlayContestantInput>? ManualContestants = null);
public sealed record PlayContestantInput(PlayContestantKind Kind, string DisplayName, Guid? AgentId = null, int? AgentDefinitionRevision = null, Guid? TeamId = null);

public sealed record PlayContestantView(
    Guid MatchId,
    Guid ContestantId,
    string Title,
    int QuestionIndex,
    PlayRoundPhase Phase,
    string? Prompt,
    IReadOnlyList<string> Options,
    bool HasSubmitted,
    DateTimeOffset? Deadline,
    IReadOnlyDictionary<Guid, int> PublicScores,
    string? TeamPrivateState,
    bool IsSpectator);

public sealed record PlayActionDescriptor(string ActionId, string Name, string InputSchema, PlayOperationRisk Risk, bool Reversible, bool ExternalSideEffect);
public sealed record PlayDomainEvent(Guid EventId, Guid MatchId, Guid ActionGraphId, long Sequence, string EventType, Guid? ContestantId, DateTimeOffset OccurredAt, string PublicPayload);
public sealed record PlayCheckpoint(Guid MatchId, long Revision, PlayMatchSnapshot Snapshot, DateTimeOffset CreatedAt);
public sealed record PlayHandoffPayload(Guid MatchId, Guid GameDefinitionId, int GameRevision, string Summary, IReadOnlyDictionary<string, string> PermittedArtifacts);
public sealed record PlayApiError(string Code, string Message, string Target, string Action, bool Recoverable, bool Retryable);
public sealed record PlayApiResult<T>(bool Succeeded, T? Value, PlayApiError? Error)
{
    public static PlayApiResult<T> Success(T value) => new(true, value, null);
    public static PlayApiResult<T> Failure(string code, string message, string target, string action, bool recoverable = false, bool retryable = false) =>
        new(false, default, new PlayApiError(code, message, target, action, recoverable, retryable));
}

public sealed record PlayLibraryV1(int Version, IReadOnlyList<PlayGameDefinition> Games, IReadOnlyList<PlayMatchSnapshot> Matches,
    IReadOnlyList<PlayDomainEvent> Events, IReadOnlyList<PlayCheckpoint> Checkpoints, long NextSequence);

/// <summary>Authoritative, versioned Play game/match state. It contains no model, filesystem, or external-action executor.</summary>
public sealed class PlayMatchService(IVersionedSettingsStore settings)
{
    public const string StateKey = "play.match-library.v1";
    private const string Target = "9to1.Play";
    private const int MaximumGames = 500;
    private const int MaximumMatches = 500;
    private const int MaximumContestants = 64;
    private const int MaximumQuestions = 100;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PlayLibraryV1? _library;

    public async Task<PlayApiResult<IReadOnlyList<PlayGameDefinition>>> ListGamesAsync(CancellationToken cancellationToken)
    {
        var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return loaded.Error is not null ? Fail<IReadOnlyList<PlayGameDefinition>>(loaded.Error) : PlayApiResult<IReadOnlyList<PlayGameDefinition>>.Success(loaded.Library!.Games.GroupBy(game => game.GameDefinitionId).Select(group => group.MaxBy(game => game.Revision)!).OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).Select(PublicGame).ToArray());
    }

    public async Task<PlayApiResult<PlayGameDefinition>> GetGameAsync(Guid id, int? revision, CancellationToken cancellationToken)
    {
        var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Error is not null) return Fail<PlayGameDefinition>(loaded.Error);
        var candidates = loaded.Library!.Games.Where(item => item.GameDefinitionId == id);
        var game = revision is int requested ? candidates.FirstOrDefault(item => item.Revision == requested) : candidates.MaxBy(item => item.Revision);
        return game is null ? Error<PlayGameDefinition>(revision is null ? "GameNotFound" : "GameRevisionUnavailable", "The requested game definition revision is unavailable.", "GetGame", true) : PlayApiResult<PlayGameDefinition>.Success(PublicGame(game));
    }

    public Task<PlayApiResult<PlayGameDefinition>> CreateGameAsync(PlayGameDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return MutateAsync(library =>
        {
            var now = DateTimeOffset.UtcNow;
            var created = definition with { GameDefinitionId = definition.GameDefinitionId == Guid.Empty ? Guid.NewGuid() : definition.GameDefinitionId, Revision = 1, CreatedAt = now, UpdatedAt = now };
            var validation = Validate(created);
            if (validation is not null) return Failed<PlayGameDefinition>("InvalidGameDefinition", validation, "CreateGame");
            if (library.Games.Count >= MaximumGames) return Failed<PlayGameDefinition>("LimitReached", "The saved game limit has been reached.", "CreateGame");
            if (library.Games.Any(game => game.GameDefinitionId == created.GameDefinitionId)) return Failed<PlayGameDefinition>("RevisionConflict", "That game definition ID already exists.", "CreateGame", true);
            return Updated(library with { Games = library.Games.Append(created).ToArray() }, PlayApiResult<PlayGameDefinition>.Success(created));
        }, cancellationToken);
    }

    public Task<PlayApiResult<PlayGameDefinition>> GenerateGameAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return Task.FromResult(Error<PlayGameDefinition>("InvalidGameDefinition", "Describe the game you want to create.", "GenerateGame"));
        if (!prompt.Contains("math", StringComparison.OrdinalIgnoreCase) && !prompt.Contains("quiz", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Error<PlayGameDefinition>("InvalidGameDefinition", "This local generator currently supports structured maths quizzes. No game was created.", "GenerateGame", true));

        var count = ParseBoundedNumber(prompt, @"(?<n>\d{1,3})\s*(?:-question|question|questions|round|rounds)", 15, 1, 100);
        var agents = ParseBoundedNumber(prompt, @"(?<n>\d{1,2})\s*(?:AI\s*)?(?:agents?|opponents?|contestants?)", 10, 0, 63);
        var now = DateTimeOffset.UtcNow;
        var seed = StableHash(prompt);
        var questions = Enumerable.Range(0, count).Select(index => CreateMathQuestion(index, seed)).ToArray();
        var game = new PlayGameDefinition(Guid.NewGuid(), 1, "Generated maths quiz", "Quiz", 1, Math.Clamp(agents + 1, 1, MaximumContestants), false, true,
            PlayTimingModel.Simultaneous, new PlayScoringPolicy("deterministic.maths.v1", true, false, false, "Exact deterministic option validation."),
            [new("web.search", PlayCapabilityAccess.Denied), new("code.execution", PlayCapabilityAccess.Denied), new("calculator", PlayCapabilityAccess.Denied), new("files.read", PlayCapabilityAccess.Denied), new("plugins.invoke", PlayCapabilityAccess.Denied)],
            questions, now, now, prompt.Trim());
        return CreateGameAsync(game, cancellationToken);
    }

    public Task<PlayApiResult<PlayGameDefinition>> UpdateGameAsync(PlayGameDefinition updated, int expectedRevision, CancellationToken cancellationToken) =>
        MutateAsync(library =>
        {
            var current = library.Games.Where(game => game.GameDefinitionId == updated.GameDefinitionId).MaxBy(game => game.Revision);
            if (current is null) return Failed<PlayGameDefinition>("GameNotFound", "The game definition was not found.", "UpdateGame", true);
            if (current.Revision != expectedRevision) return Failed<PlayGameDefinition>("RevisionConflict", "The game definition changed since it was read.", "UpdateGame", true, true);
            var validation = Validate(updated);
            if (validation is not null) return Failed<PlayGameDefinition>("InvalidGameDefinition", validation, "UpdateGame");
            var next = updated with { Revision = current.Revision + 1, CreatedAt = current.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
            if (library.Games.Count >= MaximumGames) return Failed<PlayGameDefinition>("LimitReached", "The saved game revision limit has been reached.", "UpdateGame");
            return Updated(library with { Games = library.Games.Append(next).ToArray() }, PlayApiResult<PlayGameDefinition>.Success(next));
        }, cancellationToken);

    public async Task<PlayApiResult<string>> ValidateGameAsync(Guid id, int? revision, CancellationToken cancellationToken)
    {
        var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Error is not null) return Fail<string>(loaded.Error);
        var candidates = loaded.Library!.Games.Where(item => item.GameDefinitionId == id);
        var game = revision is int requested ? candidates.FirstOrDefault(item => item.Revision == requested) : candidates.MaxBy(item => item.Revision);
        if (game is null) return Error<string>(revision is null ? "GameNotFound" : "GameRevisionUnavailable", "The requested game definition revision is unavailable.", "ValidateGame", true);
        var error = Validate(game);
        return error is null ? PlayApiResult<string>.Success("Valid") : Error<string>("InvalidGameDefinition", error, "ValidateGame");
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> StartMatchAsync(Guid gameId, PlayMatchConfiguration? configuration, CancellationToken cancellationToken) =>
        MutateAsync(library =>
        {
            var game = library.Games.Where(item => item.GameDefinitionId == gameId).MaxBy(item => item.Revision);
            if (game is null) return Failed<PlayMatchSnapshot>("GameNotFound", "The game definition was not found.", "StartMatch", true);
            var config = configuration ?? new PlayMatchConfiguration();
            if (config.SpectatorOnly && !game.AllowsSpectators) return Failed<PlayMatchSnapshot>("InvalidGameDefinition", "This game does not allow spectator matches.", "StartMatch");
            if (config.SessionAgentCount < 0 || config.SessionAgentCount > MaximumContestants - 1) return Failed<PlayMatchSnapshot>("InvalidGameDefinition", "The requested opponent count is outside the supported range.", "StartMatch");
            if (config.SubmissionLimit is TimeSpan limit && (limit <= TimeSpan.Zero || limit > TimeSpan.FromHours(24))) return Failed<PlayMatchSnapshot>("InvalidGameDefinition", "A submission limit must be between one tick and 24 hours.", "StartMatch");
            if (config.SpectatorOnly && (config.IncludeHuman || config.ManualContestants?.Any(item => item.Kind == PlayContestantKind.Human) == true)) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "A spectator-only match cannot include a human contestant.", "StartMatch");
            var contestants = new List<PlayContestant>();
            if (config.IncludeHuman && !config.SpectatorOnly) contestants.Add(new(Guid.NewGuid(), PlayContestantKind.Human, "You", null, null, null, TeamAt(config.TeamIds, 0), 0, true, true));
            foreach (var input in config.ManualContestants ?? [])
            {
                if (input.Kind == PlayContestantKind.DulcheAgent && input.AgentId is null) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "A persistent Agent contestant requires its canonical AgentID.", "StartMatch");
                if (input.Kind != PlayContestantKind.DulcheAgent && input.AgentId is not null) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "Only persistent Agent contestants may reference a canonical AgentID.", "StartMatch");
                if (!game.AllowsTeams && input.TeamId is not null) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "This game does not allow teams.", "StartMatch");
                var name = CleanName(input.DisplayName);
                contestants.Add(new(Guid.NewGuid(), input.Kind, name, input.AgentId,
                    input.Kind == PlayContestantKind.DulcheAgent ? input.AgentDefinitionRevision : null,
                    input.Kind == PlayContestantKind.SessionAgent ? new PlaySessionAgentProfile(name, "balanced", config.ModelPolicy) : null,
                    input.TeamId, 0, true, input.Kind == PlayContestantKind.Human));
            }
            for (var i = 0; i < config.SessionAgentCount; i++)
                contestants.Add(new(Guid.NewGuid(), PlayContestantKind.SessionAgent, $"Opponent {i + 1}", null, null,
                    new PlaySessionAgentProfile($"Opponent {i + 1}", NormalizeStrategy(config.SessionAgentStrategy), config.ModelPolicy), TeamAt(config.TeamIds, i + (config.IncludeHuman ? 1 : 0)), 0, true, false));
            if ((!game.AllowsTeams && contestants.Any(item => item.TeamId is not null)) || contestants.Count < game.MinimumContestants || contestants.Count > Math.Min(game.MaximumContestants, MaximumContestants))
                return Failed<PlayMatchSnapshot>("MatchNotJoinable", "The selected contestant count does not fit this game.", "StartMatch");
            var now = DateTimeOffset.UtcNow;
            var match = new PlayMatchSnapshot(Guid.NewGuid(), game.GameDefinitionId, game.Revision, game, contestants,
                new PlayMatchState(0, PlayRoundPhase.Active, now, GetDeadline(config.SubmissionLimit, now), new Dictionary<Guid, PlaySubmission>(), new Dictionary<Guid, string>(), []),
                PlayMatchStatus.Active, Guid.NewGuid(), now, now, config.SpectatorOnly, 0);
            return Updated(library with { Matches = library.Matches.Append(match).ToArray() }, PlayApiResult<PlayMatchSnapshot>.Success(match), match, "MatchStarted");
        }, cancellationToken);

    public async Task<PlayApiResult<PlayMatchSnapshot>> GetMatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var match = await FindMatchAsync(id, cancellationToken).ConfigureAwait(false);
        return match.Match is null ? match.Error is not null ? Fail<PlayMatchSnapshot>(match.Error) : Error<PlayMatchSnapshot>("MatchNotFound", "The match was not found.", "GetMatch", true)
            : PlayApiResult<PlayMatchSnapshot>.Success(PublicMatch(match.Match));
    }

    public async Task<PlayApiResult<IReadOnlyList<PlayContestant>>> ListContestantsAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        return match.Match is not null ? PlayApiResult<IReadOnlyList<PlayContestant>>.Success(match.Match.Contestants.ToArray()) : match.Error is not null ? Fail<IReadOnlyList<PlayContestant>>(match.Error) : Error<IReadOnlyList<PlayContestant>>("MatchNotFound", "The match was not found.", "ListContestants", true);
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> CreateMatchLobbyAsync(Guid gameId, PlayMatchConfiguration? configuration, CancellationToken cancellationToken) =>
        MutateAsync(library =>
        {
            var game = library.Games.Where(item => item.GameDefinitionId == gameId).MaxBy(item => item.Revision);
            if (game is null) return Failed<PlayMatchSnapshot>("GameNotFound", "The game definition was not found.", "StartMatch", true);
            var config = configuration ?? new PlayMatchConfiguration();
            if (config.SpectatorOnly && (!game.AllowsSpectators || config.IncludeHuman)) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "A spectator-only lobby cannot include the user as a contestant.", "StartMatch");
            if (!game.AllowsTeams && (config.TeamIds?.Count ?? 0) > 0) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "This game does not allow teams.", "StartMatch");
            var now = DateTimeOffset.UtcNow;
            var contestants = config.IncludeHuman && !config.SpectatorOnly
                ? new[] { new PlayContestant(Guid.NewGuid(), PlayContestantKind.Human, "You", null, null, null, TeamAt(config.TeamIds, 0), 0, true, true) }
                : [];
            var match = new PlayMatchSnapshot(Guid.NewGuid(), game.GameDefinitionId, game.Revision, game, contestants,
                new PlayMatchState(0, PlayRoundPhase.Pending, null, null, new Dictionary<Guid, PlaySubmission>(), new Dictionary<Guid, string>(), [], SubmissionLimit: config.SubmissionLimit),
                PlayMatchStatus.Lobby, Guid.NewGuid(), now, now, config.SpectatorOnly, 0);
            return Updated(library with { Matches = library.Matches.Append(match).ToArray() }, PlayApiResult<PlayMatchSnapshot>.Success(match));
        }, cancellationToken);

    public Task<PlayApiResult<PlayMatchSnapshot>> StartLobbyMatchAsync(Guid matchId, int sessionAgentCount, string strategy, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "StartMatch", (library, match) =>
        {
            if (match.Status != PlayMatchStatus.Lobby) return Failed<PlayMatchSnapshot>("MatchAlreadyStarted", "This match has already started.", "StartMatch", true);
            if (sessionAgentCount < 0 || match.Contestants.Count + sessionAgentCount > Math.Min(match.PinnedGame.MaximumContestants, MaximumContestants)) return Failed<PlayMatchSnapshot>("MatchNotJoinable", "The requested Agent count does not fit the remaining contestant slots.", "StartMatch");
            var agents = Enumerable.Range(0, sessionAgentCount).Select(i => new PlayContestant(Guid.NewGuid(), PlayContestantKind.SessionAgent, $"Opponent {i + 1}", null, null,
                new PlaySessionAgentProfile($"Opponent {i + 1}", NormalizeStrategy(strategy)), null, 0, true, false)).ToArray();
            var contestants = match.Contestants.Concat(agents).ToArray();
            if (contestants.Length < match.PinnedGame.MinimumContestants) return Failed<PlayMatchSnapshot>("MatchNotJoinable", "Add eligible contestants before starting this match.", "StartMatch");
            var now = DateTimeOffset.UtcNow;
            var started = match with { Contestants = contestants, Status = PlayMatchStatus.Active, MatchState = match.MatchState with { Phase = PlayRoundPhase.Active, PresentedAt = now, Deadline = GetDeadline(match.MatchState.SubmissionLimit, now) }, UpdatedAt = now };
            return UpdatedMatch(library, started, PlayApiResult<PlayMatchSnapshot>.Success(started), null, "MatchStarted");
        }, cancellationToken);

    public async Task<PlayApiResult<IReadOnlyList<PlayMatchSnapshot>>> ListMatchesAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset < 0 || limit is < 1 or > 100) return Error<IReadOnlyList<PlayMatchSnapshot>>("InvalidPage", "Match history pages use offsets ≥ 0 and limits from 1 to 100.", "ListMatches");
        var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return loaded.Error is not null ? Fail<IReadOnlyList<PlayMatchSnapshot>>(loaded.Error) : PlayApiResult<IReadOnlyList<PlayMatchSnapshot>>.Success(loaded.Library!.Matches.OrderByDescending(item => item.UpdatedAt).Skip(offset).Take(limit).Select(PublicMatch).ToArray());
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> SetTeamPrivateStateAsync(Guid matchId, Guid contestantId, string value, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "SetTeamPrivateState", (library, match) =>
        {
            var contestant = match.Contestants.FirstOrDefault(item => item.ContestantId == contestantId);
            if (contestant is null) return Failed<PlayMatchSnapshot>("ContestantNotFound", "The contestant was not found.", "SetTeamPrivateState", true);
            if (!match.PinnedGame.AllowsTeams || contestant.TeamId is not Guid teamId) return Failed<PlayMatchSnapshot>("ContestantNotEligible", "This contestant is not on a team in this game.", "SetTeamPrivateState");
            var teamState = new Dictionary<Guid, string>(match.MatchState.TeamPrivateState) { [teamId] = value.Length > 4000 ? value[..4000] : value };
            var updated = match with { MatchState = match.MatchState with { TeamPrivateState = teamState }, UpdatedAt = DateTimeOffset.UtcNow };
            return UpdatedMatch(library, updated, PlayApiResult<PlayMatchSnapshot>.Success(updated), contestant, "TeamStateChanged");
        }, cancellationToken);

    public Task<PlayApiResult<PlayContestant>> AddContestantAsync(Guid matchId, PlayContestantKind kind, string name, Guid? agentId, int? agentRevision, Guid? teamId, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "AddContestant", (library, match) =>
        {
            if (match.Status != PlayMatchStatus.Lobby) return Failed<PlayContestant>("MatchAlreadyStarted", "Contestants can only join before the match starts.", "AddContestant");
            if (match.Contestants.Count >= Math.Min(match.PinnedGame.MaximumContestants, MaximumContestants)) return Failed<PlayContestant>("MatchNotJoinable", "The match has no open contestant slots.", "AddContestant");
            if (kind == PlayContestantKind.DulcheAgent && agentId is null) return Failed<PlayContestant>("ContestantNotEligible", "A persistent Agent contestant requires its canonical AgentID.", "AddContestant");
            if (kind != PlayContestantKind.DulcheAgent && agentId is not null) return Failed<PlayContestant>("ContestantNotEligible", "Only persistent Agent contestants may reference a canonical AgentID.", "AddContestant");
            var contestant = new PlayContestant(Guid.NewGuid(), kind, CleanName(name), agentId, kind == PlayContestantKind.DulcheAgent ? agentRevision : null,
                kind == PlayContestantKind.SessionAgent ? new PlaySessionAgentProfile(CleanName(name), "balanced") : null, teamId, 0, true, false);
            return UpdatedMatch(library, match with { Contestants = match.Contestants.Append(contestant).ToArray() }, PlayApiResult<PlayContestant>.Success(contestant), contestant, "ContestantAdded");
        }, cancellationToken);

    public Task<PlayApiResult<bool>> RemoveContestantAsync(Guid matchId, Guid contestantId, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "RemoveContestant", (library, match) =>
        {
            if (match.Status != PlayMatchStatus.Lobby) return Failed<bool>("MatchAlreadyStarted", "Contestants can only leave before the match starts.", "RemoveContestant");
            if (!match.Contestants.Any(contestant => contestant.ContestantId == contestantId)) return Failed<bool>("ContestantNotFound", "The contestant was not found.", "RemoveContestant", true);
            return UpdatedMatch(library, match with { Contestants = match.Contestants.Where(contestant => contestant.ContestantId != contestantId).ToArray() }, PlayApiResult<bool>.Success(true), null, "ContestantRemoved");
        }, cancellationToken);

    public Task<PlayApiResult<IReadOnlyList<PlayContestant>>> FillAgentSlotsAsync(Guid matchId, int count, string strategy, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "FillAgentSlots", (library, match) =>
        {
            if (match.Status != PlayMatchStatus.Lobby) return Failed<IReadOnlyList<PlayContestant>>("MatchAlreadyStarted", "Agent slots can only be filled before the match starts.", "FillAgentSlots");
            if (count < 0 || match.Contestants.Count + count > Math.Min(match.PinnedGame.MaximumContestants, MaximumContestants)) return Failed<IReadOnlyList<PlayContestant>>("MatchNotJoinable", "The requested Agent count does not fit the remaining slots.", "FillAgentSlots");
            var added = Enumerable.Range(0, count).Select(i => new PlayContestant(Guid.NewGuid(), PlayContestantKind.SessionAgent, $"Opponent {match.Contestants.Count + i + 1}", null, null,
                new PlaySessionAgentProfile($"Opponent {match.Contestants.Count + i + 1}", NormalizeStrategy(strategy)), null, 0, true, false)).ToArray();
            return UpdatedMatch(library, match with { Contestants = match.Contestants.Concat(added).ToArray() }, PlayApiResult<IReadOnlyList<PlayContestant>>.Success(added), null, "AgentSlotsFilled");
        }, cancellationToken);

    public async Task<PlayApiResult<PlayContestantView>> GetContestantViewAsync(Guid matchId, Guid contestantId, CancellationToken cancellationToken)
    {
        var found = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        if (found.Match is null) return found.Error is not null ? Fail<PlayContestantView>(found.Error) : Error<PlayContestantView>("MatchNotFound", "The match was not found.", "GetContestantView", true);
        var match = found.Match;
        var contestant = match.Contestants.FirstOrDefault(item => item.ContestantId == contestantId);
        if (contestant is null) return Error<PlayContestantView>("ContestantNotFound", "The contestant was not found.", "GetContestantView", true);
        return PlayApiResult<PlayContestantView>.Success(CreateView(match, contestant, false));
    }

    public async Task<PlayApiResult<PlayContestantView>> GetSpectatorViewAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var found = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        if (found.Match is null) return found.Error is not null ? Fail<PlayContestantView>(found.Error) : Error<PlayContestantView>("MatchNotFound", "The match was not found.", "GetContestantView", true);
        if (!found.Match.PinnedGame.AllowsSpectators) return Error<PlayContestantView>("ContestantViewDenied", "Spectator access is not allowed for this game.", "GetContestantView");
        return PlayApiResult<PlayContestantView>.Success(CreateView(found.Match, null, true));
    }

    public async Task<PlayApiResult<IReadOnlyList<PlayActionDescriptor>>> ListActionsAsync(Guid matchId, Guid contestantId, CancellationToken cancellationToken)
    {
        var view = await GetContestantViewAsync(matchId, contestantId, cancellationToken).ConfigureAwait(false);
        if (!view.Succeeded) return new(false, default, view.Error);
        if (view.Value!.Phase is not (PlayRoundPhase.Active or PlayRoundPhase.AwaitingSubmissions) || view.Value.HasSubmitted)
            return PlayApiResult<IReadOnlyList<PlayActionDescriptor>>.Success([]);
        var match = await GetMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        var actions = new List<PlayActionDescriptor>();
        if (view.Value.Options.Count > 0)
            actions.Add(new("play.submit-answer", "Submit answer", "{\"selectedOption\":\"integer 0..optionCount-1\"}", PlayOperationRisk.Low, false, false));
        return PlayApiResult<IReadOnlyList<PlayActionDescriptor>>.Success(actions);
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> SubmitAnswerAsync(Guid matchId, Guid contestantId, int selectedOption, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "SubmitAnswer", (library, match) =>
        {
            if (match.Status != PlayMatchStatus.Active) return Failed<PlayMatchSnapshot>("RoundNotActive", "The match is not accepting submissions.", "SubmitAnswer", true);
            var contestant = match.Contestants.FirstOrDefault(item => item.ContestantId == contestantId && item.IsEligible);
            if (contestant is null) return Failed<PlayMatchSnapshot>("ContestantNotFound", "An eligible contestant was not found.", "SubmitAnswer", true);
            if (match.MatchState.Phase is not (PlayRoundPhase.Active or PlayRoundPhase.AwaitingSubmissions)) return Failed<PlayMatchSnapshot>("RoundNotActive", "This round is not accepting submissions.", "SubmitAnswer", true);
            if (match.MatchState.Deadline is DateTimeOffset deadline && DateTimeOffset.UtcNow > deadline) return Failed<PlayMatchSnapshot>("SubmissionDeadlinePassed", "The submission deadline has passed.", "SubmitAnswer", true);
            if (match.MatchState.Submissions.ContainsKey(contestantId)) return Failed<PlayMatchSnapshot>("SubmissionAlreadyLocked", "This contestant has already submitted an answer.", "SubmitAnswer", true);
            if (selectedOption < 0 || selectedOption >= match.PinnedGame.Questions[match.MatchState.RoundIndex].Options.Count) return Failed<PlayMatchSnapshot>("IllegalAction", "That answer option is unavailable.", "SubmitAnswer", true);
            var now = DateTimeOffset.UtcNow;
            var submissions = new Dictionary<Guid, PlaySubmission>(match.MatchState.Submissions) { [contestantId] = new(contestantId, selectedOption, now, true) };
            var phase = submissions.Count == match.Contestants.Count(item => item.IsEligible) ? PlayRoundPhase.Resolving : PlayRoundPhase.AwaitingSubmissions;
            var updated = match with { MatchState = match.MatchState with { Submissions = submissions, Phase = phase }, UpdatedAt = now };
            return UpdatedMatch(library, updated, PlayApiResult<PlayMatchSnapshot>.Success(updated), contestant, "SubmissionLocked");
        }, cancellationToken);

    public async Task<PlayApiResult<PlayMatchSnapshot>> ResolveRoundAsync(Guid matchId, CancellationToken cancellationToken) =>
        await MutateMatchAsync(matchId, "ResolveRound", (library, match) => Resolve(library, match, false), cancellationToken).ConfigureAwait(false);

    public async Task<PlayApiResult<IReadOnlyDictionary<Guid, int>>> GetLeaderboardAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        return match.Match is not null ? PlayApiResult<IReadOnlyDictionary<Guid, int>>.Success(match.Match.Contestants.ToDictionary(item => item.ContestantId, item => item.Score)) : match.Error is not null ? Fail<IReadOnlyDictionary<Guid, int>>(match.Error) : Error<IReadOnlyDictionary<Guid, int>>("MatchNotFound", "The match was not found.", "GetLeaderboard", true);
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> PauseMatchAsync(Guid matchId, CancellationToken cancellationToken) => SetStatusAsync(matchId, PlayMatchStatus.Active, PlayMatchStatus.Paused, "PauseMatch", cancellationToken);
    public Task<PlayApiResult<PlayMatchSnapshot>> ResumeMatchAsync(Guid matchId, CancellationToken cancellationToken) => SetStatusAsync(matchId, PlayMatchStatus.Paused, PlayMatchStatus.Active, "ResumeMatch", cancellationToken);

    public Task<PlayApiResult<PlayMatchSnapshot>> EndMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "EndMatch", (library, match) =>
        {
            if (match.Status is PlayMatchStatus.Completed or PlayMatchStatus.Cancelled) return Failed<PlayMatchSnapshot>("MatchAlreadyStarted", "The match has already ended.", "EndMatch", true);
            var ended = match with { Status = PlayMatchStatus.Completed, MatchState = match.MatchState with { Phase = PlayRoundPhase.Completed, CompletionReason = "Ended by the match owner." }, UpdatedAt = DateTimeOffset.UtcNow };
            return UpdatedMatch(library, ended, PlayApiResult<PlayMatchSnapshot>.Success(ended), null, "MatchEnded");
        }, cancellationToken);

    public Task<PlayApiResult<PlayCheckpoint>> CreateCheckpointAsync(Guid matchId, CancellationToken cancellationToken) =>
        MutateMatchAsync(matchId, "CreateCheckpoint", (library, match) =>
        {
            var checkpoint = new PlayCheckpoint(matchId, match.CheckpointRevision + 1, match, DateTimeOffset.UtcNow);
            var updated = match with { CheckpointRevision = checkpoint.Revision };
            var next = library with { Matches = library.Matches.Select(item => item.MatchId == matchId ? updated : item).ToArray(), Checkpoints = library.Checkpoints.Append(checkpoint).ToArray() };
            return Updated(next, PlayApiResult<PlayCheckpoint>.Success(checkpoint), updated, "CheckpointCreated");
        }, cancellationToken);

    public async Task<PlayApiResult<IReadOnlyList<PlayDomainEvent>>> GetReplayAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Error is not null) return Fail<IReadOnlyList<PlayDomainEvent>>(loaded.Error);
        if (!loaded.Library!.Matches.Any(match => match.MatchId == matchId)) return Error<IReadOnlyList<PlayDomainEvent>>("MatchNotFound", "The match was not found.", "GetReplay", true);
        var graphId = loaded.Library.Matches.First(match => match.MatchId == matchId).ActionGraphId;
        return PlayApiResult<IReadOnlyList<PlayDomainEvent>>.Success(loaded.Library.Events.Where(item => item.MatchId == matchId && item.ActionGraphId == graphId).OrderBy(item => item.Sequence).ToArray());
    }

    public async Task<PlayApiResult<PlayRoundSnapshot>> GetRoundAsync(Guid matchId, int? roundIndex, Guid? contestantId, CancellationToken cancellationToken)
    {
        var found = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        if (found.Match is null) return found.Error is not null ? Fail<PlayRoundSnapshot>(found.Error) : Error<PlayRoundSnapshot>("MatchNotFound", "The match was not found.", "GetRound", true);
        var match = found.Match;
        if (contestantId is Guid id && !match.Contestants.Any(item => item.ContestantId == id)) return Error<PlayRoundSnapshot>("ContestantNotFound", "The contestant was not found.", "GetRound", true);
        if (contestantId is null && !match.PinnedGame.AllowsSpectators) return Error<PlayRoundSnapshot>("ContestantViewDenied", "Spectator access is not allowed for this game.", "GetRound");
        var index = roundIndex ?? match.MatchState.RoundIndex;
        if (index < 0 || index >= match.PinnedGame.Questions.Count) return Error<PlayRoundSnapshot>("RoundNotActive", "The requested round is unavailable.", "GetRound", true);
        var revealed = match.MatchState.Reveals.Any(item => item.QuestionIndex == index);
        var question = revealed ? null : match.PinnedGame.Questions[index];
        return PlayApiResult<PlayRoundSnapshot>.Success(new(matchId, index, revealed ? PlayRoundPhase.Revealed : match.MatchState.Phase,
            question?.Prompt, question?.Options ?? [], index == match.MatchState.RoundIndex ? match.MatchState.PresentedAt : null,
            index == match.MatchState.RoundIndex ? match.MatchState.Deadline : null,
            revealed || index != match.MatchState.RoundIndex ? 0 : match.MatchState.Submissions.Count, revealed));
    }

    public Task<PlayApiResult<PlayMatchSnapshot>> PerformActionAsync(Guid matchId, Guid contestantId, string actionId, JsonElement input, CancellationToken cancellationToken)
    {
        if (string.Equals(actionId, "play.submit-answer", StringComparison.Ordinal))
        {
            if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("selectedOption", out var option) || !option.TryGetInt32(out var selected))
                return Task.FromResult(Error<PlayMatchSnapshot>("IllegalAction", "The submit-answer action needs an integer selectedOption.", "PerformAction", true));
            return SubmitAnswerAsync(matchId, contestantId, selected, cancellationToken);
        }
        return Task.FromResult(Error<PlayMatchSnapshot>("ActionUnavailable", "That typed action is not available in this Play match.", "PerformAction", true));
    }

    public async Task<PlayApiResult<bool>> RequestCapabilityAsync(Guid matchId, Guid contestantId, string capabilityId, CancellationToken cancellationToken)
    {
        var found = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        if (found.Match is null) return found.Error is not null ? Fail<bool>(found.Error) : Error<bool>("MatchNotFound", "The match was not found.", "RequestCapability", true);
        if (!found.Match.Contestants.Any(item => item.ContestantId == contestantId)) return Error<bool>("ContestantNotFound", "The contestant was not found.", "RequestCapability", true);
        var rule = found.Match.PinnedGame.Capabilities.FirstOrDefault(item => string.Equals(item.CapabilityId, capabilityId, StringComparison.Ordinal));
        if (rule is null || rule.Access == PlayCapabilityAccess.Denied) return Error<bool>("CapabilityDeniedByGame", "This capability is denied by the game rules.", "RequestCapability");
        if (rule.Access == PlayCapabilityAccess.Constrained) return Error<bool>("CapabilityDeniedByGame", "This capability requires a game-specific constraint that is not satisfied.", "RequestCapability", true);
        return Error<bool>("CapabilityDeniedByGame", "External capability execution requires the shared Home permission and capability brokers; Play does not execute it directly.", "RequestCapability", true);
    }

    public async Task<PlayApiResult<PlayHandoffPayload>> CreateExperiencesHandoffAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await FindMatchAsync(matchId, cancellationToken).ConfigureAwait(false);
        if (match.Match is null) return match.Error is not null ? Fail<PlayHandoffPayload>(match.Error) : Error<PlayHandoffPayload>("MatchNotFound", "The match was not found.", "ContinueInExperiences", true);
        if (match.Match.Status != PlayMatchStatus.Completed) return Error<PlayHandoffPayload>("MatchNotJoinable", "Finish the Play match before handing its permitted summary to Experiences.", "ContinueInExperiences", true);
        return PlayApiResult<PlayHandoffPayload>.Success(new(matchId, match.Match.GameDefinitionId, match.Match.GameRevision,
            $"{match.Match.PinnedGame.Title}: {match.Match.MatchState.Reveals.Count} rounds resolved.", new Dictionary<string, string> { ["play.matchId"] = matchId.ToString("D"), ["play.gameRevision"] = match.Match.GameRevision.ToString() }));
    }

    private Task<PlayApiResult<PlayMatchSnapshot>> SetStatusAsync(Guid id, PlayMatchStatus from, PlayMatchStatus to, string action, CancellationToken token) =>
        MutateMatchAsync(id, action, (library, match) =>
        {
            if (match.Status != from) return Failed<PlayMatchSnapshot>("MatchAlreadyStarted", $"The match cannot be {(action == "PauseMatch" ? "paused" : "resumed")} from its current state.", action, true);
            var now = DateTimeOffset.UtcNow;
            var state = to == PlayMatchStatus.Paused
                ? match.MatchState with { PausedAt = now, RemainingOnPause = match.MatchState.Deadline is DateTimeOffset deadline ? (deadline > now ? deadline - now : TimeSpan.Zero) : null, Deadline = null }
                : match.MatchState with { PausedAt = null, Deadline = match.MatchState.RemainingOnPause is TimeSpan remaining ? now + remaining : null, RemainingOnPause = null };
            var updated = match with { Status = to, MatchState = state, UpdatedAt = now };
            return UpdatedMatch(library, updated, PlayApiResult<PlayMatchSnapshot>.Success(updated), null, to.ToString());
        }, token);

    private static (PlayLibraryV1? Library, PlayApiError? Error) Load(PlayLibraryV1? value)
    {
        if (value is null) return (new PlayLibraryV1(1, [], [], [], [], 0), null);
        if (value.Version != 1) return (null, new("UnsupportedSchemaVersion", $"Play data schema {value.Version} is not supported; saved data was preserved.", Target, "Load", false, false));
        return (value, null);
    }

    private async Task<(PlayLibraryV1? Library, PlayApiError? Error)> ReadAsync(CancellationToken token)
    {
        if (_library is not null) return Load(_library);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_library is null)
            {
                var stored = await settings.GetAsync<PlayLibraryV1>(StateKey, token).ConfigureAwait(false);
                var loaded = Load(stored);
                if (loaded.Error is not null) return loaded;
                _library = loaded.Library;
            }
            return Load(_library);
        }
        catch (JsonException)
        {
            return (null, new("InvalidStoredData", "Saved Play data could not be read and was left unchanged.", Target, "Load", false, false));
        }
        finally { _gate.Release(); }
    }

    private async Task<(PlayMatchSnapshot? Match, PlayApiError? Error)> FindMatchAsync(Guid id, CancellationToken token)
    {
        var loaded = await ReadAsync(token).ConfigureAwait(false);
        return loaded.Error is not null ? (null, loaded.Error) : (loaded.Library!.Matches.FirstOrDefault(item => item.MatchId == id), null);
    }

    private async Task<PlayApiResult<T>> MutateAsync<T>(Func<PlayLibraryV1, (PlayLibraryV1 Library, PlayApiResult<T> Result, PlayMatchSnapshot? Match, string? EventType)> mutate, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var stored = _library ?? await settings.GetAsync<PlayLibraryV1>(StateKey, token).ConfigureAwait(false);
            var loaded = Load(stored);
            if (loaded.Error is not null) return Fail<T>(loaded.Error);
            var (next, result, match, eventType) = mutate(loaded.Library!);
            if (!result.Succeeded) return result;
            next = Trim(next);
            if (match is not null && eventType is not null)
                next = AppendEvent(next, match, eventType, null, eventType);
            await settings.SetAsync(StateKey, next, token).ConfigureAwait(false);
            _library = next;
            if (result.Value is PlayMatchSnapshot matchSnapshot)
                return PlayApiResult<T>.Success((T)(object)PublicMatch(matchSnapshot));
            if (result.Value is PlayCheckpoint checkpoint)
                return PlayApiResult<T>.Success((T)(object)(checkpoint with { Snapshot = PublicMatch(checkpoint.Snapshot) }));
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (JsonException) { return Error<T>("InvalidStoredData", "Saved Play data is invalid; no change was written.", "Mutate"); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        { return Error<T>("PersistenceFailed", "Play could not save the requested change. Try again; the previous saved state remains authoritative.", "Mutate", true, true); }
        finally { _gate.Release(); }
    }

    private Task<PlayApiResult<T>> MutateMatchAsync<T>(Guid matchId, string action,
        Func<PlayLibraryV1, PlayMatchSnapshot, (PlayLibraryV1 Library, PlayApiResult<T> Result, PlayMatchSnapshot? Match, string? EventType)> mutate, CancellationToken token) =>
        MutateAsync(library =>
        {
            var match = library.Matches.FirstOrDefault(item => item.MatchId == matchId);
            return match is null ? Failed<T>("MatchNotFound", "The match was not found.", action, true) : mutate(library, match);
        }, token);

    private static (PlayLibraryV1 Library, PlayApiResult<PlayMatchSnapshot> Result, PlayMatchSnapshot? Match, string? EventType) Resolve(PlayLibraryV1 library, PlayMatchSnapshot match, bool deadlineResolution)
    {
        if (match.Status != PlayMatchStatus.Active) return Failed<PlayMatchSnapshot>("RoundNotActive", "The match is not active.", "ResolveRound", true);
        var complete = match.Contestants.Where(item => item.IsEligible).All(item => match.MatchState.Submissions.ContainsKey(item.ContestantId));
        var deadlinePassed = match.MatchState.Deadline is DateTimeOffset deadline && DateTimeOffset.UtcNow >= deadline;
        if (!complete && !(deadlineResolution && deadlinePassed)) return Failed<PlayMatchSnapshot>("ResolutionFailed", "Submissions remain sealed until every eligible contestant submits or the deadline expires.", "ResolveRound", true, true);
        var question = match.PinnedGame.Questions[match.MatchState.RoundIndex];
        var correctness = new Dictionary<Guid, bool>();
        foreach (var contestant in match.Contestants.Where(item => item.IsEligible))
        {
            if (match.MatchState.Submissions.TryGetValue(contestant.ContestantId, out var submission)) correctness[contestant.ContestantId] = submission.SelectedOption == question.CorrectOption;
            else correctness[contestant.ContestantId] = false;
        }
        var contestants = match.Contestants.Select(item => correctness.TryGetValue(item.ContestantId, out var correct) && correct ? item with { Score = item.Score + 1 } : item).ToArray();
        var now = DateTimeOffset.UtcNow;
        var reveal = new PlayQuestionReveal(match.MatchState.RoundIndex, correctness, now);
        var finished = match.MatchState.RoundIndex + 1 >= match.PinnedGame.Questions.Count;
        var state = match.MatchState with { RoundIndex = finished ? match.MatchState.RoundIndex : match.MatchState.RoundIndex + 1,
            Phase = finished ? PlayRoundPhase.Completed : PlayRoundPhase.Active, PresentedAt = finished ? match.MatchState.PresentedAt : now,
            Deadline = null, Submissions = new Dictionary<Guid, PlaySubmission>(), Reveals = match.MatchState.Reveals.Append(reveal).ToArray(),
            CompletionReason = finished ? "All rounds resolved." : null };
        var updated = match with { Contestants = contestants, MatchState = state, Status = finished ? PlayMatchStatus.Completed : PlayMatchStatus.Active, UpdatedAt = now };
        var next = library with { Matches = library.Matches.Select(item => item.MatchId == match.MatchId ? updated : item).ToArray() };
        return UpdatedMatch(next, updated, PlayApiResult<PlayMatchSnapshot>.Success(updated), null, "RoundRevealed");
    }

    private static PlayContestantView CreateView(PlayMatchSnapshot match, PlayContestant? contestant, bool spectator)
    {
        var index = Math.Clamp(match.MatchState.RoundIndex, 0, Math.Max(0, match.PinnedGame.Questions.Count - 1));
        var revealed = match.MatchState.Reveals.Any(item => item.QuestionIndex == index);
        var question = match.PinnedGame.Questions.Count == 0 || revealed ? null : match.PinnedGame.Questions[index];
        var submitted = contestant is not null && match.MatchState.Submissions.ContainsKey(contestant.ContestantId);
        var teamState = contestant?.TeamId is Guid teamId && match.MatchState.TeamPrivateState.TryGetValue(teamId, out var state) ? state : null;
        return new(match.MatchId, contestant?.ContestantId ?? Guid.Empty, match.PinnedGame.Title, index, match.MatchState.Phase,
            question?.Prompt, question?.Options ?? [], submitted, match.MatchState.Deadline,
            match.Contestants.ToDictionary(item => item.ContestantId, item => item.Score), teamState, spectator);
    }

    private static PlayGameDefinition PublicGame(PlayGameDefinition game) => game with
    {
        Questions = game.Questions.Select(question => question with { CorrectOption = -1, Explanation = string.Empty }).ToArray()
    };

    private static PlayMatchSnapshot PublicMatch(PlayMatchSnapshot match) => match with
    {
        PinnedGame = PublicGame(match.PinnedGame),
        MatchState = match.MatchState with { Submissions = new Dictionary<Guid, PlaySubmission>(), TeamPrivateState = new Dictionary<Guid, string>() }
    };

    private static string? Validate(PlayGameDefinition game)
    {
        if (game.GameDefinitionId == Guid.Empty) return "GameDefinitionID must be stable and non-empty.";
        if (string.IsNullOrWhiteSpace(game.Title) || game.Title.Length > 120) return "A game title between 1 and 120 characters is required.";
        if (string.IsNullOrWhiteSpace(game.Category) || game.Category.Length > 80) return "A game category between 1 and 80 characters is required.";
        if (game.MinimumContestants < 1 || game.MaximumContestants < game.MinimumContestants || game.MaximumContestants > MaximumContestants) return "Contestant limits are invalid.";
        if (game.Questions is null || game.Questions.Count == 0 || game.Questions.Count > MaximumQuestions) return "A game needs between 1 and 100 rounds.";
        if (string.IsNullOrWhiteSpace(game.Scoring.PolicyId)) return "A game that selects winners must declare a scoring policy.";
        foreach (var question in game.Questions)
            if (string.IsNullOrWhiteSpace(question.Prompt) || question.Options is null || question.Options.Count is < 2 or > 8 || question.CorrectOption < 0 || question.CorrectOption >= question.Options.Count || question.Options.Any(string.IsNullOrWhiteSpace)) return "Every quiz round needs a prompt, 2–8 options and one valid answer.";
        if (game.Capabilities is null || game.Capabilities.Any(item => string.IsNullOrWhiteSpace(item.CapabilityId))) return "Every game capability must declare a stable capability ID.";
        return null;
    }

    private static PlayLibraryV1 AppendEvent(PlayLibraryV1 library, PlayMatchSnapshot match, string type, Guid? contestant, string payload)
    {
        var sequence = library.NextSequence + 1;
        var publicPayload = type == "RoundRevealed" && match.MatchState.Reveals.Count > 0
            ? JsonSerializer.Serialize(new { match.MatchState.Reveals[^1].QuestionIndex, match.MatchState.Reveals[^1].CorrectByContestant, scores = match.Contestants.ToDictionary(contestant => contestant.ContestantId, contestant => contestant.Score) })
            : JsonSerializer.Serialize(new { eventType = type, phase = match.MatchState.Phase.ToString(), roundIndex = match.MatchState.RoundIndex });
        var item = new PlayDomainEvent(Guid.NewGuid(), match.MatchId, match.ActionGraphId, sequence, type, contestant, DateTimeOffset.UtcNow, publicPayload);
        return library with { Events = library.Events.Append(item).ToArray(), NextSequence = sequence };
    }

    private static PlayLibraryV1 Trim(PlayLibraryV1 library) => library with
    {
        Games = library.Games.TakeLast(MaximumGames).ToArray(),
        Matches = library.Matches.TakeLast(MaximumMatches).ToArray(),
        Events = library.Events.TakeLast(50_000).ToArray(),
        Checkpoints = library.Checkpoints.TakeLast(5_000).ToArray()
    };

    private static (PlayLibraryV1 Library, PlayApiResult<T> Result, PlayMatchSnapshot? Match, string? EventType) Updated<T>(PlayLibraryV1 library, PlayApiResult<T> result, PlayMatchSnapshot? match = null, string? eventType = null) => (library, result, match, eventType);
    private static (PlayLibraryV1 Library, PlayApiResult<T> Result, PlayMatchSnapshot? Match, string? EventType) UpdatedMatch<T>(PlayLibraryV1 library, PlayMatchSnapshot match, PlayApiResult<T> result, PlayContestant? contestant, string eventType) =>
        (library with { Matches = library.Matches.Select(item => item.MatchId == match.MatchId ? match : item).ToArray() }, result, match, eventType);
    private static (PlayLibraryV1 Library, PlayApiResult<T> Result, PlayMatchSnapshot? Match, string? EventType) Failed<T>(string code, string message, string action, bool recoverable = false, bool retryable = false) =>
        (null!, PlayApiResult<T>.Failure(code, message, Target, action, recoverable, retryable), null, null);
    private static PlayApiResult<T> Error<T>(string code, string message, string action, bool recoverable = false, bool retryable = false) => PlayApiResult<T>.Failure(code, message, Target, action, recoverable, retryable);
    private static PlayApiResult<T> Fail<T>(PlayApiError error) => new(false, default, error);
    private static (PlayLibraryV1? Library, PlayApiError? Error) FailedLoad(string code, string message, string action) => (null, new(code, message, Target, action, false, false));
    private static Guid? TeamAt(IReadOnlyList<Guid>? teams, int index) => teams is not null && teams.Count > 0 ? teams[index % teams.Count] : null;
    private static DateTimeOffset? GetDeadline(TimeSpan? limit, DateTimeOffset now) => limit is TimeSpan duration && duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(24) ? now + duration : null;
    private static string CleanName(string name) => string.IsNullOrWhiteSpace(name) ? "Contestant" : name.Trim()[..Math.Min(name.Trim().Length, 80)];
    private static string NormalizeStrategy(string strategy) => strategy?.Trim().ToLowerInvariant() is "careful" or "accurate" or "fast" or "risky" or "balanced" ? strategy.Trim().ToLowerInvariant() : "balanced";
    private static int ParseBoundedNumber(string text, string pattern, int fallback, int minimum, int maximum) => int.TryParse(Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Groups["n"].Value, out var value) ? Math.Clamp(value, minimum, maximum) : fallback;
    private static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in value) hash = (hash ^ character) * 16777619;
            return (int)(hash & 0x7fffffff);
        }
    }

    private static PlayQuestionDefinition CreateMathQuestion(int index, int seed)
    {
        var n = Math.Abs(seed + index * 7919) % 9 + 2;
        var k = index % 5 + 2;
        var correct = n * (int)Math.Pow(k, n - 1);
        var options = new[] { correct.ToString(), (correct + n).ToString(), (correct - n).ToString(), (correct + k).ToString() };
        var rotation = Math.Abs(seed + index) % options.Length;
        var shuffled = options.Skip(rotation).Concat(options.Take(rotation)).ToArray();
        var correctIndex = Array.IndexOf(shuffled, correct.ToString());
        return new($"Differentiate f(x) = x^{n} and evaluate f'({k}).", shuffled, correctIndex,
            $"The power rule gives f'(x) = {n}x^{n - 1}, so f'({k}) = {correct}.", "Differentiation");
    }
}
