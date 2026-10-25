using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Haven.Infrastructure.Native.Windows;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Selected-app consumer of the SAME actual registered Root/controlled-child
/// proof. A locator, copied witness or UID cannot create an installed Home role.</summary>
public sealed class LinuxHomeRootHostVerifier : IHomeNativeSessionHostVerifier, IAsyncDisposable
{
    private readonly string _endpoint;
    private readonly string _compiledAppId;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private readonly List<Task> _originals = [];
    private bool _retiring;
    private Task? _close;
    internal sealed record Observation(NativeWindowsHomeRootWire.Listening Listening,
        LinuxHomeRootControlWire.ProcessWitness HomeProcess, HomePackageOriginalInstalledActivationRecord HomeActivation,
        NativeWindowsHomeRootWire.SelectedWitness SelectedProcess);
    public LinuxHomeRootHostVerifier(string actualRootEndpoint, string compiledApplicationId, CancellationToken actualAppLifetime)
    {
        if (!OperatingSystem.IsLinux() || !actualAppLifetime.CanBeCanceled || !Path.IsPathFullyQualified(actualRootEndpoint) ||
            System.Text.Encoding.UTF8.GetByteCount(actualRootEndpoint) > 100 || string.IsNullOrWhiteSpace(compiledApplicationId) ||
            compiledApplicationId is "root" or "home") throw new ArgumentException("The actual selected app lifetime/compiled app ID and bounded original Root locator are required.");
        _endpoint = actualRootEndpoint; _compiledAppId = compiledApplicationId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(actualAppLifetime);
    }
    internal Task<Observation> ObserveOriginalHomeAsync(CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _originals.RemoveAll(actual => actual.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Settle retained original Root host observations before another request.");
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = Read(begin.Task, token); _originals.Add(actual); begin.SetResult(); return actual;
        }
    }
    private async Task<Observation> Read(Task begin, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token); lifetime.CancelAfter(TimeSpan.FromSeconds(10));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint), lifetime.Token).ConfigureAwait(false);
        using var root = HomeNativePeerObservation.RetainOriginalUnixServiceLifetime(socket)
            ?? throw new UnauthorizedAccessException("The actual connected Root process lifetime is unavailable.");
        void DemandRoot()
        {
            var actual = root.ObserveCurrent();
            if (actual?.OperatingSystemPrincipalId != "unix-euid:0" || !LinuxHomeRegisteredRootServiceContext.IsOriginalRegisteredProcess(actual.ProcessId))
                throw new UnauthorizedAccessException("The SAME actual registered Root service is required.");
        }
        DemandRoot(); using var stream = new NetworkStream(socket, ownsSocket: false);
        var request = new LinuxHomeRootControlWire.Request(1, Guid.NewGuid(), "observe-home-for-selected");
        await LinuxHomeRootServiceRuntime.WriteFrame(stream, request, lifetime.Token).ConfigureAwait(false);
        var response = await LinuxHomeRootServiceRuntime.ReadFrame<LinuxHomeRootControlWire.Response>(stream, lifetime.Token).ConfigureAwait(false);
        DemandRoot();
        var listening = response?.Listening; var home = response?.Activation; var host = response?.OriginalHomeProcess; var self = response?.SelectedWitness;
        if (response is not { SchemaVersion: 1, Accepted: true } || response.CorrelationId != request.CorrelationId ||
            listening is null || home is not { AppId: "home", Platform: "linux" } || host is null || self is null ||
            response.SelectedChoice != self.OriginalChoice || self.OriginalChoice.OriginalListening != listening ||
            self.OriginalChoice.AppId != _compiledAppId || self.ProcessId != Environment.ProcessId ||
            self.OperatingSystemPrincipalId != "unix-euid:" + LinuxOriginalDownloadPaths.CurrentUid() ||
            self.ProcessStartIdentity != HomeNativePeerObservation.ReadLinuxProcessStartMetadata(Environment.ProcessId) ||
            host.ProcessId <= 0 || host.AppId != "home" || host.PackageId != home.PackageId || host.OperatingSystemPrincipalId != self.OperatingSystemPrincipalId ||
            host.InstallationRevision != Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(home))) ||
            listening.InstallationRevision != host.InstallationRevision || listening.Actor.AccountId is not null || listening.Actor.OrganisationId is not null ||
            listening.Actor.ProfileId != listening.ProfileId || listening.LeaseIdentity == Guid.Empty || listening.InstalledApplicationId == Guid.Empty ||
            listening.OriginalSessionId != listening.LeaseIdentity.ToString("D") || !Path.IsPathFullyQualified(listening.PipeName))
            throw new UnauthorizedAccessException("The actual registered Root did not attest this exact selected process and its existing Home lease.");
        return new(listening, host, home, self);
    }
    public async ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
        HomeNativeSessionHostRequirement required, CancellationToken token)
    {
        var actual = await ObserveOriginalHomeAsync(token).ConfigureAwait(false);
        if (required.AppId != "home" || required.OperatingSystemApplicationId != "9to1.package:" + actual.HomeActivation.PackageId ||
            actual.HomeProcess.ProcessId != observed.ProcessId || actual.HomeProcess.OperatingSystemPrincipalId != observed.OperatingSystemPrincipalId) return null;
        var package = actual.HomeActivation;
        var descriptor = HomePackageOriginalArtifactDescriptorParser.Parse(Convert.FromBase64String(package.SignedDescriptorBase64),
            Convert.FromBase64String(package.DescriptorPayloadBase64), package.CatalogueRevision,
            new(package.PackageId, HomePackageAction.Install, "linux.root.home.observation", package.Version, null, package.CatalogueRevision)).Descriptor;
        var image = package.ActivatedFiles.Single(value => value.RelativePath == package.EntrypointRelativePath);
        if (actual.HomeProcess.ExecutableIdentity != "sha256:" + image.Sha256) return null;
        return new("home", actual.Listening.InstalledApplicationId, actual.HomeProcess.InstallationRevision, actual.HomeProcess.ExecutableIdentity,
            new HashSet<string>(descriptor.RequiredServiceIds, StringComparer.Ordinal)) {
            Roles = new HashSet<string>([HomeNativeSessionHostRequirement.RequiredRole], StringComparer.Ordinal) };
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate) { _retiring = true; return _close ??= Close(_originals.ToArray()); }
        async Task Close(Task[] all)
        {
            var failures = new List<Exception>();
            try { _lifetime.Cancel(); } catch (Exception cause) { failures.Add(cause); }
            foreach (var actual in all) try { await actual.ConfigureAwait(false); } catch (Exception cause) { failures.Add(actual.Exception ?? cause); }
            _lifetime.Dispose();
            if (failures.Count != 0) throw new AggregateException("Original Linux Root host observations retained actual failures.", failures);
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
