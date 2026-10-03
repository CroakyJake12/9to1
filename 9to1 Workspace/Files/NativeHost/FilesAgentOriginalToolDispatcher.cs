using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

/// <summary>Only the registered Home issuer admits an original call. Files owns its canonical
/// target, final store/actor/revision fence and durable journal. This does not grant Den execution.</summary>
public sealed class FilesAgentOriginalToolDispatcher : IWorkspaceOriginalToolDispatcher,
    IHomeAgentOriginalToolPolicySource, IHomeActionPolicySource
{
    public const string ToolName = "files_rename";
    public const string ActionId = "files.agent.rename";
    public const string TargetAppId = "files";
    private static readonly HomePermissionActionPolicy Policy = new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Routine, true, false, true);
    private readonly NativeFilesWorkspaceAuthority _files;
    private readonly IChatExecutionAdmission _admissions;
    private readonly int _maximumRetainedCalls;
    private readonly object _gate = new();
    private readonly Dictionary<OllamaToolCall, OriginalCall> _calls = new(ReferenceEqualityComparer.Instance);

    /// <summary>The admission service must be the registered deferred Home forwarding service;
    /// resolving the issuer directly while it resolves this tool policy would create a DI cycle.</summary>
    public FilesAgentOriginalToolDispatcher(NativeFilesWorkspaceAuthority files, IChatExecutionAdmission registeredOriginalAdmissions,
        int maximumRetainedCalls = 1024)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _admissions = registeredOriginalAdmissions ?? throw new ArgumentNullException(nameof(registeredOriginalAdmissions));
        if (maximumRetainedCalls is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maximumRetainedCalls));
        _maximumRetainedCalls = maximumRetainedCalls;
    }

    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == TargetAppId && actionId == ActionId ? Policy : null;

    public async ValueTask<IReadOnlyList<OllamaToolDefinition>> GetOriginalDefinitionsAsync(object originalExecutionAuthority,
        Guid conversationId, string modelIdentity, IReadOnlyCollection<ActiveCapability> currentCapabilities,
        CancellationToken cancellationToken = default)
    {
        if (RuntimeSafetyState.IsSafeMode) return [];
        await _admissions.DemandCurrentAsync(originalExecutionAuthority, conversationId, modelIdentity, null, cancellationToken).ConfigureAwait(false);
        var workspace = await _files.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        await _admissions.DemandCurrentAsync(originalExecutionAuthority, conversationId, modelIdentity, null, cancellationToken).ConfigureAwait(false);
        if (RuntimeSafetyState.IsSafeMode || workspace is null || workspace.Actor.OrganisationId is not null ||
            !currentCapabilities.Any(item => item.Key == "write-file")) return [];
        return [new(ToolName, "Rename an existing canonical Files item. Select its actual store, item and current revision; Home approval is required.",
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["store_id"] = new { type = "string", description = "Canonical Files store UUID" },
                ["file_id"] = new { type = "string", description = "Canonical Files item UUID" },
                ["expected_revision"] = new { type = "string", description = "Actual current item revision UUID" },
                ["new_name"] = new { type = "string", description = "One nonempty name component" }
            }, ["store_id", "file_id", "expected_revision", "new_name"])];
    }

    public async ValueTask<HomeAgentToolDemand?> ResolveAsync(AuthenticatedResourceActor originalActor,
        DenAgentReference reference, AgentExecutionStep originalStep, OllamaToolCall originalCall,
        OllamaToolCall originalDispatchCall, CancellationToken cancellationToken)
    {
        if (RuntimeSafetyState.IsSafeMode || originalActor.OrganisationId is not null || ReferenceEquals(originalCall, originalDispatchCall) ||
            originalDispatchCall.Arguments is not FrozenDictionary<string, JsonElement> ||
            !TryReadRename(originalDispatchCall, out var storeId, out var itemId, out var revision, out var name)) return null;
        var workspace = await _files.GetCurrentAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (workspace is null || workspace.Actor != originalActor) return null;
        var read = await workspace.Provider.GetForOriginalStoreAtRevisionAsync(storeId, itemId, revision, cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) return null;
        var actual = await _files.GetCurrentAsync(storeId, cancellationToken).ConfigureAwait(false);
        if (RuntimeSafetyState.IsSafeMode || actual is null || actual.Actor != originalActor || !ReferenceEquals(actual.Provider, workspace.Provider)) return null;
        lock (_gate)
        {
            if (RuntimeSafetyState.IsSafeMode) return null;
            if (_calls.TryGetValue(originalDispatchCall, out var retained))
            {
                if (!retained.Matches(originalActor, reference, originalStep, originalCall, originalDispatchCall) ||
                    retained.Execution is not null) return null;
            }
            else
            {
                if (_calls.Count >= _maximumRetainedCalls) return null;
                _calls.Add(originalDispatchCall, new(originalActor, reference, originalStep, originalCall,
                    originalDispatchCall, workspace, itemId, revision, name, read.Value!));
            }
        }
        return new(ActionId, [new("files.item", itemId.ToString())],
            [new("files.item", itemId.ToString(), revision.ToString(), ResourceAccess.Write)],
            new HashSet<string>(["files.write"], StringComparer.OrdinalIgnoreCase), Policy, TargetAppId);
    }

    public ValueTask<WorkspaceToolResult> ExecuteOriginalAsync(object originalExecutionAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalDispatchCall, CancellationToken cancellationToken = default)
    {
        OriginalCall call;
        TaskCompletionSource<bool>? release = null;
        Task<WorkspaceToolResult> original;
        lock (_gate)
        {
            if (!_calls.TryGetValue(originalDispatchCall, out call!) || conversationId == Guid.Empty || string.IsNullOrWhiteSpace(modelIdentity))
                return ValueTask.FromException<WorkspaceToolResult>(Refused());
            if (call.Execution is not null)
            {
                if (!ReferenceEquals(call.Authority, originalExecutionAuthority) || call.ConversationId != conversationId || call.ModelIdentity != modelIdentity)
                    return ValueTask.FromException<WorkspaceToolResult>(Refused());
                return new(call.Execution);
            }
            call.Authority = originalExecutionAuthority; call.ConversationId = conversationId; call.ModelIdentity = modelIdentity;
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = ExecuteOwnerAsync(call, release.Task, cancellationToken);
            call.Execution = original; // SAME original task is retained before any external callback or I/O.
        }
        release.SetResult(true);
        return new(original);
    }

    private async Task<WorkspaceToolResult> ExecuteOwnerAsync(OriginalCall call, Task release, CancellationToken token)
    {
        await release.ConfigureAwait(false);
        Exception? primary = null;
        FilesResult<FilesOperation>? result = null;
        FilesOriginalStructuralReceipt? receipt = null;
        object? admission = null;
        CancellationTokenSource? originalLifetime = null;
        try
        {
            if (RuntimeSafetyState.IsSafeMode) throw Refused();
            var lifetime = await _admissions.GetOriginalLifetimeAsync(call.Authority!, token).ConfigureAwait(false);
            if (!lifetime.CanBeCanceled || lifetime.IsCancellationRequested) throw Refused();
            originalLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            token = originalLifetime.Token;
            admission = await _admissions.GetOriginalCommitAdmissionAsync(call.Authority!, call.ConversationId,
                call.ModelIdentity!, call.Dispatch, token).ConfigureAwait(false);
            if (await _admissions.DemandOriginalCommitCurrentAsync(admission, token).ConfigureAwait(false) != call.Actor) throw Refused();
            var guard = await _files.CaptureCommitAuthorityAsync(call.Actor, call.Workspace.Provider,
                () => !token.IsCancellationRequested && !RuntimeSafetyState.IsSafeMode, token).ConfigureAwait(false);
            guard = guard.WithAdditionalCurrentCheck(async current =>
                !RuntimeSafetyState.IsSafeMode &&
                await _admissions.DemandOriginalCommitCurrentAsync(admission, current).ConfigureAwait(false) == call.Actor &&
                !RuntimeSafetyState.IsSafeMode);
            var now = DateTimeOffset.UtcNow;
            var operation = new FilesOperation(call.OperationId, call.Actor.ActorId, call.ItemId, call.Before.ParentId,
                null, "Rename", call.BaseRevision, null, FilesOperationState.Pending, now, now,
                JsonSerializer.Serialize(new { Name = call.Before.Name, ParentId = call.Before.ParentId }), null);
            result = await call.Workspace.Provider.MutateAsync(operation, call.NewName, call.Workspace.Configuration.StoreId, guard, token).ConfigureAwait(false);
            if (!result.IsSuccess) throw new InvalidOperationException("Files refused the original rename: " + result.Error!.Code);
        }
        catch (Exception error) { primary = error; }
        try
        {
            receipt = await call.Workspace.Provider.GetOriginalStructuralReceiptAsync(call.Workspace.Configuration.StoreId,
                call.OperationId, CancellationToken.None).ConfigureAwait(false);
            if (receipt is not null && !MatchesReceipt(call, receipt))
            {
                receipt = null;
                throw new InvalidDataException("The actual Files journal differs from its original dispatch.");
            }
        }
        catch (Exception error) { primary = Combine(primary, error); }
        if (primary is null && (receipt is null || result?.Value?.ResultRevisionId != receipt.Operation.ResultRevisionId))
            primary = new InvalidDataException("The successful Files return has no matching durable original receipt.");
        // After the last receipt I/O, publication independently demands the SAME current admission.
        // Audit of a known commit survives retirement; a later call or success publication does not.
        if (primary is null)
        {
            try
            {
                if (RuntimeSafetyState.IsSafeMode ||
                    await _admissions.DemandOriginalCommitCurrentAsync(admission!, token).ConfigureAwait(false) != call.Actor ||
                    RuntimeSafetyState.IsSafeMode) throw Refused();
            }
            catch (Exception error) { primary = error; }
        }
        try { originalLifetime?.Dispose(); }
        catch (Exception error) { primary = Combine(primary, error); }
        if (primary is not null)
        {
            Exception actual = receipt is null ? primary : new WorkspaceOriginalCommittedObservationException("files", call.OperationId.ToString(), primary);
            lock (_gate) { call.Failure = actual; call.Receipt = receipt; }
            ExceptionDispatchInfo.Capture(actual).Throw();
        }
        var returned = new WorkspaceToolResult(
            new(Guid.NewGuid(), "Rename Files item", "Canonical Files journal confirmed.", true, TimeSpan.Zero, DateTimeOffset.UtcNow),
            "Canonical Files rename committed.");
        lock (_gate) { call.Result = returned; call.Receipt = receipt; }
        return returned;
    }

    public async ValueTask<HomeExecutionOutcome?> VerifyOriginalOutcomeAsync(AuthenticatedResourceActor originalActor,
        DenAgentReference reference, AgentExecutionStep originalStep, OllamaToolCall originalCall, OllamaToolCall originalDispatchCall,
        string originalRequestId, WorkspaceToolResult? originalResult, Exception? originalFailure, CancellationToken cancellationToken)
    {
        OriginalCall retained;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(originalRequestId) || !_calls.TryGetValue(originalDispatchCall, out retained!) ||
                !retained.Matches(originalActor, reference, originalStep, originalCall, originalDispatchCall) || retained.Execution?.IsCompleted != true ||
                (originalResult is null) == (originalFailure is null) ||
                originalResult is not null && !ReferenceEquals(originalResult, retained.Result) ||
                originalFailure is not null && !ReferenceEquals(originalFailure, retained.Failure)) return null;
            if (retained.RequestId is not null && retained.RequestId != originalRequestId) return null;
            retained.RequestId = originalRequestId;
        }
        // Independent already-admitted settlement survives retired execution. This issues no new
        // call, does not resolve a new Files scope and never mutates the journal.
        var receipt = await retained.Workspace.Provider.GetOriginalStructuralReceiptAsync(retained.Workspace.Configuration.StoreId,
            retained.OperationId, CancellationToken.None).ConfigureAwait(false);
        if (receipt is not null && !MatchesReceipt(retained, receipt)) return null;
        if (receipt is not null)
            return new(originalFailure is null ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.PartiallyCompleted,
                originalFailure is null ? "Files.RenameCommitted" : "Files.RenameCommittedObservationFailed",
                originalFailure is null ? "The original canonical rename is durably committed." :
                    "The original canonical rename is committed; final observation failed. Do not replay it.",
                [new("files.item", retained.ItemId.ToString())]);
        if (originalFailure is null || retained.Receipt is not null) return null;
        return new(originalFailure is OperationCanceledException ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Failed,
            "Files.RenameNotCommitted", "The original Files journal contains no commit for this admitted operation.", []);
    }

    private static bool MatchesReceipt(OriginalCall call, FilesOriginalStructuralReceipt receipt) =>
        receipt.StoreId == call.Workspace.Configuration.StoreId && receipt.Operation.Id == call.OperationId &&
        receipt.Operation.ActorId == call.Actor.ActorId && receipt.Operation.ItemId == call.ItemId &&
        receipt.Operation.Operation == "Rename" && receipt.Operation.BaseRevisionId == call.BaseRevision &&
        receipt.Operation.SourceParentId == call.Before.ParentId && receipt.Operation.DestinationParentId is null &&
        receipt.Operation.Payload?.NewName == call.NewName && receipt.Change.Metadata?.Name == call.NewName &&
        receipt.Change.Metadata?.ParentId == call.Before.ParentId;

    private static bool TryReadRename(OllamaToolCall call, out Guid storeId, out HostedItemId itemId,
        out FilesRevisionId revision, out string name)
    {
        storeId = default; itemId = default; revision = default; name = "";
        if (call.Name != ToolName || call.Arguments.Count != 4 || !Read("store_id", out var store) || !Read("file_id", out var item) ||
            !Read("expected_revision", out var expected) || !Read("new_name", out name) ||
            !Guid.TryParse(store, out storeId) || storeId == Guid.Empty || !Guid.TryParse(item, out var id) || id == Guid.Empty ||
            !Guid.TryParse(expected, out var version) || version == Guid.Empty || name.Length is < 1 or > 255 || name != name.Trim() ||
            name is "." or ".." || name.Any(character => char.IsControl(character) || character is '/' or '\\')) return false;
        itemId = new(id); revision = new(version); return true;
        bool Read(string key, out string text)
        {
            text = "";
            if (!call.Arguments.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String) return false;
            text = value.GetString()!; return text is not null;
        }
    }

    private static UnauthorizedAccessException Refused() => new("The exact original Files dispatch is unavailable.");
    private static Exception Combine(Exception? primary, Exception next) => primary is null ? next :
        ReferenceEquals(primary, next) ? primary : new AggregateException("Original Files work and receipt observation failed.", primary, next);

    private sealed class OriginalCall(AuthenticatedResourceActor actor, DenAgentReference reference, AgentExecutionStep step,
        OllamaToolCall original, OllamaToolCall dispatch, NativeFilesWorkspace workspace, HostedItemId item,
        FilesRevisionId revision, string name, HostedItemMetadata before)
    {
        internal readonly AuthenticatedResourceActor Actor = actor;
        internal readonly DenAgentReference Reference = reference;
        internal readonly AgentExecutionStep Step = step;
        internal readonly OllamaToolCall Original = original, Dispatch = dispatch;
        internal readonly NativeFilesWorkspace Workspace = workspace;
        internal readonly HostedItemId ItemId = item;
        internal readonly FilesRevisionId BaseRevision = revision;
        internal readonly string NewName = name;
        internal readonly HostedItemMetadata Before = before;
        internal readonly FilesOperationId OperationId = new(Guid.NewGuid());
        internal object? Authority;
        internal Guid ConversationId;
        internal string? ModelIdentity, RequestId;
        internal Task<WorkspaceToolResult>? Execution;
        internal WorkspaceToolResult? Result;
        internal Exception? Failure;
        internal FilesOriginalStructuralReceipt? Receipt;
        internal bool Matches(AuthenticatedResourceActor actualActor, DenAgentReference actualReference, AgentExecutionStep actualStep,
            OllamaToolCall actualOriginal, OllamaToolCall actualDispatch) => Actor == actualActor && Reference == actualReference &&
            ReferenceEquals(Step, actualStep) && ReferenceEquals(Original, actualOriginal) && ReferenceEquals(Dispatch, actualDispatch);
    }
}
