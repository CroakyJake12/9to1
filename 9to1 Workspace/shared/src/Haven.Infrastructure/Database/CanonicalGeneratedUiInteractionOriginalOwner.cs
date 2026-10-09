using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Original interaction storage over the SAME maintained genui_apps table.
/// Source provenance and Home permissions remain independent from SQL/native custody.</summary>
public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner : ICanonicalGeneratedUiInteractionOriginalReadSource, ICanonicalGeneratedUiInteractionOriginalWriteSource, ICanonicalGeneratedUiInteractionOriginalProcessSource, IAsyncDisposable
{
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly GenUiAppRepository _repository;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly ICanonicalGeneratedUiOriginalMessageAuthority _origins;
    private readonly GenUiInstanceStore _instances;
    private readonly object _gate = new();
    private readonly AsyncLocal<int> _logical = new();
    private readonly List<Original> _originals = [];
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalObservation, Observation> _observations = new();
    private ICanonicalGeneratedUiOriginalHomeWriteSource? _home;
    private bool _retiring;
    private Task? _close;
    internal const string DescriptorPrefix = "canonical.genui.interaction.v1.";
    internal const string OperationPrefix = "canonical.genui.operation.v1.";

    public CanonicalGeneratedUiInteractionOriginalOwner(CanonicalSqliteOriginalStoreOwner sameStore,
        SqliteDatabase sameDatabase, GenUiAppRepository sameRepository,
        HomeResourceStoreOwnershipAuthority sameOwnership, ICanonicalGeneratedUiOriginalMessageAuthority actualOrigins)
    {
        ArgumentNullException.ThrowIfNull(actualOrigins); ArgumentNullException.ThrowIfNull(sameOwnership);
        if (!sameStore.HasOriginalDatabase(sameDatabase) || !sameRepository.HasOriginalSqliteFactory(sameDatabase))
            throw new UnauthorizedAccessException("Use the SAME configured protected SQLite store/database/generated-app repository.");
        _store = sameStore; _repository = sameRepository; _ownership = sameOwnership; _origins = actualOrigins;
        _instances = actualOrigins.OriginalInstances;
    }
    public CanonicalSqliteOriginalStoreOwner OriginalStore => _store;
    public GenUiAppRepository OriginalRepository => _repository;
    public HomeResourceStoreOwnershipAuthority OriginalOwnership => _ownership;
    public ICanonicalGeneratedUiOriginalMessageAuthority OriginalMessageAuthority => _origins;
    public ICanonicalGeneratedUiOriginalHomeWriteSource? OriginalHomeWriteSource { get { lock (_gate) return _home; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    private sealed class Original(CanonicalSqliteOriginalSourceScope source)
    {
        internal readonly CanonicalSqliteOriginalSourceScope Source = source;
        internal readonly TaskCompletionSource<Task> Publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Raw;
        internal Task? Inner; // Actual factory Task retained separately from the final-publication wrapper.
        internal Task Observation = null!;
        internal bool Joined;
    }
    private sealed record CapturedOrigin(ICanonicalGeneratedUiOriginalSnapshot Source, AuthenticatedResourceActor Actor,
        Guid ConversationId, Guid MessageId, int Ordinal, string ContentDigest, string DeclarationDigest,
        VerifiedResourceStoreOwnership DenOwnership,
        GenUiDocument RuntimeDocument, GenUiOriginalInstanceObservation Registration,
        GenUiOriginalMutationReceipt? Mutation, string DefinitionJson);
    private sealed record RawValue(string StorageClass, string? Text = null, long? Integer = null, double? Real = null, string? Blob = null);
    private sealed record StoredInteraction(int SchemaVersion, Guid StoreId, Guid ConversationId, Guid MessageId,
        int TemplateOrdinal, string ContentSha256, string DeclarationSha256, Guid InstanceId, long Revision,
        string DefinitionSha256, string RowSha256, string? PreviousDescriptorSha256, CanonicalGeneratedUiOriginalCommitReceipt Receipt);
    private sealed record DurableOperation(int SchemaVersion, Guid StoreId, Guid OperationId,
        string IntentSha256, string BeforeRowSha256, string AfterRowSha256, StoredInteraction Descriptor);
    private sealed record StoredSnapshot(SortedDictionary<string, RawValue>? Row, string RowSha256,
        SortedDictionary<string, RawValue>? TargetRow, string TargetRowSha256, string MessageSourceSha256,
        string? DescriptorJson, StoredInteraction? Descriptor, DurableOperation? Operation);
    private sealed class Observation(CanonicalGeneratedUiInteractionOriginalOwner owner, CapturedOrigin origin,
        ResourceStoreIdentity identity, VerifiedResourceStoreOwnership ownership, StoredSnapshot stored,
        CanonicalGeneratedUiOriginalReadState state, string detail, GenUiAppDefinition? saved) : ICanonicalGeneratedUiOriginalObservation
    {
        internal readonly CanonicalGeneratedUiInteractionOriginalOwner Owner = owner;
        internal readonly CapturedOrigin Origin = origin;
        internal readonly VerifiedResourceStoreOwnership Ownership = ownership;
        internal readonly StoredSnapshot Stored = stored;
        public ICanonicalGeneratedUiOriginalSnapshot OriginalSnapshot => Origin.Source;
        public ResourceStoreIdentity OriginalStoreIdentity { get; } = identity;
        public CanonicalGeneratedUiOriginalReadState State { get; } = state;
        public string Detail { get; } = detail;
        public GenUiAppDefinition? SavedDefinition { get; } = saved;
        public CanonicalGeneratedUiOriginalCommitReceipt? OriginalReceipt => Stored.Descriptor?.Receipt;
    }
    public bool IsIssuedOriginalObservation(ICanonicalGeneratedUiOriginalObservation same) => same is Observation observed &&
        ReferenceEquals(observed.Owner, this) && _observations.TryGetValue(same, out var issued) && ReferenceEquals(issued, observed);
    private Observation RequireObservation(ICanonicalGeneratedUiOriginalObservation same) => IsIssuedOriginalObservation(same)
        ? (Observation)same : throw new UnauthorizedAccessException("Use the SAME privately issued original interaction observation.");

    private CapturedOrigin CaptureOrigin(ICanonicalGeneratedUiOriginalSnapshot same)
    {
        if (!_origins.IsIssuedOriginalSnapshot(same)) throw new UnauthorizedAccessException("The actual canonical message authority must issue this snapshot.");
        var definition = same.Definition;
        var captured = new CapturedOrigin(same, same.HomeActor, same.ConversationId, same.MessageId, same.TemplateOrdinal,
            same.MessageContentSha256, same.OriginalDeclarationSha256, same.OriginalDenOwnership, definition.Document, same.OriginalRegistration,
            same.OriginalMutation, JsonSerializer.Serialize(definition));
        if (captured.ConversationId == Guid.Empty || captured.MessageId == Guid.Empty || captured.Ordinal is < 0 or > 3 ||
            !IsDigest(captured.ContentDigest) || !IsDigest(captured.DeclarationDigest) ||
            captured.RuntimeDocument.Origin.ThreadId != captured.ConversationId || captured.RuntimeDocument.Origin.AppKey != "assistants" ||
            Encoding.UTF8.GetByteCount(captured.DefinitionJson) > GenerativeUiContractValidator.MaximumJsonBytes)
            throw new InvalidDataException("The issued origin is not a bounded canonical Assistant message/runtime snapshot.");
        if (captured.DenOwnership.ResourceKind != "den" || captured.DenOwnership.Receipt is null ||
            captured.DenOwnership.ProfileId != captured.Actor.ProfileId)
            throw new UnauthorizedAccessException("The SAME source-issued actual Home Den ownership receipt is required.");
        var semantic = GenUiSemanticValidator.ValidateAndRepair(definition);
        if (!semantic.IsValid || semantic.Repairs.Count != 0)
            throw new InvalidDataException("The original runtime definition must already satisfy the maintained semantic contract without repair.");
        DemandRuntime(captured); return captured;
    }
    private void DemandRuntime(CapturedOrigin origin)
    {
        if (!_origins.IsIssuedOriginalSnapshot(origin.Source)) throw new UnauthorizedAccessException("The original snapshot is no longer issued.");
        _origins.DemandOriginalSnapshotCurrent(origin.Source);
        var instances = _origins.OriginalInstances;
        if (!ReferenceEquals(instances, _instances)) throw new UnauthorizedAccessException("The configured runtime store was replaced.");
        var current = origin.Mutation is null ? instances.IsCurrentOriginalObservation(origin.Registration) :
            instances.IsOriginalContinuation(origin.Registration, origin.Mutation);
        var document = origin.Mutation?.OriginalSuccessor?.Document ?? origin.Registration.Document;
        if (!current || !ReferenceEquals(document, origin.RuntimeDocument) || !ReferenceEquals(instances.TryGet(document.Origin.InstanceId), document))
            throw new UnauthorizedAccessException("The SAME live runtime registration and acknowledged mutation lineage are required.");
    }
    private async Task DemandCurrent(CapturedOrigin origin, ResourceStoreIdentity identity, VerifiedResourceStoreOwnership receipt,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        await _store.DemandActorAsync(origin.Actor, source, token).ConfigureAwait(false);
        await source.Read(() => _origins.RevalidateOriginalSnapshotWithinSourceAsync(origin.Source, source.Run, source.Retain, token)).ConfigureAwait(false);
        source.Run(() => DemandRuntime(origin));
        if (receipt.ResourceKind != "canonical.sqlite" || receipt.ProfileId != origin.Actor.ProfileId || receipt.Receipt is null ||
            receipt.StoreId != identity.StoreId.ToString("D") || !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(
                receipt, origin.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("A SAME current canonical.sqlite Home READ receipt is required.");
        await source.JoinAllAsync().ConfigureAwait(false);
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static bool IsDigest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string OriginKey(CapturedOrigin origin, ResourceStoreIdentity identity) => Hash(JsonSerializer.Serialize(new
        { identity.StoreId, origin.ConversationId, origin.MessageId, origin.Ordinal }));
    private static string RowDigest(SortedDictionary<string, RawValue>? row) => Hash(row is null ? "ABSENT" : JsonSerializer.Serialize(row));
}
