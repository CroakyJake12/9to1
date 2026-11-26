using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace Haven.Desktop.Tests;

public sealed class SpacesOriginalStoreSetupTests
{
    [Fact]
    public async Task Strict_primary_inspection_is_stable_and_does_not_seed_or_mutate_the_store()
    {
        using var root = new PrivateRoot();
        var token = TestContext.Current.CancellationToken;
        var paths = new Paths(Path.Combine(root.Path, "missing"));
        var settings = new VersionedAtomicSettingsStore(paths);
        var evidence = new SpacesLocalStoreEvidenceProvider(settings);
        Assert.Null(await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        Assert.False(Directory.Exists(paths.DataDirectory));

        await settings.SetAsync("spaces.registry", new { Name = "existing" }, token);
        var primary = Path.Combine(paths.DataDirectory, "settings.json");
        var before = await File.ReadAllBytesAsync(primary, token);
        var one = Assert.IsType<ExistingSettingsStoreMetadata>(await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        var two = Assert.IsType<ExistingSettingsStoreMetadata>(await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        Assert.Equal(one, two);
        var actual = Assert.IsType<HomeLocalStoreEvidence>(await evidence.ReadAsync(one.StoreIdentity.StoreId.ToString("D"), token));
        Assert.False(actual.NewlyCreated); Assert.False(actual.IsEmpty);
        Assert.Equal(one.Revision, actual.Revision); Assert.True(actual.AccessibleToCurrentOsPrincipal);
        Assert.Equal(before, await File.ReadAllBytesAsync(primary, token));

        var manifest = JsonSerializer.Deserialize<SettingsExportManifest>(before)!;
        await File.WriteAllTextAsync(primary, JsonSerializer.Serialize(new SettingsExportManifest
        {
            Version = manifest.Version, StoreIdentity = manifest.StoreIdentity, Settings = manifest.Settings,
            ExportedAt = "a different export presentation timestamp"
        }), token);
        Assert.Equal(one.Revision, (await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token))!.Revision);
    }

    [Fact]
    public async Task Corrupt_primary_with_valid_backup_is_refused_without_copyback_or_identity_initialisation()
    {
        using var root = new PrivateRoot(); var token = TestContext.Current.CancellationToken;
        var settings = new VersionedAtomicSettingsStore(new Paths(root.Path));
        await settings.SetAsync("spaces.registry", new { Name = "first" }, token);
        await settings.SetAsync("spaces.current", new { Name = "second" }, token);
        var primary = Path.Combine(root.Path, "settings.json");
        var backup = await File.ReadAllBytesAsync(primary + ".bak", token);
        await File.WriteAllTextAsync(primary, "{ corrupt primary", token);
        // The ordinary reader can recover this fixture. Ownership evidence must reject it.
        Assert.NotNull((await settings.ExportAsync(token)).StoreIdentity);
        var evidence = new SpacesLocalStoreEvidenceProvider(settings);
        var failure = await Record.ExceptionAsync(() => evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        Assert.Contains(Causes(failure!), cause => cause is InvalidDataException);
        Assert.Equal("{ corrupt primary", await File.ReadAllTextAsync(primary, token));
        Assert.Equal(backup, await File.ReadAllBytesAsync(primary + ".bak", token));
    }

    [Fact]
    public async Task Missing_identity_remains_unavailable_and_a_replaced_identity_cannot_reuse_the_provider()
    {
        using var root = new PrivateRoot(); var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(root.Path, "settings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new SettingsExportManifest
        { Settings = new() { ["spaces.registry"] = "{}" } }), token);
        var evidence = new SpacesLocalStoreEvidenceProvider(new VersionedAtomicSettingsStore(new Paths(root.Path)));
        var original = await File.ReadAllBytesAsync(path, token);
        Assert.Null(await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, token));

        var first = new SettingsStoreIdentity(1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await WriteFixture(first);
        Assert.Equal(first, (await evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token))!.StoreIdentity);
        await WriteFixture(first with { StoreId = Guid.NewGuid() });
        var replaced = await File.ReadAllBytesAsync(path, token);
        var failure = await Record.ExceptionAsync(() => evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, token));
        Assert.Contains(Causes(failure!), cause => cause is UnauthorizedAccessException);
        Assert.Equal(replaced, await File.ReadAllBytesAsync(path, token));

        Task WriteFixture(SettingsStoreIdentity identity) => File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(new SettingsExportManifest { StoreIdentity = identity, Settings = new() { ["spaces.registry"] = "{}" } }), token);
    }

    [AvaloniaFact]
    public async Task Native_App_factory_uses_the_registered_container_scope_and_same_store_without_inspection_effects()
    {
        using var root = new PrivateRoot(); var paths = new Paths(root.Path);
        var settings = new VersionedAtomicSettingsStore(paths);
        var evidence = new SpacesLocalStoreEvidenceProvider(settings);
        var principal = new OperatingSystemPrincipalSource();
        var home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(root.Path, "Home", "state.json")),
            principal, paths, new("9to1.home.spaces.factory-component.v1"),
            configureOriginalStores: _ => new([evidence], new Dictionary<Type, object>
            { [typeof(IVersionedSettingsStore)] = settings, [typeof(SpacesLocalStoreEvidenceProvider)] = evidence }));
        var approvals = new HomeNativeApprovalWindowOwner(home.Runtime, home.Profiles, home.Permissions);
        var collection = new ServiceCollection();
        collection.AddSingleton<IAppPaths>(paths); collection.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
        collection.AddHavenOwnedWindowsHomeDomain(home, paths, principal);
        collection.AddSingleton<IVersionedSettingsStore>(settings); collection.AddSingleton(evidence);
        collection.AddSingleton(approvals); collection.AddSingleton(new SpaceRegistry(settings));
        var app = new App();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(App).GetField("_actualWindowsHome", flags)!.SetValue(app, home);
        typeof(App).GetMethod("RegisterOriginalSpacesStoreSetup", flags)!.Invoke(app, [collection, home, settings, evidence]);
        await using var provider = collection.BuildServiceProvider();
        typeof(App).GetField("_services", flags)!.SetValue(app, provider);
        var work = Assert.IsType<DesktopOriginalWorkLifetime>(typeof(App).GetField("_originalAppWork", flags)!.GetValue(app));
        try
        {
            // The actual Microsoft DI factory receives this registered scope, not the outer container wrapper.
            Assert.NotSame(provider, provider.GetRequiredService<IServiceProvider>());
            var actual = provider.GetRequiredService<SpacesOriginalStoreSetupBackend>();
            Assert.Same(actual, provider.GetRequiredService<SpacesOriginalStoreSetupBackend>());
            Assert.True(actual.HasOriginalComposition(settings, evidence, home, approvals));
            Assert.Same(actual, typeof(App).GetField("_actualSpacesStoreSetup", flags)!.GetValue(app));
            Assert.True(provider.GetRequiredService<SpaceRegistry>().HasOriginalSettingsStore(settings));
            Assert.False(File.Exists(Path.Combine(root.Path, "settings.json")));
            Assert.False(File.Exists(Path.Combine(root.Path, "Home", "state.json")));
        }
        finally
        {
            await work.CloseAndDrainAsync();
            await approvals.CloseAndDrainAsync();
            await home.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Compiled_Home_Settings_requires_exact_review_then_completion_and_preserves_existing_Spaces()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        var settingsBefore = await File.ReadAllBytesAsync(rig.SettingsPath, rig.Token);
        var homeBefore = await File.ReadAllBytesAsync(rig.HomePath, rig.Token);
        await using var theme = rig.CreateTheme();
        var page = new HomeSettingsDestinationPage(theme, rig.AdmitAsync) { SpacesStoreSetup = rig.Backend };
        page.SetInitialRoute(AppContentAddress.Parse("browse://home/settings/settings-permissions"));
        var window = new Window { Content = page, Width = 900, Height = 760 };
        try
        {
            await page.ActivateAsync(rig.Token); window.Show(); window.UpdateLayout();
            Click(page, "home-settings-spaces-storage-inspect"); await page.WhenActionsIdleAsync();
            Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(rig.SettingsPath, rig.Token));
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
            Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));

            Click(page, "home-settings-spaces-storage-request"); await page.WhenActionsIdleAsync();
            var first = await rig.PendingAsync();
            Assert.Equal("spaces:" + rig.StoreId, Assert.Single(first.Scope.Objects).ObjectId);
            Assert.Equal(rig.Actor.ProfileId, first.Caller.Origin);
            Click(page, "home-settings-spaces-storage-review");
            await rig.DecideAsync(first.RequestId, "approval-decline"); await page.WhenActionsIdleAsync();
            Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));

            Click(page, "home-settings-spaces-storage-request"); await page.WhenActionsIdleAsync();
            var second = await rig.PendingAsync(); Assert.NotEqual(first.RequestId, second.RequestId);
            Click(page, "home-settings-spaces-storage-review");
            await rig.DecideAsync(second.RequestId, "approval-accept"); await page.WhenActionsIdleAsync();
            Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
            Click(page, "home-settings-spaces-storage-complete"); await page.WhenActionsIdleAsync();
            var receipt = Assert.IsType<VerifiedResourceStoreOwnership>(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
            Assert.Equal(rig.Actor.ProfileId, receipt.ProfileId);
            Assert.True(await rig.Home.Ownership.IsCurrentAsync(receipt, rig.Actor, rig.Token));
            Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(rig.SettingsPath, rig.Token));
            Assert.Contains("verified", Find<TextBlock>(page, "home-settings-spaces-storage-status").Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Same_version_payload_change_after_approval_requires_a_fresh_review_and_stale_snapshot_cannot_complete_it()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        var approvedA = await rig.ApproveAsync();
        var before = (await rig.Evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, rig.Token))!;
        var manifest = JsonSerializer.Deserialize<SettingsExportManifest>(await File.ReadAllTextAsync(rig.SettingsPath, rig.Token))!;
        manifest.Settings["fixture.changed-without-version"] = "\"changed\"";
        await File.WriteAllTextAsync(rig.SettingsPath, JsonSerializer.Serialize(manifest), rig.Token);
        var after = (await rig.Evidence.InspectWithinOriginalSourceAsync(body => body(), _ => { }, rig.Token))!;
        Assert.Equal(before.StoreIdentity, after.StoreIdentity); Assert.NotEqual(before.Revision, after.Revision);
        var refused = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.CompleteImport, approvedA);
        Assert.False(refused.IsOwned); Assert.True(refused.CanRequestImport);
        Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
        var approvedB = await rig.ApproveAsync(refused);
        Assert.NotEqual(approvedA.Import!.RequestId, approvedB.Import!.RequestId);
        var stale = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.CompleteImport, approvedA);
        Assert.Null(stale.Import); Assert.Null(stale.Metadata);
        Assert.False(stale.CanCompleteImport); Assert.False(stale.CanRequestImport); Assert.False(stale.CanOpenReview);
        Assert.Contains("Inspect", stale.Message, StringComparison.Ordinal);
        Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
        // The stale A click neither consumes B nor replaces its current observation.
        var completed = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.CompleteImport, approvedB);
        Assert.True(completed.IsOwned);
        await rig.Backend.CloseAndDrainAsync();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_audit_acknowledgement_loss_retains_exact_retry_and_never_reimports(bool recover)
    {
        using var root = new PrivateRoot();
        var observer = new AuditObserver(Path.Combine(root.Path, "Home", "state.json"));
        await using var rig = await Rig.CreateAsync(root.Path, observer: observer);
        var approved = await rig.ApproveAsync();
        observer.Armed = true;
        var pending = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.CompleteImport, approved);
        Assert.True(pending.CanRetryAudit); Assert.False(pending.CanCompleteImport); Assert.False(pending.CanRequestImport);
        Assert.NotNull(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
        var bindingWrites = observer.BindingWrites;
        Assert.Equal(1, observer.Failures);
        if (recover)
        {
            var imported = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.RetryAudit, pending);
            Assert.True(imported.IsOwned); Assert.False(imported.CanRetryAudit);
            Assert.Equal(bindingWrites, observer.BindingWrites);
            await rig.Backend.CloseAndDrainAsync();
        }
        else await rig.ExpectBackendCloseFailureAsync(observer.Cause);
    }

    [AvaloniaFact]
    public async Task Audit_loss_with_independent_cleanup_callback_failure_is_not_qualified_for_retry_or_replay()
    {
        using var root = new PrivateRoot();
        var observer = new AuditObserver(Path.Combine(root.Path, "Home", "state.json"));
        var rig = await Rig.CreateAsync(root.Path, observer: observer);
        var failures = new List<Exception>();
        try
        {
            var approved = await rig.ApproveAsync();
            var independent = new IOException("Independent original cleanup callback failure after the actual audit fault.");
            var injected = 0; observer.Armed = true;
            var failure = await Record.ExceptionAsync(() => rig.Backend.ExecuteWithinOriginalSourceAsync(
                SpacesOriginalStoreSetupOperation.CompleteImport, approved, body =>
                {
                    body();
                    if (observer.Failures == 1 && Interlocked.CompareExchange(ref injected, 1, 0) == 0)
                        throw independent;
                }, _ => { }, rig.Token));
            Assert.NotNull(failure); Assert.Equal(1, injected);
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, observer.Cause));
            Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, independent));
            var observed = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.Inspect);
            Assert.False(observed.CanRetryAudit); Assert.False(observed.CanCompleteImport); Assert.False(observed.CanRequestImport);
            Assert.Equal(HomeOriginalLocalStoreImportState.OutcomeUnconfirmed, observed.Import!.State);
            var bindingWrites = observer.BindingWrites;
            var staleAttempt = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.CompleteImport, approved);
            Assert.Null(staleAttempt.Import);
            Assert.Equal(bindingWrites, observer.BindingWrites);
            await rig.ExpectBackendCloseFailureAsync(observer.Cause, independent);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            try { await rig.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("The actual audit/callback assertion and independent cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Foreign_settings_owner_or_unissued_snapshot_cannot_request_import()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        var homeBefore = await File.ReadAllBytesAsync(rig.HomePath, rig.Token);
        Assert.Throws<UnauthorizedAccessException>(() => new SpacesOriginalStoreSetupBackend(
            new VersionedAtomicSettingsStore(new Paths(root.Path)), rig.Evidence, rig.Home, rig.Approvals, rig.Token));
        Assert.Throws<UnauthorizedAccessException>(() => new SpacesOriginalStoreSetupBackend(
            rig.Settings, new SpacesLocalStoreEvidenceProvider(rig.Settings), rig.Home, rig.Approvals, rig.Token));
        var observed = await rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.Inspect);
        var copied = new SpacesOriginalStoreSetupBackend.Snapshot(observed.Metadata, observed.Actor, observed.Import);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.ExecuteAsync(SpacesOriginalStoreSetupOperation.RequestImport, copied));
        Assert.Equal(homeBefore, await File.ReadAllBytesAsync(rig.HomePath, rig.Token));
        Assert.Empty((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).PendingRequests);
    }

    [AvaloniaFact]
    public async Task Independent_original_callback_failure_is_retained_by_backend_retirement()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        var cause = new IOException("Independent actual caller scope failure.");
        var failure = await Record.ExceptionAsync(() => rig.Backend.ExecuteWithinOriginalSourceAsync(
            SpacesOriginalStoreSetupOperation.Inspect, null, body => { body(); throw cause; }, _ => { }, rig.Token));
        Assert.Contains(Causes(failure!), value => ReferenceEquals(value, cause));
        await rig.ExpectBackendCloseFailureAsync(cause);
    }

    [AvaloniaFact]
    public async Task Reactivated_Settings_recovers_controls_after_held_original_admission_settles()
    {
        using var root = new PrivateRoot(); var principal = new HeldPrincipal();
        await using var rig = await Rig.CreateAsync(root.Path, principal: principal);
        await using var theme = rig.CreateTheme();
        var page = new HomeSettingsDestinationPage(theme, rig.AdmitAsync) { SpacesStoreSetup = rig.Backend };
        page.SetInitialRoute(AppContentAddress.Parse("browse://home/settings/settings-permissions"));
        var window = new Window { Content = page, Width = 900, Height = 760 };
        try
        {
            await page.ActivateAsync(rig.Token); window.Show(); window.UpdateLayout();
            principal.Arm();
            var action = page.DispatchAsync("SettingsSpacesStoreInspect", null, rig.Token).AsTask();
            await principal.Entered.Task.WaitAsync(rig.Token);
            page.Deactivate(); await page.ActivateAsync(rig.Token);
            Assert.False(Find<Button>(page, "home-settings-spaces-storage-inspect").IsEnabled);
            principal.Release.TrySetResult();
            await action;
            Assert.True(Find<Button>(page, "home-settings-spaces-storage-inspect").IsEnabled);
            Assert.Empty((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).PendingRequests);
            Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("spaces", rig.StoreId, rig.Token));
        }
        finally { principal.Release.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var cause in Causes(child)) yield return cause;
        else if (error.InnerException is { } child)
            foreach (var cause in Causes(child)) yield return cause;
    }
    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(root.GetVisualDescendants().OfType<Control>().Prepend(root).Single(control => control.Name == name));
    private static void Click(Control root, string name)
    {
        var button = Find<Button>(root, name);
        Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private sealed class PrivateRoot : IDisposable
    {
        internal string Path { get; } = Directory.CreateTempSubdirectory("9to1-spaces-owned-store-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(root, "Attachments");
        public string LogsDirectory => Path.Combine(root, "Logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class HeldPrincipal : ITrustedHostPrincipalSource
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Interlocked.Exchange(ref _armed, 1);
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var actual = await new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
            if (Interlocked.Exchange(ref _armed, 0) != 0)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return actual;
        }
    }
    private sealed class AuditObserver(string path) : IHomePersistedWriteObserver
    {
        internal bool Armed;
        internal int Failures, BindingWrites;
        internal readonly IOException Cause = new("Actual Home audit acknowledgement lost after persistence.");
        public async Task OnPersistedWriteAsync(string recordId, long actualRecordRevision, CancellationToken token)
        {
            if (recordId.StartsWith("home.local-store:", StringComparison.Ordinal)) BindingWrites++;
            if (!Armed) return;
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
            var record = json.RootElement.GetProperty("Records").EnumerateArray()
                .Single(item => item.GetProperty("RecordId").GetString() == recordId);
            if (record.GetProperty("RecordType").GetString() == "home.permissions-trust" &&
                record.GetProperty("Payload").GetRawText().Contains("HOME_STORE_IMPORTED", StringComparison.Ordinal))
            { Armed = false; Failures++; throw Cause; }
        }
    }
    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private Exception? _expectedBackendClose;
        internal CancellationToken Token => _lifetime.Token;
        internal HomeNativeWindowsComposition Home { get; }
        internal HomeNativeApprovalWindowOwner Approvals { get; }
        internal VersionedAtomicSettingsStore Settings { get; }
        internal SpacesLocalStoreEvidenceProvider Evidence { get; }
        internal SpacesOriginalStoreSetupBackend Backend { get; }
        internal AuthenticatedResourceActor Actor { get; private set; } = null!;
        internal string StoreId { get; private set; } = null!;
        internal string SettingsPath { get; }
        internal string HomePath { get; }
        private readonly string _root;
        private Rig(string root, ITrustedHostPrincipalSource? principal, AuditObserver? observer)
        {
            _root = root; _lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            _lifetime.CancelAfter(TimeSpan.FromSeconds(40));
            var paths = new Paths(root); SettingsPath = Path.Combine(root, "settings.json");
            HomePath = Path.Combine(root, "Home", "state.json");
            Settings = new(paths); Evidence = new(Settings);
            var homeState = observer is null ? new FileHomeCoreStateStore(HomePath) : new FileHomeCoreStateStore(HomePath, observer);
            Home = new(homeState, principal ?? new OperatingSystemPrincipalSource(), paths, new("9to1.home.spaces.setup-component.v1"),
                configureOriginalStores: _ => new([Evidence], new Dictionary<Type, object>
                { [typeof(IVersionedSettingsStore)] = Settings, [typeof(SpacesLocalStoreEvidenceProvider)] = Evidence }));
            Approvals = new(Home.Runtime, Home.Profiles, Home.Permissions);
            Backend = new(Settings, Evidence, Home, Approvals, Token);
        }
        internal static async Task<Rig> CreateAsync(string root, ITrustedHostPrincipalSource? principal = null, AuditObserver? observer = null)
        {
            var rig = new Rig(root, principal, observer);
            await new SpaceRegistry(rig.Settings).GetAllAsync(cancellationToken: rig.Token);
            await rig.Home.StartOriginalAsync();
            rig.Actor = await rig.Home.Profiles.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("Actual local profile absent.");
            rig.StoreId = (await rig.Settings.ExportAsync(rig.Token)).StoreIdentity!.StoreId.ToString("D");
            return rig;
        }
        internal Task<SpacesOriginalStoreSetupBackend.Snapshot> ExecuteAsync(SpacesOriginalStoreSetupOperation operation,
            SpacesOriginalStoreSetupBackend.Snapshot? expected = null) =>
            Backend.ExecuteWithinOriginalSourceAsync(operation, expected, body => body(), _ => { }, Token);
        internal async Task AdmitAsync(CancellationToken token)
        {
            if (await Home.Profiles.GetCurrentAsync(token) != Actor) throw new UnauthorizedAccessException("The actual Home page profile changed.");
        }
        internal NativeHomeThemePreferences CreateTheme() => new(Path.Combine(_root, "preferences.json"),
            new(HomeDataScope.DeviceLocal, Actor.ProfileId), UserPreferencesService.ProjectLegacyHomeTheme,
            async token => await Home.Profiles.GetCurrentAsync(token) == Actor, body => Dispatcher.UIThread.Post(body));
        internal async Task<HomePermissionRequest> PendingAsync()
        {
            while (true)
            {
                var snapshot = await Home.Permissions.GetSnapshotAsync(cancellationToken: Token);
                var request = snapshot.PendingRequests.SingleOrDefault(row => row.Scope.ActionName == "home.profile.importStore");
                if (request is not null) return request;
                await Task.Delay(10, Token);
            }
        }
        internal async Task<SpacesOriginalStoreSetupBackend.Snapshot> ApproveAsync(SpacesOriginalStoreSetupBackend.Snapshot? inspected = null)
        {
            inspected ??= await ExecuteAsync(SpacesOriginalStoreSetupOperation.Inspect);
            var pending = await ExecuteAsync(SpacesOriginalStoreSetupOperation.RequestImport, inspected);
            var request = await PendingAsync();
            var review = ExecuteAsync(SpacesOriginalStoreSetupOperation.OpenReview, pending);
            await DecideAsync(request.RequestId, "approval-accept");
            var approved = await review; Assert.True(approved.CanCompleteImport); return approved;
        }
        internal async Task DecideAsync(string requestId, string button)
        {
            while (true)
            {
                Token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() => Approvals.OriginalWindow?.UpdateLayout(), DispatcherPriority.Background, Token);
                if (Approvals.OriginalWindow is not { IsVisible: true } window || Approvals.OriginalSurface is not { } surface ||
                    !ReferenceEquals(window.Content, surface)) continue;
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var displayed = typeof(HomeApprovalCuiSurface).GetProperty("CurrentRequest", flags)!.GetValue(surface) as HomePermissionRequest;
                if (displayed?.RequestId != requestId) continue;
                var waits = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(HomeApprovalCuiSurface).GetField("_decisionWaits", flags)!.GetValue(surface));
                var originals = new List<Task<HomeNativeApprovalDecisionObservation>>();
                foreach (var wait in waits)
                {
                    var type = wait.GetType();
                    if (type.GetProperty("RequestId", flags)!.GetValue(wait) as string != requestId) continue;
                    var raw = Assert.IsAssignableFrom<Task<HomeNativeApprovalDecisionObservation>>(type.GetField("Original", flags)!.GetValue(wait));
                    if (!raw.IsCompleted) originals.Add(raw);
                }
                if (originals.Count == 0) continue;
                Click(window, button);
                foreach (var observed in await Task.WhenAll(originals).WaitAsync(Token))
                { Assert.Equal(requestId, observed.RequestId); Assert.True(observed.DecisionRecorded); }
                return;
            }
        }
        internal async Task ExpectBackendCloseFailureAsync(params Exception[] causes)
        {
            _expectedBackendClose = await Record.ExceptionAsync(Backend.CloseAndDrainAsync);
            Assert.NotNull(_expectedBackendClose);
            foreach (var cause in causes)
                Assert.Contains(Causes(_expectedBackendClose), actual => ReferenceEquals(actual, cause));
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            try { await Backend.CloseAndDrainAsync(); }
            catch (Exception error) when (ReferenceEquals(error, _expectedBackendClose)) { }
            catch (Exception error) { failures.Add(error); }
            try { await Approvals.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            try { await Home.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            _lifetime.Dispose();
            if (failures.Count != 0) throw new AggregateException("The actual Spaces setup fixture did not drain cleanly.", failures);
        }
    }
}
