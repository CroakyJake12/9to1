#if !ANDROID
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.MiniComputer;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Apps.MiniComputer;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    public static bool UsesLinuxMiniComputerFixture => OperatingSystem.IsLinux();
    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Rendered_Mini_catalogue_import_selects_and_reopens_the_same_VM_without_provider_or_catalogue_mutation() =>
        WithActualMiniFixture(true, async fixture =>
        {
            AssistantIdentity? identity = null; Guid conversation = default;
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
                Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional Mini helper"));
                await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
                identity = workspace.Snapshot.SelectedAssistant!.Identity;
                await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
                await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
                conversation = workspace.Snapshot.ConversationBinding!.Conversation.Id;
                await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
                Assert.Empty(MiniRows(surface));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), row => row.Text == fixture.Vm.Name);
                await ClickActualMini(window, surface, "Request this catalogue in Home", () => MiniValue(surface, "HasMiniImportRequest") is true);
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniImportRequest"));
                var request = Assert.Single((await fixture.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                    item => item.RequestId == requestId);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.Empty(MiniRows(surface));
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.True((await fixture.Rig.Permissions.DecideAsync(requestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                await ClickActualMini(window, surface, "Refresh Home decision", () => MiniValue(surface, "CanMiniImportComplete") is true);
                await ClickActualMini(window, surface, "Complete approved catalogue import", () => MiniRows(surface).Length == 1 && MiniValue(surface, "CanMiniRefresh") is true);
                var row = Assert.Single(MiniRows(surface));
                Assert.Equal(fixture.Vm.VMID.Value, row.Original.VirtualMachineId);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == fixture.Vm.Name);
                var foreign = new AssistantsMiniComputerCuiBindings.VirtualMachineRow(row.Original);
                await Assert.ThrowsAsync<InvalidOperationException>(() => surface.MiniComputerBindings.DispatchAsync("assistants.mini.choose", foreign, Token).AsTask());
                await ClickActualMini(window, surface, "Use this VM for this Assistant", () =>
                    workspace.Snapshot.SelectedAssistant?.Configuration.MiniComputerId == fixture.Vm.VMID.Value.ToString("D") &&
                    MiniValue(surface, "CanMiniInspect") is true);
                Assert.Equal(identity, workspace.Snapshot.SelectedAssistant!.Identity);
                Assert.Equal(conversation, workspace.Snapshot.ConversationBinding!.Conversation.Id);
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.Null(fixture.Provider.OriginalClose); // No provider operation is performed by import or choosing a preference.
            });
            await fixture.Reopen();
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await OpenActualSavedMemoryConversation(surface, identity!, conversation, window);
                await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
                Assert.Equal(fixture.Vm.VMID.Value.ToString("D"), workspace.Snapshot.SelectedAssistant!.Configuration.MiniComputerId);
                Assert.Equal(fixture.Vm.VMID.Value, Assert.Single(MiniRows(surface)).Original.VirtualMachineId);
                Assert.False(Assert.IsType<bool>(MiniValue(surface, "CanMiniImportRequest")));
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
            });
        });

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Mini_catalogue_without_owning_identity_stays_unavailable_without_read_seed_or_Home_request() =>
        WithActualMiniFixture(false, async fixture =>
        {
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
                Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional missing identity helper"));
                await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
                await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
                await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
                await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
                Assert.Empty(MiniRows(surface));
                Assert.Contains("owning setup", Assert.IsType<string>(MiniValue(surface, "MiniImportStatus")));
                Assert.False(Assert.IsType<bool>(MiniValue(surface, "CanMiniImportRequest")));
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.DoesNotContain((await fixture.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                    item => item.State == HomePermissionRequestState.PendingApproval);
            });
        });

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Rendered_Mini_Inspect_needs_its_own_manual_Home_decision_and_decline_preserves_catalogue() =>
        WithActualMiniFixture(true, async fixture =>
        {
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await PrepareActualMiniOperation(fixture, window, surface);
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniRequest"));
                var request = Assert.Single((await fixture.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                    item => item.RequestId == requestId);
                Assert.Equal("mini-computer", request.Scope.TargetAppId);
                Assert.Equal(HomeCanonicalMiniComputerOperationSource.InspectAction, request.Scope.ActionName);
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.Null(fixture.Provider.OriginalClose);
                Assert.True((await fixture.Rig.Permissions.DecideAsync(requestId, HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
                await AwaitMiniUi(window, surface, () => MiniValue(surface, "CanMiniInspect") is true &&
                    Assert.IsType<string>(MiniValue(surface, "MiniStatus")).Contains("declined", StringComparison.Ordinal));
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.Null(fixture.Provider.OriginalClose);
                Assert.Equal(fixture.Vm.VMID.Value.ToString("D"), workspace.Snapshot.SelectedAssistant!.Configuration.MiniComputerId);
            });
        });

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Actual_Mini_pending_review_withdrawal_settles_the_same_accepted_operation_after_native_retirement() =>
        WithActualMiniFixture(true, async fixture =>
        {
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await PrepareActualMiniOperation(fixture, window, surface);
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniRequest"));
                fixture.Operations.RequestOriginalRetirement();
                var homeClose = fixture.Operations.CloseAndDrainOriginalAsync(); fixture.RetainOriginal(homeClose);
                var withdrawal = Assert.IsAssignableFrom<Task>(fixture.Operations.OriginalPendingReviewWithdrawalTask);
                fixture.RetainOriginal(withdrawal);
                surface.RequestRetirement();
                var close = surface.CloseAndDrainAsync(); fixture.RetainOriginal(close);
                await AwaitMiniUi(window, surface, () => close.IsCompleted, allowRetiring: true);
                await close; await withdrawal; await homeClose;
                Assert.Same(homeClose, fixture.Operations.OriginalClose);
                Assert.Same(close, surface.OriginalClose);
                Assert.Same(withdrawal, fixture.Operations.OriginalPendingReviewWithdrawalTask);
                Assert.True(owner.OriginalClose?.IsCompletedSuccessfully);
                var request = await fixture.Rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(requestId, Scope, fixture.RetainOriginal, Token);
                Assert.NotNull(request); Assert.Equal(HomePermissionRequestState.Cancelled, request.State);
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.Null(fixture.Provider.OriginalClose);
            });
        });

    private static async Task PrepareActualMiniOperation(MiniFixture fixture, Window window, AssistantsNativeCuiSurface surface)
    {
        await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
        Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional explicitly reviewed Mini helper"));
        await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
        await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
        await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
        await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
        await ClickActualMini(window, surface, "Request this catalogue in Home", () => MiniValue(surface, "HasMiniImportRequest") is true);
        var importId = Assert.IsType<string>(MiniValue(surface, "MiniImportRequest"));
        Assert.True((await fixture.Rig.Permissions.DecideAsync(importId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
        await ClickActualMini(window, surface, "Refresh Home decision", () => MiniValue(surface, "CanMiniImportComplete") is true);
        await ClickActualMini(window, surface, "Complete approved catalogue import", () => MiniRows(surface).Length == 1 && MiniValue(surface, "CanMiniRefresh") is true);
        await ClickActualMini(window, surface, "Use this VM for this Assistant", () => MiniValue(surface, "CanMiniInspect") is true);
        await ClickActualMini(window, surface, "Inspect current state", () => MiniValue(surface, "CanMiniConfirm") is true);
        await ClickActualMini(window, surface, "Request this exact action in Home", () => MiniValue(surface, "HasMiniRequest") is true);
        Assert.NotEqual(importId, Assert.IsType<string>(MiniValue(surface, "MiniRequest")));
    }
    private static async Task AwaitMiniUi(Window window, AssistantsNativeCuiSurface surface, Func<bool> settled, bool allowRetiring = false)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await FlushNativeMemoryUi(window);
            Assert.All(window.GetVisualDescendants().OfType<CuiSceneHost>(), host => Assert.Null(host.LastActionFailure));
            if (settled()) return;
            if (!allowRetiring) Assert.False(surface.IsRetiring);
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual Mini Computer operation and its original sources did not settle.");
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Rendered_identity_setup_preserves_existing_VM_unknown_fields_and_requires_separate_catalogue_import() =>
        WithActualMiniFixture(false, async fixture =>
        {
            var text = await File.ReadAllTextAsync(fixture.Path, Token);
            text = text.TrimEnd().TrimEnd('}') + ",\"retainedFutureField\":{\"version\":19,\"values\":[true,null,\"unchanged\"]}}";
            await File.WriteAllTextAsync(fixture.Path, text, Token);
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            var mode = MiniFixtureMode(fixture.Path);
            AssistantIdentity? assistant = null; Guid conversation = default;
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await PrepareActualMiniIdentitySetup(window, surface);
                assistant = workspace.Snapshot.SelectedAssistant!.Identity;
                conversation = workspace.Snapshot.ConversationBinding!.Conversation.Id;
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniIdentityRequest"));
                var request = Assert.Single((await fixture.Rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                    item => item.RequestId == requestId);
                Assert.Equal(HomeCanonicalMiniComputerCatalogIdentitySource.WriteAction, request.Scope.ActionName);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.Empty(MiniRows(surface)); Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token));
                Assert.True((await fixture.Rig.Permissions.DecideAsync(requestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                await AwaitMiniUi(window, surface, () => MiniValue(surface, "CanMiniImportRequest") is true &&
                    Assert.IsType<string>(MiniValue(surface, "MiniIdentityStatus")).Contains("durable identity", StringComparison.Ordinal));
                Assert.Empty(MiniRows(surface)); Assert.Null(fixture.Provider.OriginalClose);
                var after = await File.ReadAllBytesAsync(fixture.Path, Token);
                using var beforeJson = JsonDocument.Parse(original); using var afterJson = JsonDocument.Parse(after);
                foreach (var field in beforeJson.RootElement.EnumerateObject().Where(field => field.Name != "originalStoreIdentity"))
                    Assert.Equal(field.Value.GetRawText(), afterJson.RootElement.GetProperty(field.Name).GetRawText());
                var identity = afterJson.RootElement.GetProperty("originalStoreIdentity").Deserialize<ResourceStoreIdentity>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.NotNull(identity); Assert.NotEqual(Guid.Empty, identity.StoreId); Assert.False(identity.NewlyCreated);
                Assert.Equal(mode, MiniFixtureMode(fixture.Path));
                await ClickActualMini(window, surface, "Request this catalogue in Home", () => MiniValue(surface, "HasMiniImportRequest") is true);
                var importId = Assert.IsType<string>(MiniValue(surface, "MiniImportRequest")); Assert.NotEqual(requestId, importId);
                Assert.Empty(MiniRows(surface));
                Assert.True((await fixture.Rig.Permissions.DecideAsync(importId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                await ClickActualMini(window, surface, "Refresh Home decision", () => MiniValue(surface, "CanMiniImportComplete") is true);
                await ClickActualMini(window, surface, "Complete approved catalogue import", () => MiniRows(surface).Length == 1 && MiniValue(surface, "CanMiniRefresh") is true);
                Assert.Equal(fixture.Vm.VMID.Value, Assert.Single(MiniRows(surface)).Original.VirtualMachineId);
                Assert.Equal(after, await File.ReadAllBytesAsync(fixture.Path, Token));
            });
            var published = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.Reopen();
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await OpenActualSavedMemoryConversation(surface, assistant!, conversation, window);
                await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
                Assert.Equal(fixture.Vm.VMID.Value, Assert.Single(MiniRows(surface)).Original.VirtualMachineId);
                await ClickActualMini(window, surface, "Review catalogue identity setup", () => MiniValue(surface, "HasMiniIdentityPreview") is true);
                Assert.False(Assert.IsType<bool>(MiniValue(surface, "CanMiniIdentityRequest")));
                Assert.Equal(published, await File.ReadAllBytesAsync(fixture.Path, Token));
            });
        });

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Rendered_identity_setup_manual_decline_does_not_add_identity_or_import_access() =>
        WithActualMiniFixture(false, async fixture =>
        {
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await PrepareActualMiniIdentitySetup(window, surface);
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniIdentityRequest"));
                Assert.True((await fixture.Rig.Permissions.DecideAsync(requestId, HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
                await AwaitMiniUi(window, surface, () => MiniValue(surface, "CanMiniIdentityDiscard") is true &&
                    Assert.IsType<string>(MiniValue(surface, "MiniIdentityStatus")).Contains("declined", StringComparison.Ordinal));
                Assert.Empty(MiniRows(surface)); Assert.False(Assert.IsType<bool>(MiniValue(surface, "CanMiniImportRequest")));
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token)); Assert.Null(fixture.Provider.OriginalClose);
            });
        });

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Actual_identity_setup_pending_withdrawal_keeps_bytes_and_settles_retired_native_delivery() =>
        WithActualMiniFixture(false, async fixture =>
        {
            var original = await File.ReadAllBytesAsync(fixture.Path, Token);
            await fixture.WithView(async (window, surface, workspace, owner) =>
            {
                await PrepareActualMiniIdentitySetup(window, surface);
                var requestId = Assert.IsType<string>(MiniValue(surface, "MiniIdentityRequest"));
                fixture.IdentityOperations.RequestOriginalRetirement();
                var homeClose = fixture.IdentityOperations.CloseAndDrainOriginalAsync(); fixture.RetainOriginal(homeClose);
                var withdrawal = Assert.IsAssignableFrom<Task>(fixture.IdentityOperations.OriginalPendingReviewWithdrawalTask);
                fixture.RetainOriginal(withdrawal); surface.RequestRetirement();
                var close = surface.CloseAndDrainAsync(); fixture.RetainOriginal(close);
                await AwaitMiniUi(window, surface, () => close.IsCompleted, allowRetiring: true);
                await close; await withdrawal; await homeClose;
                Assert.Same(homeClose, fixture.IdentityOperations.OriginalClose);
                Assert.Same(close, surface.OriginalClose); Assert.True(owner.OriginalClose?.IsCompletedSuccessfully);
                var request = await fixture.Rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(requestId, Scope, fixture.RetainOriginal, Token);
                Assert.NotNull(request); Assert.Equal(HomePermissionRequestState.Cancelled, request.State);
                Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, Token)); Assert.Null(fixture.Provider.OriginalClose);
            });
        });
    private static UnixFileMode MiniFixtureMode(string path) => OperatingSystem.IsLinux()
        ? File.GetUnixFileMode(path) : throw new PlatformNotSupportedException("The actual mode fixture is Linux-only.");
    private static readonly List<MiniFixture> RetainedMiniIdentityFailures = [];
    [Fact(SkipUnless = nameof(UsesLinuxMiniComputerFixture), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public async Task Actual_identity_setup_stale_review_preserves_newer_bytes_and_same_failed_original_without_replay()
    {
        var fixture = new MiniFixture(); lock (RetainedMiniIdentityFailures) RetainedMiniIdentityFailures.Add(fixture);
        await fixture.Initialize(false);
        var actor = await fixture.Rig.Profiles.GetCurrentAsync(Token) ?? throw new InvalidOperationException("Actual Home actor required.");
        var prepared = await fixture.Catalog.PrepareOriginalIdentitySetupWithinSourceAsync(actor, Guid.NewGuid(), Scope, fixture.RetainOriginal, Token);
        Assert.NotNull(prepared.Intent);
        // Test-only concurrent edit of the actual existing private file, after the
        // original preview released its read reservation. No production repair runs.
        await File.AppendAllTextAsync(fixture.Path, " ", Token);
        var newer = await File.ReadAllBytesAsync(fixture.Path, Token);
        var actual = fixture.Catalog.ExecuteOriginalIdentitySetupWithinSourceAsync(prepared.Intent, Scope, fixture.RetainOriginal, Token);
        fixture.RetainOriginal(actual);
        await Assert.ThrowsAnyAsync<Exception>(async () => { await actual; });
        Assert.Same(actual, fixture.Catalog.ExecuteOriginalIdentitySetupWithinSourceAsync(prepared.Intent, Scope, fixture.RetainOriginal, Token));
        var errors = new List<Exception>(); await fixture.Close(errors);
        Assert.NotEmpty(errors); Assert.True(actual.IsFaulted); Assert.True(fixture.Catalog.OriginalClose?.IsFaulted);
        Assert.Equal(newer, await File.ReadAllBytesAsync(fixture.Path, Token));
        using var json = JsonDocument.Parse(newer);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("originalStoreIdentity").ValueKind);
    }
    private static async Task PrepareActualMiniIdentitySetup(Window window, AssistantsNativeCuiSurface surface)
    {
        await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
        Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional catalogue setup helper"));
        await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
        await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
        await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
        await surface.Bindings.DispatchAsync("assistants.computer.open", null, Token);
        Assert.Empty(MiniRows(surface));
        await ClickActualMini(window, surface, "Review catalogue identity setup", () => MiniValue(surface, "CanMiniIdentityRequest") is true);
        await ClickActualMini(window, surface, "Request identity setup in Home", () => MiniValue(surface, "HasMiniIdentityRequest") is true);
    }

    private static object? MiniValue(AssistantsNativeCuiSurface surface, string key)
    { Assert.True(surface.MiniComputerBindings.TryGetValue(key, out var value)); return value; }
    private static AssistantsMiniComputerCuiBindings.VirtualMachineRow[] MiniRows(AssistantsNativeCuiSurface surface) =>
        Assert.IsAssignableFrom<IEnumerable<AssistantsMiniComputerCuiBindings.VirtualMachineRow>>(MiniValue(surface, "MiniMachines")).ToArray();
    private static async Task ClickActualMini(Window window, AssistantsNativeCuiSurface surface, string label, Func<bool> settled)
    {
        await FlushNativeMemoryUi(window);
        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), item => item.IsEffectivelyVisible && item.Content as string == label);
        Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            await FlushNativeMemoryUi(window);
            Assert.All(window.GetVisualDescendants().OfType<CuiSceneHost>(), host => Assert.Null(host.LastActionFailure));
            if (settled()) return;
            Assert.False(surface.IsRetiring); await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual rendered Mini Computer source action did not settle.");
    }
    private static async Task WithActualMiniFixture(bool identity, Func<MiniFixture, Task> body)
    {
        var fixture = new MiniFixture(); var errors = new List<Exception>();
        try { await fixture.Initialize(identity); await body(fixture); } catch (Exception cause) { errors.Add(cause); }
        await fixture.Close(errors);
        if (errors.Count != 0) throw new AggregateException("Actual Mini Computer fixtures retained at " + fixture.Path, errors);
    }
    private sealed class MiniFixturePolicies : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
            new HomeMiniComputerOperationActionPolicySource().TryGet(appId, actionId) ??
            new HomeMiniComputerCatalogIdentityActionPolicySource().TryGet(appId, actionId);
    }
    private sealed class MiniFixture
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "assistant-mini-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(_root, "catalog.json");
        internal readonly VirtualMachine Vm = new(new(Guid.NewGuid()), VirtualBoxCliProvider.DefaultProviderID,
            Guid.NewGuid().ToString("D"), "Fictional retained Linux VM", "Linux", null, "x64", 2, 2048, "efi",
            VirtualMachineLifecycleState.PoweredOff, 1, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], []);
        internal Rig Rig = null!;
        private JsonMiniComputerCatalogStore _store = null!;
        private CanonicalMiniComputerCatalogOriginalOwner _catalog = null!;
        internal CanonicalMiniComputerCatalogOriginalOwner Catalog => _catalog;
        private HomeOriginalLocalStoreImportSession? _imports;
        private AssistantMiniComputerSource? _source;
        internal HomeCanonicalMiniComputerOperationSource Operations = null!;
        internal HomeCanonicalMiniComputerCatalogIdentitySource IdentityOperations = null!;
        private readonly List<Task> _originalCloses = [];
        internal VirtualBoxCliProvider Provider = null!;
        internal async Task Initialize(bool identity)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This actual protected fixture is Linux-only.");
            Directory.CreateDirectory(_root);
            File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _store = new(Path);
            var snapshot = JsonMiniComputerCatalogStore.Empty() with { VirtualMachines = [Vm],
                OriginalStoreIdentity = identity ? new ResourceStoreIdentity(1, Guid.NewGuid(), DateTimeOffset.UtcNow, false) : null };
            // Test-only existing canonical JSON fixture. Production navigation must
            // never seed this identity; the second fact proves the missing boundary.
            await File.WriteAllTextAsync(Path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Token);
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Rig = new((_, profiles) => _catalog = new(_store, profiles),
                originalAdditionalPolicy: new MiniFixturePolicies());
            await Rig.InitializeAsync(true, importMemory: false); Compose();
        }
        private void Compose()
        {
            Provider = VirtualBoxCliProvider.CreateOriginalDeferred(new JsonProviderIdentityMapStore(System.IO.Path.Combine(_root, "identities.json")),
                new LocalVirtualDiskLocationProvider(System.IO.Path.Combine(_root, "disks")));
            var engine = new MiniComputerEngine(new VirtualisationProviderRegistry([Provider], VirtualBoxCliProvider.DefaultProviderID),
                _store, new DenyWithoutHomeAuthorization());
            _source = new(Rig.Home, Rig.OriginalConversations, engine, _catalog, Rig.Profiles, Rig.Authority);
            _imports = new(CanonicalMiniComputerCatalogOriginalOwner.CatalogResourceKind, _catalog, Rig.Profiles, Rig.Ownership,
                _catalog, Rig.Permissions, Rig.Authority);
            _source.BindOriginalMiniComputerImportSession(_imports);
            var resources = new ResourceAuthorizationService(Rig.Profiles,
                [new HomeMiniComputerOperationResourceResolver(() => Operations),
                 new HomeMiniComputerCatalogIdentityResourceResolver(() => IdentityOperations)]);
            var broker = new HomeResourceOperationBroker(resources, Rig.Permissions);
            Operations = new(Rig.OriginalStateStore, Rig.Profiles, resources, broker, Rig.Permissions, _source);
            _source.BindOriginalHomeOperationSource(Operations);
            IdentityOperations = new(Rig.OriginalStateStore, Rig.Profiles, resources, broker, Rig.Permissions, _catalog);
            _catalog.BindOriginalIdentitySetupHomeSource(IdentityOperations);
        }
        internal async Task WithView(Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, AssistantMiniComputerController, Task> body)
        {
            var workspace = new AssistantsWorkspaceController(Rig.Bridge); var owner = new AssistantMiniComputerController(_source!, workspace);
            var window = new Window { Width = 1100, Height = 900 }; window.Show();
            AssistantsNativeCuiSurface? surface = null; var errors = new List<Exception>();
            try
            {
                surface = new(workspace, new MemoryFixtureReadiness(), captureOriginalOwner: value => surface = value, miniComputerManagement: owner);
                window.Content = surface; await surface.InitializeAsync(Token); await FlushNativeMemoryUi(window);
                await body(window, surface, workspace, owner);
                if (surface.IsRetiring) Assert.True(surface.OriginalClose?.IsCompletedSuccessfully);
                else Assert.True(await surface.PrepareToCloseAsync(Token));
            }
            catch (Exception cause) { errors.Add(cause); }
            foreach (var close in new Func<Task>[] { () => surface?.CloseAndDrainAsync() ?? Task.CompletedTask, owner.CloseAndDrainAsync, workspace.CloseAndDrainAsync })
            {
                Task? raw = null;
                try { raw = close(); _originalCloses.Add(raw); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
            if (surface?.OriginalClose?.IsCompletedSuccessfully == true && workspace.OriginalClose?.IsCompletedSuccessfully == true) window.Close();
            if (errors.Count != 0) throw new AggregateException("Actual Mini Computer view sources remain retained.", errors);
        }
        internal void RetainOriginal(Task raw) => _originalCloses.Add(raw);
        internal async Task Reopen()
        {
            var errors = new List<Exception>(); await CloseMini(errors);
            if (errors.Count != 0) throw new AggregateException(errors);
            await Rig.ReopenAsync(); Compose();
        }
        private async Task CloseMini(List<Exception> errors)
        {
            var closes = new List<Func<Task>>();
            if (Operations is not null)
            {
                try
                {
                    Operations.RequestOriginalPendingReviewWithdrawals();
                    if (Operations.OriginalPendingReviewWithdrawalTask is { } withdrawal)
                    { _originalCloses.Add(withdrawal); closes.Add(() => withdrawal); }
                }
                catch (Exception cause) { errors.Add(cause); }
            }
            if (IdentityOperations is not null)
            {
                try
                {
                    IdentityOperations.RequestOriginalPendingReviewWithdrawals();
                    if (IdentityOperations.OriginalPendingReviewWithdrawalTask is { } withdrawal)
                    { _originalCloses.Add(withdrawal); closes.Add(() => withdrawal); }
                }
                catch (Exception cause) { errors.Add(cause); }
            }
            if (_source is not null) closes.Add(_source.CloseAndDrainAsync);
            if (Operations is not null) closes.Add(Operations.CloseAndDrainOriginalAsync);
            if (IdentityOperations is not null) closes.Add(IdentityOperations.CloseAndDrainOriginalAsync);
            if (_imports is not null) closes.Add(_imports.CloseAndDrainAsync);
            if (_catalog is not null) closes.Add(_catalog.CloseAndDrainOriginalAsync);
            if (Provider is not null) closes.Add(Provider.CloseAndDrainOriginalAsync);
            foreach (var close in closes)
            {
                Task? raw = null;
                try { raw = close(); _originalCloses.Add(raw); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
        }
        internal async Task Close(List<Exception> errors)
        {
            await CloseMini(errors);
            if (Rig is not null)
            {
                Task? raw = null;
                try { raw = Rig.CloseAsync(); _originalCloses.Add(raw); await raw; }
                catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
            }
        }
    }
}
#endif
