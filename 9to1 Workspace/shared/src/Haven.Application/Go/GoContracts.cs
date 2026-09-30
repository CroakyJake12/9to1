namespace Haven.Application.Go;

/// <summary>Discovery filters only. Providers must independently check current actor/resource authority.</summary>
public sealed record GoScope(IReadOnlySet<string>? ProviderIds = null, IReadOnlySet<string>? Owners = null,
    IReadOnlySet<string>? Kinds = null, IReadOnlySet<string>? ActionIds = null);

public sealed record GoQuery(string Text, string? Category = null, int Limit = 100, GoScope? Scope = null);
public sealed record GoCanonicalReference(string Owner, string Kind, string Id, string Revision);
public sealed record GoAction(string Id, string Label);
public sealed record GoResult(string ProviderId, GoCanonicalReference Reference, string Label, string Category, IReadOnlyList<GoAction> Actions);
public sealed record GoUpdate(string ProviderId, GoResult? Result, bool Complete, string? Failure);
/// <summary>Owns permission-filtered live discovery and fresh owner-authorized actions, never Go-owned entity copies.</summary>
public interface IGoProvider
{
    string ProviderId { get; }
    IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken cancellationToken);
    Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken cancellationToken);
}
