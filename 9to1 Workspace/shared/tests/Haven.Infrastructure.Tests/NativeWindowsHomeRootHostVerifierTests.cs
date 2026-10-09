using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual platform absence and original owner lifetime controls; no
/// fabricated signer, package, process, Root response, Home lease or Ready proof.</summary>
public sealed class NativeWindowsHomeRootHostVerifierTests
{
    [NonWindowsFact]
    public async Task Endpoint_and_connected_peer_observations_refuse_missing_Windows_evidence_and_join_the_same_owner()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var file = Path.Combine(Path.GetTempPath(), "root-host-uncreated-" + Guid.NewGuid().ToString("N") + ".json");
        var actual = new NativeWindowsHomeRootHostVerifier(file, lifetime.Token);
        var endpoint = actual.ObserveOriginalEndpointAsync(lifetime.Token);
        Task<HomeNativeInstalledPeer?>? peer = null; Task? close = null; Exception? body = null;
        try
        {
            Assert.Null(await endpoint);
            peer = actual.VerifyHostAsync(new(Environment.ProcessId, "unsupported-original-principal"),
                new("home", "9to1.package:unsupported-original-package"), lifetime.Token).AsTask();
            Assert.Null(await peer);
            Assert.False(File.Exists(file));
            actual.RequestOriginalRetirement();
            close = actual.CloseAndDrainOriginalAsync(); await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.Same(close, actual.OriginalClose); Assert.Same(close, actual.CloseAndDrainOriginalAsync());
            Assert.Throws<ObjectDisposedException>(() => { _ = actual.ObserveOriginalEndpointAsync(lifetime.Token); });
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            try { close ??= actual.CloseAndDrainOriginalAsync(); }
            catch (Exception cause) { body = body is null ? cause : new AggregateException(body, cause); }
        }
        var errors = new List<Exception>(); if (body is not null) errors.Add(body);
        // Each encompassing actual source and the SAME cached close joins
        // independently. An assertion failure never skips a cleanup sibling.
        foreach (var raw in new Task?[] { endpoint, peer, close })
            if (raw is not null)
                try { await raw; } catch (Exception cause) { errors.Add(raw.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual host observation fixture/source/close failed.", errors);
    }

    [Fact]
    public void Noncancelable_lifetime_cannot_create_a_host_observation_owner()
    {
        Assert.Throws<ArgumentException>(() => new NativeWindowsHomeRootHostVerifier(
            Path.Combine(Path.GetTempPath(), "root-host-no-owner.json"), CancellationToken.None));
    }
    private sealed class NonWindowsFactAttribute : FactAttribute
    { public NonWindowsFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "This absence control supplies no installed Windows/Root proof."; } }
}
