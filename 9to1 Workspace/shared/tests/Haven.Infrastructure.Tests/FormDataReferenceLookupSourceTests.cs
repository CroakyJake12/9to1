using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Forms;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

[Collection("Forms Data native lookup")]
public sealed class FormDataReferenceLookupSourceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Actual_mounted_reference_response_uses_personal_Data_owner_and_saves_only_selected_original_reference(int lostReadReturn)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(LookupApplication));
        await native.Dispatch<bool>(async () =>
        {
            using var fixture = new Fixture(); await fixture.InitializeAsync();
            var denied = await FormNativeResponseSurface.OpenAsync(fixture.Responses, fixture.FormID, fixture.ResponseID);
            Assert.False(denied.Success); Assert.Equal("CapabilityUnavailable", denied.Code);
            var opened = await FormNativeResponseSurface.OpenAsync(fixture.Responses, fixture.FormID, fixture.ResponseID,
                token: default, referenceLookup: fixture.Source);
            Assert.True(opened.Success, opened.Code); using var surface = opened.Surface!;
            var registry = new CuiControlRegistry(); surface.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("actual-data-reference", "References", "forms",
                surface.CreateDocument(), surface, surface, new LookupReady()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content);
            window.Show();
            try
            {
                var before = fixture.Bytes();
                var query = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => AutomationProperties.GetName(x) == "Reference record search");
                var choices = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => AutomationProperties.GetName(x) == "Reference record choices");
                var status = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => AutomationProperties.GetName(x) == "Reference lookup status");
                var search = Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Find records"));
                if (lostReadReturn != 0)
                {
                    fixture.Observed.LoseNextReturn = lostReadReturn == 1;
                    fixture.Observed.LoseNextReturnAsCanceled = lostReadReturn == 2;
                    search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await ObserveAsync(() => status.Text?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true);
                    Assert.Empty(choices.Items); Assert.Equal(0, surface.UnsavedAnswerCount);
                    AssertBytes(before, fixture.Bytes());
                }
                query.Text = "Beta"; search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await ObserveAsync(() => status.Text == "Choose an authorized record.");
                var original = Assert.IsType<FormDataReferenceChoice>(Assert.Single(choices.Items));
                Assert.Equal("Beta", original.Label); AssertBytes(before, fixture.Bytes());
                choices.SelectedItem = original;
                await ObserveAsync(() => surface.UnsavedAnswerCount == 1);
                AssertBytes(before, fixture.Bytes()); // A lookup and selection do not save an answer.
                await surface.DispatchAsync("Save", null);
                Assert.Equal(0, surface.UnsavedAnswerCount);
                var reopened = await fixture.ReopenResponses().ReadSessionAsync(fixture.FormID, fixture.ResponseID);
                Assert.True(reopened.Success);
                var value = Assert.Single(reopened.Response!.Answers).Value.Deserialize<FormTableInputResponse>()!;
                var cell = Assert.Single(value.Rows).Cells[fixture.ColumnID];
                Assert.Equal(fixture.TableID, cell.GetProperty("tableID").GetGuid());
                Assert.Equal(original.RecordID.ToString("D"), cell.GetProperty("recordID").GetString());
                var committed = fixture.Bytes(); surface.Dispose();
                query.Text = "Alpha"; search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                AssertBytes(committed, fixture.Bytes());
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    [Fact]
    public async Task Actual_native_query_change_retires_completed_old_record_selection_before_answer_publication()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(LookupApplication));
        await native.Dispatch<bool>(async () =>
        {
            using var fixture = new Fixture(); await fixture.InitializeAsync();
            var opened = await FormNativeResponseSurface.OpenAsync(fixture.Responses, fixture.FormID, fixture.ResponseID,
                token: default, referenceLookup: fixture.Source);
            Assert.True(opened.Success, opened.Code); using var surface = opened.Surface!;
            var registry = new CuiControlRegistry(); surface.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("actual-reference-retirement", "References", "forms",
                surface.CreateDocument(), surface, surface, new LookupReady()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            try
            {
                var before = fixture.Bytes();
                var query = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => AutomationProperties.GetName(x) == "Reference record search");
                var choices = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => AutomationProperties.GetName(x) == "Reference record choices");
                var status = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => AutomationProperties.GetName(x) == "Reference lookup status");
                var search = Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Find records"));
                query.Text = "Alpha"; search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await ObserveAsync(() => status.Text == "Choose an authorized record.");
                var oldChoice = Assert.IsType<FormDataReferenceChoice>(Assert.Single(choices.Items));
                fixture.Observed.HoldLoadNumber = fixture.Observed.Loads + 1;
                choices.SelectedItem = oldChoice;
                await fixture.Observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, surface.UnsavedAnswerCount);
                query.Text = "Beta"; fixture.Observed.Release.TrySetResult();
                await fixture.Observed.HeldReturnObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(0, surface.UnsavedAnswerCount); Assert.Empty(choices.Items);
                AssertBytes(before, fixture.Bytes());
                search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await ObserveAsync(() => status.Text == "Choose an authorized record.");
                var fresh = Assert.IsType<FormDataReferenceChoice>(Assert.Single(choices.Items)); Assert.Equal("Beta", fresh.Label);
                choices.SelectedItem = fresh; await ObserveAsync(() => surface.UnsavedAnswerCount == 1);
                await surface.DispatchAsync("Save", null);
                var reopened = await fixture.ReopenResponses().ReadSessionAsync(fixture.FormID, fixture.ResponseID);
                Assert.True(reopened.Success);
                var cell = Assert.Single(Assert.Single(reopened.Response!.Answers).Value.Deserialize<FormTableInputResponse>()!.Rows).Cells[fixture.ColumnID];
                Assert.Equal(fresh.RecordID.ToString("D"), cell.GetProperty("recordID").GetString());
            }
            finally
            {
                fixture.Observed.Release.TrySetResult(); window.Close();
                if (fixture.Observed.Entered.Task.IsCompletedSuccessfully)
                    await fixture.Observed.HeldReturnObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return true;
        }, default);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Canonical_Respond_route_requires_explicit_actual_lookup_and_retains_same_durable_response_on_reopen(bool configured)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(LookupApplication));
        await native.Dispatch<bool>(async () =>
        {
            using var fixture = new Fixture(); await fixture.InitializeAsync(startResponse: false);
            var shown = 0; Guid? originalResponseID = null; Guid? selectedRecordID = null;
            async Task PresentAsync(FormNativeResponseSurface surface, CancellationToken token)
            {
                shown++; Assert.NotNull(surface.Response);
                if (originalResponseID is null) originalResponseID = surface.Response!.ResponseID;
                else Assert.Equal(originalResponseID, surface.Response!.ResponseID);
                Assert.Equal(0, surface.UnsavedAnswerCount);
                var registry = new CuiControlRegistry(); surface.Register(registry);
                var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("canonical-reference-response", "References", "forms",
                    surface.CreateDocument(), surface, surface, new LookupReady()) { ControlRegistry = registry }, cancellationToken: token);
                using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
                try
                {
                    var beforeSelection = fixture.Bytes();
                    if (shown > 1)
                    {
                        var saved = Assert.Single(surface.Response!.Answers).Value.Deserialize<FormTableInputResponse>()!;
                        Assert.Equal(selectedRecordID!.Value.ToString("D"), Assert.Single(saved.Rows).Cells[fixture.ColumnID].GetProperty("recordID").GetString());
                        var retainedStatus = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => AutomationProperties.GetName(x) == "Reference lookup status");
                        Assert.Equal("Saved reference retained. Find an authorized record to replace it.", retainedStatus.Text);
                        Assert.Empty(Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => AutomationProperties.GetName(x) == "Reference record choices").Items);
                        Assert.Equal(0, surface.UnsavedAnswerCount); AssertBytes(beforeSelection, fixture.Bytes());
                        return;
                    }
                    var query = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => AutomationProperties.GetName(x) == "Reference record search");
                    var choices = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => AutomationProperties.GetName(x) == "Reference record choices");
                    var status = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => AutomationProperties.GetName(x) == "Reference lookup status");
                    query.Text = "Beta";
                    Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Find records"))
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await ObserveAsync(() => status.Text == "Choose an authorized record.");
                    var choice = Assert.IsType<FormDataReferenceChoice>(Assert.Single(choices.Items));
                    Assert.Equal("Beta", choice.Label); selectedRecordID = choice.RecordID; choices.SelectedItem = choice;
                    await ObserveAsync(() => surface.UnsavedAnswerCount == 1); AssertBytes(beforeSelection, fixture.Bytes());
                    await surface.DispatchAsync("Save", null, token); Assert.Equal(0, surface.UnsavedAnswerCount);
                }
                finally { window.Close(); }
            }
            var workspace = configured
                ? new FormsCuiWorkspace(fixture.Publications, new FormAuthoringService(fixture.Publications), () => fixture.FormID, _ => true,
                    null, fixture.Responses, PresentAsync, fixture.Source)
                : new FormsCuiWorkspace(fixture.Publications, new FormAuthoringService(fixture.Publications), () => fixture.FormID, _ => true,
                    responseSessions: fixture.Responses, showResponse: PresentAsync);
            await workspace.DispatchAsync("9to1.Forms.Open", null);
            if (configured) await workspace.DispatchAsync("9to1.Forms.Respond", null);
            else
            {
                var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DispatchAsync("9to1.Forms.Respond", null).AsTask());
                Assert.Equal("CapabilityUnavailable", unavailable.Message);
            }
            if (configured)
            {
                Assert.Equal(1, shown); Assert.NotNull(originalResponseID);
                var reopened = await fixture.ReopenResponses().ReadSessionAsync(fixture.FormID, originalResponseID!.Value);
                Assert.True(reopened.Success); var saved = Assert.Single(reopened.Response!.Answers).Value.Deserialize<FormTableInputResponse>()!;
                Assert.Equal(selectedRecordID!.Value.ToString("D"), Assert.Single(saved.Rows).Cells[fixture.ColumnID].GetProperty("recordID").GetString());
            }
            else
            {
                Assert.Equal(0, shown); Assert.True(workspace.TryGetValue("Status", out var status));
                Assert.Contains("CapabilityUnavailable", Assert.IsType<string>(status));
            }
            var afterFirst = fixture.Bytes();
            if (configured) await workspace.DispatchAsync("9to1.Forms.Respond", null);
            else
            {
                var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DispatchAsync("9to1.Forms.Respond", null).AsTask());
                Assert.Equal("CapabilityUnavailable", unavailable.Message);
            }
            Assert.Equal(configured ? 2 : 0, shown); AssertBytes(afterFirst, fixture.Bytes());
            return true;
        }, default);
    }

    private static async Task ObserveAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Actual native lookup publication did not arrive.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(1); // Observe an actual asynchronous IO/UI condition, never substitute delayed fake data.
        }
    }
    public sealed class LookupApplication : Avalonia.Application
    {
        public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<LookupApplication>().UseSkia())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    }
    private sealed class LookupReady : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "FixtureOwnerReady", "Actual owner fixture mounted."));
    }
    [Fact]
    public async Task Actual_personal_bound_stores_read_paginated_canonical_choices_and_retain_reference_answer_without_lookup_writes()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var before = fixture.Bytes();
        using var lookup = await fixture.OpenAsync() ?? throw new InvalidOperationException("Actual original lookup required.");
        var first = await lookup.ReadChoicesAsync("a", 0, 2);
        Assert.True(first.Success); Assert.True(first.HasMore);
        Assert.Equal(new[] { "Alpha", "Beta" }, first.Choices!.Select(choice => choice.Label).ToArray());
        var beta = first.Choices![1];
        Assert.Null(await lookup.SelectAsync(new(beta.RecordID, beta.Label))); // Locator values do not replace actual issued observation.
        var selected = (await lookup.SelectAsync(beta))!.Value;
        Assert.Equal(fixture.TableID, selected.GetProperty("tableID").GetGuid());
        Assert.Equal(beta.RecordID.ToString("D"), selected.GetProperty("recordID").GetString());
        var second = await lookup.ReadChoicesAsync("a", 2, 2);
        Assert.True(second.Success); Assert.False(second.HasMore); Assert.Equal("Gamma", Assert.Single(second.Choices!).Label);
        Assert.Null(await lookup.SelectAsync(beta)); // Prior page is not the currently issued choice set.
        var empty = await lookup.ReadChoicesAsync("not present", 0, 2);
        Assert.True(empty.Success); Assert.Empty(empty.Choices!);
        AssertBytes(before, fixture.Bytes());
        var answer = new FormTableInputResponse(fixture.FieldID,
            [new(fixture.FixedRowID, new Dictionary<Guid, JsonElement> { [fixture.ColumnID] = selected })]);
        var saved = await fixture.Responses.AnswerAsync(fixture.FormID, fixture.ResponseID, 1, fixture.FieldID,
            JsonSerializer.SerializeToElement(answer)); Assert.True(saved.Success, saved.Code);
        var reopened = await fixture.ReopenResponses().ReadSessionAsync(fixture.FormID, fixture.ResponseID);
        Assert.True(reopened.Success); Assert.Equal(saved.Response!.Revision, reopened.Response!.Revision);
        var cell = Assert.Single(Assert.Single(reopened.Response.Answers).Value.Deserialize<FormTableInputResponse>()!.Rows).Cells[fixture.ColumnID];
        Assert.Equal(fixture.TableID, cell.GetProperty("tableID").GetGuid());
        Assert.Equal(beta.RecordID.ToString("D"), cell.GetProperty("recordID").GetString());
        // The lookup only produced typed answer data. Existing response service owns this separate admitted save.
    }
    [Theory]
    [InlineData("data")] [InlineData("forms")]
    public async Task Actual_original_session_never_adopts_replaced_same_profile_receipt_but_explicit_fresh_open_can_read(string resourceKind)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var original = await fixture.OpenAsync() ?? throw new InvalidOperationException("Original lookup required.");
        var first = await original.ReadChoicesAsync("Alpha", 0, 2);
        var choice = Assert.Single(first.Choices!); Assert.True(first.Success);
        await fixture.ChangeActualBindingRevisionAsync(resourceKind);
        Assert.Equal(fixture.Actor, await fixture.Profiles.GetCurrentAsync(default));
        var changed = fixture.Bytes();
        var denied = await original.ReadChoicesAsync("Alpha", 0, 2);
        Assert.False(denied.Success); Assert.Null(denied.Choices);
        Assert.Null(await original.SelectAsync(choice)); AssertBytes(changed, fixture.Bytes());
        using var fresh = await fixture.OpenAsync() ?? throw new InvalidOperationException("Explicit fresh lookup required.");
        var accepted = await fresh.ReadChoicesAsync("Alpha", 0, 2);
        Assert.True(accepted.Success); var replacement = Assert.Single(accepted.Choices!);
        Assert.Equal(choice.RecordID, replacement.RecordID); Assert.NotSame(choice, replacement);
        Assert.Null(await fresh.SelectAsync(choice)); Assert.NotNull(await fresh.SelectAsync(replacement));
        AssertBytes(changed, fixture.Bytes());
    }
    [Fact]
    public async Task Actual_original_ownership_receipt_retired_during_final_repository_identity_withholds_choices()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var lookup = await fixture.OpenAsync() ?? throw new InvalidOperationException("Actual original lookup required.");
        fixture.Observed.HoldIdentityNumber = 2;
        var pending = lookup.ReadChoicesAsync("", 0, 2).AsTask();
        try
        {
            await fixture.Observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(pending.IsCompleted);
            await fixture.ChangeActualDataBindingRevisionAsync();
            Assert.Equal(fixture.Actor, await fixture.Profiles.GetCurrentAsync(default));
            var retired = fixture.Bytes(); fixture.Observed.Release.TrySetResult();
            var denied = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(denied.Success); Assert.Null(denied.Choices);
            AssertBytes(retired, fixture.Bytes());
        }
        finally
        {
            fixture.Observed.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    [Fact]
    public async Task Actual_completed_repository_read_lost_return_reports_unavailable_not_empty_and_invalidates_original_choices()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var lookup = await fixture.OpenAsync() ?? throw new InvalidOperationException("Actual original lookup required.");
        var issued = await lookup.ReadChoicesAsync("Alpha", 0, 2);
        var original = Assert.Single(issued.Choices!); Assert.True(issued.Success);
        var before = fixture.Bytes(); fixture.Observed.LoseNextReturn = true;
        var unavailable = await lookup.ReadChoicesAsync("", 0, 2);
        Assert.False(unavailable.Success); Assert.Equal("LookupUnavailable", unavailable.Code);
        Assert.Null(unavailable.Choices); Assert.Null(await lookup.SelectAsync(original));
        AssertBytes(before, fixture.Bytes());
        var fresh = await lookup.ReadChoicesAsync("Alpha", 0, 2);
        Assert.True(fresh.Success); Assert.NotNull(await lookup.SelectAsync(Assert.Single(fresh.Choices!)));
        AssertBytes(before, fixture.Bytes());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Actual_completed_repository_return_cannot_publish_after_original_lifetime_or_Data_revision_retires(int change)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var current = true; using var lookup = await fixture.OpenAsync(() => current) ?? throw new InvalidOperationException();
        fixture.Observed.HoldLoadNumber = 2;
        var pending = lookup.ReadChoicesAsync("", 0, 10).AsTask();
        Dictionary<string, byte[]> expected = null!;
        try
        {
            await fixture.Observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(pending.IsCompleted);
            if (change == 1) await fixture.ChangeActualSourceAsync();
            else if (change == 2) await fixture.ChangeActualProfileAsync();
            else current = false;
            expected = fixture.Bytes();
        }
        finally
        {
            fixture.Observed.Release.TrySetResult();
            if (expected is null) { try { await pending; } catch { } }
        }
        var denied = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(denied.Success); Assert.Null(denied.Choices);
        AssertBytes(expected, fixture.Bytes());
        Assert.Null(await lookup.SelectAsync(new(Guid.NewGuid(), "manufactured choice")));
    }
    [Fact]
    public async Task Actual_foreign_Home_composition_and_foreign_response_or_table_configuration_do_not_gain_lookup_reads()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var before = fixture.Bytes(); var loads = fixture.Observed.Loads;
        var foreignPath = Path.Combine(fixture.Paths.DataDirectory, "foreign-home.json");
        var foreign = fixture.CreateSource(new FileHomeCoreStateStore(foreignPath));
        Assert.Null(await foreign.OpenForOriginalResponseAsync(fixture.FormID, fixture.ResponseID, fixture.FieldID,
            fixture.ColumnID, fixture.Actor, () => true));
        Assert.False(File.Exists(foreignPath)); Assert.Equal(loads, fixture.Observed.Loads);
        Assert.Null(await fixture.Source.OpenForOriginalResponseAsync(fixture.FormID, Guid.NewGuid(), fixture.FieldID,
            fixture.ColumnID, fixture.Actor, () => true));
        var mismatched = fixture.CreateSource(fixture.Home, fixture.Binding with { TableID = Guid.NewGuid() });
        Assert.Null(await mismatched.OpenForOriginalResponseAsync(fixture.FormID, fixture.ResponseID, fixture.FieldID,
            fixture.ColumnID, fixture.Actor, () => true));
        Assert.Equal(loads, fixture.Observed.Loads); AssertBytes(before, fixture.Bytes());
    }
    [Fact]
    public async Task Actual_original_publication_and_response_reads_refuse_foreign_actor_and_store_before_content_load()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var before = fixture.Bytes();
        var observed = new ObservedForms(fixture.Settings);
        var authority = new FormLocalStoreAuthority(fixture.Settings, fixture.Settings, fixture.Profiles, fixture.Ownership);
        var publications = new FormPublicationService(observed, observed, authority, new BackendCanonicalValidator(), actors: fixture.Profiles);
        var originalStore = (await fixture.Settings.GetStoreIdentityAsync(default)).StoreId;
        var foreignActor = fixture.Actor with { ActorId = "foreign-actor" };
        var deniedActor = await publications.ReadForOriginalActorAsync(fixture.FormID, foreignActor, originalStore);
        Assert.False(deniedActor.Success); Assert.Equal("PermissionDenied", deniedActor.Code); Assert.Equal(0, observed.ContentReads);
        var deniedStore = await publications.ReadForOriginalActorAsync(fixture.FormID, fixture.Actor, Guid.NewGuid());
        Assert.False(deniedStore.Success); Assert.Equal("PermissionDenied", deniedStore.Code); Assert.Equal(0, observed.ContentReads);
        var sessions = new FormResponseSessionService(publications, observed, observed, authority, fixture.Profiles);
        var foreignScope = await sessions.ReadSessionAsync(fixture.FormID, fixture.ResponseID,
            scope: new(originalStore, foreignActor));
        Assert.False(foreignScope.Success); Assert.Equal("PermissionDenied", foreignScope.Code); Assert.Equal(0, observed.ContentReads);
        var wrongRoot = await sessions.ReadSessionAsync(fixture.FormID, fixture.ResponseID,
            scope: new(Guid.NewGuid(), fixture.Actor));
        Assert.False(wrongRoot.Success); Assert.Equal("PermissionDenied", wrongRoot.Code); Assert.Equal(0, observed.ContentReads);
        AssertBytes(before, fixture.Bytes());
    }
    private static void AssertBytes(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    { Assert.Equal(expected.Keys.Order().ToArray(), actual.Keys.Order().ToArray()); foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]); }
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Profiles { get; }
        public HomeResourceStoreOwnershipAuthority Ownership { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public VersionedAtomicSettingsStore Settings { get; }
        public DataWorkbookRepository Data { get; }
        public ObservedData Observed { get; }
        public DataLocalStoreAuthority DataAuthority { get; private set; } = null!;
        public FormResponseSessionService Responses { get; private set; } = null!;
        private FormPublicationService _publications = null!;
        public FormPublicationService Publications => _publications;
        private FormLocalStoreAuthority _formsAuthority = null!;
        public FormDataReferenceLookupBinding Binding { get; private set; } = null!;
        public FormDataReferenceLookupSource Source { get; private set; } = null!;
        public Guid FormID => Binding.FormID;
        public Guid ResponseID { get; private set; }
        public Guid FieldID => Binding.FieldID;
        public Guid ColumnID => Binding.ColumnID;
        public Guid TableID => Binding.TableID;
        public Guid FixedRowID { get; } = Guid.NewGuid();
        public Fixture()
        {
            Home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            Profiles = new(Home, new OperatingSystemPrincipalSource());
            Settings = new(Paths); Data = new(Paths); Observed = new(Data);
        }
        public async Task InitializeAsync(bool startResponse = true)
        {
            Actor = await Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException();
            var policy = new DataMutationActionPolicies();
            var permissions = new HomePermissionTrustService(Home, (app, action) => policy.TryGet(app, action)
                ?? (app == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null));
            var local = new HomeLocalStoreOwnership(Home, Profiles,
                new HomeLocalStoreEvidenceRegistry([new DataLocalStoreEvidenceProvider(Data), new FormLocalStoreEvidenceProvider(Settings, Settings)]), permissions);
            Ownership = new(local, Profiles); DataAuthority = new(Data, Profiles, Ownership);
            _formsAuthority = new(Settings, Settings, Profiles, Ownership);
            var storeID = (await Data.GetStoreIdentityAsync(default)).StoreId;
            await local.BindNewEmptyAsync("data", storeID.ToString("D"));
            var import = await local.RequestImportAsync("forms", storeID.ToString("D"), "actual-reference-lookup-setup");
            Assert.True((await permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            await local.CompleteImportAsync(import.RequestId);
            var workbook = DataWorkbook.Create("Actual choices"); var sheet = workbook.Sheets[0];
            sheet.SetCell(0, 0, "Name"); sheet.SetCell(1, 0, "Alpha"); sheet.SetCell(2, 0, "Beta"); sheet.SetCell(3, 0, "Gamma");
            var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 3 } };
            DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
            var admission = await DataAuthority.CaptureAsync(storeID, workbook.Id, 0, Guid.Empty, "data.workbook.create", Actor, default);
            await Data.SaveAsync(workbook, "Actual reference source", admission!, default);
            workbook = (await Data.LoadAsync(workbook.Id, default))!;
            var now = DateTimeOffset.UtcNow; var form = FormProjectEditor.Create("Reference response", FormModeKind.Form, now);
            var fieldID = Guid.NewGuid(); var columnID = Guid.NewGuid();
            var definition = new FormTableInputDefinition(fieldID,
                [new(columnID, "Record", FormTableCellType.Reference, true, ReferencedTableID: table.Id)],
                MinimumRows: 1, MaximumRows: 1, AllowAddedRows: false, FixedRowIDs: [FixedRowID]);
            var field = new FormField(fieldID, FormFieldKind.TableInput, "Reference table", null,
                JsonSerializer.SerializeToElement(new { }), true, new(), Table: definition);
            form = FormProjectEditor.AddField(form, form.Revision, form.Pages[0].PageID, field, now);
            // This backend-only fixture validates canonical schema. It does not attest an installed
            // native reference renderer; production native publication remains unavailable until wiring.
            _publications = new(Settings, Settings, _formsAuthority, new BackendCanonicalValidator(), actors: Profiles);
            var created = await _publications.CreateAsync(form.FormID, FormProjectEditor.Project(form)); Assert.True(created.Success);
            var published = await _publications.PublishAsync(form.FormID, created.Publication!.Revision); Assert.True(published.Success);
            Responses = ReopenResponses();
            var versionID = published.Publication!.ActiveVersionID ?? throw new InvalidOperationException("Actual published version required.");
            if (startResponse)
            {
                var started = await Responses.StartAsync(form.FormID, published.Publication.Revision);
                Assert.True(started.Success); ResponseID = started.Response!.ResponseID;
                Assert.Equal(versionID, started.Response.FormVersionID);
            }
            Binding = new(form.FormID, versionID, fieldID, columnID, storeID,
                workbook.Id, table.Id, table.Fields[0].FieldID, workbook.RevisionId, workbook.Version);
            Source = CreateSource(Home);
        }
        public FormDataReferenceLookupSource CreateSource(FileHomeCoreStateStore home, FormDataReferenceLookupBinding? binding = null)
            => new(home, Profiles, Ownership, Observed, Settings, [binding ?? Binding]);
        public FormResponseSessionService ReopenResponses()
        {
            var reopened = new VersionedAtomicSettingsStore(Paths);
            var authority = new FormLocalStoreAuthority(reopened, reopened, Profiles, Ownership);
            var publications = new FormPublicationService(reopened, reopened, authority, new BackendCanonicalValidator(), actors: Profiles);
            return new(publications, reopened, reopened, authority, Profiles);
        }
        public Task<IFormDataReferenceLookupSession?> OpenAsync(Func<bool>? lifetime = null)
            => Source.OpenForOriginalResponseAsync(FormID, ResponseID, FieldID, ColumnID, Actor, lifetime ?? (() => true));
        public async Task ChangeActualSourceAsync()
        {
            var workbook = (await Data.LoadAsync(Binding.WorkbookID, default))!;
            workbook.Sheets[0].SetCell(1, 0, "Changed source");
            var admission = await DataAuthority.CaptureAsync(Binding.DataStoreID, workbook.Id, workbook.Version,
                workbook.RevisionId, "data.workbook.save", Actor, default);
            await Data.SaveAsync(workbook, "Actual independently changed source", admission!, default);
        }
        public Task ChangeActualDataBindingRevisionAsync() => ChangeActualBindingRevisionAsync("data");
        public async Task ChangeActualBindingRevisionAsync(string resourceKind)
        {
            var state = await Home.ReadAsync(); Assert.True(state.IsSuccess);
            var record = state.State!.Records.Single(value => value.RecordType == "home.local-store-ownership"
                && value.Payload.Deserialize<HomeLocalStoreBinding>()?.ResourceKind == resourceKind);
            var changed = await Home.WriteAsync(record with { Revision = checked(record.Revision + 1) }, record.Revision);
            Assert.True(changed.IsSuccess);
        }
        public async Task ChangeActualProfileAsync()
        {
            var state = await Home.ReadAsync(); Assert.True(state.IsSuccess);
            var record = state.State!.Records.Single(value => value.RecordId == "home.local-profile");
            var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
            var write = await Home.WriteAsync(record with { Revision = checked(record.Revision + 1),
                Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision);
            Assert.True(write.IsSuccess);
        }
        public Dictionary<string, byte[]> Bytes() => Directory.GetFiles(Paths.DataDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Paths.DataDirectory, path), File.ReadAllBytes);
        public void Dispose() => Paths.Dispose();
    }
    private sealed class BackendCanonicalValidator : IFormProjectPublicationValidator
    { public void Validate(Guid id, JsonElement value) { var project = FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText())); Assert.Equal(id, project.FormID); } }
    private sealed class ObservedData(DataWorkbookRepository actual) : IDataGuardedWorkbookRepository
    {
        private int _loads;
        private int _identities;
        public int HoldIdentityNumber { get; set; }
        public int Loads => Volatile.Read(ref _loads);
        public int HoldLoadNumber { get; set; }
        public bool LoseNextReturn { get; set; }
        public bool LoseNextReturnAsCanceled { get; set; }
        public TaskCompletionSource HeldReturnObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DataWorkbook?> LoadAsync(Guid id, CancellationToken token)
        {
            var loaded = await actual.LoadAsync(id, token);
            if (LoseNextReturn) { LoseNextReturn = false; throw new IOException("Actual acknowledged read return lost."); }
            if (LoseNextReturnAsCanceled) { LoseNextReturnAsCanceled = false; throw new OperationCanceledException("Actual acknowledged read cancelled before return."); }
            if (Interlocked.Increment(ref _loads) == HoldLoadNumber)
            {
                Entered.TrySetResult();
                try { await Release.Task.WaitAsync(token); }
                finally { HeldReturnObserved.TrySetResult(); }
            }
            return loaded;
        }
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken token) => actual.ListAsync(token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken token) => actual.SaveAsync(workbook, reason, token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission, CancellationToken token) => actual.SaveAsync(workbook, reason, admission, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (Interlocked.Increment(ref _identities) == HoldIdentityNumber)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken token) => actual.ReadStoreEvidenceAsync(token);
    }
    private sealed class ObservedForms(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IResourceStoreIdentitySource
    {
        public int ContentReads { get; private set; }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { ContentReads++; return actual.GetAsync<T>(key, token); }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => actual.GetStoreIdentityAsync(token);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-reference-lookup-").FullName;
        public string RootDirectory => DataDirectory;
        public string DatabasePath => Path.Combine(DataDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
