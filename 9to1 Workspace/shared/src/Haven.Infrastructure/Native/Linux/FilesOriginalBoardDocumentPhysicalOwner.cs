using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

/// <summary>Original Files-owned immutable board bytes. Paths come exclusively
/// from the bound Files issuer's registered anchor; no path is inferred from an ID.
/// This owner performs no metadata mutation or Home approval. Every physical effect
/// has the issuer's raw current Files/Home claim fence. Old bytes are never touched.</summary>
public sealed class FilesOriginalBoardDocumentPhysicalOwner : IFilesOriginalBoardDocumentPhysicalOwner, IAsyncDisposable
{
    private readonly object _gate = new();
    private IFilesOriginalBoardDocumentWriteSource? _issuer;
    private readonly ConditionalWeakTable<IFilesOriginalBoardDocumentWriteDestination, Pin> _issued = new();
    private readonly List<Pin> _pins = [];
    private readonly List<Exception> _unexpected = [];
    private readonly AsyncLocal<Pin?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesOriginalBoardDocumentPhysicalOwner, int>? _rawCommitOwners;
    private bool _retiring;
    private Task? _close;
    public IFilesOriginalBoardDocumentWriteSource? OriginalBoardDocumentWriteSource { get { lock (_gate) return _issuer; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    private sealed class Held(IDisposable actual) : IAsyncDisposable
    {
        private Task? _close;
        public ValueTask DisposeAsync()
        {
            if (_close is not null) return new(_close);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = completion.Task;
            try { actual.Dispose(); completion.SetResult(); } catch (Exception cause) { completion.SetException(cause); }
            return new(_close);
        }
    }
    private sealed class Check(CanonicalSqliteOriginalSourceScope source)
    { internal readonly CanonicalSqliteOriginalSourceScope Source = source; internal Task Driver = null!; }
    private sealed class Pin(FilesOriginalBoardDocumentPhysicalOwner owner,
        IFilesOriginalBoardDocumentWriteDestination destination) : IFilesOriginalBoardDocumentMaterialization
    {
        internal readonly FilesOriginalBoardDocumentPhysicalOwner Owner = owner;
        public IFilesOriginalBoardDocumentWriteDestination OriginalDestination { get; } = destination;
        internal FilesOriginalBoardDocumentWriteDescription Description = null!;
        internal FilesOriginalBoardDocumentMaterializationRecord Record = null!;
        public FilesOriginalBoardDocumentMaterializationRecord OriginalRecord => Record;
        public string RelativeContentReference => Record.RelativeContentReference;
        public long SizeBytes => Record.SizeBytes;
        public string Sha256 => Record.Sha256;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
        internal Task<IFilesOriginalBoardDocumentMaterialization> Driver = null!;
        internal readonly List<Check> Checks = [];
        internal LinuxOriginalDownloadRoot? Root;
        internal SafeFileHandle? Parent, File;
        internal LinuxOriginalDownloadIdentity ParentIdentity, PublishedIdentity;
        internal string Directory = "", StageLeaf = "";
        internal uint Uid;
        internal byte[]? Payload;
        internal long ExpectedBytes;
        internal string ExpectedSha = "";
        internal bool Published, Ready, Retiring;
        internal Task? Close;
        public Task? OriginalClose { get { lock (Owner._gate) return Close; } }
        public void RequestRetirement() { lock (Owner._gate) Retiring = true; }
        public void DemandExternalOriginalRetirementJoin() => Owner.DemandExternalJoin(this);
        public Task CloseAndDrainAsync() => Owner.ClosePin(this);
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }

    public void BindOriginalBoardDocumentWriteSource(IFilesOriginalBoardDocumentWriteSource sameSource)
    {
        ArgumentNullException.ThrowIfNull(sameSource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_issuer is not null && !ReferenceEquals(_issuer, sameSource))
                throw new UnauthorizedAccessException("The original Files board-write issuer cannot be replaced.");
            _issuer = sameSource;
        }
    }

    public Task<IFilesOriginalBoardDocumentMaterialization> MaterializeOriginalBoardDocumentWithinSourceAsync(
        IFilesOriginalBoardDocumentWriteDestination originalDestination, ReadOnlyMemory<byte> originalValidatedPayload,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalDestination); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        cancellationToken.ThrowIfCancellationRequested();
        if (originalValidatedPayload.Length is < 1 or > HavenBoardDocumentCodec.MaximumDocumentBytes)
            throw new InvalidDataException("The canonical board payload is empty or exceeds the maintained document bound.");
        var bytes = originalValidatedPayload.ToArray(); // detach before any callback/await
        var document = HavenBoardDocumentCodec.Deserialize(bytes);
        using (var header = JsonDocument.Parse(bytes))
            if (document.SchemaVersion != HavenBoardDocument.CurrentSchemaVersion ||
                header.RootElement.EnumerateObject().Count(property => property.Name == "schemaVersion") != 1 ||
                header.RootElement.GetProperty("schemaVersion").GetInt32() != HavenBoardDocument.CurrentSchemaVersion)
                throw new InvalidDataException("Save the current canonical board schema; older payloads must be migrated by Boards first.");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Pin pin; TaskCompletionSource start;
        lock (_gate)
        {
            DemandNewAdmission();
            if (_issued.TryGetValue(originalDestination, out var known))
            {
                if (known.Retiring || known.ExpectedBytes != bytes.Length || known.ExpectedSha != sha)
                    throw new UnauthorizedAccessException("The original one-use board destination cannot adopt different or retired payload bytes.");
                return known.Driver;
            }
            _pins.RemoveAll(value => value.Close?.IsCompletedSuccessfully == true);
            if (_pins.Count >= 16) throw new InvalidOperationException("Drain retained board byte operations before another materialization.");
            pin = new(this, originalDestination) { Payload = bytes, ExpectedBytes = bytes.Length, ExpectedSha = sha };
            pin.Source = NewSource(scope, retain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pin.Driver = Materialize(pin, start.Task, document.DocumentId, sha, cancellationToken);
            _pins.Add(pin); _issued.Add(originalDestination, pin);
        }
        try { pin.Source.Run(() => retain(pin.Driver)); }
        finally { start.SetResult(); }
        return pin.Driver;
    }

    private async Task<IFilesOriginalBoardDocumentMaterialization> Materialize(Pin pin, Task start, Guid documentId,
        string sha, CancellationToken token)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = pin;
        try
        {
            var source = pin.Source;
            source.RunProductive(() =>
            {
                token.ThrowIfCancellationRequested();
                var issuer = _issuer ?? throw new UnauthorizedAccessException("Bind the actual Files board-write issuer first.");
                if (!issuer.IsIssuedOriginalBoardDocumentWriteDestination(pin.OriginalDestination))
                    throw new UnauthorizedAccessException("The actual Files owner did not issue this board destination.");
                pin.Description = issuer.GetOriginalBoardDocumentWriteDescription(pin.OriginalDestination) with { };
                var description = pin.Description;
                if (description.FilesStoreId == Guid.Empty || description.ParentFolderId == Guid.Empty || description.FileId == Guid.Empty ||
                    description.ExpectedRevisionId == Guid.Empty || description.NewRevisionId == Guid.Empty || description.DocumentId != documentId ||
                    description.NewRevisionId == description.ExpectedRevisionId || description.PayloadBytes != pin.Payload!.Length ||
                    !sha.Equals(description.PayloadSha256, StringComparison.OrdinalIgnoreCase) ||
                    description.RelativeContentReference != ".9to1-board-" + description.NewRevisionId.ToString("N") + ".content" ||
                    !Path.IsPathFullyQualified(description.DirectoryPath) || description.OriginalActor is null ||
                    !Text(description.OriginalActor.ActorId) || !Text(description.OriginalActor.ProfileId) || !Text(description.OriginalActor.AuthenticationRevision))
                    throw new UnauthorizedAccessException("The exact original file/parent/revision/actor/board payload destination is required.");
                pin.Record = new(description.FilesStoreId, description.ParentFolderId, description.FileId, description.ExpectedRevisionId,
                    description.NewRevisionId, description.DocumentId, description.RelativeContentReference, description.PayloadBytes, sha, description.OriginalActor);
                pin.Directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(description.DirectoryPath));
                pin.Uid = LinuxOriginalDownloadPaths.CurrentUid();
                pin.Root = LinuxOriginalDownloadPaths.RetainRoot(pin.Directory);
                source.CaptureOriginalResource(new Held(pin.Root));
                pin.Parent = pin.Root.OpenDirectory(pin.Directory, mutable: true);
                source.CaptureOriginalResource(new Held(pin.Parent));
                pin.ParentIdentity = LinuxOriginalDownloadPaths.ReadIdentity(pin.Parent);
                DemandPhysical(pin);
                pin.StageLeaf = ".9to1-board-stage-" + description.NewRevisionId.ToString("N") + ".tmp";
                pin.File = LinuxOriginalDownloadPaths.CreateStage(pin.Parent, pin.StageLeaf);
                source.CaptureOriginalResource(new Held(pin.File));
                LinuxOriginalDownloadPaths.DemandPrivateStage(pin.File, pin.Uid);
                LinuxOriginalDownloadPaths.DemandPath(pin.File, Path.Combine(pin.Directory, pin.StageLeaf), directory: false);
            });
            var offset = 0;
            while (offset < pin.Payload!.Length)
            {
                source.RunProductive(() => { token.ThrowIfCancellationRequested(); DemandPhysical(pin); });
                var count = Math.Min(64 * 1024, pin.Payload.Length - offset);
                await source.Read(() => RandomAccess.WriteAsync(pin.File!, pin.Payload.AsMemory(offset, count), offset, token).AsTask()).ConfigureAwait(false);
                offset += count;
            }
            source.RunProductive(() => { token.ThrowIfCancellationRequested(); DemandPhysical(pin); LinuxOriginalDownloadPaths.Flush(pin.File!); });
            await VerifyBytes(pin, source, published: false, token).ConfigureAwait(false);
            source.RunProductive(() =>
            {
                token.ThrowIfCancellationRequested(); DemandPhysical(pin);
                var identity = LinuxOriginalDownloadPaths.ReadIdentity(pin.File!);
                pin.PublishedIdentity = LinuxOriginalDownloadPaths.PublishCreateOnly(pin.File!, pin.Parent!, pin.Directory,
                    pin.StageLeaf, pin.Record.RelativeContentReference, identity);
                pin.Published = true;
                LinuxOriginalDownloadPaths.Flush(pin.Parent!); DemandPublished(pin); DemandPhysical(pin);
            });
            await VerifyBytes(pin, source, published: true, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            pin.Ready = true;
            return pin;
        }
        finally { pin.Payload = null; _executing.Value = previous; }
    }

    public bool IsIssuedOriginalBoardDocumentMaterialization(IFilesOriginalBoardDocumentMaterialization originalMaterialization)
        => originalMaterialization is Pin pin && ReferenceEquals(pin.Owner, this) &&
            _issued.TryGetValue(pin.OriginalDestination, out var actual) && ReferenceEquals(actual, pin);

    public void DemandOriginalBoardDocumentCommit(IFilesOriginalBoardDocumentMaterialization originalMaterialization)
    {
        var pin = Require(originalMaterialization);
        lock (_gate)
        {
            DemandNewAdmission();
            if (pin.Retiring || !pin.Ready || !pin.Driver.IsCompletedSuccessfully)
                throw new UnauthorizedAccessException("The original completed physical board receipt retired or is unavailable.");
        }
        var owners = _rawCommitOwners ??= new(ReferenceEqualityComparer.Instance);
        owners.TryGetValue(this, out var depth); owners[this] = depth + 1;
        try { DemandPhysical(pin); DemandPublished(pin); }
        finally { if (depth == 0) owners.Remove(this); else owners[this] = depth; }
    }

    public Task RevalidateOriginalBoardDocumentWithinSourceAsync(IFilesOriginalBoardDocumentMaterialization originalMaterialization,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var pin = Require(originalMaterialization); Check check; TaskCompletionSource start;
        lock (_gate)
        {
            DemandNewAdmission();
            if (pin.Retiring || !pin.Ready || !pin.Driver.IsCompletedSuccessfully)
                throw new UnauthorizedAccessException("Revalidate the same live completed board materialization.");
            pin.Checks.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Source.IsHealthySettled);
            if (pin.Checks.Count >= 128) throw new InvalidOperationException("Retain and drain unresolved original board byte checks.");
            check = new(NewSource(scope, retain)); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            check.Driver = Revalidate(start.Task); pin.Checks.Add(check);
        }
        try { check.Source.Run(() => retain(check.Driver)); }
        finally { start.SetResult(); }
        return check.Driver;
        async Task Revalidate(Task begin)
        {
            await begin.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = pin;
            try { await VerifyBytes(pin, check.Source, published: true, cancellationToken).ConfigureAwait(false); await check.Source.JoinAllAsync().ConfigureAwait(false); }
            finally { _executing.Value = previous; }
        }
    }

    private async Task VerifyBytes(Pin pin, CanonicalSqliteOriginalSourceScope source, bool published, CancellationToken token)
    {
        LinuxOriginalDownloadIdentity before = default;
        source.RunProductive(() =>
        {
            token.ThrowIfCancellationRequested(); DemandPhysical(pin);
            if (published) DemandPublished(pin);
            else LinuxOriginalDownloadPaths.DemandPath(pin.File!, Path.Combine(pin.Directory, pin.StageLeaf), false);
            LinuxOriginalDownloadPaths.DemandPrivateStage(pin.File!, pin.Uid);
            before = LinuxOriginalDownloadPaths.ReadIdentity(pin.File!);
            if (before.Size != (ulong)pin.Record.SizeBytes) throw new IOException("The actual staged board byte length changed.");
        });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long offset = 0;
        while (offset < pin.Record.SizeBytes)
        {
            source.RunProductive(() => { token.ThrowIfCancellationRequested(); DemandPhysical(pin); });
            var count = await source.Read(() => RandomAccess.ReadAsync(pin.File!, buffer.AsMemory(0,
                (int)Math.Min(buffer.Length, pin.Record.SizeBytes - offset)), offset, token).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("The original board bytes were truncated.");
            hash.AppendData(buffer, 0, count); offset += count;
        }
        var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        source.RunProductive(() =>
        {
            token.ThrowIfCancellationRequested(); DemandPhysical(pin);
            if (!before.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(pin.File!)) || sha != pin.Record.Sha256)
                throw new IOException("The held immutable board bytes changed or failed integrity.");
            if (published) DemandPublished(pin);
        });
    }

    private void DemandPhysical(Pin pin)
    {
        DemandHealthy();
        LinuxOriginalDownloadPaths.DemandCurrentUid(pin.Uid); pin.Root!.DemandCurrent();
        LinuxOriginalDownloadPaths.DemandPath(pin.Parent!, pin.Directory, directory: true);
        LinuxOriginalDownloadPaths.DemandOwner(pin.Parent!, pin.Uid);
        if (!pin.ParentIdentity.SamePermissions(LinuxOriginalDownloadPaths.ReadIdentity(pin.Parent!)))
            throw new UnauthorizedAccessException("The original registered Files anchor changed identity or permissions.");
        var issuer = _issuer ?? throw new UnauthorizedAccessException("The original Files issuer is unavailable.");
        if (!issuer.IsIssuedOriginalBoardDocumentWriteDestination(pin.OriginalDestination) ||
            issuer.GetOriginalBoardDocumentWriteDescription(pin.OriginalDestination) != pin.Description)
            throw new UnauthorizedAccessException("The original Files board destination, actor, parent or revision changed.");
        issuer.DemandOriginalFilesBoardDocumentCommit(pin.OriginalDestination);
    }
    private static void DemandPublished(Pin pin)
    {
        if (!pin.Published || pin.File is null || !pin.PublishedIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(pin.File)))
            throw new IOException("The original published board inode/read version changed.");
        LinuxOriginalDownloadPaths.DemandPrivateStage(pin.File, pin.Uid);
        LinuxOriginalDownloadPaths.DemandPath(pin.File, Path.Combine(pin.Directory, pin.Record.RelativeContentReference), false);
    }
    private CanonicalSqliteOriginalSourceScope NewSource(Action<Action> scope, Action<Task> retain)
        => new(this, scope, retain, RememberUnexpected, DemandHealthy);
    private void RememberUnexpected(Exception cause) { lock (_gate) if (!_unexpected.Contains(cause)) _unexpected.Add(cause); }
    private void DemandHealthy() { lock (_gate) if (_unexpected.Count != 0) throw new AggregateException("Original board source callbacks failed and remain retained.", _unexpected); }
    private void DemandNewAdmission() { ObjectDisposedException.ThrowIf(_retiring, this); DemandHealthy(); }
    private Pin Require(IFilesOriginalBoardDocumentMaterialization actual) => IsIssuedOriginalBoardDocumentMaterialization(actual)
        ? (Pin)actual : throw new UnauthorizedAccessException("The SAME privately issued physical board materialization is required.");
    private static bool Text(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096 && !value.Any(char.IsControl);

    private void DemandExternalJoin(Pin? pin)
    {
        if (_executing.Value is not null || CanonicalSqliteOriginalSourceScope.IsPhysicalSource(this) || _rawCommitOwners?.ContainsKey(this) == true)
            throw new InvalidOperationException("The original board byte operation/callback cannot join its own close.");
    }
    private Task ClosePin(Pin pin)
    {
        DemandExternalJoin(pin); TaskCompletionSource? start = null; Task original;
        lock (_gate)
        {
            pin.Retiring = true;
            if (pin.Close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); pin.Close = DrainPin(pin, start.Task); }
            original = pin.Close;
        }
        start?.SetResult(); return original;
    }
    private async Task DrainPin(Pin pin, Task start)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = pin;
        try
        {
            Check[] checks; lock (_gate) checks = pin.Checks.ToArray();
            var errors = new List<Exception>();
            foreach (var original in new[] { pin.Driver }.Cast<Task>().Concat(checks.Select(value => value.Driver)))
                try { await original.ConfigureAwait(false); } catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, original, cause); }
            foreach (var source in checks.Select(value => value.Source).Append(pin.Source))
            {
                try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
            // Published and unpublished bytes remain for Files-owned reference-aware
            // recovery/GC. In particular, no close guesses whether a metadata CAS ran.
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
        }
        finally { _executing.Value = previous; }
    }
    public void RequestRetirement() { lock (_gate) { _retiring = true; foreach (var pin in _pins) pin.Retiring = true; } }
    public void DemandExternalOriginalRetirementJoin() => DemandExternalJoin(null);
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); TaskCompletionSource? start = null; Task original;
        lock (_gate)
        {
            RequestRetirement();
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task); }
            original = _close;
        }
        start?.SetResult(); return original;
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); Pin[] pins; lock (_gate) pins = _pins.ToArray();
        var errors = new List<Exception>();
        foreach (var pin in pins) try { await ClosePin(pin).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        lock (_gate) errors.AddRange(_unexpected);
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
