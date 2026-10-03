using System.Security.Cryptography;
using System.Text;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteDomainVerificationTests
{
    [Fact]
    public async Task Matching_challenge_commits_site_bound_ownership_and_survives_reopen()
    {
        using var fixture = await Fixture.CreateAsync();
        var instructions = await fixture.BeginAsync();
        fixture.Resolver.Resolve = (_, _) => Task.FromResult<IReadOnlyList<string>>([instructions.RecordValue]);
        var result = await fixture.Service.VerifyDomainAsync(fixture.DomainId);
        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(SiteDomainVerificationState.Verified, result.Value!.OwnershipState);
        var reopened = new FileSiteWorkspaceStore(fixture.Path);
        var snapshot = await reopened.ReadAsync(state => state);
        Assert.Equal(fixture.SiteId, Assert.Single(snapshot.Domains).SiteId);
        Assert.Equal(SiteDomainVerificationState.Verified, Assert.Single(snapshot.Domains).OwnershipState);
        Assert.NotNull(Assert.Single(snapshot.DomainChallenges).VerifiedAt);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instructions.RecordValue))), Assert.Single(snapshot.DomainChallenges).TokenHash);
        Assert.Equal(instructions.RecordValue, Assert.Single(Assert.Single(snapshot.Domains).RequiredRecords).Value);
    }

    [Fact]
    public async Task Wrong_txt_token_cannot_verify_domain()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.BeginAsync();
        fixture.Resolver.Resolve = (_, _) => Task.FromResult<IReadOnlyList<string>>(["wrong-token"]);
        var result = await fixture.Service.VerifyDomainAsync(fixture.DomainId);
        Assert.Equal("DomainVerificationPending", result.Error?.Code);
        Assert.Equal(SiteDomainVerificationState.Pending, (await fixture.DomainAsync()).OwnershipState);
        Assert.Null(await fixture.Store.ReadAsync(state => Assert.Single(state.DomainChallenges).VerifiedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_dns_response_cannot_modify_rotated_challenge(bool matching)
    {
        using var fixture = await Fixture.CreateAsync();
        var old = await fixture.BeginAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Resolver.Resolve = (_, _) => { started.SetResult(); return response.Task; };
        var verifying = fixture.Service.VerifyDomainAsync(fixture.DomainId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await fixture.BeginAsync();
        var before = await fixture.IndexBytesAsync();
        response.SetResult(matching ? [old.RecordValue] : []);
        var result = await verifying;
        Assert.Equal("DomainVerificationExpired", result.Error?.Code);
        Assert.Equal(before, await fixture.IndexBytesAsync());
        Assert.Equal(2, current.Generation);
        Assert.All(await fixture.Store.ReadAsync(state => state.DomainChallenges), challenge => Assert.Null(challenge.VerifiedAt));
    }

    [Fact]
    public async Task Stale_negative_dns_response_cannot_revoke_concurrently_verified_ownership()
    {
        using var fixture = await Fixture.CreateAsync();
        var instructions = await fixture.BeginAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fixture.Resolver.Resolve = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { started.SetResult(); return delayed.Task; }
            return Task.FromResult<IReadOnlyList<string>>([instructions.RecordValue]);
        };
        var stale = fixture.Service.VerifyDomainAsync(fixture.DomainId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await fixture.Service.VerifyDomainAsync(fixture.DomainId)).IsSuccess);
        var before = await fixture.IndexBytesAsync();
        delayed.SetResult([]);
        Assert.Equal("DomainVerificationExpired", (await stale).Error?.Code);
        Assert.Equal(before, await fixture.IndexBytesAsync());
        Assert.Equal(SiteDomainVerificationState.Verified, (await fixture.DomainAsync()).OwnershipState);
    }

    [Fact]
    public async Task Denied_permission_performs_no_dns_lookup_or_metadata_mutation()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.BeginAsync();
        var before = await File.ReadAllBytesAsync(System.IO.Path.Combine(fixture.Path, ".9to1-sites-index.json"));
        fixture.Authorizer.State = SiteAuthorizationState.Denied;
        fixture.Resolver.Resolve = (_, _) => throw new InvalidOperationException("Denied request reached DNS.");
        Assert.Equal("PermissionDenied", (await fixture.Service.VerifyDomainAsync(fixture.DomainId)).Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(System.IO.Path.Combine(fixture.Path, ".9to1-sites-index.json")));
    }

    [Fact]
    public async Task Expired_challenge_fails_without_dns_lookup_and_preserves_token_history()
    {
        using var fixture = await Fixture.CreateAsync();
        var instructions = await fixture.BeginAsync();
        await fixture.Store.MutateAsync(state => (state with
        {
            DomainChallenges = state.DomainChallenges.Select(challenge => challenge with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).ToArray()
        }, true));
        fixture.Resolver.Resolve = (_, _) => throw new InvalidOperationException("Expired challenge reached DNS.");
        Assert.Equal("DomainVerificationExpired", (await fixture.Service.VerifyDomainAsync(fixture.DomainId)).Error?.Code);
        Assert.Equal(SiteDomainVerificationState.Expired, (await fixture.DomainAsync()).OwnershipState);
        var preserved = await fixture.Store.ReadAsync(state => Assert.Single(state.DomainChallenges));
        Assert.Equal(instructions.ChallengeId, preserved.ChallengeId);
        Assert.Null(preserved.VerifiedAt);
    }

    [Fact]
    public async Task Resolver_failure_preserves_metadata_for_retry()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.BeginAsync();
        var before = await fixture.IndexBytesAsync();
        fixture.Resolver.Resolve = (_, _) => throw new IOException("Controlled test resolver failure.");
        Assert.Equal("DnsLookupUnavailable", (await fixture.Service.VerifyDomainAsync(fixture.DomainId)).Error?.Code);
        Assert.Equal(before, await fixture.IndexBytesAsync());
    }

    [Fact]
    public async Task Second_site_cannot_take_verified_domain()
    {
        using var fixture = await Fixture.CreateAsync();
        var instructions = await fixture.BeginAsync();
        fixture.Resolver.Resolve = (_, _) => Task.FromResult<IReadOnlyList<string>>([instructions.RecordValue]);
        Assert.True((await fixture.Service.VerifyDomainAsync(fixture.DomainId)).IsSuccess);
        var second = await new SiteProjectService(fixture.Store).CreateProjectAsync(new(Guid.NewGuid(), null, null, "revision-b", "Other", "static", "other"));
        Assert.True(second.IsSuccess);
        var result = await fixture.Service.AttachDomainAsync(new(second.Value!.SiteId, second.Value.Environments.First().EnvironmentId, "studio.example", true));
        Assert.Equal("DomainConflict", result.Error?.Code);
        Assert.Equal(fixture.SiteId, (await fixture.DomainAsync()).SiteId);
        Assert.Equal(SiteDomainVerificationState.Verified, (await fixture.DomainAsync()).OwnershipState);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sites-domain-c4-" + Guid.NewGuid().ToString("N"));
        public FileSiteWorkspaceStore Store { get; }
        public Authorizer Authorizer { get; } = new();
        public Resolver Resolver { get; } = new();
        public SitePublicIdentityService Service { get; }
        public Guid DomainId { get; private set; }
        public Guid SiteId { get; private set; }
        private Fixture()
        {
            Store = new(Path);
            Service = new(Store, new SiteNameVerificationService(Store, new NameModel()), Authorizer, new Policy(), Resolver);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var projectResult = await new SiteProjectService(fixture.Store).CreateProjectAsync(new(Guid.NewGuid(), null, null, "revision-a", "Studio", "static", "studio"));
            Assert.True(projectResult.IsSuccess, projectResult.Error?.Message);
            var project = projectResult.Value!;
            fixture.SiteId = project.SiteId;
            var attached = await fixture.Service.AttachDomainAsync(new(project.SiteId, project.Environments.First(environment => environment.IsEnabled).EnvironmentId, "studio.example", true));
            Assert.True(attached.IsSuccess, attached.Error?.Message);
            fixture.DomainId = attached.Value!.DomainBindingId;
            return fixture;
        }
        public async Task<DomainChallengeInstructions> BeginAsync()
        {
            var result = await Service.BeginDomainVerificationAsync(DomainId);
            Assert.True(result.IsSuccess, result.Error?.Message);
            return result.Value!;
        }
        public Task<byte[]> IndexBytesAsync() => File.ReadAllBytesAsync(System.IO.Path.Combine(Path, ".9to1-sites-index.json"));
        public Task<DomainBinding> DomainAsync() => Store.ReadAsync(state => Assert.Single(state.Domains));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
    private sealed class Authorizer : ISiteActionAuthorizer
    {
        public SiteAuthorizationState State { get; set; } = SiteAuthorizationState.Allowed;
        public Task<SiteAuthorizationDecision> RequestAsync(SiteAuthorizationRequest request, CancellationToken cancellationToken) => Task.FromResult(new SiteAuthorizationDecision(State, "test-request", null));
    }
    private sealed class Resolver : ISiteTxtRecordResolver
    {
        public Func<string, CancellationToken, Task<IReadOnlyList<string>>> Resolve { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetTxtRecordsAsync(string canonicalRecordName, CancellationToken cancellationToken) => Resolve(canonicalRecordName, cancellationToken);
    }
    private sealed class Policy : ISiteDomainChallengePolicy
    {
        public string Version => "test-policy-v1";
        public TimeSpan Lifetime => TimeSpan.FromHours(1);
    }
    private sealed class NameModel : ISiteNameAssessmentModel
    {
        public Task<SiteNameModelAssessment> AssessAsync(SiteNameAssessmentInput input, CancellationToken cancellationToken) => Task.FromResult(new SiteNameModelAssessment(true, [], "test-model"));
    }
}
