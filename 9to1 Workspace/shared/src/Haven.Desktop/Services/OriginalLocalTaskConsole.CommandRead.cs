using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private FilesDeveloperOriginalCurrentProjectExecutionBridge? _currentProjectExecution;
        private HomeDeveloperWorkspaceExecutionConsentSource? _executionConsent;
        private FilesDeveloperOriginalCurrentProjectExecutionResolver? _currentExecutionResolver;
        private HomeDeveloperWorkspaceExecutionActionPolicySource? _currentExecutionPolicy;

        private void PrepareOriginalCurrentProjectExecutionPolicies()
        {
            if (!ProjectConfigurationRequested) return;
            _currentExecutionResolver = new(() => _currentProjectExecution
                ?? throw new InvalidOperationException("The SAME actual current-project execution bridge is not captured."));
            _currentExecutionPolicy = new();
        }
        private void ConfigureOriginalCurrentProjectExecutionOwners(IServiceCollection services)
        {
            if (!ProjectConfigurationRequested) return;
            services.AddHavenOwnedCurrentProjectExecution(_registration!.OriginalHome,
                _currentExecutionResolver!, _currentExecutionPolicy!);
        }
        private void DemandOriginalCurrentProjectExecutionJoins()
        {
            _currentProjectExecution?.DemandExternalOriginalExecutionTrustJoin();
            _executionConsent?.DemandExternalOriginalProcessStartConsentJoin();
            IDeveloperOriginalProjectCommandRead[] reads;
            lock (_gate) reads = _ownedCommandReads.ToArray();
            foreach (var read in reads) read.DemandExternalOriginalJoin();
        }
        private void RequestOriginalCurrentProjectExecutionRetirement(List<Exception> failures)
        {
            void Request(Action request)
            { try { CleanupCallback(request); } catch (Exception cause) { Capture(failures, null, cause); } }
            if (_currentProjectExecution is not null) Request(_currentProjectExecution.RequestOriginalExecutionRetirement);
            if (_executionConsent is not null) Request(_executionConsent.RequestOriginalExecutionRetirement);
        }
        private void AcquireOriginalCurrentProjectExecutionCloses(List<Task> closes, List<Exception> failures)
        {
            if (_currentProjectExecution is not null) AcquireClose(_currentProjectExecution.CloseAndDrainOriginalExecutionAsync, closes, failures);
            if (_executionConsent is not null) AcquireClose(_executionConsent.CloseAndDrainOriginalExecutionsAsync, closes, failures);
        }

        private readonly List<IDeveloperOriginalProjectCommandRead> _commandReads = [];
        private readonly HashSet<IDeveloperOriginalProjectCommandRead> _ownedCommandReads = new(ReferenceEqualityComparer.Instance);
        private readonly List<FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation> _projectCommands = [];

        private void CaptureOriginalCurrentProjectExecution(DesktopOriginalWorkLifetime.Original original)
        {
            if (!ProjectConfigurationRequested) return;
            var reads = Resolve<IDeveloperOriginalProjectCommandReadSource>(original);
            var actual = Resolve<FilesDeveloperOriginalCurrentProjectExecutionBridge>(original,
                value => _currentProjectExecution = value);
            var tools = Resolve<ITaskRunToolActionOwner>(original);
            var consent = Resolve<HomeDeveloperWorkspaceExecutionConsentSource>(original, value => _executionConsent = value);
            Scope(original, () =>
            {
                if (!ReferenceEquals(reads, RequireCapturedOriginalProjectSource()) ||
                    !ReferenceEquals(actual, Resolve<IDeveloperWorkspaceTrustService>(original)) ||
                    !ReferenceEquals(actual, Resolve<IDeveloperWorkspaceOriginalProjectExecutionTrustService>(original)) ||
                    !actual.IsBoundToOriginalToolOwner(tools) ||
                    !ReferenceEquals(consent, Resolve<IWorkspaceOriginalProcessStartConsentSource>(original)))
                    throw new InvalidOperationException("The SAME configured current project READ, execution bridge and canonical tool owner are required.");
            });
        }

        private void LaunchOriginalDocumentRead(DesktopOriginalWorkLifetime.Original commandOriginal,
            string path, CancellationToken commandToken)
        {
            commandToken.ThrowIfCancellationRequested();
            var attachment = Attachment;
            var source = RequireCapturedOriginalProjectSource();
            var bridge = _currentProjectExecution
                ?? throw new InvalidOperationException("The original current-project execution bridge is unavailable.");
            lock (_gate)
                if (_commandReads.Count >= 128 || _projectCommands.Count >= 128)
                    throw new InvalidOperationException("Current project command custody requires external process retirement.");
            Scope(commandOriginal, () =>
            {
                if (!ReferenceEquals(Resolve<IDeveloperOriginalProjectCommandReadSource>(commandOriginal), source))
                    throw new InvalidOperationException("The current project command READ source changed.");
            });
            // The background body retains this exact attachment, including its original
            // Task/Run/project. A later console selection cannot change an admitted command.
            _ = _work.RunAsync(original => ReadOriginalCurrentProjectDocumentAsync(original, attachment,
                source, bridge, path, original.Token));
            Write(new { documentReadRequested = true, attachment.View.Link.TaskId,
                attachment.View.Link.ExecutionId, path,
                note = "Use home-requests, home-display and an explicit decision while the SAME command waits for its current READ and execution review." });
        }

        private async Task ReadOriginalCurrentProjectDocumentAsync(DesktopOriginalWorkLifetime.Original original,
            SpaceDevTaskAttachmentSession.SpaceDevTaskAttachment attachment,
            HomeColdProjectReadReconciliation source,
            FilesDeveloperOriginalCurrentProjectExecutionBridge bridge, string path, CancellationToken token)
        {
            var sources = new OriginalConsoleSources(original);
            var failures = new List<Exception>();
            IDeveloperOriginalProjectCommandRead? read = null;
            FilesDeveloperOriginalCurrentProjectExecutionBridge.OriginalCommandInvocation? invocation = null;
            DeveloperOperationResult<DeveloperActionObservation>? observation = null;
            var ownedRead = false;
            try
            {
                var attachedSnapshot = attachment.View.Task.Snapshot
                    ?? throw new InvalidOperationException("The SAME original attachment has no canonical Task snapshot.");
                var coordinator = Resolve<TaskExecutionCoordinator>(original);
                var current = await Acquire(original, () => coordinator.GetAsync(attachment.View.Link.TaskId, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The original canonical Task is unavailable.");
                if (current.TaskId != attachment.View.Link.TaskId || current.ExecutionId != attachment.View.Link.ExecutionId ||
                    current.ContextId != attachedSnapshot.ContextId)
                    throw new InvalidOperationException("The admitted document command must retain the SAME reopened Task/Run/context.");
                var conversation = await Acquire(original, () => Resolve<IConversationRepository>(original)
                    .GetAsync(current.ContextId, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The actual persisted Task conversation is unavailable.");
                var containers = await Acquire(original, () => Resolve<IContainerRepository>(original)
                    .GetByModeAsync(HavenMode.Tasks, token)).ConfigureAwait(false);
                var container = containers.Single(value => value.Id == conversation.ContainerId && !value.IsArchived);
                DemandProjectContainer(attachment.View.Project, container);
                Task<IDeveloperOriginalProjectCommandRead>? preparation = null;
                try
                {
                    Scope(original, () => preparation = source.AcquireOriginalCommandReadWithinSourceAsync(conversation,
                        container, JsonSerializer.Serialize(attachment.View.Project.Reference),
                        callback => Scope(original, callback), sources.Retain, token));
                }
                catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                if (preparation is null) throw new InvalidOperationException("The genuine current command READ returned no preparation Task.");
                read = await sources.AwaitAsync(preparation).ConfigureAwait(false);
                // Historical custody comes before any live-use or post-publication gate.
                lock (_gate) _commandReads.Add(read);
                CleanupCallback(() => ownedRead = source.IsOwnedOriginalCommandRead(read));
                if (!ownedRead) throw new UnauthorizedAccessException("The returned command READ has no SAME source cleanup identity.");
                lock (_gate) _ownedCommandReads.Add(read);
                Throw(failures);
                Scope(original, () =>
                {
                    if (!source.IsIssuedOriginalCommandRead(read) || !ReferenceEquals(read.OriginalPreparation, preparation))
                        throw new UnauthorizedAccessException("The actual current command READ preparation is not live and issued by the SAME source.");
                });
                var selected = await Acquire(original, () => _files!.ResolveOriginalDeveloperDocumentAsync(
                    attachment.View.Project, path, () => !_work.IsRetiring, token)).ConfigureAwait(false);
                if (!selected.Succeeded || selected.Value is null)
                    throw new InvalidOperationException(JsonSerializer.Serialize(selected.Error));
                // Current canonical attempt/revision is re-read after the manual READ wait.
                current = await Acquire(original, () => coordinator.GetAsync(attachment.View.Link.TaskId, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The original canonical Task is unavailable.");
                if (current.TaskId != attachment.View.Link.TaskId || current.ExecutionId != attachment.View.Link.ExecutionId ||
                    current.ContextId != attachment.View.Link.ConversationId)
                    throw new InvalidOperationException("The current canonical Task/Run/context changed during the command READ.");
                var attempt = await Acquire(original, () => coordinator.TryGetIssuedAttemptAsync(current.TaskId, current.ExecutionId,
                    current.Attempts.LastOrDefault()?.Id ?? throw new InvalidOperationException("No original current attempt exists."), token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("No genuine live attempt supports this Dev action. Cold metadata does not issue one.");
                var action = new DeveloperCanonicalActionContext(current.TaskId, current.ExecutionId, current.ContextId,
                    attempt.AttemptId, current.PersistenceRevision, Guid.NewGuid());
                try
                {
                    Scope(original, () => invocation = bridge.CaptureOriginalCommandWithinSource(read,
                        attachment.View.Project.Reference, action,
                        () => _session!.ExecuteOriginalAsync(attachment, action, new SpaceReadSource(selected.Value), token),
                        callback => Scope(original, callback), sources.Retain, token));
                }
                catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                if (invocation is null) throw new InvalidOperationException("No actual current-project command invocation was acquired.");
                lock (_gate) _projectCommands.Add(invocation);
            }
            catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
            finally
            {
                // The record preserves the exact borrowed command even if its finite
                // publication callback failed. Join it and the record BEFORE retiring READ.
                if (invocation is not null)
                {
                    try { await sources.AwaitAsync(invocation.OriginalCommandTask).ConfigureAwait(false); }
                    catch (Exception cause) { Capture(failures, invocation.OriginalCommandTask, cause); }
                    Task<DeveloperOperationResult<DeveloperActionObservation>>? joined = null;
                    try { CleanupCallback(() => joined = invocation.JoinOriginalAsync()); }
                    catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                    if (joined is not null)
                        try { observation = await sources.AwaitAsync(joined).ConfigureAwait(false); }
                        catch (Exception cause) { Capture(failures, joined, cause); }
                }
                await sources.JoinRemainingAsync(failures).ConfigureAwait(false);
                if (read is not null && ownedRead)
                {
                    try { CleanupCallback(read.RequestOriginalRetirement); }
                    catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                    Task? close = null;
                    try { CleanupCallback(() => close = read.CloseAndDrainOriginalAsync()); }
                    catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                    if (close is not null)
                        try { await sources.AwaitAsync(close).ConfigureAwait(false); }
                        catch (Exception cause) { Capture(failures, close, cause); }
                }
                await sources.JoinRemainingAsync(failures).ConfigureAwait(false);
            }
            Throw(failures);
            Scope(original, () => Write(observation ?? throw new InvalidOperationException("No actual Dev command observation was returned.")));
        }

    }
}
