using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NineToOne.Accounts;
namespace NineToOne.Admin;

/// <summary>One opaque token generation pins every request in an Admin refresh; changed sessions never publish old responses.</summary>
public sealed class AdminAccountClient(HttpClient http,Func<CancellationToken,ValueTask<string?>> accessToken)
{
    public sealed class Session
    {
        internal Session(AdminAccountClient issuer,string token,long generation){Issuer=issuer;Token=token;Generation=generation;}
        internal AdminAccountClient Issuer {get;}
        internal string Token {get;}
        public long Generation {get;}
    }
    private readonly object _sessionGate=new();
    private string? _observedToken;
    private long _generation;
    public async Task<Session> BeginSessionAsync(CancellationToken ct)
    {
        var token=await accessToken(ct);ct.ThrowIfCancellationRequested();
        lock(_sessionGate)
        {
            if(!StringComparer.Ordinal.Equals(token,_observedToken)){_observedToken=token;_generation=checked(_generation+1);}
            if(string.IsNullOrWhiteSpace(token))throw new UnauthorizedAccessException("CAKE sign-in is required.");
            return new(this,token,_generation);
        }
    }
    public async Task ValidateSessionAsync(Session session,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        var current=await BeginSessionAsync(ct);
        if(!ReferenceEquals(session.Issuer,this)||current.Generation!=session.Generation||!StringComparer.Ordinal.Equals(current.Token,session.Token))
            throw new UnauthorizedAccessException("The CAKE account session changed. Refresh the current account.");
    }
    public Task<CakeProfile> GetCurrentAsync(CancellationToken ct)=>RequestAsync<CakeProfile>(HttpMethod.Get,"api/account/current",null,null,ct);
    public Task<CakeProfile> GetCurrentAsync(Session session,CancellationToken ct)=>RequestAsync<CakeProfile>(HttpMethod.Get,"api/account/current",null,session,ct);
    public Task<Organisation[]> ListAsync(CancellationToken ct)=>ListAsync(null,ct);
    public Task<Organisation[]> ListAsync(Session? session,CancellationToken ct)=>RequestAsync<Organisation[]>(HttpMethod.Post,"api/apps/Admin/Organisations.List",new{offset=0,limit=200},session,ct);
    public Task<Organisation> GetAsync(Guid orgID,CancellationToken ct)=>GetAsync(orgID,null,ct);
    public Task<Organisation> GetAsync(Guid orgID,Session? session,CancellationToken ct)=>RequestAsync<Organisation>(HttpMethod.Post,"api/apps/Admin/Organisations.Get",new{orgID},session,ct);
    public Task<OrganisationAudit[]> GetAuditAsync(Guid orgID,int offset,int limit,CancellationToken ct)=>GetAuditAsync(orgID,offset,limit,null,ct);
    public Task<OrganisationAudit[]> GetAuditAsync(Guid orgID,int offset,int limit,Session? session,CancellationToken ct)=>RequestAsync<OrganisationAudit[]>(HttpMethod.Post,"api/apps/Admin/Audit.List",new{orgID,offset,limit},session,ct);
    public Task<OrganisationBillingConfiguration> GetBillingConfigurationAsync(Guid orgID,CancellationToken ct)=>GetBillingConfigurationAsync(orgID,null,ct);
    public Task<OrganisationBillingConfiguration> GetBillingConfigurationAsync(Guid orgID,Session? session,CancellationToken ct)=>RequestAsync<OrganisationBillingConfiguration>(HttpMethod.Post,"api/apps/Admin/Billing.GetConfiguration",new{orgID},session,ct);
    public Task<Organisation> PublishPolicyAsync(Guid orgID,long expectedRevision,string idempotencyKey,
        IReadOnlySet<string> blockedCapabilities,IReadOnlyDictionary<string,string> forcedSettings,
        IReadOnlyDictionary<string,string> defaultSettings,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(blockedCapabilities);ArgumentNullException.ThrowIfNull(forcedSettings);ArgumentNullException.ThrowIfNull(defaultSettings);
        var blockedSnapshot=blockedCapabilities.ToArray();var forcedSnapshot=new Dictionary<string,string>(forcedSettings,StringComparer.Ordinal);
        var defaultSnapshot=new Dictionary<string,string>(defaultSettings,StringComparer.Ordinal);
        return RequestAsync<Organisation>(HttpMethod.Post,"api/apps/Admin/Policies.Publish",new{orgID,expectedRevision,idempotencyKey,blockedCapabilities=blockedSnapshot,forcedSettings=forcedSnapshot,defaultSettings=defaultSnapshot},null,ct);
    }
    private async Task<T> RequestAsync<T>(HttpMethod method,string path,object? arguments,Session? session,CancellationToken ct)
    {
        var endpoint=http.BaseAddress??throw new InvalidOperationException("CAKE account server is not configured.");
        if(endpoint.Scheme!="https"&&!(endpoint.Scheme=="http"&&endpoint.IsLoopback))throw new InvalidOperationException("CAKE account transport requires HTTPS.");
        session??=await BeginSessionAsync(ct);await ValidateSessionAsync(session,ct);
        using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",session.Token);
        if(arguments is not null)request.Content=JsonContent.Create(arguments);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException("Current CAKE authentication or organisation permission is unavailable.");
        response.EnsureSuccessStatusCode();
        var value=await response.Content.ReadFromJsonAsync<T>(AccountContractJson.CreateOptions(System.Text.Json.JsonSerializerDefaults.Web),ct)??throw new InvalidDataException("The CAKE server returned an invalid response.");
        await ValidateSessionAsync(session,ct);
        return value;
    }
}
