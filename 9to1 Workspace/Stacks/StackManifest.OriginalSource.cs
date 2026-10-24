using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HavenOS.Apps.Stacks;

public sealed partial class JsonFileStackProjectStore
{
    public const string OriginalFenceRelativePath = ".branches/original-source.write-fence";
    public const string OriginalJournalRelativePath = ".branches/original-source-journal";
    private const int MaximumOriginalMetadataBytes = 128 * 1024 * 1024;
    private const int MaximumOriginalJournalEntries = 4096;
    private readonly SemaphoreSlim _originalSourceGate = new(1, 1);
    private readonly object _originalReceiptsGate = new();
    private sealed class LegacyReceipt(IStackOriginalSourceRevision revision) { internal IStackOriginalSourceRevision Revision = revision; }
    private readonly ConditionalWeakTable<StackManifest, LegacyReceipt> _legacyOriginalReceipts = new();
    private readonly List<object> _heldOriginalFailures = [];
    private readonly List<Task<byte[]>> _originalReads = [];
    private bool _originalPhysicalRetirementUncertain;
    private Guid? _lastCompletedOriginalOperation, _originalJournalProjectId;

    private sealed record RawPair(byte[] Manifest, byte[] Roots)
    {
        internal string ManifestHash => Convert.ToHexString(SHA256.HashData(Manifest));
        internal string RootsHash => Convert.ToHexString(SHA256.HashData(Roots));
        internal bool Matches(RawPair other) => Manifest.AsSpan().SequenceEqual(other.Manifest) && Roots.AsSpan().SequenceEqual(other.Roots);
    }
    private sealed class OriginalRevision(JsonFileStackProjectStore owner, Guid projectId, RawPair raw,
        JsonNode knownManifest, JsonNode knownRoots) : IStackOriginalSourceRevision
    {
        internal JsonFileStackProjectStore Owner { get; } = owner;
        internal Guid ProjectId { get; } = projectId;
        internal RawPair Raw { get; } = raw;
        internal JsonNode KnownManifest { get; } = knownManifest;
        internal JsonNode KnownRoots { get; } = knownRoots;
        internal bool Consumed;
    }
    private sealed record JournalIntent(Guid OperationId, Guid ProjectId, string BeforeManifest,
        string BeforeRoots, string AfterManifest, string AfterRoots, Guid? PreviousOperationId);

    public Task<StackOriginalSourceLoad> LoadOriginalAsync(CancellationToken cancellationToken = default)
        => WithinOriginalFenceAsync(async () =>
        {
            DemandSettledJournals();
            var before = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            var manifest = await LoadValidatedLegacyAsync(cancellationToken).ConfigureAwait(false);
            if (_originalJournalProjectId is { } journalProject && journalProject != manifest.ProjectId) throw OriginalRecovery("The retained source journal belongs to another project.");
            var after = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!before.Matches(after)) throw OriginalConflict("The complete source changed while opening this project.");
            var receipt = IssueOriginal(manifest, after);
            lock (_originalReceiptsGate) _legacyOriginalReceipts.Add(manifest, new(receipt));
            return new StackOriginalSourceLoad(manifest, receipt);
        }, cancellationToken);

    public async Task ValidateOriginalRevisionAsync(IStackOriginalSourceRevision originalRevision, CancellationToken cancellationToken = default)
    {
        if (originalRevision is not OriginalRevision source || !ReferenceEquals(source.Owner, this)) throw OriginalConflict("The original source belongs to another store.");
        _ = await WithinOriginalFenceAsync(async () =>
        {
            DemandSettledJournals();
            var actual = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (source.Consumed || !actual.Matches(source.Raw)) throw OriginalConflict("The complete source changed. Reopen this project before continuing.");
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads only lineage metadata from the canonical manifest. The raw pair is still pinned and
    /// checked under the existing writer fence; resource payloads and unrelated records are
    /// syntactically skipped rather than deserialized into a StackManifest.
    /// </summary>
    public Task<StackLineagePage> ReadLineagePageOriginalAsync(IStackOriginalSourceRevision? originalRevision,
        int pageIndex, int pageSize, Guid? requestedDomainId = null, bool locateRequestedDomain = false,
        CancellationToken cancellationToken = default)
    {
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageIndex > int.MaxValue / pageSize) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (originalRevision is not null && (originalRevision is not OriginalRevision source || !ReferenceEquals(source.Owner, this)))
            return Task.FromException<StackLineagePage>(OriginalConflict("The original source belongs to another store."));
        return WithinOriginalFenceAsync(async () =>
        {
            DemandSettledJournals();
            var before = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (originalRevision is OriginalRevision expected && (expected.Consumed || !before.Matches(expected.Raw)))
                throw OriginalConflict("The complete source changed. Reopen this project before continuing.");
            ValidateJournalProject(before.Manifest);
            StackLineagePage page = ProjectLineagePage(before.Manifest, before.Roots, pageIndex, pageSize,
                requestedDomainId, locateRequestedDomain);
            var after = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!before.Matches(after)) throw OriginalConflict("The complete source changed while reading its lineage page.");
            return page;
        }, cancellationToken);
    }

    /// <summary>Reads the project-list projection without deserializing domain resources.</summary>
    public Task<StackProjectSummary> ReadProjectSummaryOriginalAsync(IStackOriginalSourceRevision? originalRevision,
        CancellationToken cancellationToken = default)
    {
        if (originalRevision is not null && (originalRevision is not OriginalRevision source || !ReferenceEquals(source.Owner, this)))
            return Task.FromException<StackProjectSummary>(OriginalConflict("The original source belongs to another store."));
        return WithinOriginalFenceAsync(async () =>
        {
            DemandSettledJournals();
            var before = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (originalRevision is OriginalRevision expected && (expected.Consumed || !before.Matches(expected.Raw)))
                throw OriginalConflict("The complete source changed. Reopen this project before continuing.");
            ValidateJournalProject(before.Manifest);
            StackProjectSummary summary = ProjectSummary(before.Manifest, before.Roots);
            var after = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!before.Matches(after)) throw OriginalConflict("The complete source changed while reading its project summary.");
            return summary;
        }, cancellationToken);
    }

    private static StackProjectSummary ProjectSummary(byte[] manifestBytes, byte[] rootsBytes)
    {
        try
        {
            StackLineagePage lineage = ProjectLineagePage(manifestBytes, rootsBytes, 0, 1);
            Guid projectId = Guid.Empty, mainId = Guid.Empty;
            Guid? activeId = null;
            string? name = null;
            StackStorageMode? storageMode = null;
            StackAuthorityMode? authorityMode = null;
            long revisionSequence = -1;
            int unresolvedConflicts = 0, activeFreezes = 0, schema = -1;
            var reader = new Utf8JsonReader(manifestBytes, new JsonReaderOptions { MaxDepth = 128 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Manifest root must be an object.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a manifest property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Manifest property has no value.");
                switch (property)
                {
                    case "schemaVersion": schema = reader.GetInt32(); break;
                    case "projectId": projectId = ReadGuid(ref reader, false)!.Value; break;
                    case "mainDomainId": mainId = ReadGuid(ref reader, false)!.Value; break;
                    case "activeDomainId": activeId = ReadGuid(ref reader, true); break;
                    case "name": name = reader.GetString(); break;
                    case "storageMode": storageMode = ReadEnum<StackStorageMode>(ref reader); break;
                    case "authorityMode": authorityMode = ReadEnum<StackAuthorityMode>(ref reader); break;
                    case "revisionSequence": revisionSequence = reader.GetInt64(); break;
                    case "conflicts": unresolvedConflicts = CountUnresolvedConflicts(ref reader); break;
                    case "freezes": activeFreezes = CountActiveFreezes(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            if (reader.Read()) throw new JsonException("Manifest has trailing JSON values.");
            if (schema != StackManifest.CurrentSchemaVersion)
                throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported,
                    "The loaded Stack manifest schema is not supported.", ManifestRelativePath, recoverable: true);
            if (projectId == Guid.Empty || mainId == Guid.Empty || string.IsNullOrWhiteSpace(name) ||
                storageMode is null || authorityMode is null || revisionSequence < 0)
                throw new JsonException("Project summary metadata is incomplete.");
            return new StackProjectSummary(projectId, name, storageMode.Value, authorityMode.Value,
                activeId ?? mainId, lineage.TotalCount, unresolvedConflicts, activeFreezes, revisionSequence);
        }
        catch (StackFailureException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException or KeyNotFoundException)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "The Stack project-summary metadata is invalid. Source bytes have been left untouched.",
                ManifestRelativePath, recoverable: true, innerException: exception);
        }
    }

    private static T ReadEnum<T>(ref Utf8JsonReader reader) where T : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out T parsed) && Enum.IsDefined(parsed)) return parsed;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value) && Enum.IsDefined(typeof(T), value)) return (T)Enum.ToObject(typeof(T), value);
        throw new JsonException($"{typeof(T).Name} value is invalid.");
    }

    private static int CountUnresolvedConflicts(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Manifest conflicts must be an array.");
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A conflict must be an object.");
            bool resolved = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a conflict property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Conflict property has no value.");
                if (property == "isResolved") resolved = reader.GetBoolean(); else reader.Skip();
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Conflict record is incomplete.");
            if (!resolved) checked { count++; }
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Manifest conflicts array is incomplete.");
        return count;
    }

    private static int CountActiveFreezes(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Manifest freezes must be an array.");
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A Freeze must be an object.");
            bool active = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a Freeze property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Freeze property has no value.");
                if (property == "state") active = ReadEnum<StackFreezeState>(ref reader) == StackFreezeState.Active;
                else reader.Skip();
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Freeze record is incomplete.");
            if (active) checked { count++; }
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Manifest freezes array is incomplete.");
        return count;
    }

    private void ValidateJournalProject(byte[] manifestBytes)
    {
        Guid projectId = Guid.Empty;
        var reader = new Utf8JsonReader(manifestBytes, new JsonReaderOptions { MaxDepth = 128 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack manifest root is invalid.", ManifestRelativePath, recoverable: true);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack manifest properties are invalid.", ManifestRelativePath, recoverable: true);
            string property = reader.GetString()!;
            if (!reader.Read()) throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "The Stack manifest property is incomplete.", ManifestRelativePath, recoverable: true);
            if (property == "projectId") { projectId = ReadGuid(ref reader, false)!.Value; break; }
            reader.Skip();
        }
        if (projectId == Guid.Empty) throw new StackFailureException(StackFailureCode.SourceCorrupt,
            "The Stack manifest has no project identity.", ManifestRelativePath, recoverable: true);
        if (_originalJournalProjectId is { } journalProject && journalProject != projectId)
            throw OriginalRecovery("The retained source journal belongs to another project.");
    }

    private sealed record LineageDomain(Guid Id, Guid ProjectId, string Name, StackDomainKind Kind,
        Guid? ParentId, bool IsActive, bool IsDeleted, int WorkingChangeCount, Guid[] ChildIds);

    private static StackLineagePage ProjectLineagePage(byte[] manifestBytes, byte[] rootsBytes, int pageIndex,
        int pageSize, Guid? requestedDomainId, bool locateRequestedDomain)
    {
        Guid projectId = Guid.Empty, mainId = Guid.Empty;
        int schema = -1;
        var domains = new List<LineageDomain>();
        var rootIds = new HashSet<Guid>();
        try
        {
            var reader = new Utf8JsonReader(manifestBytes, new JsonReaderOptions { MaxDepth = 128 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Manifest root must be an object.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a manifest property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Manifest property has no value.");
                switch (property)
                {
                    case "schemaVersion": schema = reader.GetInt32(); break;
                    case "projectId": projectId = ReadGuid(ref reader, allowNull: false)!.Value; break;
                    case "mainDomainId": mainId = ReadGuid(ref reader, allowNull: false)!.Value; break;
                    case "domains": ReadDomains(ref reader, domains); break;
                    case "roots": ReadRootIds(ref reader, rootIds); break;
                    default: reader.Skip(); break;
                }
            }
            if (reader.Read()) throw new JsonException("Manifest has trailing JSON values.");
            if (schema != StackManifest.CurrentSchemaVersion)
                throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported,
                    "The loaded Stack manifest schema is not supported.", ManifestRelativePath, recoverable: true);

            HashSet<Guid> actualRootIds = ParseExternalRootIds(rootsBytes);
            if (!rootIds.SetEquals(actualRootIds))
                throw new StackFailureException(StackFailureCode.ManagedMetadataMissing,
                    "The derived roots.json index does not match canonical Root metadata; project recovery is required.", RootsRelativePath, recoverable: true);
            return BuildLineagePage(projectId, mainId, domains, pageIndex, pageSize, requestedDomainId, locateRequestedDomain);
        }
        catch (StackFailureException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException or KeyNotFoundException)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "The Stack manifest lineage metadata is invalid. Source bytes have been left untouched.", ManifestRelativePath,
                recoverable: true, innerException: exception);
        }
    }

    private static void ReadDomains(ref Utf8JsonReader reader, List<LineageDomain> domains)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Manifest domains must be an array.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A domain record must be an object.");
            Guid id = Guid.Empty, projectId = Guid.Empty;
            string? name = null;
            StackDomainKind? kind = null;
            Guid? parentId = null;
            bool parentSeen = false, active = false, deleted = false, activeSeen = false, deletedSeen = false;
            bool idSeen = false, projectSeen = false, nameSeen = false, kindSeen = false;
            int workingCount = 0;
            var children = new List<Guid>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a domain property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Domain property has no value.");
                switch (property)
                {
                    case "id": id = ReadGuid(ref reader, false)!.Value; idSeen = true; break;
                    case "projectId": projectId = ReadGuid(ref reader, false)!.Value; projectSeen = true; break;
                    case "name": name = reader.GetString(); nameSeen = true; break;
                    case "kind":
                        if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out StackDomainKind parsedKind) && Enum.IsDefined(parsedKind)) kind = parsedKind;
                        else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int numericKind) && Enum.IsDefined(typeof(StackDomainKind), numericKind)) kind = (StackDomainKind)numericKind;
                        else throw new JsonException("Domain kind is invalid.");
                        kindSeen = true;
                        break;
                    case "parentId": parentId = ReadGuid(ref reader, true); parentSeen = true; break;
                    case "isActive": active = reader.GetBoolean(); activeSeen = true; break;
                    case "isDeleted": deleted = reader.GetBoolean(); deletedSeen = true; break;
                    case "childDomainIds": ReadChildIds(ref reader, children); break;
                    case "workingChanges": workingCount = CountChangeEntries(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Domain record is incomplete.");
            if (!idSeen || !projectSeen || !nameSeen || !kindSeen || !parentSeen || !activeSeen || !deletedSeen ||
                id == Guid.Empty || projectId == Guid.Empty || string.IsNullOrWhiteSpace(name))
                throw new JsonException("A domain record is missing required lineage metadata.");
            domains.Add(new LineageDomain(id, projectId, name, kind!.Value, parentId, active, deleted, workingCount, children.ToArray()));
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Manifest domains array is incomplete.");
    }

    private static int CountChangeEntries(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Domain working changes must be an object.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("A working change must have a path key.");
            string path = reader.GetString()!;
            if (!StackPath.Normalize(path).Equals(path, StringComparison.Ordinal) || !keys.Add(path))
                throw new JsonException("Working change keys must be canonical and unique.");
            if (!reader.Read()) throw new JsonException("Working change has no value.");
            reader.Skip();
        }
        if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Working changes object is incomplete.");
        return keys.Count;
    }

    private static void ReadChildIds(ref Utf8JsonReader reader, List<Guid> children)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Child domain IDs must be an array.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            children.Add(ReadGuid(ref reader, false)!.Value);
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Child domain IDs are incomplete.");
    }

    private static void ReadRootIds(ref Utf8JsonReader reader, HashSet<Guid> rootIds)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Manifest Roots must be an array.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A Root record must be an object.");
            Guid id = Guid.Empty;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a Root property.");
                string property = reader.GetString()!;
                if (!reader.Read()) throw new JsonException("Root property has no value.");
                if (property == "id") id = ReadGuid(ref reader, false)!.Value; else reader.Skip();
            }
            if (id == Guid.Empty || !rootIds.Add(id)) throw new JsonException("Root IDs must be nonempty and unique.");
        }
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Manifest Roots array is incomplete.");
    }

    private static HashSet<Guid> ParseExternalRootIds(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 128 });
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != StackManifest.CurrentSchemaVersion)
            throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported,
                "Root metadata uses an unsupported schema version.", RootsRelativePath, recoverable: true);
        var ids = new HashSet<Guid>();
        foreach (JsonElement entry in root.GetProperty("roots").EnumerateArray())
            if (!entry.GetProperty("id").TryGetGuid(out Guid id) || id == Guid.Empty || !ids.Add(id))
                throw new JsonException("Derived Root IDs must be nonempty and unique.");
        return ids;
    }

    private static Guid? ReadGuid(ref Utf8JsonReader reader, bool allowNull)
    {
        if (allowNull && reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String || !Guid.TryParse(reader.GetString(), out Guid id) || id == Guid.Empty)
            throw new JsonException("A Stack identity is invalid.");
        return id;
    }

    private static StackLineagePage BuildLineagePage(Guid projectId, Guid mainId, List<LineageDomain> domains,
        int pageIndex, int pageSize, Guid? requestedDomainId, bool locateRequestedDomain)
    {
        var byId = new Dictionary<Guid, LineageDomain>(domains.Count);
        foreach (LineageDomain domain in domains)
            if (domain.ProjectId != projectId || !byId.TryAdd(domain.Id, domain))
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "The Stack lineage contains a foreign or duplicate DomainID.", domain.Id.ToString("D"), recoverable: true);
        LineageDomain[] mains = domains.Where(domain => domain.Kind == StackDomainKind.Main).ToArray();
        if (projectId == Guid.Empty || mains.Length != 1 || mains[0].Id != mainId || mains[0].ParentId is not null || mains[0].IsDeleted)
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "The Stack lineage has no valid unique Main domain.", ManifestRelativePath, recoverable: true);
        foreach (LineageDomain parent in domains)
        {
            var unique = new HashSet<Guid>();
            foreach (Guid childId in parent.ChildIds)
                if (!unique.Add(childId) || !byId.TryGetValue(childId, out LineageDomain? child) || child.IsDeleted || child.ParentId != parent.Id)
                    throw new StackFailureException(StackFailureCode.SourceCorrupt,
                        "The Stack lineage contains a duplicate, deleted, missing or mismatched child link.", parent.Id.ToString("D"), recoverable: true);
        }
        foreach (LineageDomain domain in domains.Where(domain => domain.Kind != StackDomainKind.Main))
        {
            if (domain.ParentId is not Guid parentId || !byId.TryGetValue(parentId, out LineageDomain? parent) ||
                !StackHierarchy.IsAllowed(parent.Kind, domain.Kind) || domain.IsDeleted == parent.ChildIds.Contains(domain.Id) ||
                (parent.IsDeleted && !domain.IsDeleted))
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "A Stack domain has invalid parent, kind or deleted-state lineage.", domain.Id.ToString("D"), recoverable: true);
        }

        int total = domains.Count(domain => !domain.IsDeleted), index = 0, requestedIndex = -1, resolvedPage = pageIndex;
        int offset = checked(pageIndex * pageSize), targetOffset = offset;
        bool located = false;
        var entries = new List<StackLineageEntry>(Math.Min(pageSize, Math.Max(0, total - offset)));
        var recent = new Queue<StackLineageEntry>(pageSize);
        var visited = new HashSet<Guid>();
        void Visit(LineageDomain domain, int depth)
        {
            if (depth > 3 || !visited.Add(domain.Id))
                throw new StackFailureException(StackFailureCode.SourceCorrupt, "The live Stack lineage is cyclic or exceeds Main/Branch/Twig/Leaf.", domain.Id.ToString("D"), recoverable: true);
            if (requestedDomainId == domain.Id)
            {
                requestedIndex = index;
                if (locateRequestedDomain)
                {
                    resolvedPage = index / pageSize; targetOffset = checked(resolvedPage * pageSize); entries.Clear();
                    int priorIndex = index - recent.Count;
                    foreach (StackLineageEntry prior in recent)
                    { if (priorIndex >= targetOffset) entries.Add(prior); priorIndex++; }
                    located = true;
                }
            }
            var entry = new StackLineageEntry(domain.Id, domain.ProjectId, domain.Name, domain.Kind,
                domain.ParentId, domain.IsActive, domain.WorkingChangeCount, depth);
            if (index >= (locateRequestedDomain && located ? targetOffset : offset) && entries.Count < pageSize) entries.Add(entry);
            if (recent.Count == pageSize) recent.Dequeue();
            recent.Enqueue(entry); index++;
            foreach (LineageDomain child in domain.ChildIds.Select(id => byId[id]).OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase))
                Visit(child, depth + 1);
        }
        Visit(byId[mainId], 0);
        if (index != total) throw new StackFailureException(StackFailureCode.SourceCorrupt,
            "The Stack lineage contains a live domain unreachable from Main.", ManifestRelativePath, recoverable: true);
        return new StackLineagePage(resolvedPage, pageSize, total, requestedDomainId,
            requestedDomainId is null || requestedIndex >= 0, entries.ToArray());
    }

    /// <summary>Pages proposal metadata from the pinned source and hydrates only the requested proposal.</summary>
    public Task<StackConflictProposalPage> ReadConflictProposalsPageOriginalAsync(
        IStackOriginalSourceRevision? originalRevision, Guid domainId, int pageIndex, int pageSize,
        Guid? requestedProposalId = null, bool locateRequestedProposal = false,
        CancellationToken cancellationToken = default)
    {
        if (domainId == Guid.Empty) throw new ArgumentException("A domain identity is required.", nameof(domainId));
        if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (pageSize is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (pageIndex > int.MaxValue / pageSize) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (originalRevision is not null && (originalRevision is not OriginalRevision source || !ReferenceEquals(source.Owner, this)))
            return Task.FromException<StackConflictProposalPage>(OriginalConflict("The original source belongs to another store."));
        return WithinOriginalFenceAsync(async () =>
        {
            DemandSettledJournals();
            var before = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (originalRevision is OriginalRevision expected && (expected.Consumed || !before.Matches(expected.Raw)))
                throw OriginalConflict("The complete source changed. Reopen this project before continuing.");
            ValidateJournalProject(before.Manifest);
            _ = ProjectLineagePage(before.Manifest, before.Roots, 0, 1, locateRequestedDomain: false);
            StackConflictProposalPage page = ProjectConflictProposalPage(before.Manifest, domainId, pageIndex,
                pageSize, requestedProposalId, locateRequestedProposal);
            var after = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!before.Matches(after)) throw OriginalConflict("The complete source changed while reading its conflict proposals.");
            return page;
        }, cancellationToken);
    }

    private sealed record ConflictLineage(Guid Id, Guid DomainId);
    private sealed record ProposalMetadata(Guid Id, Guid ConflictId, string Summary,
        StackConflictProposalState State, DateTimeOffset CreatedAt, string ProposedBy);

    private static StackConflictProposalPage ProjectConflictProposalPage(byte[] manifestBytes, Guid domainId,
        int pageIndex, int pageSize, Guid? requestedProposalId, bool locateRequestedProposal)
    {
        try
        {
            Dictionary<Guid, Guid> conflicts = ReadConflictLineage(manifestBytes);
            var entries = new List<StackConflictProposalSummary>(pageSize);
            var recent = new Queue<StackConflictProposalSummary>(pageSize);
            int total = 0, requestedIndex = -1, resolvedPage = pageIndex;
            int offset = checked(pageIndex * pageSize), targetOffset = offset;
            bool located = false;
            ReadProposalMetadata(manifestBytes, proposal =>
            {
                if (!conflicts.TryGetValue(proposal.ConflictId, out Guid owner) || owner != domainId) return;
                if (requestedProposalId == proposal.Id)
                {
                    requestedIndex = total;
                    if (locateRequestedProposal)
                    {
                        resolvedPage = total / pageSize;
                        targetOffset = checked(resolvedPage * pageSize);
                        entries.Clear();
                        int priorIndex = total - recent.Count;
                        foreach (StackConflictProposalSummary prior in recent)
                        { if (priorIndex >= targetOffset) entries.Add(prior); priorIndex++; }
                        located = true;
                    }
                }
                int activeOffset = locateRequestedProposal && located ? targetOffset : offset;
                var summary = new StackConflictProposalSummary(proposal.Id, proposal.ConflictId, proposal.Summary,
                    proposal.State, proposal.CreatedAt, proposal.ProposedBy);
                if (total >= activeOffset && entries.Count < pageSize) entries.Add(summary);
                if (recent.Count == pageSize) recent.Dequeue();
                recent.Enqueue(summary);
                total++;
            });
            bool requestedFound = requestedProposalId is null || requestedIndex >= 0;
            StackConflictProposal? selected = requestedProposalId is { } id && requestedIndex >= 0
                ? ReadProposalSnapshot(manifestBytes, id) : null;
            if (selected is not null && (!conflicts.TryGetValue(selected.ConflictId, out Guid owner) || owner != domainId))
                throw new StackFailureException(StackFailureCode.SourceCorrupt,
                    "The requested proposal does not belong to the selected domain.", selected.Id.ToString("D"), recoverable: true);
            return new StackConflictProposalPage(resolvedPage, pageSize, total, requestedProposalId,
                requestedFound, selected, entries.ToArray());
        }
        catch (StackFailureException) { throw; }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt,
                "The Stack conflict-proposal metadata is invalid. Source bytes have been left untouched.",
                ManifestRelativePath, recoverable: true, innerException: exception);
        }
    }

    private static Dictionary<Guid, Guid> ReadConflictLineage(byte[] bytes)
    {
        var result = new Dictionary<Guid, Guid>();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Manifest root must be an object.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a manifest property.");
            string property = reader.GetString()!;
            if (!reader.Read()) throw new JsonException("Manifest property has no value.");
            if (property != "conflicts") { reader.Skip(); continue; }
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Manifest conflicts must be an array.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A conflict must be an object.");
                Guid id = Guid.Empty, domainId = Guid.Empty;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a conflict property.");
                    string member = reader.GetString()!;
                    if (!reader.Read()) throw new JsonException("Conflict property has no value.");
                    if (member == "id") id = ReadGuid(ref reader, false)!.Value;
                    else if (member == "domainId") domainId = ReadGuid(ref reader, false)!.Value;
                    else reader.Skip();
                }
                if (reader.TokenType != JsonTokenType.EndObject || id == Guid.Empty || domainId == Guid.Empty || !result.TryAdd(id, domainId))
                    throw new JsonException("Conflict lineage identities must be valid and unique.");
            }
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Manifest conflicts array is incomplete.");
        }
        if (reader.Read()) throw new JsonException("Manifest has trailing JSON values.");
        return result;
    }

    private static void ReadProposalMetadata(byte[] bytes, Action<ProposalMetadata> visit)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Manifest root must be an object.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a manifest property.");
            string property = reader.GetString()!;
            if (!reader.Read()) throw new JsonException("Manifest property has no value.");
            if (property != "conflictProposals") { reader.Skip(); continue; }
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Conflict proposals must be an array.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A conflict proposal must be an object.");
                Guid id = Guid.Empty, conflictId = Guid.Empty;
                string? summary = null, proposedBy = null;
                StackConflictProposalState? state = null;
                DateTimeOffset? createdAt = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a proposal property.");
                    string member = reader.GetString()!;
                    if (!reader.Read()) throw new JsonException("Proposal property has no value.");
                    switch (member)
                    {
                        case "id": id = ReadGuid(ref reader, false)!.Value; break;
                        case "conflictId": conflictId = ReadGuid(ref reader, false)!.Value; break;
                        case "summary": summary = reader.GetString(); break;
                        case "state":
                            if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out StackConflictProposalState parsed) && Enum.IsDefined(parsed)) state = parsed;
                            else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number) && Enum.IsDefined(typeof(StackConflictProposalState), number)) state = (StackConflictProposalState)number;
                            else throw new JsonException("Proposal state is invalid.");
                            break;
                        case "createdAt": createdAt = reader.GetDateTimeOffset(); break;
                        case "proposedBy": proposedBy = reader.GetString(); break;
                        default: reader.Skip(); break;
                    }
                }
                if (reader.TokenType != JsonTokenType.EndObject || id == Guid.Empty || conflictId == Guid.Empty || summary is null ||
                    string.IsNullOrWhiteSpace(proposedBy) || state is null || createdAt is null)
                    throw new JsonException("Proposal summary metadata is incomplete.");
                visit(new ProposalMetadata(id, conflictId, summary, state.Value, createdAt.Value, proposedBy));
            }
            if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Conflict proposal array is incomplete.");
        }
        if (reader.Read()) throw new JsonException("Manifest has trailing JSON values.");
    }

    private static StackConflictProposal ReadProposalSnapshot(byte[] bytes, Guid requestedId)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Manifest root must be an object.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a manifest property.");
            string property = reader.GetString()!;
            if (!reader.Read()) throw new JsonException("Manifest property has no value.");
            if (property != "conflictProposals") { reader.Skip(); continue; }
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Conflict proposals must be an array.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("A conflict proposal must be an object.");
                long start = reader.TokenStartIndex;
                Guid id = Guid.Empty;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a proposal property.");
                    string member = reader.GetString()!;
                    if (!reader.Read()) throw new JsonException("Proposal property has no value.");
                    if (member == "id") id = ReadGuid(ref reader, false)!.Value; else reader.Skip();
                }
                if (reader.TokenType != JsonTokenType.EndObject) throw new JsonException("Proposal object is incomplete.");
                if (id != requestedId) continue;
                int length = checked((int)(reader.BytesConsumed - start));
                return JsonSerializer.Deserialize<StackConflictProposal>(bytes.AsSpan(checked((int)start), length), JsonOptions)
                    ?? throw new JsonException("Requested proposal was empty.");
            }
            break;
        }
        throw new JsonException("Requested conflict proposal disappeared during its pinned read.");
    }

    public Task<IStackOriginalSourceRevision> SaveOriginalAsync(StackManifest manifest,
        IStackOriginalSourceRevision originalRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(originalRevision);
        // Capture the actual proposal before the first asynchronous gate/read. Later caller
        // changes cannot replace the validated project or any nested proposed bytes.
        var proposedBytes = Serialize(manifest);
        var proposed = JsonSerializer.Deserialize<StackManifest>(proposedBytes, JsonOptions)
            ?? throw new JsonException("The captured Stacks proposal is empty.");
        ValidateManifest(proposed);
        var knownManifest = Node(proposedBytes); var knownRoots = Node(SerializeRoots(proposed));
        var proposedProjectId = proposed.ProjectId;
        if (originalRevision is not OriginalRevision source || !ReferenceEquals(source.Owner, this) || source.ProjectId != proposedProjectId)
            return Task.FromException<IStackOriginalSourceRevision>(OriginalConflict("The project source receipt belongs to another store or project."));
        return WithinOriginalFenceAsync<IStackOriginalSourceRevision>(async () =>
        {
            DemandSettledJournals();
            if (source.Consumed) throw OriginalConflict("This original source receipt was already consumed.");
            var actual = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            // This comparison includes all raw metadata, unknown fields, working changes and active selection.
            if (!actual.Matches(source.Raw)) throw OriginalConflict("The project source changed in another session. Reopen it before applying this change.");
            if (JsonNode.DeepEquals(source.KnownManifest, knownManifest) && JsonNode.DeepEquals(source.KnownRoots, knownRoots)) return source;
            var next = new RawPair(Bytes(ApplyKnownDelta(Node(actual.Manifest), source.KnownManifest, knownManifest)),
                Bytes(ApplyKnownDelta(Node(actual.Roots), source.KnownRoots, knownRoots)));
            DemandBound(next.Manifest); DemandBound(next.Roots);
            if (actual.Matches(next)) return source; // Actual complete byte equality under the SAME writer fence.

            var journalRoot = Path.Combine(ProjectDirectory, OriginalJournalRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var existing = Directory.Exists(journalRoot) ? Directory.EnumerateDirectories(journalRoot).Take(MaximumOriginalJournalEntries + 1).Count() : 0;
            if (existing >= MaximumOriginalJournalEntries)
                throw new StackFailureException(StackFailureCode.MaterialisationFailed,
                    "The source journal needs additional storage before more changes can be saved. All accepted versions are preserved.", OriginalJournalRelativePath, recoverable: true);
            DemandOriginalStorageReserve(actual, next);
            var operation = Guid.NewGuid(); var journal = Path.Combine(journalRoot, operation.ToString("N"));
            var intent = new JournalIntent(operation, proposedProjectId, actual.ManifestHash, actual.RootsHash, next.ManifestHash, next.RootsHash, _lastCompletedOriginalOperation);
            // All accepted predecessor bytes are retained before either live metadata file changes.
            Directory.CreateDirectory(journal);
            await WriteOriginalNewAsync(Path.Combine(journal, "manifest.before.json"), actual.Manifest, cancellationToken).ConfigureAwait(false);
            await WriteOriginalNewAsync(Path.Combine(journal, "roots.before.json"), actual.Roots, cancellationToken).ConfigureAwait(false);
            await WriteOriginalNewAsync(Path.Combine(journal, "intent.json"), JsonSerializer.SerializeToUtf8Bytes(intent), cancellationToken).ConfigureAwait(false);
            var stillActual = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!stillActual.Matches(actual)) throw OriginalConflict("The source changed after its predecessor was preserved; publication was refused.");

            // Keep partial outputs/journal on any failure. Never replay a partially accepted publication.
            if (!actual.Roots.AsSpan().SequenceEqual(next.Roots))
                await PublishOriginalFileAsync(journal, "roots.after.json", RootsPath, next.Roots, cancellationToken).ConfigureAwait(false);
            if (!actual.Manifest.AsSpan().SequenceEqual(next.Manifest))
                await PublishOriginalFileAsync(journal, "manifest.after.json", ManifestPath, next.Manifest, cancellationToken).ConfigureAwait(false);
            var observed = await ReadOriginalPairAsync(cancellationToken).ConfigureAwait(false);
            if (!observed.Matches(next)) throw OriginalRecovery("The actual publication differs from its retained output. Source recovery is required.");
            await WriteOriginalNewAsync(Path.Combine(journal, "completed.json"), JsonSerializer.SerializeToUtf8Bytes(intent), cancellationToken).ConfigureAwait(false);
            _lastCompletedOriginalOperation = operation;
            source.Consumed = true;
            return new OriginalRevision(this, proposedProjectId, observed, knownManifest, knownRoots);
        }, cancellationToken);
    }

    private OriginalRevision IssueOriginal(StackManifest manifest, RawPair actual)
        => new(this, manifest.ProjectId, actual, Node(Serialize(manifest)), Node(SerializeRoots(manifest)));
    private static byte[] SerializeRoots(StackManifest manifest) => JsonSerializer.SerializeToUtf8Bytes(new StackRootsDocument
    { SchemaVersion = StackManifest.CurrentSchemaVersion, Roots = manifest.Roots.ToList() }, JsonOptions);
    private static JsonNode Node(byte[] bytes) => JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 128 })
        ?? throw new JsonException("Original metadata is empty.");
    private static byte[] Bytes(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node, JsonOptions);
    private static void DemandBound(byte[] bytes)
    {
        if (bytes.Length > MaximumOriginalMetadataBytes)
            throw new StackFailureException(StackFailureCode.MaterialisationFailed, "The source metadata exceeds the available bounded writer capacity.", ManifestRelativePath, recoverable: true);
    }
    private async Task<RawPair> ReadOriginalPairAsync(CancellationToken token)
    {
        ValidateManagedLayout();
        return new(await ReadOriginalMetadataAsync(ManifestPath, token).ConfigureAwait(false),
            await ReadOriginalMetadataAsync(RootsPath, token).ConfigureAwait(false));
    }
    private async Task<byte[]> ReadOriginalMetadataAsync(string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > MaximumOriginalMetadataBytes)
            throw new StackFailureException(StackFailureCode.MaterialisationFailed, "The source metadata exceeds the bounded reader capacity.", path, recoverable: true);
        var original = File.ReadAllBytesAsync(path, token);
        _originalReads.Add(original); // Retain the SAME raw file Task before any await.
        byte[] bytes;
        try { bytes = await original.ConfigureAwait(false); }
        catch (Exception observed)
        {
            var cause = original.Exception ?? observed;
            _heldOriginalFailures.Add((path, original, cause));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
        // Only this owner's actual successful observation permits healthy pruning.
        _originalReads.Remove(original);
        DemandBound(bytes); return bytes;
    }
    private void DemandSettledJournals()
    {
        var root = Path.Combine(ProjectDirectory, OriginalJournalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root)) { _lastCompletedOriginalOperation = null; _originalJournalProjectId = null; return; }
        var receipts = new Dictionary<Guid, JournalIntent>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (receipts.Count >= MaximumOriginalJournalEntries) throw OriginalRecovery("The source journal inventory requires recovery review.");
            var intent = Path.Combine(directory, "intent.json"); var completed = Path.Combine(directory, "completed.json");
            if (!File.Exists(intent) || !File.Exists(completed))
                throw OriginalRecovery("An original source publication did not settle. Its predecessor and journal are retained; reopen through recovery before writing.");
            var original = ReadBoundedJournal(intent); var terminal = ReadBoundedJournal(completed);
            if (!original.AsSpan().SequenceEqual(terminal)) throw OriginalRecovery("The original journal terminal does not match its source intent.");
            var receipt = JsonSerializer.Deserialize<JournalIntent>(original) ?? throw OriginalRecovery("The original source journal is unreadable.");
            if (receipt.OperationId == Guid.Empty || receipt.ProjectId == Guid.Empty ||
                !string.Equals(Path.GetFileName(directory), receipt.OperationId.ToString("N"), StringComparison.Ordinal) || !receipts.TryAdd(receipt.OperationId, receipt))
                throw OriginalRecovery("The source journal has an invalid original operation identity.");
            VerifyJournalBytes(Path.Combine(directory, "manifest.before.json"), receipt.BeforeManifest);
            VerifyJournalBytes(Path.Combine(directory, "roots.before.json"), receipt.BeforeRoots);
            if (!string.Equals(receipt.BeforeManifest, receipt.AfterManifest, StringComparison.Ordinal)) VerifyJournalBytes(Path.Combine(directory, "manifest.after.json"), receipt.AfterManifest);
            if (!string.Equals(receipt.BeforeRoots, receipt.AfterRoots, StringComparison.Ordinal)) VerifyJournalBytes(Path.Combine(directory, "roots.after.json"), receipt.AfterRoots);
        }
        if (receipts.Count == 0) { _lastCompletedOriginalOperation = null; _originalJournalProjectId = null; return; }
        var genesis = receipts.Values.Where(r => r.PreviousOperationId is null).ToArray();
        if (genesis.Length != 1) throw OriginalRecovery("The original source journal has an ambiguous starting state.");
        var seen = new HashSet<Guid>(); var current = genesis[0];
        while (true)
        {
            if (!seen.Add(current.OperationId)) throw OriginalRecovery("The original source journal ordering is cyclic.");
            var next = receipts.Values.Where(r => r.PreviousOperationId == current.OperationId).ToArray();
            if (next.Length == 0) break;
            if (next.Length != 1 || next[0].ProjectId != current.ProjectId ||
                !string.Equals(next[0].BeforeManifest, current.AfterManifest, StringComparison.Ordinal) || !string.Equals(next[0].BeforeRoots, current.AfterRoots, StringComparison.Ordinal))
                throw OriginalRecovery("The original source journal ordering does not preserve its actual predecessor.");
            current = next[0];
        }
        if (seen.Count != receipts.Count) throw OriginalRecovery("An original source journal branch is disconnected.");
        // A completed marker cannot manufacture a lost rename/durable source acknowledgement.
        // Observe the actual latest pair; never restore/replay it from the retained output.
        VerifyJournalBytes(ManifestPath, current.AfterManifest); VerifyJournalBytes(RootsPath, current.AfterRoots);
        _lastCompletedOriginalOperation = current.OperationId; _originalJournalProjectId = current.ProjectId;
    }
    private static byte[] ReadBoundedJournal(string path)
    {
        if (new FileInfo(path).Length > MaximumOriginalMetadataBytes) throw OriginalRecovery("The original journal exceeds its bounded reader capacity.");
        var bytes = File.ReadAllBytes(path); DemandBound(bytes); return bytes;
    }
    private static void VerifyJournalBytes(string path, string expectedHash)
    {
        var bytes = ReadBoundedJournal(path);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.Ordinal))
            throw OriginalRecovery("The actual source or retained original journal bytes differ from their observed publication.");
    }
    private void DemandOriginalStorageReserve(RawPair before, RawPair after)
    {
        var bytes = checked((long)before.Manifest.Length + before.Roots.Length + after.Manifest.Length + after.Roots.Length + 64 * 1024);
        var required = Math.Max(64L * 1024 * 1024, checked(bytes * 8));
        var available = new DriveInfo(Path.GetPathRoot(ProjectDirectory)!).AvailableFreeSpace;
        if (available < required) throw new StackFailureException(StackFailureCode.MaterialisationFailed,
            "Additional storage is required before saving more source changes. Accepted source and journals are preserved.", OriginalJournalRelativePath, recoverable: true);
    }
    private async Task WriteOriginalNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        FileStream? stream = null; Task? write = null, flush = null, close = null; var failures = new List<Exception>();
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
            write = stream.WriteAsync(bytes, token).AsTask(); await write.ConfigureAwait(false);
            flush = stream.FlushAsync(token); await flush.ConfigureAwait(false); stream.Flush(true);
        }
        catch (Exception failure)
        {
            var actual = flush?.IsFaulted == true ? flush.Exception : write?.IsFaulted == true ? write.Exception : null;
            failures.Add(actual ?? failure);
        }
        finally
        {
            if (stream is not null)
                try { close = stream.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
                catch (Exception failure) { failures.Add(close?.Exception ?? failure); }
        }
        if (failures.Count != 0) _heldOriginalFailures.Add((stream, write, flush, close, bytes, failures));
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Stacks retains original file publication and physical close failures.", failures);
    }
    private async Task PublishOriginalFileAsync(string journal, string leaf, string destination, byte[] bytes, CancellationToken token)
    {
        var retainedOutput = Path.Combine(journal, leaf);
        await WriteOriginalNewAsync(retainedOutput, bytes, token).ConfigureAwait(false);
        var publication = Path.Combine(journal, leaf + ".publish");
        await WriteOriginalNewAsync(publication, bytes, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); File.Move(publication, destination, overwrite: true);
    }
    private async Task<T> WithinOriginalFenceAsync<T>(Func<Task<T>> body, CancellationToken token)
    {
        await _originalSourceGate.WaitAsync(token).ConfigureAwait(false);
        FileStream? fence = null; Task? close = null; Task<T>? originalBody = null; var failures = new List<Exception>(); T result = default!;
        try
        {
            if (_originalPhysicalRetirementUncertain) throw OriginalRecovery("An original writer fence did not retire. Retain this store and its source for recovery.");
            ValidateManagedLayout();
            try
            {
                fence = new FileStream(Path.Combine(ProjectDirectory, OriginalFenceRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
            }
            catch (IOException original) { throw new StackFailureException(StackFailureCode.RevisionConflict,
                "Another original Stacks writer holds this project. Existing source is preserved.", OriginalFenceRelativePath, recoverable: true, retryable: true, innerException: original); }
            originalBody = body() ?? throw new InvalidOperationException("The actual store body did not issue its original Task.");
            result = await originalBody.ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var envelope = originalBody?.Exception;
            // Preserve the actual body Task/envelope independently. Only its known single
            // wrapper around the SAME observed object may keep the existing typed API failure.
            failures.Add(envelope is { InnerExceptions.Count: 1 } && ReferenceEquals(envelope.InnerExceptions[0], failure)
                ? failure : envelope ?? failure);
            _heldOriginalFailures.Add((body, originalBody, envelope, failure, result));
        }
        finally
        {
            if (fence is not null)
            {
                try { close = fence.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
                catch (Exception failure)
                {
                    _originalPhysicalRetirementUncertain = true; failures.Add(close?.Exception ?? failure);
                    _heldOriginalFailures.Add((fence, close, failure, result));
                }
            }
            _originalSourceGate.Release();
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Stacks retains original writer and physical fence failures.", failures);
        return result;
    }
    private static StackFailureException OriginalConflict(string message) => new(StackFailureCode.RevisionConflict, message, ManifestRelativePath, recoverable: true, retryable: true);
    private static StackFailureException OriginalRecovery(string message) => new(StackFailureCode.RecoveryStateUncertain, message, OriginalJournalRelativePath, recoverable: true);

    /// <summary>Apply the existing typed model's actual delta to the original JSON; keep unrelated unknown data.</summary>
    private static JsonNode ApplyKnownDelta(JsonNode original, JsonNode before, JsonNode after)
    {
        if (JsonNode.DeepEquals(before, after)) return original.DeepClone();
        if (original is JsonObject actualObject && before is JsonObject oldObject && after is JsonObject nextObject)
        {
            var merged = (JsonObject)actualObject.DeepClone();
            foreach (var key in oldObject.Select(pair => pair.Key).Except(nextObject.Select(pair => pair.Key), StringComparer.Ordinal)) merged.Remove(key);
            foreach (var property in nextObject)
            {
                var old = oldObject[property.Key]; var actual = actualObject[property.Key]; var next = property.Value;
                merged[property.Key] = old is not null && actual is not null && next is not null
                    ? ApplyKnownDelta(actual, old, next) : next?.DeepClone();
            }
            // The maintained commit operation transfers SAME path mutation from WorkingChanges
            // to LocalChanges. Carry its original unknown payload only when that exact known
            // mutation moved unchanged; a matching display path alone is insufficient.
            if (actualObject["workingChanges"] is JsonObject actualWorking && oldObject["workingChanges"] is JsonObject oldWorking &&
                nextObject["workingChanges"] is JsonObject nextWorking && nextObject["localChanges"] is JsonObject nextLocal && merged["localChanges"] is JsonObject mergedLocal)
                foreach (var mutation in oldWorking)
                    if (!nextWorking.ContainsKey(mutation.Key) && mutation.Value is not null &&
                        nextLocal[mutation.Key] is { } moved && actualWorking[mutation.Key] is { } actualMutation && JsonNode.DeepEquals(mutation.Value, moved))
                        mergedLocal[mutation.Key] = ApplyKnownDelta(actualMutation, mutation.Value, moved);
            return merged;
        }
        if (original is JsonArray actualArray && before is JsonArray oldArray && after is JsonArray nextArray)
        {
            static string? Id(JsonNode? item)
            {
                if (item is not JsonObject obj || obj["id"] is not JsonValue id || !id.TryGetValue<string>(out var value)) return null;
                return Guid.TryParse(value, out var identity) ? identity.ToString("D") : value;
            }
            if (oldArray.All(item => Id(item) is not null) && actualArray.All(item => Id(item) is not null) && nextArray.All(item => Id(item) is not null))
            {
                var oldById = oldArray.ToDictionary(item => Id(item)!, StringComparer.Ordinal);
                var actualById = actualArray.ToDictionary(item => Id(item)!, StringComparer.Ordinal);
                return new JsonArray(nextArray.Select(item =>
                {
                    var id = Id(item)!;
                    return oldById.TryGetValue(id, out var old) && actualById.TryGetValue(id, out var actual) && old is not null && actual is not null
                        ? ApplyKnownDelta(actual, old, item!) : item?.DeepClone();
                }).ToArray());
            }
        }
        return after.DeepClone();
    }
}
