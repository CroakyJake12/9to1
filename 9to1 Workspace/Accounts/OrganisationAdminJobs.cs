using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace NineToOne.Accounts;

public sealed partial class OrganisationService
{
    private const string OrganisationExportAction = "Admin.Organisations.Export";
    private const int MaximumExportPageBytes = 262144;
    // Trusted per-instance owning test scheduling only; no DTO, wire or global
    // authority. Default null in actual Web composition.
    internal Action<AdminExportObservationPhase>? ExportObservationCheckpoint { get; set; }

    public AdminJob RequestOrganisationExport(Guid actorID, Guid orgID, long expectedRevision,
        string idempotencyKey, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleKey(idempotencyKey); ValidateLifecycleRevision(expectedRevision);
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        DemandExport(org, actorID);
        var offset = cursor is null ? 0 : ReadCollectionCursor(cursor, actorID, org, OrganisationExportAction, limit);
        var prior = (state.AdminJobs ?? []).SingleOrDefault(job => job.OrgID == orgID &&
            job.RequestingAccountID == actorID && job.Action == OrganisationExportAction && job.IdempotencyKey == idempotencyKey);
        if (prior is not null)
        {
            if (prior.RequestedOrganisationRevision != expectedRevision || prior.Offset != offset || prior.Limit != limit)
                throw new AdministrationConflictException(AdministrationConflictCode.IdempotencyConflict);
            return ObserveAdminJob(prior, org, actorID);
        }
        if (org.Revision != expectedRevision) throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
        if ((state.AdminJobs ?? []).Count(job => job.OrgID == orgID) >= 1024)
            throw new AdministrationConflictException(AdministrationConflictCode.QuotaExceeded);
        var job = new AdminJob(Guid.NewGuid(), orgID, actorID, OrganisationExportAction, idempotencyKey,
            org.Revision, offset, limit, AdminJobState.Pending, null, 1, LifecycleNow, null, null, null,
            Array.AsReadOnly(new[] { new AdminJobTargetResult(orgID, AdminJobTargetState.Pending, null) }));
        SaveAdminJob(state, org, actorID, "Admin.Organisations.ExportRequested", job);
        return job;
    }

    public OrganisationCollectionPage<AdminJob> ListAdminJobs(Guid actorID, Guid orgID,
        string? cursor = null, int limit = 50, CancellationToken cancellationToken = default) =>
        ReadAdministrationPage(actorID, orgID, "Admin.Jobs.List", cursor, limit, org =>
        {
            // Reentrant read uses the SAME already-held canonical organisation lease.
            var state = Read();
            return (state.AdminJobs ?? []).Where(job => job.OrgID == orgID).OrderBy(job => job.JobID)
                .Select(job => ObserveAdminJob(job, org, actorID)).ToArray();
        }, cancellationToken);

    public AdminJob GetAdminJob(Guid actorID, Guid orgID, Guid jobID, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        Demand(org, actorID, "Admin.Jobs.Get");
        return ObserveAdminJob(FindAdminJob(state, orgID, jobID), org, actorID);
    }

    public AdminJob CancelAdminJob(Guid actorID, Guid orgID, Guid jobID, long expectedJobRevision, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateLifecycleKey(idempotencyKey); ValidateLifecycleRevision(expectedJobRevision);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        Demand(org, actorID, "Admin.Jobs.Cancel");
        var job = FindAdminJob(state, orgID, jobID);
        if (job.State == AdminJobState.Cancelled && job.CancellationIdempotencyKey == idempotencyKey)
        {
            if (job.CancelledFromRevision != expectedJobRevision || job.CancelledByAccountID != actorID) throw new AdministrationConflictException(AdministrationConflictCode.IdempotencyConflict);
            return ObserveAdminJob(job, org, actorID);
        }
        if (job.Revision != expectedJobRevision) throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
        // No guessed PID, cancellation or adoption of an original Running operation.
        if (job.State != AdminJobState.Pending) throw new AdministrationConflictException(AdministrationConflictCode.JobNotCancellable);
        var cancelled = job with { State = AdminJobState.Cancelled, Revision = checked(job.Revision + 1),
            CompletedAt = LifecycleNow, Code = "CancelledBeforeExecution", ExportDocument = null,
            CancellationIdempotencyKey = idempotencyKey, CancelledFromRevision = job.Revision, CancelledByAccountID = actorID,
            Targets = Array.AsReadOnly(new[] { new AdminJobTargetResult(orgID, AdminJobTargetState.Skipped, "CancelledBeforeExecution") }) };
        SaveAdminJob(state, org, actorID, "Admin.Jobs.Cancel", cancelled);
        return cancelled;
    }

    /// <summary>Real bounded owning export, called by a registered verified-session
    /// transport. Its original async Task is retained by the caller. Current server
    /// role/policy and revision are rechecked before output publication after the await.
    /// A Running job after process loss cannot be re-executed without reconciliation.</summary>
    public async Task<AdminJob> ExecuteOrganisationExportAsync(CurrentAdminSession session, Guid orgID, Guid jobID,
        long expectedJobRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var actorID = session.AccountID; // Observation only; both phases re-enter SAME issuer.
        AdminJob? admittedOriginal = null;
        try
        {
        var preparation = WithCurrentAdministrationSession(session, currentActor =>
        {
            if (currentActor != actorID) throw new UnauthorizedAccessException("current_session_binding_changed");
            cancellationToken.ThrowIfCancellationRequested();
            var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
            Demand(org, actorID, "Admin.Jobs.ExecuteExport"); DemandExport(org, actorID);
            var job = FindAdminJob(state, orgID, jobID);
            if (job.RequestingAccountID != actorID) throw new OrganisationAccessException("PermissionDenied");
            if (job.Revision != expectedJobRevision) throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
            if (job.State != AdminJobState.Pending) throw new AdministrationConflictException(AdministrationConflictCode.OriginalOutcomeRequiresReconciliation);
            if (job.RequestedOrganisationRevision != org.Revision) throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
            var capturedPage = CaptureExportPage(org, actorID, job.Offset, job.Limit);
            var started = job with { State = AdminJobState.Running, RunID = Guid.NewGuid(), Revision = checked(job.Revision + 1) };
            // Retain the exact candidate before persistence/lease exit. If an original
            // persistence or lease failure follows a real commit, the same RunID can
            // still receive a truthful failure observation under its owning lock.
            admittedOriginal = started;
            SaveAdminJob(state, org, actorID, "Admin.Jobs.ExportStarted", started);
            return (Claimed: started, Page: capturedPage);
        }, cancellationToken);
        var claimed = preparation.Claimed; var page = preparation.Page;
            // No lock is held across async work. This optional registered owning test/
            // scheduling seam cannot supply actor, permission or publication success.
            if (adminJobCheckpoint is not null) await adminJobCheckpoint(cancellationToken);
            else await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(page);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumExportPageBytes)
                throw new AdministrationConflictException(AdministrationConflictCode.ExportLimitExceeded);
            return WithCurrentAdministrationSession(session, currentActor =>
            {
            if (currentActor != actorID) throw new UnauthorizedAccessException("current_session_binding_changed");
            cancellationToken.ThrowIfCancellationRequested();
            var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
            Demand(org, actorID, "Admin.Jobs.ExecuteExport"); DemandExport(org, actorID);
            var current = FindAdminJob(state, orgID, jobID);
            if (current.State != AdminJobState.Running || current.RunID != claimed.RunID || current.Revision != claimed.Revision)
                throw new AdministrationConflictException(AdministrationConflictCode.OriginalClaimChanged);
            if (org.Revision != claimed.RequestedOrganisationRevision)
                throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
            var complete = claimed with { State = AdminJobState.Succeeded, Revision = checked(claimed.Revision + 1),
                CompletedAt = LifecycleNow, ExportDocument = json,
                Targets = Array.AsReadOnly(new[] { new AdminJobTargetResult(orgID, AdminJobTargetState.Succeeded, null) }) };
            SaveAdminJob(state, org, actorID, "Admin.Jobs.ExportCompleted", complete);
            ExportObservationCheckpoint?.Invoke(AdminExportObservationPhase.AfterCompletedSave);
            try { administrationAdmission.Value!.Recheck(); }
            catch (UnauthorizedAccessException)
            { return WithheldOriginalOutcome(complete); }
            return ObserveAdminJob(complete, org, actorID);
            }, cancellationToken);
        }
        catch (Exception primary)
        {
            try
            {
                if (admittedOriginal is not null)
                {
                using var lease = DurableState.Acquire(statePath);
                var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
                var current = FindAdminJob(state, orgID, jobID);
                if (current.State == AdminJobState.Running && current.RunID == admittedOriginal.RunID && current.Revision == admittedOriginal.Revision)
                {
                    // Trusted SAME original owner records only its failed observation.
                    // It exposes no result or authority after membership/policy retirement.
                    var code = primary is OperationCanceledException ? "OperationCancelled" :
                        primary is OrganisationAccessException or UnauthorizedAccessException ? "PermissionDenied" :
                        primary is InvalidOperationException ? "RevisionOrOutputConflict" : "OwnerOperationFailed";
                    var failed = current with { State = AdminJobState.Failed, Revision = checked(current.Revision + 1),
                        CompletedAt = LifecycleNow, Code = code, ExportDocument = null,
                        Targets = Array.AsReadOnly(new[] { new AdminJobTargetResult(orgID, AdminJobTargetState.Failed, code) }) };
                    SaveAdminJob(state, org, actorID, "Admin.Jobs.ExportFailed", failed);
                }
                }
            }
            catch (Exception cleanup)
            { if (!ReferenceEquals(primary, cleanup)) throw new AggregateException(primary, cleanup); }
            ExceptionDispatchInfo.Capture(primary).Throw(); throw;
        }
    }

    /// <summary>Registered Web publication of the SAME completed owning result.
    /// No client DTO invokes this port. Current original session and canonical job
    /// are checked again after actual serialization, before payload release.
    /// Retirement keeps the known committed IDs/status, without an export payload.</summary>
    public JsonElement SerializeOriginalExportOutcome(CurrentAdminSession session, AdminJob original)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(original);
        if (original.State != AdminJobState.Succeeded || original.RequestingAccountID != session.AccountID ||
            original.RunID is null || original.RunID == Guid.Empty)
            throw new ArgumentException("same_completed_original_export_required");
        try
        {
            return WithCurrentAdministrationSession(session, actorID =>
            {
                var state = Read(); var org = state.Organisations.Single(o => o.OrgID == original.OrgID);
                Demand(org, actorID, "Admin.Jobs.ExecuteExport"); DemandExport(org, actorID);
                var current = FindAdminJob(state, org.OrgID, original.JobID);
                if (current.State != AdminJobState.Succeeded || current.RunID != original.RunID ||
                    current.Revision != original.Revision || current.RequestingAccountID != actorID)
                    throw new AdministrationConflictException(AdministrationConflictCode.OriginalClaimChanged);
                var json = JsonSerializer.SerializeToElement(ObserveAdminJob(current, org, actorID));
                ExportObservationCheckpoint?.Invoke(AdminExportObservationPhase.AfterOutputSerialization);
                // The read-only admission performs its SAME issuer recheck after this
                // callback; a retired original never releases the built JSON payload.
                return json;
            });
        }
        catch (UnauthorizedAccessException)
        { return JsonSerializer.SerializeToElement(WithheldOriginalOutcome(original)); }
    }
    private static AdminJob WithheldOriginalOutcome(AdminJob original) => original with
    { ExportDocument = null, OutputIsCurrent = false, OutputWithheldReason = "CurrentOriginalSessionAndPermissionRequired",
        Targets = Array.AsReadOnly(original.Targets.ToArray()) };

    private OrganisationConfigurationExportPage CaptureExportPage(Organisation org, Guid actorID, int offset, int limit)
    {
        var count = checked(org.Roles.Count + org.Members.Count + 1);
        if (offset < 0 || offset > count) throw new ArgumentException("invalid_collection_cursor");
        // Canonical persisted order stays stable for this exact revision. Only selected
        // page records are materialised; credentials, invitations, profile data, actual
        // content and arbitrary forced/default setting VALUES are never exported.
        IEnumerable<OrganisationExportRecord> Records()
        {
            foreach (var role in org.Roles) yield return new("Role", role.RoleID,
                JsonSerializer.SerializeToElement(new { role.Name, role.Revision, role.IsOwner,
                    Grants = role.Grants.Order(StringComparer.Ordinal).ToArray(), Denials = role.Denials.Order(StringComparer.Ordinal).ToArray() }));
            foreach (var member in org.Members) yield return new("Membership", member.MembershipID,
                JsonSerializer.SerializeToElement(new { member.AccountID, member.RoleIDs, member.State, member.Revision }));
            yield return new("PolicyDependencies", org.Policy.PolicyID, JsonSerializer.SerializeToElement(new
            { org.Policy.Revision, org.Policy.State, BlockedCapabilities = org.Policy.BlockedCapabilities.Order(StringComparer.Ordinal).ToArray(),
                ForcedSettingKeys = org.Policy.ForcedSettings.Keys.Order(StringComparer.Ordinal).ToArray(),
                DefaultSettingKeys = org.Policy.Defaults.Keys.Order(StringComparer.Ordinal).ToArray() }));
        }
        var records = Records().Skip(offset).Take(limit).ToArray();
        var nextOffset = checked(offset + records.Length);
        var next = nextOffset < count ? WriteCollectionCursor(actorID, org, OrganisationExportAction, nextOffset, limit) : null;
        return new(1, org.OrgID, org.Name, org.Revision, org.Policy.Revision, Array.AsReadOnly(records), count, next, LifecycleNow);
    }
    private static void DemandExport(Organisation org, Guid actorID)
    { Demand(org, actorID, OrganisationExportAction); Demand(org, actorID, "Admin.Roles.List"); Demand(org, actorID, "Admin.Members.List"); Demand(org, actorID, "Admin.Policies.Get"); }
    private static AdminJob FindAdminJob(OrganisationState state, Guid orgID, Guid jobID) =>
        (state.AdminJobs ?? []).SingleOrDefault(job => job.OrgID == orgID && job.JobID == jobID)
        ?? throw new KeyNotFoundException("job_not_found");
    private static AdminJob ObserveAdminJob(AdminJob job, Organisation org, Guid actorID)
    {
        var outputAllowed = job.State == AdminJobState.Succeeded && job.RequestedOrganisationRevision == org.Revision &&
            Allowed(org, actorID, OrganisationExportAction) == "Allowed" && Allowed(org, actorID, "Admin.Roles.List") == "Allowed" &&
            Allowed(org, actorID, "Admin.Members.List") == "Allowed" && Allowed(org, actorID, "Admin.Policies.Get") == "Allowed";
        return job with { ExportDocument = outputAllowed ? job.ExportDocument : null, OutputIsCurrent = outputAllowed,
            Targets = Array.AsReadOnly(job.Targets.ToArray()) };
    }
    private void SaveAdminJob(OrganisationState state, Organisation org, Guid actorID, string action, AdminJob job) =>
        Save(state with { AdminJobs = (state.AdminJobs ?? []).Where(item => item.JobID != job.JobID).Append(job).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), org.OrgID, actorID, action, job.JobID,
                org.Revision, org.Revision, LifecycleNow, null)).ToArray() });

    private static void ValidateAdministrationState(OrganisationState state)
    {
        var receipts = state.RoleMutationReceipts ?? [];
        if (receipts.Any(r => r is null) || receipts.GroupBy(r => (r.OrgID, r.ActorID, r.Action, r.IdempotencyKey)).Any(group => group.Count() > 1) ||
            receipts.Any(r => r is null || r.SchemaVersion != 1 || r.ActorID == Guid.Empty || r.RoleID == Guid.Empty ||
                r.Action is not ("Admin.Roles.Update" or "Admin.Roles.Delete") || r.ExpectedRevision is < 1 or long.MaxValue ||
                r.ResultingRevision < 1 || r.ResultingRevision != r.ExpectedRevision + 1 || r.RoleRevision < 1 || string.IsNullOrWhiteSpace(r.IdempotencyKey) ||
                r.IdempotencyKey.Length > 256 || r.IdempotencyKey.Any(char.IsControl) ||
                r.RequestFingerprint is null || r.RequestFingerprint.Length != 64 || r.RequestFingerprint.Any(c => !Uri.IsHexDigit(c)) ||
                !state.Organisations.Any(org => org.OrgID == r.OrgID && org.Revision >= r.ResultingRevision)))
            throw new InvalidDataException("corrupt_role_mutation_receipt");
        var jobs = state.AdminJobs ?? [];
        if (jobs.Any(job => job is null) || jobs.Select(job => job.JobID).Distinct().Count() != jobs.Count ||
            jobs.GroupBy(job => (job.OrgID, job.RequestingAccountID, job.Action, job.IdempotencyKey)).Any(group => group.Count() > 1))
            throw new InvalidDataException("corrupt_admin_job_identity");
        foreach (var job in jobs)
        {
            var terminal = job.State is AdminJobState.Succeeded or AdminJobState.Failed or AdminJobState.Cancelled;
            if (job.SchemaVersion != 1 || job.JobID == Guid.Empty || job.RequestingAccountID == Guid.Empty ||
                job.Action != OrganisationExportAction || !Enum.IsDefined(job.State) || job.Revision < 1 ||
                job.RequestedOrganisationRevision < 1 || job.Offset < 0 || job.Limit is < 1 or > 200 ||
                string.IsNullOrWhiteSpace(job.IdempotencyKey) || job.IdempotencyKey.Length > 256 || job.IdempotencyKey.Any(char.IsControl) || job.CreatedAt == default ||
                terminal != job.CompletedAt.HasValue || job.CompletedAt is { } completed && completed < job.CreatedAt ||
                job.State is AdminJobState.Running or AdminJobState.Succeeded or AdminJobState.Failed && job.RunID is null ||
                job.State is AdminJobState.Pending or AdminJobState.Cancelled && job.RunID is not null ||
                job.RunID == Guid.Empty || job.OutputIsCurrent || job.OutputWithheldReason is not null ||
                (job.State == AdminJobState.Cancelled) != (job.CancellationIdempotencyKey is not null && job.CancelledFromRevision is not null && job.CancelledByAccountID is not null) ||
                job.CancelledFromRevision is { } originalRevision && (originalRevision < 1 || originalRevision >= long.MaxValue || job.Revision != originalRevision + 1) ||
                job.CancelledByAccountID == Guid.Empty ||
                job.CancellationIdempotencyKey is { } cancellationKey && (string.IsNullOrWhiteSpace(cancellationKey) || cancellationKey.Length > 256 || cancellationKey.Any(char.IsControl)) ||
                job.Targets is null || job.Targets.Count != 1 ||
                job.Targets[0] is null || job.Targets[0].TargetID != job.OrgID || !Enum.IsDefined(job.Targets[0].State) ||
                (job.State == AdminJobState.Succeeded) != (job.ExportDocument is not null) ||
                job.State is AdminJobState.Failed or AdminJobState.Cancelled &&
                    (string.IsNullOrWhiteSpace(job.Code) || job.Code.Length > 128 || job.Code.Any(char.IsControl)) ||
                job.Targets[0].State != (job.State switch { AdminJobState.Succeeded => AdminJobTargetState.Succeeded,
                    AdminJobState.Failed => AdminJobTargetState.Failed, AdminJobState.Cancelled => AdminJobTargetState.Skipped,
                    _ => AdminJobTargetState.Pending }) || job.Targets[0].Code != job.Code ||
                job.ExportDocument is { } document && System.Text.Encoding.UTF8.GetByteCount(document) > MaximumExportPageBytes ||
                !state.Organisations.Any(org => org.OrgID == job.OrgID && org.Revision >= job.RequestedOrganisationRevision))
                throw new InvalidDataException("corrupt_admin_job_state");
            if (job.ExportDocument is { } output)
            {
                OrganisationConfigurationExportPage page;
                try { page = JsonSerializer.Deserialize<OrganisationConfigurationExportPage>(output)
                    ?? throw new InvalidDataException("corrupt_admin_export"); }
                catch (JsonException error) { throw new InvalidDataException("corrupt_admin_export", error); }
                if (page.SchemaVersion != 1 || page.OrgID != job.OrgID || page.OrganisationRevision != job.RequestedOrganisationRevision ||
                    page.PolicyRevision < 1 || page.ObservedAt == default ||
                    page.Records is null || page.TotalRecords < page.Records.Count || page.Records.Count > job.Limit || page.Records.Any(record => record is null ||
                    record.CanonicalID == Guid.Empty || record.Kind is not ("Role" or "Membership" or "PolicyDependencies")))
                    throw new InvalidDataException("corrupt_admin_export");
            }
        }
    }
}
