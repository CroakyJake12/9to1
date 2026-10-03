namespace NineToOne.Cui.AI;

public enum InvocationKind { Plugin, Skill, App, Agent, File, System }
public enum AppInteractionPath { TypedApi, ComputerUseRequired, TypedApiAndComputerUse }
public enum AppClassification { OrdinaryApplication, Game, AntiCheatProtected, Unknown }

/// <summary>Provider-filtered resource identity. Listing never confers read or execution permission.</summary>
public sealed record InvocationResource(
    InvocationKind Kind, string CanonicalId, string Label, string? Revision = null,
    string? Icon = null, AppInteractionPath? InteractionPath = null,
    AppClassification Classification = AppClassification.Unknown,
    bool Available = true);

public sealed record InvocationToken(string TokenId, InvocationResource Resource, int Start, int Length,
    string RenderedText, string? ComputerUseInvocationId = null)
{
    public string AccessibilityLabel => $"Invoked {Resource.Kind}: {Resource.Label}";
}

public sealed record InvocationSection(InvocationKind Kind, bool Collapsed, IReadOnlyList<InvocationResource> Resources);

public interface IInvocationResolver
{
    /// <summary>Re-resolve stable identities and revisions against current permissions; reject revoked or changed resources.</summary>
    ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken cancellationToken);
}

public interface IInvocationCatalogue
{
    /// <summary>Return only resources visible in the authenticated request's current scope.</summary>
    ValueTask<IReadOnlyList<InvocationResource>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>Text and canonical invocation identities are edited atomically, avoiding invisible attachments.</summary>
public sealed class InvocationCompose
{
    public const string ComputerUseCapabilityId = "9to1.system.computer-use";
    public static InvocationResource ComputerUse { get; } = new(InvocationKind.System,
        ComputerUseCapabilityId, "Computer Use", Icon: "computer");
    private readonly List<InvocationToken> _tokens = [];
    private readonly HashSet<InvocationKind> _collapsed = [];
    public string Text { get; private set; } = string.Empty;
    public IReadOnlyList<InvocationToken> Tokens => _tokens.ToArray();
    public bool IsMenuOpen { get; private set; }
    public int MenuStart { get; private set; }
    public string Query { get; private set; } = string.Empty;

    public void UpdateCaret(int caret)
    {
        if (caret < 0 || caret > Text.Length) throw new ArgumentOutOfRangeException(nameof(caret));
        var at = Text.LastIndexOf('@', Math.Max(0, caret - 1), caret == 0 ? 0 : caret);
        IsMenuOpen = at >= 0 && (at == 0 || char.IsWhiteSpace(Text[at - 1])) &&
            !_tokens.Any(t => at >= t.Start && at < t.Start + t.Length) &&
            !Text.AsSpan(at + 1, caret - at - 1).Contains('\n');
        if (IsMenuOpen) { MenuStart = at; Query = Text[(at + 1)..caret]; }
        else Query = string.Empty;
    }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == Text) return;
        var prefix = 0;
        while (prefix < Text.Length && prefix < text.Length && Text[prefix] == text[prefix]) prefix++;
        var suffix = 0;
        while (suffix < Text.Length - prefix && suffix < text.Length - prefix &&
            Text[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        Replace(prefix, Text.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
    }

    public void Replace(int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (start < 0 || length < 0 || start > Text.Length - length) throw new ArgumentOutOfRangeException(nameof(start));
        var end = start + length;
        var delta = replacement.Length - length;
        for (var i = _tokens.Count - 1; i >= 0; i--)
        {
            var token = _tokens[i];
            var intersects = length == 0 ? start > token.Start && start < token.Start + token.Length
                : start < token.Start + token.Length && end > token.Start;
            if (intersects) _tokens.RemoveAt(i);
            else if (token.Start >= end) _tokens[i] = token with { Start = token.Start + delta };
        }
        Text = Text.Remove(start, length).Insert(start, replacement);
        UpdateCaret(start + replacement.Length);
    }

    public InvocationToken Insert(InvocationResource resource, int caret)
    {
        Validate(resource);
        UpdateCaret(caret);
        var start = IsMenuOpen ? MenuStart : caret;
        var rendered = $"@[{resource.Label}]";
        Replace(start, caret - start, rendered);
        var token = new InvocationToken(Guid.NewGuid().ToString("N"), resource, start, rendered.Length,
            rendered, resource.CanonicalId == ComputerUseCapabilityId ? Guid.NewGuid().ToString("N") : null);
        _tokens.Add(token);
        _tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        IsMenuOpen = false;
        return token;
    }

    public void Remove(string tokenId)
    {
        var token = _tokens.SingleOrDefault(t => t.TokenId == tokenId);
        if (token is not null) Replace(token.Start, token.Length, string.Empty);
    }

    public void SetCollapsed(InvocationKind kind, bool collapsed)
    {
        if (collapsed) _collapsed.Add(kind); else _collapsed.Remove(kind);
    }

    public IReadOnlyList<InvocationSection> Sections(IEnumerable<InvocationResource> resources)
    {
        var eligible = resources.Append(ComputerUse).Where(IsEligible)
            .Where(r => r.Label.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                r.CanonicalId.Contains(Query, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(r => (r.Kind, r.CanonicalId, r.Revision)).ToArray();
        return Enum.GetValues<InvocationKind>().Select(kind => new InvocationSection(kind,
            _collapsed.Contains(kind), eligible.Where(r => r.Kind == kind)
                .OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.CanonicalId, StringComparer.Ordinal).ToArray())).ToArray();
    }

    public IReadOnlyList<InvocationToken> Resolve()
    {
        foreach (var token in _tokens)
        {
            Validate(token.Resource);
            if (Text.Substring(token.Start, token.Length) != token.RenderedText)
                throw new InvalidOperationException("Invocation text no longer matches its canonical token.");
        }
        var computerUse = _tokens.Any(t => t.Resource.CanonicalId == ComputerUseCapabilityId && t.ComputerUseInvocationId is not null);
        if (!computerUse && _tokens.Any(t => t.Resource.InteractionPath == AppInteractionPath.ComputerUseRequired))
            throw new InvalidOperationException("This app requires explicit @Computer Use invocation.");
        return Tokens;
    }

    public static bool IsEligible(InvocationResource resource) => resource.Available &&
        (resource.Kind != InvocationKind.App || (resource.InteractionPath is not null &&
            resource.Classification == AppClassification.OrdinaryApplication));

    private static void Validate(InvocationResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource.CanonicalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource.Label);
        if (!IsEligible(resource)) throw new InvalidOperationException("The resource is unavailable for invocation.");
        if ((resource.Kind is InvocationKind.Plugin or InvocationKind.Skill or InvocationKind.Agent or InvocationKind.File) &&
            string.IsNullOrWhiteSpace(resource.Revision))
            throw new InvalidOperationException("A resolved resource version or revision is required.");
        if (resource.Kind == InvocationKind.System && resource.CanonicalId != ComputerUseCapabilityId)
            throw new InvalidOperationException("Unknown reserved system invocation.");
    }
}
