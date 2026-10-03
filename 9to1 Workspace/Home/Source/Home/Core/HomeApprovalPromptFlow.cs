using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Trusted native host navigation port. Display observations are never permission decisions.</summary>
public interface IHomeApprovalPromptPresenter
{
    /// <summary>Shows the exact pending request in Home and waits for a decision or explicit close.
    /// Returns whether the native scene acknowledged display; approval is read from the durable broker.</summary>
    ValueTask<bool> ShowPendingRequestAsync(string requestId, CancellationToken cancellationToken);
}

/// <summary>Uses the canonical broker and the configured native Home shell. No private UI fallback or grant.</summary>
public sealed class HomeApprovalPromptFlow(HomePermissionTrustService permissions,
    IHomeApprovalPromptPresenter? presenter = null)
{
    public async ValueTask<HomePermissionAuthorization> ReviewPendingAsync(string requestId,
        CancellationToken cancellationToken = default)
    {
        var original = await permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (original.State != HomePermissionRequestState.PendingApproval) return original;
        if (presenter is null) return Required(original);
        bool displayed;
        try { displayed = await presenter.ShowPendingRequestAsync(requestId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return Required(original); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!displayed) return Required(original);
        // Returning from, or closing, a prompt is not approval. Only the actual broker's decision counts.
        return await permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
    }

    private static HomePermissionAuthorization Required(HomePermissionAuthorization request) => request with
    {
        State = HomePermissionRequestState.PendingApproval,
        Code = "HOME_PERMISSION_REQUIRED",
        Message = "Open Home to review this pending request. The native prompt could not be displayed; the action remains blocked.",
        TrustLevel = null
    };
}
