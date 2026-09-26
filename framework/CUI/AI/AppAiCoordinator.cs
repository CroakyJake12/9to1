namespace NineToOne.Cui.AI;

public sealed class AppAiCoordinator(
    IAppAiContext context,
    IAppAiActions actions,
    IAppAiApprovalVerifier approvals,
    IDulcheAppClient dulche,
    IAppAiApprovalRequester? approvalRequester = null,
    IAppAiDatabaseMutationGuard? databaseGuard = null,
    IAppAiActionGraph? actionGraph = null,
    IAppAiModelPicker? modelPicker = null)
{
    public async ValueTask<AppAiContextSnapshot> CaptureContextAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await context.CaptureAsync(cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(snapshot);
        return snapshot;
    }

    public ValueTask<IReadOnlyList<AppAiModelOption>> GetModelsAsync(CancellationToken cancellationToken = default) =>
        modelPicker is null
            ? ValueTask.FromResult<IReadOnlyList<AppAiModelOption>>([])
            : modelPicker.GetModelsAsync(cancellationToken);

    public ValueTask<AppAiModelSelection?> GetModelSelectionAsync(CancellationToken cancellationToken = default) =>
        modelPicker is null
            ? ValueTask.FromResult<AppAiModelSelection?>(null)
            : modelPicker.GetSelectionAsync(cancellationToken);

    public async ValueTask<bool> SelectModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        return modelPicker is not null && await modelPicker.SelectAsync(modelId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AppAiActionResult> ExecuteAsync(
        AppAiActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AccessMode != AppAiAccessMode.Write)
            return AppAiActionResult.Rejected("Read-only mode does not allow app actions.", "read-only-mode");

        var snapshot = await context.CaptureAsync(cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(snapshot);
        if (!string.Equals(snapshot.AppId, request.AppId, StringComparison.Ordinal))
            return AppAiActionResult.Rejected("The action does not belong to the current application.", "app-mismatch");
        if (request.ExpectedRevision is not null &&
            !string.Equals(snapshot.Revision, request.ExpectedRevision, StringComparison.Ordinal))
            return AppAiActionResult.Rejected("The app content changed before the action could run. Review the current context and try again.", "stale-context", canRetry: true);

        var descriptor = actions.Actions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, request.ActionId, StringComparison.Ordinal));
        if (descriptor is null)
            return AppAiActionResult.Rejected("The requested action is not available.", "unknown-action");

        if (!descriptor.IsMutation)
            return AppAiActionResult.Rejected("Read-only app actions are not dispatched through the mutation endpoint.", "action-not-mutable");

        var dataMutation = string.Equals(snapshot.AppId, "data", StringComparison.OrdinalIgnoreCase);
        var forcePerActionApproval = dataMutation;
        AppAiDatabasePreparation? databasePreparation = null;
        if (dataMutation && snapshot.IsLiveDatabase)
        {
            if (databaseGuard is null)
                return AppAiActionResult.Rejected("The live database safety service is unavailable; this change was not run.", "database-safety-unavailable");

            databasePreparation = await databaseGuard.PrepareAsync(
                snapshot, descriptor, request.Arguments, cancellationToken).ConfigureAwait(false);
            if (!databasePreparation.IsValid || string.IsNullOrWhiteSpace(databasePreparation.BackupId) ||
                string.IsNullOrWhiteSpace(databasePreparation.Preview))
                return AppAiActionResult.Rejected(
                    databasePreparation.ErrorMessage ?? "A validated preview and recoverable backup are required before changing this database.",
                    databasePreparation.ErrorCode ?? "database-backup-required");
        }

        if (descriptor.RequiresPermission || descriptor.RequiresReview || forcePerActionApproval)
        {
            if (approvalRequester is null)
                return AppAiActionResult.Rejected("Home approval is unavailable; the action was not run.", "approval-unavailable", canRetry: true);

            await PublishGraphEventAsync(snapshot, descriptor.Id, request.CorrelationId,
                AppAiActionGraphStatus.WaitingForApproval, "Waiting for Home approval", cancellationToken).ConfigureAwait(false);
            var decision = await approvalRequester.RequestAsync(new AppAiApprovalRequest(
                request.AppId,
                snapshot,
                descriptor,
                ImpactUnknown: descriptor.AffectedObjectIds is not { Count: > 0 },
                ForcePerActionApproval: forcePerActionApproval,
                ChangePreview: databasePreparation?.Preview,
                BackupId: databasePreparation?.BackupId,
                request.CorrelationId), cancellationToken).ConfigureAwait(false);

            if (decision.Outcome != AppAiApprovalOutcome.Approved)
            {
                var code = decision.Outcome switch
                {
                    AppAiApprovalOutcome.Pending => "approval-pending",
                    AppAiApprovalOutcome.Denied => "approval-denied",
                    _ => "approval-unavailable"
                };
                return AppAiActionResult.Rejected(
                    string.IsNullOrWhiteSpace(decision.Message) ? "Home did not approve this action." : decision.Message,
                    code,
                    canRetry: decision.Outcome is AppAiApprovalOutcome.Pending or AppAiApprovalOutcome.Unavailable);
            }

            if (string.IsNullOrWhiteSpace(decision.ApprovalToken))
                return AppAiActionResult.Rejected("Home approval did not include a scoped approval token; the action was not run.", "approval-token-missing");

            var approved = await approvals.VerifyAsync(
                request.AppId,
                request.ActionId,
                decision.ApprovalToken,
                cancellationToken).ConfigureAwait(false);
            if (!approved)
                return AppAiActionResult.Rejected("The approval is invalid or expired.", "approval-invalid");

            // Re-read after approval so a stale revision or changed capability cannot inherit consent.
            var current = await context.CaptureAsync(cancellationToken).ConfigureAwait(false);
            ValidateSnapshot(current);
            if (!SameTarget(snapshot, current) ||
                !actions.Actions.Any(candidate => candidate == descriptor))
                return AppAiActionResult.Rejected("The app context or action scope changed during approval. Review the new state before retrying.", "stale-context", canRetry: true);
        }

        var result = await actions.ExecuteAsync(request with
        {
            ApprovalToken = descriptor.RequiresPermission || descriptor.RequiresReview || forcePerActionApproval
                ? (request.ApprovalToken ?? string.Empty)
                : null
        }, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await PublishGraphEventAsync(snapshot, descriptor.Id, request.CorrelationId,
                AppAiActionGraphStatus.Failed, "App action failed", cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (dataMutation && snapshot.IsLiveDatabase)
        {
            var verified = databasePreparation?.BackupId is { Length: > 0 } backupId &&
                databaseGuard is not null && await databaseGuard.VerifyAsync(
                    snapshot, descriptor, request.Arguments, result, backupId, cancellationToken).ConfigureAwait(false);
            if (!verified)
                return AppAiActionResult.Rejected(
                    "The database action ran, but its result could not be verified. The recovery backup is retained.",
                    "database-result-unverified");
        }

        await PublishGraphEventAsync(snapshot, descriptor.Id, request.CorrelationId,
            AppAiActionGraphStatus.Completed, "App action completed", cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
        string prompt,
        string correlationId,
        AppAiAccessMode accessMode = AppAiAccessMode.ReadOnly,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var snapshot = await CaptureContextAsync(cancellationToken).ConfigureAwait(false);
        var modelSelection = await GetModelSelectionAsync(cancellationToken).ConfigureAwait(false);
        var availableActions = accessMode == AppAiAccessMode.Write
            ? actions.Actions.Where(action => action.IsMutation).ToArray()
            : [];
        var selectedActionCalls = 0;
        var selectedModel = modelSelection;
        await foreach (var chunk in dulche.StreamAsync(
            new AppAiPrompt(prompt.Trim(), snapshot, correlationId, accessMode, availableActions, selectedModel),
            cancellationToken).ConfigureAwait(false))
        {
            if (chunk.RequestedAction is { } requestedAction)
            {
                selectedActionCalls++;
                if (selectedActionCalls > 8)
                {
                    yield return new AppAiResponseChunk("The request reached the action limit. Review the current state before continuing.", IsFinal: true);
                    yield break;
                }

                var result = await ExecuteAsync(new AppAiActionRequest(
                    snapshot.AppId,
                    requestedAction.ActionId,
                    requestedAction.Arguments,
                    ApprovalToken: null,
                    correlationId,
                    accessMode,
                    snapshot.Revision), cancellationToken).ConfigureAwait(false);
                var visible = result.Succeeded
                    ? $"Action completed: {result.Summary}"
                    : $"Action not run: {result.Summary}";
                yield return new AppAiResponseChunk(visible);
                continue;
            }
            yield return chunk;
        }
    }

    private async ValueTask PublishGraphEventAsync(
        AppAiContextSnapshot snapshot,
        string actionId,
        string correlationId,
        AppAiActionGraphStatus status,
        string summary,
        CancellationToken cancellationToken)
    {
        if (actionGraph is null) return;
        await actionGraph.PublishAsync(new AppAiActionGraphEvent(
            snapshot.AppId,
            snapshot.SurfaceId,
            snapshot.DocumentId,
            actionId,
            correlationId,
            status,
            summary,
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private static bool SameTarget(AppAiContextSnapshot before, AppAiContextSnapshot after) =>
        string.Equals(before.AppId, after.AppId, StringComparison.Ordinal) &&
        string.Equals(before.SurfaceId, after.SurfaceId, StringComparison.Ordinal) &&
        string.Equals(before.DocumentId, after.DocumentId, StringComparison.Ordinal) &&
        string.Equals(before.Revision, after.Revision, StringComparison.Ordinal);

    private static void ValidateSnapshot(AppAiContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.SurfaceId);
        ArgumentNullException.ThrowIfNull(snapshot.SemanticState);
    }
}
