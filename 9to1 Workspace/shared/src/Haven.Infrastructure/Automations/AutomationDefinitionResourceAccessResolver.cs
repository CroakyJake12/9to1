using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Checks actual personal store ownership and canonical protected rows for definition operations.
/// This read observation is not graph execution authority or the final SQL commit admission.</summary>
public abstract class AutomationDefinitionResourceAccessResolver(IAutomationOwnerRepository definitions,
    IReusableTaskOwnerRepository tasks, IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership, bool reusable) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => reusable ? "automation.reusable-task" : "automation.definition";
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "PermissionDenied", actor.ActorId, scope.Revision, actor.OrganisationId);
        var graphPublication = actionId is "automations.graph.save-draft" or "automations.activate";
        if (scope.Kind != ResourceKind || scope.Access != ResourceAccess.Write ||
            !(actionId is "automations.create" or "automations.update" or "automations.disable" or "automations.delete" or "automations.restore" or "automations.recover" || reusable && graphPublication) ||
            actor.AccountId is not null || actor.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(actor.ProfileId) || string.IsNullOrWhiteSpace(actor.ActorId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || ownership is not IResourceStoreOwnershipReceiptAuthority receipts)
            return Deny();
        var parts = scope.Id.Split('/');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out var storeId) || storeId == Guid.Empty ||
            !Guid.TryParseExact(parts[1], "D", out var entityId) || entityId == Guid.Empty ||
            !long.TryParse(scope.Revision, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var revision) || revision < 0 || revision == long.MaxValue)
            return Deny();
        try
        {
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Deny();
            var identity = await definitions.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
            if (identity.SchemaVersion != 1 || identity.StoreId != storeId) return Deny();
            var binding = await ownership.GetVerifiedAsync("automations", storeId.ToString("D"), cancellationToken).ConfigureAwait(false);
            if (binding?.Receipt is null || binding.ResourceKind != "automations" || binding.StoreId != storeId.ToString("D") ||
                binding.ProfileId != actor.ProfileId || !await receipts.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false)) return Deny();
            var graphEligible = false;
            bool exists, recovery; string? recoveryCode; long currentRevision; AutomationOwnerBinding? owner;
            if (reusable)
            {
                var row = await tasks.GetOwnedTaskAsync(entityId, cancellationToken).ConfigureAwait(false);
                graphEligible = row is not null && !row.Value.IsEnabled && row.Value.ArchivedAt is null;
                exists = row is not null; recovery = row?.RequiresRecovery ?? false; recoveryCode = row?.RecoveryCode;
                currentRevision = row?.Value.Revision ?? 0; owner = row?.Value.OwnerBinding;
            }
            else
            {
                var row = await definitions.GetOwnedAsync(entityId, cancellationToken).ConfigureAwait(false);
                exists = row is not null; recovery = row?.RequiresRecovery ?? false; recoveryCode = row?.RecoveryCode;
                currentRevision = row?.Value.Revision ?? 0; owner = row?.Value.OwnerBinding;
            }
            if (graphPublication && (!reusable || !graphEligible)) return Deny();
            if (actionId == "automations.create")
            { if (exists || revision != 0) return Deny(); }
            else if (actionId == "automations.recover")
            { if (!exists || !recovery || recoveryCode != "LegacyUnboundDefinition" || revision != 0 || owner is not null) return Deny(); }
            else if (!exists || recovery || currentRevision != revision || owner is null || owner.StoreId != storeId ||
                owner.ProfileId != actor.ProfileId || owner.AccountId != actor.AccountId || owner.OrganisationId != actor.OrganisationId)
                return Deny();
            if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor ||
                (await definitions.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false)).StoreId != storeId ||
                !await receipts.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false)) return Deny();
            return new(true, "Allowed", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or NotSupportedException or System.Data.Common.DbException) { return Deny(); }
    }
}

public sealed class AutomationOwnerDefinitionAccessResolver(IAutomationOwnerRepository definitions,
    IReusableTaskOwnerRepository tasks, IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership)
    : AutomationDefinitionResourceAccessResolver(definitions, tasks, actors, ownership, false) { }
public sealed class AutomationOwnerReusableTaskAccessResolver(IAutomationOwnerRepository definitions,
    IReusableTaskOwnerRepository tasks, IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership)
    : AutomationDefinitionResourceAccessResolver(definitions, tasks, actors, ownership, true) { }
