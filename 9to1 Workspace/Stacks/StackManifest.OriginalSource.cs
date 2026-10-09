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
