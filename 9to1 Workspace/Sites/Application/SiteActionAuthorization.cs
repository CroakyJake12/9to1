namespace HavenOS.Apps.Sites.Application;

public enum SiteActionRisk { ReadOnly, Ordinary, ExternalConsequence, Destructive, SecuritySensitive, CredentialSensitive }
public enum SiteAuthorizationState { Allowed, PendingApproval, Denied, Unavailable }

public sealed record SiteAuthorizationRequest(
    string ActionId,
    SiteActionRisk Risk,
    IReadOnlyList<string> ObjectIds,
    IReadOnlyList<string> Scopes,
    string ImpactSummary);

public sealed record SiteAuthorizationDecision(
    SiteAuthorizationState State,
    string RequestId,
    string? Reason);

/// <summary>Adapter boundary for the Home permission broker. No permissive fallback is supplied by Sites.</summary>
public interface ISiteActionAuthorizer
{
    Task<SiteAuthorizationDecision> RequestAsync(SiteAuthorizationRequest request, CancellationToken cancellationToken);
}
