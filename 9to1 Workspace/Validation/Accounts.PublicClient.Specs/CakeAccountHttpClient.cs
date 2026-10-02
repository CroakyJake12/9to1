using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Accounts;

public sealed record CakeTokenExchangeRequest(string Code,string ClientID,string RedirectURI,string Verifier,string DeviceName);
public sealed record CakePublicToken(
    [property:JsonPropertyName("access_token")] string AccessToken,
    [property:JsonPropertyName("token_type")] string TokenType,
    [property:JsonPropertyName("expires_in")] int ExpiresIn);
public sealed class CakeAccountTransportException(string code,HttpStatusCode? status,bool operationMayHaveApplied)
    :Exception(code)
{
    public string Code { get; }=code;
    public HttpStatusCode? Status { get; }=status;
    public bool OperationMayHaveApplied { get; }=operationMayHaveApplied;
}

/// <summary>Exact existing public HTTP API client. One explicitly configured origin,
/// redirects disabled, no local-authority fallback or automatic mutation retries.
/// Profile/session DTOs are display data, never a trusted Home principal or commit grant.
/// Token exchange returns the ACTUAL wire response, not a fabricated IssuedSession.
/// Local loopback HTTP is opt-in fixture-only; production requires HTTPS.</summary>
public sealed class CakeAccountHttpClient : IDisposable
{
    private readonly HttpClient http;
    private readonly Uri origin;
    private const int MaximumResponseBytes=1_048_576;
    private static readonly JsonSerializerOptions Json=AccountContractJson.CreateOptions(JsonSerializerDefaults.Web);
    public CakeAccountHttpClient(Uri backend,bool allowLocalLoopbackFixture=false)
        :this(backend,new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false },allowLocalLoopbackFixture) { }
    // Owning test-handler injection only. Production uses the nonredirecting constructor above.
    internal CakeAccountHttpClient(Uri backend,HttpMessageHandler handler,bool allowLocalLoopbackFixture=false)
    {
        if(!backend.IsAbsoluteUri || backend.UserInfo.Length!=0 || backend.Query.Length!=0 || backend.Fragment.Length!=0 ||
            backend.AbsolutePath!="/" || (backend.Scheme!="https" && !(allowLocalLoopbackFixture && backend.Scheme=="http" && backend.IsLoopback)))
        {
            handler.Dispose();
            throw new ArgumentException("explicit_backend_origin_required",nameof(backend));
        }
        origin=backend;http=new HttpClient(handler,disposeHandler:true);
    }
    public async ValueTask<CakePublicToken> ExchangeAsync(CakeTokenExchangeRequest request,CancellationToken ct=default)
    {
        var token=await ReadAsync<CakePublicToken>(HttpMethod.Post,"oauth/token",null,request,true,ct).ConfigureAwait(false);
        if(string.IsNullOrWhiteSpace(token.AccessToken) || token.TokenType!="Bearer" || token.ExpiresIn<=0)
            throw new CakeAccountTransportException("InvalidTokenResponse",null,true);
        return token;
    }
    public ValueTask<CakeProfile> GetCurrentAsync(string token,CancellationToken ct=default)
        =>ReadAsync<CakeProfile>(HttpMethod.Get,"api/account/current",token,null,false,ct);
    public ValueTask<IReadOnlyList<CakeSession>> ListSessionsAsync(string token,CancellationToken ct=default)
        =>ReadAsync<IReadOnlyList<CakeSession>>(HttpMethod.Get,"api/account/sessions",token,null,false,ct);
    public ValueTask SignOutAsync(string token,CancellationToken ct=default)
        =>MutationAsync(HttpMethod.Post,"api/account/signout",token,ct);
    public ValueTask RevokeOtherSessionsAsync(string token,CancellationToken ct=default)
        =>MutationAsync(HttpMethod.Post,"api/account/revoke-other-sessions",token,ct);
    public ValueTask RevokeSessionAsync(string token,Guid sessionID,CancellationToken ct=default)
    {
        if(sessionID==Guid.Empty)throw new ArgumentException("invalid_session_id",nameof(sessionID));
        return MutationAsync(HttpMethod.Delete,"api/account/sessions/"+sessionID.ToString("D"),token,ct);
    }
    private async ValueTask<T> ReadAsync<T>(HttpMethod method,string path,string? token,object? body,bool mutation,CancellationToken ct) where T:class
    {
        using var response=await SendAsync(method,path,token,body,mutation,ct).ConfigureAwait(false);
        try
        {
            if(response.Content.Headers.ContentLength is long declared && declared>MaximumResponseBytes)
                throw new CakeAccountTransportException("ResponseTooLarge",response.StatusCode,mutation);
            await using var stream=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var bounded=new MemoryStream();var buffer=new byte[8192];
            while(true)
            {
                var read=await stream.ReadAsync(buffer.AsMemory(),ct).ConfigureAwait(false);
                if(read==0)break;
                if(bounded.Length+read>MaximumResponseBytes)
                    throw new CakeAccountTransportException("ResponseTooLarge",response.StatusCode,mutation);
                bounded.Write(buffer,0,read);
            }
            // Bound compressed/chunked responses while streaming, before JSON allocation/parse.
            ct.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<T>(bounded.ToArray(),Json)
                ??throw new JsonException("empty_account_response");
        }
        catch(JsonException) { throw new CakeAccountTransportException("InvalidResponse",response.StatusCode,mutation); }
        catch(Exception error) when(error is HttpRequestException or IOException or OperationCanceledException)
        { throw new CakeAccountTransportException("ResponseUnavailable",response.StatusCode,mutation); }
    }
    private async ValueTask MutationAsync(HttpMethod method,string path,string token,CancellationToken ct)
    { using var response=await SendAsync(method,path,token,null,true,ct).ConfigureAwait(false); }
    private async ValueTask<HttpResponseMessage> SendAsync(HttpMethod method,string path,string? token,object? body,bool mutation,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var request=new HttpRequestMessage(method,new Uri(origin,path));
        if(token is not null)
        {
            if(string.IsNullOrWhiteSpace(token))throw new UnauthorizedAccessException("missing_token");
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        }
        if(body is not null)request.Content=new StringContent(JsonSerializer.Serialize(body,Json),System.Text.Encoding.UTF8,"application/json");
        HttpResponseMessage response;
        try { response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false); }
        catch(Exception error) when(error is HttpRequestException or IOException or OperationCanceledException)
        { throw new CakeAccountTransportException("BackendUnavailable",null,mutation); }
        if(response.IsSuccessStatusCode)return response;
        var status=response.StatusCode;response.Dispose();
        // Do not log raw provider response/token or infer rollback from a lost/5xx response.
        var code=status switch
        {
            HttpStatusCode.Unauthorized=>"Unauthenticated",
            HttpStatusCode.Forbidden=>"PermissionDenied",
            HttpStatusCode.Conflict=>"Conflict",
            HttpStatusCode.BadRequest=>"InvalidInput",
            _=>"BackendRejected"
        };
        throw new CakeAccountTransportException(code,status,mutation && ((int)status>=500 || (int)status is >=300 and <=399));
    }
    public void Dispose()=>http.Dispose();
}
