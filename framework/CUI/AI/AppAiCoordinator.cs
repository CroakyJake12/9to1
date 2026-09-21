namespace NineToOne.Cui.AI;

public sealed class AppAiCoordinator(
    IAppAiContext context,
    IAppAiActions actions,
    IAppAiApprovalVerifier approvals,
    IDulcheAppClient dulche)
{
    public ValueTask<AppAiContextSnapshot> CaptureContextAsync(CancellationToken cancellationToken = default) =>
        context.CaptureAsync(cancellationToken);

    public async ValueTask<AppAiActionResult> ExecuteAsync(
        AppAiActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = await context.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(snapshot.AppId, request.AppId, StringComparison.Ordinal))
            return AppAiActionResult.Rejected("The action does not belong to the current application.", "app-mismatch");

        var descriptor = actions.Actions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, request.ActionId, StringComparison.Ordinal));
        if (descriptor is null)
            return AppAiActionResult.Rejected("The requested action is not available.", "unknown-action");

        if (descriptor.RequiresReview)
        {
            if (string.IsNullOrWhiteSpace(request.ApprovalToken))
                return AppAiActionResult.Rejected("Review and approval are required.", "approval-required");

            var approved = await approvals.VerifyAsync(
                request.AppId,
                request.ActionId,
                request.ApprovalToken,
                cancellationToken).ConfigureAwait(false);
            if (!approved)
                return AppAiActionResult.Rejected("The approval is invalid or expired.", "approval-invalid");
        }

        return await actions.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(
        string prompt,
        string correlationId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var snapshot = await context.CaptureAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in dulche.StreamAsync(
            new AppAiPrompt(prompt.Trim(), snapshot, correlationId),
            cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }
}
