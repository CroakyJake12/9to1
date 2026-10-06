using System.Text.Json;
namespace Haven.Application;

/// <summary>A separate fixed read of the SAME privately compiled Task/Run key. A key's
/// actual 404 is accepted only between successful exact namespace observations. Neither
/// an arbitrary 404 nor a namespace/configuration DTO establishes resource ownership.</summary>
public static class CloudflareRawTaskMarkerAbsenceTransport
{
    public const string OriginalReadBody = """
async()=>{
 const errors=[];
 async function bounded(response,limit){
  let reader=null;const chunks=[];let length=0;
  try{if(response.body===null)throw new Error('CF_ABSENCE_BODY_MISSING');reader=response.body.getReader();
   for(;;){const part=await reader.read();if(part.done)break;
    if(!(part.value instanceof Uint8Array))throw new Error('CF_ABSENCE_INVALID_CHUNK');
    length+=part.value.byteLength;if(length>limit)throw new Error('CF_ABSENCE_OVERSIZE');chunks.push(part.value.slice());}
  }catch(error){errors.push(error);}
  finally{if(reader!==null){try{await reader.cancel();}catch(error){errors.push(error);}try{reader.releaseLock();}catch(error){errors.push(error);}}
   else if(response.body!==null){try{await response.body.cancel();}catch(error){errors.push(error);}}}
  if(errors.length!==0)throw new AggregateError(errors,'CF_ABSENCE_READ_OR_CLEANUP_FAILED');
  const bytes=new Uint8Array(length);let at=0;for(const part of chunks){bytes.set(part,at);at+=part.length;}return bytes;
 }
 async function namespace(){
  const response=await fetch(__NAMESPACE_URL__,{method:'GET',redirect:'error'});
  const bytes=await bounded(response,16384);
  if(response.status!==200||!response.ok)throw new Error('CF_ABSENCE_NAMESPACE_UNCONFIRMED');
  const value=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(bytes));
  if(value.success!==true||value.result===null||typeof value.result!=='object'||value.result.id!==__NAMESPACE_ID__||
     typeof value.result.title!=='string'||value.result.title.length>200)throw new Error('CF_ABSENCE_NAMESPACE_UNCONFIRMED');
  return value.result.title;
 }
 const before=await namespace();
 const response=await fetch(__KEY_URL__,{method:'GET',redirect:'error'});
 const status=response.status;const ok=response.ok;
 if(response.body!==null){try{await response.body.cancel();}catch(error){errors.push(error);}}
 if(errors.length!==0)throw new AggregateError(errors,'CF_ABSENCE_KEY_CLEANUP_FAILED');
 if(status!==404||ok)throw new Error('CF_TASK_MARKER_ABSENCE_UNCONFIRMED');
 const after=await namespace();if(after!==before)throw new Error('CF_ABSENCE_NAMESPACE_CHANGED');
 return {operation_key:__OPERATION_KEY__,ok:true,status:404,data:{absent:true,namespace_id:__NAMESPACE_ID__}};
}
""";
    internal static string CompileOriginal(CloudflareSavedService service, string namespacePath,
        string keyPath, string namespaceId, string operationKey)
    {
        if (!CloudflareRawTaskMarkerTransport.IsSupportedSavedService(service))
            throw new CloudflareSetupRequiredException(CloudflareSetupStage.RawValueTransportUnsupported,
                "CF_OFFICIAL_RAW_TRANSPORT_REQUIRED", "Review the saved official Cloudflare MCP connection before verifying marker absence.");
        return OriginalReadBody.Replace("__NAMESPACE_URL__", JsonSerializer.Serialize("https://api.cloudflare.com/client/v4" + namespacePath), StringComparison.Ordinal)
            .Replace("__KEY_URL__", JsonSerializer.Serialize("https://api.cloudflare.com/client/v4" + keyPath), StringComparison.Ordinal)
            .Replace("__NAMESPACE_ID__", JsonSerializer.Serialize(namespaceId), StringComparison.Ordinal)
            .Replace("__OPERATION_KEY__", JsonSerializer.Serialize(operationKey), StringComparison.Ordinal);
    }
    internal static void DemandOriginalResponse(CloudflareCompiledInvocation original, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 2 ||
            !data.TryGetProperty("absent", out var absent) || absent.ValueKind != JsonValueKind.True ||
            !data.TryGetProperty("namespace_id", out var ns) || ns.ValueKind != JsonValueKind.String || ns.GetString() != original.NamespaceId)
            throw new InvalidOperationException("The SAME privately compiled marker key absence is unconfirmed.");
    }
}
