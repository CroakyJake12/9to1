using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NineToOne.Accounts;
namespace NineToOne.Admin;

/// <summary>First-party Admin uses the same server authority as the web app; no local account/role assertions.</summary>
public sealed class AdminAccountClient(HttpClient http,Func<CancellationToken,ValueTask<string?>> accessToken)
{
    public async Task<CakeProfile> GetCurrentAsync(CancellationToken ct)
        =>await RequestAsync<CakeProfile>(HttpMethod.Get,"api/account/current",null,ct);
    public Task<Organisation[]> ListAsync(CancellationToken ct)
        =>RequestAsync<Organisation[]>(HttpMethod.Post,"api/apps/Admin/Organisations.List",new{offset=0,limit=200},ct);
    public Task<Organisation> GetAsync(Guid orgID,CancellationToken ct)
        =>RequestAsync<Organisation>(HttpMethod.Post,"api/apps/Admin/Organisations.Get",new{orgID},ct);
    public Task<OrganisationAudit[]> GetAuditAsync(Guid orgID,int offset,int limit,CancellationToken ct)
        =>RequestAsync<OrganisationAudit[]>(HttpMethod.Post,"api/apps/Admin/Audit.List",new{orgID,offset,limit},ct);
    private async Task<T> RequestAsync<T>(HttpMethod method,string path,object? arguments,CancellationToken ct)
    {
        var endpoint=http.BaseAddress??throw new InvalidOperationException("CAKE account server is not configured.");
        if(endpoint.Scheme!="https"&&!(endpoint.Scheme=="http"&&endpoint.IsLoopback))throw new InvalidOperationException("CAKE account transport requires HTTPS.");
        var token=await accessToken(ct);
        if(string.IsNullOrWhiteSpace(token))throw new UnauthorizedAccessException("CAKE sign-in is required.");
        using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(arguments is not null)request.Content=JsonContent.Create(arguments);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new UnauthorizedAccessException("Current CAKE authentication or organisation permission is unavailable.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(AccountContractJson.CreateOptions(System.Text.Json.JsonSerializerDefaults.Web),ct)??throw new InvalidDataException("The CAKE server returned an invalid response.");
    }
}
