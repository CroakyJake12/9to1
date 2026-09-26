using System.IO.Compression;

using Haven.Application;
using Haven.Infrastructure;
using Haven.Infrastructure.WindowsCompatibility;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class ApkLaunchServiceTests
{
    [Fact]
    public async Task AddHavenApkLaunch_RegistersFailClosedServiceWithoutRuntimeProvider()
    {
        var services = new ServiceCollection();
        services.AddHavenApkLaunch();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApkLaunchService>();
        var capability = await service.GetCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal("none", capability.RuntimeId);
        Assert.Contains("No APK runtime provider", capability.UnavailableReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddHavenApkLaunch_UsesHostRegisteredRuntimeProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApkRuntimeProvider>(new RecordingProvider(available: true));
        services.AddHavenApkLaunch();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApkLaunchService>();
        var capability = await service.GetCapabilityAsync(CancellationToken.None);

        Assert.True(capability.IsAvailable);
        Assert.Equal("test-runtime", capability.RuntimeId);
    }

    [Fact]
    public async Task GetCapabilityAsync_FailsClosed_WhenNoRuntimeProviderIsRegistered()
    {
        var service = new ApkLaunchService();

        var capability = await service.GetCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Equal("none", capability.RuntimeId);
        Assert.Contains("No APK runtime provider", capability.UnavailableReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchAsync_RejectsNonApkBeforeRuntimeProbe()
    {
        var provider = new RecordingProvider(available: true);
        var service = new ApkLaunchService([provider]);
        var path = Path.Combine(Path.GetTempPath(), "haven-apk-launch-test.txt");

        var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ApkLaunchStatus.InvalidRequest, result.Status);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.LaunchCount);
    }

    [Fact]
    public async Task LaunchAsync_DoesNotDispatch_WhenRuntimeIsUnavailable()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: false, unavailableReason: "Runtime is not installed.");
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.RuntimeUnavailable, result.Status);
            Assert.Equal(1, provider.ProbeCount);
            Assert.Equal(0, provider.LaunchCount);
            Assert.Contains("not installed", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LaunchAsync_RejectsArchiveWithoutAndroidManifestBeforeRuntimeProbe()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haven-not-apk-{Guid.NewGuid():N}.apk");
        using (var file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            archive.CreateEntry("notes.txt");

        try
        {
            var runtime = new RecordingProvider(available: true);
            var service = new ApkLaunchService([runtime]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.InvalidRequest, result.Status);
            Assert.Equal(0, runtime.ProbeCount);
            Assert.Equal(0, runtime.LaunchCount);
            Assert.Contains("AndroidManifest.xml", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LaunchAsync_DelegatesCanonicalExistingApk_WhenRuntimeIsAvailable()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: true);
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.Launched, result.Status);
            Assert.Equal("test-runtime", result.RuntimeId);
            Assert.Equal(1, provider.ProbeCount);
            Assert.Equal(1, provider.LaunchCount);
            Assert.Equal(Path.GetFullPath(path), provider.LastRequest?.ApkPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LaunchAsync_ConvertsProviderExceptionToObservedFailure()
    {
        var path = CreateTemporaryApk();
        try
        {
            var provider = new RecordingProvider(available: true, throwOnLaunch: true);
            var service = new ApkLaunchService([provider]);

            var result = await service.LaunchAsync(new ApkLaunchRequest(path), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(ApkLaunchStatus.LaunchFailed, result.Status);
            Assert.Equal("test-runtime", result.RuntimeId);
            Assert.Contains(nameof(InvalidOperationException), result.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryApk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"haven-apk-launch-{Guid.NewGuid():N}.apk");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using var manifest = new StreamWriter(archive.CreateEntry("AndroidManifest.xml").Open());
        manifest.Write("<manifest package=\"test.runtime\" />");
        return path;
    }

    private sealed class RecordingProvider(
        bool available,
        string? unavailableReason = null,
        bool throwOnLaunch = false) : IApkRuntimeProvider
    {
        public int ProbeCount { get; private set; }
        public int LaunchCount { get; private set; }
        public ApkLaunchRequest? LastRequest { get; private set; }

        public Task<ApkRuntimeCapability> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            return Task.FromResult(new ApkRuntimeCapability(
                available,
                "test-runtime",
                "Test APK Runtime",
                available ? null : unavailableReason ?? "Unavailable for test."));
        }

        public Task<ApkLaunchResult> LaunchAsync(ApkLaunchRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCount++;
            LastRequest = request;

            if (throwOnLaunch)
                throw new InvalidOperationException("Synthetic runtime failure.");

            return Task.FromResult(ApkLaunchResult.Success("Observed test launch.", "test-runtime"));
        }
    }
}
