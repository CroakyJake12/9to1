using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class SpacePolicyResolverTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ancestor_exclusions_survive_enabled_descendants_and_inheritance_reset_after_file_reload(bool inheritAvailability)
    {
        using var files = new IsolatedSettingsFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var parent = await registry.CreateAsync("Restricted parent", cancellationToken: token);
        var exclusions = Enum.GetValues<SpaceCapabilityKind>()
            .Select(kind => new SpaceCapabilityBinding("blocked", kind, SpaceCapabilityOverride.Disabled, "parent-version"))
            .ToArray();
        parent = await registry.UpdateAsync(parent with
        {
            CapabilityBindings = [.. exclusions, new("allowed", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Enabled, "allowed-version")]
        }, parent.Revision, token);
        var child = await registry.CreateAsync("Child", null, parent.Id, token);
        child = await registry.UpdateAsync(child with
        {
            Inheritance = Categories(inheritAvailability),
            CapabilityBindings = exclusions.Select(binding => binding with
            { Override = SpaceCapabilityOverride.Enabled, VersionPin = "child-version" }).ToArray()
        }, child.Revision, token);
        var grandchild = await registry.CreateAsync("Grandchild", null, child.Id, token);
        grandchild = await registry.UpdateAsync(grandchild with
        {
            Inheritance = Categories(false),
            CapabilityBindings = [.. exclusions.Select(binding => binding with
                { Override = SpaceCapabilityOverride.Enabled, VersionPin = "grandchild-version" }),
                new("local", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Enabled, "local-version")]
        }, grandchild.Revision, token);

        // Recreate both production owners: assertions use disk-deserialized records,
        // not the record instances passed to UpdateAsync or an in-memory fake store.
        registry = files.ReopenRegistry();
        var resolver = new SpacePolicyResolver(registry);
        var childPolicy = await resolver.ResolveAsync(child.Id, token);
        var grandchildPolicy = await resolver.ResolveAsync(grandchild.Id, token);

        foreach (var denied in exclusions)
        {
            Assert.Equal(denied, Assert.Single(childPolicy.Capabilities,
                binding => binding.Kind == denied.Kind && binding.CapabilityId == denied.CapabilityId));
            Assert.Equal(denied, Assert.Single(grandchildPolicy.Capabilities,
                binding => binding.Kind == denied.Kind && binding.CapabilityId == denied.CapabilityId));
        }
        Assert.Equal(inheritAvailability, childPolicy.Capabilities.Any(binding => binding.CapabilityId == "allowed"));
        Assert.DoesNotContain(grandchildPolicy.Capabilities, binding => binding.CapabilityId == "allowed");
        Assert.Equal("local-version", Assert.Single(grandchildPolicy.Capabilities,
            binding => binding.CapabilityId == "local").VersionPin);
        Assert.Equal(child.Id, childPolicy.SpaceId);
        Assert.Equal(grandchild.Id, grandchildPolicy.SpaceId);
        Assert.Equal([parent.Id, child.Id, grandchild.Id], grandchildPolicy.Ancestry);
        // Resolving a restriction must not rewrite a descendant's authored request.
        Assert.Equal(JsonSerializer.Serialize(child), JsonSerializer.Serialize(await registry.GetAsync(child.Id, token)));
        Assert.Equal(JsonSerializer.Serialize(grandchild), JsonSerializer.Serialize(await registry.GetAsync(grandchild.Id, token)));
        Assert.Equal(JsonSerializer.Serialize(parent), JsonSerializer.Serialize(await registry.GetAsync(parent.Id, token)));
    }

    [Theory]
    [InlineData(SpaceCapabilityKind.Plugin)]
    [InlineData(SpaceCapabilityKind.Skill)]
    [InlineData(SpaceCapabilityKind.Agent)]
    [InlineData(SpaceCapabilityKind.ConnectedApp)]
    public async Task Only_selected_categories_inherit_positive_availability(SpaceCapabilityKind category)
    {
        using var files = new IsolatedSettingsFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var parent = await registry.CreateAsync("Available parent", cancellationToken: token);
        var bindings = Enum.GetValues<SpaceCapabilityKind>()
            .Select(kind => new SpaceCapabilityBinding("same-canonical-id", kind, SpaceCapabilityOverride.Enabled, kind + "-version"))
            .ToArray();
        parent = await registry.UpdateAsync(parent with { CapabilityBindings = bindings }, parent.Revision, token);
        var child = await registry.CreateAsync("Selective child", null, parent.Id, token);
        child = await registry.UpdateAsync(child with
        {
            Inheritance = new SpaceInheritancePolicy(
                ConnectedApps: category == SpaceCapabilityKind.ConnectedApp,
                Plugins: category == SpaceCapabilityKind.Plugin,
                Skills: category == SpaceCapabilityKind.Skill,
                Agents: category == SpaceCapabilityKind.Agent)
        }, child.Revision, token);

        var policy = await new SpacePolicyResolver(files.ReopenRegistry()).ResolveAsync(child.Id, token);
        // MCP and connected-app bindings share the existing ConnectedApps flag.
        var expected = bindings.Where(binding => binding.Kind == category ||
            category == SpaceCapabilityKind.ConnectedApp && binding.Kind == SpaceCapabilityKind.Mcp)
            .OrderBy(binding => binding.Kind).ToArray();
        Assert.Equal(expected, policy.Capabilities);
        Assert.Equal([parent.Id, child.Id], policy.Ancestry);
    }

    [Fact]
    public async Task Child_exclusion_is_carried_without_changing_sibling_kind_or_canonical_records()
    {
        using var files = new IsolatedSettingsFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var parent = await registry.CreateAsync("Parent", cancellationToken: token);
        parent = await registry.UpdateAsync(parent with
        {
            CapabilityBindings = [
                new("shared-id", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Enabled, "plugin-parent"),
                new("shared-id", SpaceCapabilityKind.Skill, SpaceCapabilityOverride.Enabled, "skill-parent")]
        }, parent.Revision, token);
        var child = await registry.CreateAsync("Restricting child", null, parent.Id, token);
        var exclusion = new SpaceCapabilityBinding("shared-id", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "child-exclusion");
        child = await registry.UpdateAsync(child with { CapabilityBindings = [exclusion] }, child.Revision, token);
        var grandchild = await registry.CreateAsync("Grandchild", null, child.Id, token);
        grandchild = await registry.UpdateAsync(grandchild with
        {
            CapabilityBindings = [new("shared-id", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Enabled, "grandchild-attempt")]
        }, grandchild.Revision, token);

        registry = files.ReopenRegistry();
        var resolver = new SpacePolicyResolver(registry);
        var policy = await resolver.ResolveAsync(grandchild.Id, token);
        Assert.Equal(exclusion, Assert.Single(policy.Capabilities, binding => binding.Kind == SpaceCapabilityKind.Plugin));
        Assert.Equal(parent.CapabilityBindings![1], Assert.Single(policy.Capabilities, binding => binding.Kind == SpaceCapabilityKind.Skill));
        Assert.Equal([parent.Id, child.Id, grandchild.Id], policy.Ancestry);
        Assert.Equal(JsonSerializer.Serialize(parent), JsonSerializer.Serialize(await registry.GetAsync(parent.Id, token)));
        Assert.Equal(JsonSerializer.Serialize(child), JsonSerializer.Serialize(await registry.GetAsync(child.Id, token)));
        Assert.Equal(JsonSerializer.Serialize(grandchild), JsonSerializer.Serialize(await registry.GetAsync(grandchild.Id, token)));
    }

    private static SpaceInheritancePolicy Categories(bool enabled) => new(
        ConnectedApps: enabled, Plugins: enabled, Skills: enabled, Agents: enabled);

    // A path fixture only. Persistence, serialization, leases and registry writes
    // are the real VersionedAtomicSettingsStore/SpaceRegistry implementations.
    private sealed class IsolatedSettingsFiles : IAppPaths, IDisposable
    {
        public IsolatedSettingsFiles()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-space-policy-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DataDirectory);
            else Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy.json");

        public SpaceRegistry OpenRegistry() => new(new VersionedAtomicSettingsStore(this));

        public SpaceRegistry ReopenRegistry()
        {
            Assert.True(File.Exists(Path.Combine(DataDirectory, "settings.json")));
            return OpenRegistry();
        }

        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
}
