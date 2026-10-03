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
    private long _selectionGeneration;
    private long _sessionGeneration;
    private Guid _accountID;
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
        if(_organisationID==id)return true;
        _organisationID=id;_selectionGeneration=checked(_selectionGeneration+1);
        ClearOrganisation();Notify(path);return true;
    }
    public bool? IsActionAvailable(string command)=>command is "Refresh" or "OpenOrganisation" or "RefreshAudit" or "RefreshBilling";
    public async ValueTask DispatchAsync(string command,object? parameter,CancellationToken ct=default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var selectedOrganisation=_organisationID;
            var selectedGeneration=_selectionGeneration;
            _status="Loading current account and organisation state…";Notify("Status");
            var session=await accounts.BeginSessionAsync(ct);
            if(_sessionGeneration!=0&&_sessionGeneration!=session.Generation)ClearAccount();
            _sessionGeneration=session.Generation;
            var account=await accounts.GetCurrentAsync(session,ct);
            if(account.AccountID==Guid.Empty)throw new InvalidDataException("The CAKE server did not return a verified account.");
            if(_accountID!=Guid.Empty&&_accountID!=account.AccountID)ClearAccount();
            _accountID=account.AccountID;
            _identity=account.DisplayName;Notify("AccountName");
            if(command!="Refresh")EnsureSelection(selectedOrganisation,selectedGeneration);
            switch(command)
            {
                case "Refresh":
                    Organisations=(await accounts.ListAsync(session,ct)).Select(o=>new AdminOrganisationRow(o.OrgID.ToString("D"),o.Name,o.AddOn.AddOnID,
                        $"{o.Members.Count(m=>m.State==OrganisationMemberState.Active)} / {(o.AddOn.SeatLimit?.ToString()??"Unlimited")} seats",o.AddOn.State.ToString())).ToArray();Notify("Organisations");break;
                case "OpenOrganisation":
                    if(!Guid.TryParse(selectedOrganisation,out var orgID))throw new ArgumentException("Choose a valid organisation ID from your organisations.");
                    var org=await accounts.GetAsync(orgID,session,ct);
                    EnsureSelection(selectedOrganisation,selectedGeneration);
                    if(org.OrgID!=orgID)throw new InvalidDataException("Organisation response identity mismatch.");
                    _organisationName=org.Name;_billing=$"{org.AddOn.Currency} {org.AddOn.MonthlyPrice:0.00} per month · {org.AddOn.AddOnID} · resources purchased separately";
                    Members=org.Members.Select(m=>new AdminMemberRow(m.AccountID.ToString("D"),string.Join(", ",org.Roles.Where(r=>m.RoleIDs.Contains(r.RoleID)).Select(r=>r.Name)),m.State.ToString())).ToArray();
                    Policy=org.Policy.ForcedSettings.Select(p=>new AdminPolicyRow(p.Key,p.Value)).Concat(org.Policy.BlockedCapabilities.Select(c=>new AdminPolicyRow(c,"Blocked"))).ToArray();
                    Audit=[];foreach(var property in new[]{"OrganisationName","BillingSummary","Members","Policy","Audit"})Notify(property);break;
                case "RefreshBilling":
                    if(!Guid.TryParse(selectedOrganisation,out var billingOrg))throw new ArgumentException("Choose an organisation first.");
                    var billing=await accounts.GetBillingConfigurationAsync(billingOrg,session,ct);EnsureSelection(selectedOrganisation,selectedGeneration);
                    if(billing.OrgID!=billingOrg)throw new InvalidDataException("Organisation billing response identity mismatch.");
                    _organisationName=billing.Name;
                    _billing=$"{billing.AddOn.Currency} {billing.AddOn.MonthlyPrice:0.00} per month · {billing.AddOn.AddOnID} · {billing.AddOn.State} · {billing.ActiveSeats} / {billing.AddOn.SeatLimit?.ToString()??"Unlimited"} seats · resources purchased separately";
                    _billing+=$" · effective {billing.AddOn.EffectiveFrom:u}";
                    if(billing.AddOn.EffectiveUntil is {} until)_billing+=$" · entitlement ends {until:u}";
                    Members=[];Policy=[];Audit=[];
                    foreach(var property in new[]{"OrganisationName","BillingSummary","Members","Policy","Audit"})Notify(property);break;
                case "RefreshAudit":
                    if(!Guid.TryParse(selectedOrganisation,out var auditOrg))throw new ArgumentException("Choose an organisation first.");
                    var audit=await accounts.GetAuditAsync(auditOrg,0,200,session,ct);EnsureSelection(selectedOrganisation,selectedGeneration);Audit=audit;Notify("Audit");break;
                default:throw new KeyNotFoundException("Admin action unavailable.");
            }
            _status="Current server state loaded.";
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(UnauthorizedAccessException)
        {
            ClearAccount();
            _status="CAKE sign-in or organisation permission is required. Existing data was preserved.";
        }
        catch(ArgumentException e){_status=e.Message;}
        catch(Exception){_status="The account service could not load current state. Retry when available.";}
        finally{Notify("Status");_gate.Release();}
    }
    private void ClearOrganisation()
    {
        _organisationName="Choose an organisation";_billing="";Members=[];Policy=[];Audit=[];
        foreach(var property in new[]{"OrganisationName","BillingSummary","Members","Policy","Audit"})Notify(property);
    }
    private void ClearAccount()
    {
        _identity="";_accountID=Guid.Empty;Organisations=[];_organisationID="";_selectionGeneration=checked(_selectionGeneration+1);
        ClearOrganisation();
        foreach(var property in new[]{"AccountName","Organisations","OrganisationID"})Notify(property);
    }
    private void EnsureSelection(string expected,long generation)
    {if(_organisationID!=expected||_selectionGeneration!=generation)throw new ArgumentException("Organisation selection changed. Refresh the selected organisation.");}
    private void Notify(string property)=>PropertyChanged?.Invoke(this,new(property));
}
