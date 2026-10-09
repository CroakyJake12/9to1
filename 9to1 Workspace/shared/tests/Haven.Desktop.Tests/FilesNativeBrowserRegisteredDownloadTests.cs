using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

// Genuine FileHome/OS profile/NativeWorkspace/JSONDrive metadata and actual CUI.
// Local readiness below is a controlled owner-read fixture, not an App/Home
// native-compositor handshake, installation, browser approval or Files WRITE.
public sealed class FilesNativeBrowserRegisteredDownloadTests
{
    [AvaloniaFact]
    public async Task Same_original_parent_page_and_row_are_selected_then_refresh_outlives_Browser_scope_without_writes()
        => await Run(async rig =>
        {
            var readiness = new Readiness(rig);
            var surface = rig.Create(readiness);
            var scope = new CallerScope();
            await surface.RevealOriginalRegisteredPageWithinSourceAsync(rig.Browser, rig.Page, rig.Row,
                rig.Workspace.Actor, scope.Invoke, scope.Retain, rig.Token);
            Assert.Empty(await scope.Join()); rig.Window(surface).UpdateLayout();
            var list = Assert.Single(surface.GetVisualDescendants().OfType<ListBox>());
            Assert.Same(rig.Row, list.SelectedItem);
            Assert.Contains(list.Items.Cast<HostedItemMetadata>(), row => ReferenceEquals(row, rig.Row));
            Assert.True(surface.TryGetValue("SelectedDetails", out var detail));
            Assert.Contains(rig.Row.Name, Assert.IsType<string>(detail));
            Assert.True(readiness.ScopedReads > 0);
            var originalCalls = scope.Calls; scope.Active = false;
            await surface.DispatchAsync("9to1.Files.Refresh", null, rig.Token);
            await surface.RefreshAsync(rig.Token);
            Assert.Equal(originalCalls, scope.Calls);
            Assert.True(readiness.NormalReads > 0);
            Assert.Contains(list.Items.Cast<HostedItemMetadata>(), row => row.Id == rig.Row.Id);
            Assert.Equal(rig.HomeBytes, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
            Assert.Equal(rig.DriveBytes, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
            var close = surface.CloseAndDrainAsync(); await close;
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.Null(surface.Content);
        });

    [AvaloniaFact]
    public async Task Copied_page_and_row_are_refused_and_restored_callback_cannot_join_its_pending_or_cached_surface_close()
        => await Run(async rig =>
        {
            // Row copying is refused synchronously before any view operation.
            var rowSurface = rig.Create(new Readiness(rig));
            Assert.Throws<UnauthorizedAccessException>(() =>
            {
                _ = rowSurface.RevealOriginalRegisteredPageWithinSourceAsync(rig.Browser, rig.Page,
                    rig.Row with { }, rig.Workspace.Actor, body => body(), _ => { }, rig.Token);
            });
            await rowSurface.CloseAndDrainAsync();
            // A copied page carrying the real row still lacks private page issuance.
            var pageSurface = rig.Create(new Readiness(rig));
            var foreign = pageSurface.RevealOriginalRegisteredPageWithinSourceAsync(rig.Browser,
                rig.Page with { }, rig.Row, rig.Workspace.Actor, body => body(), _ => { }, rig.Token);
            var foreignCause = await Failure(foreign);
            var foreignClose = pageSurface.CloseAndDrainAsync(); var foreignCloseCause = await Failure(foreignClose);
            Assert.True(Contains(foreignCloseCause, foreignCause) ||
                foreign.Exception!.InnerExceptions.Any(cause => Contains(foreignCloseCause, cause)));
            Assert.Same(foreignClose, pageSurface.CloseAndDrainAsync()); rig.ExpectSameFailedClose(pageSurface, foreignClose, [foreignCause]);

            var neutral = ExecutionContext.Capture()!;
            var readiness = new Readiness(rig) { HoldNext = true };
            var surface = rig.Create(readiness); var scope = new CallerScope(); var refused = 0;
            readiness.AfterRead = callback => callback(() => ExecutionContext.Run(neutral, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = surface.CloseAndDrainAsync(); });
                refused++;
            }, null));
            var reveal = surface.RevealOriginalRegisteredPageWithinSourceAsync(rig.Browser, rig.Page,
                rig.Row, rig.Workspace.Actor, scope.Invoke, scope.Retain, rig.Token);
            Task? close = null; Exception? assertion = null;
            try
            {
                await EnteredBeforeRevealCompletes(readiness.Entered.Task, reveal);
                Assert.False(reveal.IsCompleted);
                // This real external close is published while the actual readiness
                // source is held. The restored callback must refuse BEFORE returning
                // this cached close, which is still waiting on the same reveal.
                close = surface.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
            }
            catch (Exception failure) { assertion = failure; }
            finally { readiness.Release.TrySetResult(); }
            var actualRevealFailure = await Failure(reveal);
            close ??= surface.CloseAndDrainAsync(); var actualCloseFailure = await Failure(close);
            var originalReadFailures = await scope.Join();
            foreach (var failure in originalReadFailures) Assert.True(Contains(actualCloseFailure, failure));
            Assert.Equal(1, refused); Assert.True(reveal.IsCompleted); Assert.True(close.IsCompleted);
            Assert.True(Contains(actualCloseFailure, actualRevealFailure) ||
                reveal.Exception!.InnerExceptions.Any(cause => Contains(actualCloseFailure, cause)));
            Assert.Same(close, surface.CloseAndDrainAsync()); rig.ExpectSameFailedClose(surface, close, [actualRevealFailure, .. originalReadFailures]);
            Assert.Equal(rig.HomeBytes, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
            Assert.Equal(rig.DriveBytes, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
            if (assertion is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(assertion).Throw();
        });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_resumed_body_fault_survives_caller_scope_swallow_or_replacement(bool replace)
        => await Run(async rig =>
        {
            var sentinel = new IOException("Actual readiness body refused after its retained source resumed.");
            var replacement = new IOException("Caller scope substituted another synchronous cause.");
            var readiness = new Readiness(rig) { HoldNext = true };
            var surface = rig.Create(readiness); var retained = new CallerScope();
            void ForeignScope(Action body)
            {
                try { retained.Invoke(body); }
                catch (Exception actual) when (ReferenceEquals(actual, sentinel))
                { if (replace) throw replacement; } // Only the exact fixture body cause is altered.
            }
            readiness.AfterRead = invoke => invoke(() => throw sentinel);
            var reveal = surface.RevealOriginalRegisteredPageWithinSourceAsync(rig.Browser, rig.Page,
                rig.Row, rig.Workspace.Actor, ForeignScope, retained.Retain, rig.Token);
            Exception? assertion = null;
            try
            {
                await EnteredBeforeRevealCompletes(readiness.Entered.Task, reveal); Assert.False(reveal.IsCompleted);
            }
            catch (Exception failure) { assertion = failure; }
            finally { readiness.Release.TrySetResult(); }
            var actualFailure = await Failure(reveal); Assert.True(Contains(actualFailure, sentinel));
            if (replace) Assert.True(Contains(actualFailure, replacement));
            var close = surface.CloseAndDrainAsync(); var actualCloseFailure = await Failure(close);
            Assert.True(Contains(actualCloseFailure, sentinel));
            if (replace) Assert.True(Contains(actualCloseFailure, replacement));
            var originalReadFailures = await retained.Join();
            foreach (var failure in originalReadFailures) Assert.True(Contains(actualCloseFailure, failure));
            Assert.Same(close, surface.CloseAndDrainAsync()); rig.ExpectSameFailedClose(surface, close, [actualFailure, .. originalReadFailures]);
            Assert.Equal(rig.HomeBytes, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
            Assert.Equal(rig.DriveBytes, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
            if (assertion is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(assertion).Throw();
        });

    private sealed class CallerScope
    {
        internal bool Active = true;
        internal int Calls;
        private readonly List<Task> _actual = [];
        internal void Invoke(Action body)
        {
            Calls++; if (!Active) throw new InvalidOperationException("The Browser caller source has ended."); body();
        }
        internal void Retain(Task actual) { lock (_actual) _actual.Add(actual); }
        internal async Task<IReadOnlyList<Exception>> Join()
        {
            Task[] originals; lock (_actual) originals = _actual.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            var failures = new List<Exception>();
            foreach (var actual in originals)
            {
                try { await actual; }
                catch (Exception failure)
                {
                    if (actual.Exception is { } clr) failures.AddRange(clr.InnerExceptions);
                    else failures.Add(failure);
                }
            }
            return failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        }
    }
    private sealed class Readiness(Rig actual) : IFilesOriginalRegisteredRevealReadiness
    {
        internal int ScopedReads, NormalReads;
        internal bool HoldNext;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action<Action<Action>>? AfterRead;
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            NormalReads++;
            await actual.Browser.RevalidateAsync(actual.Page, actual.Workspace.Actor, token);
            return new(CuiSceneAvailabilityState.Ready, "FixtureOwnerRead", "Actual Files owner read; no platform handshake claim.");
        }
        public async ValueTask<CuiSceneAvailability> CheckRegisteredRevealWithinSourceAsync(
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ScopedReads++; Task? read = null;
            scope(() => { read = actual.Browser.RevalidateAsync(actual.Page, actual.Workspace.Actor, token); retain(read); });
            await read!;
            if (HoldNext)
            {
                HoldNext = false;
                scope(() => retain(Release.Task)); Entered.TrySetResult();
                await Release.Task; // SAME fixture-owned raw suspension, joined before teardown.
            }
            // Called after an actual awaited source. The surface must supply its
            // physical guard even when the callback restores another context.
            AfterRead?.Invoke(scope);
            return new(CuiSceneAvailabilityState.Ready, "FixtureOwnerRead", "Actual Files owner read; no platform handshake claim.");
        }
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "astra-files-registered-navigation-" + Guid.NewGuid().ToString("N"));
        internal string HomePath => Path.Combine(Root, "home.json");
        internal string Chosen => Path.Combine(Root, "chosen");
        internal string DrivePath => Path.Combine(Chosen, ".9to1-files", "drive.json");
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal ServiceProvider? Graph;
        internal NativeFilesWorkspace Workspace = null!;
        internal FilesNativeBrowserService Browser = null!;
        internal FilesNativeBrowserPage Page = null!;
        internal HostedItemMetadata Row = null!;
        internal byte[] HomeBytes = [], DriveBytes = [];
        internal readonly List<(FilesNativeBrowserSurface Surface, Window Window)> Views = [];
        internal readonly Dictionary<FilesNativeBrowserSurface, Task> ExpectedFailedCloses = new(ReferenceEqualityComparer.Instance);
        internal async Task Initialize()
        {
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Chosen);
            var home = new FileHomeCoreStateStore(HomePath);
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IHomeCoreStateStore>(home); registrations.AddSingleton(profiles);
            registrations.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            registrations.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            registrations.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            registrations.AddSingleton<HomeLocalStoreOwnership>();
            registrations.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            registrations.AddSingleton<ResourceAuthorizationService>(); registrations.AddFilesNativeHost();
            Graph = registrations.BuildServiceProvider();
            Workspace = await Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(Chosen,
                Graph.GetRequiredService<HomeLocalStoreOwnership>(), Token);
            var authority = Graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var folder = (await Workspace.Provider.GetAsync(Workspace.Configuration.AppFolders["picture"], Token)).Value!;
            var directory = (await Workspace.Directories.ResolveProfileAsync(Guid.Parse(Workspace.Actor.ProfileId), "picture", Token)).Value!.DirectoryPath;
            byte[] bytes = [1, 2, 3, 4];
            await File.WriteAllBytesAsync(Path.Combine(directory, "registered-download.txt"), bytes, Token);
            var file = HostedItemId.New(); var revision = new FilesRevisionId(Guid.NewGuid());
            var upload = new FilesUploadedContent(file, folder.Id, "registered-download.txt", "text/plain", revision,
                null, Workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "registered-download.txt");
            var authorityGuard = await authority.CaptureCommitAuthorityAsync(Workspace.Actor, Workspace.Provider, () => true, Token);
            Assert.True((await Workspace.Provider.CommitUploadedContentAsync(upload,
                [new(folder.Id, folder.CurrentRevisionId)], authorityGuard, Token)).IsSuccess);
            Browser = Graph.GetRequiredService<FilesNativeBrowserService>();
            Page = await Browser.ListAsync(Workspace.Actor, folder.Id.Value, token: Token, expectedStoreId: Workspace.Configuration.StoreId);
            Row = Page.Items.Single(item => item.Id == file);
            HomeBytes = await File.ReadAllBytesAsync(HomePath, Token); DriveBytes = await File.ReadAllBytesAsync(DrivePath, Token);
        }
        internal FilesNativeBrowserSurface Create(Readiness readiness)
        {
            var surface = new FilesNativeBrowserSurface(Browser, null, Workspace.Actor, readiness, CancellationToken.None);
            var window = new Window { Content = surface, Width = 720, Height = 480 };
            Views.Add((surface, window)); window.Show(); return surface;
        }
        internal Window Window(FilesNativeBrowserSurface surface) => Views.Single(view => ReferenceEquals(view.Surface, surface)).Window;
        internal void ExpectSameFailedClose(FilesNativeBrowserSurface surface, Task close, IReadOnlyList<Exception> independentlyObserved)
        {
            Assert.True(close.IsFaulted);
            var allowed = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            foreach (var actual in independentlyObserved) Remember(actual);
            // Unknown packaging may combine independently observed payloads, but
            // a new scene/window/cleanup cause cannot be waived by the cached Task.
            Assert.True(IsObserved(close.Exception!), "The actual cached close contained an independently unobserved cause.");
            ExpectedFailedCloses.Add(surface, close);

            void Remember(Exception actual)
            {
                if (!allowed.Add(actual)) return;
                if (actual is AggregateException group) foreach (var direct in group.InnerExceptions) Remember(direct);
            }
            bool IsObserved(Exception actual) => allowed.Contains(actual) ||
                actual is AggregateException { InnerExceptions.Count: > 0 } group && group.InnerExceptions.All(IsObserved);
        }
        internal async Task Drain(List<Exception> failures)
        {
            foreach (var view in Views)
            {
                Task? close = null;
                try { close = view.Surface.CloseAndDrainAsync(); await close; }
                catch (Exception failure)
                {
                    // Only the SAME already independently joined cached expected
                    // failure is observed again; no exception type/token waiver.
                    if (close is null || !ExpectedFailedCloses.TryGetValue(view.Surface, out var expected) || !ReferenceEquals(close, expected))
                        failures.Add(close?.Exception ?? failure);
                }
                try { view.Window.Close(); } catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count == 0 && ExpectedFailedCloses.Count == 0 && Graph is not null)
                try { await Graph.DisposeAsync(); } catch (Exception failure) { failures.Add(failure); }
            // Retain actual fixture paths and graph/failed originals. No deletion.
        }
    }
    private static readonly List<(Rig Original, Exception? Failure)> RetainedFixtures = [];
    private static async Task Run(Func<Rig, Task> test)
    {
        var actual = new Rig(); var failures = new List<Exception>();
        try { await actual.Initialize(); await test(actual); } catch (Exception failure) { failures.Add(failure); }
        await actual.Drain(failures);
        lock (RetainedFixtures) RetainedFixtures.Add((actual, failures.Count == 0 ? null : new AggregateException(failures)));
        if (failures.Count != 0) throw new AggregateException("Actual Files navigation fixture/originals retained.", failures);
    }
    private static async Task EnteredBeforeRevealCompletes(Task actualEntered, Task actualReveal)
    {
        if (ReferenceEquals(await Task.WhenAny(actualEntered, actualReveal), actualEntered))
        { await actualEntered; return; }
        await actualReveal; // Independently settle an unexpected early original.
        throw new InvalidOperationException("The actual reveal completed before its held readiness source entered.");
    }
    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; } catch (Exception failure) { return actual.Exception ?? failure; }
        throw new InvalidOperationException("The SAME original Task was expected to fail.");
    }
    private static bool Contains(Exception actual, Exception same) => ReferenceEquals(actual, same) ||
        actual is AggregateException group && group.InnerExceptions.Any(inner => Contains(inner, same));
}
