using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Owning refusal/current-source controls only. No installed/publisher happy-path
// qualification is inferred from missing root ports or these test implementations.
public sealed class NativeWindowsHomePackageActivationOwnerTests
{
    [Fact]
    public async Task Foreign_device_store_cannot_be_composed_as_the_original_activation_source()
    {
        await WithFixture(async rig =>
        {
            var other = new FileHomeCoreStateStore(Path.Combine(rig.Root, "other.json"));
            Assert.Throws<UnauthorizedAccessException>(() => new NativeWindowsHomePackageActivationOwner(rig.Device, other,
                Path.Combine(rig.Root, "other.json"), rig.Artifacts, rig.RootPort, rig.Profiles, () => null, "spaces"));
            Assert.False(File.Exists(Path.Combine(rig.Root, "other.json"))); await Task.CompletedTask;
        });
    }
    [WindowsActivationFact]
    public Task Missing_actual_retained_root_returns_no_installed_entry_and_does_not_open_privileged_ports()
        => WithFixture(async rig =>
        {
            var original = rig.Source.ObserveWithinOriginalSourceAsync(body => body(), rig.Retain, rig.Token).AsTask();
            var observed = await original; Assert.Empty(observed); Assert.Equal(0, rig.RootPort.Calls); Assert.Equal(0, rig.Artifacts.Calls);
            var close = rig.Source.CloseAndDrainAsync(); Assert.Same(close, rig.Source.CloseAndDrainAsync()); await close;
            Assert.True(close.IsCompletedSuccessfully); Assert.True(original.IsCompletedSuccessfully);
            Assert.All(rig.Raw, actual => Assert.True(actual.IsCompletedSuccessfully));
        });
    [WindowsActivationFact]
    public Task Actual_source_callback_with_restored_context_refuses_joining_the_same_observer()
        => WithFixture(async rig =>
        {
            var clean = ExecutionContext.Capture()!; var calls = 0;
            void Scope(Action body)
            {
                body();
                ExecutionContext.Run(clean.CreateCopy(), _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { _ = rig.Source.CloseAndDrainAsync(); });
                    Assert.Throws<InvalidOperationException>(() => rig.Source.DemandExternalOriginalJoin()); calls++;
                }, null);
            }
            var observed = await rig.Source.ObserveWithinOriginalSourceAsync(Scope, rig.Retain, rig.Token);
            Assert.Empty(observed); Assert.True(calls > 0); Assert.Null(rig.Source.OriginalClose);
            await rig.Source.CloseAndDrainAsync();
        });
    public sealed class WindowsActivationFactAttribute : FactAttribute
    { public WindowsActivationFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Actual Windows source producer; Linux does not establish installed Windows evidence."; } }
    private static async Task WithFixture(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { errors.Add(cause); }
        Task? sourceClose = null, deviceClose = null;
        try { sourceClose = rig.Source.CloseAndDrainAsync(); await sourceClose; } catch (Exception cause) { errors.Add(sourceClose?.Exception ?? cause); }
        Task[] originals; lock (rig.Raw) originals = rig.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var raw in originals) try { await raw; } catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
        try { deviceClose = rig.Device.CloseAndDrainAsync(); await deviceClose; } catch (Exception cause) { errors.Add(deviceClose?.Exception ?? cause); }
        rig.Active.Dispose();
        if (errors.Count != 0) throw new AggregateException("Actual activation control failed; original fixture retained at " + rig.Root, errors);
        Directory.Delete(rig.Root, true); // Exclusive test fixture, after SAME originals join.
    }
    private sealed class Rig
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("original-activation-control-").FullName;
        internal readonly CancellationTokenSource Active = new(TimeSpan.FromSeconds(30));
        internal CancellationToken Token => Active.Token;
        internal readonly List<Task> Raw = [];
        internal void Retain(Task actual) { lock (Raw) Raw.Add(actual); }
        internal readonly MissingArtifacts Artifacts = new(); internal readonly MissingRoot RootPort = new();
        internal readonly HomePackageOriginalDeviceOwner Device;
        internal readonly HomeLocalProfileIdentity Profiles;
        internal readonly NativeWindowsHomePackageActivationOwner Source;
        internal Rig()
        {
            var file = Path.Combine(Root, "home.json"); var store = new FileHomeCoreStateStore(file);
            Profiles = new(store, new OperatingSystemPrincipalSource()); Device = new(store, Artifacts, RootPort, Token);
            Source = new(Device, store, file, Artifacts, RootPort, Profiles, () => null, "spaces");
        }
    }
    // Explicit missing-port fixtures. These callbacks must never be invoked or
    // provide accepted admission, installed peer, root outcome or publisher evidence.
    private sealed class MissingArtifacts : IHomePackageOriginalArtifactProvider
    {
        internal int Calls;
        public ValueTask<HomePackageOriginalArtifactObservation?> ResolveAsync(HomePackageActionRequest request, HomePackageDatabaseSnapshot registry, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No enrolled artifact owner."); }
        public HomePackageOriginalActionMap? ResolveAction(HomePackageAction action, HomePackageArtifactDescriptor descriptor)
        { Calls++; throw new InvalidOperationException("No enrolled artifact owner."); }
        public Task DemandOriginalCurrentAsync(HomePackageOriginalArtifactObservation artifact, AuthenticatedResourceActor actor,
            HomeNativeInstalledPeer caller, string session, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No enrolled artifact owner."); }
    }
    private sealed class MissingRoot : IHomePackageOriginalRootMutationPort
    {
        internal int Calls;
        public Task<IHomePackageOriginalRootMutation> OpenOriginalAsync(HomePackageOriginalRootRequest request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("No enrolled root channel."); }
    }
}
