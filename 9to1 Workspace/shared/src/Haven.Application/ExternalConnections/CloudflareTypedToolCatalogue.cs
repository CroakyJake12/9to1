using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

public static class CloudflareTypedToolCatalogue
{
    private static readonly ConditionalWeakTable<CloudflareCompiledInvocation, object> Issued = new();
    // Positive completeness proof from maintained page/per_page/count/total_count metadata.
    public const string OriginalCompleteInventoryExpression = "before.success===true&&Array.isArray(before.result)&&before.result.length<=500&&Number.isSafeInteger(before.result_info?.page)&&before.result_info.page===1&&Number.isSafeInteger(before.result_info?.per_page)&&before.result_info.per_page>0&&before.result_info.per_page<=500&&Number.isSafeInteger(before.result_info?.count)&&before.result_info.count===before.result.length&&before.result_info.per_page>=before.result_info.count&&Number.isSafeInteger(before.result_info?.total_count)&&before.result_info.total_count===before.result_info.count&&before.result.every(x=>x!==null&&typeof x==='object'&&typeof x.id==='string'&&/^[a-f0-9]{32}$/.test(x.id)&&typeof x.title==='string'&&x.title.length<=200)";
    public static IReadOnlyList<CloudflareToolDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
    {
        new CloudflareToolDescriptor("cloudflare_kv_list", "cloudflare.kv.list", CloudflareOperationKind.KvList, true, true),
        new CloudflareToolDescriptor("cloudflare_kv_inspect", "cloudflare.kv.inspect", CloudflareOperationKind.KvInspect, true, true),
        new CloudflareToolDescriptor("cloudflare_kv_create_isolated", "cloudflare.kv.create", CloudflareOperationKind.KvCreate, false, true),
        new CloudflareToolDescriptor("cloudflare_kv_put_task_marker", "cloudflare.kv.marker.put", CloudflareOperationKind.KvMarkerPut, false, true),
        new CloudflareToolDescriptor("cloudflare_kv_get_task_marker", "cloudflare.kv.marker.get", CloudflareOperationKind.KvMarkerGet, true, true),
        new CloudflareToolDescriptor("cloudflare_kv_delete_task_marker", "cloudflare.kv.marker.delete", CloudflareOperationKind.KvMarkerDelete, false, true),
        new CloudflareToolDescriptor("cloudflare_kv_verify_task_marker_absent", "cloudflare.kv.marker.verifyAbsent", CloudflareOperationKind.KvMarkerVerifyAbsent, true, true),
        new CloudflareToolDescriptor("cloudflare_kv_delete_isolated", "cloudflare.kv.delete", CloudflareOperationKind.KvDelete, false, true),
        new CloudflareToolDescriptor("cloudflare_workers_read", "cloudflare.workers.read", CloudflareOperationKind.WorkerRead, true, false),
        new CloudflareToolDescriptor("cloudflare_workers_deploy", "cloudflare.workers.deploy", CloudflareOperationKind.WorkerDeploy, false, false),
        new CloudflareToolDescriptor("cloudflare_dns_read", "cloudflare.dns.read", CloudflareOperationKind.DnsRead, true, false),
        new CloudflareToolDescriptor("cloudflare_dns_write", "cloudflare.dns.write", CloudflareOperationKind.DnsWrite, false, false),
        new CloudflareToolDescriptor("cloudflare_secret_names_read", "cloudflare.secrets.names", CloudflareOperationKind.SecretNamesRead, true, false),
        new CloudflareToolDescriptor("cloudflare_secret_write_by_reference", "cloudflare.secrets.write", CloudflareOperationKind.SecretWriteByReference, false, false),
        new CloudflareToolDescriptor("cloudflare_account_wide", "cloudflare.account", CloudflareOperationKind.AccountWide, false, false),
        new CloudflareToolDescriptor("cloudflare_destructive", "cloudflare.destructive", CloudflareOperationKind.Destructive, false, false)
    });
    public static bool IsIssuedOriginal(CloudflareCompiledInvocation invocation) => Issued.TryGetValue(invocation, out _);
    public static bool IsHexId(string value) => value.Length == 32 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static CloudflareCompiledInvocation CompileOriginal(CloudflareSavedService service, Guid taskId, Guid executionId, Guid actionId, OllamaToolCall call)
    {
        ArgumentNullException.ThrowIfNull(service); ArgumentNullException.ThrowIfNull(call);
        if (!IsHexId(service.AccountId) || taskId == Guid.Empty || executionId == Guid.Empty || actionId == Guid.Empty) throw new ArgumentException("Actual account/task/run/action binding required.");
        var descriptor = Descriptors.SingleOrDefault(x => x.ToolName == call.Name) ?? throw new NotSupportedException("Unknown typed Cloudflare operation.");
        if (!descriptor.IsImplemented) throw new NotSupportedException("This distinct Cloudflare operation has no connected owning producer.");
        bool hasNamespace = descriptor.Kind is not (CloudflareOperationKind.KvList or CloudflareOperationKind.KvCreate);
        var expected = hasNamespace ? new[] { "namespace_id" } : Array.Empty<string>();
        if (call.Arguments.Count != expected.Length || call.Arguments.Keys.Any(x => !expected.Contains(x, StringComparer.Ordinal)))
            throw new ArgumentException("Only declared typed arguments are allowed; code, paths, credentials and account overrides are forbidden.");
        string? ns = hasNamespace ? call.Arguments["namespace_id"].GetString() : null;
        if (hasNamespace && (ns is null || !IsHexId(ns))) throw new ArgumentException("Canonical namespace identity required.");
        var digest = WorkspaceToolOriginalDigest.Call(call);
        var key = taskId.ToString("N") + ":" + executionId.ToString("N") + ":" + actionId.ToString("N");
        var taskRunDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(taskId.ToString("N") + ":" + executionId.ToString("N")))).ToLowerInvariant();
        var marker = JsonSerializer.Serialize(new { taskId, executionId, taskRunDigest });
        var root = "/accounts/" + service.AccountId + "/storage/kv/namespaces";
        var path = ns is null ? root : root + "/" + ns;
        string method, body = "", query = "", projection;
        switch (descriptor.Kind)
        {
            case CloudflareOperationKind.KvList:
                method = "GET"; query = ",query:{per_page:500,page:1}";
                projection = "Array.isArray(r.result)?r.result.map(x=>({id:x.id,title:x.title})):null"; break;
            case CloudflareOperationKind.KvInspect:
                method = "GET"; projection = "r.result?{id:r.result.id,title:r.result.title}:null"; break;
            case CloudflareOperationKind.KvCreate:
                method = "POST";
                body = ",body:{title:" + JsonSerializer.Serialize("9to1-safety-net-" + taskId.ToString("N")) + "}";
                projection = "r.result?{id:r.result.id,title:r.result.title}:null"; break;
            case CloudflareOperationKind.KvMarkerPut:
            case CloudflareOperationKind.KvMarkerGet:
            case CloudflareOperationKind.KvMarkerDelete:
            case CloudflareOperationKind.KvMarkerVerifyAbsent:
                path += "/values/" + Uri.EscapeDataString("9to1-task/" + taskId.ToString("N") + "/" + executionId.ToString("N") + "/proof");
                method = descriptor.Kind == CloudflareOperationKind.KvMarkerPut ? "PUT" : descriptor.Kind is CloudflareOperationKind.KvMarkerGet or CloudflareOperationKind.KvMarkerVerifyAbsent ? "GET" : "DELETE";
                if (method == "PUT") { body = ",body:" + JsonSerializer.Serialize(marker) + ",contentType:'application/octet-stream',rawBody:true"; query = ",query:{expiration_ttl:120}"; }
                // Raw GET uses the separate fixed official fetch/body-byte compiler below.
                // No generic response result string is used as marker byte proof.
                projection = "null"; break;
            default: method = "DELETE"; projection = "null"; break;
        }
        var code = "async()=>{const r=await cloudflare.request({method:" + JsonSerializer.Serialize(method) + ",path:" + JsonSerializer.Serialize(path) + body + query + "});return {operation_key:" + JsonSerializer.Serialize(key) + ",ok:r.success===true,status:r.status,data:r.success===true?(" + projection + "):null};}";
        if (descriptor.Kind == CloudflareOperationKind.KvCreate)
        {
            // One SDK POST declaration follows complete preflight. Maintained transport may
            // retry network faults/429 internally, so a create never proves all effects known.
            // A previous/unknown same-title result never authorizes another SDK create.
            var title = JsonSerializer.Serialize("9to1-safety-net-" + taskId.ToString("N"));
            var preflight = "const before=await cloudflare.request({method:'GET',path:" + JsonSerializer.Serialize(root) + ",query:{per_page:500,page:1}});" +
                "if(!(" + OriginalCompleteInventoryExpression + ")||before.result.some(x=>x.title===" + title + "))" +
                "return {operation_key:" + JsonSerializer.Serialize(key) + ",ok:false,status:409,data:null};";
            code = code.Replace("async()=>{const r=", "async()=>{" + preflight + "const r=", StringComparison.Ordinal);
        }
        if (descriptor.Kind == CloudflareOperationKind.KvMarkerGet)
            code = CloudflareRawTaskMarkerTransport.CompileOriginal(service, path, marker, key);
        if (descriptor.Kind == CloudflareOperationKind.KvMarkerVerifyAbsent)
            code = CloudflareRawTaskMarkerAbsenceTransport.CompileOriginal(service, root + "/" + ns, path, ns!, key);
        var invocation = new CloudflareCompiledInvocation(service, descriptor, taskId, executionId, actionId, digest, key, ns, code, marker);
        Issued.Add(invocation, new()); return invocation;
    }

    public static JsonElement DemandBoundedResponse(CloudflareCompiledInvocation original, JsonElement envelope)
    {
        if (!IsIssuedOriginal(original) || envelope.ValueKind != JsonValueKind.Object || envelope.GetRawText().Length > 128_000 ||
            envelope.EnumerateObject().Count() != 4 || !envelope.TryGetProperty("operation_key", out var key) || key.GetString() != original.OperationKey ||
            !envelope.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !envelope.TryGetProperty("status", out var status) || !status.TryGetInt32(out var code) || (original.Descriptor.Kind == CloudflareOperationKind.KvMarkerVerifyAbsent ? code != 404 : code is < 200 or > 299) || !envelope.TryGetProperty("data", out var data))
            throw new InvalidOperationException("The exact compiled operation response is unconfirmed; reconcile before any write retry.");
        if (original.Descriptor.Kind is CloudflareOperationKind.KvCreate or CloudflareOperationKind.KvInspect)
        {
            if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 2 || !data.TryGetProperty("id", out var id) || !IsHexId(id.GetString() ?? "") ||
                !data.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 200 ||
                (original.NamespaceId is not null && id.GetString() != original.NamespaceId) ||
                (original.Descriptor.Kind == CloudflareOperationKind.KvCreate && title.GetString() != "9to1-safety-net-" + original.TaskId.ToString("N")))
                throw new InvalidOperationException("The isolated resource identity is unconfirmed.");
        }
        else if (original.Descriptor.Kind == CloudflareOperationKind.KvList)
        {
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 500) throw new InvalidOperationException("Unexpected namespace list.");
            foreach (var item in data.EnumerateArray())
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 || !item.TryGetProperty("id", out var id) || !IsHexId(id.GetString() ?? "") ||
                    !item.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 200) throw new InvalidOperationException("Unexpected namespace metadata.");
        }
        else if (original.Descriptor.Kind == CloudflareOperationKind.KvMarkerGet)
        {
            CloudflareRawTaskMarkerTransport.DemandOriginalResponse(original, data);
        }
        else if (original.Descriptor.Kind == CloudflareOperationKind.KvMarkerVerifyAbsent)
        {
            CloudflareRawTaskMarkerAbsenceTransport.DemandOriginalResponse(original, data);
        }
        else if (data.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Unexpected remote content.");
        return data.Clone();
    }
}
