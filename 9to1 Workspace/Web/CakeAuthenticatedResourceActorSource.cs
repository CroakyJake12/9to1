using Haven.Application;
using NineToOne.Accounts;

namespace NineToOne.Web;

/// <summary>Request-scoped actor derived only from the server-verified bearer session. No JSON identity/role input.</summary>
public sealed class CakeAuthenticatedResourceActorSource(CakeIdentityService identity,Func<string> trustedRequestToken)
    : IAuthenticatedResourceActorSource
{
    public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var session=identity.Authenticate(trustedRequestToken());
            // Earlier envelopes without registered-client provenance cannot inherit a caller trust grant.
            if(string.IsNullOrWhiteSpace(session.RegisteredClientID))return ValueTask.FromResult<AuthenticatedResourceActor?>(null);
            return ValueTask.FromResult<AuthenticatedResourceActor?>(new(
                "cake-client:"+session.RegisteredClientID,"cake-account:"+session.AccountID.ToString("D"),session.AccountID,null,
                session.SessionID.ToString("D")));
        }
        catch(UnauthorizedAccessException){return ValueTask.FromResult<AuthenticatedResourceActor?>(null);}
    }
}
