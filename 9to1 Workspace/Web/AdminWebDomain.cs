using System.Text.Json;
using NineToOne.Accounts;

namespace NineToOne.Web;

/// <summary>Management transport over the authoritative Business service. Caller identity comes only from CAKE authentication.</summary>
public sealed class AdminWebDomain(OrganisationService organisations) : IWorkspaceWebDomain
{
    public string Name=>"Admin";
    public IReadOnlySet<string> Actions {get;}=new HashSet<string>{"Organisations.List","Organisations.Get","Audit.List","Members.Invite","Members.AcceptInvitation","Members.Suspend","Members.Reinstate","Members.Remove","Members.SetRoles","Roles.Create","Policies.GetEffective","Policies.Publish","Billing.PreviewDowngrade","Billing.GetConfiguration"};
    public async Task<JsonElement> InvokeAsync(string action,JsonElement args,Guid accountID,CancellationToken ct)
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
                case "Audit.List":result=organisations.ListAudit(accountID,orgID,args.TryGetProperty("offset",out var auditOffset)?auditOffset.GetInt32():0,args.TryGetProperty("limit",out var auditLimit)?auditLimit.GetInt32():100);break;
                case "Members.Invite":result=organisations.Invite(accountID,orgID,args.GetProperty("intendedAccountID").GetGuid(),Roles(args),args.GetProperty("expiresAt").GetDateTimeOffset());break;
                case "Members.SetRoles":result=organisations.SetRoles(accountID,orgID,Revision(args),Key(args),args.GetProperty("memberID").GetGuid(),Roles(args));break;
                case "Members.Suspend":case "Members.Reinstate":case "Members.Remove":result=organisations.SetMemberState(accountID,orgID,Revision(args),Key(args),args.GetProperty("memberID").GetGuid(),action=="Members.Suspend"?OrganisationMemberState.Suspended:action=="Members.Remove"?OrganisationMemberState.Removed:OrganisationMemberState.Active);break;
                case "Roles.Create":result=organisations.CreateRole(accountID,orgID,Revision(args),Key(args),args.GetProperty("name").GetString()!,args.GetProperty("grants").Deserialize<HashSet<string>>()!,args.GetProperty("denials").Deserialize<HashSet<string>>()!);break;
                case "Policies.Publish":result=organisations.PublishPolicy(accountID,orgID,Revision(args),Key(args),
                    args.GetProperty("blockedCapabilities").Deserialize<HashSet<string>>()??throw new ArgumentException("Blocked capabilities are required."),
                    args.GetProperty("forcedSettings").Deserialize<Dictionary<string,string>>()??throw new ArgumentException("Forced settings are required."),
                    args.GetProperty("defaultSettings").Deserialize<Dictionary<string,string>>()??throw new ArgumentException("Default settings are required."));break;
                case "Policies.GetEffective":result=await organisations.EvaluateAsync(accountID,orgID,args.GetProperty("capability").GetString()!,[],null,ct);break;
                case "Billing.GetConfiguration":result=organisations.GetBillingConfiguration(accountID,orgID);break;
                case "Billing.PreviewDowngrade":result=organisations.PreviewDowngrade(accountID,orgID);break;
                default:throw new KeyNotFoundException("capability_unavailable");
            }
        }
        return JsonSerializer.SerializeToElement(result);
    }
    private static long Revision(JsonElement args)=>args.GetProperty("expectedRevision").GetInt64();
    private static string Key(JsonElement args)=>args.GetProperty("idempotencyKey").GetString()!;
    private static Guid[] Roles(JsonElement args)=>args.GetProperty("roleIDs").Deserialize<Guid[]>()!;
}
