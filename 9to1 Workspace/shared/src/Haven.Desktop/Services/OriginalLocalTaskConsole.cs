using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Explicit Linux console over the normal configured business owners. It owns
/// process startup and observation, not a second model/tool loop. Saved IDs, displayed
/// requests and provider catalogues remain observations; their actual issuers authorize work.</summary>
public static partial class OriginalLocalTaskConsole
{
    public static async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.SequenceEqual(["--help"])) { Write(Help); return 0; }
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The explicit local Task console requires Linux.");
        if (arguments.Length != 2 || arguments[0] != "--data-directory" ||
            !Path.IsPathFullyQualified(arguments[1]))
            throw new ArgumentException("Use --local-task-console --data-directory <absolute private directory>, or --help.");
        var host = new Host(new ExplicitPaths(arguments[1]));
        var failures = new List<Exception>();
        Task? startup = null;
        Task? close = null;
        try
        {
            startup = host.StartOriginalAsync();
            await startup.WaitAsync(cancellationToken).ConfigureAwait(false); // Withdraws this wait only; host retains the original.
            Write(Help);
            while (true)
            {
                var actualRead = Console.In.ReadLineAsync(cancellationToken).AsTask();
                host.RetainInputRead(actualRead);
                var line = await actualRead.ConfigureAwait(false);
                if (line is null || line.Trim() == "quit") break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { await host.ExecuteCommandAsync(line, cancellationToken).ConfigureAwait(false); }
                catch (Exception cause) { Write(new { commandFailed = true, cause = cause.ToString() }); }
            }
        }
        catch (Exception cause) { Capture(failures, startup, cause); }
        finally
        {
            try { close = host.CloseAndDrainAsync(); }
            catch (Exception cause) { Capture(failures, null, cause); }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception cause) { Capture(failures, close, cause); }
            // A withdrawn startup wait cannot discard the actual late startup/status.
            if (startup is not null)
                try { await startup.ConfigureAwait(false); }
                catch (Exception cause) { Capture(failures, startup, cause); }
        }
        if (failures.Count == 0) return 0;
        Write(new { processFailed = true, causes = failures.Select(value => value.ToString()).ToArray() });
        return 1;
    }

    private static readonly string[] Help = [
        "models <provider-id> | probe | model <observed-provider:model-key> | engine Automatic|LlamaCpp|Strata",
        "files-configure <chosen empty absolute directory> | files-status",
        "containers | project <workspace-id> <project-id> <root-id> <existing-container-id> | project-clear | spaces | space <space-id> | space-create <name>",
        "task <instruction> | context | attach | open <space-id> <reference-id> <task-id> <run-id>",
        "status | read <project-relative-path> | steer <instruction> | queue <instruction> | pause | stop | resume",
        "cold-input | cold-resume (explicit protected native recovery opt-in; the same saved Task/Run)",
        "home-requests | home-display <request-id> | home-accept <displayed-request-id> | home-decline <displayed-request-id>",
        "cf-connections | cf-setup | cf-prepare <saved-connection-id> <account-id> <observed-revision> | cf-commit <prepared-request-id>",
        "cf-discover (read-only KV list on the SAME authentic current Task attempt) | history",
        "cloud-approve <displayed-request-id> | cloud-deny <displayed-request-id> | quit",
        "Approval never resumes a Task. Resume is a separate fresh-authorized command.",
        "Project selection requires existing reviewed metadata/root/file registration. Browser, computer and unconfigured Dev execution remain unavailable.",
        "quit/EOF drains actual owners. External process termination leaves crash/cold recovery to its protected source."
    ];
    private static readonly object OutputGate = new();
    private static void Write(object? value)
    { lock (OutputGate) Console.Out.WriteLine(JsonSerializer.Serialize(value)); }
    private static void Capture(List<Exception> failures, Task? actual, Exception caught)
    {
        foreach (var cause in actual?.Exception is { InnerExceptions.Count: > 0 } group ? group.InnerExceptions : new[] { caught })
            if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
    }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Original console sources did not all settle successfully.", failures);
    }

    private sealed class ExplicitPaths(string selected) : IAppPaths
    {
        public string DataDirectory { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy-state.json");
    }

    private sealed partial class Host
    {
        private readonly object _gate = new();
        private readonly IAppPaths _paths;
        private readonly DesktopOriginalWorkLifetime _work;
        private readonly DesktopOriginalShutdownSequence _shutdown;
        private readonly List<Task> _inputReads = [];
        private readonly List<NewChatOriginalInitialTaskObservation> _initials = [];
        private readonly List<SpaceOriginalRunResumeObservation> _coldObservers = [];
        private readonly List<Exception> _requests = [];
        private readonly Dictionary<string, string> _displayedHome = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, RemediationRequest> _displayedCloud = [];
        private readonly List<ProviderModelDescriptor> _catalogue = [];
        private CloudflareLocalDomainRegistration? _registration;
        private ServiceProvider? _provider;
        private TaskRunCanonicalProcessRetirementOwner? _canonicalProcess;
        private DeveloperTaskWorkspaceService? _developer;
        private FilesNativeBrowserService? _files;
        private NativeFilesWorkspaceService? _nativeFiles;
        private NativeFilesWorkspaceAuthority? _nativeAuthority;
        private FilesArtifactResourceResolver? _fileResolver;
        private ManagedDulcheRuntimeService? _managed;
        private SpaceDevTaskAttachmentSession? _session;
        private IStartupRecoveryCoordinator? _recovery;
        private NativePersonalTaskColdRecoveryHost? _coldHost;
        private Task? _startup;
        private NewChatOriginalInitialTaskObservation? _currentInitial;
        private readonly OriginalTaskSelection _selectedTask = new();
        private SpaceDevTaskAttachmentSession.SpaceDevTaskAttachment? _attachment;
        private DeveloperResolvedProject? _project;
        private ContainerDefinition? _container;
        private ProviderModelDescriptor? _model;
        private Guid? _spaceId;
        private bool _started;
        [ThreadStatic] private static List<Host>? _physical;

        internal Host(IAppPaths paths)
        {
            _paths = paths;
            _work = new(StopOriginalObserversAsync, () => Task.CompletedTask);
            _shutdown = new(RetireAndJoinProcessWorkAsync, () => Task.CompletedTask,
                PrepareOriginalFinalWriterAsync, JoinOriginalBorrowersAsync, DisposeOriginalProviderAsync);
        }

        internal Task StartOriginalAsync()
        {
            lock (_gate)
            {
                if (_startup is not null) return _startup;
            }
            // RunAsync publishes its actual driver before opening the body gate.
            return _work.RunAsync(StartBodyAsync, actual => _startup = actual);
        }

        private async Task StartBodyAsync(DesktopOriginalWorkLifetime.Original original)
        {
            Scope(original, () =>
            {
                var store = new FileHomeCoreStateStore(Path.Combine(_paths.DataDirectory, "Home", "state.json"));
                _registration = CloudflareLocalDomainRegistration.CreateOriginal(store, new OperatingSystemPrincipalSource(), _paths,
                    configureOriginalStores: identity =>
                    {
                        _nativeFiles = new NativeFilesWorkspaceService(identity.StateStore, identity.Profiles);
                        return new HomeLocalDomainStoreRegistrations([_nativeFiles], new Dictionary<Type, object>
                        { [typeof(NativeFilesWorkspaceService)] = _nativeFiles });
                    },
                    configureOriginalResolvers: components =>
                    {
                        _nativeAuthority = new NativeFilesWorkspaceAuthority(_nativeFiles!, components.Identity.Profiles, components.Ownership);
                        _fileResolver = new FilesArtifactResourceResolver(_nativeAuthority);
                        return new HomeLocalDomainResolverRegistrations([_fileResolver], new Dictionary<Type, object>
                        { [typeof(NativeFilesWorkspaceAuthority)] = _nativeAuthority, [typeof(FilesArtifactResourceResolver)] = _fileResolver });
                    });
            });
            Scope(original, () =>
            {
                var collection = new ServiceCollection();
                collection.AddHavenInfrastructure();
                collection.AddHavenOwnedNativeTaskColdRecovery(NativePersonalTaskColdRecoveryConfiguration.ReadExplicitEnvironment());
                _registration!.ConfigureOriginalServices(collection);
                // Exactly the same precreated Files objects feed Home evidence/resolution
                // and normal Files/Dev service factories. TryAdd cannot create replacements.
                collection.AddSingleton(_nativeFiles!);
                collection.AddSingleton(_nativeAuthority!);
                collection.AddSingleton(_fileResolver!);
                collection.AddFilesNativeHost();
                collection.AddHavenOriginalNativeDevelopment();
                collection.AddHavenOriginalTaskExecutionServices();
                _provider = collection.BuildServiceProvider();
            });
            Resolve<TaskRunCanonicalProcessRetirementOwner>(original, value => _canonicalProcess = value);
            var coordinator = Resolve<TaskExecutionCoordinator>(original);
            var agents = Resolve<AgentTaskRuntimeService>(original);
            var authority = Resolve<TaskRunPermissionAuthority>(original);
            var frames = Resolve<TaskRunOriginalFrameOwner>(original);
            if (!_canonicalProcess!.HasOriginalComposition(coordinator, agents, authority, frames))
                throw new InvalidOperationException("The original configured canonical process tuple changed.");
            Scope(original, () => _registration!.CaptureOriginalOwner(Provider)); // Early source custody before Home startup.
            Resolve<DeveloperTaskWorkspaceService>(original, value => _developer = value);
            Resolve<FilesNativeBrowserService>(original, value => _files = value);
            Resolve<ManagedDulcheRuntimeService>(original, value => _managed = value);
            Resolve<IStartupRecoveryCoordinator>(original, value => _recovery = value);
            Resolve<NativePersonalTaskColdRecoveryHost>(original, value => _coldHost = value);
            var lifecycle = Resolve<IApplicationLifecycle>(original);
            await Acquire(original, () => _recovery!.BeginStartupAsync(original.Token)).ConfigureAwait(false);
            await Acquire(original, () => lifecycle.CrashRecoveryAsync(original.Token)).ConfigureAwait(false);
            await Acquire(original, () => lifecycle.StartupAsync(original.Token)).ConfigureAwait(false);
            if (!lifecycle.IsStartupComplete) throw new InvalidOperationException("The actual lifecycle owner did not complete startup.");
            await Acquire(original, () => Provider.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(original.Token)).ConfigureAwait(false);
            await Acquire(original, () => Provider.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(original.Token)).ConfigureAwait(false);
            await Acquire(original, () => _registration!.StartOriginalHomeAsync()).ConfigureAwait(false);
            Scope(original, () => _registration!.CaptureOriginalOwner(Provider)); // SAME retained owner; startup recheck grants nothing.
            Scope(original, () => _session = SpaceDevTaskAttachmentSession.CreateOriginal(_registration!.OriginalHome, Provider));
            await Acquire(original, () => _session!.StartOriginalAsync(original.Token)).ConfigureAwait(false);
            await Acquire(original, () => _recovery!.MarkStartupCompletedAsync(original.Token)).ConfigureAwait(false);
            original.DemandPublication();
            _started = true;
            Write(new { localHomeStarted = true, dataDirectory = _paths.DataDirectory,
                installedSession = false, modelInitialized = false, coldRecovery = _coldHost!.ConfigurationStatus, devExecutionTrust = Provider.GetService<IDeveloperWorkspaceTrustService>() is not null });
        }

        private ServiceProvider Provider => _provider ?? throw new InvalidOperationException("The actual configured provider is unavailable.");
        private HomeLocalDomainComposition Home => _registration?.OriginalHome ?? throw new InvalidOperationException("The original Home domain is unavailable.");
        private T Resolve<T>(DesktopOriginalWorkLifetime.Original original, Action<T>? capture = null) where T : class
        {
            T? value = null;
            Scope(original, () => { value = Provider.GetRequiredService<T>(); capture?.Invoke(value); });
            return value!;
        }
        internal void RetainInputRead(Task actual)
        {
            lock (_gate)
            {
                _inputReads.RemoveAll(value => value.IsCompletedSuccessfully);
                if (_inputReads.Count >= 128) throw new InvalidOperationException("Original console input custody requires retirement.");
                _inputReads.Add(actual);
            }
        }

        internal Task ExecuteCommandAsync(string line, CancellationToken commandToken)
        {
            var separator = line.IndexOf(' ');
            var command = separator < 0 ? line.Trim() : line[..separator].Trim();
            var argument = separator < 0 ? "" : line[(separator + 1)..].Trim();
            if (command == "help") { Write(Help); return Task.CompletedTask; }
            if (!_started) throw new InvalidOperationException("The actual original startup has not completed.");
            return _work.RunAsync(original => CommandBodyAsync(original, command, argument, commandToken));
        }

        private async Task CommandBodyAsync(DesktopOriginalWorkLifetime.Original original,
            string command, string argument, CancellationToken token)
        {
            switch (command)
            {
                case "models":
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                    var provider = Resolve<IModelProviderRegistry>(original).GetRequired(argument);
                    var models = await Acquire(original, () => provider.GetModelsAsync(token)).ConfigureAwait(false);
                    if (models.Any(value => value.ProviderId != provider.Id || value.IsLocal != provider.IsLocal))
                        throw new InvalidDataException("The original catalogue has a mismatched provider identity.");
                    lock (_gate) { _catalogue.RemoveAll(value => value.ProviderId == provider.Id); _catalogue.AddRange(models); }
                    Write(models); break;
                }
                case "probe":
                {
                    var source = Resolve<LlamaCppModelProvider>(original);
                    if (!ReferenceEquals(Resolve<IModelProviderRegistry>(original).GetRequired(source.Id), source))
                        throw new InvalidOperationException("The registered local provider was replaced.");
                    var actual = await Acquire(original, () => source.ProbeOriginalCapabilitiesAsync(true, token)).ConfigureAwait(false);
                    if (!source.IsOriginalEndpointObservation(actual)) throw new InvalidOperationException("The local probe is not source-issued.");
                    Write(actual); break;
                }
                case "model":
                    lock (_gate) _model = _catalogue.Single(value => value.Key == argument);
                    Write(new { selectedModel = _model.Key, note = "Observed catalogue selection; request authorities still validate current use." }); break;
                case "engine":
                {
                    var requested = argument.ToLowerInvariant() switch
                    {
                        "automatic" => InferenceEngine.Automatic,
                        "llamacpp" => InferenceEngine.LlamaCpp,
                        "strata" => InferenceEngine.Strata,
                        _ => throw new ArgumentException("Choose Automatic, LlamaCpp or Strata.")
                    };
                    ProviderModelDescriptor selected;
                    lock (_gate) selected = _model ?? throw new InvalidOperationException("Select an actual observed model first.");
                    var preferences = Resolve<ConfiguredInferenceEnginePreferences>(original);
                    if (!ReferenceEquals(Resolve<IInferenceEnginePreferenceSource>(original), preferences))
                        throw new InvalidOperationException("The configured engine preference source was replaced.");
                    // Normal selected-route capture conserves this observed catalogue identity
                    // and explicitly refuses an artifact revision. This is requested configuration
                    // only; no endpoint is created, switched or declared effective here.
                    var identity = new ModelIdentity(selected.ProviderId, selected.Model.Name);
                    Scope(original, () => preferences.SetRequestedInferenceEngine(identity, requested));
                    Write(new { requestedEngine = requested.ToString(), model = selected.Key,
                        note = "Applies when the actual next request initializes its endpoint. Effective engine and admission remain owned by that source." });
                    break;
                }
                case "files-configure":
                    if (!Path.IsPathFullyQualified(argument)) throw new ArgumentException("Choose an actual empty absolute Files directory.");
                    Write(await Acquire(original, () => _nativeFiles!.ConfigureNewAsync(argument, Home.LocalStoreOwnership, token)).ConfigureAwait(false)); break;
                case "files-status":
                    Write(await Acquire(original, () => _nativeFiles!.GetConfigurationAsync(token)).ConfigureAwait(false)); break;
                case "containers": Write(await Acquire(original, () => Resolve<IContainerRepository>(original).GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false)); break;
                case "project-clear": _project = null; _container = null; Write(new { projectSelectionCleared = true }); break;
                case "project":
                {
                    var ids = Ids(argument, 4);
                    var saved = await Acquire(original, () => Resolve<IDeveloperWorkspaceStore>(original).GetAsync(ids[0], token)).ConfigureAwait(false);
                    if (!saved.Succeeded || saved.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(saved.Error));
                    var project = saved.Value.Projects.Single(value => value.ProjectId == ids[1]);
                    if (!project.RootIds.Contains(ids[2])) throw new InvalidOperationException("Select a saved root of the SAME project.");
                    var reference = new DeveloperProjectReference(saved.Value.WorkspaceId, saved.Value.Revision, project.ProjectId,
                        project.Revision, ids[2], saved.Value.SourceControlBindings.SingleOrDefault(value => value.RootId == ids[2])?.BindingId);
                    var resolved = await Acquire(original, () => _developer!.ResolveAsync(reference, token)).ConfigureAwait(false);
                    if (!resolved.Succeeded || resolved.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(resolved.Error));
                    var containers = await Acquire(original, () => Resolve<IContainerRepository>(original).GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false);
                    var container = containers.Single(value => value.Id == ids[3] && value.Mode == HavenMode.Tasks && !value.IsArchived);
                    DemandProjectContainer(resolved.Value, container);
                    _project = resolved.Value; _container = container; Write(new { project = _project, container = _container }); break;
                }
                case "spaces": Write(await Acquire(original, () => Resolve<SpaceRegistry>(original).GetAllAsync(false, token)).ConfigureAwait(false)); break;
                case "space-create":
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(argument);
                    var actual = await Acquire(original, () => Resolve<SpaceRegistry>(original).CreateAsync(argument, cancellationToken: token)).ConfigureAwait(false);
                    _spaceId = actual.Id; Write(actual); break;
                }
                case "space":
                {
                    var id = Ids(argument, 1)[0];
                    var actual = await Acquire(original, () => Resolve<SpaceRegistry>(original).ReadExistingAsync(id, token)).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("The saved Space is unavailable.");
                    _spaceId = actual.Id; Write(actual); break;
                }
                case "task": await LaunchInitialAsync(original, argument, token).ConfigureAwait(false); break;
                case "context": Write((object?)CurrentInitial()?.CurrentAcknowledgedContext ?? _attachment?.View.Task.Snapshot); break;
                case "attach":
                {
                    var context = CurrentInitial()?.CurrentAcknowledgedContext ?? throw new InvalidOperationException("Wait for the authentic initial source acknowledgement.");
                    var project = _project ?? throw new InvalidOperationException("Select a saved project first.");
                    var space = await Acquire(original, () => Resolve<SpaceRegistry>(original).ReadExistingAsync(
                        _spaceId ?? throw new InvalidOperationException("Select an existing Space first."), token)).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("The saved Space is unavailable.");
                    _attachment = await Acquire(original, () => _session!.AttachOriginalAsync(space.Id, space.Revision,
                        context.ContextId, context.TaskId, context.ExecutionId, project.Reference, token)).ConfigureAwait(false);
                    SelectOriginalAttachment(_attachment);
                    Write(_attachment.View); break;
                }
                case "open":
                {
                    var ids = Ids(argument, 4);
                    _attachment = await Acquire(original, () => _session!.OpenOriginalAsync(ids[0], ids[1], ids[2], ids[3], token)).ConfigureAwait(false);
                    SelectOriginalAttachment(_attachment);
                    _project = _attachment.View.Project; _container = null; _spaceId = ids[0]; Write(_attachment.View); break;
                }
                case "status": Write(await Acquire(original, () => _session!.GetOriginalRunControlAvailabilityAsync(Attachment, token)).ConfigureAwait(false)); break;
                case "read": await ReadOriginalDocumentAsync(original, argument, token).ConfigureAwait(false); break;
                case "steer":
                case "queue":
                    Write(await Acquire(original, () => _session!.SubmitFollowUpOriginalAsync(Attachment, argument,
                        command == "steer" ? TaskFollowUpMode.Steer : TaskFollowUpMode.Queue, token)).ConfigureAwait(false)); break;
                case "pause": Write(await Acquire(original, () => _session!.PauseOriginalRunAsync(Attachment, token)).ConfigureAwait(false)); break;
                case "stop": Write(await Acquire(original, () => _session!.StopOriginalRunAsync(Attachment, token)).ConfigureAwait(false)); break;
                case "resume":
                    var observed = await Acquire(original, () => _session!.StartObservedOriginalResumeAsync(Attachment, token)).ConfigureAwait(false);
                    if (!observed.IsIssuedBy(_session!)) throw new InvalidOperationException("The resume observer has a foreign source.");
                    Write(new { observedResumeStarted = true, taskId = Attachment.View.Link.TaskId, runId = Attachment.View.Link.ExecutionId }); break;
                case "cold-input":
                    Write(await Acquire(original, () => _coldHost!.ObserveOriginalInputAsync(Attachment.View.Link.TaskId, Attachment.View.Link.ExecutionId, token)).ConfigureAwait(false)); break;
                case "cold-resume": LaunchColdObserver(original, token); break;
                case "cf-connections": await ObserveOriginalCloudflareConnectionsAsync(original, token).ConfigureAwait(false); break;
                case "cf-setup": Write(await Acquire(original, () => RequireOriginalCloudflareSetupSource(original).GetSetupAsync(token)).ConfigureAwait(false)); break;
                case "cf-prepare": await PrepareOriginalCloudflareSetupAsync(original, argument, token).ConfigureAwait(false); break;
                case "cf-commit": await CommitOriginalCloudflareSetupAsync(original, argument, token).ConfigureAwait(false); break;
                case "cf-discover":
                    if (argument.Length != 0) throw new ArgumentException("cf-discover accepts no code, scope or operation overrides.");
                    LaunchOriginalCloudflareDiscovery(original, token); break;
                case "history":
                    if (argument.Length != 0) throw new ArgumentException("history observes the SAME acknowledged or reopened Task only.");
                    await ObserveOriginalHistoryAsync(original, token).ConfigureAwait(false); break;
                case "home-requests": Write(await Acquire(original, () => Home.Permissions.GetSnapshotAsync(cancellationToken: token)).ConfigureAwait(false)); break;
                case "home-display": await DisplayHomeRequestAsync(original, argument, token).ConfigureAwait(false); break;
                case "home-accept":
                case "home-decline": await DecideHomeRequestAsync(original, argument, command == "home-accept", token).ConfigureAwait(false); break;
                case "cloud-approve":
                case "cloud-deny":
                    var id = Ids(argument, 1)[0];
                    lock (_gate)
                        if (!_displayedCloud.ContainsKey(id)) throw new InvalidOperationException("Display the actual source-issued permission event before choosing.");
                    var owner = Resolve<TaskRunCloudPermissionRemediationOwner>(original);
                    if (command == "cloud-approve") Write(await Acquire(original, () => owner.ApproveOriginalAsync(id, token)).ConfigureAwait(false));
                    else Write(await Acquire(original, () => owner.DenyOriginalAsync(id, token)).ConfigureAwait(false));
                    break;
                default: throw new ArgumentException("Unknown command. Use help.");
            }
        }

        private SpaceDevTaskAttachmentSession.SpaceDevTaskAttachment Attachment => _attachment
            ?? throw new InvalidOperationException("Attach or reopen the SAME saved Task/Run/project first.");
        private NewChatOriginalInitialTaskObservation? CurrentInitial() { lock (_gate) return _currentInitial; }
        private static Guid[] Ids(string arguments, int count)
        {
            var words = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != count) throw new ArgumentException($"Expected {count} actual nonempty IDs.");
            return words.Select(value => Guid.TryParse(value, out var id) && id != Guid.Empty
                ? id : throw new ArgumentException("Select an actual nonempty ID.")).ToArray();
        }

        private async Task LaunchInitialAsync(DesktopOriginalWorkLifetime.Original commandOriginal, string instruction, CancellationToken token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
            ProviderModelDescriptor selected;
            lock (_gate)
            {
                selected = _model ?? throw new InvalidOperationException("Select a current observed model first.");
                if (_initials.Count >= 128) throw new InvalidOperationException("Original initial-observer custody requires external retirement.");
            }
            var chat = Resolve<ChatSessionService>(commandOriginal);
            var catalogue = Resolve<CapabilityRegistryService>(commandOriginal);
            var project = _project;
            ContainerDefinition? container = null;
            if (project is not null)
            {
                var observed = _container ?? throw new InvalidOperationException("Select an existing Tasks container for the saved project.");
                var actualContainers = await Acquire(commandOriginal, () => Resolve<IContainerRepository>(commandOriginal).GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false);
                container = actualContainers.Single(value => value.Id == observed.Id && value.Mode == HavenMode.Tasks && !value.IsArchived);
                if (container != observed) throw new InvalidOperationException("The actual saved Tasks container changed. Select it again.");
                DemandProjectContainer(project, container);
                throw new InvalidOperationException("Project-backed initial submission requires the genuine current Home project-input proof. Clear the project selection for a plain Task; saved metadata alone cannot authorize submission.");
            }
            var space = _spaceId;
            var now = DateTimeOffset.UtcNow;
            var draft = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
                "Task", container?.Id, null, false, false, now, now, SpaceId: space);
            // The durable Chat producer snapshots and saves this draft. The console never presaves or retries it.
            var selectedInitial = _selectedTask.CreatePendingInitial();
            _ = _work.RunAsync(async original =>
            {
                var available = await Acquire(original, () => catalogue.DiscoverAsync(CapabilityPlatform.Linux, token)).ConfigureAwait(false);
                var capabilities = available.Select(ActiveCapability.FromDefinition).ToArray();
                NewChatOriginalInitialTaskObservation? child = null;
                Scope(original, () =>
                {
                    child = new NewChatOriginalInitialTaskObservation(chat, original, draft.Id,
                        () => chat.StartObservedOriginalTaskSendAsync(draft, instruction,
                            selected.Model with { Name = selected.Key }, EffortLevel.Medium, capabilities,
                            "Task", "", DuoMode.Solo, project?.Root.Location,
                            project is null ? null : JsonSerializer.Serialize(project.Reference), container?.Instructions, null,
                            token, availableCapabilities: capabilities),
                        PublishOriginalEventAsync, () => _work.IsRetiring,
                        chat.DemandExternalOriginalTaskObservationSourceJoin);
                    lock (_gate) { _initials.Add(child); _currentInitial = child; }
                    selectedInitial.CaptureObservation(() =>
                    {
                        var actual = child!.CurrentAcknowledgedContext
                            ?? throw new InvalidOperationException("Wait for the selected authentic initial source acknowledgement.");
                        return (actual.TaskId, actual.ExecutionId, actual.ContextId);
                    });
                });
                child!.BeginOriginalAcquisition();
                await original.AwaitAsync(child.ActualObservation).ConfigureAwait(false);
            }, _ => _selectedTask.SelectInitial(selectedInitial)); // Selection follows actual driver admission, before its body gate opens.
            Write(new { initialObservationStarted = true, conversationId = draft.Id, model = selected.Key,
                note = "Task/Run IDs will come from the actual source acknowledgement." });
        }

        private static void DemandProjectContainer(DeveloperResolvedProject project, ContainerDefinition container)
        {
            if (container.Mode != HavenMode.Tasks || container.IsArchived || string.IsNullOrWhiteSpace(container.RootPath) ||
                !Path.IsPathFullyQualified(container.RootPath) ||
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(container.RootPath)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root.Location)), StringComparison.Ordinal))
                throw new InvalidOperationException("The actual persisted Tasks container must select the SAME saved project root.");
            // Metadata observation only. Home project proof and each real action owner authorize resources.
        }

        private void LaunchColdObserver(DesktopOriginalWorkLifetime.Original commandOriginal, CancellationToken token)
        {
            var attachment = Attachment;
            var coordinator = Resolve<TaskExecutionCoordinator>(commandOriginal);
            var task = attachment.View.Task.Snapshot;
            lock (_gate)
                if (_coldObservers.Count >= 128) throw new InvalidOperationException("Original cold observation custody requires retirement.");
            _ = _work.RunAsync(async original =>
            {
                SpaceOriginalRunResumeObservation? child = null;
                Scope(original, () =>
                {
                    child = new SpaceOriginalRunResumeObservation(coordinator, original, task.TaskId, task.ExecutionId, task.ContextId,
                        () => _coldHost!.StartObservedOriginalResumeAsync(task.TaskId, task.ExecutionId, token), () => _work.IsRetiring);
                    lock (_gate) _coldObservers.Add(child);
                });
                child!.BeginOriginalAcquisition();
                var result = await original.AwaitAsync(child.ActualObservation).ConfigureAwait(false);
                Scope(original, () => Write(result));
            });
            Write(new { coldObservationRequested = true, task.TaskId, task.ExecutionId,
                note = "The protected native source must validate the actual capsule, fresh owner and resource proof. No replacement Send occurs." });
        }

        private Task PublishOriginalEventAsync(ChatStreamEvent actual)
        {
            Write(actual); // Serial JSON output preserves escaped text and the whole actual request observation.
            if (actual.Kind == ChatStreamEventKind.PermissionRequired && actual.PermissionRequest is { } request)
                lock (_gate) _displayedCloud[request.Id] = request;
            return Task.CompletedTask;
        }

        private async Task ReadOriginalDocumentAsync(DesktopOriginalWorkLifetime.Original original, string path, CancellationToken token)
        {
            var attachment = Attachment;
            var selected = await Acquire(original, () => _files!.ResolveOriginalDeveloperDocumentAsync(
                attachment.View.Project, path, () => !_work.IsRetiring, token)).ConfigureAwait(false);
            if (!selected.Succeeded || selected.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(selected.Error));
            var coordinator = Resolve<TaskExecutionCoordinator>(original);
            var current = await Acquire(original, () => coordinator.GetAsync(attachment.View.Link.TaskId, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original canonical Task is unavailable.");
            var admission = await Acquire(original, () => coordinator.TryGetIssuedAttemptAsync(
                current.TaskId, current.ExecutionId, current.Attempts.LastOrDefault()?.Id
                    ?? throw new InvalidOperationException("No original current attempt exists."), token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No genuine live attempt supports this Dev action. Cold metadata does not issue one.");
            var action = new DeveloperCanonicalActionContext(current.TaskId, current.ExecutionId, current.ContextId,
                admission.AttemptId, current.PersistenceRevision, Guid.NewGuid());
            Write(await Acquire(original, () => _session!.ExecuteOriginalAsync(attachment, action, new SpaceReadSource(selected.Value), token)).ConfigureAwait(false));
        }

        private async Task DisplayHomeRequestAsync(DesktopOriginalWorkLifetime.Original original, string id, CancellationToken token)
        {
            var request = await Acquire(original, () => Home.Permissions.ReadRequestObservationAsync(id, token)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("The actual Home request is unavailable.");
            Write(request);
            var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
            lock (_gate) _displayedHome[id] = digest;
        }
        private async Task DecideHomeRequestAsync(DesktopOriginalWorkLifetime.Original original, string id, bool accept, CancellationToken token)
        {
            string digest;
            lock (_gate) digest = _displayedHome.TryGetValue(id, out var displayed) ? displayed
                : throw new InvalidOperationException("Use home-display on the actual request before choosing.");
            var acknowledged = await Acquire(original, () => Home.Permissions.AcknowledgePromptDisplayedAsync(id, digest, token)).ConfigureAwait(false);
            if (!acknowledged.Succeeded) { Write(acknowledged); return; }
            Write(await Acquire(original, () => Home.Permissions.DecideAsync(id,
                accept ? HomeApprovalChoice.Accept : HomeApprovalChoice.Decline, cancellationToken: token)).ConfigureAwait(false));
        }

        private void Scope(DesktopOriginalWorkLifetime.Original original, Action callback)
        {
            original.DemandPublication();
            (_physical ??= []).Add(this);
            try { callback(); }
            catch (OperationCanceledException cause) { throw new AggregateException("An original synchronous console callback failed.", cause); }
            finally { _physical.RemoveAt(_physical.Count - 1); }
            original.DemandPublication();
        }
        private async Task<T> Acquire<T>(DesktopOriginalWorkLifetime.Original original, Func<Task<T>> factory)
        {
            Task<T>? raw = null; Exception? prefix = null;
            try { Scope(original, () => raw = factory()); }
            catch (Exception cause) { prefix = cause; original.Retain(cause); }
            if (raw is null) { original.ThrowRetained(); throw new InvalidOperationException("The original console source returned no Task."); }
            var result = await original.AwaitAsync(raw).ConfigureAwait(false);
            if (prefix is not null) original.ThrowRetained();
            return result;
        }
        private async Task Acquire(DesktopOriginalWorkLifetime.Original original, Func<Task> factory)
        {
            Task? raw = null; Exception? prefix = null;
            try { Scope(original, () => raw = factory()); }
            catch (Exception cause) { prefix = cause; original.Retain(cause); }
            if (raw is null) { original.ThrowRetained(); throw new InvalidOperationException("The original console source returned no Task."); }
            await original.AwaitAsync(raw).ConfigureAwait(false);
            if (prefix is not null) original.ThrowRetained();
        }

        private void DemandExternalJoin()
        {
            if (_physical?.Any(owner => ReferenceEquals(owner, this)) == true)
                throw new InvalidOperationException("The actual console callback must return before joining its process.");
            _work.DemandExternalClose();
            NewChatOriginalInitialTaskObservation[] children; lock (_gate) children = _initials.ToArray();
            foreach (var child in children) child.DemandExternalClose();
            SpaceOriginalRunResumeObservation[] cold; lock (_gate) cold = _coldObservers.ToArray();
            foreach (var child in cold) child.DemandExternalClose();
            _session?.DemandExternalOriginalRetirementJoin();
            _canonicalProcess?.DemandExternalOriginalProcessJoin();
            _developer?.DemandExternalOriginalRetirementJoin();
            _files?.DemandExternalOriginalDeveloperReadJoin();
            _managed?.DemandExternalOriginalProcessJoin();
            _registration?.DemandExternalOriginalCloudflareJoin();
        }
        internal Task CloseAndDrainAsync()
        { DemandExternalJoin(); return _shutdown.CloseAndDrainAsync(); }
        private void CleanupCallback(Action callback)
        {
            (_physical ??= []).Add(this);
            try { callback(); }
            finally { _physical.RemoveAt(_physical.Count - 1); }
        }
        private async Task RetireAndJoinProcessWorkAsync()
        {
            DemandExternalJoin(); // Whole pure preflight precedes ANY actual cancellation/seal.
            var failures = new List<Exception>();
            void Request(Action source)
            { try { CleanupCallback(source); } catch (Exception cause) { Capture(failures, null, cause); } }
            if (_canonicalProcess is not null) Request(_canonicalProcess.RequestOriginalProcessRetirement); // FIRST synchronous business seal.
            if (_registration is not null) Request(_registration.RequestOriginalCloudflareRetirement);
            if (_developer is not null) Request(_developer.RequestRetirement);
            if (_files is not null) Request(_files.RequestOriginalDeveloperReadRetirement);
            if (_managed is not null) Request(_managed.RequestStop);
            if (_session is not null) Request(_session.RequestOriginalObservationRetirement);
            Request(_work.RequestRetirement);
            Task? actual = null;
            try { CleanupCallback(() => actual = _work.CloseAndDrainAsync()); }
            catch (Exception cause) { Capture(failures, null, cause); }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { Capture(failures, actual, cause); }
            lock (_gate) foreach (var cause in failures) if (!_requests.Any(prior => ReferenceEquals(prior, cause))) _requests.Add(cause);
            Throw(failures);
        }

        private async Task StopOriginalObserversAsync()
        {
            var failures = new List<Exception>(); var closes = new List<Task>();
            NewChatOriginalInitialTaskObservation[] children; SpaceOriginalRunResumeObservation[] cold; Task[] inputs;
            lock (_gate) { children = _initials.ToArray(); cold = _coldObservers.ToArray(); inputs = _inputReads.ToArray(); }
            foreach (var child in children)
                try { CleanupCallback(child.RequestRetirement); } catch (Exception cause) { Capture(failures, null, cause); }
            foreach (var child in cold)
                try { CleanupCallback(child.RequestRetirement); } catch (Exception cause) { Capture(failures, null, cause); }
            foreach (var child in children) AcquireClose(child.CloseAndDrainAsync, closes, failures);
            foreach (var child in cold) AcquireClose(child.CloseAndDrainAsync, closes, failures);
            if (_session is not null) AcquireClose(_session.CloseAndDrainAsync, closes, failures);
            closes.AddRange(inputs);
            await JoinAll(closes, failures).ConfigureAwait(false);
            Throw(failures);
        }
        private async Task JoinOriginalBorrowersAsync()
        {
            var failures = new List<Exception>(); var closes = new List<Task>();
            lock (_gate) failures.AddRange(_requests);
            // Acquire every actual global close before any await. CF independently joins
            // the SAME canonical close before its permission/audit work settles.
            if (_canonicalProcess is not null) AcquireClose(_canonicalProcess.CloseAndSuspendOriginalProducersAsync, closes, failures);
            if (_developer is not null) AcquireClose(_developer.CloseAndDrainAsync, closes, failures);
            if (_files is not null) AcquireClose(_files.CloseOriginalDeveloperReadsAsync, closes, failures);
            if (_managed is not null) AcquireClose(_managed.CloseAndDrainAsync, closes, failures);
            if (_registration is not null) AcquireClose(_registration.CloseAndDrainOriginalCloudflareAsync, closes, failures);
            await JoinAll(closes, failures).ConfigureAwait(false);
            Throw(failures); // Failed/unknown borrowers keep shared Home/provider alive.
            if (_registration is not null)
            {
                var homeCloses = new List<Task>();
                AcquireClose(_registration.CloseOriginalHomeAfterBorrowersAsync, homeCloses, failures);
                await JoinAll(homeCloses, failures).ConfigureAwait(false);
            }
            Throw(failures);
        }
        private void AcquireClose(Func<Task> source, List<Task> closes, List<Exception> failures)
        {
            try { CleanupCallback(() => closes.Add(source() ?? throw new InvalidOperationException("An actual borrower returned no close Task."))); }
            catch (Exception cause) { Capture(failures, null, cause); }
        }
        private static async Task JoinAll(IEnumerable<Task> originals, List<Exception> failures)
        {
            foreach (var actual in originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await actual.ConfigureAwait(false); } catch (Exception cause) { Capture(failures, actual, cause); }
        }
        private Task<IStartupRecoveryFinalCleanWriter> PrepareOriginalFinalWriterAsync() =>
            (_recovery as IStartupRecoveryFinalCleanWriterSource
                ?? throw new InvalidOperationException("The actual startup final-writer owner is unavailable."))
            .PrepareFinalCleanWriterAsync(CancellationToken.None);
        private Task DisposeOriginalProviderAsync() => _provider?.DisposeAsync().AsTask() ?? Task.CompletedTask;
    }
}
