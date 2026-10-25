using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual OS context refusals and real signed-fixture held-code lifetime.
/// No service registration, native helper launch, privilege change or production
/// publisher/installed-peer authority is performed or fabricated.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomeRootServiceRuntimeTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux filesystem/socket APIs."; } }
    private sealed class LinuxAdminFactAttribute : FactAttribute
    { public LinuxAdminFactAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires actual Linux UID0 isolated signed-file fixture."; } }

    [LinuxAdminFact]
    public async Task Settled_code_source_retains_exact_original_files_until_runtime_callback_and_rejects_unlisted_files()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        LinuxHomeBootstrapActivationOwner.PublishedCode? retained = null;
        var before = File.ReadAllBytes(rig.StatePath);
        await rig.Owner.WithinSettledCodeAsync(rig.Request(), async (code, token) => {
            retained = code;
            var home = code.Packages.Single(package => package.AppId == "home");
            using var image = code.OpenOriginalFile(home, home.EntrypointRelativePath);
            var bytes = new byte[rig.Elf.Length]; await RandomAccess.ReadAsync(image, bytes, 0, token);
            Assert.Equal(rig.Elf, bytes);
            Assert.Throws<UnauthorizedAccessException>(() => code.OpenOriginalFile(home, "receipt.json"));
            await code.DemandCurrentAsync(token);
        }, rig.Token);
        Assert.NotNull(retained);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => retained!.DemandCurrentAsync(rig.Token));
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }

    [LinuxAdminFact]
    public async Task Runtime_inspection_preserves_actual_pending_publication_without_implicitly_recovering_it()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(failAfterPublication: true); await rig.Enroll();
        await Assert.ThrowsAsync<LinuxHomeBootstrapActivationOwner.ActivationUnconfirmedException>(() => rig.Owner.ActivateAsync(rig.Request(), rig.Token));
        var before = File.ReadAllBytes(rig.StatePath); var called = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Owner.WithinSettledCodeAsync(rig.Request(), (_, _) => { called = true; return Task.CompletedTask; }, rig.Token));
        Assert.False(called); Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
        Assert.All((await rig.Database.ReadAsync(rig.Token)).Snapshot!.RecentOperations, item => Assert.Equal(HomePackageJournalState.Validated, item.State));
    }

    [LinuxAdminFact]
    public async Task Runtime_callback_IO_failure_is_preserved_and_never_reported_as_an_installer_mutation()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(); await rig.Enroll();
        await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var failure = new IOException("Actual owning runtime fixture refusal after real signed-code inspection.");
        var actual = await Assert.ThrowsAsync<IOException>(() => rig.Owner.WithinSettledCodeAsync(rig.Request(), (_, _) => Task.FromException(failure), rig.Token));
        Assert.Same(failure, actual);
    }

    [LinuxFact]
    public void Unissued_logind_record_cannot_create_an_original_active_user()
    {
        var kernel = new LinuxHomeRegisteredRootServiceContext();
        var unissued = new LinuxHomeRegisteredRootServiceContext.InteractiveUser(kernel, "unissued.fixture.session", 1000, "wayland", null);
        Assert.Throws<UnauthorizedAccessException>(() => kernel.DemandOriginalCurrent(unissued));
    }

    [LinuxFact]
    public async Task Ordinary_nonservice_process_does_not_start_or_mutate_the_configured_canonical_state()
    {
        var observed = new LinuxHomeRegisteredRootServiceContext().ObserveOriginalActiveUser();
        // A real service-owned test invocation must be evaluated by its own explicit
        // runtime fixture, never relabelled as a negative ordinary-process control.
        Assert.Null(observed.OriginalUser);
        Assert.NotNull(observed.UnavailableReason);
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig();
        await using var runtime = new LinuxHomeRootServiceRuntime(rig.Owner);
        await runtime.RunOriginalAsync(new(rig.Request(), Path.Combine(rig.Root, "unregistered-control")), rig.Token);
        var startup = await runtime.OriginalStartup;
        Assert.False(startup.HasAcceptedOriginalHomeControl); Assert.NotNull(startup.MissingPrerequisite);
        Assert.False(File.Exists(rig.StatePath)); Assert.False(Directory.Exists(Path.Combine(rig.Root, "unregistered-control")));
    }
    [LinuxFact]
    public async Task Failed_real_Root_connection_preserves_foreign_endpoint_and_creates_no_Home_profile_or_installed_authority()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig();
        var endpoint = Path.Combine(rig.Root, "foreign.sock"); File.WriteAllText(endpoint, "preserve unrelated file");
        var userState = Path.Combine(rig.Root, "user-state.json");
        var store = new HavenOS.Home.Core.FileHomeCoreStateStore(userState);
        var profiles = new HavenOS.Home.Core.HomeLocalProfileIdentity(store, new HavenOS.Home.Core.OperatingSystemPrincipalSource());
        var client = new LinuxHomeRootClient(endpoint, rig.Token);
        client.BindOriginalHomeInventory(store, profiles, new(store, profiles, [client]));
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectOriginalWithinSourceAsync(body => body(), _ => { }, rig.Token));
        await Assert.ThrowsAsync<AggregateException>(() => client.CloseAndDrainAsync());
        Assert.Equal("preserve unrelated file", File.ReadAllText(endpoint)); Assert.False(File.Exists(userState));
    }

    [LinuxFact]
    public async Task Omitted_original_client_callback_is_retained_before_connect_and_cannot_be_retried_as_healthy()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig();
        var endpoint = Path.Combine(rig.Root, "not-created.sock");
        var store = new HavenOS.Home.Core.FileHomeCoreStateStore(Path.Combine(rig.Root, "user-state.json"));
        var profiles = new HavenOS.Home.Core.HomeLocalProfileIdentity(store, new HavenOS.Home.Core.OperatingSystemPrincipalSource());
        var client = new LinuxHomeRootClient(endpoint, rig.Token); client.BindOriginalHomeInventory(store, profiles, new(store, profiles, [client]));
        Assert.Throws<InvalidOperationException>(() => { _ = client.ConnectOriginalWithinSourceAsync(_ => { }, _ => { }, rig.Token); });
        await Assert.ThrowsAsync<AggregateException>(() => client.CloseAndDrainAsync()); Assert.False(File.Exists(endpoint));
    }

    [LinuxFact]
    public async Task Actual_kernel_start_metadata_and_copied_installed_tuple_cannot_replace_registered_Root_process_proof()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var root = Path.Combine(Path.GetTempPath(), "unconfigured-root-proof-" + Guid.NewGuid().ToString("N"));
        var endpoint = Path.Combine(root, "never-bound.sock");
        await using var client = new LinuxHomeRootClient(endpoint, lifetime.Token);
        var actualStart = HavenOS.Home.Core.HomeNativePeerObservation.ReadLinuxProcessStartMetadata(Environment.ProcessId);
        Assert.StartsWith("linux-proc-start:", actualStart);
        var peer = new Haven.Application.HomeNativeObservedPeer(Environment.ProcessId, "unix-euid:" + GetEuid());
        var copiedActor = new Haven.Application.AuthenticatedResourceActor("unissued-actor", "unissued-profile", null, null, "unissued-revision");
        var copiedInstalled = new Haven.Application.HomeNativeInstalledPeer("home", Guid.NewGuid(), "unissued-installation",
            "unissued-executable", new HashSet<string> { "home.core" });
        Assert.False(await client.IsOriginalControlledProcessCurrentAsync(peer, copiedActor, actualStart, copiedInstalled, lifetime.Token));
        Assert.False(Directory.Exists(root)); // proof refusal creates no profile, socket, state or installed authority
        Assert.Equal(actualStart, HavenOS.Home.Core.HomeNativePeerObservation.ReadLinuxProcessStartMetadata(Environment.ProcessId));
    }

    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
