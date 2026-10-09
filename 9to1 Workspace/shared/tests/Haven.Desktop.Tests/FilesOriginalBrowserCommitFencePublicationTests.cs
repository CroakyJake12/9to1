using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

// Actual FileHome/OS profile/Files workspace, current folder revision, production
// Files resolver/policy, individual manual approval and SAME claimed handle.
// This tests finite fence publication/cleanup, not native download or OS custody.
public sealed class FilesOriginalBrowserCommitFencePublicationTests
{
    [Fact]
    public async Task Outer_publication_fault_retains_and_joins_the_same_successfully_acquired_Files_Home_fence()
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try
        {
            await rig.Initialize();
            var homeBefore = await File.ReadAllBytesAsync(rig.HomePath, rig.Token);
            var driveBefore = await File.ReadAllBytesAsync(rig.DrivePath, rig.Token);
            var original = new IOException("The actual outer Files fence publication refused after capture.");
            HomeClaimedResourceCommitFence? captured = null; int publications = 0;
            void Capture(HomeClaimedResourceCommitFence actual)
            {
                if (captured is null) { captured = actual; rig.Fences.Add(actual); }
                Assert.Same(captured, actual);
                // Service capture returned this actual object successfully. The
                // authority's later publication is the independent refusal.
                if (++publications == 2) throw original;
            }
            var actual = rig.Authority.CaptureBrowserCommitFenceWithinOriginalSourceAsync(rig.Workspace,
                rig.Broker, rig.Capability, () => true, body => body(), rig.Retain, rig.Token, Capture).AsTask();
            rig.Retain(actual);
            var failure = await Failure(actual);
            Assert.True(OnlyObserved(failure, original));
            rig.ExpectedFaults.Add(actual, original);
            Assert.Equal(2, publications); Assert.NotNull(captured);
            var close = Assert.IsAssignableFrom<Task>(captured!.OriginalClose); await close;
            var repeated = captured.DisposeWithinOriginalSourceAsync(body => body(), rig.Retain).AsTask();
            Assert.Same(close, repeated); await repeated;
            await rig.JoinRaw(failures); Assert.Empty(failures);
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
            Assert.Equal(driveBefore, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
        }
        catch (Exception failure) { failures.Add(failure); }
        await rig.Drain(failures);
        lock (Retained) Retained.Add((rig, failures.Count == 0 ? null : new AggregateException(failures)));
        if (failures.Count != 0) throw new AggregateException("Actual Files fence fixture/originals retained.", failures);
    }

    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "astra-files-fence-publication-" + Guid.NewGuid().ToString("N"));
        internal string HomePath => Path.Combine(Root, "home.json");
        internal string Chosen => Path.Combine(Root, "chosen");
        internal string DrivePath => Path.Combine(Chosen, ".9to1-files", "drive.json");
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal ServiceProvider? Graph;
        internal NativeFilesWorkspace Workspace = null!;
        internal NativeFilesWorkspaceAuthority Authority = null!;
        internal HomeResourceOperationBroker Broker = null!;
        internal HomeResourceExecutionCapability Capability = null!;
        internal readonly List<HomeClaimedResourceCommitFence> Fences = [];
        internal readonly Dictionary<Task, Exception> ExpectedFaults = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Task> _raw = new(ReferenceEqualityComparer.Instance);
        private bool _audited;
        internal void Retain(Task actual) { lock (_raw) _raw.Add(actual); }
        internal async Task Initialize()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Chosen);
            var state = new FileHomeCoreStateStore(HomePath);
            var profiles = new HomeLocalProfileIdentity(state, new OperatingSystemPrincipalSource());
            var policy = new FilesOriginalBrowserDownloadActionPolicySource();
            var permissions = new HomePermissionTrustService(state, policy.TryGet);
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(state); services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(permissions);
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
            Graph = services.BuildServiceProvider();
            var setup = Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(Chosen,
                Graph.GetRequiredService<HomeLocalStoreOwnership>(), Token);
            Retain(setup); var configured = await setup;
            Authority = Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            // Use Home's actual current revision-keyed workspace, rather than the temporary setup instance.
            var acquisition = Authority.GetCurrentAsync(configured.Configuration.StoreId, Token);
            Retain(acquisition);
            Workspace = await acquisition
                ?? throw new InvalidOperationException("The actual configured Files workspace is unavailable.");
            Assert.Equal(configured.Actor, Workspace.Actor);
            Assert.Equal(configured.Configuration.StoreId, Workspace.Configuration.StoreId);
            var observation = Authority.GetCurrentAsync(configured.Configuration.StoreId, Token);
            Retain(observation); var current = await observation;
            Assert.NotNull(current);
            Assert.Same(Workspace.Provider, current.Provider);
            Assert.Same(Workspace.Directories, current.Directories);
            Assert.Same(Workspace.Materializations, current.Materializations);
            var folder = (await Workspace.Provider.GetAsync(Workspace.Configuration.AppFolders["picture"], Token)).Value!;
            Assert.Equal(HostedItemKind.Folder, folder.Kind);
            var revision = folder.CurrentRevisionId ?? throw new InvalidOperationException("No actual configured folder revision.");
            var resources = Graph.GetRequiredService<ResourceAuthorizationService>();
            Broker = new(resources, permissions);
            const string action = "9to1.Files.RegisterBrowserDownload";
            ResourceScope[] scopes = [new("files.item", folder.Id.ToString(), revision.ToString(), ResourceAccess.Write)];
            var arguments = JsonSerializer.SerializeToElement(new { operationId = Guid.NewGuid(), purpose = "finite fence publication test only" });
            var review = Broker.PrepareReviewForActor(Workspace.Actor, "files", action, scopes, arguments,
                "Actual separately approved Files fence; no download or content mutation.", null, "files-original-fence-publication");
            var submitted = await Broker.AuthorizePreparedReviewAsync(review, Token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, submitted.Request!.State);
            Assert.True((await permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            Capability = await Broker.BeginExecutionCapabilityAsync(review.RequestId, arguments, Token)
                ?? throw new InvalidOperationException("No actual approved Files capability.");
            Assert.Equal(HomeResourceClaimDisposition.Claimed, (await Broker.ClaimExecutionObservedAsync(Capability,
                "files", action, scopes, arguments, Token)).Disposition);
        }
        internal async Task JoinRaw(List<Exception> failures)
        {
            Task[] originals; lock (_raw) originals = _raw.ToArray();
            foreach (var actual in originals)
            {
                try { await actual; }
                catch (Exception failure)
                {
                    var payload = actual.Exception ?? failure;
                    if (!ExpectedFaults.TryGetValue(actual, out var same) || !OnlyObserved(payload, same)) failures.Add(payload);
                }
            }
        }
        internal async Task Drain(List<Exception> failures)
        {
            foreach (var fence in Fences)
                try { await fence.DisposeWithinOriginalSourceAsync(body => body(), Retain); }
                catch (Exception failure) { failures.Add(fence.OriginalClose?.Exception ?? failure); }
            await JoinRaw(failures);
            // Actual Home audit is entered only after every SAME held release and
            // retained source has independently settled. Unknown cleanup stays retained.
            if (failures.Count == 0 && Capability is not null && !_audited)
            {
                try
                {
                    var actual = Broker.CompleteExecutionAsync(Capability,
                        new(HomePermissionRequestState.Failed, "TEST_FILES_FENCE_NO_EFFECT",
                            "Actual fence publication refused; no Files content or metadata effect occurred.", []), Token);
                    Retain(actual); _audited = (await actual).Succeeded; Assert.True(_audited);
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            // Failed source occurrences remain on this exact retained graph. No deletion.
            if (failures.Count == 0 && ExpectedFaults.Count == 0 && Graph is not null)
                try { await Graph.DisposeAsync(); } catch (Exception failure) { failures.Add(failure); }
        }
    }
    private static readonly List<(Rig Original, Exception? Failure)> Retained = [];
    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; } catch (Exception failure) { return actual.Exception ?? failure; }
        throw new InvalidOperationException("The SAME actual fence publication Task was expected to fail.");
    }
    private static bool OnlyObserved(Exception actual, Exception same) => ReferenceEquals(actual, same) ||
        actual is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(cause => OnlyObserved(cause, same));
}
