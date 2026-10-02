using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoNativeSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuspendedOriginalSessionReadKeepsSubmittedQueryOrRejectsReplacedSearch(bool replaceSearch)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-go-native-" + Guid.NewGuid());
            try
            {
                var actors = new Actors(); var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var resources = new ResourceAuthorizationService(actors, [new ShellConfigurationResourceResolver(home)]);
                var configuration = new ShellConfigurationService(new HomeShellConfigurationStore(home, actors, resources));
                var provider = new Provider(actors); var registry = new EmptyRegistry();
                using var model = new ShellViewModel();
                await model.StartAsync(configuration, new GoService([provider]), new LinuxApplicationLauncher(registry, resources, actors), default);
                // Complete startup before pausing the next real canonical actor read.
                await model.DispatchAsync("Search", null);
                model.TrySetValue("Query", "submitted original"); actors.PauseNext = true;
                var pending = model.DispatchAsync("Search", null).AsTask();
                await actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                model.TrySetValue("Query", "new input");
                if (replaceSearch) await model.DispatchAsync("Search", null);
                actors.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(model.TryGetValue("Results", out var rows));
                var shown = Assert.Single(Assert.IsAssignableFrom<IEnumerable<GoResult>>(rows));
                Assert.Equal(replaceSearch ? "new input" : "submitted original", shown.Label);
                Assert.DoesNotContain(provider.Queries, text => text == (replaceSearch ? "submitted original" : "new input"));
                // The displayed canonical result object is accepted only by an original-actor owner port.
                await model.DispatchAsync("Open", shown);
                Assert.Equal(1, provider.Invocations);
                Assert.Equal(actors.Current, provider.CapturedActor);
                await model.DispatchAsync("Open", shown with { });
                Assert.Equal(1, provider.Invocations);
                actors.Current = actors.Current with { AuthenticationRevision = "changed" };
                await model.DispatchAsync("Open", shown);
                Assert.Equal(1, provider.Invocations);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            return true;
        }, CancellationToken.None));
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public bool PauseNext;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        {
            if (PauseNext) { PauseNext = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return Current;
        }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class Provider(Actors actors) : IGoOriginalActorInvocation, IGoOriginalActorQuery
    {
        // Controlled fixture actor seam; actual FileHome/installed owner proof is in GoPrivateOriginalReadNativeTests.
        public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expected,
            [EnumeratorCancellation] CancellationToken ct)
        {
            if (actors.Current != expected) throw new UnauthorizedAccessException("Fixture original actor changed.");
            await foreach (var result in QueryAsync(query, ct))
            {
                if (actors.Current != expected) throw new UnauthorizedAccessException("Fixture original actor changed.");
                yield return result;
            }
            if (actors.Current != expected) throw new UnauthorizedAccessException("Fixture original actor changed.");
        }
        public string ProviderId => "fixture.original-owner";
        public List<string> Queries { get; } = [];
        public int Invocations;
        public AuthenticatedResourceActor? CapturedActor;
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask; ct.ThrowIfCancellationRequested();
            if (query.Text.Length == 0) yield break;
            Queries.Add(query.Text);
            yield return new(ProviderId, new("Fixture", "entity", query.Text, "1"), query.Text, "Apps", [new("Open", "Open")]);
        }
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) => throw new InvalidOperationException("Current-only fallback is forbidden.");
        public Task InvokeForActorAsync(GoCanonicalReference reference, string action, AuthenticatedResourceActor expected, CancellationToken ct)
        { CapturedActor = expected; Invocations++; return Task.CompletedTask; } // Controlled dispatch boundary only; not installed platform authorization.
    }
    private sealed class EmptyRegistry : IInstalledApplicationRegistry
    {
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationReference>>([]);
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) => ValueTask.FromResult<InstalledApplicationReference?>(null);
    }
}
