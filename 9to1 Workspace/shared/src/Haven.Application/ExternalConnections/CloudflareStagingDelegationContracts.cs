using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>Explicit nonsecret selection, not namespace ownership or an operation grant.</summary>
public sealed record CloudflareStagingBindingSelection(string WorkerName, string BindingName);
public sealed record CloudflareWorkerBindingObservation(string NamespaceId, JsonElement OriginalProjection,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<Task> OriginalTasks,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<Exception> OriginalErrors);
public interface ICloudflareOriginalWorkerReadEntry : IAsyncDisposable
{
    T RunOriginalRead<T>(Func<T> finiteOriginalReadStart, CancellationToken cancellationToken);
}
public interface ICloudflareOriginalWorkerReadAuthority
{
    ValueTask<ICloudflareOriginalWorkerReadEntry> EnterOriginalWorkerReadAsync(CloudflareSavedService sameService,
        CloudflareStagingBindingSelection sameSelection, CancellationToken cancellationToken);
    void DemandOriginalWorkerReadEntry(CloudflareSavedService sameService,
        CloudflareStagingBindingSelection sameSelection, ICloudflareOriginalWorkerReadEntry sameEntry);
}
public interface ICloudflareWorkerBindingClient
{
    Task<CloudflareWorkerBindingObservation> ReadOriginalWorkerBindingAsync(CloudflareSavedService sameService,
        CloudflareStagingBindingSelection sameSelection, ICloudflareOriginalWorkerReadAuthority sameAuthority,
        Action<Action>? originalCallerScope,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalWorkerBinding(CloudflareSavedService sameService,
        CloudflareStagingBindingSelection sameSelection, CloudflareWorkerBindingObservation sameObservation);
}
public interface ICloudflareOriginalStagingReview : IAsyncDisposable
{
    string RequestId { get; }
    Task<CloudflareSetupObservation> SubmitOriginalAsync(CancellationToken cancellationToken);
    Task<CloudflareSetupObservation> CommitOriginalAsync(CancellationToken cancellationToken);
}
public interface ICloudflareStagingDelegationSource
{
    Task<ICloudflareOriginalStagingReview> PrepareStagingDelegationAsync(
        CloudflareStagingBindingSelection explicitSelection, long expectedRevision,
        CancellationToken cancellationToken);
    void RequestOriginalStagingRetirement();
    void DemandExternalOriginalStagingJoin();
    Task CloseAndDrainOriginalStagingReviewsAsync();
}
/// <summary>Optional actual Home permission extension. SDK calls before taking its connection gate;
/// the actual binding read owns a separate finite entry and releases it before awaiting network.
/// A fresh read observes current metadata; it is not atomic exclusion of later remote changes.</summary>
public interface ICloudflareOriginalBorrowedBindingPermission
{
    Task RevalidateOriginalBorrowedBindingAsync(CloudflareOriginalTaskBinding sameBinding,
        ICloudflareWorkerBindingClient sameClient, CancellationToken cancellationToken);
}

public static class CloudflareStagingBindingContract
{
    public static void DemandSelection(CloudflareStagingBindingSelection selection)
    {
        static bool Safe(string value) => value.Length is > 0 and <= 100 &&
            value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        if (!Safe(selection.WorkerName) || !Safe(selection.BindingName))
            throw new ArgumentException("Select an exact supported Worker name and KV binding name.");
    }
    public static string DemandNamespace(JsonElement projection, CloudflareStagingBindingSelection selection)
    {
        DemandSelection(selection);
        if (projection.ValueKind != JsonValueKind.Object || projection.EnumerateObject().Count() != 4 ||
            !projection.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !projection.TryGetProperty("worker", out var worker) || worker.GetString() != selection.WorkerName ||
            !projection.TryGetProperty("binding", out var binding) || binding.GetString() != selection.BindingName ||
            !projection.TryGetProperty("namespace_id", out var ns) || ns.ValueKind != JsonValueKind.String ||
            !CloudflareTypedToolCatalogue.IsHexId(ns.GetString()!))
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.AccountSelectionRequired,
                "CF_STAGING_KV_BINDING_REQUIRED", "The exact configured staging Worker has no unique supported KV binding. Configure and review it explicitly before a marker operation.");
        return ns.GetString()!;
    }
    // Public transport projection contains only these validated nonsecret fields. The raw
    // SDK response remains in actual task custody and is never copied on a refusal path.
    public static JsonElement DetachValidatedOriginalProjection(JsonElement raw, CloudflareStagingBindingSelection selection)
    {
        var namespaceId = DemandNamespace(raw, selection);
        return JsonSerializer.SerializeToElement(new { ok = true, worker = selection.WorkerName, binding = selection.BindingName, namespace_id = namespaceId });
    }
    public static string CompileOriginal(CloudflareSavedService service, CloudflareStagingBindingSelection selection)
    {
        DemandSelection(selection);
        if (!CloudflareTypedToolCatalogue.IsHexId(service.AccountId) || service.ExecuteTool != "execute" ||
            new Uri(JsonSerializer.Deserialize<McpConnectionConfiguration>(service.Connection.ConfigurationJson)?.Endpoint ?? throw new NotSupportedException("The saved MCP configuration is unavailable.")) != new Uri("https://mcp.cloudflare.com/mcp"))
            throw new NotSupportedException("Only the exact reviewed official Cloudflare OAuth execute transport supports this binding read.");
        var path = "/accounts/" + service.AccountId + "/workers/scripts/" + selection.WorkerName + "/settings";
        // Return ONLY the named KV binding. Secret bindings/values and arbitrary settings never leave the isolate.
        return "async()=>{const r=await cloudflare.request({method:'GET',path:" + JsonSerializer.Serialize(path) +
            "});const b=r.success===true&&Array.isArray(r.result?.bindings)?r.result.bindings.filter(x=>x.name===" +
            JsonSerializer.Serialize(selection.BindingName) + "):[];if(b.length!==1||b[0].type!=='kv_namespace'||" +
            "typeof b[0].namespace_id!=='string'||!/^[a-fA-F0-9]{32}$/.test(b[0].namespace_id))throw new Error('CF_STAGING_KV_BINDING_REQUIRED');" +
            "return {ok:true,worker:" + JsonSerializer.Serialize(selection.WorkerName) + ",binding:" +
            JsonSerializer.Serialize(selection.BindingName) + ",namespace_id:b[0].namespace_id};}";
    }
}
