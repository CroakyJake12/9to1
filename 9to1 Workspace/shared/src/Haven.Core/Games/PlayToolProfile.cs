namespace Haven.Core.Games;

/// <summary>The outcome of authorizing a tool call against a match's tool profile.</summary>
public enum PlayToolDecision { Allowed, Denied }

/// <summary>A structured authorization result. A denial always carries an explicit reason rather than silently allowing or failing.</summary>
public sealed record PlayToolAuthorization(PlayToolDecision Decision, string ToolID, string? Reason)
{
    public static PlayToolAuthorization Allow(string toolID) => new(PlayToolDecision.Allowed, toolID, null);
    public static PlayToolAuthorization Deny(string toolID, string reason) => new(PlayToolDecision.Denied, toolID, reason);
    public bool IsAllowed => Decision == PlayToolDecision.Allowed;
}

/// <summary>A game-specific tool profile that constrains real capability as well as prompts. A no-tools match exposes no outside assistance:
/// every tool call is denied regardless of the declared set. Otherwise only explicitly permitted tool IDs are allowed.</summary>
public sealed record PlayToolProfile(string ProfileID, string Name, bool NoTools, IReadOnlyList<string> PermittedToolIDs)
{
    public const int MaximumPermittedTools = 256;

    public PlayToolProfile Capture()
    {
        if (string.IsNullOrWhiteSpace(ProfileID)) throw new InvalidDataException("Play tool profile identity is required.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Play tool profile name is required.");
        var permitted = (PermittedToolIDs ?? []).ToArray();
        if (permitted.Length > MaximumPermittedTools) throw new InvalidDataException("Play tool profile exceeds the configured tool limit.");
        if (permitted.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("A permitted tool id may not be empty.");
        if (permitted.Distinct(StringComparer.Ordinal).Count() != permitted.Length) throw new InvalidOperationException("PlayDuplicatePermittedTool");
        // A no-tools match must not declare any permitted tools; that would contradict 'no outside assistance'.
        if (NoTools && permitted.Length > 0) throw new InvalidOperationException("PlayNoToolsProfileCannotPermitTools");
        return this with { PermittedToolIDs = permitted };
    }

    public static PlayToolProfile CreateNoTools(string profileID, string name) =>
        (new PlayToolProfile(profileID, name, NoTools: true, Array.Empty<string>())).Capture();

    public static PlayToolProfile WithTools(string profileID, string name, IEnumerable<string> permittedToolIDs) =>
        (new PlayToolProfile(profileID, name, NoTools: false, (permittedToolIDs ?? []).ToArray())).Capture();

    /// <summary>Authorize a tool call. A no-tools match denies everything; otherwise only declared tools are allowed. Never throws for an unknown tool — it returns an explicit denial.</summary>
    public PlayToolAuthorization Authorize(string toolID)
    {
        var captured = Capture();
        if (string.IsNullOrWhiteSpace(toolID)) throw new InvalidDataException("A tool call requires a tool id.");
        if (captured.NoTools) return PlayToolAuthorization.Deny(toolID, "PlayNoOutsideAssistance");
        return captured.PermittedToolIDs.Any(allowed => string.Equals(allowed, toolID, StringComparison.Ordinal))
            ? PlayToolAuthorization.Allow(toolID)
            : PlayToolAuthorization.Deny(toolID, "PlayToolNotPermitted");
    }
}
