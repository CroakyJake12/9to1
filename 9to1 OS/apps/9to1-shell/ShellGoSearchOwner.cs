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
        static IReadOnlySet<string>? Copy(IReadOnlySet<string>? values)
        {
            if (values is null) return null;
            var captured = new List<string>();
            foreach (var value in values)
            {
                if (captured.Count == 256 || string.IsNullOrWhiteSpace(value) || value.Length > 4096)
                    throw new ArgumentException("Displayed Go scope identifiers must be bounded and explicit.");
                captured.Add(value);
            }
            return captured.ToFrozenSet(StringComparer.Ordinal);
        }
        var scope = query.Scope is { } s ? new GoScope(Copy(s.ProviderIds), Copy(s.Owners), Copy(s.Kinds), Copy(s.ActionIds)) : null;
        var request = new ShellGoSearchRequest(this, original, query with { Scope = scope });
        Interlocked.Exchange(ref _current, request);
        return request;
    }
    public bool IsDisplayed(ShellGoSearchRequest request) => ReferenceEquals(request.Owner, this) && ReferenceEquals(Volatile.Read(ref _current), request);
    public async Task<bool> IsCurrentAsync(ShellGoSearchRequest request, CancellationToken ct)
    {
        if (!IsDisplayed(request)) return false;
        try
        {
            await configuration.GetForOriginalAsync(request.Original, ct);
            return IsDisplayed(request) && !ct.IsCancellationRequested;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
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
