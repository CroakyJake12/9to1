using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
namespace NineToOne.Os.Shell.Tests;

public sealed class ShellGoPinnedHomeTests
{
    [Fact]
    public async Task ActualHomePinUsesFreshOwnerLabelRevisionAndNotCopiedStoredEntity()
    {
        using var f = new Fixture(); var original = await f.PinAsync();
        var owner = new Resolver(); var service = new ShellGoPinnedHome(f.Configuration, new GoService([owner]));
        var result = Assert.Single(await service.ReadAsync(original, default));
        Assert.Equal("Fresh owner label", result.Label); Assert.Equal("42", result.Reference.Revision);
        Assert.Equal(f.ApplicationId.ToString("D"), result.Reference.Id);
        Assert.Equal(original.Revision, (await f.Configuration.GetAsync()).Stored.Revision);
    }
    [Fact]
    public async Task OriginalSessionChangeDuringCanonicalResolutionPublishesNothing()
    {
        using var f = new Fixture(); var original = await f.PinAsync();
        var owner = new Resolver { Suspend = true }; var service = new ShellGoPinnedHome(f.Configuration, new GoService([owner]));
        var pending = service.ReadAsync(original, default); await owner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" }; owner.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
    }
    [Fact]
    public async Task MissingCanonicalResolverDoesNotInventPinnedResult()
    {
        using var f = new Fixture(); var original = await f.PinAsync();
        Assert.Empty(await new ShellGoPinnedHome(f.Configuration, new GoService([])).ReadAsync(original, default));
    }
    // Controlled canonical dispatch only; this is not installed-platform admission/launch proof.
    private sealed class Resolver : IGoCanonicalResolver
    {
        public string ProviderId => "os.installed-applications";
        public bool Suspend;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
        {
            if (Suspend) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return new(ProviderId, new(locator.Owner, locator.Kind, locator.Id, "42"), "Fresh owner label", "Apps", [new("Open", "Open")]);
        }
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; }
        public Task InvokeAsync(GoCanonicalReference reference, string actionId, CancellationToken ct) => throw new InvalidOperationException("Read-only fixture.");
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-go-home-" + Guid.NewGuid());
        public Guid ApplicationId { get; } = Guid.NewGuid(); public Actors Actors { get; } = new();
        public ShellConfigurationService Configuration { get; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
            Configuration = new(new HomeShellConfigurationStore(home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(home)])));
        }
        public async Task<ShellStoredConfiguration> PinAsync()
        {
            var original = await Configuration.GetAsync();
            var preview = await Configuration.PreviewAsync(original.Stored, DesktopPageEdits.PinApplication(original.Stored.Current, ApplicationId, "Old saved pin label"), TimeSpan.FromMinutes(1));
            return (await Configuration.KeepAsync(preview.Preview!.Id)).Stored;
        }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
