using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed class AgentDependencyCatalogServiceTests
{
    [Fact]
    public async Task Current_canonical_UUID_key_and_implementation_share_one_observation_without_granting_aliases()
    {
        var definition = Definition("files.current", "files.owning.current");
        var repository = new Repository(_ => [definition]);
        var service = new AgentDependencyCatalogService(new(repository));
        var result = await service.ResolveCurrentAsync(new([definition.Id.ToString("D"), definition.Key,
            definition.ImplementationKey], [], [], [], "user"), CapabilityPlatform.Windows);
        Assert.True(result.DependenciesResolved);
        Assert.Equal(definition, Assert.Single(result.Capabilities));
        Assert.Empty(result.Packages); Assert.Empty(result.Skills);
        var alias = await service.ResolveCurrentAsync(new([definition.Name], [], [], [], "user"), CapabilityPlatform.Windows);
        Assert.False(alias.DependenciesResolved); Assert.Empty(alias.Capabilities);
        Assert.Contains(alias.Diagnostics, item => item.Code == "CapabilityNotFound");
        Assert.Equal(4, repository.Reads);
    }

    [Fact]
    public async Task Ambiguous_route_and_MCP_category_mismatch_refuse_before_any_runtime_invocation()
    {
        var first = Definition("files.one", "owner.shared");
        var second = Definition("files.two", "owner.shared");
        var service = new AgentDependencyCatalogService(new(new Repository(_ => [first, second])));
        var ambiguous = await service.ResolveCurrentAsync(new(["owner.shared"], [], [], [], "user"), CapabilityPlatform.Windows);
        Assert.False(ambiguous.DependenciesResolved); Assert.Empty(ambiguous.Capabilities);
        Assert.Contains(ambiguous.Diagnostics, item => item.Code == "AmbiguousCapabilityIdentity");
        var foreign = await service.ResolveCurrentAsync(new([], [], [], [first.Key], "user"), CapabilityPlatform.Windows);
        Assert.False(foreign.DependenciesResolved); Assert.Empty(foreign.Capabilities);
        Assert.Contains(foreign.Diagnostics, item => item.Code == "McpCapabilityRequired");
    }

    [Fact]
    public async Task A_configured_MCP_connection_is_only_eligible_metadata_and_disabled_connection_is_refused()
    {
        var ready = Definition("placeholder", "connection.mcp") with { Availability = CapabilityAvailability.PermissionRequired };
        ready = ready with { Key = ExternalConnectionNaming.CapabilityKey(ready.Id) };
        var repository = new Repository(_ => [ready]);
        var service = new AgentDependencyCatalogService(new(repository));
        var result = await service.ResolveCurrentAsync(new([], [], [], [ready.Key], "user"), CapabilityPlatform.Windows);
        Assert.True(result.DependenciesResolved);
        Assert.Equal(CapabilityAvailability.PermissionRequired, Assert.Single(result.Capabilities).Availability);
        repository.Values = _ => [ready with { IsEnabled = false }];
        var revoked = await service.ResolveCurrentAsync(new([], [], [], [ready.Key], "user"), CapabilityPlatform.Windows);
        Assert.False(revoked.DependenciesResolved); Assert.Empty(revoked.Capabilities);
        Assert.Contains(revoked.Diagnostics, item => item.Code == "CapabilityNotFound");
    }

    [Fact]
    public async Task A_registry_change_during_lookup_returns_explicit_unresolved_observation()
    {
        var original = Definition("files.one", "owner.one");
        var repository = new Repository(read => read == 1 ? [original] :
            [original with { Availability = CapabilityAvailability.DependencyRequired, UpdatedAt = original.UpdatedAt.AddTicks(1) }]);
        var result = await new AgentDependencyCatalogService(new(repository)).ResolveCurrentAsync(
            new([original.Key], [], [], [], "user"), CapabilityPlatform.Windows);
        Assert.False(result.DependenciesResolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "CapabilityChanged");
        Assert.Equal(2, repository.Reads);
    }

    [Theory]
    [InlineData(CapabilityPlatform.None)]
    [InlineData(CapabilityPlatform.All)]
    [InlineData((CapabilityPlatform)64)]
    public async Task Unsupported_concrete_platform_does_not_borrow_another_platform_catalogue(CapabilityPlatform platform)
    {
        var repository = new Repository(_ => throw new InvalidOperationException("Discovery must not run."));
        var result = await new AgentDependencyCatalogService(new(repository)).ResolveCurrentAsync(
            new([], [], [], [], "user"), platform);
        Assert.False(result.DependenciesResolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "UnsupportedConcretePlatform");
        Assert.Equal(0, repository.Reads);
    }

    [Fact]
    public async Task Uninstalled_package_and_unknown_Skill_are_not_reported_as_resolved_draft_dependencies()
    {
        var repository = new Repository(_ => []);
        var result = await new AgentDependencyCatalogService(new(repository)).ResolveCurrentAsync(
            new([], ["missing-package/missing-skill"], ["missing-plugin"], [], "user"), CapabilityPlatform.Windows);
        Assert.False(result.DependenciesResolved); Assert.Empty(result.Packages); Assert.Empty(result.Skills);
        Assert.Contains(result.Diagnostics, item => item.Code == "PackageIdentityUnavailable");
        Assert.Contains(result.Diagnostics, item => item.Code == "SkillRuntimeUnavailable");
    }

    [Fact]
    public async Task Cancellation_and_noncanonical_scope_stop_lookup_before_discovery()
    {
        var repository = new Repository(_ => throw new InvalidOperationException("Discovery must not run."));
        var service = new AgentDependencyCatalogService(new(repository));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ResolveCurrentAsync(
            new([], [], [], [], "user"), CapabilityPlatform.Windows, canceled.Token));
        var wrong = await service.ResolveCurrentAsync(new([], [], [], [], " agent:" + Guid.NewGuid().ToString("N")), CapabilityPlatform.Windows);
        Assert.False(wrong.DependenciesResolved);
        Assert.Contains(wrong.Diagnostics, item => item.Code == "InvalidCurrentScope");
        Assert.Equal(0, repository.Reads);
    }

    [Theory]
    [InlineData(CapabilityPlatform.Linux)]
    [InlineData(CapabilityPlatform.MacOS)]
    [InlineData(CapabilityPlatform.iOS)]
    public async Task A_new_concrete_platform_uses_only_declared_current_metadata_and_rejects_late_revocation(CapabilityPlatform platform)
    {
        var actual = Definition("current.actual", "owner.actual") with { Platforms = platform };
        var windows = Definition("windows.only", "owner.windows");
        var repository = new Repository(read => read == 1 ? [actual, windows] : [actual with { IsEnabled = false }, windows]);
        var service = new AgentDependencyCatalogService(new(repository));
        var stale = await service.ResolveCurrentAsync(new([actual.Key], [], [], [], "user"), platform);
        Assert.False(stale.DependenciesResolved);
        Assert.Contains(stale.Diagnostics, value => value.Code == "CapabilityChanged");
        repository.Values = _ => [actual, windows];
        var foreign = await service.ResolveCurrentAsync(new([windows.Key], [], [], [], "user"), platform);
        Assert.False(foreign.DependenciesResolved); Assert.Empty(foreign.Capabilities);
        Assert.Contains(foreign.Diagnostics, value => value.Code == "CapabilityNotFound");
        var current = await service.ResolveCurrentAsync(new([actual.Key], [], [], [], "user"), platform);
        Assert.True(current.DependenciesResolved); Assert.Equal(actual, Assert.Single(current.Capabilities));
        Assert.Equal(6, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_final_new_alias_collision_refuses_the_original_exact_Tool_or_MCP_declaration(bool mcp)
    {
        var original = Definition("saved.route", mcp ? "connection.mcp" : "owner.route");
        if (mcp) original = original with { Key = ExternalConnectionNaming.CapabilityKey(original.Id) };
        var late = original with { Id = Guid.NewGuid() };
        var repository = new Repository(read => read == 1 ? [original] : [original, late]);
        var request = mcp ? new AgentDependencyRequest([], [], [], [original.Key], "user") :
            new AgentDependencyRequest([original.Key], [], [], [], "user");
        var result = await new AgentDependencyCatalogService(new(repository)).ResolveCurrentAsync(request, CapabilityPlatform.Windows);
        Assert.False(result.DependenciesResolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "AmbiguousCapabilityIdentity" && item.Target == original.Key);
        Assert.Contains(result.Diagnostics, item => item.Code == "CapabilityChanged");
        Assert.Equal(2, repository.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Maintained_Skill_resolver_ABA_package_snapshot_cannot_mismatch_catalogue_provenance(bool sameVersion) =>
        WithInstalledSkills(async (first, second, token) =>
        {
            if (sameVersion) second = second with { Manifest = second.Manifest with { Version = first.Manifest.Version } };
            var repository = new InstalledRepository(read => read == 2 ? [second] : [first]);
            var result = await ResolveInstalled(repository, token);
            Assert.False(result.DependenciesResolved); Assert.Empty(result.Skills);
            Assert.Contains(result.Diagnostics, item => item.Code == "SkillPackageChanged");
            var observed = Assert.Single(result.Packages);
            Assert.Equal(first.Manifest.Version, observed.Version); Assert.Equal(first.ContentHash, observed.ContentHash);
            Assert.Equal(3, repository.Reads);
        });

    [Fact]
    public Task Maintained_Skill_resolver_same_original_package_retains_exact_content_and_witness() =>
        WithInstalledSkills(async (first, _, token) =>
        {
            var repository = new InstalledRepository(_ => [first]);
            var result = await ResolveInstalled(repository, token);
            Assert.True(result.DependenciesResolved); Assert.Empty(result.Diagnostics);
            var skill = Assert.Single(result.Skills);
            Assert.Equal("original inspected instructions", skill.Instructions);
            Assert.Equal("original resource", skill.Resources["resource.txt"]);
            Assert.Equal(first.Manifest.PackageId, skill.PackageId); Assert.Equal(first.Manifest.Version, skill.PackageVersion);
            Assert.Equal(JsonSerializer.Serialize(first), skill.OriginalPackageFingerprint);
            Assert.Equal(first.ContentHash, Assert.Single(result.Packages).ContentHash); Assert.Equal(3, repository.Reads);
        });

    private static async Task<AgentDependencyLookup> ResolveInstalled(InstalledRepository repository, CancellationToken token)
    {
        var capabilities = new Repository(_ => []);
        var runtime = new NativePluginRuntime(capabilities, Refuse<ICatalogRepository>(),
            Refuse<INativePluginProcessFactory>(), Refuse<IExecutionEventSink>(), repository);
        AgentDependencyLookup? result = null; Exception? primary = null; Exception? cleanup = null;
        try { result = await new AgentDependencyCatalogService(new(capabilities), repository, runtime)
            .ResolveCurrentAsync(new([], ["original-package/inspect"], [], [], "user"), CapabilityPlatform.Windows, token); }
        catch (Exception error) { primary = error; }
        try { await runtime.DisposeAsync(); } catch (Exception error) { cleanup = error; }
        if (primary is not null && cleanup is not null && !ReferenceEquals(primary, cleanup)) throw new AggregateException(primary, cleanup);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
        return result!;
    }

    private static async Task WithInstalledSkills(Func<InstalledExtensionPackage, InstalledExtensionPackage, CancellationToken, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-original-skill-provenance-" + Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Exception? primary = null; Exception? cleanup = null;
        try
        {
            async Task<InstalledExtensionPackage> Package(string directory, string version, string instructions, string resource)
            {
                var path = Path.Combine(root, directory); Directory.CreateDirectory(path);
                await File.WriteAllTextAsync(Path.Combine(path, "instructions.txt"), instructions, lifetime.Token);
                await File.WriteAllTextAsync(Path.Combine(path, "resource.txt"), resource, lifetime.Token);
                var manifest = new ExtensionPackageManifest("original-package", ".", "Original package", ExtensionPackageType.Skill,
                    version, "*", "description", "author", "publisher", null, null, ExtensionPermission.None, [], [],
                    [new("inspect", "Inspect", "description", "instructions.txt", false, ResourcePaths: ["resource.txt"])], null);
                var hash = await ExtensionPackageIntegrity.ComputeHashAsync(path, lifetime.Token);
                return new(Guid.NewGuid(), Guid.NewGuid(), manifest, path, ExtensionPermission.None, ExtensionInstallState.Enabled,
                    true, false, hash, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, EnablementScope: "user", EnabledScopes: ["user"],
                    SkillEnablementScopes: new Dictionary<string, IReadOnlyList<string>> { ["inspect"] = ["user"] });
            }
            var first = await Package("original", "1", "original inspected instructions", "original resource");
            var second = await Package("resolver", "2", "different inspected instructions", "different resource");
            await action(first, second, lifetime.Token);
        }
        catch (Exception error) { primary = error; }
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { cleanup = error; }
        if (primary is not null && cleanup is not null && !ReferenceEquals(primary, cleanup)) throw new AggregateException(primary, cleanup);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
    }

    private sealed class InstalledRepository(Func<int, IReadOnlyList<InstalledExtensionPackage>> values) : IExtensionRepository
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(values(++Reads)); }
        public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken token) => throw new NotSupportedException();
        public Task UpsertSourceAsync(ExtensionSource source, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteSourceAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteInstalledAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private static T Refuse<T>() where T : class => DispatchProxy.Create<T, RefusingUnusedPluginPort>();
    public class RefusingUnusedPluginPort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) =>
            throw new InvalidOperationException("Skill resolution must not invoke plugin/process/catalog/event port: " + method?.Name);
    }

    private static CapabilityDefinition Definition(string key, string route) => new(Guid.NewGuid(), key,
        "Display name is not an ID", "description", "files", "file", "instructions", route, "[]",
        CapabilityPlatform.Windows, CapabilityRiskClass.ReadOnly, CapabilityAvailability.Available,
        "[]", "owner.provider", true, true, true, true, DateTimeOffset.UnixEpoch);

    private sealed class Repository(Func<int, IReadOnlyList<CapabilityDefinition>> values) : ICapabilityRepository
    {
        public Func<int, IReadOnlyList<CapabilityDefinition>> Values { get; set; } = values;
        public int Reads { get; private set; }
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(Values(++Reads)); }
        public Task UpsertCapabilityAsync(CapabilityDefinition definition, CancellationToken ct) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
