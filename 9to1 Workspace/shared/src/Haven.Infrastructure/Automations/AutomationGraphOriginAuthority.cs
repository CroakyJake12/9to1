using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;

namespace Haven.Infrastructure;

/// <summary>Trusted Automation module adapter. Public descriptors never substitute for the
/// private original caller selection or the actual canonical SQL factory.</summary>
public sealed class AutomationGraphOriginAuthority(AutomationDefinitionReviewCaller caller,
    AutomationRepository definitions, WorkspaceStateRepository tasks, SqliteDatabase database, IVersionedNodeGraphRepository graphs)
    : IGraphPublicationOriginAuthority
{
    public string OwnerAppId => "automations";
    public bool IsBoundToGraphRepository(IVersionedNodeGraphRepository actualRepository) =>
        ReferenceEquals(graphs, actualRepository);
    private sealed record Selection(AutomationGraphOriginAuthority Issuer,
        IAutomationDefinitionCallerSelection Original) : IOriginalGraphOwnerSelection;

    public async Task<IOriginalGraphOwnerSelection> CaptureAsync(
        IAutomationDefinitionCallerSelection original, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!caller.HasRepositoryOrigin(original, definitions, tasks))
            throw new UnauthorizedAccessException("The original caller selection belongs to a different SQL repository graph.");
        await caller.RequireCurrentAsync(original, token).ConfigureAwait(false);
        RequireFactory();
        if ((await database.GetStoreIdentityAsync(token).ConfigureAwait(false)).StoreId != original.StoreId)
            throw new UnauthorizedAccessException("The original Automation SQL store changed.");
        await caller.RequireCurrentAsync(original, token).ConfigureAwait(false);
        return new Selection(this, original);
    }

    public async ValueTask RequireCurrentAsync(IOriginalGraphOwnerSelection originalSelection,
        GraphPublicationIntent intent, CancellationToken cancellationToken)
    {
        if (originalSelection is not Selection selected || !ReferenceEquals(selected.Issuer, this))
            throw new UnauthorizedAccessException("This Automation graph owner did not issue the original selection.");
        ArgumentNullException.ThrowIfNull(intent);
        RequireFactory();
        if (!caller.HasRepositoryOrigin(selected.Original, definitions, tasks))
            throw new UnauthorizedAccessException("The original caller repository graph changed.");
        await caller.RequireCurrentAsync(selected.Original, cancellationToken).ConfigureAwait(false);
        if (intent.Owner.AppId != OwnerAppId || intent.Owner.EntityKind != "automation.reusable-task" ||
            intent.Owner.StoreId != selected.Original.StoreId || intent.OriginalActor != selected.Original.Actor)
            throw new UnauthorizedAccessException("The graph proposal does not retain the original owning store and actor.");
        var identity = await database.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var row = await tasks.GetOwnedTaskAsync(intent.Owner.EntityId, cancellationToken).ConfigureAwait(false);
        if (identity.StoreId != selected.Original.StoreId || row is null || row.RequiresRecovery ||
            row.Value.OwnerBinding is not { } owner || owner.StoreId != selected.Original.StoreId ||
            owner.ProfileId != selected.Original.Actor.ProfileId || owner.AccountId != selected.Original.Actor.AccountId ||
            owner.OrganisationId != selected.Original.Actor.OrganisationId || row.Value.ArchivedAt is not null ||
            row.Value.IsEnabled || (row.Value.GraphBinding is { } graph
                ? graph.GraphId != intent.Graph.GraphId || graph.DraftRevision != intent.ExpectedGraphRevision
                : intent.ExpectedGraphRevision != 0 || intent.Kind != GraphPublicationKind.SaveDraft))
            throw new UnauthorizedAccessException("The actual original reusable graph owner is unavailable.");
        var actualGraph = await graphs.GetAsync(intent.Graph.GraphId, cancellationToken).ConfigureAwait(false);
        var binding = row.Value.GraphBinding;
        if (binding is null ? actualGraph is not null :
            actualGraph is null || actualGraph.OwnerAppId != OwnerAppId ||
            actualGraph.OwnerEntityId != intent.Owner.CanonicalEntityId ||
            actualGraph.Draft.Revision != binding.DraftRevision ||
            actualGraph.Active?.Revision != binding.ActiveRevision)
            throw new UnauthorizedAccessException("The actual Home graph diverged from its original SQL publication association; explicit journal recovery is required.");
        var expectedScope = selected.Original.StoreId.ToString("D") + "/" + row.Value.Id.ToString("D");
        if (!intent.Scopes.Any(scope => scope.Kind == "automation.reusable-task" &&
            scope.Id == expectedScope && scope.Revision == row.Value.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
            scope.Access == ResourceAccess.Write))
            throw new UnauthorizedAccessException("The publication does not bind the actual definition revision.");
        await caller.RequireCurrentAsync(selected.Original, cancellationToken).ConfigureAwait(false);
    }
    public async ValueTask RequirePublishedAsync(IOriginalGraphOwnerSelection originalSelection,
        GraphPublicationIntent intent, GraphPublicationReceipt knownPublication, CancellationToken cancellationToken)
    {
        if (originalSelection is not Selection selected || !ReferenceEquals(selected.Issuer, this) ||
            !caller.HasRepositoryOrigin(selected.Original, definitions, tasks))
            throw new UnauthorizedAccessException("The published graph observation lost its original SQL issuer.");
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(knownPublication);
        RequireFactory();
        await caller.RequireCurrentAsync(selected.Original, cancellationToken).ConfigureAwait(false);
        if (intent.Owner.AppId != OwnerAppId || intent.Owner.EntityKind != "automation.reusable-task" ||
            intent.Owner.StoreId != selected.Original.StoreId || intent.OriginalActor != selected.Original.Actor ||
            knownPublication.Owner != intent.Owner || knownPublication.OperationId != intent.OperationId ||
            knownPublication.Kind != intent.Kind || knownPublication.GraphId != intent.Graph.GraphId ||
            knownPublication.ExpectedGraphRevision != intent.ExpectedGraphRevision ||
            knownPublication.CommittedGraphRevision != intent.Graph.Revision)
            throw new UnauthorizedAccessException("The publication metadata does not match the original exact proposal.");
        var identity = await database.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        var row = await tasks.GetOwnedTaskAsync(intent.Owner.EntityId, cancellationToken).ConfigureAwait(false);
        if (identity.StoreId != selected.Original.StoreId || row is null || row.RequiresRecovery ||
            row.Value.OwnerBinding is not { } owner || owner.StoreId != selected.Original.StoreId ||
            owner.ProfileId != selected.Original.Actor.ProfileId || owner.AccountId != selected.Original.Actor.AccountId ||
            owner.OrganisationId != selected.Original.Actor.OrganisationId || row.Value.ArchivedAt is not null || row.Value.IsEnabled ||
            (row.Value.GraphBinding is { } binding
                ? binding.GraphId != intent.Graph.GraphId || binding.DraftRevision != intent.ExpectedGraphRevision
                : intent.ExpectedGraphRevision != 0 || intent.Kind != GraphPublicationKind.SaveDraft))
            throw new UnauthorizedAccessException("The original SQL owner changed after Home publication.");
        var scopeID = selected.Original.StoreId.ToString("D") + "/" + row.Value.Id.ToString("D");
        if (!intent.Scopes.Any(scope => scope.Kind == "automation.reusable-task" && scope.Id == scopeID &&
            scope.Revision == row.Value.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) && scope.Access == ResourceAccess.Write))
            throw new UnauthorizedAccessException("The original SQL revision scope changed.");
        var actual = await graphs.GetAsync(intent.Graph.GraphId, cancellationToken).ConfigureAwait(false);
        if (actual is null || actual.CanonicalOwner != intent.Owner || actual.OwnerAppId != OwnerAppId ||
            actual.OwnerEntityId != intent.Owner.CanonicalEntityId || actual.LastPublication != knownPublication ||
            !System.Text.Json.JsonElement.DeepEquals(System.Text.Json.JsonSerializer.SerializeToElement(actual.Draft),
                System.Text.Json.JsonSerializer.SerializeToElement(intent.Graph)) ||
            intent.Kind == GraphPublicationKind.Activate && !System.Text.Json.JsonElement.DeepEquals(
                System.Text.Json.JsonSerializer.SerializeToElement(actual.Active),
                System.Text.Json.JsonSerializer.SerializeToElement(intent.Graph with { State = GraphRevisionState.Active })))
            throw new UnauthorizedAccessException("The actual complete Home publication body or receipt changed.");
        await caller.RequireCurrentAsync(selected.Original, cancellationToken).ConfigureAwait(false);
        // Metadata matched a genuine read. Only Home's private issuer may create its sealed observation;
        // this check itself never returns a SQL admission or an execution permit.
    }
    private void RequireFactory()
    {
        if (!definitions.UsesConnectionFactory(database) || !tasks.UsesConnectionFactory(database))
            throw new UnauthorizedAccessException("The owning repositories do not share the original canonical SQL factory.");
    }
}
