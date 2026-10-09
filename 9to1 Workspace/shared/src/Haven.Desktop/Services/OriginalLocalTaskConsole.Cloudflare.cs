using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

public static partial class OriginalLocalTaskConsole
{
    // An observation selection only. Tuple values never authorize work; each consumer
    // still calls its actual canonical/action source. Old sources keep their own custody.
    internal sealed class OriginalTaskSelection
    {
        private readonly object _gate = new();
        private Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)>? _observeSelected;

        internal sealed class PendingInitial
        {
            private readonly object _gate = new();
            private Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)>? _observe;

            internal void CaptureObservation(Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> observe)
            {
                ArgumentNullException.ThrowIfNull(observe);
                lock (_gate)
                {
                    if (_observe is not null) throw new InvalidOperationException("The same initial observation was already captured.");
                    _observe = observe;
                }
            }

            internal (Guid TaskId, Guid ExecutionId, Guid ContextId) Observe()
            {
                Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> same;
                lock (_gate) same = _observe ?? throw new InvalidOperationException("The selected initial source is still being acquired.");
                return same(); // Pure source observation, outside the selection metadata gate.
            }
        }

        internal PendingInitial CreatePendingInitial() => new();
        internal void SelectInitial(PendingInitial same)
        {
            ArgumentNullException.ThrowIfNull(same);
            lock (_gate) _observeSelected = same.Observe;
        }
        internal void SelectAttachment(Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> observe)
        {
            ArgumentNullException.ThrowIfNull(observe);
            lock (_gate) _observeSelected = observe;
        }
        internal (Guid TaskId, Guid ExecutionId, Guid ContextId) ObserveSelectedTuple()
        {
            Func<(Guid TaskId, Guid ExecutionId, Guid ContextId)> same;
            lock (_gate) same = _observeSelected ?? throw new InvalidOperationException("Select an authentic initial source or reopen the SAME saved Task/Run first.");
            return same();
        }
    }

    private sealed partial class Host
    {
        private readonly Dictionary<string, ICloudflareOriginalSetupReview> _cloudflareSetupReviews = new(StringComparer.Ordinal);
        private readonly List<ITaskRunToolActionPreparation> _cloudflareDiscoveryPreparations = [];

        private HomeCloudflareServiceOwner RequireOriginalCloudflareSetupSource(DesktopOriginalWorkLifetime.Original original)
        {
            var sameOwner = Resolve<HomeCloudflareServiceOwner>(original);
            if (!ReferenceEquals(Resolve<ICloudflareProductionSetupSource>(original), sameOwner))
                throw new InvalidOperationException("The configured Cloudflare setup alias changed.");
            HomeCloudflareServiceOwner? captured = null;
            Scope(original, () => captured = _registration!.CaptureOriginalOwner(Provider));
            if (!ReferenceEquals(sameOwner, captured))
                throw new InvalidOperationException("The source-owned Cloudflare borrower was replaced.");
            return sameOwner;
        }

        private async Task ObserveOriginalCloudflareConnectionsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            var saved = await Acquire(original, () => Resolve<IExternalConnectionRepository>(original).GetAllAsync(token)).ConfigureAwait(false);
            // Only public selection metadata is displayed. The saved configuration body,
            // credential owner and secret values are never serialized by this command.
            Write(saved.Where(value => value.Kind == ExternalConnectionKind.Mcp).Select(value => new
            {
                value.Id, value.Name, value.IsEnabled, value.State,
                note = "Saved connection observation; choose the official OAuth Cloudflare service explicitly."
            }).ToArray());
        }

        private async Task PrepareOriginalCloudflareSetupAsync(DesktopOriginalWorkLifetime.Original original, string argument, CancellationToken token)
        {
            var words = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 3 || !Guid.TryParse(words[0], out var connection) || connection == Guid.Empty ||
                !CloudflareTypedToolCatalogue.IsHexId(words[1]) || !long.TryParse(words[2], out var revision) || revision < 0)
                throw new ArgumentException("Use cf-prepare <saved-connection-id> <32-lowercase-hex-account-id> <observed-revision>.");
            lock (_gate)
                if (_cloudflareSetupReviews.Count >= 128) throw new InvalidOperationException("Original setup-review custody requires process retirement.");
            var owner = RequireOriginalCloudflareSetupSource(original);
            var selection = new CloudflareSetupSelection(connection, "https://mcp.cloudflare.com/mcp", words[1], "execute");
            var review = await AcquireOriginalCloudflareCapturedAsync(original,
                () => owner.PrepareSetupAsync(selection, revision, token), actual =>
                {
                    lock (_gate)
                    {
                        if (_cloudflareSetupReviews.TryGetValue(actual.RequestId, out var prior) && !ReferenceEquals(prior, actual))
                            throw new InvalidOperationException("A different original setup review reused this request identity.");
                        _cloudflareSetupReviews[actual.RequestId] = actual;
                    }
                }).ConfigureAwait(false);
            Write(await Acquire(original, () => review.SubmitOriginalAsync(token)).ConfigureAwait(false));
            Write(new { review.RequestId, note = "Use home-display and explicitly home-accept or home-decline on this exact request. cf-commit is separate; no remote action was dispatched." });
        }

        private async Task CommitOriginalCloudflareSetupAsync(DesktopOriginalWorkLifetime.Original original, string request, CancellationToken token)
        {
            ICloudflareOriginalSetupReview sameReview;
            lock (_gate) sameReview = _cloudflareSetupReviews.TryGetValue(request, out var actual) ? actual
                : throw new InvalidOperationException("Prepare and retain the original Cloudflare review in this process before committing it.");
            _ = RequireOriginalCloudflareSetupSource(original);
            Write(await Acquire(original, () => sameReview.CommitOriginalAsync(token)).ConfigureAwait(false));
        }

        private void SelectOriginalAttachment(SpaceDevTaskAttachmentSession.SpaceDevTaskAttachment sameAttachment)
        {
            _selectedTask.SelectAttachment(() =>
            {
                var sameLink = sameAttachment.View.Link;
                return (sameLink.TaskId, sameLink.ExecutionId, sameLink.ConversationId);
            });
        }

        private (Guid TaskId, Guid ExecutionId, Guid ContextId) RequireOriginalConsoleTaskTuple() =>
            _selectedTask.ObserveSelectedTuple();

        private void LaunchOriginalCloudflareDiscovery(DesktopOriginalWorkLifetime.Original commandOriginal, CancellationToken token)
        {
            var tuple = RequireOriginalConsoleTaskTuple();
            var coordinator = Resolve<TaskExecutionCoordinator>(commandOriginal);
            var owner = Resolve<CanonicalWorkspaceCloudflareToolActionOwner>(commandOriginal);
            if (!ReferenceEquals(Resolve<ITaskRunToolActionOwner>(commandOriginal), owner))
                throw new InvalidOperationException("The configured canonical action owner changed.");
            lock (_gate)
                if (_cloudflareDiscoveryPreparations.Count >= 128) throw new InvalidOperationException("Original discovery custody requires process retirement.");
            var actionId = Guid.NewGuid(); // One explicitly requested read, retained by its actual encompassing driver.
            var call = new OllamaToolCall("cloudflare_kv_list", new Dictionary<string, JsonElement>(), actionId.ToString("D"));
            _ = _work.RunAsync(async original =>
            {
                var row = await Acquire(original, () => coordinator.GetAsync(tuple.TaskId, token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The SAME canonical Task is unavailable.");
                if (row.ExecutionId != tuple.ExecutionId || row.ContextId != tuple.ContextId)
                    throw new InvalidOperationException("The actual canonical Task/Run/context changed.");
                var attempt = await Acquire(original, () => coordinator.TryGetIssuedAttemptAsync(row.TaskId, row.ExecutionId,
                    row.Attempts.LastOrDefault()?.Id ?? throw new InvalidOperationException("No genuine current attempt exists."), token)).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("An original active tool-capable attempt is required; saved IDs or a completed Task do not issue one.");
                Action<Action> sameScope = callback => Scope(original, callback);
                var preparation = await AcquireOriginalCloudflareCapturedAsync(original,
                    () => owner.PrepareCallerScopedOriginalAsync(attempt, row, actionId, call, ToolRuntimeKind.Mcp,
                        PermissionMode.FullAccess, null, sameScope, token), actual =>
                    { lock (_gate) _cloudflareDiscoveryPreparations.Add(actual); }).ConfigureAwait(false);
                if (!ReferenceEquals(preparation.OriginalAttempt, attempt) || preparation.ActionId != actionId ||
                    preparation is not ICloudflareOriginalToolPreparation fixedOriginal ||
                    fixedOriginal.OriginalInvocation.Descriptor.Kind != CloudflareOperationKind.KvList ||
                    !fixedOriginal.OriginalInvocation.Descriptor.IsReadOnly)
                    throw new InvalidOperationException("The actual owner did not issue the SAME harmless discovery preparation.");
                row = await Acquire(original, () => coordinator.RegisterOriginalToolActionAsync(preparation,
                    row.LastCheckpointActionId, "Observe the explicitly configured Cloudflare KV namespace list", token)).ConfigureAwait(false);
                var result = await Acquire(original, () => owner.ExecuteOriginalAsync(preparation,
                    bodyToken => Acquire(original, () => owner.ExecuteOriginalCloudflareRuntimeAsync(call, preparation, sameScope, bodyToken)), token)).ConfigureAwait(false);
                await Acquire(original, () => owner.ValidateOriginalResultAsync(preparation, result, token).AsTask()).ConfigureAwait(false);
                row = await Acquire(original, () => coordinator.RecordOriginalToolActionOutcomeAsync(preparation, result, token)).ConfigureAwait(false);
                await Acquire(original, () => coordinator.RetireAcknowledgedToolOriginalAsync(preparation, row).AsTask()).ConfigureAwait(false);
                Scope(original, () => Write(new { tuple.TaskId, tuple.ExecutionId, actionId,
                    canonicalRevision = row.PersistenceRevision, result = result.OriginalResult,
                    note = "Actual read-only scoped result and canonical acknowledgment; no staging resource was created or changed." }));
            });
            Write(new { discoveryRequested = true, tuple.TaskId, tuple.ExecutionId, actionId,
                note = "The retained original driver waits for genuine current admission and explicit Home action approval. Use home-requests/display/accept or decline; this command grants nothing." });
        }

        private async Task<T> AcquireOriginalCloudflareCapturedAsync<T>(DesktopOriginalWorkLifetime.Original original,
            Func<Task<T>> factory, Action<T> retainProduct)
        {
            Task<T>? actual = null; Exception? prefix = null;
            try { Scope(original, () => actual = factory()); }
            catch (Exception cause) { prefix = cause; original.Retain(cause); }
            if (actual is null) { original.ThrowRetained(); throw new InvalidOperationException("The original Cloudflare source returned no Task."); }
            var returned = await original.AwaitAsync(actual).ConfigureAwait(false);
            retainProduct(returned); // Historical custody before any post-await publication/currentness check.
            if (prefix is not null) original.ThrowRetained();
            original.DemandPublication();
            return returned;
        }

        private async Task ObserveOriginalHistoryAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            var tuple = RequireOriginalConsoleTaskTuple();
            var coordinator = Resolve<TaskExecutionCoordinator>(original);
            var conversations = Resolve<IConversationRepository>(original);
            var before = await Acquire(original, () => coordinator.GetAsync(tuple.TaskId, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original canonical Task is unavailable.");
            if (before.ExecutionId != tuple.ExecutionId || before.ContextId != tuple.ContextId)
                throw new InvalidOperationException("The observed original Task/Run/context changed.");
            var conversation = await Acquire(original, () => conversations.GetAsync(tuple.ContextId, token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The actual original Conversation is unavailable.");
            var messages = await Acquire(original, () => conversations.GetMessagesAsync(tuple.ContextId, token)).ConfigureAwait(false);
            var context = await Acquire(original, () => conversations.GetContextEntriesAsync(tuple.ContextId, token)).ConfigureAwait(false);
            var messagesAfter = await Acquire(original, () => conversations.GetMessagesAsync(tuple.ContextId, token)).ConfigureAwait(false);
            var contextAfter = await Acquire(original, () => conversations.GetContextEntriesAsync(tuple.ContextId, token)).ConfigureAwait(false);
            var conversationAfter = await Acquire(original, () => conversations.GetAsync(tuple.ContextId, token)).ConfigureAwait(false);
            var after = await Acquire(original, () => coordinator.GetAsync(tuple.TaskId, token)).ConfigureAwait(false);
            if (conversation.Mode != HavenMode.Tasks || conversation.Id != tuple.ContextId ||
                conversationAfter != conversation || ObservationDigest(before) != ObservationDigest(after) ||
                ObservationDigest(messages) != ObservationDigest(messagesAfter) || ObservationDigest(context) != ObservationDigest(contextAfter))
                throw new InvalidOperationException("Canonical history changed during observation. Inspect again after the actual source reaches a stable boundary.");
            Write(new { canonical = before, canonicalSha256 = ObservationDigest(before),
                digestEncoding = "SHA256 of System.Text.Json UTF8 serialization; observation only",
                conversation.Id, conversation.ContainerId, conversation.SpaceId,
                messagesSha256 = ObservationDigest(messages), contextSha256 = ObservationDigest(context),
                messages = messages.Select(value => new { value.Id, value.ConversationId, value.Role, value.CreatedAt,
                    value.IsCompacted, contentSha256 = ObservationDigest(value.Content), metadataSha256 = ObservationDigest(value.MetadataJson) }).ToArray(),
                context = context.Select(value => new { value.Id, value.ConversationId, value.Kind, value.CreatedAt,
                    contentSha256 = ObservationDigest(value.Content), evidenceSha256 = ObservationDigest(value.Evidence) }).ToArray(),
                note = "Persisted original transcript/action/checkpoint observations for before/after conservation. These hashes, IDs and old receipts issue no continuation authority." });
        }

        private static string ObservationDigest<T>(T value) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    }
}
