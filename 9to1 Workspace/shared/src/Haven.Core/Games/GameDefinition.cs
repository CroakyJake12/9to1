namespace Haven.Core.Games;

/// <summary>Who may see a piece of game state. Hidden information is removed at the runtime layer until reveal.
/// Self = only the owning contestant; Team = teammates; Public = everyone including spectators; Judge = judge/authority only.</summary>
public enum PlayVisibility { Self, Team, Public, Judge }

/// <summary>A declared field in the match state schema. Each field carries its visibility so hidden answers, future questions and other teams'
/// private information are structurally distinguished from public state.</summary>
public sealed record PlayStateField(string Name, string Type, PlayVisibility Visibility)
{
    public PlayStateField Capture()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Play state field name is required.");
        if (string.IsNullOrWhiteSpace(Type)) throw new InvalidDataException("Play state field type is required.");
        if (!Enum.IsDefined(Visibility)) throw new InvalidDataException("Play state field visibility is unsupported.");
        return this;
    }
}

/// <summary>A declared contestant role. Persistent Assistant/Specialist contestants retain canonical identity; temporary Session contestants become
/// persistent only through an explicit save operation (modelled by IsPersistent).</summary>
public sealed record PlayRole(string Name, bool IsPersistent)
{
    public PlayRole Capture()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Play role name is required.");
        return this;
    }
}

/// <summary>A legal typed action a contestant may take. The engine validates availability/ownership/prerequisites before changing match state.
/// A tool profile constrains real capability as well as prompts.</summary>
public sealed record PlayAction(string Name, string TargetType, IReadOnlyList<string> AllowedRoles)
{
    public PlayAction Capture()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Play action name is required.");
        if (AllowedRoles is null || AllowedRoles.Count == 0) throw new InvalidDataException("A play action must permit at least one role.");
        if (AllowedRoles.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("A play action role may not be empty.");
        return this;
    }
}

/// <summary>Turn/round/clock configuration. Simultaneous submissions remain sealed until resolution/deadline.</summary>
public sealed record PlayClock(int TurnSeconds, int RoundSeconds)
{
    public PlayClock Capture()
    {
        if (TurnSeconds <= 0 || RoundSeconds <= 0) throw new InvalidDataException("Play clock durations must be positive.");
        return this;
    }
}

/// <summary>A versioned authored game definition. A Match pins a specific DefinitionID + Revision so later edits cannot rewrite an active match.
/// Revision-aware immutable transactions never mutate the original.</summary>
public sealed record GameDefinition(Guid DefinitionID, int Revision, IReadOnlyList<PlayStateField> StateSchema,
    IReadOnlyList<PlayRole> Roles, IReadOnlyList<string> Teams, IReadOnlyList<PlayAction> Actions, PlayClock Clock,
    string ScoringRule, string WinnerRule)
{
    public const int CurrentSchemaVersion = 1;

    public GameDefinition Capture()
    {
        if (DefinitionID == Guid.Empty) throw new InvalidDataException("Game definition identity is missing.");
        if (Revision < 1) throw new InvalidDataException("Game definition revision must be positive.");
        if (StateSchema is null || Roles is null || Teams is null || Actions is null) throw new InvalidDataException("Game definition structure is missing.");
        var fields = StateSchema.Select(f => f?.Capture() ?? throw new InvalidDataException("A play state field is missing.")).ToArray();
        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length) throw new InvalidOperationException("PlayDuplicateStateField");
        var roles = Roles.Select(r => r?.Capture() ?? throw new InvalidDataException("A play role is missing.")).ToArray();
        if (roles.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count() != roles.Length) throw new InvalidOperationException("PlayDuplicateRole");
        var teams = Teams.ToArray();
        if (teams.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("A team name may not be empty.");
        if (teams.Distinct(StringComparer.Ordinal).Count() != teams.Length) throw new InvalidOperationException("PlayDuplicateTeam");
        var actions = Actions.Select(a => a?.Capture() ?? throw new InvalidDataException("A play action is missing.")).ToArray();
        if (actions.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != actions.Length) throw new InvalidOperationException("PlayDuplicateAction");
        var roleNames = roles.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var action in actions)
            foreach (var role in action.AllowedRoles)
                if (!roleNames.Contains(role)) throw new InvalidOperationException("PlayActionUnknownRole");
        Clock.Capture();
        if (string.IsNullOrWhiteSpace(ScoringRule)) throw new InvalidDataException("A scoring rule is required.");
        if (string.IsNullOrWhiteSpace(WinnerRule)) throw new InvalidDataException("A winner rule is required.");
        return this with { StateSchema = fields, Roles = roles, Teams = teams, Actions = actions };
    }

    public static GameDefinition Create(Guid definitionID, IEnumerable<PlayStateField> stateSchema, IEnumerable<PlayRole> roles,
        IEnumerable<string> teams, PlayClock clock, string scoringRule, string winnerRule)
        => new GameDefinition(definitionID, 1, stateSchema.ToArray(), roles.ToArray(), teams.ToArray(), Array.Empty<PlayAction>(), clock, scoringRule, winnerRule).Capture();

    private GameDefinition Next(int expectedRevision, IReadOnlyList<PlayStateField> fields, IReadOnlyList<PlayRole> roles,
        IReadOnlyList<string> teams, IReadOnlyList<PlayAction> actions, PlayClock clock, string scoringRule, string winnerRule)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GameDefinitionRevisionConflict");
        return new GameDefinition(DefinitionID, checked(Revision + 1), fields, roles, teams, actions, clock, scoringRule, winnerRule).Capture();
    }

    public GameDefinition AddAction(PlayAction action, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Actions.Any(existing => string.Equals(existing.Name, action.Name, StringComparison.Ordinal))) throw new InvalidOperationException("PlayDuplicateAction");
        return Next(expectedRevision, StateSchema, Roles, Teams, Actions.Append(action).ToArray(), Clock, ScoringRule, WinnerRule);
    }

    public GameDefinition AddStateField(PlayStateField field, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (StateSchema.Any(existing => string.Equals(existing.Name, field.Name, StringComparison.Ordinal))) throw new InvalidOperationException("PlayDuplicateStateField");
        return Next(expectedRevision, StateSchema.Append(field).ToArray(), Roles, Teams, Actions, Clock, ScoringRule, WinnerRule);
    }

    public GameDefinition AddRole(PlayRole role, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (Roles.Any(existing => string.Equals(existing.Name, role.Name, StringComparison.Ordinal))) throw new InvalidOperationException("PlayDuplicateRole");
        return Next(expectedRevision, StateSchema, Roles.Append(role).ToArray(), Teams, Actions, Clock, ScoringRule, WinnerRule);
    }

    public GameDefinition AddTeam(string team, int expectedRevision)
    {
        if (string.IsNullOrWhiteSpace(team)) throw new InvalidDataException("A team name may not be empty.");
        if (Teams.Any(existing => string.Equals(existing, team, StringComparison.Ordinal))) throw new InvalidOperationException("PlayDuplicateTeam");
        return Next(expectedRevision, StateSchema, Roles, Teams.Append(team).ToArray(), Actions, Clock, ScoringRule, WinnerRule);
    }

    public GameDefinition SetClock(PlayClock clock, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Next(expectedRevision, StateSchema, Roles, Teams, Actions, clock, ScoringRule, WinnerRule);
    }

    /// <summary>Produce the authorised view for a contestant: fields whose visibility permits that audience are retained; hidden answers, future
    /// questions and other teams' private information are removed at this layer. Spectators receive only Public fields.</summary>
    public IReadOnlyList<PlayStateField> AuthorisedView(bool isContestant, bool isJudge)
    {
        var visible = new List<PlayStateField>();
        foreach (var field in StateSchema)
        {
            if (field.Visibility == PlayVisibility.Public) visible.Add(field);
            else if (field.Visibility == PlayVisibility.Judge && isJudge) visible.Add(field);
            else if ((field.Visibility == PlayVisibility.Self || field.Visibility == PlayVisibility.Team) && isContestant) visible.Add(field);
        }
        return visible;
    }
}
