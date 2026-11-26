using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Settings;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Settings' device/profile owners appear under the four navigation groups from Interface Atlas v4.</summary>
internal sealed class HomeAtlasSettingsView : UserControl, ICuiActionDispatcher, IDisposable
{
    private sealed record Entry(string Key,string Label,string Route,string Icon,string Group,string Parent,string Description,string Scope);
    private sealed record NavRow(string Key,string Label,string Accessible,string Description,string Mark,string Chevron,
        string Padding,string Weight,bool IsGroup,bool Selected,bool HasIcon,string IconSource);
    private sealed record Link(string Key,string Label,string Detail) { public string Accessible=>"Open "+Label; }
    private readonly CuiControlLoader _loader=new(HomeAppsCuiDocument.CreateControlRegistry());
    private readonly CuiViewModel _bindings=new();
    private readonly TextBox _search=new();
    private readonly Border _owner=new();
    private readonly Button _clear=new(),_profile=new(),_help=new(),_home=new();
    private readonly TextBlock _status=new();
    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string,Entry> _routes;
    private readonly HashSet<string> _expanded=new(StringComparer.Ordinal){"global","local"};
    private readonly Control _appearance, _lifecycle;
    private readonly HomeDulcheExternalSettings _external;
    private readonly Func<SettingsHavenPage> _legacyFactory;
    private readonly Func<Func<CancellationToken,Task>,CancellationToken,Task> _run;
    private readonly DulcheLocalClient _api;
    private SettingsHavenPage? _legacy;
    private readonly HashSet<string> _searchCollapsed=new(StringComparer.Ordinal);
    private string _previousQuery="";
    private bool _active, _disposed;
    private string _route="settings-general";
    private long _generation;
    internal event Action<string>? Navigate;

    internal HomeAtlasSettingsView(Control appearance,Control lifecycle,DulcheLocalClient api,
        Func<SettingsHavenPage> legacyFactory,Func<Func<CancellationToken,Task>,CancellationToken,Task> run,
        CakeOS.Cui.Themes.CuiThemePreferenceContext theme)
    {
        _appearance=appearance;_lifecycle=lifecycle;_api=api;_legacyFactory=legacyFactory;_run=run;
        _external=new(api,run);
        using(var stream=GetType().Assembly.GetManifestResourceStream("Haven.Desktop.Home.SettingsCatalogue.json")??throw new InvalidDataException("Settings catalogue is missing."))
            _entries=JsonSerializer.Deserialize<List<Entry>>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Settings catalogue is empty.");
        _routes=_entries.Where(e=>e.Route.Length>0).ToDictionary(e=>e.Route,StringComparer.Ordinal);
        using var view=GetType().Assembly.GetManifestResourceStream("Haven.Desktop.Home.SettingsShell.cui")??throw new InvalidDataException("Settings navigation is missing.");
        using var reader=new StreamReader(view);var parser=new CuiRichParser();var document=parser.Parse(reader.ReadToEnd(),"SettingsShell.cui");
        _loader.SetThemePreferenceContext(theme);_loader.SetBindingContext(_bindings);_loader.SetActionDispatcher(this);
        Bind("settings-search",_search);Bind("settings-owner",_owner);Bind("settings-clear-search",_clear);
        Bind("settings-profile",_profile);Bind("settings-help",_help);Bind("settings-home",_home);Bind("settings-navigation-status",_status);
        var loaded=_loader.TryLoad(document);
        if(loaded.Root is null||parser.Diagnostics.Diagnostics.Concat(loaded.Diagnostics).Any(d=>d.Severity==CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Settings navigation could not be rendered: "+string.Join("; ",loaded.Diagnostics.Select(d=>d.Message)));
        _loader.WireBindings(loaded.Root);Content=loaded.Root;
        _search.TextChanged+=(_,_)=>RefreshTree();_clear.Click+=(_,_)=>{_search.Text="";_search.Focus();};
        _profile.Click+=(_,_)=>Select("settings-account");_help.Click+=(_,_)=>Select("settings-help");
        _home.Click+=(_,_)=>Navigate?.Invoke("browse://home");
        KeyDown+=OnNavigationKey;
        SizeChanged+=(_,_)=>SetWidth();SetWidth();RefreshTree();
    }
    private void Bind(string id,Control control)=>_loader.BindAuthoredControl(id,_=>control);
    private void SetWidth() => _bindings.Set("SidebarWidth",Bounds.Width switch {>0 and <=440=>165d,>0 and <=680=>205d,>0 and <=900=>245d,_=>275d});
    private void Message(string value)=>CuiPropertyPresentation.Set(_status,"Text",value,CuiPropertyLevel.Instance,"Global Settings",1);
    internal void Activate(string? requested=null){if(_active&&requested is null)return;_active=true;Select(requested??_route);}
    internal void Deactivate(){_active=false;_generation++;_external.Deactivate();}
    internal void OpenProviders()=>Select("settings-providers");

    private void RefreshTree()
    {
        var query=(_search.Text??"").Trim();
        if(query!=_previousQuery){_searchCollapsed.Clear();_previousQuery=query;}
        var rows=new List<NavRow>();
        foreach(var group in _entries.Where(x=>x.Route.Length==0))
        {
            var members=_entries.Where(x=>x.Group==group.Key && x.Route.Length>0).ToArray();
            bool matches(Entry e)=>string.Join(" ",e.Label,e.Description,e.Route,e.Group,SearchTerms(e.Route)).Contains(query,StringComparison.OrdinalIgnoreCase);
            var direct=new HashSet<string>(members.Where(matches).Select(e=>e.Key),StringComparer.Ordinal);
            var include=new HashSet<string>(direct,StringComparer.Ordinal);
            foreach(var entry in members.Where(e=>direct.Contains(e.Key)&&e.Parent.Length>0))include.Add(entry.Parent);
            if(query.Length>0 && matches(group))include.UnionWith(members.Select(e=>e.Key));
            if(query.Length>0&&include.Count==0)continue;
            var expanded=query.Length>0?!_searchCollapsed.Contains(group.Key):_expanded.Contains(group.Key);
            rows.Add(new(group.Key,group.Label,group.Label+" settings group",group.Description,"",expanded?"⌄":"›",
                "5 7 7 5","700",true,false,false,"icon:settings"));
            if(!expanded)continue;
            foreach(var entry in members.Where(e=>query.Length==0||include.Contains(e.Key)))
            {
                var child=entry.Parent.Length>0;
                var parent=child?_entries.First(e=>e.Key==entry.Parent).Label:null;
                rows.Add(new(entry.Key,entry.Label,string.Join(" › ",new[]{group.Label,parent,entry.Label}.Where(v=>!string.IsNullOrEmpty(v))),"",
                    child?"└":"","",child?"21 5 4 5":"5 7 4 7",child?"600":"700",false,
                    entry.Route==_route||(_route=="settings-provider"&&entry.Route=="settings-providers"),
                    !child,"icon:"+IconKey(entry.Icon)));
            }
        }
        _bindings.Set("Rows",rows.Select(r => new Dictionary<string,object?>
        {
            ["Key"]=r.Key, ["Label"]=r.Label, ["Accessible"]=r.Accessible, ["Description"]=r.Description,
            ["Mark"]=r.Mark, ["Chevron"]=r.Chevron, ["Padding"]=r.Padding, ["Weight"]=r.Weight,
            ["IsGroup"]=r.IsGroup, ["Selected"]=r.Selected, ["HasIcon"]=r.HasIcon, ["IconSource"]=r.IconSource
        }).ToArray());_bindings.Set("HasQuery",query.Length>0);_bindings.Set("NoResults",rows.Count==0);
    }
    private static string SearchTerms(string route)=>route switch
    {
        "settings-providers"=>"Dulche OpenCode external endpoint API key credentials model IDs",
        "settings-installed-models"=>"Keep Models Loaded model residency Always While Resources are Free While Active Never",
        "settings-skills"=>"skill extensions", "settings-plugins"=>"plugins integrations tools MCP",
        "settings-accessibility"=>"contrast fonts keyboard focus motion", _=>""
    };
    private static string IconKey(string key)=>key switch
    {
        "Palette"=>"palette","Sparkles"=>"sparkles","ShieldCheck"=>"shield","UserRound"=>"user",
        "Monitor" or "PanelsTopLeft"=>"monitor","Grid2X2"=>"apps","Cpu"=>"cpu",
        "Bell"=>"bell","Bookmark"=>"bookmark","History"=>"history","Keyboard"=>"keyboard",
        "Activity"=>"activity","Layers"=>"folder",_=>"settings"
    };

    public ValueTask DispatchAsync(string command,object? parameter,CancellationToken cancellationToken=default)
    {
        if(!_active||_disposed)return ValueTask.CompletedTask;
        if(parameter is not string key)return ValueTask.CompletedTask;
        if(command=="settings.select")
        {
            var entry=_entries.FirstOrDefault(e=>e.Key==key);
            if(entry is null)return ValueTask.CompletedTask;
            if(entry.Route.Length==0)
            {
                var set=string.IsNullOrWhiteSpace(_search.Text)?_expanded:_searchCollapsed;
                if(!set.Remove(key))set.Add(key);
                RefreshTree();
            }
            else Select(entry.Route);
        }
        else if(command=="settings.related")
        {
            if(key=="native-account"||key.StartsWith("browse://",StringComparison.Ordinal)||key.StartsWith("9to1://",StringComparison.Ordinal))Navigate?.Invoke(key);
            else Select(key);
        }
        return ValueTask.CompletedTask;
    }

    private void Select(string route)
    {
        var recognized=_routes.ContainsKey(route)||route is "settings-help" or "settings-provider" or "settings-updates" or "settings-about";
        if(!recognized)return;
        _generation++;_route=route;_external.Deactivate();_owner.Child=null;Message("");
        var entry=_routes.GetValueOrDefault(route);
        if(entry is not null&&string.IsNullOrWhiteSpace(_search.Text))_expanded.Add(entry.Group);
        RefreshTree();
        var scope=entry is null?"Global":_entries.First(e=>e.Key==entry.Group).Scope;
        var heading=entry?.Label??(route switch{"settings-provider"=>"Provider connection","settings-updates"=>"Updates","settings-about"=>"About 9to1",_=>"Settings help"});
        _bindings.Set("ShowSectionTitle",route=="settings-general");_bindings.Set("SectionTitle","");_bindings.Set("Scope",scope);_bindings.Set("Heading",heading);_bindings.Set("HasDescription",false);
        _bindings.Set("ShowTitle",true);
        var links=new List<Link>();
        void add(string key,string label,string detail)=>links.Add(new(key,label,detail));
        void destination(string id,string detail){if(_routes.TryGetValue(id,out var target))add(id,target.Label,detail);}
        if(entry is not null)
            foreach(var child in _entries.Where(e=>e.Parent==entry.Key))add(child.Route,child.Label,"");
        try
        {
            switch(route)
            {
                case "settings-general":
                    _bindings.Set("SectionTitle","Across 9to1");
                    destination("settings-appearance","Theme, display and accessibility preferences");
                    destination("settings-ai","Model routes, providers and personalisation");
                    destination("settings-security","Privacy, permissions and sandboxing");
                    destination("settings-account","Profile and account resources");
                    break;
                case "settings-appearance":break;
                case "appearance": case "settings-theme-options": case "settings-accessibility":
                    _bindings.Set("ShowTitle",false);_owner.Child=_appearance;break;
                case "settings-local-general":case "settings-installed-models":case "settings-haven-model-memory":case "settings-haven-services":
                    _owner.Child=_lifecycle;
                    add("browse://home/models","Open Models","Manage models, profiles, agents and Swarm Mode.");
                    destination("settings-providers","Connect an external client to Dulche.");
                    ObserveDevice();break;
                case "settings-providers":
                    _owner.Child=_external;if(_active)_external.Activate();
                    add("settings-provider","Personal endpoint","Review existing provider connections and their settings.");
                    destination("settings-ai","Model roles and routing");destination("settings-privacy","Processing privacy");break;
                case "settings-provider": ShowLegacy("integrations");break;
                case "settings-layout":ShowLegacy("appearance");break;
                case "settings-ai":add("browse://home/models","Model routes","Choose a local model and a performance profile.");break;
                case "settings-personalisation":ShowLegacy("privacy");break;
                case "settings-plugins":case "settings-skills":ShowLegacy("extensions");break;
                case "settings-voice":ShowLegacy("voice");break;
                case "settings-security-general":case "settings-permissions":case "settings-sandbox":ShowLegacy("permissions");break;
                case "settings-privacy":ShowLegacy("privacy");break;
                case "settings-profile":case "settings-subscriptions":case "settings-account-storage":
                    add("native-account","Open account","Review the signed-in account through its native account owner.");break;
                case "settings-mesh":add("9to1://mesh","Open Mesh","Manage device connections.");break;
                case "settings-apps":add("browse://home","Installed apps","Open the actual Home app catalogue.");add("settings-updates","Updates","Version and update channel on this device.");break;
                case "settings-notifications":add("browse://home/events","Notifications and activity","Open the device’s activity view.");break;
                case "settings-haven-newtab":add("browse://newtab","Open New Tab","Open the configured New Tab surface.");break;
                case "settings-haven-tabs-address":case "settings-haven-tab-history":case "settings-haven-performance":
                    add("browse://settings","Haven browser settings","Review the browser’s own settings and tab behaviour.");break;
                case "settings-haven-bookmarks":case "settings-haven-pinned":case "settings-haven-shortcut-order":
                    add("browse://bookmarks","Open bookmarks","Manage saved destinations in Haven.");break;
                case "settings-haven-history":add("browse://history","Open browsing history","Review records in Haven.");break;
                case "settings-haven-recent-files":add("browse://home/library","Recent files","Open the Home library.");break;
                case "settings-haven-key-navigation":case "settings-haven-key-menus":
                    Description(route=="settings-haven-key-navigation"?"Use Tab and Shift+Tab to move focus. Use the arrow keys in the Settings tree.":"Use Enter or Space to activate a focused control. Escape closes an open menu.");
                    destination("settings-accessibility","Review contrast and keyboard accessibility.");break;
                case "settings-haven-storage-cache":ObserveDevice();add("browse://downloads","Downloads","Inspect the browser’s downloads.");break;
                case "settings-apps-shared":ShowLegacy("apps");break;
                case "settings-updates":ShowLegacy("updates");break;
                case "settings-about":ShowLegacy("about");break;
                case "settings-help":add("settings-about","About 9to1","Application and runtime information.");Description("Search by setting or category. Global follows the account, Local applies to this device, 9to1 Haven covers browsing, and Apps contains shared and individual app settings.");
                    destination("settings-general","Return to the settings index.");break;
                default:
                    if(entry?.Group=="apps")
                    {
                        destination("settings-apps-shared","Shared preferences for installed apps.");
                        add("browse://home","Open installed apps","Choose the app to inspect its own settings.");
                    }
                    break;
            }
        }
        catch(Exception error) { Message("This settings owner could not open: "+error.Message); }
        _bindings.Set("Links",links.Select(l => new Dictionary<string,object?>
        { ["Key"]=l.Key, ["Label"]=l.Label, ["Detail"]=l.Detail, ["Accessible"]=l.Accessible }).ToArray());

    }
    private void Description(string text){_bindings.Set("Description",text);_bindings.Set("HasDescription",true);}
    private void ShowLegacy(string section){_legacy??=_legacyFactory();_legacy.ShowAtlasSection(section);_owner.Child=_legacy;}
    private void ObserveDevice()
    {
        var epoch=_generation;
        _=Run();
        async Task Run()
        {
            try
            {
                await _run(async token=>
                {
                    try
                    {
                        var state=await _api.GetAsync("status",token);
                        if(_active&&epoch==_generation) Message("Dulche · "+state["phase"]+" · "+state["resources"]?["host_available_mib"]+" MiB RAM available");
                    }
                    catch(Exception e) when(e is IOException or HttpRequestException or OperationCanceledException)
                    { if(_active&&epoch==_generation)Message(e.Message); }
                },CancellationToken.None);
            }
            catch(ObjectDisposedException) when(_disposed||!_active){}
        }
    }
    private void OnNavigationKey(object? sender,KeyEventArgs e)
    {
        if(e.Source is not Control source)return;
        if(ReferenceEquals(source,_search))
        {
            var query=(_search.Text??"").Trim();
            if(e.Key==Key.Escape && query.Length>0){_search.Text="";e.Handled=true;return;}
            if(e.Key is Key.Enter or Key.Down && query.Length>0)
            {
                var first=_entries.FirstOrDefault(row=>row.Route.Length>0 &&
                    (row.Label.Contains(query,StringComparison.OrdinalIgnoreCase)||SearchTerms(row.Route).Contains(query,StringComparison.OrdinalIgnoreCase)));
                if(first is not null)
                {
                    if(e.Key==Key.Enter)Select(first.Route);
                    else this.GetVisualDescendants().OfType<Button>().FirstOrDefault(b=>AutomationProperties.GetName(b)?.EndsWith(" › "+first.Label,StringComparison.Ordinal)==true)?.Focus();
                    e.Handled=true;
                }
            }
            return;
        }
        var button=source as Button??source.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if(button is null)return;
        var items=this.GetVisualDescendants().OfType<Button>().Where(b=>AutomationProperties.GetName(b)?.Contains(" › ")==true||AutomationProperties.GetName(b)?.EndsWith(" settings group",StringComparison.Ordinal)==true).ToList();
        var index=items.IndexOf(button);if(index<0)return;
        var target=e.Key switch{Key.Up=>index-1,Key.Down=>index+1,Key.Home=>0,Key.End=>items.Count-1,_=>-1};
        if(target>=0&&target<items.Count){items[target].Focus();e.Handled=true;}
    }
    public void Dispose(){if(_disposed)return;_disposed=true;Deactivate();_owner.Child=null;_external.Dispose();_legacy?.Dispose();_loader.Dispose();Content=null;}
}
