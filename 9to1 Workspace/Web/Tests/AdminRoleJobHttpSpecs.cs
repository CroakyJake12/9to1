using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NineToOne.Accounts;

/// <summary>Actual existing Web host and original authenticated HTTP transport;
/// synthetic verified fixture identity only, never remote Worker acceptance.</summary>
internal static class AdminRoleJobHttpSpecs
{
    public static async Task RunAsync(HttpClient http,CakeIdentityService identity,OrganisationService organisations,
        Guid owner,string redirect)
    {
        var org=organisations.CreateTrustedOrganisation(owner,"Separate actual HTTP role/export fixture",BusinessAddOnKind.Business,"fixture-only-settled-billing");
        var verifier=new string('b',64);
        var code=identity.AuthorizeAuthenticatedAccount(new(owner,"Synthetic original Admin HTTP owner"),"test",redirect,CakeIdentityService.Challenge(verifier));
        var issued=identity.Exchange(code,"test",redirect,verifier,"original-admin-http");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",issued.AccessToken);
        var created=await Post(http,"Roles.Create",new{orgID=org.OrgID,expectedRevision=org.Revision,idempotencyKey="http-role-create",name="HTTP role",grants=new[]{"Admin.Organisations.Get"},denials=Array.Empty<string>()});
        var roleID=created.GetProperty("Roles").EnumerateArray().Single(r=>r.GetProperty("Name").GetString()=="HTTP role").GetProperty("RoleID").GetGuid();
        var roleRevision=created.GetProperty("Revision").GetInt64();
        var args=new{orgID=org.OrgID,roleID,expectedRevision=roleRevision,idempotencyKey="http-role-update",name="Updated HTTP role",grants=new[]{"Admin.Organisations.Get","Admin.Members.List"},denials=new[]{"Files.Delete"},accountID=Guid.NewGuid()};
        var updated=await Post(http,"Roles.Update",args);
        Check(updated.GetProperty("Role").GetProperty("RoleID").GetGuid()==roleID&&updated.GetProperty("Receipt").GetProperty("ActorID").GetGuid()==owner,
            "actual original authenticated account replaces copied DTO account claim");
        var replay=await Post(http,"Roles.Update",args);
        Check(replay.GetProperty("Replayed").GetBoolean(),"HTTP exact idempotency replay returns original durable role receipt");
        using(var stale=await http.PostAsJsonAsync("/api/apps/Admin/Roles.Update",new{orgID=org.OrgID,roleID,expectedRevision=roleRevision,idempotencyKey="http-role-stale",name="Stale",grants=Array.Empty<string>(),denials=Array.Empty<string>()}))
            Check(stale.StatusCode==HttpStatusCode.Conflict&&(await stale.Content.ReadAsStringAsync()).Contains("RevisionConflict",StringComparison.Ordinal),"actual HTTP stale role code is structured");
        var effective=await Post(http,"Roles.GetEffectivePermissions",new{orgID=org.OrgID,accountID=owner});
        Check(effective.GetProperty("RequiresCurrentOwningObjectAdmission").GetBoolean(),"effective role metadata cannot replace owning object or device admission");
        var current=organisations.Get(owner,org.OrgID);
        var request=await Post(http,"Organisations.Export",new{orgID=org.OrgID,expectedRevision=current.Revision,idempotencyKey="http-export",limit=1});
        var jobID=request.GetProperty("JobID").GetGuid();
        var complete=await Post(http,"Jobs.ExecuteExport",new{orgID=org.OrgID,jobID,expectedJobRevision=request.GetProperty("Revision").GetInt64()});
        Check(complete.GetProperty("State").GetInt32()==(int)AdminJobState.Succeeded&&complete.GetProperty("OutputIsCurrent").GetBoolean()&&
            complete.GetProperty("RunID").GetGuid()!=Guid.Empty,"same real HTTP original export owner produced current terminal result");
        using(var output=JsonDocument.Parse(complete.GetProperty("ExportDocument").GetString()!))
            Check(output.RootElement.GetProperty("OrgID").GetGuid()==org.OrgID&&output.RootElement.GetProperty("Records").GetArrayLength()==1,
                "actual HTTP output is bounded canonical configuration page");
        using(var foreign=await http.PostAsJsonAsync("/api/apps/Admin/Jobs.Get",new{orgID=Guid.NewGuid(),jobID}))
            Check(!foreign.IsSuccessStatusCode,"copied job ID never selects a foreign organisation");
        var cancelledRequest=await Post(http,"Organisations.Export",new{orgID=org.OrgID,expectedRevision=current.Revision,idempotencyKey="http-export-cancel",limit=1});
        var cancellation=new{orgID=org.OrgID,jobID=cancelledRequest.GetProperty("JobID").GetGuid(),expectedJobRevision=cancelledRequest.GetProperty("Revision").GetInt64(),idempotencyKey="http-original-cancel"};
        var cancelled=await Post(http,"Jobs.Cancel",cancellation);
        var cancelReplay=await Post(http,"Jobs.Cancel",cancellation);
        Check(cancelled.GetProperty("State").GetInt32()==(int)AdminJobState.Cancelled&&cancelReplay.GetProperty("Revision").GetInt64()==cancelled.GetProperty("Revision").GetInt64(),
            "HTTP pending cancellation and replay retain original terminal identity");
        var deleted=await Post(http,"Roles.Delete",new{orgID=org.OrgID,roleID,expectedRevision=current.Revision,idempotencyKey="http-role-delete"});
        Check(deleted.GetProperty("Role").ValueKind==JsonValueKind.Null,"actual HTTP unused role deletion");
        var retired=await Post(http,"Jobs.Get",new{orgID=org.OrgID,jobID});
        Check(retired.GetProperty("State").GetInt32()==(int)AdminJobState.Succeeded&&retired.GetProperty("ExportDocument").ValueKind==JsonValueKind.Null,
            "current organisation change retires original output while preserving observed success");
        identity.SignOut(issued.AccessToken);
        using(var revoked=await http.PostAsJsonAsync("/api/apps/Admin/Jobs.Get",new{orgID=org.OrgID,jobID}))
            Check(revoked.StatusCode==HttpStatusCode.Unauthorized,"same original signed-out token cannot return any Admin job output");
        Console.WriteLine("PASS actual authenticated HTTP Admin role/export/current-scope/cancellation/revocation flow");
    }
    private static async Task<JsonElement> Post(HttpClient http,string action,object args)
    {
        using var response=await http.PostAsJsonAsync("/api/apps/Admin/"+action,args);
        response.EnsureSuccessStatusCode();
        using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return result.RootElement.Clone();
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
