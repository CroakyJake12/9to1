using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application.Go;

namespace NineToOne.Launcher;

public sealed class LauncherNavigationGoProvider(HomeLauncherLayoutStore layouts) : IGoProvider
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
}
