using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Application;

public sealed record SiteContentImportIssue(string Code, string Element, string Message, bool BlocksImport);
public sealed record SiteContentImportResult(SiteProject? Project, IReadOnlyList<SiteContentImportIssue> Issues);

/// <summary>Converts HTML content to editable canonical Sites components. Scripts, embeds and forms require owning runtime adapters.</summary>
public sealed class SiteHtmlContentImporter(SiteProjectService projects)
{
    private static readonly IReadOnlyDictionary<string,string> Types = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["main"]="main",["sup"]="superscript",["sub"]="subscript",["div"]="container",["section"]="section",["article"]="section",["header"]="header",["footer"]="footer",["nav"]="navigation",
        ["p"]="paragraph",["span"]="inline-text",["a"]="link",["strong"]="strong",["b"]="strong",["em"]="emphasis",["i"]="emphasis",
        ["ul"]="list",["ol"]="ordered-list",["li"]="list-item",["img"]="image",["br"]="line-break",["hr"]="separator",
        ["figure"]="figure",["figcaption"]="caption",["blockquote"]="quote",["table"]="table",["thead"]="table-head",
        ["tbody"]="table-body",["tr"]="row",["td"]="cell",["th"]="header-cell",
        ["h1"]="heading",["h2"]="heading",["h3"]="heading",["h4"]="heading",["h5"]="heading",["h6"]="heading"
    };
    public async Task<SiteContentImportResult> ImportPageAsync(Guid siteID, long expectedRevision, string name, string route,
        string html, CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(html);
        if(html.Length>4_000_000) throw new ArgumentException("Content exceeds the bounded import limit.",nameof(html));
        var segments=SiteAddressRules.NormalizeRoutePath(route);
        var path=segments.Count==0 ? "/" : "/"+string.Join('/',segments);
        if(string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Page name is required.",nameof(name));
        var document=await new HtmlParser().ParseDocumentAsync(html,ct);
        var issues=new List<SiteContentImportIssue>();var components=new List<SiteComponent>();
        JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
        Guid? Convert(INode node,int depth)
        {
            ct.ThrowIfCancellationRequested();
            if(depth>128 || components.Count>=20_000) throw new InvalidDataException("Content hierarchy exceeds bounded import limits.");
            if(node is IComment) return null;
            var properties=new Dictionary<string,JsonElement>();string type;
            if(node is IText text)
            {
                if(text.Data.Length==0) return null;
                type="inline-text";properties["text"]=Json(text.Data);
            }
            else if(node is IElement element)
            {
                if(!Types.TryGetValue(element.LocalName,out type!))
                {
                    issues.Add(new("OwningRuntimeRequired",element.LocalName,"Unsupported content is retained in the original source; import cannot silently omit it.",true));
                    return null;
                }
                if(type=="heading")properties["level"]=Json(int.Parse(element.LocalName[1..],System.Globalization.CultureInfo.InvariantCulture));
                foreach(var attribute in element.Attributes)
                {
                    if(attribute.Name.StartsWith("on",StringComparison.OrdinalIgnoreCase))issues.Add(new("ExecutableAttribute",element.LocalName,"Executable handlers require an owning typed interaction provider.",true));
                    else if(attribute.Name is "href" or "src" or "alt" or "aria-label")properties[attribute.Name]=Json(attribute.Value);
                    else if(attribute.Name is "style" or "class" or "id" or "width" or "height" or "colspan" or "rowspan")
                        issues.Add(new("DesignMappingRequired",element.LocalName,$"Attribute '{attribute.Name}' needs explicit canonical design/layout mapping; content import does not claim visual parity.",false));
                }
                foreach(var key in new[]{"href","src"})
                    if(properties.TryGetValue(key,out var url) && url.GetString() is {} address && !address.StartsWith('/') && !address.StartsWith('#') && (!Uri.TryCreate(address,UriKind.Absolute,out var absolute) || absolute.Scheme is not ("http" or "https" or "mailto")))
                        issues.Add(new("UnsafeURL",element.LocalName,"The content URL protocol is not allowed.",true));
                if(type=="image"&&!properties.ContainsKey("alt"))
                    issues.Add(new("ImageAlternativeMissing",element.LocalName,"Provide alternative text or an explicit empty alternative before publication.",true));
            }
            else return null;
            var id=Guid.NewGuid();
            var children=node.ChildNodes.Select(child=>Convert(child,depth+1)).Where(child=>child.HasValue).Select(child=>child!.Value).ToArray();
            components.Add(new(id,type,properties,children,new Dictionary<string,IReadOnlyList<Guid>>(),
                new(SiteLayoutMode.Flow,new Dictionary<string,JsonElement>(),new Dictionary<string,SiteLayoutOverride>()),
                new Dictionary<string,string>(),new Dictionary<string,JsonElement>(),new Dictionary<string,JsonElement>(),
                new Dictionary<string,JsonElement>(),null,null,new Dictionary<string,JsonElement>(),1));
            return id;
        }
        var roots=document.Body!.ChildNodes.Select(node=>Convert(node,0)).Where(id=>id.HasValue).Select(id=>id!.Value).ToArray();
        if(document.Head!.QuerySelector("script,style,link,base") is not null)
            issues.Add(new("DocumentDependencyRequired","head","Document-level dependencies require explicit owning asset/runtime mapping.",true));
        if(issues.Any(issue=>issue.BlocksImport))return new(null,issues);
        var saved=await projects.UpdateProjectAsync(siteID,expectedRevision,project=>
        {
            if(project.Routes.Any(existing=>existing.Pattern==path)) throw new SiteOperationException(new("InvalidInput","Route already exists.","Sites.ImportPage",false));
            var page=new SitePage(Guid.NewGuid(),name,null,roots,name,null,1);
            return project with {Pages=project.Pages.Append(page).ToArray(),Routes=project.Routes.Append(new SiteRoute(Guid.NewGuid(),page.PageId,path,SiteRouteKind.Static)).ToArray(),Components=project.Components.Concat(components).ToArray()};
        },ct);
        if(saved.Error is {} error)issues.Add(new(error.Code,"project",error.Message,true));
        return new(saved.Value,issues);
    }
}
