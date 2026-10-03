using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Spaces;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class HomePluginSidebarAuthorityTests
{
    [Fact]
    public async Task Missing_authority_denies_and_read_only_access_cannot_manage_or_open_without_host()
    {
        var space = Guid.NewGuid(); var packages = new Packages { Items = [Package(space)] };
        var missing = new HomePluginSidebarAuthority(packages);
        Assert.Empty(await missing.GetDeclaredAsync(space, TestContext.Current.CancellationToken)); Assert.Equal(0, packages.Reads);
        Assert.False(await missing.MayManageAsync(space, TestContext.Current.CancellationToken));
        var authority = new HomePluginSidebarAuthority(packages, new Access());
        var item = Assert.Single(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken));
        Assert.False(await authority.MayManageAsync(space, TestContext.Current.CancellationToken));
        Assert.False(await authority.MayOpenAsync(space, item, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.OpenAsync(space, item, TestContext.Current.CancellationToken));
        Assert.Equal(0, packages.Writes);
    }
    [Fact]
    public async Task Exact_space_enablement_grants_package_kind_and_unambiguous_identity_are_required()
    {
        var space = Guid.NewGuid(); var valid = Package(space); var packages = new Packages();
        var authority = new HomePluginSidebarAuthority(packages, new Access());
        foreach (var invalid in new[] { valid with { IsEnabled = false }, valid with { State = ExtensionInstallState.Quarantined },
            valid with { EnabledScopes = ["device"] }, valid with { EnabledScopes = ["space:" + Guid.NewGuid().ToString("D")] },
            valid with { GrantedPermissions = ExtensionPermission.None }, valid with { Manifest = valid.Manifest with { PackageType = ExtensionPackageType.Skill } } })
        { packages.Items = [invalid]; Assert.Empty(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken)); }
        packages.Items = [valid, valid with { Id = Guid.NewGuid() }];
        Assert.Empty(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken));
        packages.Items = [valid]; Assert.Single(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Read_revoked_during_real_repository_await_clears_declarations_and_cancellation_propagates()
    {
        var space = Guid.NewGuid(); var access = new Access();
        var packages = new Packages { Items = [Package(space)], OnRead = () => { access.Read = false; return Task.CompletedTask; } };
        var authority = new HomePluginSidebarAuthority(packages, access);
        Assert.Empty(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken)); Assert.Equal(0, packages.Writes);
        access.Read = true; packages.OnRead = () => throw new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Typed_target_is_revalidated_and_never_opened_from_forged_or_revoked_declaration()
    {
        var space = Guid.NewGuid(); var packages = new Packages { Items = [Package(space)] }; var access = new Access(); var host = new Host();
        var authority = new HomePluginSidebarAuthority(packages, access, host);
        var item = Assert.Single(await authority.GetDeclaredAsync(space, TestContext.Current.CancellationToken));
        await authority.OpenAsync(space, item, TestContext.Current.CancellationToken); Assert.Equal(item, Assert.Single(host.Opened));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.OpenAsync(space, item with { TargetId = "arbitrary command" }, TestContext.Current.CancellationToken));
        access.Read = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.OpenAsync(space, item, TestContext.Current.CancellationToken));
        Assert.Single(host.Opened); Assert.Equal(0, packages.Writes);
    }
    private static InstalledExtensionPackage Package(Guid space)
    {
        var manifest = new ExtensionPackageManifest("canonical.plugin", "plugin", "Plugin", ExtensionPackageType.Plugin, "1.0", "*", "", "", "", null, "MIT",
            ExtensionPermission.ReadHavenData, [], [], [], null)
        { SidebarContributions = [new("declared.page", "Declared page", "page", ExtensionSidebarTargetKind.Page, "canonical.page")] };
        return new(Guid.NewGuid(), Guid.NewGuid(), manifest, "/controlled/package", ExtensionPermission.ReadHavenData,
            ExtensionInstallState.Installed, true, false, "controlledhash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            EnabledScopes: ["space:" + space.ToString("D")]);
    }
    private sealed class Access : IPluginSidebarSpaceAccess
    {
        public bool Read = true;
        public Task<bool> MayReadAsync(Guid id, CancellationToken ct) => Task.FromResult(Read);
        public Task<bool> MayManageAsync(Guid id, CancellationToken ct) => Task.FromResult(false);
    }
    private sealed class Host : IPluginSidebarTargetHost
    {
        public List<PluginSidebarContribution> Opened { get; } = [];
        public Task<bool> MayOpenAsync(Guid id, PluginSidebarContribution item, CancellationToken ct) => Task.FromResult(true);
        public Task OpenAsync(Guid id, PluginSidebarContribution item, CancellationToken ct) { Opened.Add(item); return Task.CompletedTask; }
    }
    private sealed class Packages : IExtensionRepository
    {
        public IReadOnlyList<InstalledExtensionPackage> Items = []; public int Reads; public int Writes; public Func<Task>? OnRead;
        public async Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken ct) { Reads++; if (OnRead is not null) await OnRead(); return Items; }
        public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExtensionSource>>([]);
        public Task UpsertSourceAsync(ExtensionSource source, CancellationToken ct) { Writes++; throw new NotSupportedException(); }
        public Task DeleteSourceAsync(Guid id, CancellationToken ct) { Writes++; throw new NotSupportedException(); }
        public Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken ct) { Writes++; throw new NotSupportedException(); }
        public Task DeleteInstalledAsync(Guid id, CancellationToken ct) { Writes++; throw new NotSupportedException(); }
    }
}
