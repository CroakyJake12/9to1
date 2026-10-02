using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;
using Haven.Infrastructure;
using Haven.Desktop.Views.Pages.Shelf;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace Haven.Desktop.Tests;
public sealed class ShelfNativeWorkspaceTests
{
    [AvaloniaFact]
    public async Task Real_rendered_target_actions_require_Home_then_reload_canonical_identity_without_Finish_replay()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        string? opened = null;
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor,
            id => { opened = id; return Task.CompletedTask; }, action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); var before = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review web target"); var review = Assert.Single(host.Reviews);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            await Click(host, "Review in Home"); Assert.Equal(review.RequestID, opened);
            await Click(host, "Finish audit"); Assert.Equal("NoAttemptedOutcome", Assert.Single(host.Reviews).LastObservation!.Code);
            await Click(host, "Apply approved request or recover outcome");
            Assert.Equal("ApprovalRequired", Assert.Single(host.Reviews).LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome");
            Assert.True(Assert.Single(host.Reviews).LastObservation!.Committed);
            var committed = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Reload library");
            var item = Assert.Single((await f.Library.ReadAsync()).Library.Items);
            Assert.Equal("https://example.test/reference", item.Target.Uri);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains(item.Id.ToString("D")) == true);
            await Click(host, "Finish audit"); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
            host.Dispose();
            Assert.True((await host.FinishRetainedAsync(review.RequestID)).Committed);
            await host.OpenRetainedHomeReviewAsync(review.RequestID); Assert.Equal(review.RequestID, opened);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Real_rendered_pending_request_survives_tab_close_for_exact_Home_decline_without_owner_write()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); await Click(host, "Review web target");
            var id = Assert.Single(host.Reviews).RequestID;
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var retained = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Apply approved request or recover outcome"));
            host.Dispose(); retained.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await host.WhenActionsIdleAsync();
            Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Decline)).Succeeded);
            Assert.Equal(id, Assert.Single(host.Reviews).RequestID);
            Assert.Equal("NoAttemptedOutcome", (await host.FinishRetainedAsync(id)).Code);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.FinishRetainedAsync(Guid.NewGuid().ToString("N")));
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Empty((await f.Library.ReadAsync()).Library.Items);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_tab_close_during_original_identity_or_final_publication_denies_without_physical_write(bool finalPublication)
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host);
            if (finalPublication)
            {
                await Click(host, "Review web target");
                var id = Assert.Single(host.Reviews).RequestID;
                Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
                f.Held.HoldPublication = true;
            }
            else f.Held.HoldIdentity = true;
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var home = await File.ReadAllBytesAsync(f.HomeFile);
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content,
                finalPublication ? "Apply approved request or recover outcome" : "Review web target"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var accepted = host.WhenActionsIdleAsync();
            try { await f.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); host.Dispose(); }
            catch
            {
                f.Held.Release.TrySetResult();
                try { await accepted; } catch { } // Preserve the original entry/assertion failure after observing the action.
                throw;
            }
            finally { f.Held.Release.TrySetResult(); }
            await accepted;
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Empty((await f.Library.ReadAsync()).Library.Items);
            if (!finalPublication)
            {
                Assert.Empty(host.Reviews);
                Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
                Assert.Equal(0, f.Held.GuardedWriteCalls);
            }
            else
            {
                Assert.Equal(1, f.Held.GuardedWriteCalls);
                Assert.False(Assert.Single(host.Reviews).LastObservation?.Committed == true);
            }
        }
        finally { f.Held.Release.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task Old_rendered_display_refuses_independently_Home_approved_replacement_root_without_new_pending_or_foreign_write()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host);
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(f.SettingsFile))!.AsObject();
            var foreignID = Guid.NewGuid();
            envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = foreignID;
            var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await File.WriteAllBytesAsync(f.SettingsFile, foreign);
            // Actual repository refuses the first observed substitution; do not pretend automatic adoption.
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Held.GetStoreIdentityAsync(default).AsTask());
            Assert.Equal(foreign, await File.ReadAllBytesAsync(f.SettingsFile));
            var import = await f.Ownership.RequestImportAsync(f.Actor, "shelf", foreignID.ToString("D"), "actual-native-foreign-root");
            Assert.Equal(HomePermissionRequestState.PendingApproval, import.State);
            Assert.True((await f.Permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            await f.Ownership.CompleteImportAsync(import.RequestId);
            Assert.NotNull(await f.Ownership.GetVerifiedAsync("shelf", foreignID.ToString("D")));
            // Genuine newly loaded private context is now admitted; stale displayed origin must independently deny.
            var current = await f.Owner.LoadForDisplayAsync(f.Actor); current.Selection.Dispose();
            var home = await File.ReadAllBytesAsync(f.HomeFile);
            await Click(host, "Review web target"); await Click(host, "Reload library");
            Assert.Empty(host.Reviews);
            Assert.Equal(foreign, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
            Assert.Equal(0, f.Held.GuardedWriteCalls);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Actual_delayed_UI_publication_after_close_preserves_pending_owner_handle_without_late_presentation()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            async action => { entered.TrySetResult(); await release.Task; action(); });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Review web target"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var accepted = host.WhenActionsIdleAsync();
            string id;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); id = Assert.Single(host.Reviews).RequestID;
                Assert.Equal("Ready", host.PresentationStatus); Assert.Equal("", host.PresentedRequestID);
                host.Dispose();
            }
            catch
            {
                release.TrySetResult();
                try { await accepted; } catch { } // Preserve the original failure before fixture deletion.
                throw;
            }
            finally { release.TrySetResult(); }
            await accepted;
            Assert.Equal("Ready", host.PresentationStatus); Assert.Equal("", host.PresentedRequestID);
            Assert.Equal(id, Assert.Single(host.Reviews).RequestID);
            Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Decline)).Succeeded);
            Assert.Equal("NoAttemptedOutcome", (await host.FinishRetainedAsync(id)).Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { release.TrySetResult(); window.Close(); }
    }
    private static async Task Fill(ShelfNativeWorkspaceHost host)
    {
        var inputs = host.GetVisualDescendants().OfType<TextBox>().ToArray(); Assert.Equal(3, inputs.Length);
        inputs[0].Text = "Reference site"; inputs[1].Text = "https://example.test/reference";
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
    }
    private static async Task Click(ShelfNativeWorkspaceHost host, string content)
    {
        var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, content));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await host.WhenActionsIdleAsync();
    }
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeShelfLibraryOwner Owner { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public ShelfLibraryService Library { get; }
        private readonly VersionedAtomicSettingsStore _settings;
        public HeldSettings Held { get; }
        public HomeLocalStoreOwnership Ownership { get; private set; } = null!;
        private readonly FileHomeCoreStateStore _home;
        private readonly HomeLocalProfileIdentity _profiles;
        public Fixture()
        {
            _settings = new(Paths); Held = new(_settings); Library = new(Held);
            _home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            _profiles = new(_home, new OperatingSystemPrincipalSource());

        }
        public async Task InitializeAsync()
        {
            Actor = await _profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home actor required.");
            // Actual production policy owner supplies the maintained individual store-import policy.
            // The same authentic OS-profile caller/Home store and owning Shelf policy are composed;
            // no graph or invocation operation is dispatched by this native ownership fixture.
            var graphDatabase = new SqliteDatabase(Paths);
            var policyOwner = new HomeAppAiServices(new ModelProviderRegistry([]), _home,
                new HomePermissionCallerIdentity(Actor.ActorId, "9to1 native Home host", "os-bound-local-profile", Actor.AuthenticationRevision, true),
                new ExecutionEventRepository(graphDatabase),
                new HomeInvocationCatalogue(new ModeRegistry(graphDatabase), new ExtensionRepository(graphDatabase)),
                [new ShelfOwnedLibraryActionPolicies()]);
            Permissions = policyOwner.Permissions;
            var identity = await _settings.GetStoreIdentityAsync(default);
            var ownership = new HomeLocalStoreOwnership(_home, _profiles,
                new HomeLocalStoreEvidenceRegistry([new ShelfOwnedLibraryEvidence(Held)]), Permissions);
            Ownership = ownership;
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, _profiles);
            var resolver = new ShelfOwnedLibraryAccessResolver(Library, _profiles, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [resolver]), Permissions);
            Owner = new(Library, _profiles, authority, broker, Permissions,
                new HomeOwnedLibraryCommitFenceSource(_home, _profiles, authority, broker));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Owner.LoadForDisplayAsync(Actor));
            await ownership.BindNewEmptyAsync("shelf", identity.StoreId.ToString("D"));
        }
        public void Dispose() { try { Directory.Delete(Paths.RootDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool LoseWriteReturnOnce { get; set; }
        public bool HideAfterLostReturn { get; set; }
        public bool HideLibraryReads { get; set; }
        public int GuardedWriteCalls { get; private set; }
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            HideLibraryReads && (key == "shelf.library.v1" || key == "maps.journeys.v1")
                ? Task.FromException<T?>(new IOException("Actual library observation temporarily unavailable after lost return."))
                : actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
            => actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            GuardedWriteCalls++;
            var result = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), token);
            if (result.Exchanged && LoseWriteReturnOnce)
            {
                LoseWriteReturnOnce = false; HideLibraryReads = HideAfterLostReturn;
                throw new IOException("Decorator loses return AFTER actual inner atomic physical publication.");
            }
            return result;
        }
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return await actual.CheckAsync(context, token);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-shelf-workspace-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => RootDirectory;
        public string DatabasePath => Path.Combine(RootDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(RootDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(RootDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(RootDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
    }
}
