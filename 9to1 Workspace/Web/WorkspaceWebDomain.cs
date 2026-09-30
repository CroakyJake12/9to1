using System.Text.Json;

namespace NineToOne.Web;

public interface IWorkspaceWebDomain
{
    string Name { get; }
    IReadOnlySet<string> Actions { get; }
    Task<JsonElement> InvokeAsync(string action, JsonElement arguments, Guid accountID, CancellationToken cancellationToken);
}

public sealed class WorkspaceWebDomains
{
    private readonly Dictionary<string, IWorkspaceWebDomain> domains = new(StringComparer.Ordinal);
    public void Register(IWorkspaceWebDomain domain)
    {
        if (!domains.TryAdd(domain.Name, domain)) throw new InvalidOperationException("duplicate_domain");
    }
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Catalogue => domains.ToDictionary(p => p.Key, p => p.Value.Actions);
    public Task<JsonElement> InvokeAsync(string domain, string action, JsonElement args, Guid accountID, CancellationToken ct)
    {
        if (!domains.TryGetValue(domain, out var adapter) || !adapter.Actions.Contains(action))
            throw new KeyNotFoundException("capability_unavailable");
        return adapter.InvokeAsync(action, args, accountID, ct);
    }
}
