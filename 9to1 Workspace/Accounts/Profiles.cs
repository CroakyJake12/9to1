using System.Text;
using System.Text.Json;

namespace NineToOne.Accounts;

public sealed record AccountProfile(Guid AccountID,string Name,string Username,string? Icon,string? Pronouns,string? Job,long Revision);
public sealed record ProfileAudit(Guid AuditEventID,Guid AccountID,string Action,DateTimeOffset At,long Revision);
public sealed record ProfileState(IReadOnlyList<AccountProfile> Profiles,IReadOnlyList<ProfileAudit> Audit);

public sealed class ProfileService(string statePath,Guid? trustedVerifiedJacobAccountID)
{
    public AccountProfile? Get(Guid authenticatedAccountID)
    {DemandAccountID(authenticatedAccountID);using var lease=DurableState.Acquire(statePath);return Read().Profiles.SingleOrDefault(p=>p.AccountID==authenticatedAccountID);}
    public bool HasAccountBoundFreeBusiness(Guid authenticatedAccountID)=>trustedVerifiedJacobAccountID is {} verified&&verified==authenticatedAccountID;
    public bool UsernameAvailable(Guid authenticatedAccountID,string username)
    {
        DemandAccountID(authenticatedAccountID);
        var normalized=NormalizeUsername(username);
        if(normalized=="croakyjake" && authenticatedAccountID!=trustedVerifiedJacobAccountID)return false;
        using var lease=DurableState.Acquire(statePath);
        return !Read().Profiles.Any(p=>p.AccountID!=authenticatedAccountID&&NormalizeUsername(p.Username)==normalized);
    }
    public AccountProfile Update(Guid authenticatedAccountID,long expectedRevision,JsonElement patch)
    {
        DemandAccountID(authenticatedAccountID);
        if(patch.ValueKind!=JsonValueKind.Object)throw new ArgumentException("invalid_profile_patch");
        using var lease=DurableState.Acquire(statePath);var state=Read();var current=state.Profiles.SingleOrDefault(p=>p.AccountID==authenticatedAccountID);
        if((current?.Revision??0)!=expectedRevision)throw new InvalidOperationException("profile_revision_conflict");
        if(current?.Revision==long.MaxValue)throw new InvalidOperationException("profile_revision_exhausted");
        var values=new Dictionary<string,string?>{["name"]=current?.Name,["username"]=current?.Username,["icon"]=current?.Icon,["pronouns"]=current?.Pronouns,["job"]=current?.Job};
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in patch.EnumerateObject())
        {
            if(!seen.Add(field.Name)||!values.ContainsKey(field.Name)||field.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))throw new ArgumentException("invalid_profile_field");
            values[field.Name]=field.Value.ValueKind==JsonValueKind.Null?null:field.Value.GetString()?.Trim();
        }
        if(string.IsNullOrWhiteSpace(values["name"])||string.IsNullOrWhiteSpace(values["username"]))throw new ArgumentException("name_and_username_required");
        if(values.Values.Any(v=>v?.Length>1000))throw new ArgumentException("profile_field_too_long");
        var normalized=NormalizeUsername(values["username"]!);
        if(normalized=="croakyjake"&&authenticatedAccountID!=trustedVerifiedJacobAccountID)throw new UnauthorizedAccessException("reserved_username");
        if(state.Profiles.Any(p=>p.AccountID!=authenticatedAccountID&&NormalizeUsername(p.Username)==normalized))throw new InvalidOperationException("username_unavailable");
        var profile=new AccountProfile(authenticatedAccountID,values["name"]!,values["username"]!,Empty(values["icon"]),Empty(values["pronouns"]),Empty(values["job"]),(current?.Revision??0)+1);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        DurableState.Write(statePath,state with {Profiles=state.Profiles.Where(p=>p.AccountID!=authenticatedAccountID).Append(profile).ToArray(),
            Audit=state.Audit.Append(new(Guid.NewGuid(),authenticatedAccountID,"ProfileUpdated",DateTimeOffset.UtcNow,profile.Revision)).ToArray()});return profile;
    }
    private static string? Empty(string? value)=>string.IsNullOrEmpty(value)?null:value;
    private static void DemandAccountID(Guid accountID)
    {if(accountID==Guid.Empty)throw new ArgumentException("invalid_authenticated_account");}
    private static string NormalizeUsername(string value)
    {
        if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("username_required");
        var normalized=value.Trim().TrimStart('@').Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        if(string.IsNullOrWhiteSpace(normalized))throw new ArgumentException("username_required");
        return normalized;
    }
    private ProfileState Read()
    {
        if(!File.Exists(statePath))return new([],[]);
        var state=DurableState.Read<ProfileState>(statePath);
        if(state.Profiles is null||state.Audit is null||
            state.Profiles.Any(p=>p is null||p.AccountID==Guid.Empty||string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Username)||p.Revision<1||p.Name.Length>1000||p.Username.Length>1000||p.Icon?.Length>1000||p.Pronouns?.Length>1000||p.Job?.Length>1000)||
            state.Profiles.Select(p=>p.AccountID).Distinct().Count()!=state.Profiles.Count||
            state.Audit.Any(a=>a is null||a.AuditEventID==Guid.Empty||a.AccountID==Guid.Empty||string.IsNullOrWhiteSpace(a.Action)||a.At==default||a.Revision<1||!state.Profiles.Any(p=>p.AccountID==a.AccountID&&p.Revision>=a.Revision)||a.Action!="ProfileUpdated")||
            state.Audit.Select(a=>a.AuditEventID).Distinct().Count()!=state.Audit.Count||
            state.Audit.Select(a=>(a.AccountID,a.Revision)).Distinct().Count()!=state.Audit.Count)
            throw new InvalidDataException("invalid_profile_state");
        try
        {
            if(state.Profiles.Select(p=>NormalizeUsername(p.Username)).Distinct(StringComparer.Ordinal).Count()!=state.Profiles.Count||
                state.Profiles.Any(p=>NormalizeUsername(p.Username)=="croakyjake"&&p.AccountID!=trustedVerifiedJacobAccountID))
                throw new InvalidDataException("invalid_profile_state");
        }
        catch(ArgumentException error){throw new InvalidDataException("invalid_profile_state",error);}
        return state;
    }
}
