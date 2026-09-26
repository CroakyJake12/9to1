using System.Security.Cryptography;
using System.Text;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;

namespace HavenOS.Apps.Sites.Application;

public sealed record SiteSlugOperation(SiteSlugReservation Reservation, Uri? PublicUrl, Guid VerificationId);
public sealed record ChangeSiteSlugOptions(bool CreateRedirect, DateTimeOffset? RedirectExpiresAt, long ExpectedSiteRevision, bool UserConfirmed);
public sealed record AttachSiteDomainRequest(Guid SiteId, Guid EnvironmentId, string Hostname, bool UserConfirmed);
public sealed record DomainChallengeInstructions(Guid ChallengeId, Guid DomainBindingId, string RecordType, string RecordName, string RecordValue, DateTimeOffset ExpiresAt, int Generation);

public interface ISiteTxtRecordResolver
{
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string canonicalRecordName, CancellationToken cancellationToken);
}

public interface ISiteDomainChallengePolicy
{
    string Version { get; }
    TimeSpan Lifetime { get; }
}

public sealed class SitePublicIdentityService(
    FileSiteWorkspaceStore store,
    SiteNameVerificationService nameVerification,
    ISiteActionAuthorizer authorizer,
    ISiteDomainChallengePolicy challengePolicy,
    ISiteTxtRecordResolver txtResolver)
{
    public async Task<SiteApiResult<SiteSlugOperation>> ReserveSlugAsync(Guid siteId, string input, CancellationToken cancellationToken = default)
    {
        string slug;
        try { slug = SiteAddressRules.NormalizeSlug(input); }
        catch (SiteOperationException ex) { return SiteApiResult<SiteSlugOperation>.Failure(ex.Error); }

        var projectResult = await GetProjectAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (projectResult.Error is { } projectError) return SiteApiResult<SiteSlugOperation>.Failure(projectError);
        var verificationResult = await nameVerification.VerifyAsync(siteId, slug, SitePublicNameKind.FirstPartySlug, projectResult.Value!.Name, cancellationToken).ConfigureAwait(false);
        if (verificationResult.Error is { } verificationError) return SiteApiResult<SiteSlugOperation>.Failure(verificationError);
        var verification = verificationResult.Value!;

        var authorization = await AuthorizeAsync("sites.slug.reserve", "Reserve a first-party public site URL", [siteId.ToString("D")], ["sites.slug.reserve"], cancellationToken).ConfigureAwait(false);
        if (authorization.Error is { } authorizationError) return SiteApiResult<SiteSlugOperation>.Failure(authorizationError);

        try
        {
            var reservation = await store.MutateAsync(state =>
            {
                var project = state.Projects.SingleOrDefault(candidate => candidate.SiteId == siteId)
                    ?? throw new SiteOperationException(new SiteApiError("SiteNotFound", "The Sites project was not found.", siteId.ToString(), false));
                var current = state.SlugReservations.SingleOrDefault(candidate => candidate.SiteId == siteId && candidate.ReleasedAt is null);
                if (current is not null && string.Equals(current.Slug, slug, StringComparison.Ordinal))
                    return (state with
                    {
                        SlugReservations = state.SlugReservations.Select(candidate => candidate == current ? current with { NameSafetyState = verification.State } : candidate).ToArray()
                    }, current with { NameSafetyState = verification.State });
                if (state.SlugReservations.Any(candidate => candidate.ReleasedAt is null && string.Equals(candidate.Slug, slug, StringComparison.Ordinal)))
                    throw new SiteOperationException(new SiteApiError("SlugUnavailable", "This first-party site slug is already reserved.", slug, true));
                var next = new SiteSlugReservation(slug, project.SiteId, verification.State, DateTimeOffset.UtcNow, null);
                return (state with { SlugReservations = [.. state.SlugReservations, next] }, next);
            }, cancellationToken).ConfigureAwait(false);
            var activeUrl = verification.State == SiteNameVerificationState.Passed ? SiteAddressRules.FirstPartyUrl(slug) : null;
            return SiteApiResult<SiteSlugOperation>.Success(new(reservation, activeUrl, verification.VerificationId));
        }
        catch (SiteOperationException ex) { return SiteApiResult<SiteSlugOperation>.Failure(ex.Error); }
    }

    public async Task<SiteApiResult<SiteSlugOperation>> ChangeSlugAsync(Guid siteId, string input, ChangeSiteSlugOptions options, CancellationToken cancellationToken = default)
    {
        if (!options.UserConfirmed)
            return SiteApiResult<SiteSlugOperation>.Failure(new SiteApiError("ConfirmationRequired", "Changing a public slug requires an explicit user confirmation.", "ChangeSlug", false));
        string slug;
        try { slug = SiteAddressRules.NormalizeSlug(input); }
        catch (SiteOperationException ex) { return SiteApiResult<SiteSlugOperation>.Failure(ex.Error); }
        var projectResult = await GetProjectAsync(siteId, cancellationToken).ConfigureAwait(false);
        if (projectResult.Error is { } projectError) return SiteApiResult<SiteSlugOperation>.Failure(projectError);
        var project = projectResult.Value!;
        if (project.Revision != options.ExpectedSiteRevision)
            return SiteApiResult<SiteSlugOperation>.Failure(new SiteApiError("RevisionConflict", "Reload the Sites project before changing its public slug.", siteId.ToString(), true));
        var verificationResult = await nameVerification.VerifyAsync(siteId, slug, SitePublicNameKind.FirstPartySlug, project.Name, cancellationToken).ConfigureAwait(false);
        if (verificationResult.Error is { } verificationError) return SiteApiResult<SiteSlugOperation>.Failure(verificationError);
        var verification = verificationResult.Value!;
        if (verification.State != SiteNameVerificationState.Passed)
            return SiteApiResult<SiteSlugOperation>.Failure(new SiteApiError(
                verification.State == SiteNameVerificationState.Unavailable ? "NameAssessmentUnavailable" : "NameReviewRequired",
                "The new slug has not passed name-safety review, so the current public address was left unchanged.",
                verification.VerificationId.ToString(), verification.State is SiteNameVerificationState.Unavailable or SiteNameVerificationState.PendingReview));

        var authorization = await AuthorizeAsync("sites.slug.change", "Change the public site URL", [siteId.ToString("D"), project.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)], ["sites.slug.change", "sites.redirect.configure"], cancellationToken).ConfigureAwait(false);
        if (authorization.Error is { } authorizationError) return SiteApiResult<SiteSlugOperation>.Failure(authorizationError);
        try
        {
            var result = await store.MutateAsync(state =>
            {
                var projectIndex = IndexOf(state.Projects, candidate => candidate.SiteId == siteId);
                if (projectIndex < 0) throw new SiteOperationException(new SiteApiError("SiteNotFound", "The Sites project was not found.", siteId.ToString(), false));
                var currentProject = state.Projects[projectIndex];
                if (currentProject.Revision != options.ExpectedSiteRevision)
                    throw new SiteOperationException(new SiteApiError("RevisionConflict", "Reload the Sites project before changing its public slug.", siteId.ToString(), true));
                var old = state.SlugReservations.SingleOrDefault(candidate => candidate.SiteId == siteId && candidate.ReleasedAt is null)
                    ?? throw new SiteOperationException(new SiteApiError("SlugNotReserved", "The site has no current first-party slug to change.", siteId.ToString(), false));
                if (state.SlugReservations.Any(candidate => candidate.ReleasedAt is null && candidate.SiteId != siteId && string.Equals(candidate.Slug, slug, StringComparison.Ordinal)))
                    throw new SiteOperationException(new SiteApiError("SlugUnavailable", "This first-party site slug is already reserved.", slug, true));
                var now = DateTimeOffset.UtcNow;
                var newReservation = new SiteSlugReservation(slug, siteId, SiteNameVerificationState.Passed, now, null);
                var reservations = state.SlugReservations.Select(candidate => candidate == old ? old with { ReleasedAt = now } : candidate).Append(newReservation).ToArray();
                var redirects = currentProject.Redirects;
                if (options.CreateRedirect)
                {
                    redirects = [.. redirects, new SiteRedirect(Guid.NewGuid(), $"/{old.Slug}", $"/{slug}", 308, options.RedirectExpiresAt)];
                }
                var updatedProject = currentProject with { Revision = checked(currentProject.Revision + 1), UpdatedAt = now, Redirects = redirects };
                var projects = state.Projects.ToArray();
                projects[projectIndex] = updatedProject;
                return (state with { Projects = projects, SlugReservations = reservations }, newReservation);
            }, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<SiteSlugOperation>.Success(new(result, SiteAddressRules.FirstPartyUrl(slug), verification.VerificationId));
        }
        catch (SiteOperationException ex) { return SiteApiResult<SiteSlugOperation>.Failure(ex.Error); }
    }

    public async Task<SiteApiResult<DomainBinding>> AttachDomainAsync(AttachSiteDomainRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SiteId == Guid.Empty || request.EnvironmentId == Guid.Empty)
            return SiteApiResult<DomainBinding>.Failure(new SiteApiError("InvalidInput", "SiteID and environment ID are required.", "AttachDomain", false));
        if (!request.UserConfirmed)
            return SiteApiResult<DomainBinding>.Failure(new SiteApiError("ConfirmationRequired", "Attaching a custom domain requires explicit confirmation.", "AttachDomain", false));
        NormalizedHostname normalized;
        try { normalized = SiteAddressRules.NormalizeHostname(request.Hostname); }
        catch (SiteOperationException ex) { return SiteApiResult<DomainBinding>.Failure(ex.Error); }
        var projectResult = await GetProjectAsync(request.SiteId, cancellationToken).ConfigureAwait(false);
        if (projectResult.Error is { } projectError) return SiteApiResult<DomainBinding>.Failure(projectError);
        var verificationResult = await nameVerification.VerifyAsync(request.SiteId, normalized.DisplayName, SitePublicNameKind.CustomDomain, projectResult.Value!.Name, cancellationToken).ConfigureAwait(false);
        if (verificationResult.Error is { } verificationError) return SiteApiResult<DomainBinding>.Failure(verificationError);
        var nameState = verificationResult.Value!.State;
        var authorization = await AuthorizeAsync("sites.domain.attach", "Attach a custom domain to this site", [request.SiteId.ToString("D"), normalized.AsciiName], ["sites.domain.attach", "sites.domain.verify"], cancellationToken).ConfigureAwait(false);
        if (authorization.Error is { } authorizationError) return SiteApiResult<DomainBinding>.Failure(authorizationError);
        try
        {
            var domain = await store.MutateAsync(state =>
            {
                var project = state.Projects.SingleOrDefault(candidate => candidate.SiteId == request.SiteId);
                if (project is null)
                    throw new SiteOperationException(new SiteApiError("SiteNotFound", "The Sites project was not found.", request.SiteId.ToString(), false));
                if (!project.Environments.Any(environment => environment.EnvironmentId == request.EnvironmentId && environment.IsEnabled))
                    throw new SiteOperationException(new SiteApiError("EnvironmentNotFound", "The selected enabled environment does not belong to this site.", request.EnvironmentId.ToString(), false));
                var claim = state.Domains.FirstOrDefault(candidate => string.Equals(candidate.HostnameAscii, normalized.AsciiName, StringComparison.Ordinal) && candidate.OwnershipState == SiteDomainVerificationState.Verified);
                if (claim is not null && claim.SiteId != request.SiteId)
                    throw new SiteOperationException(new SiteApiError("DomainConflict", "This domain is already verified for another site.", normalized.AsciiName, false));
                var existing = state.Domains.FirstOrDefault(candidate => candidate.SiteId == request.SiteId && string.Equals(candidate.HostnameAscii, normalized.AsciiName, StringComparison.Ordinal));
                if (existing is not null) return (state, existing);
                var now = DateTimeOffset.UtcNow;
                var binding = new DomainBinding(Guid.NewGuid(), request.SiteId, request.EnvironmentId, normalized.AsciiName, normalized.DisplayName, false, SiteDomainVerificationState.Pending, nameState, SiteTlsState.Unknown,
                    [new SiteDnsRequirement("TXT", SiteAddressRules.DomainChallengeRecordName(normalized.AsciiName), "", "Ownership verification", true)], 1, now, now);
                var projectIndex = IndexOf(state.Projects, project => project.SiteId == request.SiteId);
                var project = state.Projects[projectIndex] with { DomainBindingIds = [.. state.Projects[projectIndex].DomainBindingIds, binding.DomainBindingId], Revision = checked(state.Projects[projectIndex].Revision + 1), UpdatedAt = now };
                var projects = state.Projects.ToArray();
                projects[projectIndex] = project;
                return (state with { Projects = projects, Domains = [.. state.Domains, binding] }, binding);
            }, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<DomainBinding>.Success(domain);
        }
        catch (SiteOperationException ex) { return SiteApiResult<DomainBinding>.Failure(ex.Error); }
    }

    public async Task<SiteApiResult<DomainChallengeInstructions>> BeginDomainVerificationAsync(Guid domainBindingId, CancellationToken cancellationToken = default)
    {
        var domainResult = await GetDomainAsync(domainBindingId, cancellationToken).ConfigureAwait(false);
        if (domainResult.Error is { } domainError) return SiteApiResult<DomainChallengeInstructions>.Failure(domainError);
        var domain = domainResult.Value!;
        var authorization = await AuthorizeAsync("sites.domain.begin-verification", "Generate a one-time domain ownership challenge", [domain.SiteId.ToString("D"), domain.DomainBindingId.ToString("D"), domain.HostnameAscii], ["sites.domain.verify"], cancellationToken).ConfigureAwait(false);
        if (authorization.Error is { } authorizationError) return SiteApiResult<DomainChallengeInstructions>.Failure(authorizationError);
        if (challengePolicy.Lifetime <= TimeSpan.Zero)
            return SiteApiResult<DomainChallengeInstructions>.Failure(new SiteApiError("CapabilityUnavailable", "The configured domain challenge lifetime is invalid.", "BeginDomainVerification", false));
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        CryptographicOperations.ZeroMemory(tokenBytes);
        var tokenValue = $"9to1-site-verification=v1;site={domain.SiteId:N};challenge={token}";
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenValue)));
        var recordName = SiteAddressRules.DomainChallengeRecordName(domain.HostnameAscii);
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(challengePolicy.Lifetime);
        try
        {
            var generation = await store.MutateAsync(state =>
            {
                var currentDomain = state.Domains.SingleOrDefault(candidate => candidate.DomainBindingId == domainBindingId)
                    ?? throw new SiteOperationException(new SiteApiError("DomainBindingNotFound", "The domain binding was not found.", domainBindingId.ToString(), false));
                var nextGeneration = state.DomainChallenges.Where(challenge => challenge.DomainBindingId == domainBindingId).Select(challenge => challenge.Generation).DefaultIfEmpty(0).Max() + 1;
                var challenges = state.DomainChallenges.Select(challenge => challenge.DomainBindingId == domainBindingId && challenge.VerifiedAt is null && challenge.ExpiresAt > now ? challenge with { ExpiresAt = now } : challenge).ToArray();
                var newChallenge = new DomainOwnershipChallenge(Guid.NewGuid(), domainBindingId, currentDomain.SiteId, currentDomain.HostnameAscii, recordName, tokenHash, now, expires, null, nextGeneration);
                challenges = [.. challenges, newChallenge];
                var domains = state.Domains.Select(candidate => candidate.DomainBindingId == domainBindingId
                    ? candidate with { OwnershipState = SiteDomainVerificationState.Pending, RequiredRecords = [new SiteDnsRequirement("TXT", recordName, tokenValue, "Domain ownership verification", true)], Revision = checked(candidate.Revision + 1), UpdatedAt = now }
                    : candidate).ToArray();
                return (state with { Domains = domains, DomainChallenges = challenges }, newChallenge.Generation);
            }, cancellationToken).ConfigureAwait(false);
            var challengeId = await store.ReadAsync(state => state.DomainChallenges.Single(challenge => challenge.DomainBindingId == domainBindingId && challenge.Generation == generation).ChallengeId, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<DomainChallengeInstructions>.Success(new(challengeId, domainBindingId, "TXT", recordName, tokenValue, expires, generation));
        }
        catch (SiteOperationException ex) { return SiteApiResult<DomainChallengeInstructions>.Failure(ex.Error); }
    }

    public async Task<SiteApiResult<DomainBinding>> VerifyDomainAsync(Guid domainBindingId, CancellationToken cancellationToken = default)
    {
        var domainResult = await GetDomainAsync(domainBindingId, cancellationToken).ConfigureAwait(false);
        if (domainResult.Error is { } domainError) return SiteApiResult<DomainBinding>.Failure(domainError);
        var domain = domainResult.Value!;
        var authorization = await AuthorizeAsync("sites.domain.verify", "Verify domain ownership against its DNS challenge", [domain.SiteId.ToString("D"), domain.DomainBindingId.ToString("D"), domain.HostnameAscii], ["sites.domain.verify"], cancellationToken).ConfigureAwait(false);
        if (authorization.Error is { } authorizationError) return SiteApiResult<DomainBinding>.Failure(authorizationError);

        DomainOwnershipChallenge? challenge;
        try
        {
            challenge = await store.ReadAsync(state => state.DomainChallenges.Where(candidate => candidate.DomainBindingId == domainBindingId && candidate.VerifiedAt is null)
                .OrderByDescending(candidate => candidate.Generation).FirstOrDefault(), cancellationToken).ConfigureAwait(false);
        }
        catch (SiteOperationException ex) { return SiteApiResult<DomainBinding>.Failure(ex.Error); }
        if (challenge is null)
            return SiteApiResult<DomainBinding>.Failure(new SiteApiError("DomainVerificationNotStarted", "Generate a fresh DNS challenge before verifying this domain.", domainBindingId.ToString(), true));
        var now = DateTimeOffset.UtcNow;
        if (challenge.ExpiresAt <= now)
        {
            await SetDomainStateAsync(domainBindingId, SiteDomainVerificationState.Expired, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<DomainBinding>.Failure(new SiteApiError("DomainVerificationExpired", "The DNS challenge expired. Generate a new challenge and update the TXT record.", domainBindingId.ToString(), true));
        }

        IReadOnlyList<string> records;
        try { records = await txtResolver.GetTxtRecordsAsync(challenge.RecordName, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return SiteApiResult<DomainBinding>.Failure(new SiteApiError("Cancelled", "Domain verification was cancelled; no DNS state was changed.", domainBindingId.ToString(), true)); }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException)
        { return SiteApiResult<DomainBinding>.Failure(new SiteApiError("DnsLookupUnavailable", "DNS TXT records could not be checked. Retry when the DNS resolver is available.", domainBindingId.ToString(), true, Detail: ex.GetType().Name)); }

        var found = records.Any(record => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(record.Trim().Trim('"'))),
            Convert.FromHexString(challenge.TokenHash)));
        if (!found)
        {
            await SetDomainStateAsync(domainBindingId, SiteDomainVerificationState.Pending, cancellationToken).ConfigureAwait(false);
            return SiteApiResult<DomainBinding>.Failure(new SiteApiError("DomainVerificationPending", "The required TXT challenge was not found yet. Check the record and retry after DNS propagation.", domainBindingId.ToString(), true, TimeSpan.FromMinutes(1)));
        }

        try
        {
            var verified = await store.MutateAsync(state =>
            {
                var index = IndexOf(state.Domains, candidate => candidate.DomainBindingId == domainBindingId);
                if (index < 0) throw new SiteOperationException(new SiteApiError("DomainBindingNotFound", "The domain binding was removed during verification.", domainBindingId.ToString(), false));
                var current = state.Domains[index];
                var currentChallenge = state.DomainChallenges.SingleOrDefault(candidate => candidate.ChallengeId == challenge.ChallengeId);
                if (currentChallenge is null || currentChallenge.ExpiresAt <= DateTimeOffset.UtcNow || currentChallenge.TokenHash != challenge.TokenHash)
                    throw new SiteOperationException(new SiteApiError("DomainVerificationExpired", "The domain challenge changed or expired during DNS verification. Start a new challenge.", domainBindingId.ToString(), true));
                var duplicate = state.Domains.FirstOrDefault(candidate => candidate.DomainBindingId != domainBindingId && candidate.SiteId != current.SiteId &&
                    candidate.OwnershipState == SiteDomainVerificationState.Verified && string.Equals(candidate.HostnameAscii, current.HostnameAscii, StringComparison.Ordinal));
                if (duplicate is not null)
                {
                    var conflict = current with { OwnershipState = SiteDomainVerificationState.Conflict, Revision = checked(current.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow };
                    var conflictDomains = state.Domains.ToArray();
                    conflictDomains[index] = conflict;
                    return (state with { Domains = conflictDomains }, (DomainBinding?)null);
                }
                var updated = current with { OwnershipState = SiteDomainVerificationState.Verified, Revision = checked(current.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow };
                var domains = state.Domains.ToArray();
                domains[index] = updated;
                var challenges = state.DomainChallenges.Select(candidate => candidate.ChallengeId == currentChallenge.ChallengeId ? candidate with { VerifiedAt = DateTimeOffset.UtcNow } : candidate).ToArray();
                return (state with { Domains = domains, DomainChallenges = challenges }, (DomainBinding?)updated);
            }, cancellationToken).ConfigureAwait(false);
            return verified is null
                ? SiteApiResult<DomainBinding>.Failure(new SiteApiError("DomainConflict", "This domain is already verified for another site.", domain.HostnameAscii, false))
                : SiteApiResult<DomainBinding>.Success(verified);
        }
        catch (SiteOperationException ex) { return SiteApiResult<DomainBinding>.Failure(ex.Error); }
    }

    private async Task<SiteApiResult<DomainBinding>> GetDomainAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) return SiteApiResult<DomainBinding>.Failure(new SiteApiError("InvalidInput", "A valid DomainBindingID is required.", "domainBindingID", false));
        try
        {
            var domain = await store.ReadAsync(state => state.Domains.SingleOrDefault(candidate => candidate.DomainBindingId == id), cancellationToken).ConfigureAwait(false);
            return domain is null ? SiteApiResult<DomainBinding>.Failure(new SiteApiError("DomainBindingNotFound", "The domain binding was not found.", id.ToString(), false)) : SiteApiResult<DomainBinding>.Success(domain);
        }
        catch (SiteOperationException ex) { return SiteApiResult<DomainBinding>.Failure(ex.Error); }
    }

    private async Task<SiteApiResult<SiteProject>> GetProjectAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await store.ReadAsync(state => state.Projects.SingleOrDefault(candidate => candidate.SiteId == id), cancellationToken).ConfigureAwait(false);
        return result is null ? SiteApiResult<SiteProject>.Failure(new SiteApiError("SiteNotFound", "The Sites project was not found.", id.ToString(), false)) : SiteApiResult<SiteProject>.Success(result);
    }

    private async Task SetDomainStateAsync(Guid id, SiteDomainVerificationState state, CancellationToken cancellationToken)
    {
        await store.MutateAsync(snapshot =>
        {
            var domains = snapshot.Domains.Select(candidate => candidate.DomainBindingId == id
                ? candidate with { OwnershipState = state, Revision = checked(candidate.Revision + 1), UpdatedAt = DateTimeOffset.UtcNow }
                : candidate).ToArray();
            return (snapshot with { Domains = domains }, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SiteApiResult<bool>> AuthorizeAsync(string actionId, string impact, IReadOnlyList<string> objectIds, IReadOnlyList<string> scopes, CancellationToken cancellationToken)
    {
        try
        {
            var decision = await authorizer.RequestAsync(new SiteAuthorizationRequest(actionId, SiteActionRisk.ExternalConsequence, objectIds, scopes, impact), cancellationToken).ConfigureAwait(false);
            return decision.State == SiteAuthorizationState.Allowed
                ? SiteApiResult<bool>.Success(true)
                : SiteApiResult<bool>.Failure(new SiteApiError(decision.State switch
                {
                    SiteAuthorizationState.PendingApproval => "PermissionRequired",
                    SiteAuthorizationState.Denied => "PermissionDenied",
                    _ => "HomeServiceUnavailable"
                }, decision.Reason ?? "Home did not authorize the Sites operation.", actionId, decision.State is SiteAuthorizationState.PendingApproval or SiteAuthorizationState.Unavailable, Detail: decision.RequestId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return SiteApiResult<bool>.Failure(new SiteApiError("Cancelled", "Permission evaluation was cancelled; Sites did not perform the operation.", actionId, true)); }
        catch (Exception ex)
        { return SiteApiResult<bool>.Failure(new SiteApiError("HomeServiceUnavailable", "Home could not authorize the Sites operation. No protected operation was performed.", actionId, true, Detail: ex.GetType().Name)); }
    }

    private static int IndexOf<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Count; index++) if (predicate(values[index])) return index;
        return -1;
    }
}
