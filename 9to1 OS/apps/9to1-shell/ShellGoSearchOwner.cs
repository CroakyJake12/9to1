using System.Collections.Frozen;
using Haven.Application.Go;

namespace NineToOne.Os.Shell;

/// <summary>Owns immutable displayed query identity; never grants provider or action authority.</summary>
public sealed class ShellGoSearchOwner(ShellConfigurationService configuration)
{
    private ShellGoSearchRequest? _current;
    public ShellGoSearchRequest Begin(ShellStoredConfiguration original, GoQuery query)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(query);
        var scope = query.Scope is { } s ? new GoScope(s.ProviderIds?.ToFrozenSet(StringComparer.Ordinal),
            s.Owners?.ToFrozenSet(StringComparer.Ordinal), s.Kinds?.ToFrozenSet(StringComparer.Ordinal), s.ActionIds?.ToFrozenSet(StringComparer.Ordinal)) : null;
        var request = new ShellGoSearchRequest(this, original, query with { Scope = scope });
        Interlocked.Exchange(ref _current, request);
        return request;
    }
    public bool IsDisplayed(ShellGoSearchRequest request) => ReferenceEquals(request.Owner, this) && ReferenceEquals(Volatile.Read(ref _current), request);
    public async Task<bool> IsCurrentAsync(ShellGoSearchRequest request, CancellationToken ct)
    {
        if (!IsDisplayed(request)) return false;
        var current = await configuration.IsCurrentSessionAsync(request.Original, ct);
        return current && IsDisplayed(request) && !ct.IsCancellationRequested;
    }
    public void Invalidate() => Interlocked.Exchange(ref _current, null);
}

/// <summary>Issuer-bound in-process displayed search; cannot be reconstructed from result fields.</summary>
public sealed class ShellGoSearchRequest
{
    internal ShellGoSearchRequest(ShellGoSearchOwner owner, ShellStoredConfiguration original, GoQuery query)
    { Owner = owner; Original = original; Query = query; }
    internal ShellGoSearchOwner Owner { get; }
    internal ShellStoredConfiguration Original { get; }
    public GoQuery Query { get; }
}
