using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using NativeMailPage = Haven.Desktop.Views.Pages.Mail.MailPage;
using Haven.Desktop.Views.Pages.Mail;

namespace Haven.Desktop.Tests;

/// <summary>Actual Files/Home bytes and native picker; mailbox rows are controlled UI metadata, not transport or Send authority.</summary>
public sealed class MailCanonicalFilesAttachmentTests
{
    [AvaloniaFact]
    public async Task Actual_native_Files_picker_attaches_original_bytes_and_physical_draft_reopen_retains_distinct_attachment_identity()
    {
        var ct = TestContext.Current.CancellationToken; using var f = await Fixture.CreateAsync(ct);
        var service = f.CreateMail(); var proxy = (Mailbox)service;
        var picker = f.CreatePicker(); using var page = new NativeMailPage(service, Models(), null, picker);
        var window = new Window { Content = page, Width = 1000, Height = 760 }; window.Show();
        try
        {
            var vm = Assert.IsType<MailPageViewModel>(page.DataContext); await vm.Initialization;
            NewDraft(vm, "Original native attachment");
            var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
            ClickAttach(page); await Until(() => picker.ActiveSession?.Window.IsVisible == true, ct);
            var session = picker.ActiveSession!; await SelectPhysicalFile(session, ct);
            var nativeAttach = session.Window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "mail-files-attach");
            nativeAttach.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.PendingAttachmentSelection;
            var actual = Assert.Single(vm.ComposeAttachments);
            Assert.Equal("attachment.txt", actual.FileName); Assert.Equal("text/plain", actual.ContentType); Assert.Equal(f.Bytes, actual.Content);
            Assert.NotEqual(Guid.Empty, actual.LocalId); Assert.NotEqual(f.File.Value, actual.LocalId);
            await vm.SaveDraftCommand.ExecuteAsync();
            var persisted = Assert.Single(await new FileMailDraftStore(f.Paths).GetAllAsync(ct));
            Assert.Equal(proxy.AccountA.AccountId, persisted.AccountId); Assert.Equal("Original native attachment", persisted.Subject);
            var attachment = Assert.Single(persisted.Attachments);
            Assert.Equal(actual.LocalId, attachment.LocalId); Assert.Equal(f.Bytes, attachment.Content);
            using var reopened = new NativeMailPage(f.CreateMail(), Models(), null, f.CreatePicker());
            var restored = Assert.IsType<MailPageViewModel>(reopened.DataContext); await restored.Initialization;
            Assert.True(restored.IsComposeOpen); Assert.Equal(actual.LocalId, Assert.Single(restored.ComposeAttachments).LocalId);
            Assert.Equal(f.Bytes, restored.ComposeAttachments[0].Content);
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
            Assert.Equal(0, proxy.SendCalls);
        }
        finally { page.Dispose(); try { try { await page.PendingAttachmentSelection; } catch { } } finally { window.Close(); } }
    }

    [AvaloniaTheory]
    [InlineData("new-draft")]
    [InlineData("account-aba")]
    [InlineData("dispose")]
    [InlineData("selection-change")]
    public async Task Actual_queued_Files_metadata_read_cannot_attach_to_replacement_compose_or_retired_page(string retirement)
    {
        var ct = TestContext.Current.CancellationToken; using var f = await Fixture.CreateAsync(ct);
        var service = f.CreateMail(); var proxy = (Mailbox)service; var picker = f.CreatePicker();
        using var page = new NativeMailPage(service, Models(), null, picker);
        var window = new Window { Content = page, Width = 1000, Height = 760 }; window.Show();
        SemaphoreSlim? gate = null; var held = false; Task? pending = null;
        try
        {
            var vm = Assert.IsType<MailPageViewModel>(page.DataContext); await vm.Initialization; NewDraft(vm, "Original");
            ClickAttach(page); await Until(() => picker.ActiveSession?.Window.IsVisible == true, ct);
            var session = picker.ActiveSession!; await SelectPhysicalFile(session, ct);
            var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
            gate = ActualMetadataGate(f.DriveFile); await gate.WaitAsync(ct); held = true;
            var queue = typeof(SemaphoreSlim).GetField("m_asyncHead", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Actual Files metadata queue observer is unavailable.");
            Assert.Null(queue.GetValue(gate));
            session.Window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "mail-files-attach")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending = session.PendingOperation; await Until(() => queue.GetValue(gate) is not null, ct); Assert.False(pending.IsCompleted);
            if (retirement == "new-draft") NewDraft(vm, "Replacement");
            else if (retirement == "account-aba")
            { vm.SelectedAccount = proxy.AccountB; vm.SelectedAccount = proxy.AccountA; NewDraft(vm, "Replacement"); }
            else if (retirement == "selection-change")
            {
                session.Window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "mail-files-row").SelectedIndex = -1;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            else page.Dispose();
            gate.Release(); held = false;
            await pending;
            if (retirement == "selection-change") session.Window.Close();
            await page.PendingAttachmentSelection;
            Assert.Empty(vm.ComposeAttachments); Assert.Null(picker.ActiveSession);
            if (retirement != "dispose") { Assert.True(vm.IsComposeOpen); Assert.Equal(retirement == "selection-change" ? "Original" : "Replacement", vm.ComposeSubject); }
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
            Assert.Empty(await new FileMailDraftStore(f.Paths).GetAllAsync(ct)); Assert.Equal(0, proxy.SendCalls);
        }
        finally
        {
            if (held) gate!.Release(); page.Dispose();
            try { if (pending is not null) try { await pending; } catch { } try { await page.PendingAttachmentSelection; } catch { } }
            finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("new-draft")]
    [InlineData("account-aba")]
    [InlineData("dispose")]
    public async Task Actual_completed_physical_draft_read_cannot_restore_over_new_compose_account_cycle_or_disposal(string retirement)
    {
        var ct = TestContext.Current.CancellationToken; using var f = await Fixture.CreateAsync(ct);
        var service = f.CreateMail(); var proxy = (Mailbox)service; proxy.HoldDrafts = true;
        var saved = new Haven.Application.MailDraft(proxy.AccountA.AccountId, null, MailResponseKind.New, null, null,
            [new("recipient", "recipient@example.test")], [], [], "Saved original", "body", false, [],
            LocalId: Guid.NewGuid(), Provider: CalendarProviderKind.Google, UpdatedAt: DateTimeOffset.UtcNow);
        await f.Drafts.UpsertAsync(saved, ct);
        using var page = new NativeMailPage(service, Models()); var vm = Assert.IsType<MailPageViewModel>(page.DataContext);
        try
        {
            await proxy.DraftReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(f.Paths.DataDirectory, "MailDrafts", saved.LocalId.ToString("N") + ".json"), ct);
            if (retirement == "new-draft") NewDraft(vm, "Replacement");
            else if (retirement == "account-aba")
            { vm.SelectedAccount = proxy.AccountB; vm.SelectedAccount = proxy.AccountA; }
            else page.Dispose();
            proxy.ReleaseDraftRead.TrySetResult(); await vm.Initialization;
            if (retirement == "new-draft") { Assert.True(vm.IsComposeOpen); Assert.Equal("Replacement", vm.ComposeSubject); }
            else Assert.False(vm.IsComposeOpen);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(f.Paths.DataDirectory, "MailDrafts", saved.LocalId.ToString("N") + ".json"), ct));
            Assert.Equal(saved.LocalId, Assert.Single(await new FileMailDraftStore(f.Paths).GetAllAsync(ct)).LocalId);
            Assert.Equal(0, proxy.SendCalls);
        }
        finally { proxy.ReleaseDraftRead.TrySetResult(); try { await vm.Initialization; } catch { } page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Copied_canonical_page_or_row_cannot_borrow_original_attachment_bytes()
    {
        var ct = TestContext.Current.CancellationToken; using var f = await Fixture.CreateAsync(ct);
        var browser = f.Graph.GetRequiredService<FilesNativeBrowserService>();
        var page = await browser.ListAsync(f.Workspace.Actor, token: ct); var row = Assert.Single(page.Items, x => x.Id == f.File);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ReadOriginalMailAttachmentSelectionAsync(page with { }, row,
            f.Workspace.Actor, f.Owner, Guid.NewGuid(), Guid.NewGuid(), () => true, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ReadOriginalMailAttachmentSelectionAsync(page, row with { },
            f.Workspace.Actor, f.Owner, Guid.NewGuid(), Guid.NewGuid(), () => true, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [AvaloniaFact]
    public async Task Actual_revised_file_cannot_rebind_original_displayed_attachment_row()
    {
        var ct = TestContext.Current.CancellationToken; using var f = await Fixture.CreateAsync(ct);
        var browser = f.Graph.GetRequiredService<FilesNativeBrowserService>();
        var page = await browser.ListAsync(f.Workspace.Actor, token: ct); var row = Assert.Single(page.Items, x => x.Id == f.File);
        var bytes = Encoding.UTF8.GetBytes("Acknowledged replacement revision, never an old displayed attachment.\n");
        var local = Path.Combine(f.Workspace.Configuration.RootDirectory, "attachment.txt");
        await File.WriteAllBytesAsync(local, bytes, ct);
        var revision = new FilesRevisionId(Guid.NewGuid()); var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.True((await f.Workspace.Provider.CommitUploadedContentAsync(new(f.File, null, "attachment.txt", "text/plain", revision,
            row.CurrentRevisionId, "local-profile:" + f.Workspace.Actor.ProfileId, DateTimeOffset.UtcNow, bytes.Length, hash, "attachment.txt"), ct)).IsSuccess);
        await f.Workspace.Materializations.RegisterValidatedAsync(local, new(f.File, revision, hash, bytes.Length, DateTimeOffset.UtcNow), SyncAvailability.AvailableOffline, ct);
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ReadOriginalMailAttachmentSelectionAsync(page, row,
            f.Workspace.Actor, f.Owner, Guid.NewGuid(), Guid.NewGuid(), () => true, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(local, ct));
        var fresh = await browser.ListAsync(f.Workspace.Actor, token: ct); var freshRow = Assert.Single(fresh.Items, x => x.Id == f.File);
        var read = await browser.ReadOriginalMailAttachmentSelectionAsync(fresh, freshRow, f.Workspace.Actor, f.Owner,
            Guid.NewGuid(), Guid.NewGuid(), () => true, ct);
        Assert.Equal(bytes, read.Bytes.ToArray());
    }

    private static void NewDraft(MailPageViewModel vm, string subject)
    { vm.ComposeCommand.Execute(null); vm.ComposeTo = "recipient@example.test"; vm.ComposeSubject = subject; vm.ComposeBody = "Original body"; }
    private static void ClickAttach(NativeMailPage page) => page.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Attach"))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task SelectPhysicalFile(FilesMailCanonicalAttachmentPicker.Session session, CancellationToken ct)
    {
        var choice = session.Window.GetVisualDescendants().OfType<ComboBox>().Single(x => x.Name == "mail-files-row");
        var rows = choice.Items.Cast<string>().ToArray(); var selected = Array.FindIndex(rows, x => x.EndsWith("— attachment.txt", StringComparison.Ordinal));
        Assert.True(selected >= 0); choice.SelectedIndex = selected;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.True(session.IsActionAvailable("Attach"));
    }
    private static async Task Until(Func<bool> check, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!check()) { if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The actual native operation did not reach its expected state."); await Task.Delay(10, ct); }
    }
    private static SemaphoreSlim ActualMetadataGate(string path)
    {
        var type = typeof(DurableDriveProvider).Assembly.GetType("HavenOS.Files.FilesStatePathLocks", true)!;
        return Assert.IsType<SemaphoreSlim>(type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [Path.GetFullPath(path)]));
    }
    private static IProviderModelClient Models() => DispatchProxy.Create<IProviderModelClient, UnusedModels>();
    public class UnusedModels : DispatchProxy
    { protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Manual attachment selection does not use an AI model."); }

    public class Mailbox : DispatchProxy
    {
        public FileMailDraftStore Drafts { get; set; } = null!;
        public Haven.Application.MailAccount AccountA { get; set; } = null!;
        public Haven.Application.MailAccount AccountB { get; set; } = null!;
        public bool HoldDrafts; public int SendCalls;
        public TaskCompletionSource DraftReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDraftRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            return method?.Name switch
            {
                nameof(IMailService.GetAccountsAsync) => Task.FromResult<IReadOnlyList<Haven.Application.MailAccount>>([AccountA, AccountB]),
                nameof(IMailService.CheckAccessAsync) => Task.FromResult(new MailOperationResult(false, "No remote transport is requested by this native Files fixture.", MailFailureKind.PermissionDenied)),
                nameof(IMailService.GetDraftsAsync) => ReadDrafts((CancellationToken)args![0]!),
                nameof(IMailService.SaveDraftAsync) => Save((Haven.Application.MailDraft)args![0]!, (CancellationToken)args[1]!),
                nameof(IMailService.SendAsync) => RefuseSend(),
                _ => throw new InvalidOperationException("Unexpected mailbox action in a manual canonical Files attachment fixture: " + method?.Name)
            };
        }
        private async Task<IReadOnlyList<Haven.Application.MailDraft>> ReadDrafts(CancellationToken ct)
        {
            var actual = await Drafts.GetAllAsync(ct);
            if (HoldDrafts) { DraftReadEntered.TrySetResult(); await ReleaseDraftRead.Task; }
            return actual;
        }
        private async Task<MailOperationResult> Save(Haven.Application.MailDraft draft, CancellationToken ct)
        { await Drafts.UpsertAsync(draft, ct); return new(true, "Saved actual local draft.", LocalDraftId: draft.LocalId); }
        private Task<MailOperationResult> RefuseSend() { SendCalls++; throw new InvalidOperationException("Files read permission never grants Mail send authority."); }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-native-mail-files-" + Guid.NewGuid().ToString("N"));
        internal string HomeFile => Path.Combine(Root, "home.json");
        internal string DriveFile => Path.Combine(Workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
        internal byte[] Bytes { get; } = Encoding.UTF8.GetBytes("Native canonical Mail attachment — exact bytes.\n");
        internal HostedItemId File { get; } = HostedItemId.New();
        internal ServiceProvider Graph { get; private set; } = null!;
        internal NativeFilesWorkspace Workspace { get; private set; } = null!;
        internal FilesArtifactResourceResolver Owner => Graph.GetRequiredService<FilesArtifactResourceResolver>();
        internal IAppPaths Paths { get; private set; } = null!;
        internal FileMailDraftStore Drafts { get; private set; } = null!;
        internal FilesMailCanonicalAttachmentPicker CreatePicker() => new(Graph.GetRequiredService<FilesNativeBrowserService>(), Owner, Graph.GetRequiredService<IAuthenticatedResourceActorSource>());
        private readonly Haven.Application.MailAccount _accountA = new(Guid.NewGuid(), CalendarProviderKind.Google, "Original", "original@example.test", MailProviderCapabilities.Drafts | MailProviderCapabilities.Attachments);
        private readonly Haven.Application.MailAccount _accountB = new(Guid.NewGuid(), CalendarProviderKind.Google, "Replacement", "replacement@example.test", MailProviderCapabilities.Drafts | MailProviderCapabilities.Attachments);
        internal IMailService CreateMail()
        { var service = DispatchProxy.Create<IMailService, Mailbox>(); var proxy = (Mailbox)service;
            proxy.Drafts = Drafts; proxy.AccountA = _accountA; proxy.AccountB = _accountB; return service; }
        internal static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var f = new Fixture(); Directory.CreateDirectory(f.Root);
            try
            {
                var home = new FileHomeCoreStateStore(f.HomeFile); var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection(); services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>(); services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>(); services.AddSingleton<ResourceAuthorizationService>();
                services.AddFilesNativeHost(); f.Graph = services.BuildServiceProvider();
                var chosen = Path.Combine(f.Root, "chosen"); Directory.CreateDirectory(chosen);
                var configured = await f.Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                    f.Graph.GetRequiredService<HomeLocalStoreOwnership>(), ct);
                f.Workspace = await f.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(configured.Configuration.StoreId, ct)
                    ?? throw new InvalidOperationException("Actual configured Files workspace required.");
                Assert.Same(f.Owner, Assert.Single(f.Graph.GetServices<ICanonicalResourceAccessResolver>()));
                var local = Path.Combine(chosen, "attachment.txt"); await System.IO.File.WriteAllBytesAsync(local, f.Bytes, ct);
                var revision = new FilesRevisionId(Guid.NewGuid()); var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(f.Bytes)).ToLowerInvariant();
                Assert.True((await f.Workspace.Provider.CommitUploadedContentAsync(new(f.File, null, "attachment.txt", "text/plain", revision,
                    null, "local-profile:" + f.Workspace.Actor.ProfileId, DateTimeOffset.UtcNow, f.Bytes.Length, hash, "attachment.txt"), ct)).IsSuccess);
                await f.Workspace.Materializations.RegisterValidatedAsync(local, new(f.File, revision, hash, f.Bytes.Length, DateTimeOffset.UtcNow), SyncAvailability.AvailableOffline, ct);
                f.Paths = new AppPaths(f.Root); f.Drafts = new(f.Paths); return f;
            }
            catch { f.Dispose(); throw; }
        }
        public void Dispose() { Graph?.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class AppPaths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
