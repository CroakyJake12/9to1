using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>Fixed raw read in the maintained official CodeMode isolate. Its GlobalOutbound
/// injects the saved-service credential outside the code; no token enters this compiler.
/// Uses Response.body bytes rather than Response.text/BOM normalization. All URL, method,
/// expected bytes and bounds are generated from the privately compiled typed operation.</summary>
public static class CloudflareRawTaskMarkerTransport
{
    public const string SupportedEndpoint = "https://mcp.cloudflare.com/mcp";
    public const int MaximumBytes = 1024;
    public const string MaintainedImplementation = "https://github.com/cloudflare/mcp/blob/69ba3143bb8ffa2b3c1f12bfb7536c9ca4c57a2d/src/tools/execute.ts";
    public static bool IsSupportedSavedService(CloudflareSavedService service)
    {
        var actual = JsonSerializer.Deserialize<McpConnectionConfiguration>(service.Connection.ConfigurationJson);
        return service.ExecuteTool == "execute" && actual?.Endpoint == SupportedEndpoint && actual.UseOAuth && !actual.LocalOnly &&
            actual.Transport == McpTransportKind.StreamableHttp && service.Connection.Kind == ExternalConnectionKind.Mcp &&
            service.Connection.IsEnabled && service.Connection.State == ExternalConnectionState.Ready;
    }
    // The same exact fixed body is source-extracted by the owning Node controls. No external
    // request is made by those controls and no generic JSON result type is used as byte proof.
    public const string OriginalReadBody = """
async()=>{
 const response=await fetch(__FIXED_URL__,{method:'GET',redirect:'error'});
 const expected=new TextEncoder().encode(__EXPECTED_MARKER__);
 const errors=[];let reader=null;let chunks=[];let length=0;
 try{
  if(response.status!==200||!response.ok||response.body===null)throw new Error('CF_RAW_MARKER_RESPONSE_UNCONFIRMED');
  reader=response.body.getReader();
  for(;;){const part=await reader.read();if(part.done)break;
   if(!(part.value instanceof Uint8Array))throw new Error('CF_RAW_MARKER_INVALID_CHUNK');
   length+=part.value.byteLength;if(length>1024)throw new Error('CF_RAW_MARKER_OVERSIZE');chunks.push(part.value.slice());
  }
 }catch(error){errors.push(error);}
 finally{if(reader!==null){try{await reader.cancel();}catch(error){errors.push(error);}try{reader.releaseLock();}catch(error){errors.push(error);}}else if(response.body!==null){try{await response.body.cancel();}catch(error){errors.push(error);}}}
 if(errors.length!==0)throw new AggregateError(errors,'CF_RAW_MARKER_READ_OR_CLEANUP_FAILED');
 const bytes=new Uint8Array(length);let at=0;for(const part of chunks){bytes.set(part,at);at+=part.length;}
 const matches=bytes.length===expected.length&&bytes.every((value,index)=>value===expected[index]);
 const digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),value=>value.toString(16).padStart(2,'0')).join('');
 return {operation_key:__OPERATION_KEY__,ok:true,status:response.status,data:{matches,length,sha256:digest}};
}
""";
    internal static string CompileOriginal(CloudflareSavedService service, string ownedPath, string marker, string key)
    {
        if (!IsSupportedSavedService(service)) throw new CloudflareSetupRequiredException(CloudflareSetupStage.RawValueTransportUnsupported,
            "CF_OFFICIAL_RAW_TRANSPORT_REQUIRED", "Review the saved official Cloudflare MCP /mcp connection and exact execute tool before raw marker reads.");
        if (marker.Any(value => value > 127) || Encoding.UTF8.GetByteCount(marker) > MaximumBytes)
            throw new ArgumentException("The generated nonpersonal marker must be bounded ASCII.");
        // ownedPath comes only from the closed compiler's validated account/namespace/task/run.
        return OriginalReadBody.Replace("__FIXED_URL__", JsonSerializer.Serialize("https://api.cloudflare.com/client/v4" + ownedPath), StringComparison.Ordinal)
            .Replace("__EXPECTED_MARKER__", JsonSerializer.Serialize(marker), StringComparison.Ordinal)
            .Replace("__OPERATION_KEY__", JsonSerializer.Serialize(key), StringComparison.Ordinal);
    }
    internal static void DemandOriginalResponse(CloudflareCompiledInvocation original, JsonElement data)
    {
        var expected = Encoding.UTF8.GetBytes(original.Marker); var hash = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 3 ||
            !data.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.True ||
            !data.TryGetProperty("length", out var length) || !length.TryGetInt32(out var count) || count != expected.Length ||
            !data.TryGetProperty("sha256", out var digest) || digest.ValueKind != JsonValueKind.String || digest.GetString() != hash)
            throw new InvalidOperationException("The actual bounded raw bytes do not match the SAME nonpersonal Task/Run marker.");
    }
}
