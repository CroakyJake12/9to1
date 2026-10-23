using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Tests;

/// <summary>
/// Exercises the typed Build (Dev) native content binding: the canonical address codec and the
/// dev registration that the central host must bind into the <see cref="NativeAppContentRegistry"/>.
/// These tests prove the exact registration tuple (canonical dev id, key "dev", <see cref="HavenAppRouteKind.Dev"/>)
/// is accepted and that a dev address routes to a native session contract. The production Build
/// factory/session/workbench is exercised by the authentic owning-project tests (Haven.Desktop.Tests).
/// </summary>
public sealed class BuildBrowseAppContentIntegrationTests
{
    private static readonly ModeDefinition Dev = BuiltInModeSeed.Modes.Single(app => app.Key == "dev");

    // --- Canonical address codec ---

    [Fact]
    public void Build_landing_is_the_empty_project_reference()
    {
        var address = BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://dev"));

        Assert.Null(address.ProjectReference);
        Assert.Equal(Guid.Empty, address.TaskId);
        Assert.Equal(Guid.Empty, address.ExecutionId);
        Assert.Equal(Guid.Empty, address.ContextId);
        Assert.Equal(BuildBrowseAppContentAddress.Landing, address);
    }

    [Fact]
    public void Build_project_open_address_roundtrips_all_canonical_identity_fields()
    {
        var reference = new DeveloperProjectReference(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 7,
            Guid.Parse("22222222-2222-2222-2222-222222222222"), 3,
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var task = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var run = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var ctx = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var address = BuildBrowseAppContentAddress.For(reference, task, run, ctx); // For returns AppContentAddress.
        var parsed = BuildBrowseAppContentAddress.Parse(address);

        Assert.Equal(reference, parsed.ProjectReference);
        Assert.Equal(task, parsed.TaskId);
        Assert.Equal(run, parsed.ExecutionId);
        Assert.Equal(ctx, parsed.ContextId);

        var reencoded = BuildBrowseAppContentAddress.For(parsed.ProjectReference!, parsed.TaskId, parsed.ExecutionId, parsed.ContextId);
        Assert.Equal(address, reencoded);
    }

    [Fact]
    public void Build_project_open_address_requires_all_markers_in_order()
    {
        var reference = new DeveloperProjectReference(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 7,
            Guid.Parse("22222222-2222-2222-2222-222222222222"), 3,
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var task = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var run = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var ctx = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var good = BuildBrowseAppContentAddress.For(reference, task, run, ctx);
        Assert.Equal(reference, BuildBrowseAppContentAddress.Parse(good).ProjectReference);

        // A swapped marker must be refused, not silently accepted.
        var corrupted = good.Value
            .Replace("/wsrv/", "/prv/")
            .Replace("/workspace/", "/wsrv/");
        Assert.Throws<FormatException>(() => BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse(corrupted)));
    }

    [Theory]
    [InlineData("9to1://dev/projects")]
    [InlineData("9to1://dev/projects/")]
    [InlineData("9to1://dev/projects/22222222-2222-2222-2222-222222222222")]
    [InlineData("9to1://dev/other")]
    public void Build_address_refuses_malformed_shapes(string address)
    {
        Assert.Throws<FormatException>(() => BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));
    }

    [Theory]
    [InlineData("9to1://dev2")]
    [InlineData("9to1://files")]
    [InlineData("9to1://home")]
    [InlineData("cui://dev")]
    [InlineData("browse://dev")]
    [InlineData("https://example.com/dev")]
    public void Build_address_refuses_foreign_authority_or_namespace(string address)
    {
        Assert.Throws<UnauthorizedAccessException>(() => BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public void Build_address_refuses_empty_or_invalid_identities(string projectId)
    {
        var address = $"9to1://dev/projects/{projectId}/workspace/11111111-1111-1111-1111-111111111111/roots/33333333-3333-3333-3333-333333333333" +
            $"/wsrv/7/prv/3/task/44444444-4444-4444-4444-444444444444/run/55555555-5555-5555-5555-555555555555/ctx/66666666-6666-6666-6666-666666666666";
        Assert.Throws<FormatException>(() => BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));
    }

    [Theory]
    [InlineData("wsrv/0")]
    [InlineData("wsrv/-1")]
    [InlineData("prv/0")]
    public void Build_address_refuses_non_positive_revisions(string fragment)
    {
        var (marker, value) = fragment.Split('/') is [var m, var v] ? (m, v) : throw new InvalidOperationException();
        var address = $"9to1://dev/projects/22222222-2222-2222-2222-222222222222/workspace/11111111-1111-1111-1111-111111111111/roots/33333333-3333-3333-3333-333333333333" +
            $"/{marker}/{value}/task/44444444-4444-4444-4444-444444444444/run/55555555-5555-5555-5555-555555555555/ctx/66666666-6666-6666-6666-666666666666";
        Assert.Throws<FormatException>(() => BuildBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));
    }

    // --- Central registration + native publication contract ---

    [Fact]
    public void Build_registration_tuple_matches_the_canonical_dev_mode_route()
    {
        Assert.Equal("dev", Dev.Key);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000028"), Dev.Id);
        Assert.Equal(HavenAppRouteKind.Dev, HavenAppRoutePolicy.Resolve(Dev).Kind);

        // The exact tuple the central host registers must be accepted by the registry constructor
        // (lowercase key, parseable 9to1://dev route, unique canonical identity).
        var registry = new NativeAppContentRegistry(new DevModeRegistry(Dev),
            [new NativeAppContentRegistration(Dev.Id, "dev", HavenAppRouteKind.Dev, new StubFactory("code"))],
            () => true);
        Assert.NotNull(registry);
    }

    [AvaloniaFact]
    public async Task Build_registered_dev_destination_routes_to_a_native_session_contract()
    {
        // Registry routing contract only: this stub proves the dev tuple routes a dev address to a
        // native session. It does NOT exercise the production Build factory/session/workbench.
        var factory = new StubFactory("code");
        var registry = new NativeAppContentRegistry(new DevModeRegistry(Dev),
            [new NativeAppContentRegistration(Dev.Id, "dev", HavenAppRouteKind.Dev, factory)],
            () => true);

        // Closing an unopened wrapper must not acquire a native owner. The next wrapper
        // acquires exactly one owner only through its retained initialization task.
        await VerifySession(initialize: false);
        Assert.Equal(0, factory.Calls);
        await VerifySession(initialize: true);
        Assert.Equal(1, factory.Calls);

        async Task VerifySession(bool initialize)
        {
            var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://dev"), "original-profile", BrowserTabPrivacy.Standard);
            var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(registry.CreateSession(request));
            var failures = new List<Exception>();
            try
            {
                Assert.Equal("Opening app", session.Title);
                Assert.Equal("apps", session.IconKey);
                Assert.Equal(0, factory.Calls);
                Assert.Null(session.OriginalInitialization);
                Assert.Null(session.OriginalClose);
                Assert.False(session.IsPublicationCurrent);
                Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
                if (initialize)
                {
                    var initialization = session.InitializeAsync(TestContext.Current.CancellationToken);
                    await initialization;
                    Assert.Same(initialization, session.OriginalInitialization);
                    Assert.Equal("Build", session.Title);
                    Assert.Equal("code", session.IconKey);
                    Assert.True(session.IsPublicationCurrent);
                    Assert.Same(factory.View, session.OriginalNativeView);
                    Assert.Equal(1, factory.Calls);
                }
            }
            catch (Exception error) { failures.Add(error); }
            // Drain even after an admission/assertion failure and keep its causes alongside
            // any independent failure of the SAME wrapper's actual retirement task.
            try
            {
                var close = session.CloseAndDrainAsync();
                await close;
                Assert.Same(close, session.OriginalClose);
                Assert.True(session.CanRetireOriginalView);
                Assert.False(session.IsPublicationCurrent);
                Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
                Assert.Equal(initialize ? 1 : 0, factory.Calls);
                if (!initialize) Assert.Null(session.OriginalInitialization);
            }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("The actual lazy native registry initialization/close contract failed.", failures);
        }
    }

    [AvaloniaFact]
    public async Task Build_address_refuses_unregistered_foreign_authority()
    {
        var factory = new StubFactory("code");
        var registry = new NativeAppContentRegistry(new DevModeRegistry(Dev),
            [new NativeAppContentRegistration(Dev.Id, "dev", HavenAppRouteKind.Dev, factory)],
            () => true);

        var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://files"), "original-profile", BrowserTabPrivacy.Standard);
        Assert.Throws<NativeAppContentRegistry.NativeAppContentAdmissionRefusal>(() => registry.CreateSession(request));
        Assert.Equal(0, factory.Calls);
    }

    private sealed class DevModeRegistry(ModeDefinition app) : IModeRegistry
    {
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(id == app.Id ? app : null);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(key == app.Key ? app : null);
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModeDefinition>>([app]);
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class StubFactory(string icon) : IBrowseAppContentFactory
    {
        public int Calls;
        public readonly Control View = new Border();
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        {
            Calls++;
            return new StubSession(request.Address, icon, View);
        }
    }

    private sealed class StubSession(AppContentAddress address, string icon, Control view) : IBrowseNativeAppContentSession
    {
        public AppContentAddress Address => address;
        public string Title => "Build";
        public string IconKey => icon;
        private EventHandler? _stateChanged;
        public event EventHandler? StateChanged
        {
            add { _stateChanged += value; }
            remove { _stateChanged -= value; }
        }
        public bool IsPublicationCurrent => OriginalInitialization?.IsCompletedSuccessfully == true && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose?.IsCompletedSuccessfully == true;
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        private Task? _returnedInitialization, _returnedClose;
        public Control OriginalNativeView => view;
        public Task InitializeAsync(CancellationToken token = default) =>
            OriginalInitialization ??= (_returnedInitialization ??= Task.CompletedTask);
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync()
        {
            if (_returnedClose is null)
            { _returnedClose = Task.CompletedTask; OriginalClose = _returnedClose; }
            return _returnedClose;
        }
    }
}
