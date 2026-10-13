using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Application;

public sealed partial class SpaceRegistry
{
    private SpaceLocalStoreAuthority? _originalGraphAuthority;

    // Maintained Home composition only. The friend assembly is an encapsulation
    // seam, not a grant: the actual Home owner verifies the concrete same-store,
    // same-profile and same-receipt tuple before binding this original registry.
    internal bool IsBoundToOriginalSettings(IVersionedSettingsStore settings) => ReferenceEquals(_settings, settings);
    internal void BindOriginalGraphAuthority(SpaceLocalStoreAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (_settings is not IVersionedSettingsGuardedCompareExchange || !authority.IsBoundToOriginalSettings(_settings))
            throw new UnauthorizedAccessException("The same original guarded Space settings owner is required.");
        var prior = Interlocked.CompareExchange(ref _originalGraphAuthority, authority, null);
        if (prior is not null && !ReferenceEquals(prior, authority))
            throw new UnauthorizedAccessException("The original Space graph authority cannot be replaced.");
    }

    internal async Task<SpaceDefinition> ReserveGraphPublicationAsync(Guid spaceId, long expectedSpaceRevision,
        SpaceGraphPage page, GraphPublicationIntent intent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!Enum.IsDefined(page) || expectedSpaceRevision < 1 || expectedSpaceRevision == long.MaxValue ||
            intent.Owner.AppId != "spaces" || intent.Owner.EntityKind != "space.definition" || intent.Owner.EntityId != spaceId)
            throw new ArgumentException("An exact original Space graph proposal is required.");
        var authority = _originalGraphAuthority ?? throw new UnauthorizedAccessException("Original Home graph ownership is unavailable.");
        var admission = await authority.CaptureWriteAdmissionForActorAsync(intent.OriginalActor, token).ConfigureAwait(false);
        var arguments = intent.Arguments;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
        return await MutateOriginalGraphAsync(spaceId, expectedSpaceRevision, space =>
        {
            RequireBuilderRevision(space, expectedSpaceRevision);
            var configuration = space.GraphConfiguration ?? new();
            if (configuration.Pending is not null) throw new InvalidOperationException("Reconcile the existing graph publication before starting another.");
            var prior = configuration.GetReference(page);
            if (prior is null ? intent.Kind != GraphPublicationKind.SaveDraft || intent.ExpectedGraphRevision != 0
                : prior.GraphId != intent.Graph.GraphId || prior.CapabilityProfileId != intent.Graph.CapabilityProfileId ||
                    prior.DraftRevision != intent.ExpectedGraphRevision)
                throw new InvalidOperationException("The graph proposal no longer matches the Space's acknowledged graph reference.");
            if (intent.Scopes.Count(scope => scope.Kind == "space.definition" &&
                scope.Id == intent.Owner.StoreId.ToString("D") + "/" + spaceId.ToString("D") &&
                scope.Revision == (expectedSpaceRevision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                scope.Access == ResourceAccess.Write) != 1)
                throw new ArgumentException("The proposal must name its exact pending Space revision.");
            var pending = new SpaceGraphPendingPublication(intent.OperationId, page, intent.Kind, intent.Owner,
                intent.OriginalActor, expectedSpaceRevision, expectedSpaceRevision + 1, intent.ExpectedGraphRevision,
                intent.Graph.GraphId, intent.Graph.CapabilityProfileId, digest, arguments.Clone(), prior, _clock());
            return space with { GraphConfiguration = configuration with { Pending = pending } };
        }, new GraphStoreAdmission(intent.Owner.StoreId, admission), token).ConfigureAwait(false);
    }

    // The maintained Home owner supplies its PRIVATE claimed-association guard.
    // It must retain the exact live Home lease through the original settings
    // writer and release it after that writer joins. No UI/caller callback is
    // accepted by the public Space API and publication approval is not reused.
    internal Task<SpaceDefinition> AssociateGraphPublicationAsync(Guid spaceId, long pendingSpaceRevision,
        Guid operationId, GraphPublicationReceipt observedPublication, ISettingsCommitAdmission originalHomeAssociationGuard,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(observedPublication);
        ArgumentNullException.ThrowIfNull(originalHomeAssociationGuard);
        if (_originalGraphAuthority is null) throw new UnauthorizedAccessException("Original Home graph ownership is unavailable.");
        return MutateOriginalGraphAsync(spaceId, pendingSpaceRevision, space =>
        {
            var configuration = space.GraphConfiguration ?? throw new InvalidOperationException("The pending graph configuration is absent.");
            var pending = configuration.Pending ?? throw new InvalidOperationException("No original graph publication is pending.");
            if (pending.OperationId != operationId || pending.PendingSpaceRevision != pendingSpaceRevision ||
                observedPublication.OperationId != pending.OperationId || observedPublication.ArgumentsDigest != pending.ArgumentsDigest ||
                observedPublication.Kind != pending.Kind || observedPublication.Owner != pending.Owner ||
                observedPublication.GraphId != pending.GraphId || observedPublication.ExpectedGraphRevision != pending.ExpectedGraphRevision ||
                observedPublication.CommittedGraphRevision != (pending.Kind == GraphPublicationKind.SaveDraft
                    ? checked(pending.ExpectedGraphRevision + 1) : pending.ExpectedGraphRevision) ||
                configuration.GetReference(pending.Page) != pending.PriorReference)
                throw new InvalidOperationException("The observed publication does not match this exact pending Space/page operation.");
            var associated = new SpaceGraphReference(pending.GraphId, pending.CapabilityProfileId,
                observedPublication.CommittedGraphRevision,
                pending.Kind == GraphPublicationKind.Activate ? observedPublication.CommittedGraphRevision : pending.PriorReference?.ActiveRevision,
                observedPublication);
            return space with { GraphConfiguration = configuration.WithReference(pending.Page, associated) with { Pending = null } };
        }, new GraphStoreAdmission(observedPublication.Owner.StoreId, originalHomeAssociationGuard), token);
    }

    // Caller is the maintained Home owner after the ORIGINAL review has joined
    // and retired with a known no-publication outcome. No uncertain intent clears.
    internal async Task<SpaceDefinition> ClearUnpublishedGraphPendingAsync(Guid spaceId, long expectedRevision,
        Guid operationId, AuthenticatedResourceActor originalActor, CancellationToken token)
    {
        var authority = _originalGraphAuthority ?? throw new UnauthorizedAccessException("Original Home graph ownership is unavailable.");
        var admission = await authority.CaptureWriteAdmissionForActorAsync(originalActor, token).ConfigureAwait(false);
        return await MutateOriginalGraphAsync(spaceId, expectedRevision, space =>
        {
            var configuration = space.GraphConfiguration ?? throw new InvalidOperationException("The pending graph configuration is absent.");
            var pending = configuration.Pending ?? throw new InvalidOperationException("The original pending graph intent is absent.");
            if (pending.OperationId != operationId || pending.PendingSpaceRevision != expectedRevision || pending.OriginalActor != originalActor)
                throw new UnauthorizedAccessException("The exact original unpublished pending intent is required.");
            var cleared = configuration with { Pending = null };
            return space with { GraphConfiguration = cleared.Harness is null && cleared.Model is null && cleared.UserMessage is null ? null : cleared };
        }, admission, token).ConfigureAwait(false);
    }

    private async Task<SpaceDefinition> MutateOriginalGraphAsync(Guid spaceId, long expectedRevision,
        Func<SpaceDefinition, SpaceDefinition> mutation, ISettingsCommitAdmission admission, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
                exported.Settings.TryGetValue(SettingsKey, out var raw);
                var (state, _) = ReadComparedState(raw);
                var existing = FindRequired(state.Spaces, spaceId);
                RequireBuilderRevision(existing, expectedRevision);
                var changed = mutation(existing) with { Revision = checked(existing.Revision + 1), UpdatedAt = _clock() };
                ValidateDefinition(changed, state.Spaces.Where(space => space.Id != spaceId).ToArray());
                var next = state with { Version = CurrentVersion, Spaces = state.Spaces.Select(space => space.Id == spaceId ? changed : space).ToArray() };
                EnsurePendingDeletionsUnchanged(state, next);
                ValidateDeletionState(next);
                var replacement = JsonSerializer.Serialize(next with { Spaces = next.Spaces.Select(CloneSpace).ToArray() });
                if (await TryCommitAsync(SettingsKey, raw, replacement, new Dictionary<string, string?>(), admission, token).ConfigureAwait(false))
                    return CloneSpace(changed);
            }
            throw new InvalidOperationException("The Space registry kept changing; inspect the saved pending operation before retrying.");
        }
        finally { _gate.Release(); }
    }

    private sealed class GraphStoreAdmission(Guid originalStoreId, ISettingsCommitAdmission original) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token) =>
            context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == originalStoreId
                ? original.CheckAsync(context, token) : ValueTask.FromResult(false);
    }

    private static SpaceGraphConfiguration? CloneGraphConfiguration(SpaceGraphConfiguration? configuration) =>
        configuration is null ? null : configuration with
        {
            Pending = configuration.Pending is null ? null : configuration.Pending with
            { PublicationArguments = configuration.Pending.PublicationArguments.Clone() }
        };

    private static void ValidateGraphConfiguration(SpaceDefinition space)
    {
        if (space.GraphConfiguration is not { } configuration) return;
        var references = new[] { configuration.Harness, configuration.Model, configuration.UserMessage }
            .Where(reference => reference is not null).Cast<SpaceGraphReference>().ToArray();
        if (references.Select(reference => reference.GraphId).Distinct().Count() != references.Length)
            throw new ArgumentException("Each Expert page must retain its own canonical graph reference.");
        foreach (var reference in references)
        {
            var receipt = reference.LastAssociatedPublication;
            if (reference.GraphId == Guid.Empty || string.IsNullOrWhiteSpace(reference.CapabilityProfileId) ||
                reference.DraftRevision < 1 || reference.ActiveRevision is < 1 || reference.ActiveRevision > reference.DraftRevision ||
                receipt is null || receipt.GraphId != reference.GraphId || receipt.CommittedGraphRevision != reference.DraftRevision ||
                receipt.OperationId == Guid.Empty || !ValidGraphDigest(receipt.ArgumentsDigest) || !Enum.IsDefined(receipt.Kind) ||
                receipt.ExpectedGraphRevision != (receipt.Kind == GraphPublicationKind.SaveDraft ? reference.DraftRevision - 1 : reference.DraftRevision) ||
                receipt.Owner is null || receipt.Owner.AppId != "spaces" || receipt.Owner.EntityKind != "space.definition" ||
                receipt.Owner.EntityId != space.Id || receipt.Owner.StoreId == Guid.Empty)
                throw new ArgumentException("The canonical Space graph association is invalid.");
        }
        if (configuration.Pending is not { } pending) return;
        if (!Enum.IsDefined(pending.Page) || !Enum.IsDefined(pending.Kind) || pending.OperationId == Guid.Empty ||
            pending.GraphId == Guid.Empty || pending.OriginalSpaceRevision < 1 || pending.OriginalSpaceRevision == long.MaxValue ||
            pending.PendingSpaceRevision != pending.OriginalSpaceRevision + 1 || pending.PendingSpaceRevision != space.Revision ||
            pending.Owner is null || pending.Owner.AppId != "spaces" || pending.Owner.EntityKind != "space.definition" || pending.Owner.EntityId != space.Id ||
            pending.Owner.StoreId == Guid.Empty || pending.OriginalActor is null || pending.OriginalActor.AccountId is not null ||
            pending.OriginalActor.OrganisationId is not null || string.IsNullOrWhiteSpace(pending.OriginalActor.ProfileId) ||
            string.IsNullOrWhiteSpace(pending.OriginalActor.AuthenticationRevision) || pending.ExpectedGraphRevision < 0 ||
            pending.Kind == GraphPublicationKind.Activate && pending.ExpectedGraphRevision == 0 ||
            !ValidGraphDigest(pending.ArgumentsDigest) || pending.PublicationArguments.ValueKind != JsonValueKind.Object ||
            pending.PriorReference != configuration.GetReference(pending.Page) || pending.RequestedAt == default ||
            string.IsNullOrWhiteSpace(pending.CapabilityProfileId) ||
            (pending.PriorReference is null ? pending.Kind != GraphPublicationKind.SaveDraft || pending.ExpectedGraphRevision != 0 :
                pending.PriorReference.GraphId != pending.GraphId || pending.PriorReference.CapabilityProfileId != pending.CapabilityProfileId ||
                pending.PriorReference.DraftRevision != pending.ExpectedGraphRevision) ||
            Enum.GetValues<SpaceGraphPage>().Any(page => page != pending.Page && configuration.GetReference(page)?.GraphId == pending.GraphId))
            throw new ArgumentException("The pending Space graph publication metadata is invalid.");
        var encoded = Encoding.UTF8.GetBytes(pending.PublicationArguments.GetRawText());
        if (encoded.Length > 8 * 1024 * 1024 ||
            Convert.ToHexString(SHA256.HashData(encoded)) != pending.ArgumentsDigest)
            throw new ArgumentException("The pending Space graph publication digest is invalid or exceeds the owning admission bound.");
        try
        {
            var arguments = pending.PublicationArguments.Deserialize<StoredGraphPublicationArguments>();
            if (arguments is null || arguments.SchemaVersion != 1 || arguments.Owner != pending.Owner ||
                arguments.OriginalActor != pending.OriginalActor || arguments.OperationId != pending.OperationId ||
                arguments.Kind != pending.Kind || arguments.ExpectedGraphRevision != pending.ExpectedGraphRevision ||
                arguments.Graph is not { } graph || graph.SchemaVersion != GraphDocument.CurrentSchemaVersion ||
                graph.GraphId != pending.GraphId || graph.CapabilityProfileId != pending.CapabilityProfileId ||
                graph.State != GraphRevisionState.Draft || graph.Revision != (pending.Kind == GraphPublicationKind.SaveDraft
                    ? checked(pending.ExpectedGraphRevision + 1) : pending.ExpectedGraphRevision) ||
                arguments.Scopes is not { Length: 2 } scopes ||
                !scopes.Contains(new ResourceScope("space.definition", pending.Owner.StoreId.ToString("D") + "/" + space.Id.ToString("D"),
                    pending.PendingSpaceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)) ||
                !scopes.Contains(new ResourceScope("nodegraph.definition", pending.OriginalActor.ProfileId + "/" + pending.GraphId.ToString("D"),
                    pending.ExpectedGraphRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceAccess.Write)))
                throw new ArgumentException("The retained publication arguments disagree with the original Space graph intent.");
        }
        catch (JsonException error) { throw new ArgumentException("The retained Space graph publication arguments are invalid.", error); }
    }

    private sealed record StoredGraphPublicationArguments(int SchemaVersion, GraphOwnerDescriptor Owner,
        AuthenticatedResourceActor OriginalActor, Guid OperationId, GraphPublicationKind Kind,
        GraphDocument Graph, long ExpectedGraphRevision, ResourceScope[] Scopes);

    private static bool ValidGraphDigest(string digest) => digest is { Length: 64 } && digest.All(Uri.IsHexDigit);

    private static void EnsureGraphPublicationFieldsUnchanged(SpaceRegistryState before, SpaceRegistryState after)
    {
        foreach (var changed in after.Spaces)
        {
            var prior = before.Spaces.SingleOrDefault(space => space.Id == changed.Id);
            if (JsonSerializer.Serialize(prior?.GraphConfiguration) != JsonSerializer.Serialize(changed.GraphConfiguration))
                throw new InvalidOperationException("Graph references are maintained by the original Home graph publication owner.");
        }
        foreach (var pending in before.Spaces.Where(space => space.GraphConfiguration?.Pending is not null))
        {
            var changed = after.Spaces.SingleOrDefault(space => space.Id == pending.Id);
            if (changed is null || JsonSerializer.Serialize(changed) != JsonSerializer.Serialize(pending))
                throw new InvalidOperationException("This Space has a pending graph publication. Reconcile its observed outcome before editing or deleting it.");
        }
    }
}
