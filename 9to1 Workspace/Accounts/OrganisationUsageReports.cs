namespace NineToOne.Accounts;

/// <summary>Operational accounting only. Null attribution identifies preserved legacy history, never guessed activity.</summary>
public sealed record OrganisationUsageReportRow(string PeriodID,Guid AccountID,string ModelRouteID,
    OrganisationUsageAttribution? Attribution,IReadOnlyList<Guid> HistoricalRoleIDs,long ReservedDust,long SettledDust,long DispatchedRuns,long SettledRuns);
public sealed record OrganisationUsageReport(Guid OrgID,Guid PoolID,long PolicyRevision,DateTimeOffset ObservedAt,
    IReadOnlyList<OrganisationUsageReportRow> Rows);

public sealed partial class OrganisationDustPools
{
    public OrganisationUsageReport GetUsageReport(string token,Guid orgID,long policyRevision,Guid poolID)
        =>Current(token,orgID,policyRevision,"Admin.Resources.GetUsage",(_,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);
            var rows=state.Reservations.Where(r=>r.Funding.PoolID==pool.PoolID&&r.Funding.OrgID==org.OrgID&&r.State!=OrganisationPoolReservationState.Cancelled)
                .GroupBy(r=>(r.Funding.PeriodID,r.Funding.AccountID,r.Funding.ModelRouteID,r.Funding.Attribution,
                    Roles:string.Join(",",r.RoleIDs.OrderBy(id=>id).Select(id=>id.ToString("D")))))
                .Select(group=>new OrganisationUsageReportRow(group.Key.PeriodID,group.Key.AccountID,group.Key.ModelRouteID,
                    group.Key.Attribution,Array.AsReadOnly(group.First().RoleIDs.OrderBy(id=>id).ToArray()),
                    SumCapacity(group.Where(r=>r.State is OrganisationPoolReservationState.Reserved or OrganisationPoolReservationState.Dispatched).Select(r=>r.Funding.ReservedDust)),
                    SumCapacity(group.Where(r=>r.State==OrganisationPoolReservationState.Settled).Select(r=>r.ActualDust)),
                    group.LongCount(r=>r.State==OrganisationPoolReservationState.Dispatched),group.LongCount(r=>r.State==OrganisationPoolReservationState.Settled)))
                .OrderBy(row=>row.PeriodID,StringComparer.Ordinal).ThenBy(row=>row.AccountID).ThenBy(row=>row.ModelRouteID,StringComparer.Ordinal).ToArray();
            return new OrganisationUsageReport(org.OrgID,pool.PoolID,org.Policy.Revision,clock.GetUtcNow(),Array.AsReadOnly(rows));
        });

    private static void ValidateAttribution(OrganisationUsageAttribution? attribution)
    {
        static bool RequiredID(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=4096&&!id.Any(char.IsControl);
        static bool OptionalID(string? id)=>id is null||RequiredID(id);
        if(attribution is null||!RequiredID(attribution.AppID)||!RequiredID(attribution.ModelID)||!OptionalID(attribution.AgentID)||!OptionalID(attribution.AutomationID))throw new InvalidDataException("invalid_canonical_usage_attribution");
    }
}
