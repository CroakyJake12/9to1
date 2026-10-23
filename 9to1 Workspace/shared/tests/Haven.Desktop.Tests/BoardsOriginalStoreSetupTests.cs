using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual local Home/Notes, canonical CUI and real owned artifacts. The Linux
/// component fixture is not installed native/ARM or complete Boards acceptance.</summary>
public sealed class BoardsOriginalStoreSetupTests
{
    public static bool UsesLinuxOriginalNotesFixture => OperatingSystem.IsLinux() && BitConverter.IsLittleEndian;

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxOriginalNotesFixture), Skip = "Original Linux Notes boundary; other platform acceptance is separate.")]
    public async Task Shared_CUI_new_user_setup_requires_explicit_identity_and_empty_binding_then_separate_content_review()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://boards"),
            rig.Actor.ProfileId, BrowserTabPrivacy.Standard);
        var factory = BoardsBrowseAppContentFactory.BindOriginalSameProcess(rig.Provider, rig.Home,
            rig.Token, rig.HostToken, () => true);
        var session = Assert.IsType<BoardsBrowseAppContentSession>(factory.CreateSession(request));
        await session.InitializeAsync(rig.Token);
        var view = Assert.IsType<CuiSceneHost>(session.OriginalNativeView);
        var window = new Window { Content = view, Width = 1000, Height = 760 };
        window.Show();
        try
        {
            await IdleAsync(session, view);
            Assert.NotNull(Find(view, "boards-storage-setup"));
            Assert.Null(Find(view, "boards-workspace"));
            Assert.False(Directory.Exists(rig.NotesDirectory));
            Assert.Empty((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);

            Click(view, "boards-storage-create"); await IdleAsync(session, view);
            var identity = await rig.Notes.GetStoreIdentityAsync(rig.Token);
            Assert.True(identity.NewlyCreated); Assert.True(File.Exists(rig.Identity));
            Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("notes", identity.StoreId.ToString("D"), rig.Token));
            Assert.NotNull(Find(view, "boards-storage-bind-empty"));
            Assert.False(Directory.Exists(Path.Combine(rig.NotesDirectory, "Documents")));

            Click(view, "boards-storage-bind-empty");
            var read = await rig.WaitForRequestAsync(HomeNotesResourceAdmissionSource.ReadAction);
            Assert.NotNull(await rig.Home.Ownership.GetVerifiedAsync("notes", identity.StoreId.ToString("D"), rig.Token));
            Assert.Null(Find(view, "boards-workspace"));
            await rig.DecideAsync(read.RequestId, "approval-accept"); await IdleAsync(session, view);
            Assert.NotNull(Find(view, "boards-workspace")); Assert.Null(Find(view, "boards-storage-setup"));

            // Genuine typed Create uses the SAME command as the authored Create button,
            // and still requires its own original save review before durable publication.
            var create = session.DispatchAsync("Create", null, rig.Token).AsTask();
            var save = await rig.WaitForRequestAsync(HomeNotesResourceAdmissionSource.SaveAction);
            await rig.DecideAsync(save.RequestId, "approval-accept"); await create;
            var documents = await rig.Notes.ListAsync(rig.Token);
            var document = Assert.Single(documents);
            var reopened = await rig.Notes.LoadAsync(document.Id, rig.Token);
            Assert.NotNull(reopened); Assert.True(reopened!.Version > 0);
            var artifact = Path.Combine(rig.NotesDirectory, "Documents", document.Id.ToString("D"), "current.haven-notes.json");
            Assert.True(File.Exists(artifact)); Assert.NotEmpty(await File.ReadAllBytesAsync(artifact, rig.Token));
            var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync(rig.Token));
            await preparation.BeginClose();
            Assert.True(session.CanRetireOriginalView);
        }
        finally
        {
            window.Content = null; window.Close();
            if (session.OriginalClose is null) await session.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxOriginalNotesFixture), Skip = "Original Linux Notes boundary; other platform acceptance is separate.")]
    public async Task Existing_revision_material_is_not_empty_bound_and_actual_Home_decline_then_accept_retains_original_import()
    {
        using var root = new PrivateRoot(); await using var rig = await Rig.CreateAsync(root.Path);
        // Explicit pre-existing user artifact fixture; ordinary setup never seeds documents.
        var document = NotesDocument.Create("Computer science revision");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A stack is last in, first out.";
        await rig.Notes.SaveAsync(document, "Existing revision material", rig.Token);
        await rig.Notes.InitialiseOriginalStoreIdentityAsync(rig.Token);
        var artifact = Path.Combine(rig.NotesDirectory, "Documents", document.Id.ToString("D"), "current.haven-notes.json");
        var before = await File.ReadAllBytesAsync(artifact, rig.Token);
        var inspected = await rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.Inspect);
        Assert.False(inspected.IsOwned); Assert.False(inspected.CanBindEmpty); Assert.True(inspected.CanRequestImport);
        var pending = await rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.RequestImport, inspected);
        Assert.True(pending.CanOpenReview); Assert.False(pending.CanCompleteImport);
        var review = rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.OpenReview, pending);
        var request = await rig.WaitForRequestAsync("home.profile.importStore");
        await rig.DecideAsync(request.RequestId, "approval-decline");
        var declined = await review;
        Assert.False(declined.IsOwned); Assert.True(declined.CanRequestImport);
        Assert.Equal(before, await File.ReadAllBytesAsync(artifact, rig.Token));
        Assert.Null(await rig.Home.Ownership.GetVerifiedAsync("notes", (await rig.Notes.GetStoreIdentityAsync(rig.Token)).StoreId.ToString("D"), rig.Token));

        pending = await rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.RequestImport, declined);
        review = rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.OpenReview, pending);
        request = await rig.WaitForRequestAsync("home.profile.importStore");
        await rig.DecideAsync(request.RequestId, "approval-accept");
        var approved = await review;
        Assert.False(approved.IsOwned); Assert.True(approved.CanCompleteImport);
        var imported = await rig.ExecuteAsync(BoardsOriginalStoreSetupOperation.CompleteImport, approved);
        Assert.True(imported.IsOwned); Assert.False(imported.CanCompleteImport);
        Assert.Equal(before, await File.ReadAllBytesAsync(artifact, rig.Token));
        Assert.DoesNotContain((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants,
            grant => grant.Scope.TargetAppId == "boards");
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxOriginalNotesFixture), Skip = "Original Linux Notes boundary; other platform acceptance is separate.")]
    public async Task Restart_after_identity_before_binding_requires_explicit_existing_store_import_and_preserves_UUID()
    {
        using var root = new PrivateRoot(); Guid original;
        await using (var first = await Rig.CreateAsync(root.Path))
        {
            var empty = await first.ExecuteAsync(BoardsOriginalStoreSetupOperation.Inspect);
            Assert.True(empty.CanInitialise);
            var created = await first.ExecuteAsync(BoardsOriginalStoreSetupOperation.InitialiseIdentity, empty);
            Assert.True(created.CanBindEmpty); Assert.False(created.IsOwned);
            original = (await first.Notes.GetStoreIdentityAsync(first.Token)).StoreId;
        }
        var identityFile = Path.Combine(root.Path, "Notes", ".store-identity.json");
        var bytes = await File.ReadAllBytesAsync(identityFile);
        await using (var restarted = await Rig.CreateAsync(root.Path))
        {
            var current = await restarted.ExecuteAsync(BoardsOriginalStoreSetupOperation.Inspect);
            Assert.False(current.CanInitialise); Assert.False(current.CanBindEmpty);
            Assert.True(current.CanRequestImport); Assert.False(current.IsOwned);
            Assert.Equal(original, (await restarted.Notes.GetStoreIdentityAsync(restarted.Token)).StoreId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(identityFile));
            Assert.Empty((await restarted.Home.Permissions.GetSnapshotAsync(cancellationToken: restarted.Token)).PendingRequests);
        }
    }

    private static Control? Find(Control root, string name) => root.GetVisualDescendants().OfType<Control>()
        .Prepend(root).SingleOrDefault(control => control.Name == name);
    private static void Click(Control root, string name)
    {
        var button = Assert.IsType<Button>(Find(root, name)); Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task IdleAsync(BoardsBrowseAppContentSession session, CuiSceneHost view)
    {
        var show = session.OriginalCuiShow ?? throw new InvalidOperationException("Retain actual CUI Show.");
        await view.CaptureOriginalActionSettlement(show);
    }

    private sealed class PrivateRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "9to1-boards-real-setup-" + Guid.NewGuid().ToString("N"));
        internal PrivateRoot() => Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
    private sealed class Resolver(Func<HomeNotesResourceAdmissionSource> original) : ICanonicalResourceAccessResolver, IOriginalScopedCanonicalResourceAccessResolver
    {
        public string ResourceKind => HomeNotesResourceAdmissionSource.ScopeKind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope scope, CancellationToken token) => original().EvaluateAsync(actor, actionId, scope, token);
        public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            original().EvaluateWithinOriginalSourceAsync(actor, actionId, resource, scope, retain, token);
    }
    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(30));
        private readonly CancellationTokenSource _host = new(TimeSpan.FromSeconds(30));
        private readonly ProductionDiagnostics _diagnostics;
        internal CancellationToken Token => _deadline.Token;
        internal CancellationToken HostToken => _host.Token;
        internal HomeNativeWindowsComposition Home { get; }
        internal NotesRepository Notes { get; }
        internal HomeNativeApprovalWindowOwner Approvals { get; }
        internal BoardsOriginalStoreSetupBackend Backend { get; }
        internal ServiceProvider Provider { get; }
        internal AuthenticatedResourceActor Actor { get; private set; } = null!;
        internal string NotesDirectory { get; }
        internal string Identity => Path.Combine(NotesDirectory, ".store-identity.json");
        private Rig(string root)
        {
            var paths = new Paths(root); NotesDirectory = Path.Combine(root, "Notes");
            _diagnostics = new(paths); Notes = new(paths, new NotesDocumentValidator(), _diagnostics);
            var evidence = new NotesLocalStoreEvidenceProvider(Notes);
            var principal = new OperatingSystemPrincipalSource(); HomeNotesResourceAdmissionSource? admission = null;
            Home = new(new FileHomeCoreStateStore(Path.Combine(root, "Home", "state.json")), principal, paths,
                new("9to1.home.boards.setup-component.v1"), originalResourceResolvers: [new Resolver(() => admission!)],
                originalActionPolicies: [new HomeNotesActionPolicies()],
                configureOriginalStores: _ => new([evidence], new Dictionary<Type, object>
                { [typeof(NotesRepository)] = Notes, [typeof(NotesLocalStoreEvidenceProvider)] = evidence }),
                configureOriginalOwners: owners =>
                {
                    admission = new(Notes, owners.StateStore, owners.Profiles, owners.Ownership, owners.Resources, owners.Permissions, owners.Broker);
                    return new Dictionary<Type, object> { [typeof(HomeNotesResourceAdmissionSource)] = admission };
                });
            Approvals = new(Home.Runtime, Home.Profiles, Home.Permissions);
            Backend = new(Notes, evidence, Home, Approvals, Token);
            var collection = new ServiceCollection(); collection.AddSingleton<IAppPaths>(paths);
            collection.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
            collection.AddHavenOwnedWindowsHomeDomain(Home, paths, principal);
            collection.AddSingleton(Notes); collection.AddSingleton(new VerifiedNotesRepository(Notes, paths, _diagnostics));
            collection.AddSingleton(admission!); collection.AddSingleton(Approvals); collection.AddSingleton(evidence); collection.AddSingleton(Backend);
            Provider = collection.BuildServiceProvider();
        }
        internal static async Task<Rig> CreateAsync(string root)
        {
            var rig = new Rig(root);
            try
            {
                await rig.Home.StartOriginalAsync();
                rig.Actor = await rig.Home.Profiles.GetCurrentAsync(rig.Token) ?? throw new InvalidOperationException("Actual local Home profile unavailable.");
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal Task<BoardsOriginalStoreSetupBackend.Snapshot> ExecuteAsync(BoardsOriginalStoreSetupOperation operation,
            BoardsOriginalStoreSetupBackend.Snapshot? expected = null) =>
            Backend.ExecuteWithinOriginalSourceAsync(operation, expected, body => body(), _ => { }, Token);
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> WaitForRequestAsync(string action)
        {
            while (true)
            {
                Token.ThrowIfCancellationRequested();
                var current = await Home.Permissions.GetSnapshotAsync(cancellationToken: Token);
                var request = current.PendingRequests.SingleOrDefault(item => item.Scope.ActionName == action);
                if (request is not null) return request;
                await Task.Delay(10, Token);
            }
        }
        internal async Task DecideAsync(string requestId, string button)
        {
            await Approvals.OpenOriginalAsync(requestId, Token);
            var window = Approvals.OriginalWindow ?? throw new InvalidOperationException("Actual Home review window unavailable.");
            Click(window, button);
        }
        public async ValueTask DisposeAsync()
        {
            _host.Cancel(); _deadline.Cancel();
            var failures = new List<Exception>();
            async Task Join(Func<Task> action)
            {
                try { await action(); }
                catch (Exception error) { failures.Add(error); }
            }
            await Join(Backend.CloseAndDrainAsync);
            await Join(Approvals.CloseAndDrainAsync);
            await Join(Home.CloseAndDrainAsync);
            await Join(() => Provider.DisposeAsync().AsTask());
            await Join(() => _diagnostics.DisposeAsync().AsTask());
            _host.Dispose(); _deadline.Dispose();
            if (failures.Count != 0) throw new AggregateException("Actual setup fixture owners did not drain cleanly.", failures);
        }
    }
}
