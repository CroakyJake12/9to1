using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    private sealed partial class Host
    {
        private NativePersonalTaskColdRecoveryConfiguration? _coldConfiguration;
        private HomeColdProjectReadReconciliation? _projectResources;
        private readonly List<ITaskRunColdOriginalProjectInput> _projectInputs = [];

        private bool ProjectConfigurationRequested => _coldConfiguration?.OriginalStatus.Kind
            == NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified;

        private HomeColdProjectReadReconciliation RequireCapturedOriginalProjectSource() => _projectResources
            ?? throw new InvalidOperationException("Project submission requires explicit native cold opt-in and the SAME captured current Home project source.");

        private void CaptureOriginalProjectResources(DesktopOriginalWorkLifetime.Original original,
            TaskExecutionCoordinator sameCoordinator)
        {
            if (!ProjectConfigurationRequested) return;
            // Capture before lifecycle/journal I/O. The journal singleton factory configured
            // this exact source before exposing the journal to the canonical authority.
            var source = Resolve<HomeColdProjectReadReconciliation>(original, actual => _projectResources = actual);
            var journal = Resolve<SqliteTaskRunColdRecoveryJournal>(original);
            var taskActors = Resolve<HostLocalTaskActorSource>(original);
            Scope(original, () =>
            {
                if (!sameCoordinator.HasOriginalColdRecoveryComposition(journal, journal)
                    || !ReferenceEquals(journal.RequireOriginalProjectResourceSource(), source)
                    || !journal.HasOriginalProjectResourceSource(source)
                    || !source.HasOriginalColdProjectComposition(journal, taskActors)
                    || !ReferenceEquals(Resolve<ITaskRunColdProjectResourceSource>(original), source))
                    throw new InvalidOperationException("The configured journal, canonical Task actor and genuine Home project source changed.");
            });
        }

        private async Task<ITaskRunColdOriginalProjectInput> PrepareOriginalProjectInputAsync(
            DesktopOriginalWorkLifetime.Original original, Conversation sameFinalDraft,
            ContainerDefinition samePersistedContainer, DeveloperProjectReference sameReference,
            CancellationToken token)
        {
            var source = RequireCapturedOriginalProjectSource();
            lock (_gate)
                if (_projectInputs.Count >= 128)
                    throw new InvalidOperationException("Original project-input custody requires external process retirement.");
            var gate = new object();
            var retained = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            var failures = new List<Exception>();
            Task<ITaskRunColdOriginalProjectInput>? preparation = null;
            ITaskRunColdOriginalProjectInput? input = null;
            void Retain(Task actual)
            {
                ArgumentNullException.ThrowIfNull(actual);
                // Never refuse an already acquired original, including a late publication.
                lock (gate) retained.Add(actual);
            }
            try
            {
                try
                {
                    Scope(original, () => preparation = source.PrepareOriginalProjectInputWithinSourceAsync(
                        sameFinalDraft, samePersistedContainer, System.Text.Json.JsonSerializer.Serialize(sameReference),
                        callback => Scope(original, callback), Retain, token));
                }
                catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
                if (preparation is null)
                    throw new InvalidOperationException("The genuine current Home project source returned no preparation Task.");
                Retain(preparation);
                lock (gate) joined.Add(preparation);
                try
                {
                    input = await original.AwaitAsync(preparation).ConfigureAwait(false);
                    // Custody precedes every publication/current-use check. The source owns
                    // this input's short preparation and cleanup; it has no public close port.
                    lock (_gate) _projectInputs.Add(input);
                }
                catch (Exception cause) { Capture(failures, preparation, cause); }
            }
            catch (Exception cause) { Capture(failures, null, cause); original.Retain(cause); }
            finally
            {
                // The SAME preparation driver includes all admitted source factories. Join
                // each captured original independently even if its caller scope failed.
                while (true)
                {
                    Task[] pending;
                    lock (gate) pending = retained.Where(actual => joined.Add(actual)).ToArray();
                    if (pending.Length == 0) break;
                    foreach (var actual in pending)
                        try { await original.AwaitAsync(actual).ConfigureAwait(false); }
                        catch (Exception cause) { Capture(failures, actual, cause); }
                }
            }
            Throw(failures);
            if (input is null || preparation is null)
                throw new InvalidOperationException("No genuine original project input was acquired.");
            Scope(original, () =>
            {
                if (!source.IsOwnedOriginalProjectInput(input) || !source.IsIssuedOriginalProjectInput(input)
                    || !ReferenceEquals(input.OriginalConversation, sameFinalDraft)
                    || !ReferenceEquals(input.OriginalContainer, samePersistedContainer)
                    || !ReferenceEquals(input.OriginalPreparation, preparation)
                    || !preparation.IsCompletedSuccessfully)
                    throw new InvalidOperationException("The original Home source did not retain the SAME draft, persisted container and healthy preparation.");
            });
            return input;
        }
    }
}
