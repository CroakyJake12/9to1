using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
namespace NineToOne.Os.Shell.Tests;

public sealed class ShellGoAllAppsHomeTests
{
    [Fact]
    public async Task AllAppsScopesActualGoDispatchAndDoesNotQueryAndroidDrawer()
    {
        using var f = new Fixture(); var original = (await f.Configuration.GetAsync()).Stored;
        var installed = new Resolver(); var drawer = new Resolver { Id = "android.launcher-drawer" };
        var service = new ShellGoAllAppsHome(f.Configuration, new GoService([installed, drawer]));
        var results = new List<GoResult>();
        await foreach (var update in service.ReadAsync(original, default)) if (update.Result is { } result) results.Add(result);
        Assert.Equal(2, results.Count); Assert.Equal(1, installed.Queries); Assert.Equal(0, drawer.Queries);
        Assert.All(results, x => Assert.Equal("os.installed-applications", x.ProviderId));
    }
    [Fact]
    public async Task SessionSwitchBetweenIncrementalUpdatesDeniesNextPublication()
    {
        using var f = new Fixture(); var original = (await f.Configuration.GetAsync()).Stored;
        var service = new ShellGoAllAppsHome(f.Configuration, new GoService([new Resolver()]));
        await using var updates = service.ReadAsync(original, default).GetAsyncEnumerator();
        Assert.True(await updates.MoveNextAsync()); Assert.NotNull(updates.Current.Result);
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => { await updates.MoveNextAsync(); });
    }
    // Controlled canonical dispatch only; this is not installed-platform admission/launch proof.
    private sealed class Resolver : IGoCanonicalResolver
    {
        public string Id = "os.installed-applications";
        public string ProviderId => Id;
        public int Queries;
        public bool Suspend = false; // This read-only fixture starts unpaused.
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct)
        {
            if (Suspend) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return new(ProviderId, new(locator.Owner, locator.Kind, locator.Id, "42"), "Fresh owner label", "Apps", [new("Open", "Open")]);
        }
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            Queries++; Assert.Equal("", query.Text); Assert.Equal("Apps", query.Category);
            for (var i = 0; i < 2; i++)
            {
                await Task.Yield(); ct.ThrowIfCancellationRequested();
                yield return new(ProviderId, new("Home", "os.installed-application", Guid.NewGuid().ToString("D"), "1"), "App", "Apps", [new("Open", "Open")]);
            }
        }
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
