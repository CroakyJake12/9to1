using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulcheSwarm
{
    private readonly Dictionary<string,(string Model,string Profile,DateTimeOffset At)> _placementRead = new();

    private static string PlacementText(JsonNode record)
    {
        var requested=record["configured"];
        var observed=record["observed"];
        var lines=new List<string>();
        if(requested is null) return "Placement has not been reported by this backend.";
        lines.Add("Configured · "+string.Join(" · ",new[]{Str(requested,"engine"),Str(requested,"gpu"),Str(requested,"label")}.Where(s=>s.Length>0)));
        var limits=PlacementFields(requested,[("gpu_layers","GPU layers",""),("vram_ceiling_mib","VRAM ceiling"," MiB"),
            ("estimated_cpu_weight_mib","CPU-weight upper bound"," MiB"),("estimated_gpu_weight_mib","GPU-weight upper bound"," MiB")]);
        if(limits.Length>0) lines.Add(limits);
        if(observed is null || observed["profile_matches"]?.GetValue<bool>()!=true)
            lines.Add("Observed · no matching loaded-profile measurement.");
        else
        {
            var measured=PlacementFields(observed,[("host_expert_arena_mib","host expert arena"," MiB"),("gpu_expert_cache_mib","GPU expert cache"," MiB"),
                ("model_vram_mib","model VRAM"," MiB"),("cpu_weight_buffer_mib","CPU-mapped weight buffer"," MiB"),
                ("gpu_weight_buffer_mib","GPU weight buffer"," MiB"),("offloaded_layers","offloaded layers",""),
                ("total_offload_layers","offloadable layers",""),("kv_resident_tokens","resident KV"," tokens")]);
            lines.Add("Observed · "+Str(observed,"state")+" · "+(measured.Length>0?measured:"allocation counters unavailable"));
        }
        return string.Join("\n",lines);
    }

    private static string PlacementFields(JsonNode? record,(string Key,string Label,string Unit)[] fields) =>
        string.Join(" · ",fields.Where(f=>record?[f.Key] is not null)
            .Select(f=>$"{f.Label} {Str(record,f.Key)}{f.Unit}"));

    private async Task PreviewPlacementAsync(string who,CancellationToken token)
    {
        var model=Selected(who+"-model");
        var profile=Selected(who+"-profile");
        if(model is null || profile is null) return;
        if(_placementRead.TryGetValue(who,out var prior) && prior.Model==model.Id && prior.Profile==profile.Id &&
            DateTimeOffset.UtcNow-prior.At<TimeSpan.FromSeconds(10)) return;
        _placementRead[who]=(model.Id,profile.Id,DateTimeOffset.UtcNow);
        try
        {
            var placement=await _client.PostAsync("swarms/placement",new{model=model.Id,profile=profile.Id,role=who},token);
            if(Selected(who+"-model")?.Id==model.Id && Selected(who+"-profile")?.Id==profile.Id)
                Label(who+"-placement",PlacementText(placement));
        }
        catch(HttpRequestException error)
        {
            if(Selected(who+"-model")?.Id==model.Id && Selected(who+"-profile")?.Id==profile.Id)
                Label(who+"-placement","Placement unavailable · "+error.Message);
        }
    }

    private async Task FindPresetsAsync(CancellationToken token)
    {
        var presets=new List<Choice>();
        var errors=new List<string>();
        var models=Select("mentor-model").Items.OfType<Choice>().ToArray();
        foreach(var model in models)
        {
            var profiles=await _client.GetAsync("models/"+Uri.EscapeDataString(model.Id)+"/profiles",token);
            foreach(var profile in Rows(profiles["items"]))
            {
                var id=Str(profile,"id");
                try
                {
                    var report=await _client.PostAsync("swarms/placement",new {model=model.Id,profile=id,role="candidate"},token);
                    var cfg=report["configured"];var kind=Str(cfg,"kind");
                    if(kind is not ("native_strata_expert_cache" or "partial_gpu_layers" or "cpu")) continue;
                    var label=$"{model.Label} · {Str(profile,"name")} · {Str(cfg,"label")}";
                    presets.Add(new(model.Id+"/"+id,label,new JsonObject{
                        ["model"]=model.Id, ["profile"]=id, ["kind"]=kind}));
                }
                catch(HttpRequestException error)
                {
                    // Unsupported or insufficient-context profiles cannot enter a swarm.
                    if((int?)error.StatusCode!=422) errors.Add(model.Label+" · "+error.Message);
                }
            }
        }
        var sorted=presets.OrderBy(p=>Str(p.Record,"kind")=="native_strata_expert_cache"?0:1).ThenBy(p=>p.Label).ToArray();
        foreach(var who in new[]{"mentor","worker"})
        {
            Choices(who+"-ram-preset",sorted);
            Enabled(who+"-apply-preset",sorted.Length>0);
        }
        Label("status",$"{sorted.Length} supported RAM-backed profiles."+(errors.Count==0?"":" "+errors[0]));
    }
    private async Task ApplyPlacementPresetAsync(string who,CancellationToken token)
    {
        var preset=Selected(who+"-ram-preset")?.Record??throw new InvalidOperationException("Find and choose a RAM-backed preset first.");
        Choose(who+"-model",Str(preset,"model"));
        await ReadProfilesAsync(who,token);
        Choose(who+"-profile",Str(preset,"profile"));
        _placementRead.Remove(who);
        await PreviewPlacementAsync(who,token);
        UpdateBudgets();
        Label("status","Applied to the "+who+" configuration.");
    }
}
