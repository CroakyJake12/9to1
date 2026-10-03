using System.Globalization;
using System.Runtime.Versioning;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Actual isolated hosted root lane: all three protected signed installations, the
// signed atomic helper plus original received child descriptors, and the
// product Home and independent product owner must exist. No context, endpoint,
// actor, provider response or registration is manufactured by this fixture.
[SupportedOSPlatform("linux")]
public sealed class LinuxNativePairedInstalledWidgetCaptureTests
{
    [Fact]
    public async Task ActualIndependentInstalledOwnerRegistersAndCapturesCanonicalCardWithoutHomeWrite()
    {
        await Run(async (pair, ct) =>
        {
            var definition = await WaitForActualRegistration(pair, ct);
            Assert.Equal(LinuxInstalledApplicationWidgetBackend.WidgetId, definition.Reference.WidgetId);
            Assert.Equal(LinuxInstalledApplicationWidgetBackend.DefinitionRevision, definition.Reference.DefinitionRevision);
            Assert.Equal(pair.OriginalOwner.OriginalOwnerAppId, definition.Reference.AppId);
            Assert.Empty(definition.Definition.ActionIds);
            var before = await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct);
            var first = await Read(pair, definition.Reference, ct);
            var surface = Assert.IsType<LinuxHomeOriginalWidgetSurfaceObservation>(first.Surface);
            Assert.Equal(definition.Reference, surface.Reference);
            Assert.Equal(LinuxInstalledApplicationWidgetBackend.SurfaceReference, surface.SurfaceReference);
            Assert.Contains("{Binding label}", surface.AuthoredCui, StringComparison.Ordinal);
            Assert.Equal(2, surface.Data.Count);
            Assert.False(string.IsNullOrWhiteSpace(surface.Data["label"].GetString()));
            Assert.False(string.IsNullOrWhiteSpace(surface.Data["version"].GetString()));
            var second = await Read(pair, definition.Reference, ct);
            Assert.Equal(surface.Reference, second.Surface!.Reference);
            Assert.Equal(surface.Data["label"].GetString(), second.Surface.Data["label"].GetString());
            Assert.Equal(before, await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct));
            Assert.True(await pair.OriginalOwner.IsOriginalCurrentAsync(ct));
            Assert.True(await pair.OriginalHome.IsOriginalIssuedContextCurrentAsync(pair.OriginalHome.ObserveOriginalContext(), ct));
        });
    }

    [Fact]
    public async Task ActualOriginalOwnerExitCannotAdoptPreviouslyObservedWidgetReference()
    {
        await Run(async (pair, ct) =>
        {
            var definition = await WaitForActualRegistration(pair, ct);
            Assert.NotNull((await Read(pair, definition.Reference, ct)).Surface);
            var before = await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct);
            await pair.RetireOriginalOwnerAndDrainAsync(); // Observe canceled original channel, then exact original pidfd exit.
            Assert.False(await pair.OriginalOwner.IsOriginalCurrentAsync(ct));
            // This is authentic root original-owner-current refusal BEFORE another Home read.
            // It does not independently prove remote stale-registration removal.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(pair, definition.Reference, ct));
            Assert.Equal(before, await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct));
        });
    }

    [Fact]
    public async Task ActualSameInstalledPairCapturesAfterSixtyFiveSecondsWithoutControlTraffic()
    {
        await Run(async (pair, ct) =>
        {
            var definition = await WaitForActualRegistration(pair, ct);
            var originalContext = pair.OriginalHome.ObserveOriginalContext();
            var first = await Read(pair, definition.Reference, ct);
            var originalSurface = Assert.IsType<LinuxHomeOriginalWidgetSurfaceObservation>(first.Surface);
            var before = await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct);
            // No heartbeat/current query replaces the actual idle interval. All original
            // control and widget connections must survive together.
            await Task.Delay(TimeSpan.FromSeconds(65), ct);
            Assert.False(pair.OwnerChannel.IsCompleted);
            var second = await Read(pair, definition.Reference, ct);
            var surface = Assert.IsType<LinuxHomeOriginalWidgetSurfaceObservation>(second.Surface);
            Assert.Equal(originalSurface.Reference, surface.Reference);
            Assert.Equal(originalSurface.SurfaceReference, surface.SurfaceReference);
            Assert.Equal(originalSurface.AuthoredCui, surface.AuthoredCui);
            Assert.Equal(originalSurface.Data["label"].GetString(), surface.Data["label"].GetString());
            Assert.Equal(originalSurface.Data["version"].GetString(), surface.Data["version"].GetString());
            Assert.Same(originalContext, pair.OriginalHome.ObserveOriginalContext());
            Assert.True(await pair.OriginalOwner.IsOriginalCurrentAsync(ct));
            Assert.True(await pair.OriginalHome.IsOriginalIssuedContextCurrentAsync(originalContext, ct));
            Assert.Equal(before, await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct));
        });
    }

    [Fact]
    public async Task ActualNativePairShutdownObservesOriginalHomeExitAndRetiresBothContexts()
    {
        await Run(async (pair, ct) =>
        {
            var definition = await WaitForActualRegistration(pair, ct);
            Assert.NotNull((await Read(pair, definition.Reference, ct)).Surface);
            var originalContext = pair.OriginalHome.ObserveOriginalContext();
            var originalHome = pair.OriginalHome; var originalOwner = pair.OriginalOwner;
            await pair.DisposeAsync();
            await originalHome.OriginalExitObserved.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.False(await originalHome.IsOriginalIssuedContextCurrentAsync(originalContext, ct));
            Assert.False(await originalOwner.IsOriginalCurrentAsync(ct));
            await pair.DisposeAsync(); // Observe the identical cached completed drain.
        });
    }

    [Fact]
    public async Task ActualSignedOwnerHelperDeathDrainsOriginalUidDroppedOwnerWithoutHomeWrite()
    {
        await Run(async (pair, ct) =>
        {
            var definition = await WaitForActualRegistration(pair, ct);
            Assert.NotNull((await Read(pair, definition.Reference, ct)).Surface);
            var originalHomeContext = pair.OriginalHome.ObserveOriginalContext();
            var before = await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct);
            Assert.True(await pair.OriginalOwner.IsOriginalCurrentAsync(ct));
            await pair.RetireOriginalNativeOwnerHelperAndDrainAsync();
            // This selected operation kills ONLY the actual signed original helper;
            // its received child descriptor independently observes/drains the real owner.
            Assert.False(await pair.OriginalOwner.IsOriginalCurrentAsync(ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(pair, definition.Reference, ct));
            Assert.True(await pair.OriginalHome.IsOriginalIssuedContextCurrentAsync(originalHomeContext, ct));
            Assert.Equal(before, await File.ReadAllBytesAsync(Required("ASTRA_ROOT_TEST_HOME_STATE_PATH"), ct));
            await pair.RetireOriginalNativeOwnerHelperAndDrainAsync(); // Same cached retirement, no second helper selection.
            Assert.Throws<InvalidOperationException>(() => { _ = pair.RetireOriginalOwnerAndDrainAsync(); });
        });
    }

    private static Task<LinuxHomeOriginalWidgetReply> Read(LinuxRootPairedInstalledRuntime pair,
        HomeNativeWidgetReference? reference, CancellationToken ct) => LinuxRootOriginalHomeWidgetObservation.ReadAsync(
            pair.OriginalWidgetObservationSocket, pair.OriginalHome, pair.OriginalOwner, reference, ct);
    private static async Task<HomeNativeWidgetResolution> WaitForActualRegistration(LinuxRootPairedInstalledRuntime pair, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(40));
        using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (true)
        {
            if (pair.OwnerChannel.IsCompleted) await pair.OwnerChannel; // Surface actual producer failure immediately.
            var listed = await Read(pair, null, deadline.Token);
            if (listed.Definitions.Count != 0) return Assert.Single(listed.Definitions);
            await ticks.WaitForNextTickAsync(deadline.Token);
        }
    }
    private static async Task Run(Func<LinuxRootPairedInstalledRuntime, CancellationToken, Task> action)
    {
        Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var pair = await LinuxRootPairedInstalledRuntime.StartNativeAsync(
            uint.Parse(Required("ASTRA_ROOT_TEST_UID"), CultureInfo.InvariantCulture),
            uint.Parse(Required("ASTRA_ROOT_TEST_GID"), CultureInfo.InvariantCulture),
            Required("ASTRA_ROOT_TEST_USER_HOME"), Required("ASTRA_ROOT_TEST_RUNTIME_DIRECTORY"), deadline.Token);
        Exception? primary = null;
        try { await action(pair, deadline.Token); }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { await pair.DisposeAsync(); }
            catch (Exception cleanupFailure) when (primary is not null)
            {
                throw new AggregateException("Original paired observation and exact cleanup both failed.", primary, cleanupFailure);
            }
        }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing actual isolated hosted fixture input: " + name);
}
