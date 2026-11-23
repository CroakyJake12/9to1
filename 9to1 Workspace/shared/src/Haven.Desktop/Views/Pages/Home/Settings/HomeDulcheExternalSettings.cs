using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed class HomeDulcheExternalSettings : UserControl, IDisposable
{
    private sealed record Client(string Id,string Label,bool Revoked) { public override string ToString()=>Label; }
    private readonly DulcheLocalClient _api;
    private readonly Func<Func<CancellationToken,Task>,CancellationToken,Task> _run;
    private readonly CuiControlLoader _loader=new();
    private readonly CuiViewModel _bindings=new();
    private readonly Dictionary<string,Control> _controls=new();
    private readonly CancellationTokenSource _lifetime=new();
    private bool _enabled, _active, _busy;
    private long _view;
    private string? _base, _config, _token, _revoke;
    private TextBox Token=>(TextBox)_controls["token"];
    private ComboBox Models=>(ComboBox)_controls["models"];
    private ComboBox Clients=>(ComboBox)_controls["clients"];
    private TextBox NameField=>(TextBox)_controls["client-name"];

    internal HomeDulcheExternalSettings(DulcheLocalClient client,Func<Func<CancellationToken,Task>,CancellationToken,Task> run)
    {
        _api=client;_run=run;
        using var stream=GetType().Assembly.GetManifestResourceStream("Haven.Desktop.Home.DulcheProvider.cui")??throw new InvalidDataException("Provider layout unavailable");
        using var reader=new StreamReader(stream);var parser=new CuiRichParser();var document=parser.Parse(reader.ReadToEnd(),"DulcheProvider.cui");
        foreach(var key in new[]{"state","base","result"}) Mount(key,new TextBlock());
        foreach(var key in new[]{"models","clients"}) Mount(key,new ComboBox());
        Mount("config",new TextBox { IsReadOnly=true, AcceptsReturn=true });
        Mount("token",new TextBox { IsReadOnly=true, IsUndoEnabled=false, PasswordChar='●', RevealPassword=false });
        Mount("client-name",new TextBox { Text="OpenCode" });
        foreach(var key in "enable copy-base copy-model connect copy-config issue reveal copy-token revoke refresh".Split(' '))
        {
            var button=new Button();Mount(key,button);button.Click+=async (_,_)=>await RunAsync(key);
        }
        _loader.SetBindingContext(_bindings);var view=_loader.TryLoad(document);
        if(view.Root is null || parser.Diagnostics.Diagnostics.Concat(view.Diagnostics).Any(d=>d.Severity==CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Dulche external provider controls failed to render: "+string.Join("; ",parser.Diagnostics.Diagnostics.Concat(view.Diagnostics).Select(d=>d.Message)));
        _loader.WireBindings(view.Root);Content=view.Root;
        Clients.SelectionChanged+=(_,_)=>{_revoke=null;RenderActions();};
        Models.SelectionChanged+=(_,_)=>RenderActions();
        RenderActions();
    }
    private void Mount(string key,Control c) { _controls[key]=c; _loader.BindAuthoredControl("provider-"+key,_=>c); }
    private static void Set(Control c,string property,object? value)=>CuiPropertyPresentation.Set(c,property,value,CuiPropertyLevel.Instance,"Dulche external provider",order:2);
    private void Message(string value)=>Set(_controls["result"],"Text",value);
    private static IEnumerable<JsonNode> Rows(JsonNode? node)=>node is JsonArray a?a.OfType<JsonNode>():[];
    public void Activate(){_active=true;_view++;_=RunAsync("refresh");}
    public void Deactivate(){_active=false;_view++;_revoke=null;ForgetToken();}
    private void ForgetToken()
    {
        _token=null;Token.Text="";Token.RevealPassword=false;_bindings.Set("HasOneTimeToken",false);
        Set(_controls["reveal"],"Content","Show");
    }
    private void RenderActions()
    {
        foreach(var (key,control) in _controls)
            if(control is Button) Set(control,"IsEnabled",_active && !_busy);
        Set(_controls["copy-base"],"IsEnabled",_active&&!_busy&&!string.IsNullOrWhiteSpace(_base));
        Set(_controls["copy-model"],"IsEnabled",_active&&!_busy&&Models.SelectedItem is string);
        Set(_controls["copy-config"],"IsEnabled",_active&&!_busy&&!string.IsNullOrWhiteSpace(_config));
        Set(_controls["revoke"],"IsEnabled",_active&&!_busy&&Clients.SelectedItem is Client {Revoked:false});
        Set(_controls["connect"],"IsEnabled",_active&&!_busy&&_enabled);
    }
    private async Task RefreshAsync(long view,CancellationToken token)
    {
        var state=await _api.GetAsync("external",token);
        if (!_active || view!=_view)return;
        _enabled=state["enabled"]?.GetValue<bool>()==true;
        var listening=state["listening"]?.GetValue<bool>()==true;
        Set(_controls["enable"],"Content",_enabled?"Disable":"Enable");
        Set(_controls["state"],"Text",state["error"]?.ToString() is {Length:>0} error?error:
            (listening?"Listening on this device":(_enabled?"Enabled · listener unavailable":"Disabled")));
        _base=state["base_url"]?.ToString(); Set(_controls["base"],"Text",_base??"No endpoint reported");
        _config=state["opencode_config"]?.ToJsonString(new JsonSerializerOptions {WriteIndented=true});
        ((TextBox)_controls["config"]).Text=_config;
        var chosenModel=Models.SelectedItem as string;
        var models=Rows(state["models"]).Select(row=>row["id"]?.ToString()).OfType<string>().ToArray();
        Models.ItemsSource=models;Models.SelectedItem=models.Contains(chosenModel)?chosenModel:models.FirstOrDefault();
        var chosenClient=(Clients.SelectedItem as Client)?.Id;
        var clients=Rows(state["clients"]).Select(row=>
        {
            var id=row["id"]?.ToString()??"";var revoked=row["revoked_at"] is not null;
            return new Client(id,$"{row["name"]} · {id[..Math.Min(8,id.Length)]} · {(revoked?"Revoked":"Active")}",revoked);
        }).ToArray();
        Clients.ItemsSource=clients;Clients.SelectedItem=clients.FirstOrDefault(c=>c.Id==chosenClient)??clients.FirstOrDefault();
    }
    private async Task RunAsync(string action)
    {
        if(!_active||_busy)return;
        _busy=true;var view=_view; RenderActions();
        try
        {
            await _run(async token=>
            {
                try
                {
                    if(action=="refresh"){await RefreshAsync(view,token);return;}
                    if(action=="reveal")
                    {
                        Token.RevealPassword=!Token.RevealPassword;Set(_controls["reveal"],"Content",Token.RevealPassword?"Hide":"Show");return;
                    }
                    if(action.StartsWith("copy-",StringComparison.Ordinal))
                    {
                        var value=action switch{"copy-base"=>_base,"copy-model"=>Models.SelectedItem as string,"copy-config"=>_config,"copy-token"=>_token,_=>null};
                        if(value is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)throw new InvalidOperationException("Clipboard is unavailable.");
                        await clipboard.SetTextAsync(value);if(_active && view==_view) Message("Copied.");return;
                    }
                    string result;
                    switch(action)
                    {
                        case "enable":
                            await _api.PostAsync("external",new {enabled=!_enabled},token);result="Listener settings saved.";break;
                        case "connect":
                            var installed=await _api.PostAsync("external/opencode",new {name="OpenCode"},token);
                            result=$"OpenCode provider {installed["provider_id"]} · {installed["credential_status"]} · {installed["config_path"]}";break;
                        case "issue":
                            var name=NameField.Text?.Trim();if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("Enter a client name.");
                            var created=await _api.PostAsync("external/clients",new {name},token);
                            if(_active && view==_view)
                            {
                                ForgetToken();_token=created["token"]?.ToString()??throw new InvalidDataException("The service did not return a new credential.");
                                Token.Text=_token;_bindings.Set("HasOneTimeToken",true);
                            }
                            result="Credential created.";break;
                        case "revoke":
                            if(Clients.SelectedItem is not Client selected || selected.Revoked)return;
                            if(_revoke!=selected.Id){_revoke=selected.Id;Message("Select Revoke again to confirm removal of "+selected.Label+".");return;}
                            await _api.PostAsync("external/clients/"+Uri.EscapeDataString(selected.Id)+"/revoke",new {},token);
                            _revoke=null;ForgetToken();result="Credential revoked.";break;
                        default:throw new ArgumentOutOfRangeException(nameof(action));
                    }
                    await RefreshAsync(view,token);if(_active && view==_view)Message(result);
                }
                catch(Exception error) when(error is IOException or HttpRequestException or OperationCanceledException or InvalidOperationException)
                { if(_active && view==_view)Message(error.Message); }
            },_lifetime.Token);
        }
        catch(ObjectDisposedException) when(_lifetime.IsCancellationRequested) { }
        finally
        {
            _busy=false;
            if(!_lifetime.IsCancellationRequested)
            {
                RenderActions();
                if(_active && view!=_view) _=RunAsync("refresh");
            }
        }
    }
    public void Dispose(){Deactivate();_lifetime.Cancel();_loader.Dispose();_lifetime.Dispose();Content=null;}
}
