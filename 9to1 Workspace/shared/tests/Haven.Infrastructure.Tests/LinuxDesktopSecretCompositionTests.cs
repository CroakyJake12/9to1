using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure.Tests;

[Collection("Linux desktop application paths")]
public sealed class LinuxDesktopSecretCompositionTests
{
    [LinuxDesktopFact]
    public async Task Actual_Home_registration_routes_Mesh_identity_through_same_private_store_and_reopens_it()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning control requires Linux.");
        var root = Directory.CreateTempSubdirectory("haven-linux-desktop-secret-").FullName;
        try
        {
            var paths = new Paths(root);
            var principal = new OperatingSystemPrincipalSource();
            await using var home = Home(paths, principal);
            var services = Graph();
            services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
            services.AddHavenMesh();
            var device = Guid.NewGuid();
            const string controlledIdentity = "controlled-identity-not-a-live-credential";
            await using (var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
            {
                Assert.Same(home.Profiles, provider.RequireOriginalWindowsHomeComponents(home).Profiles);
                Assert.Same(paths, provider.GetRequiredService<IAppPaths>());
                var original = provider.GetRequiredService<LinuxProviderSecretStore>();
                Assert.Same(original, provider.GetRequiredService<IProviderSecretStore>());
                Assert.True(original.HasOriginalComposition(paths));
                var mesh = provider.GetRequiredService<IMeshIdentitySecretStore>();
                await mesh.SetPrivateKeyAsync(device, controlledIdentity, CancellationToken.None);
                Assert.Equal(controlledIdentity, await mesh.GetPrivateKeyAsync(device, CancellationToken.None));
                Assert.DoesNotContain(controlledIdentity, System.Text.Encoding.UTF8.GetString(
                    await File.ReadAllBytesAsync(Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential").Single())));
            }
            using var reopened = new LinuxProviderSecretStore(paths);
            var reopenedMesh = new MeshIdentitySecretStore(reopened);
            Assert.Equal(controlledIdentity, await reopenedMesh.GetPrivateKeyAsync(device, CancellationToken.None));
            await reopenedMesh.DeletePrivateKeyAsync(device, CancellationToken.None);
            Assert.Null(await reopenedMesh.GetPrivateKeyAsync(device, CancellationToken.None));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LinuxDesktopFact]
    public async Task Foreign_secret_descriptor_is_refused_before_Home_or_paths_registration_changes()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning control requires Linux.");
        var root = Directory.CreateTempSubdirectory("haven-linux-desktop-secret-").FullName;
        try
        {
            var paths = new Paths(root);
            var principal = new OperatingSystemPrincipalSource();
            await using var home = Home(paths, principal);
            var services = Graph();
            services.AddSingleton<IProviderSecretStore>(new WindowsProviderSecretStore());
            var before = services.ToArray();
            Assert.Throws<InvalidOperationException>(() => services.AddHavenOwnedWindowsHomeDomain(home, paths, principal));
            Assert.Equal(before, services.ToArray());
            Assert.False(Directory.Exists(Path.Combine(root, "Credentials")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LinuxDesktopFact]
    public async Task Replaced_paths_are_refused_before_credential_acquisition()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning control requires Linux.");
        var root = Directory.CreateTempSubdirectory("haven-linux-desktop-secret-").FullName;
        try
        {
            var paths = new Paths(root);
            var principal = new OperatingSystemPrincipalSource();
            await using var home = Home(paths, principal);
            var services = Graph();
            services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
            services.RemoveAll<IAppPaths>();
            services.AddSingleton<IAppPaths>(new Paths(root)); // Equal path text is not the same owner.
            await using var provider = services.BuildServiceProvider();
            Assert.Throws<UnauthorizedAccessException>(() => provider.GetRequiredService<IProviderSecretStore>());
            Assert.False(Directory.Exists(Path.Combine(root, "Credentials")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LinuxDesktopFact]
    public async Task Existing_insecure_root_is_refused_without_permission_changes()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning control requires Linux.");
        var root = Directory.CreateTempSubdirectory("haven-linux-desktop-secret-").FullName;
        try
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead;
            File.SetUnixFileMode(root, mode);
            var paths = new Paths(root);
            var principal = new OperatingSystemPrincipalSource();
            await using var home = Home(paths, principal);
            var services = Graph();
            services.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
            await using var provider = services.BuildServiceProvider();
            var failure = Assert.Throws<AggregateException>(() => provider.GetRequiredService<IProviderSecretStore>());
            Assert.Contains(failure.Flatten().InnerExceptions, cause => cause is UnauthorizedAccessException);
            Assert.Equal(mode, File.GetUnixFileMode(root));
            Assert.False(Directory.Exists(Path.Combine(root, "Credentials")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LinuxDesktopFact]
    public void New_application_directories_are_private_and_existing_permissions_are_not_rewritten()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This owning control requires Linux.");
        var parent = Directory.CreateTempSubdirectory("haven-linux-desktop-paths-").FullName;
        var previous = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        try
        {
            var root = Path.Combine(parent, "fresh");
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", root);
            var paths = new AppPaths();
            var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            foreach (var directory in new[] { paths.DataDirectory, paths.AttachmentsDirectory, paths.BrowserProfileDirectory, paths.LogsDirectory })
                Assert.Equal(privateMode, File.GetUnixFileMode(directory));
            var insecure = privateMode | UnixFileMode.OtherRead;
            File.SetUnixFileMode(root, insecure);
            _ = new AppPaths();
            Assert.Equal(insecure, File.GetUnixFileMode(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previous);
            Directory.Delete(parent, recursive: true);
        }
    }

    private sealed class LinuxDesktopFactAttribute : FactAttribute
    {
        public LinuxDesktopFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
                Skip = "Requires actual Linux Home credential custody; other platforms remain separately unverified.";
        }
    }

    private static ServiceCollection Graph()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        return services;
    }

    private static HomeNativeWindowsComposition Home(IAppPaths paths, ITrustedHostPrincipalSource principal) =>
        new(new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json")), principal, paths,
            new HomeNativeWindowsEndpoint("linux-secret-composition-control-" + Guid.NewGuid().ToString("N")));

    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
    }
}

// AppPaths reads the process environment; its controlled construction cannot overlap
// any other collection using the default application paths.
[CollectionDefinition("Linux desktop application paths", DisableParallelization = true)]
public sealed class LinuxDesktopApplicationPathsCollection;
