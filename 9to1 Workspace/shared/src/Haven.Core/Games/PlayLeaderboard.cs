namespace Haven.Core.Games;

/// <summary>Declared ordering policy for the leaderboard. Standings are ordered by score then a deterministic tie-break so replay is stable.</summary>
public enum PlayLeaderboardOrder { ScoreThenName }

/// <summary>A public leaderboard row. It exposes only public identity/status and derived score — never private model context, judge state or unrevealed submissions.</summary>
public sealed record PlayLeaderboardRow(string Team, int Score, int Rank)
{
    public PlayLeaderboardRow Capture()
    {
        if (string.IsNullOrWhiteSpace(Team)) throw new InvalidDataException("Play leaderboard team is required.");
        if (Score < 0) throw new InvalidDataException("Play leaderboard score may not be negative.");
        if (Rank < 1) throw new InvalidDataException("Play leaderboard rank must be positive.");
        return this;
    }
}

/// <summary>A pure projection of committed match state onto public standings. It derives scores strictly from accepted ScoredEvents, so a draft or
/// sealed-but-unrevealed submission never appears. Recomputation is deterministic: the same MatchState always yields identical rows.</summary>
public sealed record PlayLeaderboard(PlayLeaderboardOrder Order, IReadOnlyList<PlayLeaderboardRow> Rows)
{
    public const int CurrentSchemaVersion = 1;

    public PlayLeaderboard Capture()
    {
        if (!Enum.IsDefined(Order)) throw new InvalidDataException("Play leaderboard order is unsupported.");
        var rows = (Rows ?? []).Select(r => r?.Capture() ?? throw new InvalidDataException("A play leaderboard row is missing.")).ToArray();
        if (rows.Select(r => r.Team).Distinct(StringComparer.Ordinal).Count() != rows.Length) throw new InvalidOperationException("PlayDuplicateLeaderboardTeam");
        return this with { Rows = rows };
    }

    /// <summary>Build standings from a match state. Only accepted scored events contribute; teams with no events still appear at zero so the roster is complete.</summary>
    public static PlayLeaderboard From(MatchState state, PlayLeaderboardOrder order = PlayLeaderboardOrder.ScoreThenName)
    {
        var captured = state.Capture();
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var contestant in captured.Contestants)
            if (!totals.ContainsKey(contestant.Team)) totals[contestant.Team] = 0;
        foreach (var scored in captured.ScoredEvents)
            totals[scored.Team] = totals.GetValueOrDefault(scored.Team) + scored.Points;

        // Deterministic ordering: score descending, then team name ascending as a stable tie-break.
        var ordered = totals
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select((pair, index) => new PlayLeaderboardRow(pair.Key, pair.Value, index + 1))
            .ToArray();
        return new PlayLeaderboard(order, ordered).Capture();
    }

    /// <summary>Rank for a team after the declared ordering; teams absent from the projection are unranked.</summary>
    public int? RankOf(string team) => Rows.FirstOrDefault(row => string.Equals(row.Team, team, StringComparison.Ordinal))?.Rank;
}
