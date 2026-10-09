using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
namespace NineToOne.Accounts.Oidc;

// Exact maintained EdDSA/Ed25519 path; unchanged RSA reader remains separate.
public sealed class Ed25519IssuerReader(IApprovedIssuerKeysSource keys,TimeProvider clock):IVerifiedIssuerTokenReader
{
 public async ValueTask<VerifiedToken?> VerifyAsync(string raw,TokenPolicy policy,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  if(raw.Length is <1 or >16384||!policy.AllowedAlgorithms.SetEquals(new[]{"EdDSA"}))return null;
  var snapshot=await NineToOne.Accounts.Native.NativeCakeErrors.Await(keys.ReadAsync(policy.Issuer,ct).AsTask()).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
  if(snapshot is null||snapshot.Issuer!=policy.Issuer||snapshot.FreshUntil<=clock.GetUtcNow()||snapshot.JwksJson.Length>65536)return null;
  try
  {
   var parts=raw.Split('.');if(parts.Length!=3)return null;
   using var header=JsonDocument.Parse(Decode(parts[0]),new JsonDocumentOptions{MaxDepth=4});
   using var payload=JsonDocument.Parse(Decode(parts[1]),new JsonDocumentOptions{MaxDepth=8});
   using var jwks=JsonDocument.Parse(snapshot.JwksJson,new JsonDocumentOptions{MaxDepth=8});
   if(!Unique(header.RootElement)||!Unique(payload.RootElement)||!Unique(jwks.RootElement))return null;
   var h=header.RootElement;var p=payload.RootElement;var kid=Text(h,"kid");
   if(Text(h,"alg")!="EdDSA"||kid is not {Length: >0 and <=256}||h.TryGetProperty("crit",out _)||h.TryGetProperty("b64",out _)||h.TryGetProperty("jwk",out _)||h.TryGetProperty("jku",out _)||h.TryGetProperty("x5u",out _))return null;
   if(!jwks.RootElement.TryGetProperty("keys",out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength() is <1 or >16)return null;
   JsonElement? selected=null;var kids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var key in list.EnumerateArray())
   {var id=Text(key,"kid");if(key.ValueKind!=JsonValueKind.Object||id is not {Length: >0 and <=256}||!kids.Add(id)||key.TryGetProperty("d",out _)||key.TryGetProperty("k",out _))return null;if(id==kid)selected=key;}
   if(selected is null)return null;var k=selected.Value;var x=Text(k,"x");
   if(Text(k,"kty")!="OKP"||Text(k,"crv")!="Ed25519"||x is null||k.TryGetProperty("alg",out _)&&Text(k,"alg")!="EdDSA"||k.TryGetProperty("use",out _)&&Text(k,"use")!="sig")return null;
   if(k.TryGetProperty("key_ops",out var ops)&&(ops.ValueKind!=JsonValueKind.Array||ops.GetArrayLength()!=1||ops[0].ValueKind!=JsonValueKind.String||ops[0].GetString()!="verify"))return null;
   var keyBytes=Decode(x);var signature=Decode(parts[2]);if(keyBytes.Length!=32||signature.Length!=64)return null;
   var signer=new Ed25519Signer();signer.Init(false,new Ed25519PublicKeyParameters(keyBytes));
   var signed=Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]);signer.BlockUpdate(signed,0,signed.Length);if(!signer.VerifySignature(signature))return null;
   var issuer=Text(p,"iss");var subject=Text(p,"sub");if(issuer!=policy.Issuer||!CanonicalUuid(subject))return null;
   var audiences=new HashSet<string>(StringComparer.Ordinal);if(!p.TryGetProperty("aud",out var aud))return null;
   if(aud.ValueKind==JsonValueKind.String){var a=aud.GetString();if(a is not {Length: >0 and <=2048})return null;audiences.Add(a);}
   else if(aud.ValueKind==JsonValueKind.Array&&aud.GetArrayLength() is >0 and <=8)
   {foreach(var a in aud.EnumerateArray()){var value=a.ValueKind==JsonValueKind.String?a.GetString():null;if(value is not {Length: >0 and <=2048}||!audiences.Add(value))return null;}}
   else return null;
   if(!audiences.Contains(policy.Audience)||!Number(p,"exp",out var exp)||!Number(p,"iat",out var iat)||exp<=iat||exp-iat>600)return null;
   var nbf=iat;if(p.TryGetProperty("nbf",out _)&&!Number(p,"nbf",out nbf))return null;
   var now=clock.GetUtcNow().ToUnixTimeSeconds();if(iat>now||nbf>now||exp<=now)return null;
   var nonce=Text(p,"nonce");var azp=Text(p,"azp");
   if(policy.Purpose==TokenPurpose.ApiAccessToken)
   {var client=Text(p,"client_id");if(azp is not null&&client is not null&&azp!=client)return null;azp??=client;}
   if(policy.Purpose==TokenPurpose.IdToken&&(policy.ExpectedNonce is null||nonce!=policy.ExpectedNonce||audiences.Count>1&&azp!=policy.Audience||azp is not null&&azp!=policy.Audience))return null;
   var sid=Text(p,"sid");if(policy.Purpose==TokenPurpose.ApiAccessToken&&!CanonicalUuid(sid))return null;
   var scopes=new HashSet<string>((Text(p,"scope")??"").Split(' ',StringSplitOptions.RemoveEmptyEntries),StringComparer.Ordinal);if(!policy.RequiredScopes.IsSubsetOf(scopes))return null;
   ct.ThrowIfCancellationRequested();return new(issuer!,subject!,audiences,DateTimeOffset.FromUnixTimeSeconds(exp),DateTimeOffset.FromUnixTimeSeconds(nbf),nonce,azp,sid,null,scopes,DateTimeOffset.FromUnixTimeSeconds(iat));
  }
  catch(Exception e)when(e is JsonException or FormatException or ArgumentException or OverflowException){return null;}
 }
 internal static bool CanonicalUuid(string? t)=>t is {Length:36}&&Guid.TryParseExact(t,"D",out var id)&&id!=Guid.Empty&&id.ToString("D")==t&&t[14] is >= '1' and <= '8'&&"89ab".Contains(t[19]);
 private static string? Text(JsonElement e,string name)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
 private static bool Number(JsonElement p,string name,out long value){value=0;return p.TryGetProperty(name,out var n)&&n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out value)&&value>=0&&value<=253402300799L;}
 private static bool Unique(JsonElement e)
 {if(e.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in e.EnumerateObject())if(!names.Add(p.Name)||!Unique(p.Value))return false;}else if(e.ValueKind==JsonValueKind.Array){foreach(var v in e.EnumerateArray())if(!Unique(v))return false;}return true;}
 private static byte[] Decode(string value)
 {if(value.Length==0||value.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '-' or '_')))throw new FormatException("Invalid base64url");var b=Convert.FromBase64String(value.Replace('-','+').Replace('_','/')+new string('=',(4-value.Length%4)%4));if(Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_')!=value)throw new FormatException("Noncanonical base64url");return b;}
}
