using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class CanonicalMiniComputerCatalogOriginalOwner : ICanonicalMiniComputerCatalogIdentitySource,
    ICanonicalOriginalWriteSettlementPinOwner<ICanonicalMiniComputerCatalogIdentityIntent, ICanonicalMiniComputerCatalogIdentityAcknowledgment>
{
    private ICanonicalMiniComputerCatalogIdentityHomeSource? _identityHome;
    private readonly ConditionalWeakTable<ICanonicalMiniComputerCatalogIdentityIntent, IdentityIntent> _identityIntents = new();
    private readonly ConditionalWeakTable<IdentityIntent, IdentityWrite> _identityWrites = new();
    private readonly List<IdentityWrite> _identityActive = [];
    private readonly AsyncLocal<IdentityWrite?> _activeIdentityWrite = new();
    private bool CanRetireOriginalIdentityLease(Lease sameLease)
    {
        lock (_gate) return !_identityActive.Any(write => ReferenceEquals(write.Pin, sameLease) && !write.PinsReleased);
    }
    public void BindOriginalIdentitySetupHomeSource(ICanonicalMiniComputerCatalogIdentityHomeSource sameHome)
    {
        ArgumentNullException.ThrowIfNull(sameHome);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_identityHome is not null && !ReferenceEquals(_identityHome, sameHome))
                throw new InvalidOperationException("The original catalogue identity WRITE source is already bound.");
            _identityHome = sameHome;
        }
    }
    public bool HasOriginalIdentitySetupHomeSource(ICanonicalMiniComputerCatalogIdentityHomeSource sameHome)
    { lock (_gate) return ReferenceEquals(_identityHome, sameHome); }
    private sealed class IdentityIntent(CanonicalMiniComputerCatalogOriginalOwner owner, AuthenticatedResourceActor actor,
        Guid operation, NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp stamp, byte[] original)
        : ICanonicalMiniComputerCatalogIdentityIntent
    {
        internal readonly CanonicalMiniComputerCatalogOriginalOwner Owner = owner;
        internal readonly NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp Stamp = stamp;
        internal readonly byte[] Bytes = original;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public Guid OperationId { get; } = operation;
        public string CatalogueName => "Configured Mini Computer catalogue";
        public string OriginalCatalogSha256 { get; } = Convert.ToHexString(SHA256.HashData(original));
        public string OriginalFileEvidenceSha256 => Stamp.Fingerprint;
        public long OriginalByteLength => Bytes.LongLength;
        public int OriginalSchemaVersion => 1;
    }
    private sealed record IdentityAcknowledgment(ICanonicalMiniComputerCatalogIdentityIntent OriginalIntent, bool Applied,
        ResourceStoreIdentity? CreatedIdentity, string? PublishedCatalogSha256, string Reason)
        : ICanonicalMiniComputerCatalogIdentityAcknowledgment;
    private sealed class IdentityWrite(IdentityIntent intent)
    {
        internal readonly IdentityIntent Intent = intent;
        internal Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> Driver = null!;
        internal ICanonicalMiniComputerCatalogIdentityHomeClaim? Claim;
        internal Lease? Pin;
        internal NativePersonalTaskRecoveryStore? PublishedPhysical;
        internal FileStream? PublishedReader;
        internal Task? PublishedReaderClose;
        internal NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp? PublishedStamp;
        internal NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp? StageStamp;
        internal byte[]? PublishedBytes;
        internal Microsoft.Win32.SafeHandles.SafeFileHandle? StageHandle;
        internal FileStream? Stage;
        internal Task? StageClose;
        internal string? StagePath;
        internal bool StageHandleCloseEntered, PublishedPhysicalCloseEntered;
        internal Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment>? Atomic;
        internal readonly TaskCompletionSource DispatchSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? DispatchWait, Release, PinClose;
        internal bool DispatchObserved, PinsReleased;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent,
            ICanonicalMiniComputerCatalogIdentityAcknowledgment>? Phase;
    }
    public Task<CanonicalMiniComputerCatalogIdentityPreparation> PrepareOriginalIdentitySetupWithinSourceAsync(
        AuthenticatedResourceActor expectedActor, Guid operationId, Action<Action> scope, Action<Task> retain,
        CancellationToken token) => AdmitScoped(scope, retain, async source =>
    {
        if (_identityHome is null) return new CanonicalMiniComputerCatalogIdentityPreparation(null, "The Home catalogue identity setup service is unavailable in this host.");
        if (operationId == Guid.Empty) return new CanonicalMiniComputerCatalogIdentityPreparation(null, "A new explicit setup operation is required.");
        if (!OperatingSystem.IsLinux()) return new CanonicalMiniComputerCatalogIdentityPreparation(null,
            "Catalogue identity setup needs this platform's verified atomic file writer. Existing catalogue reads remain available.");
        await DemandActor(expectedActor, source, token).ConfigureAwait(false);
        var lease = await AcquireIdentitySetupRead(source, expectedActor, token).ConfigureAwait(false);
        if (lease is null) return new CanonicalMiniComputerCatalogIdentityPreparation(null,
            "Create the canonical Mini Computer catalogue through its owning setup before reviewing identity initialization. This action does not create a missing catalogue.");
        IdentityIntent? intent = null; var reason = "The catalogue already has a durable identity. Review its separate Home import.";
        try
        {
            if (lease.IdentityOrNull is null)
            {
                var bytes = source.Invoke(lease.CopyIdentitySetupBytes);
                source.Run(() =>
                {
                    DemandUninitializedIdentityJson(bytes);
                    if (_catalog is not ICanonicalMiniComputerCatalogIdentityFormat format)
                        throw new InvalidOperationException("The configured canonical catalogue format owner is unavailable.");
                    format.ValidateOriginalIdentitySetupBytes(bytes);
                });
                var stamp = source.Invoke(lease.CaptureIdentitySetupStamp);
                intent = new(this, expectedActor, operationId, stamp, bytes);
                lock (_gate) _identityIntents.Add(intent, intent);
                reason = "Initialize one identity in this existing catalogue. VM records and other fields stay intact. Catalogue import and VM actions require separate Home decisions.";
            }
        }
        catch (Exception cause) { source.Remember(cause); }
        await source.ReadSetupTask(lease.CloseAndDrainOriginalAsync).ConfigureAwait(false);
        await DemandActor(expectedActor, source, token).ConfigureAwait(false);
        return new CanonicalMiniComputerCatalogIdentityPreparation(intent, reason);
    });
    private async Task<Lease?> AcquireIdentitySetupRead(OriginalScope source, AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (!source.Invoke(() => File.Exists(_path))) return null;
        // Lease acquisition has its own finite child cohort. Its cached close must
        // not join the parent cohort that will retain that same close Task.
        var acquisition = new OriginalScope(this, source.Run, source.Retain);
        var lease = new Lease(this, actor, acquisition);
        lock (_gate)
        {
            if (_leases.Count >= 64) throw new InvalidOperationException("Original catalogue setup custody requires inspection.");
            _leases.Add(lease);
        }
        try { await lease.Acquire(token).ConfigureAwait(false); return lease; }
        catch (Exception cause)
        {
            source.Remember(cause);
            await source.ReadSetupTask(lease.CloseAndDrainOriginalAsync).ConfigureAwait(false);
            throw;
        }
    }
    public bool IsIssuedOriginalIdentityIntent(ICanonicalMiniComputerCatalogIdentityIntent sameIntent) =>
        sameIntent is IdentityIntent intent && ReferenceEquals(intent.Owner, this) &&
        _identityIntents.TryGetValue(sameIntent, out var actual) && ReferenceEquals(intent, actual);
    private IdentityIntent RequireIdentityIntent(ICanonicalMiniComputerCatalogIdentityIntent value) =>
        IsIssuedOriginalIdentityIntent(value) ? (IdentityIntent)value : throw new UnauthorizedAccessException("Use the SAME privately issued catalogue identity preview.");
    public string GetOriginalIdentityIntentDigest(ICanonicalMiniComputerCatalogIdentityIntent sameIntent)
    {
        var intent = RequireIdentityIntent(sameIntent);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { intent.Actor, intent.OperationId, intent.CatalogueName, intent.OriginalCatalogSha256,
          intent.OriginalFileEvidenceSha256, intent.OriginalByteLength, intent.OriginalSchemaVersion })));
    }
    public Task ValidateOriginalIdentityIntentWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => AdmitScoped(scope, retain, async source =>
    {
        var intent = RequireIdentityIntent(sameIntent);
        await DemandActor(intent.Actor, source, token).ConfigureAwait(false);
        var lease = await AcquireIdentitySetupRead(source, intent.Actor, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original existing catalogue is no longer available.");
        try { source.Run(() => DemandIdentityOriginal(lease, intent)); }
        catch (Exception cause) { source.Remember(cause); }
        await source.ReadSetupTask(lease.CloseAndDrainOriginalAsync).ConfigureAwait(false);
        await DemandActor(intent.Actor, source, token).ConfigureAwait(false); return true;
    });
    private static void DemandIdentityOriginal(Lease lease, IdentityIntent intent)
    {
        if (lease.IdentityOrNull is not null) throw new UnauthorizedAccessException("A catalogue identity now exists. This create-only review cannot replace it.");
        lease.DemandIdentitySetupStamp(intent.Stamp);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(lease.CopyIdentitySetupBytes()), Convert.FromHexString(intent.OriginalCatalogSha256)))
            throw new UnauthorizedAccessException("The exact reviewed catalogue bytes changed.");
    }
    private static void DemandUninitializedIdentityJson(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
            throw new InvalidDataException("The actual canonical catalogue schema is unsupported.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("The actual catalogue contains ambiguous duplicate fields.");
            if (property.Name.Equals("originalStoreIdentity", StringComparison.OrdinalIgnoreCase) &&
                (property.Name != "originalStoreIdentity" || property.Value.ValueKind != JsonValueKind.Null))
                throw new InvalidDataException("The existing identity field cannot be replaced or reinterpreted.");
        }
    }
    private static byte[] AddOriginalIdentity(byte[] original, ResourceStoreIdentity identity)
    {
        DemandUninitializedIdentityJson(original);
        using var document = JsonDocument.Parse(original); using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name == "originalStoreIdentity") continue; // A canonical null is absent identity.
                writer.WritePropertyName(property.Name); writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: false);
            }
            writer.WritePropertyName("originalStoreIdentity"); JsonSerializer.Serialize(writer, identity, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            writer.WriteEndObject(); writer.Flush();
        }
        return output.ToArray();
    }

    /// <summary>The accepted original operation survives presentation cancellation.
    /// Only its actual Home owner can withdraw a still-pending review.</summary>
    public Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> ExecuteOriginalIdentitySetupWithinSourceAsync(
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        var intent = RequireIdentityIntent(sameIntent);
        TaskCompletionSource begin; IdentityWrite write;
        lock (_gate)
        {
            if (_identityWrites.TryGetValue(intent, out write!)) return write.Driver;
            ObjectDisposedException.ThrowIf(_retiring, this); token.ThrowIfCancellationRequested();
            if (_identityHome is null) throw new InvalidOperationException("The separately configured Home catalogue setup owner is unavailable.");
            _identityActive.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.PinsReleased);
            if (_identityActive.Count >= 128) throw new InvalidOperationException("Original catalogue setup outcomes require inspection.");
            write = new(intent);
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            write.Driver = AdmitScoped<ICanonicalMiniComputerCatalogIdentityAcknowledgment>(scope, retain, async source =>
            {
                await begin.Task.ConfigureAwait(false);
                return await ExecuteIdentityBody(write, source).ConfigureAwait(false);
            });
            _identityWrites.Add(intent, write); _identityActive.Add(write);
        }
        begin.SetResult(); return write.Driver;
    }
    private async Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> ExecuteIdentityBody(IdentityWrite write, OriginalScope source)
    {
        var home = _identityHome!; var intent = write.Intent;
        var prior = _activeIdentityWrite.Value; _activeIdentityWrite.Value = write;
        ICanonicalMiniComputerCatalogIdentityAcknowledgment? result = null; var declined = false;
        try
        {
            Task<ICanonicalMiniComputerCatalogIdentityHomeClaim>? admission = null;
            try
            {
                write.Claim = await source.ReadSetupAdmission(() => admission = home.AcquireOriginalIdentityWriteWithinSourceAsync(
                    intent, source.Run, source.Retain, actual => write.Claim = actual, CancellationToken.None)).ConfigureAwait(false);
            }
            catch
            {
                if (admission is null || write.Claim is null || !home.IsAcknowledgedOriginalIdentityWriteRefusal(admission)) throw;
                source.AcknowledgeSetupSources(home.IsAcknowledgedOriginalIdentityWriteRefusal); declined = true;
            }
            if (!declined)
            {
                if (!home.IsIssuedOriginalIdentityWriteClaim(write.Claim!, intent))
                    throw new UnauthorizedAccessException("The SAME individual catalogue identity WRITE decision is required.");
                await DemandActor(intent.Actor, source, CancellationToken.None).ConfigureAwait(false);
                write.Pin = await AcquireIdentitySetupRead(source, intent.Actor, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The reviewed existing catalogue is no longer available.");
                source.Run(() => DemandIdentityOriginal(write.Pin, intent));
                // The canonical catalogue reservation is held before the Home entry.
                // No profile/import/ownership operation occurs below this boundary.
                await source.ReadSetupTask(() => home.AcquireOriginalIdentityWriteEntryWithinSourceAsync(write.Claim!,
                    source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                write.Atomic = PublishIdentity(start.Task, write, source, home);
                var published = false;
                try
                {
                    source.Run(() => { home.RetainOriginalIdentityWrite(write.Claim!, write.Atomic); source.Retain(write.Atomic); });
                    published = true;
                }
                finally { start.SetResult(published); }
                result = await write.Atomic.ConfigureAwait(false);
            }
        }
        catch (Exception cause) { source.Remember(cause); }
        finally
        {
            try { await source.Join().ConfigureAwait(false); } catch { /* same raw failures remain in this source */ }
            write.DispatchSettled.TrySetResult();
            if (write.Atomic is { } raw && write.Claim is { } claim)
                try { await source.ReadSetupTask(() => home.CompleteOriginalIdentityWriteWithinSourceAsync(claim, raw,
                    source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
                catch (Exception cause) { source.Remember(cause); }
            if (write.Claim is { } actualClaim)
                try { await source.ReadSetupTask(actualClaim.CloseAndDrainOriginalAsync).ConfigureAwait(false); }
                catch (Exception cause) { source.Remember(cause); }
            if (write.Pin is not null && !write.PinsReleased)
                source.Remember(new InvalidOperationException("The actual Home settlement has not acknowledged the original catalogue setup pin release."));
            _activeIdentityWrite.Value = prior;
        }
        return result ?? (declined
            ? new IdentityAcknowledgment(intent, false, null, null, "Home declined this catalogue identity setup before any file write.")
            : throw new InvalidOperationException("The original catalogue identity outcome remains unconfirmed."));
    }
    private async Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> PublishIdentity(Task<bool> start,
        IdentityWrite write, OriginalScope source, ICanonicalMiniComputerCatalogIdentityHomeSource home)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("The actual atomic catalogue write was not retained by Home.");
        var intent = write.Intent; var pin = write.Pin!;
        source.Run(() => { DemandOriginalPinnedIdentityIntent(intent); home.DemandOriginalIdentityWrite(write.Claim!, intent); });
        var identity = new ResourceStoreIdentity(1, Guid.NewGuid(), DateTimeOffset.UtcNow, false);
        var bytes = source.Invoke(() =>
        {
            if (_catalog is not ICanonicalMiniComputerCatalogIdentityFormat format)
                throw new InvalidOperationException("The actual canonical catalogue format owner is unavailable.");
            format.ValidateOriginalIdentitySetupBytes(intent.Bytes);
            var result = AddOriginalIdentity(intent.Bytes, identity); format.ValidateOriginalIdentitySetupBytes(result); return result;
        });
        if (bytes.Length > MaximumCatalogBytes) throw new InvalidDataException("The identity-bearing catalogue exceeds its maintained size bound.");
        source.Run(() =>
        {
            write.StagePath = ".mini-identity-" + intent.OperationId.ToString("N") + "-" + Guid.NewGuid().ToString("N");
            write.StageHandle = pin.OriginalIdentitySetupPhysical.CreateOriginalMiniComputerStage(write.StagePath, intent.Stamp, this);
            // Capture the actual handle before the stream constructor or any postguard.
            write.Stage = new FileStream(write.StageHandle, FileAccess.Write, 16_384, isAsync: false);
        });
        await source.ReadSetupTask(() => write.Stage!.WriteAsync(bytes.AsMemory(), CancellationToken.None).AsTask()).ConfigureAwait(false);
        await source.ReadSetupTask(() => write.Stage!.FlushAsync(CancellationToken.None)).ConfigureAwait(false);
        source.Run(() =>
        {
            write.Stage!.Flush(flushToDisk: true);
            write.StageStamp = pin.OriginalIdentitySetupPhysical.ObserveOriginalMiniComputerStage(write.StageHandle!, write.StagePath!, this);
            DemandOriginalPinnedIdentityIntent(intent); home.DemandOriginalIdentityWrite(write.Claim!, intent);
            pin.OriginalIdentitySetupPhysical.PublishOriginalMiniComputerIdentityStage(write.StageHandle!, write.StagePath!, intent.Stamp, this);
            // Publication can have succeeded even when a later observation fails.
            // Never replay the atomic child or convert that uncertainty to no effect.
            write.PublishedPhysical = NativePersonalTaskRecoveryStore.Acquire(Path.GetDirectoryName(_path)!, _path);
            write.PublishedReader = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16_384, FileOptions.Asynchronous | FileOptions.RandomAccess);
            write.PublishedPhysical.ValidateOriginalMiniComputerReadHandle(write.PublishedReader.SafeFileHandle);
            write.PublishedPhysical.DemandOriginalMiniComputerIdentityStamp(write.StageStamp!, this);
        });
        var published = await ReadPublishedIdentityBytes(write, source).ConfigureAwait(false);
        source.Run(() =>
        {
            if (!published.AsSpan().SequenceEqual(bytes) || Lease.ReadIdentity(published) != identity)
                throw new UnauthorizedAccessException("The actual published catalogue is not the exact identity-only result.");
            write.PublishedStamp = write.StageStamp; write.PublishedBytes = published;
            DemandOriginalPinnedIdentityIntent(intent); home.DemandOriginalIdentityWrite(write.Claim!, intent);
        });
        return new IdentityAcknowledgment(intent, true, identity, Convert.ToHexString(SHA256.HashData(published)),
            "The existing catalogue now has its durable identity. Review separate Home catalogue access before browsing its VMs.");
    }
    private async Task<byte[]> ReadPublishedIdentityBytes(IdentityWrite write, OriginalScope source)
    {
        var bytes = source.Invoke(() =>
        {
            write.PublishedPhysical!.ValidateOriginalMiniComputerReadHandle(write.PublishedReader!.SafeFileHandle);
            var length = write.PublishedReader.Length;
            if (length is < 1 or > MaximumCatalogBytes) throw new InvalidDataException("The actual published catalogue length is invalid.");
            return new byte[checked((int)length)];
        });
        var offset = 0;
        while (offset < bytes.Length)
        {
            var at = offset;
            var count = await source.Read(() => RandomAccess.ReadAsync(write.PublishedReader!.SafeFileHandle,
                bytes.AsMemory(at), at, CancellationToken.None).AsTask()).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("The identity-bearing original catalogue changed during readback.");
            offset += count;
        }
        source.Run(() =>
        {
            write.PublishedPhysical!.ValidateOriginalMiniComputerReadHandle(write.PublishedReader!.SafeFileHandle);
            write.PublishedPhysical.DemandOriginalMiniComputerIdentityStamp(write.StageStamp!, this);
            if (write.PublishedReader.Length != bytes.Length) throw new IOException("The published catalogue changed during readback.");
        });
        return bytes;
    }
    public void DemandOriginalPinnedIdentityIntent(ICanonicalMiniComputerCatalogIdentityIntent sameIntent)
    {
        var intent = RequireIdentityIntent(sameIntent); var write = _activeIdentityWrite.Value;
        if (write is null || !ReferenceEquals(write.Intent, intent) || write.Pin is null || write.Claim is null ||
            write.DispatchSettled.Task.IsCompleted || _identityHome?.IsIssuedOriginalIdentityWriteClaim(write.Claim, intent) != true)
            throw new UnauthorizedAccessException("The SAME accepted catalogue identity setup and original file reservation are required.");
        write.Pin.DemandIdentitySetupReservation();
        if (write.PublishedStamp is null) { DemandIdentityOriginal(write.Pin, intent); return; }
        write.PublishedPhysical!.ValidateOriginalMiniComputerReadHandle(write.PublishedReader!.SafeFileHandle);
        write.PublishedPhysical.DemandOriginalMiniComputerIdentityStamp(write.PublishedStamp, this);
        if (write.PublishedReader.Length != write.PublishedBytes!.Length) throw new IOException("The committed catalogue changed before settlement.");
        var bytes = new byte[write.PublishedBytes.Length]; var offset = 0;
        while (offset < bytes.Length)
        {
            var count = RandomAccess.Read(write.PublishedReader.SafeFileHandle, bytes.AsSpan(offset), offset);
            if (count == 0) throw new EndOfStreamException("The committed catalogue changed before settlement.");
            offset += count;
        }
        if (!bytes.AsSpan().SequenceEqual(write.PublishedBytes)) throw new UnauthorizedAccessException("The exact committed catalogue revision changed.");
        write.PublishedPhysical.DemandOriginalMiniComputerIdentityStamp(write.PublishedStamp, this);
    }
    public Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> InvokeOriginalIdentityWriteWithinSourceAsync(
        ICanonicalMiniComputerCatalogIdentityIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIdentityIntent(sameIntent);
        lock (_gate) return _identityWrites.TryGetValue(intent, out var write) && write.Atomic is { } atomic
            ? atomic : throw new UnauthorizedAccessException("The original catalogue producer has not issued this atomic write.");
    }
    public bool IsOriginalIdentityWriteTask(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic)
    {
        lock (_gate) return sameIntent is IdentityIntent intent && ReferenceEquals(intent.Owner, this) &&
            _identityWrites.TryGetValue(intent, out var write) && ReferenceEquals(write.Atomic, sameAtomic);
    }
    public bool IsOwnedOriginalIdentityAcknowledgment(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        ICanonicalMiniComputerCatalogIdentityAcknowledgment acknowledgment, Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> sameAtomic) =>
        IsOriginalIdentityWriteTask(sameIntent, sameAtomic) && sameAtomic.IsCompletedSuccessfully &&
        acknowledgment is IdentityAcknowledgment && ReferenceEquals(sameAtomic.Result, acknowledgment) &&
        ReferenceEquals(acknowledgment.OriginalIntent, sameIntent);

    public (string? RequestId, bool IsPending) ObserveOriginalIdentitySetup(ICanonicalMiniComputerCatalogIdentityIntent sameIntent)
    {
        var intent = RequireIdentityIntent(sameIntent);
        lock (_gate) return _identityWrites.TryGetValue(intent, out var write)
            ? (write.Claim?.OriginalApprovalRequestId, !write.Driver.IsCompleted)
            : (null, false);
    }
    // These two finite cleanup drivers belong to the accepted identity invocation.
    // Admission during retirement is possible only while that SAME parent is live;
    // a cached driver may be observed again without starting another callback.
    private Task AdmitIdentityCleanup(IdentityWrite write, Action<Action> scope, Action<Task> retain,
        Func<OriginalScope, Task> body)
    {
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (write.Driver.IsCompleted || !_commands.Any(task => ReferenceEquals(task, write.Driver)) || _close?.IsCompleted == true)
                throw new ObjectDisposedException("The original catalogue setup parent is already terminal.");
            if (_commands.Count >= 256) throw new InvalidOperationException("Original catalogue cleanup custody is full.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, async () =>
            {
                var source = new OriginalScope(this, scope, retain);
                try { await body(source).ConfigureAwait(false); }
                catch (Exception cause) { source.Remember(cause); }
                await source.Join().ConfigureAwait(false); return true;
            });
            _commands.Add(actual);
        }
        start.SetResult(); return actual;
    }
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIdentityIntent(sameIntent);
        lock (_gate)
        {
            if (!_identityWrites.TryGetValue(intent, out var write)) throw new UnauthorizedAccessException("No accepted original catalogue setup owns this intent.");
            return write.DispatchWait ??= AdmitIdentityCleanup(write, scope, retain, async source =>
            {
                await source.ReadSetupTask(() => write.DispatchSettled.Task).ConfigureAwait(false);
                if (write.Atomic is { } raw && (!raw.IsCompleted || !IsOriginalIdentityWriteTask(intent, raw)))
                    throw new UnauthorizedAccessException("The SAME atomic catalogue write has not settled.");
                await source.Join().ConfigureAwait(false); lock (_gate) write.DispatchObserved = true;
            });
        }
    }
    public bool IsOwnedOriginalSettlementDispatch(ICanonicalMiniComputerCatalogIdentityIntent sameIntent,
        Task sameWait, Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment>? sameAtomic)
    {
        lock (_gate) return sameIntent is IdentityIntent intent && ReferenceEquals(intent.Owner, this) &&
            _identityWrites.TryGetValue(intent, out var write) && ReferenceEquals(write.DispatchWait, sameWait) &&
            sameWait.IsCompletedSuccessfully && write.DispatchObserved && write.DispatchSettled.Task.IsCompletedSuccessfully &&
            ReferenceEquals(write.Atomic, sameAtomic) && (sameAtomic is null || sameAtomic.IsCompleted && IsOriginalIdentityWriteTask(intent, sameAtomic));
    }
    private IdentityWrite RequireIdentitySettlement(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent,
            ICanonicalMiniComputerCatalogIdentityAcknowledgment> phase)
    {
        if (_identityHome?.IsIssuedOriginalSettlementReleasePhase(phase) != true)
            throw new UnauthorizedAccessException("The SAME actual Home owner must issue this settled catalogue setup phase.");
        var intent = RequireIdentityIntent(phase.OriginalIntent);
        lock (_gate)
        {
            if (!_identityWrites.TryGetValue(intent, out var write) || !write.DispatchObserved ||
                write.DispatchWait?.IsCompletedSuccessfully != true || !ReferenceEquals(write.Atomic, phase.OriginalAtomicSqlTask) ||
                write.Atomic is { } raw && (!raw.IsCompleted || !IsOriginalIdentityWriteTask(intent, raw)))
                throw new UnauthorizedAccessException("The actual original atomic/no-write cohort must be terminal before file-pin release.");
            return write;
        }
    }
    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent,
            ICanonicalMiniComputerCatalogIdentityAcknowledgment> phase,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var write = RequireIdentitySettlement(phase);
        lock (_gate)
        {
            if (write.Phase is not null && !ReferenceEquals(write.Phase, phase))
                throw new UnauthorizedAccessException("The source-issued Home catalogue setup phase cannot be replaced.");
            write.Phase = phase;
            return write.Release ??= AdmitIdentityCleanup(write, scope, retain, async source =>
            {
                if (!ReferenceEquals(RequireIdentitySettlement(phase), write)) throw new UnauthorizedAccessException();
                // Held Home and completion have independently closed. Original resources
                // close independently before the catalog reservation and Home audit.
                var closes = new List<Task>();
                if (write.Stage is { } stage)
                    try { source.Run(() => { write.StageClose ??= stage.DisposeAsync().AsTask(); closes.Add(write.StageClose); source.Retain(write.StageClose); }); }
                    catch (Exception cause) { source.Remember(cause); }
                if (write.PublishedReader is { } reader)
                    try { source.Run(() => { write.PublishedReaderClose ??= reader.DisposeAsync().AsTask(); closes.Add(write.PublishedReaderClose); source.Retain(write.PublishedReaderClose); }); }
                    catch (Exception cause) { source.Remember(cause); }
                foreach (var actual in closes)
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception cause) { source.Remember(actual.Exception ?? cause); }
                // A stream constructor could fail after the native stage was acquired.
                // Do not replay synchronous disposal when its entered result is unknown.
                if (write.Stage is null && write.StageHandle is { } handle && !write.StageHandleCloseEntered)
                    try { source.Run(() => { write.StageHandleCloseEntered = true; handle.Dispose(); }); }
                    catch (Exception cause) { source.Remember(cause); }
                if (write.PublishedPhysical is { } physical && !write.PublishedPhysicalCloseEntered)
                    try { source.Run(() => { write.PublishedPhysicalCloseEntered = true; physical.Dispose(); }); }
                    catch (Exception cause) { source.Remember(cause); }
                if (write.Pin is { } pin)
                    try { await source.ReadSetupTask(() => write.PinClose = pin.CloseAndDrainOriginalAsync()).ConfigureAwait(false); }
                    catch (Exception cause) { source.Remember(cause); }
                await source.Join().ConfigureAwait(false); lock (_gate) write.PinsReleased = true;
            });
        }
    }
    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent,
            ICanonicalMiniComputerCatalogIdentityAcknowledgment> phase, Task sameRelease)
    {
        if (_identityHome?.IsIssuedOriginalSettlementReleasePhase(phase) != true ||
            phase.OriginalIntent is not IdentityIntent intent || !ReferenceEquals(intent.Owner, this)) return false;
        lock (_gate) return _identityWrites.TryGetValue(intent, out var write) && ReferenceEquals(write.Phase, phase) &&
            ReferenceEquals(write.Release, sameRelease) && sameRelease.IsCompletedSuccessfully && write.PinsReleased &&
            (write.Pin is null || write.PinClose?.IsCompletedSuccessfully == true) &&
            (write.Stage is null || write.StageClose?.IsCompletedSuccessfully == true) &&
            (write.PublishedReader is null || write.PublishedReaderClose?.IsCompletedSuccessfully == true);
    }
    public sealed partial class Lease
    {
        internal byte[] CopyIdentitySetupBytes() => _bytes!.ToArray();
        internal NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp CaptureIdentitySetupStamp() =>
            _physical!.CaptureOriginalMiniComputerIdentityStamp(_owner);
        internal void DemandIdentitySetupStamp(NativePersonalTaskRecoveryStore.MiniComputerIdentityStamp stamp)
        { _physical!.DemandOriginalMiniComputerIdentityStamp(stamp, _owner); DemandOriginalPinnedCatalog(); }
        internal NativePersonalTaskRecoveryStore OriginalIdentitySetupPhysical => _physical!;
        internal void DemandIdentitySetupReservation() => _reservation!.DemandOriginalReservation();
    }
    internal sealed partial class OriginalScope
    {
        private readonly Dictionary<Task, Func<Task, bool>> _setupAcknowledgments = new(ReferenceEqualityComparer.Instance);
        internal async Task ReadSetupTask(Func<Task> factory)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception cause) { errors.Add(cause); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual catalogue setup source or callback failed.", errors);
        }
        internal async Task<T> ReadSetupAdmission<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
            try { Run(() => { actual = factory(); Retain(actual); }); } catch (Exception cause) { errors.Add(cause); }
            if (actual is not null) try { value = await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual catalogue setup admission or callback failed.", errors);
            return actual is null ? throw new InvalidOperationException("No original setup admission Task was captured.") : value;
        }
        internal void AcknowledgeSetupSources(Func<Task, bool> actualIssuerProof)
        {
            foreach (var task in _raw.OriginalTasks)
                if (task.IsFaulted && CloudflareOriginalExecutionGuard.InvokeOriginal(_originalOwner, () => actualIssuerProof(task)))
                    _setupAcknowledgments[task] = actualIssuerProof;
        }
        private async Task JoinIdentitySetupSources()
        {
            foreach (var task in _raw.OriginalTasks)
                try { await task.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (_setupAcknowledgments.TryGetValue(task, out var issuer) && CloudflareOriginalExecutionGuard.InvokeOriginal(_originalOwner, () => issuer(task))) continue;
                    _raw.Capture(task, cause);
                }
            if (_raw.OriginalErrors.Count != 0) throw new AggregateException("Original catalogue setup sources failed.", _raw.OriginalErrors);
        }
    }
}
