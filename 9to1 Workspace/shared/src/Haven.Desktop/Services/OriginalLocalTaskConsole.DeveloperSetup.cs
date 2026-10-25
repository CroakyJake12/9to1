using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private HomeDeveloperProjectReadResourceResolver? _setupReadResolver;
        private FilesDeveloperOriginalSetupDestinationResolver? _setupDestinationResolver;
        private HomeDeveloperProjectReadActionPolicySource? _setupReadPolicy;
        private HomeDeveloperProjectSetupActionPolicySource? _setupPolicy;
        private HomeDeveloperProjectEditResourceResolver? _setupEditResolver;
        private HomeDeveloperProjectEditActionPolicySource? _setupEditPolicy;
        private HomeDeveloperManualProjectExecutionResourceResolver? _setupManualRunResolver;
        private HomeDeveloperManualProjectExecutionActionPolicySource? _setupManualRunPolicy;
        private HomeDeveloperManualProjectExecutionAdmissionSource? _setupManualRuns;
        private DeveloperManualProjectProcessOwner? _setupManualProcessOwner;
        private CancellationTokenSource? _manualConsoleRunStop;
        private FilesDeveloperOriginalSourceSelection? _setupSelections;
        private IDeveloperProjectOriginalPhysicalCaptureSource? _setupPhysical;
        private FilesDeveloperOriginalSetupScopeSource? _setupScopes;
        private FilesDeveloperOriginalFolderSetupProducer? _setupSteps;
        private HomeDeveloperProjectReadAdmissionSource? _setupReads;
        private HomeDeveloperProjectEditAdmissionSource? _setupEdits;
        private HomeDeveloperProjectSetupPermissionSource? _setupPermissions;
        private HomeDeveloperProjectSetupJournal? _setupJournal;
        private FileDeveloperWorkspaceStore? _setupWorkspaceStore;
        private readonly Dictionary<string, (Guid SetupId, Guid WorkspaceId)> _projectRegistrations = new(StringComparer.Ordinal);

        private void PrepareOriginalDeveloperPolicies()
        {
            _setupReadResolver = new(() => _setupReads
                ?? throw new InvalidOperationException("The SAME captured Home source READ owner is unavailable."));
            _setupDestinationResolver = new(() => _setupScopes
                ?? throw new InvalidOperationException("The SAME captured Files destination owner is unavailable."));
            _setupReadPolicy = new(); _setupPolicy = new();
            _setupEditResolver = new(() => _setupEdits
                ?? throw new InvalidOperationException("The SAME captured Home source EDIT owner is unavailable."));
            _setupEditPolicy = new();
            _setupManualRunResolver = new(() => _setupManualRuns
                ?? throw new InvalidOperationException("The SAME captured Home manual Run owner is unavailable."));
            _setupManualRunPolicy = new();
        }

        private void ConfigureOriginalDeveloperSetupOwners(IServiceCollection services)
        {
            var domain = _registration!.OriginalHome;
            services.AddHavenOwnedDeveloperSourceReads(domain,
                provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), _setupReadResolver!, _setupReadPolicy!);
            services.AddHavenOwnedDeveloperSourceEdits(domain,
                provider => provider.GetRequiredService<IDeveloperProjectOriginalEditScopeSource>(),
                _setupEditResolver!, _setupEditPolicy!);
            services.AddHavenOwnedDeveloperManualExecution(domain,
                provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), _setupManualRunResolver!, _setupManualRunPolicy!);
            services.AddHavenOwnedDeveloperSetups(domain,
                provider => provider.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(),
                provider => provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
                _ => _setupSteps
                    ?? throw new InvalidOperationException("The SAME captured Files step outcome owner is unavailable."), _setupPolicy!);
            services.AddFilesOriginalDeveloperSetups(_setupDestinationResolver!, provider =>
                (provider.GetRequiredService<IWorkspaceToolService>() as Haven.Infrastructure.WorkspaceToolService
                    ?? throw new InvalidOperationException("The SAME actual workspace kernel is required."))
                .CreateOriginalDeveloperCaptureSource(provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                    // Construction remains lazy for the genuine Files/Home cycle.
                    // Capture the SAME configured owners before public startup; disposal
                    // guards still walk them after the provider stops service resolution.
                    () => _setupSelections
                        ?? throw new InvalidOperationException("The SAME captured Files source selection owner is unavailable."),
                    () => _setupPermissions
                        ?? throw new InvalidOperationException("The SAME captured Home setup permission owner is unavailable."),
                    () => _setupWorkspaceStore
                        ?? throw new InvalidOperationException("The SAME captured Dev workspace store is unavailable."),
                    () => _setupSteps
                        ?? throw new InvalidOperationException("The SAME captured Files step outcome owner is unavailable."),
                    provider.GetRequiredService<HomeDeveloperProjectEditAdmissionSource>()),
                provider => provider.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
                provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>(),
                provider => provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>(),
                provider => provider.GetRequiredService<FileDeveloperWorkspaceStore>());
        }

        private void CaptureOriginalDeveloperSetupOwners(DesktopOriginalWorkLifetime.Original original)
        {
            Resolve<FileDeveloperWorkspaceStore>(original, value => _setupWorkspaceStore = value);
            Resolve<HomeDeveloperProjectReadAdmissionSource>(original, value => _setupReads = value);
            Resolve<HomeDeveloperProjectEditAdmissionSource>(original, value => _setupEdits = value);
            Resolve<HomeDeveloperManualProjectExecutionAdmissionSource>(original, value => _setupManualRuns = value);
            Resolve<DeveloperManualProjectProcessOwner>(original, value => _setupManualProcessOwner = value);
            Resolve<HomeDeveloperProjectSetupJournal>(original, value => _setupJournal = value);
            Resolve<HomeDeveloperProjectSetupPermissionSource>(original, value => _setupPermissions = value);
            Resolve<FilesDeveloperOriginalSourceSelection>(original, value => _setupSelections = value);
            Resolve<IDeveloperProjectOriginalPhysicalCaptureSource>(original, value => _setupPhysical = value);
            Resolve<FilesDeveloperOriginalSetupScopeSource>(original, value => _setupScopes = value);
            Resolve<FilesDeveloperOriginalFolderSetupProducer>(original, value => _setupSteps = value);
        }


        private void DemandOriginalDeveloperSetupJoins()
        {
            _setupManualProcessOwner?.DemandExternalOriginalJoin();
            _setupManualRuns?.DemandExternalOriginalManualExecutionJoin();
            _setupSteps?.DemandExternalOriginalSetupStepOutcomeJoin();
            _setupScopes?.DemandExternalOriginalSetupScopeJoin();
            _setupSelections?.DemandExternalOriginalCaptureJoin();
            _setupPhysical?.DemandExternalOriginalJoin();
            _setupJournal?.DemandExternalOriginalRetirementJoin();
            _setupReads?.DemandExternalOriginalReadAdmissionJoin();
            _setupEdits?.DemandExternalOriginalEditAdmissionJoin();
            _setupPermissions?.DemandExternalOriginalSetupJoin();
        }
        private void RequestOriginalDeveloperSetupRetirement(List<Exception> failures)
        {
            // Seal new setup admission now. The same permission's final completion
            // still reads its live journal/source/scope/outcome/READ dependencies.
            if (_setupManualProcessOwner is not null)
                try { CleanupCallback(_setupManualProcessOwner.RequestOriginalRetirement); }
                catch (Exception cause) { Capture(failures, null, cause); }
            if (_setupPermissions is not null)
                try { CleanupCallback(_setupPermissions.RequestOriginalSetupRetirement); }
                catch (Exception cause) { Capture(failures, null, cause); }
        }
        private async Task CloseAndDrainOriginalDeveloperSetupsAsync()
        {
            var failures = new List<Exception>();
            // Process exit/output and its actual per-operation Home audit settle before any
            // borrowed Files selection, READ source or native capture provider retires.
            var processCloses = new List<Task>();
            if (_setupManualProcessOwner is not null) AcquireClose(_setupManualProcessOwner.CloseAndDrainOriginalAsync, processCloses, failures);
            await JoinAll(processCloses, failures).ConfigureAwait(false);
            var runCloses = new List<Task>();
            if (_setupManualRuns is not null) AcquireClose(_setupManualRuns.CloseAndDrainOriginalManualExecutionsAsync, runCloses, failures);
            await JoinAll(runCloses, failures).ConfigureAwait(false);
            var permissionCloses = new List<Task>();
            if (_setupPermissions is not null)
                AcquireClose(_setupPermissions.CloseAndDrainOriginalSetupsAsync, permissionCloses, failures);
            await JoinAll(permissionCloses, failures).ConfigureAwait(false);

            // Preserve every actual failed permission and independently drain every
            // dependency. Retire these issuers only after final completion/audit settles;
            // persisted ACK rows never replace the same private completion authority.
            void Request(Action request)
            { try { CleanupCallback(request); } catch (Exception cause) { Capture(failures, null, cause); } }
            if (_setupSteps is not null) Request(_setupSteps.RequestOriginalFolderSetupRetirement);
            if (_setupScopes is not null) Request(_setupScopes.RequestOriginalSetupScopeRetirement);
            if (_setupSelections is not null) Request(_setupSelections.RequestOriginalSelectionRetirement);
            if (_setupPhysical is not null) Request(_setupPhysical.RequestOriginalCaptureRetirement);
            if (_setupJournal is not null) Request(_setupJournal.RequestRetirement);
            if (_setupReads is not null) Request(_setupReads.RequestOriginalReadRetirement);
            var closes = new List<Task>();
            if (_setupSteps is not null) AcquireClose(_setupSteps.CloseAndDrainOriginalFolderSetupsAsync, closes, failures);
            if (_setupScopes is not null) AcquireClose(_setupScopes.CloseAndDrainOriginalSetupScopesAsync, closes, failures);
            if (_setupSelections is not null) AcquireClose(_setupSelections.CloseAndDrainOriginalSelectionsAsync, closes, failures);
            if (_setupPhysical is not null) AcquireClose(_setupPhysical.CloseAndDrainOriginalCapturesAsync, closes, failures);
            if (_setupJournal is not null) AcquireClose(_setupJournal.CloseAndDrainAsync, closes, failures);
            if (_setupReads is not null) AcquireClose(_setupReads.CloseAndDrainOriginalReadsAsync, closes, failures);
            if (_setupEdits is not null) AcquireClose(_setupEdits.CloseAndDrainOriginalEditsAsync, closes, failures);
            await JoinAll(closes, failures).ConfigureAwait(false);
            Throw(failures);
        }

        private void LaunchOriginalManualProjectRun(DesktopOriginalWorkLifetime.Original commandOriginal,
            string argument, CancellationToken token)
        {
            var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (4 or 5) || !Guid.TryParse(parts[0], out var workspace) || workspace == Guid.Empty ||
                !Guid.TryParse(parts[1], out var project) || project == Guid.Empty || !Guid.TryParse(parts[2], out var root) || root == Guid.Empty ||
                string.IsNullOrWhiteSpace(parts[3]) || parts[3].Length > 128 || parts[3].Any(char.IsControl))
                throw new ArgumentException("Use project-run <saved workspace GUID> <saved project GUID> <saved root GUID> <exact configuration ID> [exact repository binding ID]. IDs containing spaces require the native selector.");
            token.ThrowIfCancellationRequested();
            Scope(commandOriginal, () =>
            {
                if (_setupManualProcessOwner is null || _setupManualRuns is null || _setupWorkspaceStore is null || _developer is null)
                    throw new InvalidOperationException("The genuine configured manual Run owners are unavailable.");
            });
            var configurationId = parts[3]; var repository = parts.Length == 5 ? parts[4] : null;
            _ = _work.RunAsync(async original =>
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(original.Token);
                lock (_gate)
                {
                    if (_manualConsoleRunStop is not null) throw new InvalidOperationException("One manual console Run is already retained. Inspect it or request project-run-stop before another launch.");
                    _manualConsoleRunStop = stop;
                }
                try
                {
                var stored = await Acquire(original, () => _setupWorkspaceStore!.GetAsync(workspace, original.Token)).ConfigureAwait(false);
                if (!stored.Succeeded || stored.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(stored.Error));
                var selectedProject = stored.Value.Projects.Single(value => value.ProjectId == project);
                if (!selectedProject.RootIds.Contains(root)) throw new InvalidOperationException("Select an actual root of the SAME saved project.");
                var reference = new DeveloperProjectReference(workspace, stored.Value.Revision, project, selectedProject.Revision, root, repository);
                var resolved = await Acquire(original, () => _developer!.ResolveAsync(reference, original.Token)).ConfigureAwait(false);
                if (!resolved.Succeeded || resolved.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(resolved.Error));
                var choices = DeveloperRunConfigurationSelection.Observe(resolved.Value);
                var choice = choices.Value?.SingleOrDefault(value => value.Configuration.Id == configurationId)
                    ?? throw new InvalidOperationException("The saved project has no unambiguous declared Run configuration with that ID.");
                if (choice.UnavailableReason is { } reason) throw new NotSupportedException(reason);
                Scope(original, () => Write(new { manualRunReview = choice.Summary, choice.DefinitionSha256,
                    note = "This is not permission. Display and decide the separate source READ and exact console Run in Home while this original operation waits." }));
                var observed = await Acquire(original, () => _setupManualProcessOwner!.RunAsync(resolved.Value, choice,
                    body => Scope(original, body), stop.Token)).ConfigureAwait(false);
                Scope(original, () => Write(new { manualRun = observed.OperationId, project = observed.Project,
                    observed.ConfigurationId, observed.DefinitionSha256, observed.ProcessSucceeded, observed.Process,
                    observed.CleanupCompleted, physical = observed.PhysicalOutcome is { } physical
                        ? new { physical.OriginalReceiptReference, physical.Effects, physical.KnownNoEffect, physical.OutcomeUnknown } : null,
                    causes = observed.OriginalErrors.Select(error => error.ToString()).ToArray(),
                    note = "No Task was created. This retained original is never automatically replayed." }));
                foreach (var error in observed.OriginalErrors) original.Retain(error);
                original.ThrowRetained();
                }
                finally { lock (_gate) _manualConsoleRunStop = null; }
            });
            Write(new { manualRunRequested = true, workspace, project, root, configurationId,
                note = "No Task/container is required. Use home-requests/home-display and explicitly accept or decline each original review. Process output/lifecycle will be reported by the retained background operation." });
        }

        private void StopOriginalManualProjectRun(DesktopOriginalWorkLifetime.Original original)
        {
            CancellationTokenSource stop;
            lock (_gate) stop = _manualConsoleRunStop
                ?? throw new InvalidOperationException("There is no current retained manual console Run to stop.");
            Scope(original, stop.Cancel); // Actual callbacks execute outside the host lock.
            Write(new { manualRunStopRequested = true, note = "The original process owner joins its actual stop, exit, output and cleanup before reporting the outcome." });
        }

        private async Task ListOriginalFilesAsync(DesktopOriginalWorkLifetime.Original original,
            string argument, CancellationToken token)
        {
            var parent = argument.Length == 0 ? (Guid?)null : Ids(argument, 1)[0];
            var retained = new OriginalConsoleSources(original); var failures = new List<Exception>();
            try
            {
                var actor = await Acquire(original, () => Home.Profiles.GetCurrentAsync(
                    callback => Scope(original, callback), retained.Retain, token).AsTask()).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual current Home resource actor is unavailable.");
                var configuration = await Acquire(original, () => _nativeFiles!.GetConfigurationAsync(token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Configure the explicit empty Files root first.");
                var page = await Acquire(original, () => _files!.ListAsync(actor, parent, token: token,
                    expectedStoreId: configuration.StoreId)).ConfigureAwait(false);
                Scope(original, () => Write(page));
            }
            catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
            finally { await retained.JoinRemainingAsync(failures).ConfigureAwait(false); }
            Throw(failures);
        }

        private void LaunchOriginalExistingProjectRegistration(DesktopOriginalWorkLifetime.Original commandOriginal,
            string argument, CancellationToken token)
        {
            var separator = argument.IndexOf(' ');
            if (separator < 1) throw new ArgumentException("Use project-register <write or observed folder GUID> <explicit existing absolute project path>.");
            var destination = argument[..separator]; var root = argument[(separator + 1)..].Trim();
            if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Select an existing absolute project path below the configured Files root.");
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (destination != "write" && (!Guid.TryParse(destination, out var folder) || folder == Guid.Empty))
                throw new ArgumentException("Choose write or an observed canonical destination folder GUID.");
            token.ThrowIfCancellationRequested();
            var setupId = Guid.NewGuid(); var workspaceId = Guid.NewGuid();
            Scope(commandOriginal, () =>
            {
                if (_setupSelections is null || _setupScopes is null || _setupSteps is null ||
                    _setupReads is null || _setupPermissions is null || _setupJournal is null)
                    throw new InvalidOperationException("The genuine configured Files/Home setup owners are unavailable.");
                lock (_gate)
                    if (_projectRegistrations.ContainsKey(root))
                        throw new InvalidOperationException("This original selected project already has a retained registration. Inspect its same setup; unknown work cannot be replayed.");
            });
            var capturedRoot = root;
            _ = _work.RunAsync(original => RegisterOriginalExistingProjectAsync(original, destination, capturedRoot,
                setupId, workspaceId, original.Token), _ =>
            {
                lock (_gate)
                {
                    if (_projectRegistrations.Count >= 128 || !_projectRegistrations.TryAdd(capturedRoot, (setupId, workspaceId)))
                        throw new InvalidOperationException("Original setup identity custody is full or this selected project is already retained.");
                }
            });
            Write(new { projectRegistrationRequested = true, setupId, workspaceId, selectedProjectRoot = root,
                note = "Retain these IDs. Display and explicitly decide the separate source READ and setup manifest review while the SAME operation waits. Pending or unknown steps are never retried." });
        }

        private async Task RegisterOriginalExistingProjectAsync(DesktopOriginalWorkLifetime.Original original,
            string destination, string selectedRoot, Guid setupId, Guid workspaceId, CancellationToken token)
        {
            var configuration = await Acquire(original, () => _nativeFiles!.GetConfigurationAsync(token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Configure the chosen empty Files root first.");
            var destinationFolder = destination == "write"
                ? configuration.AppFolders["write"].Value : Guid.Parse(destination);
            var selection = await Acquire(original, () => _setupSelections!.SelectOriginalExistingProjectAsync(
                configuration.StoreId, selectedRoot, token)).ConfigureAwait(false);
            var read = await Acquire(original, () => _setupReads!.AcquireOriginalAsync(selection, token)).ConfigureAwait(false);
            var capture = await Acquire(original, () => _setupSelections!.CaptureOriginalAsync(selection, read, token)).ConfigureAwait(false);
            var actualDestination = await Acquire(original, () => _setupScopes!.CaptureOriginalDestinationAsync(
                configuration.StoreId, new HavenOS.Files.HostedItemId(destinationFolder), token)).ConfigureAwait(false);
            var prepared = await Acquire(original, () => _setupJournal!.PrepareExistingOriginalAsync(setupId,
                actualDestination.OriginalActor, actualDestination.OriginalStoreId, actualDestination.OriginalConfigurationDigest,
                actualDestination.OriginalFolderId, actualDestination.OriginalFolderRevision, capture, workspaceId, 0,
                Path.GetFileName(selectedRoot), token)).ConfigureAwait(false);
            Scope(original, () => Write(new { originalPreparedSetup = prepared.Intent }));
            await Acquire(original, () => _setupScopes!.BindOriginalAsync(actualDestination, prepared, capture, token)).ConfigureAwait(false);
            var permission = await Acquire(original, () => _setupPermissions!.AcquireOriginalAsync(prepared.Intent, capture, token)).ConfigureAwait(false);
            FilesDeveloperOriginalFolderSetupProducer.WorkspaceMetadataStep? savedStep = null;
            foreach (var step in prepared.Intent.Steps)
            {
                // Execute each SAME declared step exactly once. Its genuine effector owns
                // admission, complete raw/native cleanup, Home validation and sole journal ACK.
                switch (step.Kind)
                {
                    case DeveloperProjectSetupStepKind.CreateProjectFolder:
                    case DeveloperProjectSetupStepKind.CreateChildFolder:
                        Write(await Acquire(original, () => _setupSteps!.ExecuteOriginalFolderStepAsync(prepared, capture, permission, step, token)).ConfigureAwait(false)); break;
                    case DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory:
                    case DeveloperProjectSetupStepKind.ObserveExistingChildDirectory:
                        Write(await Acquire(original, () => _setupSteps!.ExecuteOriginalExistingDirectoryStepAsync(prepared, capture, permission, step, token)).ConfigureAwait(false)); break;
                    case DeveloperProjectSetupStepKind.RegisterProjectFolder:
                        Write(await Acquire(original, () => _setupSteps!.ExecuteOriginalProjectRegistrationStepAsync(prepared, capture, permission, step, token)).ConfigureAwait(false)); break;
                    case DeveloperProjectSetupStepKind.RegisterExistingFileMetadata:
                    case DeveloperProjectSetupStepKind.RegisterMaterialization:
                        Write(await Acquire(original, () => _setupSteps!.ExecuteOriginalExistingFileStepAsync(prepared, capture, permission, step, token)).ConfigureAwait(false)); break;
                    case DeveloperProjectSetupStepKind.SaveDevWorkspace:
                        savedStep = await Acquire(original, () => _setupSteps!.ExecuteOriginalWorkspaceMetadataStepAsync(prepared, capture, permission, step, token)).ConfigureAwait(false);
                        Write(savedStep); break;
                    default: throw new NotSupportedException("This explicit register-existing command cannot dispatch a mutation/import step.");
                }
            }
            var checkpoint = await Acquire(original, () => _setupJournal!.ReadCheckpointAsync(setupId,
                actualDestination.OriginalActor, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original setup checkpoint is unavailable.");
            if (checkpoint.Observations.Length != prepared.Intent.Steps.Length ||
                checkpoint.Observations.Any(row => row.State != DeveloperProjectSetupStepState.Acknowledged))
                throw new InvalidOperationException("The retained setup is not fully acknowledged; preserve its original IDs without replay.");
            var actualSaved = savedStep?.OriginalStoreResult.Workspace
                ?? throw new InvalidOperationException("No actual strict workspace save and journal ACK product was returned.");
            var actualSavedProject = actualSaved.Projects.Single(value => value.ProjectId == prepared.Intent.ProjectId);
            var reference = new DeveloperProjectReference(actualSaved.WorkspaceId, actualSaved.Revision, actualSavedProject.ProjectId,
                actualSavedProject.Revision, prepared.Intent.RootId,
                actualSaved.SourceControlBindings.SingleOrDefault(value => value.RootId == prepared.Intent.RootId)?.BindingId);
            var saved = await Acquire(original, () => _developer!.ResolveAsync(reference, token)).ConfigureAwait(false);
            if (!saved.Succeeded || saved.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(saved.Error));
            Scope(original, () => Write(new { originalSetupAcknowledged = checkpoint, savedProject = saved.Value,
                note = "Explicitly create a Tasks container for this saved project, then select project with its actual IDs. This setup does not create or resume a Task." }));
        }

        private async Task CreateOriginalProjectContainerAsync(DesktopOriginalWorkLifetime.Original original,
            string argument, CancellationToken token)
        {
            var parts = argument.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !Guid.TryParse(parts[0], out var workspaceId) || workspaceId == Guid.Empty ||
                !Guid.TryParse(parts[1], out var projectId) || projectId == Guid.Empty ||
                !Guid.TryParse(parts[2], out var rootId) || rootId == Guid.Empty ||
                string.IsNullOrWhiteSpace(parts[3]) || parts[3].Length > 120 || parts[3].Any(char.IsControl))
                throw new ArgumentException("Use container-create <saved workspace GUID> <saved project GUID> <saved root GUID> <name>.");
            var stored = await Acquire(original, () => Resolve<IDeveloperWorkspaceStore>(original).GetAsync(workspaceId, token)).ConfigureAwait(false);
            if (!stored.Succeeded || stored.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(stored.Error));
            var project = stored.Value.Projects.Single(value => value.ProjectId == projectId);
            if (!project.RootIds.Contains(rootId)) throw new InvalidOperationException("Select an actual saved root of the SAME project.");
            var reference = new DeveloperProjectReference(workspaceId, stored.Value.Revision, projectId, project.Revision, rootId,
                stored.Value.SourceControlBindings.SingleOrDefault(value => value.RootId == rootId)?.BindingId);
            var resolved = await Acquire(original, () => _developer!.ResolveAsync(reference, token)).ConfigureAwait(false);
            if (!resolved.Succeeded || resolved.Value is null) throw new InvalidOperationException(JsonSerializer.Serialize(resolved.Error));
            var repository = Resolve<IContainerRepository>(original);
            var now = DateTimeOffset.UtcNow;
            var once = new ContainerDefinition(Guid.NewGuid(), HavenMode.Tasks, parts[3].Trim(), resolved.Value.Root.Location,
                JsonSerializer.Serialize(resolved.Value.Reference), "", now, now);
            DemandProjectContainer(resolved.Value, once);
            Scope(original, () => Write(new { containerCreationRequested = once.Id, project = resolved.Value.Reference }));
            await Acquire(original, () => repository.UpsertAsync(once, token)).ConfigureAwait(false);
            var containers = await Acquire(original, () => repository.GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false);
            var actual = containers.Single(value => value.Id == once.Id);
            if (actual != once) throw new InvalidOperationException("The actual Tasks container readback differs. Preserve the original ID; no replacement is created.");
            Scope(original, () => Write(new { originalContainerSaved = actual,
                note = "Container metadata selects the saved project. Genuine initial input/current manual READ and action authorities still authorize use." }));
        }
    }
}
