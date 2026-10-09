using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    private NativeFilesWorkspaceAuthority OriginalBrowserWorkspaces => workspaces;
    private IAuthenticatedResourceActorSource OriginalBrowserActors => actors;
    private ResourceAuthorizationService OriginalBrowserResources => resources;

    public async Task<Func<CancellationToken, ValueTask<bool>>> CaptureOriginalBrowserDownloadPageReadCheckWithinSourceAsync(
        FilesNativeBrowserPage samePage, AuthenticatedResourceActor actualActor, Func<bool> originalLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        if (!source.Invoke(() => _originalPages.TryGetValue(samePage, out var original) && original.Actor == actualActor &&
            original.Workspace.Configuration.StoreId == samePage.StoreID))
            throw new UnauthorizedAccessException("The same privately issued Files page and actual actor are required.");
        var check = await source.Observe(() => CaptureOriginalPageReadCheckAsync(samePage, actualActor,
            originalLifetime, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => check(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files page read retired before publication.");
        return check; // Later surface checks belong to that actual Files surface's source cohort.
    }

    public BrowserDownloadFilesOwner CreateOriginalBrowserDownloadFilesOwner(
        IBrowserOriginalNativeDownloadPhysicalOwner actualContentOwner,
        IBrowserOriginalDownloadFilesPhysicalOwner samePhysicalFilesOwner,
        FilesArtifactResourceResolver actualReadIssuer,
        IFilesOriginalBrowserDownloadNavigator? actualNavigator = null)
    {
        if (!ReferenceEquals(actualContentOwner, samePhysicalFilesOwner) || _mutationBroker is null)
            throw new NotSupportedException("The same physical content/destination owner and actual Home Files broker are required.");
        return new(this, actualContentOwner, samePhysicalFilesOwner, actualReadIssuer, actualNavigator);
    }

    public sealed partial class BrowserDownloadFilesOwner : IBrowserOriginalDownloadFilesService,
        IBrowserOriginalFilesPhysicalDestinationSource
    {
        private const string ReadAction = "files.folder.native-root.read";
        private const string WriteAction = "9to1.Files.RegisterBrowserDownload";
        private readonly FilesNativeBrowserService _browser;
        private readonly IBrowserOriginalDownloadFilesPhysicalOwner _physicalOwner;
        private readonly FilesArtifactResourceResolver _readIssuer;
        private readonly IFilesOriginalBrowserDownloadNavigator? _navigator;
        private readonly ConditionalWeakTable<IBrowserOriginalDownloadFilesDestination, Destination> _destinations = new();
        private readonly ConditionalWeakTable<IBrowserOriginalDownloadFilesRegistration, Registration> _registrations = new();
        private readonly Dictionary<Guid, Registration> _operations = [];
        private readonly HashSet<DurableDriveProvider> _providers = [];
        public IBrowserOriginalNativeDownloadPhysicalOwner OriginalContentOwner { get; }
        public FilesNativeBrowserService OriginalBrowser => _browser;
        public IBrowserOriginalDownloadFilesPhysicalOwner OriginalPhysicalFilesOwner => _physicalOwner;
        public FilesArtifactResourceResolver OriginalReadIssuer => _readIssuer;
        public IFilesOriginalBrowserDownloadNavigator? OriginalNavigator => _navigator;
        internal BrowserDownloadFilesOwner(FilesNativeBrowserService browser,
            IBrowserOriginalNativeDownloadPhysicalOwner content, IBrowserOriginalDownloadFilesPhysicalOwner physical,
            FilesArtifactResourceResolver reads, IFilesOriginalBrowserDownloadNavigator? navigator)
        {
            _browser = browser; OriginalContentOwner = content; _physicalOwner = physical;
            _readIssuer = reads ?? throw new ArgumentNullException(nameof(reads)); _navigator = navigator;
            physical.BindOriginalFilesDestinationSource(this);
        }
        private sealed class Destination : IBrowserOriginalDownloadFilesDestination
        {
            internal BrowserDownloadFilesOwner Owner = null!;
            internal NativeFilesWorkspace Workspace = null!;
            internal AuthenticatedResourceActor Actor = null!;
            internal HostedItemMetadata Folder = null!;
            internal IOriginalCanonicalReadContext Read = null!;
            internal FilesWorkspaceDirectoryBinding Binding = null!;
            internal string StoreRevision = null!;
            public string DisplayName => Folder.Name;
        }
        private sealed record Catalogue(IReadOnlyList<IBrowserOriginalDownloadFilesDestination> Destinations,
            string Detail) : IBrowserOriginalDownloadFilesDestinationCatalogue;
        private sealed class Registration : IBrowserOriginalDownloadFilesRegistration, IBrowserOriginalFilesPhysicalDestination
        {
            internal BrowserDownloadFilesOwner Owner = null!;
            internal Destination Destination = null!;
            internal IBrowserOriginalDownloadContent? Content;
            internal BrowserDownloadRecord Record = null!;
            internal Guid Operation;
            internal FilesBrowserDownloadRegistration Saved = null!;
            internal JsonElement Arguments;
            internal IReadOnlyList<ResourceScope> Scopes = null!;
            internal HomePermissionAuthorization? Approval;
            internal HomeResourceExecutionCapability? Capability;
            internal HomeClaimedResourceCommitFence? Fence;
            internal FilesWorkspaceDirectoryResolver.FilesOriginalBindingCommitLease? Mapping;
            internal IBrowserOriginalFilesPhysicalPin? Pin;
            internal Task? ResourcesClose;
            internal Sources? ResourcesSources;
            internal readonly List<(IAsyncDisposable Resource, Task? ActualClose, Exception? SynchronousFailure)> ResourceCloses = [];
            internal Sources? Source;
            internal Command? OriginalCommand;
            internal FilesResult<FilesRevision>? ActualResult;
            internal HomeExecutionOutcome? ActualOutcome;
            internal bool Claimed, Validated, AuditRecorded;
            internal int Attempts;
            public Guid OriginalOperationId => Operation;
            public BrowserDownloadRecord OriginalRecord => Record;
            public BrowserOriginalDownloadFilesRegistrationState State =>
                ActualResult?.IsSuccess == true ? BrowserOriginalDownloadFilesRegistrationState.Registered
                : BrowserOriginalDownloadFilesRegistrationState.AwaitingApproval;
            public Guid? OriginalFilesItemId => ActualResult?.IsSuccess == true ? Saved.OriginalContent.FileId.Value : null;
            public Guid? OriginalFilesRevisionId => ActualResult?.IsSuccess == true ? Saved.OriginalContent.RevisionId.Value : null;
            public string Detail => ActualResult?.IsSuccess == true
                ? AuditRecorded ? "Registered in Files." : "Registered in Files; inspect the original Home audit."
                : "The separate Files WRITE review has not admitted this original registration. Check Home before retrying.";
        }
        public bool IsIssuedOriginalDestination(IBrowserOriginalDownloadFilesDestination actual) =>
            actual is Destination destination && ReferenceEquals(destination.Owner, this) &&
            _destinations.TryGetValue(actual, out var same) && ReferenceEquals(same, destination);
        private Destination RequireDestination(IBrowserOriginalDownloadFilesDestination actual) => IsIssuedOriginalDestination(actual)
            ? (Destination)actual : throw new UnauthorizedAccessException("Select the same privately issued Files registered folder.");
        public bool IsIssuedOriginalRegistration(IBrowserOriginalDownloadFilesRegistration actual) =>
            actual is Registration registration && ReferenceEquals(registration.Owner, this) &&
            _registrations.TryGetValue(actual, out var same) && ReferenceEquals(same, registration);
        private Registration RequireRegistration(IBrowserOriginalDownloadFilesRegistration actual) => IsIssuedOriginalRegistration(actual)
            ? (Registration)actual : throw new UnauthorizedAccessException("The same actual Browser Files registration is required.");
        private bool Alive() { lock (_gate) return !_retiring || _executing.Value is { Live: true }; }
        private async Task RequireRead(Destination destination, Sources source, CancellationToken token)
        {
            source.Invoke(() =>
            {
                if (!ReferenceEquals(destination.Owner, this) || !_browser.OriginalBrowserResources.IsIssuedOriginalReadOwnerBinding(
                    destination.Actor, destination.Read, ReadAction, destination.Read.OriginalScope,
                    destination.Workspace.Provider, destination.Workspace.Directories, destination.Workspace.Configuration.StoreId))
                    throw new UnauthorizedAccessException("The actual original Files read source changed.");
            });
            if (await source.Read(() => _browser.OriginalBrowserResources.AuthorizeOriginalReadForActorAsync(destination.Actor,
                destination.Read, ReadAction, [destination.Read.OriginalScope], token).AsTask()).ConfigureAwait(false) != destination.Actor)
                throw new UnauthorizedAccessException("The actual Home actor cannot read this registered Files folder.");
        }
        private async Task<Destination?> CaptureDestination(NativeFilesWorkspace workspace,
            string originalApp, HostedItemId originalFolder, Sources source, CancellationToken token)
        {
            if (!Guid.TryParse(workspace.Actor.ProfileId, out var profile) || profile == Guid.Empty)
                throw new UnauthorizedAccessException("A real current Home local profile is required.");
            var destination = new Destination { Owner = this, Workspace = workspace, Actor = workspace.Actor };
            destination.Read = await source.Read(() => _readIssuer.CaptureOriginalFolderReadAsync(workspace,
                originalFolder, Alive, token).AsTask()).ConfigureAwait(false);
            await RequireRead(destination, source, token).ConfigureAwait(false);
            var row = await source.Read(() => workspace.Provider.GetForOriginalStoreAsync(workspace.Configuration.StoreId,
                originalFolder, token)).ConfigureAwait(false);
            if (!row.IsSuccess || row.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null } folder ||
                folder.CurrentRevisionId.Value.ToString() != destination.Read.OriginalScope.Revision)
                throw new UnauthorizedAccessException("The current registered Files folder revision changed.");
            destination.Folder = folder;
            var binding = await source.Read(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                originalApp, originalFolder, workspace.Provider, workspace.Configuration.StoreId, requireUniqueFolder: true,
                ct => new ValueTask(RequireRead(destination, source, ct)), token)).ConfigureAwait(false);
            if (!binding.IsSuccess)
            {
                if (binding.Error?.Code == FilesErrorCode.DestinationUnavailable) return null;
                throw new UnauthorizedAccessException(binding.Error?.Message ?? "The original Files registration is unavailable.");
            }
            destination.Binding = binding.Value!;
            var evidence = await source.Read(() => workspace.Provider.GetStoreEvidenceAsync(workspace.Configuration.StoreId,
                source.Invoke, source.Retain, token)).ConfigureAwait(false);
            destination.StoreRevision = evidence.Revision;
            await RequireRead(destination, source, token).ConfigureAwait(false);
            return destination;
        }
        private async Task RevalidateDestination(Destination destination, Sources source, CancellationToken token)
        {
            await RequireRead(destination, source, token).ConfigureAwait(false);
            if (await source.Read(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != destination.Actor)
                throw new UnauthorizedAccessException("The original Home actor changed.");
            var current = await source.Read(() => _browser.OriginalBrowserWorkspaces.GetCurrentAsync(destination.Workspace.Configuration.StoreId, token)).ConfigureAwait(false);
            if (current?.Actor != destination.Actor || !ReferenceEquals(current.Provider, destination.Workspace.Provider) ||
                !ReferenceEquals(current.Directories, destination.Workspace.Directories))
                throw new UnauthorizedAccessException("The actual Files workspace was replaced.");
            var row = await source.Read(() => current.Provider.GetForOriginalStoreAtRevisionAsync(current.Configuration.StoreId,
                destination.Folder.Id, destination.Folder.CurrentRevisionId!.Value, token)).ConfigureAwait(false);
            if (!row.IsSuccess || row.Value != destination.Folder) throw new InvalidOperationException("The displayed registered Files destination changed; refresh it.");
            var binding = await source.Read(() => current.Directories.ResolveOriginalRegisteredFolderAsync(Guid.Parse(destination.Actor.ProfileId),
                destination.Binding.OwningAppId, destination.Folder.Id, current.Provider, current.Configuration.StoreId, true,
                ct => new ValueTask(RequireRead(destination, source, ct)), token)).ConfigureAwait(false);
            if (!binding.IsSuccess || binding.Value != destination.Binding) throw new UnauthorizedAccessException("The actual registered folder mapping changed.");
            var evidence = await source.Read(() => current.Provider.GetStoreEvidenceAsync(current.Configuration.StoreId,
                source.Invoke, source.Retain, token)).ConfigureAwait(false);
            if (evidence.Revision != destination.StoreRevision) throw new InvalidOperationException("The original Files store snapshot changed; refresh the destination.");
            await RequireRead(destination, source, token).ConfigureAwait(false);
        }
        public Task<IBrowserOriginalDownloadFilesDestinationCatalogue> ReadOriginalDestinationsWithinSourceAsync(
            IBrowserOriginalDownloadContent content, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            Run<IBrowserOriginalDownloadFilesDestinationCatalogue>(scope, retain, async source =>
            {
                source.Invoke(() => { if (!OriginalContentOwner.IsIssuedOriginalContent(content)) throw new UnauthorizedAccessException("Use the actual held native download content."); });
                await source.Read(() => OriginalContentOwner.RevalidateOriginalContentWithinSourceAsync(content, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                var actor = await source.Read(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
                if (actor is null) return new Catalogue(Array.Empty<IBrowserOriginalDownloadFilesDestination>(), "Open the current Home Files profile first.");
                var workspace = await source.Read(() => _browser.OriginalBrowserWorkspaces.GetCurrentAsync(token)).ConfigureAwait(false);
                if (workspace is null) return new Catalogue(Array.Empty<IBrowserOriginalDownloadFilesDestination>(), "Set up and import the actual Files workspace in Home.");
                if (workspace.Actor != actor) throw new UnauthorizedAccessException("The actual current Files workspace actor changed.");
                var rows = new List<IBrowserOriginalDownloadFilesDestination>();
                foreach (var pair in workspace.Configuration.AppFolders.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    // Only actual Home configured roots are discoverable here; no raw
                    // path, new folder, download directory or hidden fallback is adopted.
                    var destination = await CaptureDestination(workspace, pair.Key, pair.Value, source, token).ConfigureAwait(false);
                    if (destination is not null) source.Invoke(() => { _destinations.Add(destination, destination); rows.Add(destination); });
                }
                await source.Read(() => OriginalContentOwner.RevalidateOriginalContentWithinSourceAsync(content, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                return new Catalogue(Array.AsReadOnly(rows.ToArray()), rows.Count == 0
                    ? "No registered Files destination is configured in Home." : "Choose an existing registered Files folder; registration requires a separate Home WRITE review.");
            });
        public Task RevalidateOriginalDestinationWithinSourceAsync(IBrowserOriginalDownloadFilesDestination actual,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => Run<object?>(scope, retain, async source =>
            { var destination = source.Invoke(() => RequireDestination(actual)); await RevalidateDestination(destination, source, token).ConfigureAwait(false); return null; });
        public Task<IBrowserOriginalDownloadFilesRegistration> RegisterOriginalDownloadWithinSourceAsync(
            IBrowserOriginalDownloadContent content, IBrowserOriginalDownloadFilesDestination actualDestination,
            Guid originalOperationId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            Run<IBrowserOriginalDownloadFilesRegistration>(scope, retain, source => Register(content, actualDestination, originalOperationId, source, token));
        private async Task<IBrowserOriginalDownloadFilesRegistration> Register(IBrowserOriginalDownloadContent content,
            IBrowserOriginalDownloadFilesDestination actualDestination, Guid operation, Sources source, CancellationToken token)
        {
            var destination = source.Invoke(() => RequireDestination(actualDestination));
            if (operation == Guid.Empty) throw new ArgumentException("Retain one operation ID across the original approval and retry.");
            source.Invoke(() => { if (!OriginalContentOwner.IsIssuedOriginalContent(content)) throw new UnauthorizedAccessException("The actual held original native content is required."); });
            await source.Read(() => OriginalContentOwner.WaitOriginalApprovedContentWithinSourceAsync(content, source.Invoke, source.Retain, token)).ConfigureAwait(false);
            Registration? acknowledged;
            lock (_gate) _operations.TryGetValue(operation, out acknowledged);
            if (acknowledged?.ActualResult?.IsSuccess == true)
            {
                if (!ReferenceEquals(acknowledged.Content, content) || !ReferenceEquals(acknowledged.Destination, destination))
                    throw new UnauthorizedAccessException("Observe the same original registration; do not adopt another selection.");
                await RequireRead(destination, source, token).ConfigureAwait(false);
                var current = await source.Read(() => destination.Workspace.Provider.ReadOriginalBrowserDownloadRegistrationAsync(
                    destination.Workspace.Configuration.StoreId, content.OriginalRecord.Id, content.OriginalRecord.ActionId,
                    source.Invoke, source.Retain, token)).ConfigureAwait(false);
                if (current != acknowledged.Saved) throw new InvalidDataException("The original durable Browser mapping changed.");
                return acknowledged;
            }
            await RevalidateDestination(destination, source, token).ConfigureAwait(false);
            var broker = _browser._mutationBroker!;
            Registration registration;
            lock (_gate)
            {
                if (_operations.TryGetValue(operation, out registration!))
                {
                    if (!ReferenceEquals(registration.Content, content) || !ReferenceEquals(registration.Destination, destination))
                        throw new UnauthorizedAccessException("Retry the exact same original content and destination; an operation cannot adopt another selection.");
                }
                else
                {
                    if (_operations.Count >= 64) throw new InvalidOperationException("Close the current registration cohort before more original operations.");
                    var record = content.OriginalRecord; var revision = new FilesRevisionId(Guid.NewGuid());
                    var uploaded = new FilesUploadedContent(new(Guid.NewGuid()), destination.Folder.Id, record.FileName,
                        record.ContentType, revision, null, destination.Actor.ActorId, DateTimeOffset.UtcNow, record.SizeBytes,
                        record.Sha256, ".9to1-browser-" + revision.Value.ToString("N") + ".content");
                    registration = new() { Owner = this, Destination = destination, Content = content, Record = record, Operation = operation,
                        Saved = new(operation, record.Id, record.ActionId, destination.Workspace.Configuration.StoreId,
                            destination.Actor.ActorId, destination.Actor.ProfileId, uploaded) };
                    registration.Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", destination.Folder.Id.ToString(),
                        destination.Folder.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write) });
                    registration.Arguments = JsonSerializer.SerializeToElement(new { operationId = operation, downloadId = record.Id,
                        actionId = record.ActionId, storeId = registration.Saved.FilesStoreId, expectedStoreRevision = destination.StoreRevision,
                        folderId = destination.Folder.Id.Value, expectedFolderRevision = destination.Folder.CurrentRevisionId.Value.ToString(),
                        fileId = uploaded.FileId.Value, revisionId = uploaded.RevisionId.Value, name = uploaded.Name,
                        sizeBytes = uploaded.SizeBytes, contentHash = uploaded.ContentHash });
                    _operations.Add(operation, registration); _registrations.Add(registration, registration); _providers.Add(destination.Workspace.Provider);
                }
                if (registration.ActualResult?.IsSuccess == true) return registration;
                if (registration.Capability is not null || registration.Attempts++ >= 32)
                    throw new InvalidOperationException("Inspect the retained original effect/audit; it cannot replay or acquire another capability.");
                registration.Source = source; registration.OriginalCommand = source.OriginalCommand;
            }
            registration.Approval ??= await source.Read(() => broker.AuthorizeForActorAsync(destination.Actor, "files", WriteAction,
                registration.Scopes, registration.Arguments, $"Register the approved native download ‘{registration.Saved.OriginalContent.Name}’ in ‘{destination.Folder.Name}’.",
                null, "browse-files:" + operation.ToString("N"), token)).ConfigureAwait(false);
            await RevalidateDestination(destination, source, token).ConfigureAwait(false);
            await source.Read(() => OriginalContentOwner.RevalidateOriginalContentWithinSourceAsync(content, source.Invoke, source.Retain, token)).ConfigureAwait(false);
            var capability = await source.Read(() => broker.BeginExecutionCapabilityAsync(registration.Approval.RequestId,
                registration.Arguments, token), actual => registration.Capability = actual).ConfigureAwait(false);
            if (capability is null) return registration;
            var failures = new List<Exception>();
            try
            {
                var claim = await source.Read(() => broker.ClaimExecutionObservedAsync(capability, "files", WriteAction,
                    registration.Scopes, registration.Arguments, token)).ConfigureAwait(false);
                if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != destination.Actor)
                    throw new UnauthorizedAccessException("Home did not claim the exact original Files registration.");
                registration.Claimed = true;
                await RevalidateDestination(destination, source, token).ConfigureAwait(false);
                await source.Read(() => destination.Workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(destination.Binding,
                    destination.Workspace.Provider, token), actual => registration.Mapping = actual).ConfigureAwait(false);
                if (registration.Mapping?.IsHeld != true) throw new UnauthorizedAccessException("The exact registered folder mapping could not be held.");
                await source.Read(() => _physicalOwner.PinOriginalFilesDestinationWithinSourceAsync(registration, source.Invoke, source.Retain, token),
                    actual => registration.Pin = actual).ConfigureAwait(false);
                await source.Read(() => _browser.OriginalBrowserWorkspaces.CaptureBrowserCommitFenceWithinOriginalSourceAsync(destination.Workspace, broker,
                    capability, Alive, source.Invoke, source.Retain, token,
                    actual => registration.Fence = actual).AsTask(), actual => registration.Fence = actual).ConfigureAwait(false);
                var guard = new FilesCommitAuthorityGuard(destination.Actor.ActorId, async ct =>
                {
                    var valid = await source.Read(() => registration.Fence!.ValidateWithinOriginalSourceAsync(source.Invoke, source.Retain, ct).AsTask()).ConfigureAwait(false);
                    registration.Validated = valid; return valid;
                });
                await source.Read(() => destination.Workspace.Provider.CommitOriginalBrowserDownloadAsync(registration.Saved,
                    destination.StoreRevision, new(destination.Folder.Id, destination.Folder.CurrentRevisionId), guard,
                    ct => _physicalOwner.CopyOriginalDownloadToFilesWithinSourceAsync(registration.Pin!, content, source.Invoke, source.Retain, ct),
                    source.Invoke, source.Retain, token), actual => registration.ActualResult = actual).ConfigureAwait(false);
                if (registration.ActualResult?.IsSuccess != true)
                    throw new InvalidOperationException(registration.ActualResult?.Error?.Message ?? "No actual Files provider acknowledgment.");
            }
            catch (Exception error) { Add(failures, error); }
            // Finite physical pin + mapping + held Home MUST close before audit.
            try { await CloseRegistrationResources(registration).ConfigureAwait(false); } catch (Exception error) { Add(failures, error); }
            if (!registration.Claimed)
            {
                try { await source.Read(() => broker.AbortUnclaimedExecutionAsync(capability, CancellationToken.None)).ConfigureAwait(false); }
                catch (Exception error) { Add(failures, error); }
            }
            else
            {
                var known = registration.ActualResult is not null;
                registration.ActualOutcome = new(registration.ActualResult?.IsSuccess == true ? HomePermissionRequestState.Succeeded
                    : known ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted,
                    registration.ActualResult?.IsSuccess == true ? "BROWSER_FILES_REGISTERED" : "BROWSER_FILES_REGISTRATION_UNKNOWN",
                    registration.ActualResult?.IsSuccess == true ? "The exact approved native download was registered in Files."
                        : "Preserve the actual original bytes/metadata and inspect the retained registration; no replay is admitted.",
                    registration.ActualResult?.IsSuccess == true ? [new("files.item", registration.Saved.OriginalContent.FileId.ToString())] : []);
                // Failed release forbids Home re-entry; its exact close remains rooted.
                if (registration.ResourcesClose?.IsCompletedSuccessfully == true)
                    try { var audit = await source.Read(() => broker.CompleteExecutionAsync(capability, registration.ActualOutcome,
                        CancellationToken.None)).ConfigureAwait(false); registration.AuditRecorded = audit.Succeeded; }
                    catch (Exception error) { Add(failures, error); }
            }
            Throw(failures); return registration;
        }
        public bool IsIssuedOriginalPhysicalDestination(IBrowserOriginalFilesPhysicalDestination actual) =>
            actual is Registration registration && IsIssuedOriginalRegistration(registration) && registration.Content is not null;
        public BrowserOriginalFilesPhysicalDestinationDescription GetOriginalPhysicalDestinationDescription(IBrowserOriginalFilesPhysicalDestination actual)
        {
            var registration = actual is Registration value && IsIssuedOriginalPhysicalDestination(value)
                ? value : throw new UnauthorizedAccessException("Use the actual Files registration's private destination.");
            return new(registration.Destination.Binding.DirectoryPath, registration.Saved.FilesStoreId,
                registration.Destination.Folder.Id.Value, registration.Saved.OriginalContent.FileId.Value,
                registration.Saved.OriginalContent.RevisionId.Value, registration.Saved.OriginalContent.ProviderContentReference,
                registration.Destination.Actor);
        }
        public void DemandOriginalFilesCommit(IBrowserOriginalFilesPhysicalDestination actual)
        {
            if (actual is not Registration registration || !IsIssuedOriginalPhysicalDestination(registration) ||
                registration.OriginalCommand is not { Live: true } original ||
                !(ReferenceEquals(_executing.Value, original) || _physicalCommands?.ContainsKey(original) == true) ||
                !registration.Claimed || !registration.Validated ||
                registration.Fence is null || registration.Mapping?.IsHeld != true || registration.ResourcesClose is not null)
                throw new UnauthorizedAccessException("Only the actual current metadata driver with held Home and registered mapping may materialize bytes.");
        }
        private Task CloseRegistrationResources(Registration registration)
        {
            lock (_gate)
            {
                if (registration.ResourcesClose is not null) return registration.ResourcesClose;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Cleanup owns its finite nested sources. A view's productive callback
                // can end without lending that ended lifetime to retained Home releases.
                var actualCommand = registration.OriginalCommand
                    ?? throw new InvalidOperationException("The SAME registration has no retained original command.");
                registration.ResourcesSources = new(this, body => Physical(body), _ => { })
                    { OriginalCommand = actualCommand };
                var actual = registration.ResourcesClose = Close(start.Task); start.SetResult(); return actual;
            }
            async Task Close(Task begin)
            {
                await begin.ConfigureAwait(false); var errors = new List<Exception>();
                var cleanup = registration.ResourcesSources!;
                // Preserve each SAME returned release before callbacks and join every
                // nested Home/completion source independently before any audit reentry.
                foreach (var resource in new IAsyncDisposable?[] { registration.Pin, registration.Mapping, registration.Fence }.OfType<IAsyncDisposable>())
                {
                    Task? actual = null;
                    try
                    {
                        await cleanup.Read(() =>
                        {
                            actual = resource is HomeClaimedResourceCommitFence fence
                                ? fence.DisposeWithinOriginalSourceAsync(cleanup.Invoke, cleanup.Retain).AsTask()
                                : resource.DisposeAsync().AsTask();
                            registration.ResourceCloses.Add((resource, actual, null));
                            return actual;
                        }).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        Add(errors, error);
                        if (actual is null) registration.ResourceCloses.Add((resource, null, error));
                    }
                }
                try { await cleanup.Join().ConfigureAwait(false); } catch (Exception error) { Add(errors, error); }
                Throw(errors);
            }
        }
        public Task<IBrowserOriginalDownloadFilesRegistration?> ReadOriginalRegistrationWithinSourceAsync(
            IBrowserOriginalDownloadRecordSource sameRecordSource, IBrowserOriginalDownloadRecordObservation actualRecord,
            Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            Run<IBrowserOriginalDownloadFilesRegistration?>(scope, retain, async source =>
            {
                source.Invoke(() => { if (!sameRecordSource.IsIssuedOriginalDownloadRecord(actualRecord)) throw new UnauthorizedAccessException("Select the actual current Browser ledger row."); });
                await source.Read(() => sameRecordSource.RevalidateOriginalDownloadRecordWithinSourceAsync(actualRecord, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                var actor = await source.Read(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
                var workspace = await source.Read(() => _browser.OriginalBrowserWorkspaces.GetCurrentAsync(token)).ConfigureAwait(false);
                if (workspace is null || actor is null) return null;
                if (actor != workspace.Actor) throw new UnauthorizedAccessException("The actual current Files workspace actor changed.");
                // Current store ownership is revalidated BEFORE mapping metadata.
                var currentRead = await source.Read(() => _browser.OriginalBrowserWorkspaces.CaptureOriginalReadCheckAsync(workspace, Alive, token).AsTask()).ConfigureAwait(false);
                if (!await source.Read(() => currentRead(token).AsTask()).ConfigureAwait(false)) throw new UnauthorizedAccessException("The actual Files READ retired.");
                var record = actualRecord.OriginalRecord;
                var saved = await source.Read(() => workspace.Provider.ReadOriginalBrowserDownloadRegistrationAsync(workspace.Configuration.StoreId,
                    record.Id, record.ActionId, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                if (saved is null) return null;
                if (actor is null) throw new UnauthorizedAccessException("The actual original Files actor is unavailable.");
                if (saved.OriginalContent.SizeBytes != record.SizeBytes || saved.OriginalContent.ContentHash != record.Sha256 ||
                    saved.OriginalContent.Name != record.FileName || saved.ActorId != actor.ActorId || saved.ProfileId != actor.ProfileId)
                    throw new InvalidDataException("The selected Browser ledger row differs from the saved canonical Files mapping.");
                var app = workspace.Configuration.AppFolders.SingleOrDefault(pair => pair.Value == saved.OriginalContent.ParentFolderId);
                if (string.IsNullOrWhiteSpace(app.Key)) throw new NotSupportedException("The original registered Files destination is not configured.");
                var destination = await CaptureDestination(workspace, app.Key, app.Value, source, token).ConfigureAwait(false)
                    ?? throw new NotSupportedException("The original registered Files destination is no longer available.");
                var revision = await source.Read(() => workspace.Provider.GetForOriginalStoreAsync(workspace.Configuration.StoreId,
                    saved.OriginalContent.FileId, token)).ConfigureAwait(false);
                if (!revision.IsSuccess || revision.Value is not { } currentRow || currentRow.CurrentRevisionId != saved.OriginalContent.RevisionId || currentRow.ParentId != destination.Folder.Id)
                    throw new InvalidOperationException("The registered file revision changed; open its current Files history instead.");
                await RequireRead(destination, source, token).ConfigureAwait(false);
                await source.Read(() => sameRecordSource.RevalidateOriginalDownloadRecordWithinSourceAsync(actualRecord, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                var originalContent = await source.Read(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                    workspace.Configuration.StoreId, saved.OriginalContent.FileId, saved.OriginalContent.RevisionId, token)).ConfigureAwait(false);
                if (!originalContent.IsSuccess || originalContent.Value!.ProviderContentReference != saved.OriginalContent.ProviderContentReference ||
                    originalContent.Value.UploadAnchorFolderId != saved.OriginalContent.ParentFolderId)
                    throw new InvalidDataException("The actual current Files revision differs from the durable Browser mapping.");
                var registration = new Registration { Owner = this, Destination = destination, Record = record, Operation = saved.OperationId,
                    Saved = saved, ActualResult = FilesResult<FilesRevision>.Success(originalContent.Value.Revision), AuditRecorded = false };
                source.Invoke(() => _registrations.Add(registration, registration)); return registration;
            });
        public Task RevealOriginalRegistrationWithinSourceAsync(IBrowserOriginalDownloadFilesRegistration actual,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => Run<object?>(scope, retain, async source =>
            {
                var registration = source.Invoke(() => RequireRegistration(actual));
                if (registration.ActualResult?.IsSuccess != true) throw new InvalidOperationException("No actual canonical Files registration is available.");
                var navigator = _navigator ?? throw new NotSupportedException("The actual canonical Files desktop navigator is not configured.");
                var destination = registration.Destination;
                await RequireRead(destination, source, token).ConfigureAwait(false);
                var selection = await source.Read(() => _browser.ReadOriginalRegisteredDownloadSelectionWithinSourceAsync(
                    destination.Actor, destination.Workspace.Configuration.StoreId, destination.Folder.Id,
                    registration.Saved.OriginalContent.FileId, registration.Saved.OriginalContent.RevisionId,
                    Alive, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                var page = selection.Page; var row = selection.Row;
                await source.Read(() => navigator.RevealOriginalRegisteredDownloadWithinSourceAsync(_browser, page, row,
                    destination.Actor, source.Invoke, source.Retain, token)).ConfigureAwait(false);
                await source.Read(() => _browser.RevalidateAsync(page, destination.Actor, token)).ConfigureAwait(false);
                return null;
            });
    }
}
