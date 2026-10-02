using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class CompatibilityPackageHealthNativeTests
{
    [Theory]
    [InlineData("valid", "Inspected", "selected Files revision was checked")]
    [InlineData("unsupported", "Unsupported", "choose a supported EXE, MSI or APK")]
    [InlineData("damaged", "Invalid", "choose an intact package")]
    public async Task ActualFilesOwnedPackageCheckDisplaysActionableHealthWithoutExecuting(string kind, string state, string message)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var selected = await f.UploadAsync(kind);
            var closed = 0;
            using var bindings = new OsCompatibilityPackageBindings(f.Inspector, f.Actors, selected, () => closed++, default);
            await bindings.RefreshAsync(default); Assert.Equal(0, closed); Assert.Equal(state, Read(bindings, "PackageState"));
            if (kind == "valid") Assert.Equal("Windows application", Read(bindings, "Format"));
            else { Assert.Equal("", Read(bindings, "Name")); Assert.Equal("", Read(bindings, "DeclaredIdentity")); }
            using var loader = new CuiControlLoader(); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.CompatibilityPackage.cui");
            using var reader = new StreamReader(stream!); var (control, diagnostics) = loader.LoadMarkup(await reader.ReadToEndAsync());
            Assert.NotNull(control); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(control!); var window = new Window { Content = control, Width = 720, Height = 520 }; window.Show(); window.UpdateLayout();
            try
            {
                Assert.Contains(message, Assert.IsType<string>(Assert.Single(Traverse(control!).OfType<TextBlock>(), t => t.Name == "package-inspection-status").Text));
                Assert.False(Assert.Single(Traverse(control!).OfType<Button>(), b => Equals(b.Content, "Install")).IsEnabled);
                await bindings.DispatchAsync("Refresh", null); Assert.Equal(state, Read(bindings, "PackageState"));
                Assert.Equal(f.PreservedDriveBytes, await File.ReadAllBytesAsync(f.DrivePath));
                await bindings.DispatchAsync("Close", null); Assert.Equal(1, closed); Assert.Equal("", Read(bindings, "Status"));
            }
            finally { window.Close(); }
            return true;
        }, default));
    }
    [Fact]
    public async Task FailedFormatStatusDoesNotAdoptReplacedHomePrincipalOnRetry()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var selected = await f.UploadAsync("unsupported"); var closed = 0;
            using var bindings = new OsCompatibilityPackageBindings(f.Inspector, f.Actors, selected, () => closed++, default);
            await bindings.RefreshAsync(default); Assert.Equal("Unsupported", Read(bindings, "PackageState"));
            f.Principal.Value = "different-host-principal";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bindings.RefreshAsync(default));
            Assert.Equal(1, closed); Assert.Equal("", Read(bindings, "Status")); Assert.Equal("", Read(bindings, "Name"));
            Assert.Equal(f.PreservedDriveBytes, await File.ReadAllBytesAsync(f.DrivePath));
            return true;
        }, default));
    }
    private static string Read(OsCompatibilityPackageBindings bindings, string name)
    { Assert.True(bindings.TryGetValue(name, out var value)); return Assert.IsType<string>(value); }
    private static IEnumerable<Control> Traverse(Control root)
    { yield return root; foreach (var child in root.GetLogicalChildren().OfType<Control>()) foreach (var descendant in Traverse(child)) yield return descendant; }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public string Value = "original-package-host"; public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value); }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-package-health-" + Guid.NewGuid().ToString("N"));
        public Principal Principal { get; } = new(); public ServiceProvider Graph { get; }
        public IAuthenticatedResourceActorSource Actors => Graph.GetRequiredService<IAuthenticatedResourceActorSource>();
        public CompatibilityPackageInspector Inspector => Graph.GetRequiredService<CompatibilityPackageInspector>();
        public string DrivePath => Path.Combine(root, "chosen", ".9to1-files", "drive.json");
        public byte[] PreservedDriveBytes { get; private set; } = [];
        public Fixture()
        {
            Directory.CreateDirectory(root); var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, Principal); var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>(); services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>(); services.AddSingleton<ResourceAuthorizationService>();
            services.AddFilesNativeHost(); services.AddSingleton<CompatibilityPackageInspector>(); Graph = services.BuildServiceProvider();
        }
        public async Task<CompatibilityPackageSource> UploadAsync(string kind)
        {
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            await Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen, Graph.GetRequiredService<HomeLocalStoreOwnership>(), default);
            var authority = Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(); var workspace = (await authority.GetCurrentAsync(default))!;
            var folder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["picture"], default)).Value!;
            var directory = (await workspace.Directories.ResolveProfileAsync(Guid.Parse(workspace.Actor.ProfileId), "picture", default)).Value!.DirectoryPath;
            var bytes = new byte[1024];
            if (kind != "unsupported")
            {
                bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), kind == "damaged" ? uint.MaxValue : 128);
                bytes[128] = (byte)'P'; bytes[129] = (byte)'E'; BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132), 0x8664);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(134), 1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 240);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(150), 2); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152), 0x20b);
            }
            await File.WriteAllBytesAsync(Path.Combine(directory, "selected.exe"), bytes);
            var id = HostedItemId.New(); var revision = new FilesRevisionId(Guid.NewGuid()); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var upload = new FilesUploadedContent(id, folder.Id, "selected.exe", "application/octet-stream", revision, null,
                workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, hash, "selected.exe");
            var guard = await authority.CaptureCommitAuthorityAsync(workspace.Actor, workspace.Provider, () => true, default);
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(upload, [new(folder.Id, folder.CurrentRevisionId)], workspace.Configuration.StoreId, guard, default)).IsSuccess);
            var browser = Graph.GetRequiredService<FilesNativeBrowserService>();
            var page = await browser.ListAsync(workspace.Actor, parentID: folder.Id.Value, expectedStoreId: workspace.Configuration.StoreId);
            var selected = Assert.Single(page.Items, item => item.Id == id);
            var result = await browser.ReadPackageSelectionAsync(page, selected, workspace.Actor, 256L * 1024 * 1024, default);
            PreservedDriveBytes = await File.ReadAllBytesAsync(DrivePath); return result;
        }
        public void Dispose() { Graph.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
