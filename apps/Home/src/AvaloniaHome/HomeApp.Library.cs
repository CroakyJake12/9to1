using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files.NativeHost;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;

namespace AvaloniaHome;

internal sealed partial class HomeApp
{
    private sealed record StandaloneLibraryComposition(HomeNativeWindowsComposition Home, ProductionDiagnostics Diagnostics);
    private ProductionDiagnostics? _ownedLibraryDiagnostics;
    private IHomeOriginalLibrarySource? _originalLibrarySource;
    private NativeHomeFilesLibrarySource? _originalFilesLibraryIndex;
    private HomeLibraryService? _homeLibrary;
    private HomeNativeApprovalWindowOwner? _libraryApprovals;
    private IHomeDeepLinkRouter? _originalLibraryRouter;
    private TextBox? _originalLibrarySearch;
    private HomeLibraryQuery _originalLibraryQuery = new(Limit: 25);
    private Task? _libraryApprovalClose, _libraryClose;
    private bool _libraryRetiring;
    private string _libraryStatus = "Browse authorised references from your apps.";

    // Retain the existing standalone Home state/endpoint/artifact roots and existing native
    // preference reader. Notes uses the SAME maintained AppPaths as Haven. Registration never
    // creates/imports a Notes store, claims ownership, approves a read or moves saved content.
    private static StandaloneLibraryComposition CreateOriginalLibraryCandidate()
    {
        var homePaths = new OriginalLibraryCandidatePaths();
        var notesPaths = new AppPaths();
        var diagnostics = new ProductionDiagnostics(notesPaths);
        try
        {
            var notes = new NotesRepository(notesPaths, new NotesDocumentValidator(), diagnostics);
            var verified = new VerifiedNotesRepository(notes, notesPaths, diagnostics);
            var evidence = new NotesLocalStoreEvidenceProvider(notes);
            HomeNotesResourceAdmissionSource? admission = null;
            NativeFilesWorkspaceService? files = null;
            NativeFilesWorkspaceAuthority? filesAuthority = null;
            FilesArtifactResourceResolver? filesResolver = null;
            var resolver = new OriginalLibraryNotesResolver(() => admission ??
                throw new InvalidOperationException("The original standalone Notes admission is not captured."));
            var home = new HomeNativeWindowsComposition(FileHomeCoreStateStore.CreateDefault(),
                new OperatingSystemPrincipalSource(), homePaths, new("9to1.home.candidate.v1"),
                originalResourceResolvers: [resolver],
                originalActionPolicies: [new HomeNotesActionPolicies(), new FilesNativeBrowserActionPolicySource()],
                configureOriginalStores: identity =>
                {
                    files = new(identity.StateStore, identity.Profiles);
                    return new([evidence, files], new Dictionary<Type, object>
                    {
                        [typeof(NotesRepository)] = notes, [typeof(VerifiedNotesRepository)] = verified,
                        [typeof(NotesLocalStoreEvidenceProvider)] = evidence,
                        [typeof(NativeFilesWorkspaceService)] = files
                    });
                },
                configureOriginalResolvers: ownership =>
                {
                    filesAuthority = new(files!, ownership.Identity.Profiles, ownership.Ownership);
                    filesResolver = new(filesAuthority);
                    return new([filesResolver], new Dictionary<Type, object>
                    {
                        [typeof(NativeFilesWorkspaceAuthority)] = filesAuthority,
                        [typeof(FilesArtifactResourceResolver)] = filesResolver
                    });
                },
                configureOriginalOwners: owners =>
                {
                    admission = new(notes, owners.StateStore, owners.Profiles, owners.Ownership,
                        owners.Resources, owners.Permissions, owners.Broker);
                    return new Dictionary<Type, object> { [typeof(HomeNotesResourceAdmissionSource)] = admission };
                },
                configuredNativePreferences: HomeNativePreferenceFile.CaptureExistingReaderBinding(homePaths.DataDirectory));
            return new(home, diagnostics);
        }
        catch (Exception primary)
        {
            try { diagnostics.DisposeAsync().GetAwaiter().GetResult(); }
            catch (Exception cleanup) { throw new AggregateException("Original Library composition and diagnostics cleanup failed.", primary, cleanup); }
            throw;
        }
    }
    private sealed class OriginalLibraryCandidatePaths : IAppPaths
    {
        internal OriginalLibraryCandidatePaths()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("The OS did not provide Home's existing local data root.");
            DataDirectory = Path.Combine(local, "9to1");
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "state.json");
    }
    private sealed class OriginalLibraryNotesResolver(Func<HomeNotesResourceAdmissionSource> original)
        : ICanonicalResourceAccessResolver, IOriginalScopedCanonicalResourceAccessResolver
    {
        public string ResourceKind => HomeNotesResourceAdmissionSource.ScopeKind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope scope, CancellationToken token) => original().EvaluateAsync(actor, actionId, scope, token);
        public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string actionId,
            ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            original().EvaluateWithinOriginalSourceAsync(actor, actionId, resource, scope, retain, token);
    }
    private void InitializeOriginalLibrary(AuthenticatedResourceActor actor)
    {
        _originalLibraryActor = actor; // Full original principal/profile/authentication revision.
        var members = new List<NativeCompositeHomeLibrarySource.Member>();
        var notes = _nativeComposition.Services.GetService(typeof(NotesRepository)) as NotesRepository;
        var verified = _nativeComposition.Services.GetService(typeof(VerifiedNotesRepository)) as VerifiedNotesRepository;
        var admission = _nativeComposition.Services.GetService(typeof(HomeNotesResourceAdmissionSource)) as HomeNotesResourceAdmissionSource;
        if (notes is not null && verified is not null && admission is not null)
        {
            _libraryApprovals = new(_homeCore, _nativeComposition.Profiles, _nativeComposition.Permissions);
            members.Add(new("boards", "Boards", new NativeHomeArtifactLibrarySource(_nativeComposition,
                notes, verified, admission, actor, "standalone-home-library:" + Guid.NewGuid().ToString("D"),
                PresentOriginalLibraryReviewAsync,
                token => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }),
                OriginalLibraryHomeIsAlive)));
        }
        if (BindOriginalLibraryFiles())
        {
            _originalFilesLibraryIndex = new NativeHomeFilesLibrarySource(_originalLibraryFilesBrowser!, actor,
                _nativeComposition.StateStore, ObserveOriginalLibraryOwnerAsync,
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; }),
                OriginalLibraryHomeIsAlive);
            members.Add(new("files", "Files", _originalFilesLibraryIndex));
        }
        if (members.Count == 0)
        {
            _libraryStatus = "The original local Library owners are unavailable in this Home composition.";
            return;
        }
        var borrowedRouter = _nativeComposition.Services.GetService(typeof(IHomeDeepLinkRouter)) as IHomeDeepLinkRouter;
        _originalLibraryRouter = _originalLibraryFilesBrowser is not null || borrowedRouter is not null
            ? new StandaloneLibraryAppRouter(this, borrowedRouter) : null;
        _originalLibrarySource = new NativeCompositeHomeLibrarySource(_nativeComposition.StateStore, members.ToArray());
        _homeLibrary = _originalLibrarySource.CreateService(new OriginalLibraryRouter(this));
    }
    private async Task PresentOriginalLibraryReviewAsync(HomePermissionAuthorization authorization, CancellationToken token)
    {
        var owner = _libraryApprovals ?? throw new InvalidOperationException("The actual Home Library review owner is unavailable.");
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(
                () => owner.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token)).GetTask());
            var actual = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            var decision = await originals.AwaitAsync(actual).ConfigureAwait(false);
            if (decision.RequestId != authorization.RequestId)
                throw new InvalidDataException("The Library review observation belongs to a different original request.");
            return true; // The actual Notes Begin still checks the broker decision; no grant here.
        }).ConfigureAwait(false);
    }
    private void RegisterOriginalLibraryActions()
    {
        RegisterWorkspaceAction("NavigateLibrary", () => DispatchWorkspaceAsync("NavigateLibrary"));
        RegisterWorkspaceAction("SearchLibrary", () => RefreshOriginalLibraryAsync());
        RegisterWorkspaceAction("RefreshLibrary", () => RefreshOriginalLibraryAsync());
        RegisterWorkspaceAction("NextLibraryPage", () => RefreshOriginalLibraryAsync(_originalLibraryQuery.Offset + _originalLibraryQuery.Limit));
        RegisterWorkspaceAction("PreviousLibraryPage", () => RefreshOriginalLibraryAsync(Math.Max(0, _originalLibraryQuery.Offset - _originalLibraryQuery.Limit)));
        _viewModel.On("OpenHomeLibraryArtifact", parameter => _ = RunOriginalNativeWork(() => RunWorkspaceActionAsync(
            () => OpenOriginalLibraryArtifactAsync(parameter))));
        ApplyOriginalLibraryBindings();
    }
    private void RegisterOriginalLibraryControls(CuiControlLoader loader)
    { loader.BindAuthoredControl("library-search", _ => _originalLibrarySearch = new TextBox()); }
    private async Task RefreshOriginalLibraryAsync(int offset = 0)
    {
        try
        {
    
            if (_libraryRetiring) return;
            var library = _homeLibrary;
            if (library is null)
            {
                _controller.Surface.ApplyLibraryPage(new([], 0, 25, false, "LibraryOwnerUnavailable", _libraryStatus));
                return;
            }
            await _originalLibrarySource!.RevalidateAsync(CancellationToken.None);
            var query = _originalLibraryQuery with { Offset = offset,
                Text = string.IsNullOrWhiteSpace(_originalLibrarySearch?.Text) ? null : _originalLibrarySearch.Text };
            var page = await library.SearchAsync(query);
            await _originalLibrarySource.RevalidateAsync(CancellationToken.None);
            _originalLibraryQuery = query;
            _controller.Surface.ApplyLibraryPage(page);
            _libraryStatus = page.Message;
            if (page.Succeeded && !string.IsNullOrWhiteSpace(_originalLibrarySource.AvailabilityNotice))
                _libraryStatus += " " + _originalLibrarySource.AvailabilityNotice;
            if (_originalLibraryRouter is null && page.Succeeded)
                _libraryStatus += " Owning-app opening is unavailable until this standalone host has its real app router.";
        }
        catch (Exception cause) when (_originalLibrarySource?.IsExpectedRefusal(cause) == true)
        {
            _libraryStatus = cause.Message;
            _controller.Surface.ApplyLibraryPage(new([], 0, 25, false, "LibraryUnavailable", cause.Message));
        }
    }
    private async Task OpenOriginalLibraryArtifactAsync(object? parameter)
    {
        var selected = _controller.Surface.LibraryItems.SingleOrDefault(item => ReferenceEquals(item.OpenLink, parameter));
        if (_libraryRetiring || _homeLibrary is null || _originalLibraryRouter is null || selected is null)
            throw new UnauthorizedAccessException("Choose a current Library artifact in a host with its actual owning-app router.");
        var opened = await _homeLibrary.OpenAsync(selected.OwnerApp, selected.ArtifactId);
        await _originalLibrarySource!.RevalidateAsync(CancellationToken.None);
        _libraryStatus = opened.Message;
        if (!opened.Succeeded) _controller.Surface.ApplyLibraryPage(new([], 0, 25, false, opened.Code, opened.Message));
    }
    private void ApplyOriginalLibraryBindings()
    {
        var page = _controller.Surface.LibraryPage;
        var available = !_libraryRetiring && _workspaceInitialized && !_workspaceBusy && _controller.Surface.IsLibrary;
        _viewModel.Set("LibraryRows", NativeHomeArtifactLibrarySource.ProjectRows(page?.Succeeded == true ? page : null));
        _viewModel.Set("LibrarySummary", _libraryStatus);
        _viewModel.Set("CanSearchLibrary", available && _homeLibrary is not null);
        _viewModel.Set("CanOpenHomeLibraryArtifact", available && _originalLibraryRouter is not null && page?.Succeeded == true);
        _viewModel.Set("CanNextLibraryPage", available && page?.HasMore == true);
        _viewModel.Set("CanPreviousLibraryPage", available && _originalLibraryQuery.Offset > 0);
        _viewModel.Set("LibraryPageSummary", page?.Items.Count > 0
            ? $"{_originalLibraryQuery.Offset + 1}–{_originalLibraryQuery.Offset + page.Items.Count}" : "No matching authorised artifacts.");
        _viewModel.SetActionAvailability("OpenHomeLibraryArtifact", available && _originalLibraryRouter is not null && page?.Succeeded == true);
    }
    private sealed class OriginalLibraryRouter(HomeApp owner) : IHomeDeepLinkRouter
    {
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken token = default) =>
            owner._originalLibraryRouter?.OpenAsync(link, token) ?? Task.FromResult(new HomeFeatureNavigationResult(false,
                "OwningAppUnavailable", "The actual owning-app router is unavailable in this standalone host.",
                new(link.TargetRouteId!, link.OwnerApp, link.ObjectId, link.Action)));
    }
    private void PrepareOriginalLibraryRetirement()
    {
        _libraryRetiring = true;
        _libraryApprovalClose ??= _libraryApprovals?.CloseAndDrainAsync();
    }
    private Task CloseOriginalLibraryAsync() => _libraryClose ??= CloseLibraryCoreAsync();
    private async Task CloseLibraryCoreAsync()
    {
        var failures = new List<Exception>();
        try { await CloseOriginalLibraryFilesAsync().ConfigureAwait(false); }
        catch (Exception cause) { failures.Add(cause); }
        try { if (_originalLibrarySource is not null) await _originalLibrarySource.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cause) { failures.Add(cause); }
        try { if (_libraryApprovalClose is not null) await _libraryApprovalClose.ConfigureAwait(false); }
        catch (Exception cause) { failures.Add(cause); }
        if (failures.Count != 0) throw new AggregateException("Original standalone Library sources failed during close.", failures);
    }
}
