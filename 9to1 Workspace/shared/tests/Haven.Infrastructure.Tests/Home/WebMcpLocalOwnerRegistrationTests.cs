using Haven.Application;
using Haven.Browser;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace Haven.Infrastructure.Tests;

/// <summary>Actual local Home and genuine Browser registry service graph. Registration/alias lowerbound only;
/// no attached native host, private document selection or execution permission is inferred.</summary>
public sealed class WebMcpLocalOwnerRegistrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("astra-webmcp-di-").FullName;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly BrowserSessionService _browser;
    private readonly BrowseOwnedDocumentRegistry _registry;
    public WebMcpLocalOwnerRegistrationTests()
    {
        _store = new(Path.Combine(_root, "home.json")); _profiles = new(_store, new OperatingSystemPrincipalSource());
        _browser = new(new Paths(_root)); _registry = new(_browser, _profiles);
    }
    private ServiceCollection Graph(bool includeRegistry = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHomeCoreStateStore>(_store); services.AddSingleton(_profiles);
        services.AddSingleton<IAuthenticatedResourceActorSource>(_profiles); services.AddSingleton(_browser); services.AddSingleton(_registry);
        if (includeRegistry) services.AddSingleton<ICanonicalResourceAccessResolver>(provider => provider.GetRequiredService<BrowseOwnedDocumentRegistry>());
        services.AddSingleton<ResourceAuthorizationService>();
        services.AddSingleton<HomePermissionTrustService>(provider => new(provider.GetRequiredService<IHomeCoreStateStore>(),
            provider.GetRequiredService<IHomeActionPolicySource>().TryGet));
        services.AddSingleton<HomeResourceOperationBroker>(); return services;
    }
    [Fact]
    public void Default_infrastructure_does_not_register_original_WebMcp_owner_or_native_catalogue()
    {
        var services = new ServiceCollection().AddHavenInfrastructure();
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IWebMcpOriginalActorApproval));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(HomeWebMcpOriginalActorApproval));
        Assert.DoesNotContain(services, item => item.ImplementationType == typeof(HomeWebMcpActionPolicySource));
    }
    [Fact]
    public void Explicit_owner_aliases_one_actual_graph_and_does_not_create_another_store_actor_or_registry()
    {
        var services = Graph(); services.AddHavenLocalWebMcpOwner(); using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<HomeWebMcpOriginalActorApproval>(), provider.GetRequiredService<IWebMcpOriginalActorApproval>());
        Assert.Same(_store, provider.GetRequiredService<IHomeCoreStateStore>());
        Assert.Same(_profiles, provider.GetRequiredService<IAuthenticatedResourceActorSource>());
        Assert.Same(_registry, Assert.Single(provider.GetServices<ICanonicalResourceAccessResolver>()));
        Assert.Same(_browser, provider.GetRequiredService<BrowserSessionService>());
        var policy = Assert.IsType<HomeWebMcpActionPolicySource>(Assert.Single(provider.GetServices<IHomeActionPolicySource>())).TryGet("browse", "webmcp.invoke")!;
        Assert.Equal(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, policy.Risk);
        Assert.True(policy.HasExternalSideEffects); Assert.True(policy.RequiresPerActionApproval); Assert.False(policy.IsReversible);
    }
    [Fact]
    public void Missing_actual_document_owner_and_changed_canonical_actor_alias_deny_without_fallback_graph()
    {
        var missing = Graph(false); missing.AddHavenLocalWebMcpOwner(); using var missingProvider = missing.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => missingProvider.GetRequiredService<IWebMcpOriginalActorApproval>());
        var changed = Graph(); changed.RemoveAll<IAuthenticatedResourceActorSource>(); changed.AddSingleton<IAuthenticatedResourceActorSource>(new UnavailableActor());
        changed.AddHavenLocalWebMcpOwner(); using var changedProvider = changed.BuildServiceProvider();
        Assert.Throws<NotSupportedException>(() => changedProvider.GetRequiredService<IWebMcpOriginalActorApproval>());
        Assert.False(File.Exists(Path.Combine(_root, "home.json"))); // No owner reads/writes were needed to reject composition.
    }
    [Fact]
    public void Second_opt_in_does_not_install_competing_issuer_or_catalogue()
    {
        var services = Graph(); services.AddHavenLocalWebMcpOwner(); var count = services.Count;
        Assert.Throws<InvalidOperationException>(() => services.AddHavenLocalWebMcpOwner()); Assert.Equal(count, services.Count);
    }
    private sealed class UnavailableActor : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(null); }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
    public void Dispose() { _browser.Dispose(); Directory.Delete(_root, true); }
}
