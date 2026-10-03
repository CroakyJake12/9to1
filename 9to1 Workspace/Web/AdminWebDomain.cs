using System.Text.Json;
using NineToOne.Accounts;

namespace NineToOne.Web;

/// <summary>Management transport over the authoritative Business service. Caller identity comes only from CAKE authentication.</summary>
public sealed class AdminWebDomain(OrganisationService organisations) : IWorkspaceWebDomain
{
    public string Name=>"Admin";
    public IReadOnlySet<string> Actions {get;}=new HashSet<string>{"Organisations.List","Organisations.Get","Organisations.Update","Organisations.Archive","Organisations.Restore","Organisations.TransferOwnership","Organisations.AcceptOwnershipTransfer","Audit.List","Members.List","Roles.List","Members.Invite","Members.AcceptInvitation","Members.Suspend","Members.Reinstate","Members.Remove","Members.SetRoles","Roles.Create","Roles.Update","Roles.Delete","Roles.GetEffectivePermissions","Jobs.List","Jobs.Get","Jobs.Cancel","Jobs.ExecuteExport","Organisations.Export","Policies.GetEffective","Policies.Publish","Billing.PreviewDowngrade","Billing.GetConfiguration"};
    public Task<JsonElement> InvokeAsync(string action,JsonElement args,Guid accountID,CancellationToken ct)
    {
        try { return Task.FromResult(InvokeSynchronous(action,args,accountID,ct)); }
        catch(Exception error) { return Task.FromException<JsonElement>(error); }
    }
    public async Task<JsonElement> InvokeAuthenticatedAsync(string action,JsonElement args,CurrentAdminSession session,CancellationToken ct)
    {
        if(action=="Jobs.ExecuteExport")
        {
            var original=await organisations.ExecuteOrganisationExportAsync(session,
                args.GetProperty("orgID").GetGuid(),args.GetProperty("jobID").GetGuid(),args.GetProperty("expectedJobRevision").GetInt64(),ct);
            return organisations.SerializeOriginalExportOutcome(session,original);
        }
        return organisations.WithCurrentAdministrationSession(session,
            actorID=>InvokeSynchronous(action,args,actorID,ct),ct);
    }
    private JsonElement InvokeSynchronous(string action,JsonElement args,Guid accountID,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();object result;
        if(action=="Organisations.List")result=organisations.List(accountID,args.TryGetProperty("offset",out var offset)?offset.GetInt32():0,args.TryGetProperty("limit",out var limit)?limit.GetInt32():50);
        else if(action=="Members.AcceptInvitation")result=organisations.AcceptInvitation(accountID,args.GetProperty("token").GetString()!);
        else
        {
            var orgID=args.GetProperty("orgID").GetGuid();
            switch(action)
            {
                case "Organisations.Get":result=organisations.Get(accountID,orgID);break;
                case "Organisations.Update":result=organisations.UpdateIdentity(accountID,orgID,Revision(args),Key(args),args.GetProperty("name").GetString()!,ct);break;
                case "Organisations.Archive":result=organisations.Archive(accountID,orgID,Revision(args),Key(args),ct);break;
                case "Organisations.Restore":result=organisations.Restore(accountID,orgID,Revision(args),Key(args),ct);break;
                case "Organisations.TransferOwnership":result=organisations.RequestOwnershipTransfer(accountID,orgID,Revision(args),Key(args),args.GetProperty("recipientAccountID").GetGuid(),args.GetProperty("expiresAt").GetDateTimeOffset(),ct);break;
                case "Organisations.AcceptOwnershipTransfer":result=organisations.AcceptOwnershipTransfer(accountID,orgID,args.GetProperty("transferID").GetGuid(),Revision(args),Key(args),ct);break;
                case "Audit.List":result=organisations.ListAudit(accountID,orgID,args.TryGetProperty("offset",out var auditOffset)?auditOffset.GetInt32():0,args.TryGetProperty("limit",out var auditLimit)?auditLimit.GetInt32():100);break;
                case "Members.List":result=organisations.ListMembers(accountID,orgID,CollectionCursor(args),CollectionLimit(args),ct);break;
                case "Roles.List":result=organisations.ListRoles(accountID,orgID,CollectionCursor(args),CollectionLimit(args),ct);break;
                case "Members.Invite":result=organisations.Invite(accountID,orgID,args.GetProperty("intendedAccountID").GetGuid(),Roles(args),args.GetProperty("expiresAt").GetDateTimeOffset());break;
                case "Members.SetRoles":result=organisations.SetRoles(accountID,orgID,Revision(args),Key(args),args.GetProperty("memberID").GetGuid(),Roles(args));break;
                case "Members.Suspend":case "Members.Reinstate":case "Members.Remove":result=organisations.SetMemberState(accountID,orgID,Revision(args),Key(args),args.GetProperty("memberID").GetGuid(),action=="Members.Suspend"?OrganisationMemberState.Suspended:action=="Members.Remove"?OrganisationMemberState.Removed:OrganisationMemberState.Active);break;
                case "Roles.Create":result=organisations.CreateRole(accountID,orgID,Revision(args),Key(args),args.GetProperty("name").GetString()!,args.GetProperty("grants").Deserialize<HashSet<string>>()!,args.GetProperty("denials").Deserialize<HashSet<string>>()!);break;
                case "Roles.Update":result=organisations.UpdateRole(accountID,orgID,args.GetProperty("roleID").GetGuid(),Revision(args),Key(args),args.GetProperty("name").GetString()!,args.GetProperty("grants").Deserialize<HashSet<string>>()!,args.GetProperty("denials").Deserialize<HashSet<string>>()!,ct);break;
                case "Roles.Delete":result=organisations.DeleteRole(accountID,orgID,args.GetProperty("roleID").GetGuid(),Revision(args),Key(args),ct);break;
                case "Roles.GetEffectivePermissions":result=organisations.GetEffectiveRolePermissions(accountID,orgID,args.GetProperty("accountID").GetGuid(),ct);break;
                case "Jobs.List":result=organisations.ListAdminJobs(accountID,orgID,CollectionCursor(args),CollectionLimit(args),ct);break;
                case "Jobs.Get":result=organisations.GetAdminJob(accountID,orgID,args.GetProperty("jobID").GetGuid(),ct);break;
                case "Jobs.Cancel":result=organisations.CancelAdminJob(accountID,orgID,args.GetProperty("jobID").GetGuid(),args.GetProperty("expectedJobRevision").GetInt64(),Key(args),ct);break;
                case "Organisations.Export":result=organisations.RequestOrganisationExport(accountID,orgID,Revision(args),Key(args),CollectionCursor(args),CollectionLimit(args),ct);break;
                case "Jobs.ExecuteExport":throw new UnauthorizedAccessException("current_original_administration_session_required");
                case "Policies.Publish":result=organisations.PublishPolicy(accountID,orgID,Revision(args),Key(args),
                    args.GetProperty("blockedCapabilities").Deserialize<HashSet<string>>()??throw new ArgumentException("Blocked capabilities are required."),
                    args.GetProperty("forcedSettings").Deserialize<Dictionary<string,string>>()??throw new ArgumentException("Forced settings are required."),
                    args.GetProperty("defaultSettings").Deserialize<Dictionary<string,string>>()??throw new ArgumentException("Default settings are required."));break;
                case "Policies.GetEffective":result=organisations.EvaluateCurrent(accountID,orgID,args.GetProperty("capability").GetString()!,[],null,ct);break;
                case "Billing.GetConfiguration":result=organisations.GetBillingConfiguration(accountID,orgID);break;
                case "Billing.PreviewDowngrade":result=organisations.PreviewDowngrade(accountID,orgID);break;
                default:throw new KeyNotFoundException("capability_unavailable");
            }
        }
        return JsonSerializer.SerializeToElement(result);
    }
    private static string? CollectionCursor(JsonElement args)=>args.TryGetProperty("cursor",out var cursor)&&cursor.ValueKind!=JsonValueKind.Null?cursor.GetString():null;
    private static int CollectionLimit(JsonElement args)=>args.TryGetProperty("limit",out var limit)?limit.GetInt32():50;
    private static long Revision(JsonElement args)=>args.GetProperty("expectedRevision").GetInt64();
    private static string Key(JsonElement args)=>args.GetProperty("idempotencyKey").GetString()!;
    private static Guid[] Roles(JsonElement args)=>args.GetProperty("roleIDs").Deserialize<Guid[]>()!;
}
