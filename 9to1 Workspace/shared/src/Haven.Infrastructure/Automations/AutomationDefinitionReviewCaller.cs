using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace Haven.Infrastructure;

/// <summary>Actual same-SQL/Home definition review entrypoint; never enables graph/run publication.</summary>
public sealed class AutomationDefinitionReviewCaller(IAutomationOwnerRepository definitions,
    IReusableTaskOwnerRepository tasks, IResourceStoreOwnershipAuthority ownership,
    IAuthenticatedResourceActorSource actors, HomeResourceOperationBroker broker,
    HomePermissionTrustService permissions, AutomationHomeDefinitionOperation operation) : IAutomationDefinitionReviewCaller
{
    private readonly HomeResourceOperationBroker _broker = broker;
    private readonly HomePermissionTrustService _permissions = permissions;
    private readonly AutomationHomeDefinitionOperation _operation = operation;
    private sealed record Selection(AutomationDefinitionReviewCaller Issuer, Guid StoreId,
        AuthenticatedResourceActor Actor) : IAutomationDefinitionCallerSelection;
    public async Task<IAutomationDefinitionCallerSelection> CaptureAsync(AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) is not { } actual || actual != expectedActor)
            throw new UnauthorizedAccessException("Original automation actor is unavailable.");
        var identity = await definitions.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.StoreId == Guid.Empty || identity.SchemaVersion != 1 ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("Original automation store is unavailable.");
        var selection = new Selection(this, identity.StoreId, expectedActor);
        await RequireAsync(selection, cancellationToken).ConfigureAwait(false);
        return selection;
    }
    private async Task<Selection> RequireAsync(IAutomationDefinitionCallerSelection selection, CancellationToken token)
    {
        if (selection is not Selection current || !ReferenceEquals(current.Issuer, this))
            throw new UnauthorizedAccessException("Automation selection was not issued by this owner.");
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != current.Actor)
            throw new UnauthorizedAccessException("Original automation actor changed.");
        var identity = await definitions.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.StoreId != current.StoreId || identity.SchemaVersion != 1 ||
            await actors.GetCurrentAsync(token).ConfigureAwait(false) != current.Actor)
            throw new UnauthorizedAccessException("Original automation store changed.");
        if (ownership is not IResourceStoreOwnershipReceiptAuthority receipts)
            throw new UnauthorizedAccessException("Automation store ownership receipts are unavailable.");
        var binding = await ownership.GetVerifiedAsync("automations", current.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "automations" || binding.StoreId != current.StoreId.ToString("D") ||
            binding.ProfileId != current.Actor.ProfileId ||
            !await receipts.IsCurrentAsync(binding, current.Actor, token).ConfigureAwait(false) ||
            (await definitions.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != current.StoreId ||
            await actors.GetCurrentAsync(token).ConfigureAwait(false) != current.Actor)
            throw new UnauthorizedAccessException("Original automation store ownership is unavailable.");
        return current;
    }
    public async Task RequireCurrentAsync(IAutomationDefinitionCallerSelection selection, CancellationToken cancellationToken = default) =>
        await RequireAsync(selection, cancellationToken).ConfigureAwait(false);
    public async Task<AutomationDefinitionCallerLibrary> LoadLibraryAsync(IAutomationDefinitionCallerSelection selection,
        AutomationLibraryQuery query, CancellationToken cancellationToken = default)
    {
        await RequireAsync(selection, cancellationToken).ConfigureAwait(false);
        var definitionsPage = await definitions.ListOwnedAsync(query, cancellationToken).ConfigureAwait(false);
        var taskPage = await tasks.ListOwnedTasksAsync(query, cancellationToken).ConfigureAwait(false);
        await RequireAsync(selection, cancellationToken).ConfigureAwait(false);
        return new(definitionsPage, taskPage);
    }
    public async Task<IAutomationDefinitionCallerReview> ReviewAsync(IAutomationDefinitionCallerSelection selection,
        AutomationDefinition proposal, long expectedRevision, AutomationDefinitionChangeKind kind, CancellationToken cancellationToken = default)
    {
        if (selection is not Selection original || !ReferenceEquals(original.Issuer, this))
            throw new UnauthorizedAccessException("Automation selection was not issued by this owner.");
        var change = AutomationDefinitionChange.Capture(original.StoreId, original.Actor, proposal, expectedRevision, kind);
        await RequireAsync(original, cancellationToken).ConfigureAwait(false);
        return await ReviewChangeAsync(original, change, cancellationToken).ConfigureAwait(false);
    }
    public async Task<IAutomationDefinitionCallerReview> ReviewAsync(IAutomationDefinitionCallerSelection selection,
        ReusableTaskDefinition proposal, long expectedRevision, AutomationDefinitionChangeKind kind, CancellationToken cancellationToken = default)
    {
        if (selection is not Selection original || !ReferenceEquals(original.Issuer, this))
            throw new UnauthorizedAccessException("Automation selection was not issued by this owner.");
        var change = AutomationDefinitionChange.Capture(original.StoreId, original.Actor, proposal, expectedRevision, kind);
        await RequireAsync(original, cancellationToken).ConfigureAwait(false);
        return await ReviewChangeAsync(original, change, cancellationToken).ConfigureAwait(false);
    }
    private async Task<IAutomationDefinitionCallerReview> ReviewChangeAsync(Selection original, AutomationDefinitionChange change, CancellationToken token)
    {
        if (change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph || change.Automation is { IsEnabled: true } ||
            change.ReusableTask is { IsEnabled: true }) throw new InvalidOperationException("Automation publication and run authority are unavailable.");
        await RequireAsync(original, token).ConfigureAwait(false);
        var request = await broker.AuthorizeForActorAsync(original.Actor, AutomationDefinitionChange.TargetAppID,
            change.ActionID, change.Scopes, change.Arguments, "Review this exact automation library change", null,
            "automation-definition-caller", token).ConfigureAwait(false);
        // Retain exact issued intent/request even if the session changes during durable review delivery.
        return new Review(this, original, change, request.RequestId);
    }
    private sealed class Review(AutomationDefinitionReviewCaller issuer, Selection original,
        AutomationDefinitionChange change, string requestId) : IAutomationDefinitionCallerReview
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _beginAttempted;
        private AutomationDefinitionOperationResult? _result;
        private AutomationDefinitionCommitObservationRecovery? _observation;
        private HomeResourceExecutionCapability? _capability;
        public Guid OperationId => change.OperationID;
        public string RequestId => requestId;
        public async Task<AutomationDefinitionCallerOutcome> FinishAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_result is not null)
                {
                    if (_result.ObservationRecovery is { } observation)
                        _result = await observation.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    else if (_result.AuditRecovery is { } audit && await audit.FinishAsync(cancellationToken).ConfigureAwait(false))
                        _result = _result with { Committed = audit.IsUnclaimedAbort ? false : _result.Committed,
                            Code = _result.Committed == true ? "DefinitionCommitted" : "DefinitionNotCommitted", AuditRecovery = null };
                    return new(_result.Committed, _result.Code);
                }
                if (_observation is not null)
                {
                    _result = await _observation.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    return new(_result.Committed, _result.Code);
                }
                if (_beginAttempted) return new(null, "DefinitionBeginOutcomeUnconfirmed");
                await issuer.RequireAsync(original, cancellationToken).ConfigureAwait(false);
                var approval = await issuer._permissions.GetAuthorizationAsync(requestId, cancellationToken).ConfigureAwait(false);
                if (!approval.IsAllowed) return new(null, "DefinitionReview" + approval.State);
                await issuer.RequireAsync(original, cancellationToken).ConfigureAwait(false);
                _beginAttempted = true; // Never replay an ambiguous consumed Begin transport.
                var capability = await issuer._broker.BeginExecutionCapabilityAsync(requestId, change.Arguments, cancellationToken).ConfigureAwait(false);
                if (capability is null) return new(null, "DefinitionBeginOutcomeUnconfirmed");
                // Owner operation records a rejected claim after a changed original actor; never abandon/regrant capability.
                _capability = capability;
                try { _observation = issuer._operation.PrepareObservation(change, _capability); }
                catch (InvalidOperationException)
                {
                    _result = await issuer._operation.AbortUnstartedAsync(change, _capability).ConfigureAwait(false);
                    return new(_result.Committed, _result.Code);
                }
                try { _result = await issuer._operation.ExecuteAsync(change, _capability, cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or
                    NotSupportedException or OperationCanceledException or System.Data.Common.DbException)
                { _result = await _observation.ObserveAsync(CancellationToken.None).ConfigureAwait(false); }
                return new(_result.Committed, _result.Code);
            }
            finally { _gate.Release(); }
        }
    }
}
