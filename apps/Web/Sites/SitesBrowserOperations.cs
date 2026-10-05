using HavenOS.Apps.Sites.Domain;

namespace NineToOne.Web.Sites;

/// <summary>Host-bound presentation calls into the canonical Sites owner. No defaults, wire format,
/// filesystem location, actor, approval or publishing authority is supplied by the browser.</summary>
public sealed record SitesBrowserOperations(
    Func<CancellationToken, Task<SiteApiResult<IReadOnlyList<SiteProject>>>> List,
    Func<Guid, CancellationToken, Task<SiteApiResult<SiteProject>>> Open,
    Func<string, CancellationToken, Task<SiteApiResult<SiteProject>>> Create,
    Func<Guid, long, string, string, CancellationToken, Task<SiteApiResult<SiteProject>>> CreatePage,
    Func<Guid, long, Guid, string, CancellationToken, Task<SiteApiResult<SiteProject>>> AddParagraph,
    Func<Guid, long, Guid, string, CancellationToken, Task<SiteApiResult<SiteProject>>> SetText);
