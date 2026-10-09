#if !ANDROID
using System.Text.Json;
using NineToOne.Accounts.Native;
using NineToOne.Accounts.Remote;
using NineToOne.Web.Services;
namespace Haven.Desktop.Accounts;

/// <summary>Native presentation adapter. CAKE bearer stays inside the SAME memory session.
/// Wire envelopes match the existing platform-neutral account bindings, never a Home profile.</summary>
public sealed class NativeAccountTransport(INativeCakeAccountSession session):IAccountBrowserTransport
{
 public async Task<JsonElement> InvokeAsync(string action,JsonElement? arguments,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  switch(action)
  {
   case "GetCurrent":
    var c=await session.CurrentAsync(ct).ConfigureAwait(false);
    return c.Failure==ApiFailure.None&&c.Value is {} current?Ok(new{accountId=current.AccountID,displayName=current.DisplayName}):Fail(c.Failure);
   case "GetProfile":
    var p=await session.ProfileAsync(ct).ConfigureAwait(false);
    return p.Failure==ApiFailure.None&&p.Value is {} profile?Ok(new{profile=new{accountId=profile.AccountID,name=profile.Name,username=profile.Username,icon=profile.Icon,pronouns=profile.Pronouns,job=profile.Job,revision=profile.Revision}}):Fail(p.Failure);
   case "ListSessions":
    var s=await session.SessionsAsync(ct).ConfigureAwait(false);
    return s.Failure==ApiFailure.None&&s.Value is {} sessions?Ok(new{sessions=sessions.Select(x=>new{sessionId=x.SessionID,accountId=x.AccountID,deviceName=x.DeviceName,createdAt=x.CreatedAt,expiresAt=x.ExpiresAt,revokedAt=x.RevokedAt,registeredClientId=x.RegisteredClientID}).ToArray()}):Fail(s.Failure);
   case "SignOut":return Mutation(await session.SignOutAsync(ct).ConfigureAwait(false));
   case "RevokeAllOtherSessions":return Mutation(await session.RevokeOtherSessionsAsync(ct).ConfigureAwait(false));
   case "RevokeSession":
    if(arguments is not {ValueKind:JsonValueKind.Object} a||!a.TryGetProperty("sessionId",out var id)||id.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(id.GetString(),"D",out var selected)||selected==Guid.Empty||selected.ToString("D")!=id.GetString())return Fail(ApiFailure.InvalidInput);
    return Mutation(await session.RevokeSessionAsync(selected,ct).ConfigureAwait(false));
   case "UpdateProfile":return Fail(ApiFailure.PermissionDenied); // Genuine native registration has read-only profile scope.
   default:return Fail(ApiFailure.InvalidInput);
  }
 }
 private static JsonElement Ok(object body)=>JsonSerializer.SerializeToElement(new{ok=true,status=200,body});
 private static JsonElement Mutation(ApiResult<RemoteMutationAcknowledgement> result)=>result.Failure==ApiFailure.None&&result.Value?.Acknowledged==true?JsonSerializer.SerializeToElement(new{ok=true,status=204,body=(object?)null}):Fail(result.Failure);
 private static JsonElement Fail(ApiFailure failure)
 {
  var (status,code)=failure switch
  {ApiFailure.InvalidToken=>(401,"AuthenticationRequired"),ApiFailure.PermissionDenied=>(403,"PermissionDenied"),ApiFailure.InvalidInput=>(400,"InvalidArgument"),ApiFailure.Conflict=>(409,"profile_conflict"),ApiFailure.Limited=>(429,"Limited"),ApiFailure.CompletionUnknown=>(0,"CompletionUnknown"),_=>(0,"ServiceUnavailable")};
  return JsonSerializer.SerializeToElement(new{ok=false,status,error=new{code}});
 }
}

#endif
