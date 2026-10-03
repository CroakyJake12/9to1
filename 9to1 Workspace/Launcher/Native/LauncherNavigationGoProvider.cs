using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;

namespace NineToOne.Launcher;

public sealed class LauncherNavigationGoProvider(HomeLauncherLayoutStore layouts) : IGoCanonicalResolver, IGoOriginalActorQuery, IGoOriginalActorCanonicalResolver, IGoOriginalActorInvocation
{
    public const string Id = "launcher.pages";
    public string ProviderId => Id;
    private static string Stamp(LauncherStoredLayout layout) => layout.AuthorityId + "@" + layout.Revision.ToString(CultureInfo.InvariantCulture);
    public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (query.Category is not (null or "Launcher Pages")) yield break;
        var current = await layouts.ReadExistingAsync(ct);
        if (current is null) yield break;
        foreach (var page in current.Current.Pages.Where(p => p.Name.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            var fresh = await layouts.ReadExistingAsync(ct);
            if (fresh is null || Stamp(fresh) != Stamp(current)) throw new UnauthorizedAccessException("The current launcher layout changed during discovery.");
            yield return new(Id, new("Launcher", "launcher.page", page.Id.ToString("D"), Stamp(current)), page.Name, "Launcher Pages", [new("OpenPage", "Open page")]);
        }
    }
    public async Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct)
    {
        if (reference.Owner != "Launcher" || reference.Kind != "launcher.page" || actionId != "OpenPage" || !Guid.TryParse(reference.Id, out var id))
            throw new UnauthorizedAccessException("This launcher navigation reference is invalid.");
        var current = await layouts.ReadExistingAsync(ct) ?? throw new InvalidOperationException("Open Launcher to initialize this profile's layout.");
        if (reference.Revision != Stamp(current)) throw new IOException("The launcher layout or profile changed. Search again.");
        await layouts.EditAsync(current, layout => LauncherLayoutEdits.SelectPage(layout, id), ct);
    }
    public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor originalActor,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        // Even an excluded category cannot turn a revoked original actor into a successful owner read.
        var current = await layouts.ReadExistingForActorAsync(originalActor, ct);
        if (query.Category is not (null or "Launcher Pages") || current is null) yield break;
        foreach (var page in current.Current.Pages.Where(p => p.Name.Contains(query.Text, StringComparison.CurrentCultureIgnoreCase)))
        {
            var fresh = await layouts.ReadExistingForActorAsync(originalActor, ct);
            if (fresh is null || Stamp(fresh) != Stamp(current)) throw new UnauthorizedAccessException("The original launcher layout changed during discovery.");
            yield return Result(page.Id, page.Name, current);
        }
        var final = await layouts.ReadExistingForActorAsync(originalActor, ct);
        if (final is null || Stamp(final) != Stamp(current)) throw new UnauthorizedAccessException("The original launcher layout changed during discovery.");
    }
    public async Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
    {
        if (locator is not { Owner: "Launcher", Kind: "launcher.page" } || !Guid.TryParse(locator.Id, out var id)) return null;
        var current = await layouts.ReadExistingAsync(ct);
        return current is null ? null : ResolvePage(current, id);
    }
    public async Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        var current = await layouts.ReadExistingForActorAsync(originalActor, ct);
        if (locator is not { Owner: "Launcher", Kind: "launcher.page" } || !Guid.TryParse(locator.Id, out var id) || current is null) return null;
        return ResolvePage(current, id);
    }
    public async Task InvokeForActorAsync(GoCanonicalReference reference, string actionId, AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        if (reference.Owner != "Launcher" || reference.Kind != "launcher.page" || actionId != "OpenPage" || !Guid.TryParse(reference.Id, out var id))
            throw new UnauthorizedAccessException("This launcher navigation reference is invalid.");
        var current = await layouts.ReadExistingForActorAsync(originalActor, ct) ?? throw new InvalidOperationException("The original launcher layout is unavailable.");
        if (reference.Revision != Stamp(current)) throw new IOException("The original launcher layout changed. Search again.");
        await layouts.EditAsActorAsync(current, originalActor, layout => LauncherLayoutEdits.SelectPage(layout, id), ct);
    }
    private static GoResult? ResolvePage(LauncherStoredLayout current, Guid id)
    {
        var page = current.Current.Pages.SingleOrDefault(p => p.Id == id);
        return page is null ? null : Result(page.Id, page.Name, current);
    }
    private static GoResult Result(Guid id, string label, LauncherStoredLayout current) =>
        new(Id, new("Launcher", "launcher.page", id.ToString("D"), Stamp(current)), label, "Launcher Pages", [new("OpenPage", "Open page")]);

}
