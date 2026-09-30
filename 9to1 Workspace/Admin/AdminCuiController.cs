using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Accounts;
namespace NineToOne.Admin;

public sealed record AdminOrganisationRow(string OrgID,string Name,string AddOn,string SeatSummary,string Status);
public sealed record AdminMemberRow(string AccountID,string Roles,string State);
public sealed record AdminPolicyRow(string Setting,string Value);

/// <summary>Native projections are always refreshed through server-authenticated canonical organisation operations.</summary>
public sealed class AdminCuiController(AdminAccountClient accounts):ICuiWritableBindingContext,ICuiActionDispatcher,ICuiActionAvailability,INotifyPropertyChanged
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private string _status="Sign in to CAKE and connect Home to manage your organisations.";
    private string _organisationID="";
    private string _identity="";
    private string _organisationName="Choose an organisation";
    private string _billing="";
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<AdminOrganisationRow> Organisations {get;private set;}=[];
    public IReadOnlyList<AdminMemberRow> Members {get;private set;}=[];
    public IReadOnlyList<AdminPolicyRow> Policy {get;private set;}=[];
    public IReadOnlyList<OrganisationAudit> Audit {get;private set;}=[];
    public bool TryGetValue(string path,out object? value)
    {
        value=path switch{"Status"=>_status,"AccountName"=>_identity,"OrganisationID"=>_organisationID,"OrganisationName"=>_organisationName,"BillingSummary"=>_billing,"Organisations"=>Organisations,"Members"=>Members,"Policy"=>Policy,"Audit"=>Audit,_=>null};
        return value is not null;
    }
    public bool TrySetValue(string path,object? value)
    {
        if(path!="OrganisationID"||value is not string id)return false;
        _organisationID=id;Notify(path);return true;
    }
    public bool? IsActionAvailable(string command)=>command is "Refresh" or "OpenOrganisation" or "RefreshAudit";
    public async ValueTask DispatchAsync(string command,object? parameter,CancellationToken ct=default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _status="Loading current account and organisation state…";Notify("Status");
            var account=await accounts.GetCurrentAsync(ct);
            if(account.AccountID==Guid.Empty)throw new InvalidDataException("The CAKE server did not return a verified account.");
            _identity=account.DisplayName;Notify("AccountName");
            switch(command)
            {
                case "Refresh":
                    Organisations=(await accounts.ListAsync(ct)).Select(o=>new AdminOrganisationRow(o.OrgID.ToString("D"),o.Name,o.AddOn.AddOnID,
                        $"{o.Members.Count(m=>m.State==OrganisationMemberState.Active)} / {(o.AddOn.SeatLimit?.ToString()??"Unlimited")} seats",o.AddOn.State.ToString())).ToArray();Notify("Organisations");break;
                case "OpenOrganisation":
                    if(!Guid.TryParse(_organisationID,out var orgID))throw new ArgumentException("Choose a valid organisation ID from your organisations.");
                    var org=await accounts.GetAsync(orgID,ct);
                    if(org.OrgID!=orgID)throw new InvalidDataException("Organisation response identity mismatch.");
                    _organisationName=org.Name;_billing=$"{org.AddOn.Currency} {org.AddOn.MonthlyPrice:0.00} per month · {org.AddOn.AddOnID} · resources purchased separately";
                    Members=org.Members.Select(m=>new AdminMemberRow(m.AccountID.ToString("D"),string.Join(", ",org.Roles.Where(r=>m.RoleIDs.Contains(r.RoleID)).Select(r=>r.Name)),m.State.ToString())).ToArray();
                    Policy=org.Policy.ForcedSettings.Select(p=>new AdminPolicyRow(p.Key,p.Value)).Concat(org.Policy.BlockedCapabilities.Select(c=>new AdminPolicyRow(c,"Blocked"))).ToArray();
                    Audit=[];foreach(var property in new[]{"OrganisationName","BillingSummary","Members","Policy","Audit"})Notify(property);break;
                case "RefreshAudit":
                    if(!Guid.TryParse(_organisationID,out var auditOrg))throw new ArgumentException("Choose an organisation first.");
                    Audit=await accounts.GetAuditAsync(auditOrg,0,200,ct);Notify("Audit");break;
                default:throw new KeyNotFoundException("Admin action unavailable.");
            }
            _status="Current server state loaded.";
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(UnauthorizedAccessException)
        {
            _identity="";_organisationName="Choose an organisation";_billing="";Organisations=[];Members=[];Policy=[];Audit=[];
            foreach(var property in new[]{"AccountName","OrganisationName","BillingSummary","Organisations","Members","Policy","Audit"})Notify(property);
            _status="CAKE sign-in or organisation permission is required. Existing data was preserved.";
        }
        catch(ArgumentException e){_status=e.Message;}
        catch(Exception){_status="The account service could not load current state. Retry when available.";}
        finally{Notify("Status");_gate.Release();}
    }
    private void Notify(string property)=>PropertyChanged?.Invoke(this,new(property));
}
