using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Accounts.Specs")]

namespace NineToOne.Accounts;

public enum AdministrationConflictCode
{ RevisionConflict, IdempotencyConflict, RoleInUse, JobNotCancellable, OriginalOutcomeRequiresReconciliation, QuotaExceeded, ExportLimitExceeded, OriginalClaimChanged }
public sealed class AdministrationConflictException(AdministrationConflictCode code) : InvalidOperationException(code.ToString())
{
    public string Code { get; } = code.ToString();
    public bool RequiresOriginalOutcomeReconciliation { get; } = code is AdministrationConflictCode.OriginalOutcomeRequiresReconciliation or AdministrationConflictCode.OriginalClaimChanged;
}

// These are observations of the SAME organisation authority, never bearer grants.
public sealed record OrganisationRoleMutationReceipt(Guid OrgID, Guid ActorID, string Action,
    string IdempotencyKey, long ExpectedRevision, long ResultingRevision, Guid RoleID,
    string RequestFingerprint, long RoleRevision, int SchemaVersion = 1);
public sealed record OrganisationRoleMutationResult(Guid OrgID, long OrganisationRevision,
    OrganisationRole? Role, OrganisationRoleMutationReceipt Receipt, bool Replayed);
public sealed record OrganisationEffectiveRolePermissions(Guid OrgID, Guid AccountID,
    long OrganisationRevision, long PolicyRevision, IReadOnlySet<string> CandidateCapabilities,
    IReadOnlySet<string> DeniedCapabilities, bool HasWildcardCandidate,
    bool RequiresCurrentOwningObjectAdmission = true);

internal enum AdminExportObservationPhase { AfterCompletedSave, AfterOutputSerialization }

public enum AdminJobState { Pending, Running, Succeeded, Failed, Cancelled }
public enum AdminJobTargetState { Pending, Succeeded, Failed, Skipped, RolledBack }
public sealed record AdminJobTargetResult(Guid TargetID, AdminJobTargetState State, string? Code);
/// <summary>One bounded configuration-export page, not content or a credential export.
/// Only the registered owning service claims the exact RunID and publishes its result.
/// Running after a restart needs original outcome reconciliation; it is not retried.</summary>
public sealed record AdminJob(Guid JobID, Guid OrgID, Guid RequestingAccountID,
    string Action, string IdempotencyKey, long RequestedOrganisationRevision,
    int Offset, int Limit, AdminJobState State, Guid? RunID, long Revision,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? Code,
    string? ExportDocument, IReadOnlyList<AdminJobTargetResult> Targets,
    int SchemaVersion = 1, bool OutputIsCurrent = false, string? CancellationIdempotencyKey = null, long? CancelledFromRevision = null, Guid? CancelledByAccountID = null, string? OutputWithheldReason = null);
public sealed record OrganisationExportRecord(string Kind, Guid CanonicalID,
    System.Text.Json.JsonElement Configuration);
public sealed record OrganisationConfigurationExportPage(int SchemaVersion, Guid OrgID,
    string Name, long OrganisationRevision, long PolicyRevision,
    IReadOnlyList<OrganisationExportRecord> Records, int TotalRecords, string? NextCursor,
    DateTimeOffset ObservedAt);

/// <summary>Issuer-private binding to the SAME original local CAKE session. The
/// observed account ID is not a grant. Every actual administration phase re-enters
/// its original issuer; this does not implement remote Worker authentication.</summary>
public sealed class CurrentAdminSession
{
    private readonly CakeIdentityService identity;
    private readonly string accessToken;
    private readonly Guid sessionID;
    public Guid AccountID { get; }
    internal CurrentAdminSession(CakeIdentityService issuer, string token, CakeSession original)
    { identity = issuer; accessToken = token; sessionID = original.SessionID; AccountID = original.AccountID; }

    internal T WithCurrentActor<T>(Func<Guid, Action, T> operation) => identity.WithCurrentSession(accessToken, current =>
    {
        if (current.SessionID != sessionID || current.AccountID != AccountID)
            throw new UnauthorizedAccessException("current_session_binding_changed");
        var thread = Environment.CurrentManagedThreadId;
        var active = true;
        void Recheck()
        {
            if (!Volatile.Read(ref active) || Environment.CurrentManagedThreadId != thread)
                throw new UnauthorizedAccessException("original_administration_admission_not_active");
            var latest = identity.Authenticate(accessToken);
            if (latest.SessionID != sessionID || latest.AccountID != AccountID)
                throw new UnauthorizedAccessException("current_session_binding_changed");
        }
        try { return operation(AccountID, Recheck); }
        finally { Volatile.Write(ref active, false); }
    });
}

public sealed class AuthenticatedAdminManagementOperations(CakeIdentityService identity)
{
    public CurrentAdminSession Open(string accessToken) => identity.WithCurrentSession(accessToken,
        session => new CurrentAdminSession(identity, accessToken, session));
}

public sealed partial class OrganisationService
{
    private sealed class AdministrationAdmission(Action recheck)
    {
        public Action Recheck { get; } = recheck;
        public bool Committed { get; set; }
    }
    private readonly AsyncLocal<AdministrationAdmission?> administrationAdmission = new();

    /// <summary>Registered server transport only: original session, then SAME
    /// organisation lock. Synchronous callbacks do not issue reusable authority.
    /// Reads recheck before return; each durable write rechecks immediately before
    /// commit. A committed mutation retains its known outcome without a late waiver.</summary>
    public T WithCurrentAdministrationSession<T>(CurrentAdminSession session, Func<Guid, T> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(operation);
        if (IsAsyncResult(typeof(T))) throw new ArgumentException("synchronous_administration_required");
        cancellationToken.ThrowIfCancellationRequested();
        return session.WithCurrentActor((actorID, recheck) =>
        {
            using var lease = DurableState.Acquire(statePath);
            cancellationToken.ThrowIfCancellationRequested(); recheck();
            var previous = administrationAdmission.Value;
            var admitted = new AdministrationAdmission(recheck);
            administrationAdmission.Value = admitted;
            try
            {
                var result = operation(actorID);
                if (result is not null && IsAsyncResult(result.GetType()))
                    throw new ArgumentException("synchronous_administration_required");
                if (!admitted.Committed) recheck();
                return result;
            }
            finally { administrationAdmission.Value = previous; }
        });
    }
}
