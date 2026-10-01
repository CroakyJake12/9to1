using System.Runtime.CompilerServices;
using Haven.Application.Go;

namespace NineToOne.Os.Shell;

/// <summary>All Apps uses the canonical installed-app provider, independently of the Android app drawer.</summary>
public sealed class ShellGoAllAppsHome(ShellConfigurationService configuration, GoService go)
{
    public async IAsyncEnumerable<GoUpdate> ReadAsync(ShellStoredConfiguration original,
        [EnumeratorCancellation] CancellationToken ct)
    {
        async Task RequireOriginalAsync()
        {
            if (original.SessionActor is null || !await configuration.IsCurrentSessionAsync(original, ct))
                throw new UnauthorizedAccessException("Reopen All Apps in the original Home session.");
        }
        await RequireOriginalAsync();
        var query = new GoQuery("", "Apps", 1000,
            new GoScope(ProviderIds: new HashSet<string>(StringComparer.Ordinal) { "os.installed-applications" },
                Owners: new HashSet<string>(StringComparer.Ordinal) { "Home" },
                Kinds: new HashSet<string>(StringComparer.Ordinal) { "os.installed-application" }));
        await foreach (var update in go.QueryAsync(query, ct))
        {
            await RequireOriginalAsync();
            yield return update;
        }
        await RequireOriginalAsync();
    }
}
