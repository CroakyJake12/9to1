namespace NineToOne.Accounts;

public sealed partial class OrganisationService
{
    /// <summary>Only the registered owning service may call this with an AccountID obtained
    /// from its current verified session while retaining that session's authority lease.
    /// The callback synchronously admits or commits the canonical owned resource/job; it
    /// must not start asynchronous work or return authority for later reuse.</summary>
    public T WithCurrentAuthority<T>(Guid authenticatedAccountID, Guid organisationID, string capabilityOrAction,
        IReadOnlyList<string> canonicalObjectScopes, long expectedPolicyRevision, Func<T> admitOwnedOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalObjectScopes);
        ArgumentNullException.ThrowIfNull(admitOwnedOperation);
        if (authenticatedAccountID == Guid.Empty || organisationID == Guid.Empty)
            throw new UnauthorizedAccessException("verified_organisation_actor_required");
        if (string.IsNullOrWhiteSpace(capabilityOrAction) || capabilityOrAction.Length > 256 ||
            expectedPolicyRevision < 1 || canonicalObjectScopes.Count > 128)
            throw new ArgumentException("bounded_organisation_admission_required");
        if (IsAsyncResult(typeof(T))) throw new ArgumentException("synchronous_owning_admission_required");
        // Capture the caller's declared identifiers before the lock; the actual owning
        // resolver still proves their current canonical organisation binding below.
        var scopes = canonicalObjectScopes.ToArray();
        if (scopes.Any(scope => string.IsNullOrWhiteSpace(scope) || scope.Length > 2048))
            throw new ArgumentException("bounded_canonical_object_scope_required");
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var org = Read().Organisations.SingleOrDefault(o => o.OrgID == organisationID)
            ?? throw new OrganisationAccessException("OrganisationNotFound");
        if (org.Policy.Revision != expectedPolicyRevision)
            throw new OrganisationAccessException("RevisionConflict");
        Demand(org, authenticatedAccountID, capabilityOrAction);
        if (scopes.Length == 0 && !OrganisationGlobalActions.Contains(capabilityOrAction))
            throw new OrganisationAccessException("ObjectScopeRequired");
        if (scopes.Any(scope => objectScopesAuthority is null ||
            !objectScopesAuthority.Allows(authenticatedAccountID, organisationID, capabilityOrAction, scope)))
            throw new OrganisationAccessException("ObjectScopeDenied");
        cancellationToken.ThrowIfCancellationRequested();
        var result = admitOwnedOperation();
        if (result is not null && IsAsyncResult(result.GetType()))
            throw new ArgumentException("synchronous_owning_admission_required");
        // Do not report cancellation after an owning commit has already succeeded.
        // The owning result reports that actual effect; remote effects are separate.
        return result;
    }

    private static bool IsAsyncResult(Type resultType) => typeof(Task).IsAssignableFrom(resultType) ||
        resultType == typeof(ValueTask) || resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(ValueTask<>);
}

/// <summary>Registered local owning callers use the existing authenticated session authority.
/// No AccountID, role or membership assertion is accepted from a client DTO. This adapter
/// does not implement login or remote OIDC validation.</summary>
public sealed class AuthenticatedOrganisationOwningOperations(CakeIdentityService identity,
    IOrganisationOwningOperationAuthority organisations)
{
    public T WithCurrentSessionAuthority<T>(string accessToken, Guid organisationID, string capabilityOrAction,
        IReadOnlyList<string> canonicalObjectScopes, long expectedPolicyRevision,
        Func<Guid, Action, T> admitOwnedOperation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonicalObjectScopes);
        ArgumentNullException.ThrowIfNull(admitOwnedOperation);
        if (canonicalObjectScopes.Count > 128) throw new ArgumentException("bounded_canonical_object_scope_required");
        var scopes = canonicalObjectScopes.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return identity.WithCurrentSession(accessToken, session => organisations.WithCurrentAuthority(
            session.AccountID, organisationID, capabilityOrAction, scopes, expectedPolicyRevision,
            () =>
            {
                var originalThread = Environment.CurrentManagedThreadId;
                var admissionActive = true;
                void RecheckCurrentAuthority()
                {
                    if (!Volatile.Read(ref admissionActive) || Environment.CurrentManagedThreadId != originalThread)
                        throw new UnauthorizedAccessException("original_owning_admission_not_active");
                    // Original session/organisation leases remain held. The registered
                    // owner must call this after acquiring its actual resource lock,
                    // immediately before its synchronous mutation or admission. Its
                    // scope resolver must inspect already-held canonical state without
                    // reacquiring a nonreentrant provider/resource gate.
                    var current = identity.Authenticate(accessToken);
                    if (current.SessionID != session.SessionID || current.AccountID != session.AccountID)
                        throw new UnauthorizedAccessException("current_session_binding_changed");
                    organisations.WithCurrentAuthority(current.AccountID, organisationID, capabilityOrAction,
                        scopes, expectedPolicyRevision, () => 0, cancellationToken);
                }
                try
                {
                    RecheckCurrentAuthority();
                    // A pre-owner check alone cannot cover expiry while waiting for that
                    // owner's lock. This trusted callback contract supplies the same live
                    // recheck; it is not a sandbox for arbitrary in-process callbacks.
                    return admitOwnedOperation(session.AccountID, RecheckCurrentAuthority);
                }
                finally { Volatile.Write(ref admissionActive, false); }
            }, cancellationToken));
    }
}
