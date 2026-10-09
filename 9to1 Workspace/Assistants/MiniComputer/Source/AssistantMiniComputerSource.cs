using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.MiniComputer;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.MiniComputer;

/// <summary>Product adapter over the SAME canonical Mini Computer engine/catalogue.
/// It creates no VM, task, provider registry, permission broker or metadata store.</summary>
public sealed partial class AssistantMiniComputerSource : ICanonicalMiniComputerOperationSource, IAsyncDisposable
{
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly MiniComputerEngine _engine;
    private readonly CanonicalMiniComputerCatalogOriginalOwner _catalog;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly AssistantMiniComputerOriginals _originals = new();
    private readonly object _gate = new();
    private ICanonicalMiniComputerHomeOperationSource? _operations;
    private readonly Dictionary<Guid, WeakReference<Intent>> _intents = [];

    public AssistantMiniComputerSource(HomePersonalDenFactory actualHome,
        IConversationRepository actualConversations, MiniComputerEngine actualEngine,
        CanonicalMiniComputerCatalogOriginalOwner actualCatalog, HomeLocalProfileIdentity actualProfiles,
        HomeResourceStoreOwnershipAuthority actualOwnership)
    {
        ArgumentNullException.ThrowIfNull(actualHome); ArgumentNullException.ThrowIfNull(actualConversations);
        ArgumentNullException.ThrowIfNull(actualEngine); ArgumentNullException.ThrowIfNull(actualCatalog);
        ArgumentNullException.ThrowIfNull(actualProfiles); ArgumentNullException.ThrowIfNull(actualOwnership);
        if (actualEngine.OriginalCatalogStore is not ICanonicalMiniComputerCatalogLocation location ||
            !actualCatalog.HasOriginalComposition(location, actualProfiles))
            throw new ArgumentException("Mini Computer requires the SAME actual engine, configured protected catalogue and Home profile.");
        _home = actualHome; _conversations = actualConversations; _engine = actualEngine;
        _catalog = actualCatalog; _profiles = actualProfiles; _ownership = actualOwnership;
        actualEngine.BindOriginalOperationSource(this);
    }
    public bool HasOriginalComposition(HomePersonalDenFactory home, IConversationRepository conversations,
        MiniComputerEngine engine, CanonicalMiniComputerCatalogOriginalOwner catalog,
        HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership) =>
        ReferenceEquals(_home, home) && ReferenceEquals(_conversations, conversations) && ReferenceEquals(_engine, engine) &&
        ReferenceEquals(_catalog, catalog) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_ownership, ownership);
    public bool HasOriginalHomeDenFactory(HomePersonalDenFactory home) => ReferenceEquals(_home, home);
    public void BindOriginalHomeOperationSource(ICanonicalMiniComputerHomeOperationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (_operations is not null && !ReferenceEquals(_operations, source)) throw new InvalidOperationException("The original Mini Computer Home operation source is already bound.");
            _operations = source;
        }
    }
    public bool HasOriginalHomeOperationSource(ICanonicalMiniComputerHomeOperationSource source) => ReferenceEquals(_operations, source);

    internal sealed record CatalogInput(AssistantMiniComputerSource Owner, AssistantCanonicalMembershipSource Membership,
        AssistantConversationBinding Binding, AuthenticatedResourceActor Actor,
        VerifiedResourceStoreOwnership Permission, MiniComputerOriginalCatalogObservation Catalog);
    internal sealed record CatalogRead(CatalogInput? Input, string Reason);
    internal Task<CatalogRead> ReadWithinSourceAsync(AssistantConversationBinding binding,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Run(scope, retain, async source =>
    {
        var membership = RequireMembership(binding);
        var current = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        CanonicalMiniComputerCatalogOriginalOwner.Lease? lease = null;
        lease = await source.Read(() => _catalog.AcquireOriginalProtectedReadWithinSourceAsync(current.Actor,
            source.Run, source.Retain, token), value => { if (value is not null) source.OwnAsync(value); lease = value; }).ConfigureAwait(false);
        if (lease is null) return new CatalogRead(null,
            "The actual Mini Computer catalogue needs explicit owning setup. No catalogue, VM or identity was created by opening this view.");
        var permission = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(
            CanonicalMiniComputerCatalogOriginalOwner.CatalogResourceKind, lease.OriginalIdentity.StoreId.ToString("D"),
            source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (!Matches(permission, current.Actor, lease.OriginalIdentity) ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(permission!, current.Actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            return new CatalogRead(null, "Review and import this exact Mini Computer catalogue through Home before its VM records can be shown.");
        var catalog = await source.Read(() => _engine.ReadOriginalCatalogWithinSourceAsync(lease,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var final = await source.Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (final.Actor != current.Actor || !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(
            permission!, current.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Assistant or Mini Computer catalogue permission changed.");
        return new CatalogRead(new(this, membership, binding, current.Actor, permission!, catalog),
            "Saved VM catalogue records. Inspect requests a separate Home approval before the provider is queried. Closing this view leaves VMs running.");
    });

    private AssistantCanonicalMembershipSource RequireMembership(AssistantConversationBinding binding)
    {
        var actual = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        if (actual is null || !ReferenceEquals(actual.OriginalHomeDenFactory, _home) ||
            !ReferenceEquals(actual.OriginalConversations, _conversations))
            throw new UnauthorizedAccessException("The SAME actual Assistant conversation membership is required.");
        return actual;
    }
    private static bool Matches(VerifiedResourceStoreOwnership? permission, AuthenticatedResourceActor actor,
        ResourceStoreIdentity identity) => permission is { Receipt: not null } &&
        permission.ResourceKind == CanonicalMiniComputerCatalogOriginalOwner.CatalogResourceKind &&
        permission.StoreId == identity.StoreId.ToString("D") && permission.ProfileId == actor.ProfileId;

    private sealed class Intent(AssistantMiniComputerSource owner, CatalogInput input,
        VerifiedResourceStoreOwnership den, CanonicalMiniComputerTarget target,
        CanonicalMiniComputerAction action, Guid operation) : ICanonicalMiniComputerOperationIntent
    {
        internal AssistantMiniComputerSource Owner => owner;
        internal CatalogInput Input => input;
        public AuthenticatedResourceActor Actor => input.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => input.Catalog.StoreIdentity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => input.Permission;
        public VerifiedResourceStoreOwnership OriginalDenOwnership => den;
        public CanonicalMiniComputerTarget Target => target;
        public CanonicalMiniComputerAction Action => action;
        public Guid OperationId => operation;
        public string DenId => input.Binding.Definition.Identity.DenId;
        public string NamespaceId => input.Binding.Definition.Identity.NamespaceId;
        public string DefinitionId => input.Binding.Definition.Identity.DefinitionId;
        public long DefinitionRevision => input.Binding.Definition.Revision;
        public Guid ConversationId => input.Binding.Conversation.Id;
        // This user-directed VM view is not a model Task operation. A conversation ID
        // must never be invented as Task identity or as a task execution grant.
        public Guid? TaskId => null;
    }
    internal Task<ICanonicalMiniComputerOperationIntent?> PrepareWithinSourceAsync(CatalogInput sameInput,
        CanonicalMiniComputerAction action, Guid operation, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => _originals.Run<ICanonicalMiniComputerOperationIntent?>(scope, retain, async source =>
    {
        if (!ReferenceEquals(sameInput.Owner, this) || operation == Guid.Empty || !Enum.IsDefined(action) || _operations is null)
            return null;
        var definition = sameInput.Binding.Definition;
        if (!definition.Configuration.Enabled || definition.Configuration.Archived ||
            !Guid.TryParse(definition.Configuration.MiniComputerId, out var id)) return null;
        var vm = sameInput.Catalog.Snapshot.VirtualMachines.SingleOrDefault(value => value.VMID.Value == id);
        if (vm is null) return null;
        var current = await source.Read(() => ReadWithinSourceAsync(sameInput.Binding,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        if (current.Input is null || current.Input.Catalog.Sha256 != sameInput.Catalog.Sha256 || current.Input.Actor != sameInput.Actor)
            return null;
        var den = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync("den", definition.Identity.DenId,
            source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (den is not { Receipt: not null } || den.ResourceKind != "den" || den.StoreId != definition.Identity.DenId ||
            den.ProfileId != sameInput.Actor.ProfileId || !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(
                den, sameInput.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            return null;
        lock (_gate)
        {
            if (_intents.TryGetValue(operation, out var weak) && weak.TryGetTarget(out var prior))
                return ReferenceEquals(prior.Input, sameInput) && prior.Action == action ? prior : null;
            foreach (var key in _intents.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) _intents.Remove(key);
            if (_intents.Count >= 128) throw new InvalidOperationException("Retained original VM operations require retirement.");
            var intent = new Intent(this, sameInput, den, new(vm.VMID.Value, vm.ProviderID.Value, vm.ProviderMachineID,
                vm.Name, vm.ConfigurationVersion, vm.Revision, sameInput.Catalog.Sha256), action, operation);
            _intents[operation] = new(intent); return intent;
        }
    });
    public bool IsIssuedOriginalOperationIntent(ICanonicalMiniComputerOperationIntent value)
    { lock (_gate) return value is Intent actual && ReferenceEquals(actual.Owner, this) && _intents.TryGetValue(actual.OperationId, out var weak) && weak.TryGetTarget(out var known) && ReferenceEquals(known, actual); }
    private Intent RequireIntent(ICanonicalMiniComputerOperationIntent value) => IsIssuedOriginalOperationIntent(value)
        ? (Intent)value : throw new UnauthorizedAccessException("The SAME privately issued Mini Computer operation is required.");
    public string GetOriginalOperationIntentDigest(ICanonicalMiniComputerOperationIntent value)
    {
        var actual = RequireIntent(value);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { actual.Actor, actual.OriginalStoreIdentity, actual.Target, actual.Action, actual.OperationId,
          actual.DenId, actual.NamespaceId, actual.DefinitionId, actual.DefinitionRevision, actual.ConversationId, actual.TaskId })));
    }
    public Task ValidateOriginalOperationIntentWithinSourceAsync(ICanonicalMiniComputerOperationIntent value,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Run(scope, retain, async source =>
    {
        var actual = RequireIntent(value);
        var current = await source.Read(() => ReadWithinSourceAsync(actual.Input.Binding, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (current.Input is null || current.Input.Actor != actual.Actor || current.Input.Catalog.StoreIdentity != actual.OriginalStoreIdentity ||
            current.Input.Catalog.Sha256 != actual.Target.OriginalCatalogSha256 ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(actual.OriginalDenOwnership, actual.Actor,
                source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The exact Mini Computer catalogue, Assistant or Home permission changed.");
        return true;
    });
    public Task? OriginalClose => _originals.OriginalClose;
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalJoin();
    public void RequestRetirement() => _originals.RequestRetirement();
    public Task CloseAndDrainAsync() => _originals.CloseAndDrain();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
