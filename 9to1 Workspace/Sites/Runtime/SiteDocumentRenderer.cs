using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HavenOS.Apps.Sites.Domain;

namespace HavenOS.Apps.Sites.Runtime;

public enum SiteRenderContext { PublicRoute, PagePreview, ComponentPreview, EditorShell }
public sealed record SiteDiagnostic(string Code,string Message,Guid? PageID,Guid? ComponentID,bool IsError);
public sealed record SiteRenderResult(string Document,IReadOnlyList<SiteDiagnostic> Diagnostics);

/// <summary>One public renderer shared by previews and build artifacts. Editor chrome is a separate context.</summary>
public sealed class SiteDocumentRenderer
{
    private static readonly HashSet<string> CssProperties = new(StringComparer.Ordinal) {
        "display","flex-direction","flex-wrap","justify-content","align-items","gap","padding","margin",
        "width","height","min-width","max-width","min-height","max-height","grid-template-columns","grid-template-rows",
        "position","top","right","bottom","left","overflow","aspect-ratio","font-size","font-weight","line-height",
        "color","background-color","border-radius","border","box-shadow","text-align","opacity" };
    public SiteRenderResult Render(SiteProject project,Guid pageID,SiteRenderContext context)
    {
        if(context==SiteRenderContext.EditorShell) throw new InvalidOperationException("Public renderer cannot own editor chrome.");
        var page=project.Pages.Single(p=>p.PageId==pageID);var diagnostics=new List<SiteDiagnostic>();var css=new StringBuilder();
        var output=new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        output.Append("<title>").Append(E(page.SeoTitle??page.Name)).Append("</title>");
        if(page.MetaDescription is {} description)output.Append("<meta name=\"description\" content=\"").Append(E(description)).Append("\">");
        css.Append(":root{");
        foreach(var token in project.DesignSystem.ColourTokens.Concat(project.DesignSystem.TypographyTokens).Concat(project.DesignSystem.SpacingTokens).Concat(project.DesignSystem.RadiusTokens).Concat(project.DesignSystem.ShadowTokens))
            if(SafeIdentifier(token.Key)&&SafeCssValue(token.Value))css.Append("--").Append(token.Key).Append(':').Append(token.Value).Append(';');
        css.Append("}*{box-sizing:border-box}[hidden]{display:none!important}body{margin:0;font-family:system-ui,sans-serif}img,video{max-width:100%;height:auto}a:focus-visible,button:focus-visible{outline:3px solid currentColor;outline-offset:3px}@media(prefers-reduced-motion:reduce){*,*::before,*::after{animation:none!important;transition:none!important}}");
        var pageNodes=HavenOS.Apps.Sites.Application.SiteAuthoringService.Reachable(project,page.RootComponentIds);
        var defaults=new Dictionary<string,string>(StringComparer.Ordinal);
        var selections=new Dictionary<Guid,(string Group,string Value)>();
        foreach(var component in project.Components.Where(c=>pageNodes.Contains(c.ComponentId)))
        {
            if(component.Bindings.Count>0)diagnostics.Add(new("BindingUnavailable","Bindings need an owning typed runtime provider.",pageID,component.ComponentId,true));
            foreach(var interaction in component.Interactions)
            {
                if(interaction.Key!="select-content-group" || component.ComponentType.ToLowerInvariant()!="button" || interaction.Value.ValueKind!=JsonValueKind.Object ||
                    !interaction.Value.TryGetProperty("groupID",out var groupJson) || groupJson.ValueKind!=JsonValueKind.String ||
                    !interaction.Value.TryGetProperty("value",out var valueJson) || valueJson.ValueKind!=JsonValueKind.String)
                {diagnostics.Add(new("InteractionUnavailable","The interaction has no registered typed public runtime.",pageID,component.ComponentId,true));continue;}
                var group=groupJson.GetString()!;var value=valueJson.GetString()!;
                if(!SafeIdentifier(group)||!SafeIdentifier(value)||group.Length>128||value.Length>128 ||
                    !project.Components.Any(target=>pageNodes.Contains(target.ComponentId)&&Get(target,"contentGroup")==group&&Get(target,"contentValue")==value))
                {diagnostics.Add(new("ContentSelectionTargetMissing","Selection must name an existing content group/value on this page.",pageID,component.ComponentId,true));continue;}
                selections[component.ComponentId]=(group,value);
                if(Get(component,"selectedByDefault")=="true")
                {
                    if(!defaults.TryAdd(group,value)&&defaults[group]!=value)diagnostics.Add(new("ContentSelectionDefaultConflict","A content group must have one initial selection.",pageID,component.ComponentId,true));
                }
            }
        }
        foreach(var selection in selections.Values)
            if(!defaults.ContainsKey(selection.Group))diagnostics.Add(new("ContentSelectionDefaultMissing","A content group needs an explicit initial selection.",pageID,null,true));
        var active=new HashSet<Guid>();
        string RenderNode(Guid id)
        {
            if(!active.Add(id))throw new InvalidDataException("component_cycle");
            try
            {
                var node=project.Components.Single(c=>c.ComponentId==id);
                var type=node.ComponentType.ToLowerInvariant();
                var tag=type switch { "section" or "hero"=>"section","header"=>"header","footer"=>"footer","navigation"=>"nav",
                    "text" or "paragraph"=>"p","heading"=>Heading(node),"image"=>"img","button"=>"button","link"=>"a",
                    "list"=>"ul","ordered-list"=>"ol","list-item"=>"li","table"=>"table","table-head"=>"thead","table-body"=>"tbody","row"=>"tr","cell"=>"td","header-cell"=>"th",
                    "main"=>"main","superscript"=>"sup","subscript"=>"sub","inline-text"=>"span","strong"=>"strong","emphasis"=>"em","line-break"=>"br","figure"=>"figure","caption"=>"figcaption","quote"=>"blockquote","separator"=>"hr","container" or "card" or "grid" or "stack"=>"div", _=>null };
                if(tag is null){diagnostics.Add(new("UnsupportedVisualConstruct","Code-defined component requires its registered runtime provider.",pageID,id,true));return "";}
                var selector="[data-site-component=\""+id.ToString("N")+"\"]";
                var layout=new Dictionary<string,JsonElement>(node.Layout.Properties);
                if(node.Layout.Mode != SiteLayoutMode.Flow)
                    layout.TryAdd("display",JsonSerializer.SerializeToElement(node.Layout.Mode == SiteLayoutMode.Grid ? "grid" : "flex"));
                AppendStyle(css,selector,layout,diagnostics,pageID,id);
                // Wider max-width rules must precede narrower ones so phone overrides
                // inherit tablet properties and win independently of persisted map order.
                foreach(var responsive in node.Layout.BreakpointOverrides
                    .OrderByDescending(pair => project.DesignSystem.Breakpoints.GetValueOrDefault(pair.Key))
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if(!project.DesignSystem.Breakpoints.TryGetValue(responsive.Key,out var width)||width<=0)
                    {diagnostics.Add(new("BreakpointUnknown","Responsive override references an undefined breakpoint.",pageID,id,true));continue;}
                    css.Append("@media(max-width:").Append(width).Append("px){");AppendStyle(css,selector,responsive.Value.ChangedProperties,diagnostics,pageID,id);css.Append('}');
                }
                var html=new StringBuilder("<"+tag+" data-site-component=\""+id.ToString("N")+"\"");
                if(Get(node,"hidden")=="true")html.Append(" hidden");
                var label=Get(node,"aria-label");if(label is not null)html.Append(" aria-label=\"").Append(E(label)).Append('"');
                if(tag=="button")
                {
                    html.Append(" type=\"button\"");
                    if(selections.TryGetValue(id,out var select))html.Append(" data-site-select-group=\"").Append(E(select.Group)).Append("\" data-site-select-value=\"").Append(E(select.Value))
                        .Append("\" aria-pressed=\"").Append(defaults.GetValueOrDefault(select.Group)==select.Value?"true":"false").Append('"');
                }
                if(Get(node,"contentGroup") is {} contentGroup && Get(node,"contentValue") is {} contentValue)
                {
                    html.Append(" data-site-content-group=\"").Append(E(contentGroup)).Append("\" data-site-content-value=\"").Append(E(contentValue)).Append('"');
                    if(defaults.TryGetValue(contentGroup,out var selected)&&selected!=contentValue)html.Append(" hidden");
                }
                if(tag=="a")
                {
                    var href=Get(node,"href")??"#";
                    if(!SafeUrl(href))diagnostics.Add(new("UnsafeURL","Link protocol is blocked.",pageID,id,true));
                    else html.Append(" href=\"").Append(E(href)).Append('"');
                }
                if(tag=="img")
                {
                    var source=Get(node,"src")??"";var alt=Get(node,"alt");
                    if(!SafeUrl(source))diagnostics.Add(new("UnsafeURL","Image protocol is blocked.",pageID,id,true));
                    else html.Append(" src=\"").Append(E(source)).Append('"');
                    if(alt is null) diagnostics.Add(new("ImageAlternativeMissing","Provide alternative text or an explicit empty alternative for decoration.",pageID,id,true));
                    html.Append(" alt=\"").Append(E(alt??"")).Append("\" loading=\"lazy\">");return html.ToString();
                }
                if(tag is "br" or "hr") return html.Append('>').ToString();
                html.Append('>').Append(E(Get(node,"text")??""));
                if(node.ReusableDefinitionId is {} definition)
                {
                    var reusable=project.ReusableComponents.SingleOrDefault(d=>d.DefinitionId.ToString()==definition || d.DefinitionId.ToString("N")==definition);
                    if(reusable is null)diagnostics.Add(new("ReusableDefinitionMissing","Linked component definition is unavailable.",pageID,id,true));
                    else html.Append(RenderNode(reusable.RootComponentId));
                }
                foreach(var child in node.ChildIds.Concat(node.Slots.Values.SelectMany(v=>v))) html.Append(RenderNode(child));
                return html.Append("</").Append(tag).Append('>').ToString();
            }
            finally{active.Remove(id);}
        }
        var body=string.Concat(page.RootComponentIds.Select(RenderNode));
        output.Append("<style>").Append(css).Append("</style></head><body>").Append(body);
        if(selections.Count>0)output.Append("<script>document.addEventListener('click',function(event){const button=event.target.closest('[data-site-select-group]');if(!button)return;const group=button.dataset.siteSelectGroup;const value=button.dataset.siteSelectValue;document.querySelectorAll('[data-site-select-group]').forEach(function(item){if(item.dataset.siteSelectGroup===group)item.setAttribute('aria-pressed',String(item.dataset.siteSelectValue===value));});document.querySelectorAll('[data-site-content-group]').forEach(function(item){if(item.dataset.siteContentGroup===group)item.hidden=item.dataset.siteContentValue!==value;});});</script>");
        output.Append("</body></html>");
        return new(output.ToString(),diagnostics);
    }
    private static string Heading(SiteComponent c){var level=Get(c,"level");return int.TryParse(level,out var parsed)&&parsed is>=1 and<=6 ? "h"+parsed : "h2";}
    private static string? Get(SiteComponent c,string key)=>c.Properties.TryGetValue(key,out var value)?value.ValueKind==JsonValueKind.String?value.GetString():value.GetRawText():null;
    private static string E(string value)=>HtmlEncoder.Default.Encode(value);
    private static bool SafeIdentifier(string value)=>value.Length>0&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
    private static bool SafeCssValue(string value)=>value.Length<=512&&!value.Any(c=>c is '<' or '>' or '{' or '}' or ';' or '\\')&&!value.Contains("url(",StringComparison.OrdinalIgnoreCase)&&!value.Contains("expression",StringComparison.OrdinalIgnoreCase);
    private static bool SafeUrl(string value)=>!value.StartsWith("//",StringComparison.Ordinal)&&!value.Contains('\\')&&(value.StartsWith('/')||value.StartsWith('#')||
        Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http" or "mailto");
    private static void AppendStyle(StringBuilder css,string selector,IReadOnlyDictionary<string,JsonElement> properties,List<SiteDiagnostic> diagnostics,Guid pageID,Guid componentID)
    {
        css.Append(selector).Append('{');
        foreach(var prop in properties)
        {
            var value=prop.Value.ValueKind==JsonValueKind.String?prop.Value.GetString()!:prop.Value.ToString();
            if(!CssProperties.Contains(prop.Key)||!SafeCssValue(value)){diagnostics.Add(new("StyleInvalid","Unsupported or unsafe style property: "+prop.Key,pageID,componentID,true));continue;}
            css.Append(prop.Key).Append(':').Append(value).Append(';');
        }
        css.Append('}');
    }
}
