using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// Actual CUI helper/loader with explicit local owner doubles. This is not an
// installed Home/Files permission grant or native file-system qualification.
public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Actual_completed_row_reveals_existing_Files_item_using_current_original_record_source()
    {
        var f = await DownloadFixture.Create(existing: true);
        try
        {
            await f.Open(); await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton"));
            Assert.Equal(1, f.Files.RecordRevalidations); Assert.Same(f.Files.Existing, Assert.Single(f.Files.Revealed));
            Assert.Equal(f.Content.OriginalRecord.Id, f.Files.ObservedRecord!.OriginalRecord.Id);
            Assert.Equal(0, f.Files.DestinationReads); Assert.Empty(f.Files.Registrations); await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Actual_Files_folder_and_approval_buttons_reuse_same_content_destination_operation_then_reveal()
    {
        var f = await DownloadFixture.Create();
        try
        {
            await f.Open(); await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton"));
            var first = Assert.Single(DownloadDestinations(f.Workspace));
            Assert.True(f.Workspace.TryGetItemValue(first, "Label", out var label)); Assert.Equal("Personal Files", label);
            await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadDestinationButton"));
            var original = Assert.Single(f.Files.Registrations);
            Assert.Same(f.Content, original.Content); Assert.Same(f.Files.Destination, original.Destination);
            Assert.NotEqual(Guid.Empty, original.Operation); Assert.Empty(f.Files.Revealed);
            Assert.True(Find(f.Window, "BrowseRetryDownloadRegistrationButton").IsEnabled);
            f.Files.Approved = true;
            await Click(f.Window, f.Workspace, Find(f.Window, "BrowseRetryDownloadRegistrationButton"));
            Assert.Equal(2, f.Files.Registrations.Count); Assert.Equal(original, f.Files.Registrations[1]);
            Assert.Single(f.Files.Revealed); Assert.Empty(DownloadDestinations(f.Workspace));
            var driver = f.Workspace.OriginalCommand;
            await f.Workspace.DispatchAsync("9to1.Browse.RegisterDownloadDestination", first.Target);
            Assert.Same(driver, f.Workspace.OriginalCommand); Assert.Equal(2, f.Files.Registrations.Count);
            Assert.False(f.Workspace.TryGetItemValue(first, "Target", out _)); await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Legacy_download_path_cannot_mint_original_transfer_or_Files_destination()
    {
        var f = await DownloadFixture.Create(exposeContent: false);
        try
        {
            await f.Open(); await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton"));
            Assert.Equal(0, f.Files.DestinationReads); Assert.Empty(f.Files.Registrations); Assert.Empty(DownloadDestinations(f.Workspace));
            Assert.True(f.Workspace.TryGetValue("DownloadOpenStatus", out var detail)); Assert.Contains("no active original transfer", Assert.IsType<string>(detail));
            Assert.False(Find(f.Window, "BrowseRetryDownloadRegistrationButton").IsEnabled); await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Foreign_pending_Files_result_retains_failed_driver_and_same_failed_close_without_retry_grant()
    {
        var f = await DownloadFixture.Create();
        try
        {
            await f.Open(); await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton"));
            f.Files.ReturnForeign = true;
            await Assert.ThrowsAnyAsync<Exception>(() => Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadDestinationButton")));
            var failed = f.Workspace.OriginalCommand; Assert.NotNull(failed); Assert.True(failed.IsFaulted);
            Assert.Contains(failed, f.Workspace.OriginalCommands); Assert.Empty(f.Files.Revealed);
            Assert.False(Find(f.Window, "BrowseRetryDownloadRegistrationButton").IsEnabled);
            f.Window.Close(); var close = f.Window.OriginalClose; Assert.NotNull(close);
            await Assert.ThrowsAnyAsync<Exception>(() => Observe(close!, f.Window));
            f.Window.Close(); Assert.Same(close, f.Window.OriginalClose); Assert.Same(failed, f.Workspace.OriginalCommand);
            RetainedFailedOwners.Add((f.Window, close!.Exception!));
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Original_record_callback_rejects_restored_context_self_join_before_close_publication()
    {
        var f = await DownloadFixture.Create(existing: true);
        try
        {
            await f.Open(); var neutral = ExecutionContext.Capture()!;
            f.Files.InsideRecordScope = () => ExecutionContext.Run(neutral, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = f.Workspace.DisposeAsync(); });
                Assert.Throws<InvalidOperationException>(() => f.Window.CanAdmitOriginalClose());
                Assert.Null(f.Window.OriginalClose); Assert.Null(f.Workspace.OriginalClose);
            }, null);
            await Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton"));
            Assert.Equal(1, f.Files.RecordRevalidations); Assert.Single(f.Files.Revealed); await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    [AvaloniaFact]
    public async Task Postcallback_record_scope_refusal_waits_for_same_actual_UI_stage_before_original_command_terminal()
    {
        var f = await DownloadFixture.Create(existing: true);
        try
        {
            await f.Open(); var fault = new IOException("Actual source scope refused after issuing its UI stage");
            f.Files.AfterRecordScope = () => throw fault;
            await Assert.ThrowsAnyAsync<Exception>(() => Click(f.Window, f.Workspace, Find(f.Window, "BrowseDownloadFilesButton")));
            var driver = f.Workspace.OriginalCommand; Assert.NotNull(driver); Assert.True(driver.IsFaulted);
            Assert.Contains(fault, driver.Exception!.Flatten().InnerExceptions);
            Assert.Equal(2, f.Files.OriginalRecordStages.Count);
            Assert.All(f.Files.OriginalRecordStages, stage => Assert.True(stage.IsCompleted));
            Assert.True(f.Files.OriginalRecordStages[0].IsFaulted); Assert.True(f.Files.OriginalRecordStages[1].IsCompletedSuccessfully);
            Assert.Equal(0, f.Files.RecordRevalidations); Assert.Empty(f.Files.Revealed);
            f.Window.Close(); var close = f.Window.OriginalClose; Assert.NotNull(close);
            await Assert.ThrowsAnyAsync<Exception>(() => Observe(close!, f.Window));
            f.Window.Close(); Assert.Same(close, f.Window.OriginalClose);
            RetainedFailedOwners.Add((f.Window, fault));
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }
    private static BrowseNativeWorkspace.DownloadDestinationRow[] DownloadDestinations(BrowseNativeWorkspace workspace)
    { Assert.True(workspace.TryGetValue("DownloadDestinations", out var value)); return Assert.IsType<BrowseNativeWorkspace.DownloadDestinationRow[]>(value); }
    private sealed record DownloadFixture(BrowseNativeWorkspace Workspace, BrowseNativeWindow Window, FilesDouble Files, ContentDouble Content)
    {
        public static async Task<DownloadFixture> Create(bool existing = false, bool exposeContent = true)
        {
            var record = new BrowserDownloadRecord(Guid.NewGuid(), Guid.NewGuid(), "https://download.example/file", "example.wav",
                "descriptive-only/never-opened.wav", 16, new string('a', 64), "audio/wav", DateTimeOffset.UtcNow);
            var content = new ContentDouble(record); var files = new FilesDouble(content, existing);
            var chrome = await BrowseChrome.CreateAsync(new Paths(), new FixtureFactory(exposeContent ? content : null), new LedgerDouble(record));
            var workspace = new BrowseNativeWorkspace(chrome, _ => true, files);
            return new(workspace, BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness()), files, content);
        }
        public async Task Open()
        { Window.Show(); await Observe(Window.InitializeAsync(), Window); await Click(Window, Workspace, Find(Window, "BrowseDownloadsButton")); }
    }
    private sealed class PlanDouble(Guid id) : IBrowserOriginalNativeDownloadTransportPlan { public Guid ActionId { get; } = id; }
    private sealed class ContentDouble(BrowserDownloadRecord record) : IBrowserOriginalDownloadContent
    {
        public BrowserDownloadRecord OriginalRecord { get; } = record;
        public IBrowserOriginalNativeDownloadTransportPlan OriginalPlan { get; } = new PlanDouble(record.ActionId);
        public Task? OriginalClose { get; private set; }
        public void RequestRetirement() { } public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }
    private sealed record DestinationDouble(string DisplayName) : IBrowserOriginalDownloadFilesDestination;
    private sealed record CatalogueDouble(IReadOnlyList<IBrowserOriginalDownloadFilesDestination> Destinations, string Detail) : IBrowserOriginalDownloadFilesDestinationCatalogue;
    private sealed record RegistrationDouble(Guid OriginalOperationId, BrowserDownloadRecord OriginalRecord,
        BrowserOriginalDownloadFilesRegistrationState State, Guid? OriginalFilesItemId, Guid? OriginalFilesRevisionId, string Detail) : IBrowserOriginalDownloadFilesRegistration;
    private sealed class FilesDouble(ContentDouble content, bool existing) : IBrowserOriginalDownloadFilesService
    {
        private readonly List<IBrowserOriginalDownloadFilesRegistration> _issued = [];
        public IBrowserOriginalNativeDownloadPhysicalOwner OriginalContentOwner { get; } = new PhysicalDouble(content);
        public DestinationDouble Destination { get; } = new("Personal Files");
        public RegistrationDouble Existing { get; } = new(Guid.NewGuid(), content.OriginalRecord, BrowserOriginalDownloadFilesRegistrationState.Registered, Guid.NewGuid(), Guid.NewGuid(), "Existing Files item");
        public bool Approved, ReturnForeign; public int DestinationReads, RecordRevalidations;
        public Action? InsideRecordScope, AfterRecordScope; public IBrowserOriginalDownloadRecordObservation? ObservedRecord;
        public List<Task> OriginalRecordStages { get; } = [];
        public List<(IBrowserOriginalDownloadContent Content, IBrowserOriginalDownloadFilesDestination Destination, Guid Operation)> Registrations { get; } = [];
        public List<IBrowserOriginalDownloadFilesRegistration> Revealed { get; } = [];
        public Task? OriginalClose { get; private set; }
        public bool IsIssuedOriginalDestination(IBrowserOriginalDownloadFilesDestination destination) => ReferenceEquals(destination, Destination);
        public bool IsIssuedOriginalRegistration(IBrowserOriginalDownloadFilesRegistration registration) => _issued.Any(actual => ReferenceEquals(actual, registration));
        public async Task<IBrowserOriginalDownloadFilesRegistration?> ReadOriginalRegistrationWithinSourceAsync(IBrowserOriginalDownloadRecordSource source,
            IBrowserOriginalDownloadRecordObservation original, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ObservedRecord = original; Assert.True(source.IsIssuedOriginalDownloadRecord(original));
            var raw = source.RevalidateOriginalDownloadRecordWithinSourceAsync(original, callback => scope(() =>
            { InsideRecordScope?.Invoke(); callback(); AfterRecordScope?.Invoke(); }), task => { retain(task); lock (OriginalRecordStages) OriginalRecordStages.Add(task); }, token);
            retain(raw); await raw; RecordRevalidations++;
            if (!existing) return null; _issued.Add(Existing); return Existing;
        }
        public Task<IBrowserOriginalDownloadFilesDestinationCatalogue> ReadOriginalDestinationsWithinSourceAsync(IBrowserOriginalDownloadContent actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Assert.Same(content, actual); DestinationReads++; return Task.FromResult<IBrowserOriginalDownloadFilesDestinationCatalogue>(new CatalogueDouble([Destination], "Choose Files destination")); }
        public Task RevalidateOriginalDestinationWithinSourceAsync(IBrowserOriginalDownloadFilesDestination actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Assert.Same(Destination, actual); scope(() => token.ThrowIfCancellationRequested()); return Task.CompletedTask; }
        public Task<IBrowserOriginalDownloadFilesRegistration> RegisterOriginalDownloadWithinSourceAsync(IBrowserOriginalDownloadContent actual, IBrowserOriginalDownloadFilesDestination destination,
            Guid operation, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() => token.ThrowIfCancellationRequested()); Registrations.Add((actual, destination, operation));
            var result = new RegistrationDouble(operation, content.OriginalRecord, Approved ? BrowserOriginalDownloadFilesRegistrationState.Registered : BrowserOriginalDownloadFilesRegistrationState.AwaitingApproval,
                Approved ? Guid.NewGuid() : null, Approved ? Guid.NewGuid() : null, Approved ? "Saved in Files" : "Review original Files write");
            if (!ReturnForeign) _issued.Add(result); return Task.FromResult<IBrowserOriginalDownloadFilesRegistration>(result);
        }
        public Task RevealOriginalRegistrationWithinSourceAsync(IBrowserOriginalDownloadFilesRegistration registration, Action<Action> scope, Action<Task> retain, CancellationToken token)
        { Assert.True(IsIssuedOriginalRegistration(registration)); Assert.NotNull(registration.OriginalFilesItemId); scope(() => Revealed.Add(registration)); return Task.CompletedTask; }
        public void RequestRetirement() { } public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }
    private sealed class PhysicalDouble(ContentDouble original) : IBrowserOriginalNativeDownloadPhysicalOwner
    {
        public IBrowserOriginalNativeDownloadTransportSource? OriginalTransportSource => null;
        public Task? OriginalClose { get; private set; }
        public bool IsIssuedOriginalContent(IBrowserOriginalDownloadContent content) => ReferenceEquals(content, original);
        public void BindOriginalTransportSource(IBrowserOriginalNativeDownloadTransportSource source) => throw new NotSupportedException();
        public Task PrepareOriginalTransportPlanWithinSourceAsync(IBrowserOriginalNativeDownloadTransportPlan plan, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task<IBrowserOriginalDownloadContent> FinalizeOriginalDownloadWithinSourceAsync(IBrowserOriginalNativeDownloadTransportPlan plan, IBrowserOriginalNativeDownloadCompletionSource source,
            IBrowserOriginalNativeDownloadCompletion completion, string? contentType, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task RevalidateOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent content, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public Task CopyOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent content, Stream destination, Action<Action> scope, Action<Task> retain, CancellationToken token) => throw new NotSupportedException();
        public void RequestRetirement() { } public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }
    private sealed class LedgerDouble(BrowserDownloadRecord record) : IBrowserAutomationService
    {
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([record]);
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string address, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string reference, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? fileName, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid action, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> RejectAsync(Guid action, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>([]);
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
    }
}
