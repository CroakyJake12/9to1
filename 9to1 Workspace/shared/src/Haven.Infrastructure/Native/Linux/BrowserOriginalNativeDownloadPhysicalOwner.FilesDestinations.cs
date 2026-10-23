using System.Runtime.CompilerServices;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Linux;

public sealed partial class BrowserOriginalNativeDownloadPhysicalOwner
{
    private IBrowserOriginalFilesPhysicalDestinationSource? _filesDestinationSource;
    private readonly ConditionalWeakTable<IBrowserOriginalFilesPhysicalDestination, FilesPin> _issuedFilesPins = new();
    private readonly List<FilesPin> _filesPins = [];
    private readonly AsyncLocal<FilesPin?> _executingFilesPin = new();
    [ThreadStatic] private static Dictionary<FilesPin, int>? _physicalFilesPins;
    public IBrowserOriginalFilesPhysicalDestinationSource? OriginalFilesDestinationSource
    { get { lock (_gate) return _filesDestinationSource; } }
    private sealed class FilesPin(BrowserOriginalNativeDownloadPhysicalOwner owner,
        IBrowserOriginalFilesPhysicalDestination original) : IBrowserOriginalFilesPhysicalPin
    {
        internal readonly BrowserOriginalNativeDownloadPhysicalOwner Owner = owner;
        public IBrowserOriginalFilesPhysicalDestination OriginalDestination { get; } = original;
        internal BrowserOriginalFilesPhysicalDestinationDescription Description = null!;
        internal LinuxOriginalDownloadRoot? Root;
        internal uint Principal;
        internal SafeFileHandle? Parent, Stage;
        internal FileStream? Stream;
        internal LinuxOriginalDownloadIdentity ParentIdentity;
        internal LinuxOriginalDownloadIdentity PublishedIdentity;
        internal string? StagePath;
        internal bool Published;
        internal IBrowserOriginalDownloadContent? OriginalContent;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
        internal CanonicalSqliteOriginalSourceScope? CopySource;
        internal Task<IBrowserOriginalFilesPhysicalPin> Prepared = null!;
        internal Task? Copied, Close;
        public Task? OriginalClose { get { lock (Owner._gate) return Close; } }
        public ValueTask DisposeAsync() => new(Owner.CloseFilesPin(this));
    }
    public void BindOriginalFilesDestinationSource(IBrowserOriginalFilesPhysicalDestinationSource sameSource)
    {
        ArgumentNullException.ThrowIfNull(sameSource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_filesDestinationSource is not null && !ReferenceEquals(_filesDestinationSource, sameSource))
                throw new UnauthorizedAccessException("The physical Files destination source cannot be replaced.");
            _filesDestinationSource = sameSource;
        }
    }
    public Task<IBrowserOriginalFilesPhysicalPin> PinOriginalFilesDestinationWithinSourceAsync(
        IBrowserOriginalFilesPhysicalDestination originalDestination, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalDestination); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        FilesPin pin; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_issuedFilesPins.TryGetValue(originalDestination, out pin!)) return pin.Prepared;
            _filesPins.RemoveAll(value => value.Close?.IsCompletedSuccessfully == true);
            if (_filesPins.Count >= 64) throw new InvalidOperationException("Close unresolved original Files physical pins before another registration.");
            pin = new(this, originalDestination);
            pin.Source = new(this, body => InvokeFilesPin(pin, () => scope(body)), retain);
            pin.Prepared = Prepare(start.Task); _issuedFilesPins.Add(originalDestination, pin); _filesPins.Add(pin);
        }
        start.SetResult(); return pin.Prepared;
        async Task<IBrowserOriginalFilesPhysicalPin> Prepare(Task begin)
        {
            await begin.ConfigureAwait(false); var previous = _executingFilesPin.Value; _executingFilesPin.Value = pin;
            try
            {
                pin.Source.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var issuer = _filesDestinationSource ?? throw new InvalidOperationException("Bind the actual Files destination issuer first.");
                    if (!issuer.IsIssuedOriginalPhysicalDestination(originalDestination))
                        throw new UnauthorizedAccessException("The actual Files owner did not issue this registered destination.");
                    pin.Description = issuer.GetOriginalPhysicalDestinationDescription(originalDestination);
                    var description = pin.Description;
                    if (description.FilesStoreId == Guid.Empty || description.ParentFolderId == Guid.Empty ||
                        description.NewFileId == Guid.Empty || description.NewRevisionId == Guid.Empty ||
                        description.RelativeContentReference != ".9to1-browser-" + description.NewRevisionId.ToString("N") + ".content" ||
                        description.OriginalActor is null || string.IsNullOrWhiteSpace(description.OriginalActor.ActorId) ||
                        string.IsNullOrWhiteSpace(description.OriginalActor.ProfileId) || string.IsNullOrWhiteSpace(description.OriginalActor.AuthenticationRevision) ||
                        !Path.IsPathFullyQualified(description.DirectoryPath) || !LinuxOriginalDownloadPaths.IsSafeLeaf(description.RelativeContentReference))
                        throw new UnauthorizedAccessException("The original Files destination descriptor is incomplete.");
                    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Original Files materialization requires supported Linux.");
                    LinuxOriginalDownloadPaths.RequireCapabilities(); pin.Principal = LinuxOriginalDownloadPaths.CurrentUid();
                    pin.Root = LinuxOriginalDownloadPaths.RetainRoot(description.DirectoryPath);
                    pin.Source.CaptureOriginalResource(new OriginalDisposable(pin.Root));
                    pin.Parent = pin.Root.OpenDirectory(description.DirectoryPath, mutable: true);
                    pin.Source.CaptureOriginalResource(new OriginalDisposable(pin.Parent));
                    pin.ParentIdentity = LinuxOriginalDownloadPaths.ReadIdentity(pin.Parent);
                    DemandFilesParent(pin);
                });
                return pin;
            }
            finally { _executingFilesPin.Value = previous; }
        }
    }
    public bool IsIssuedOriginalFilesPin(IBrowserOriginalFilesPhysicalPin actual) =>
        actual is FilesPin pin && ReferenceEquals(pin.Owner, this) &&
        _issuedFilesPins.TryGetValue(pin.OriginalDestination, out var issued) && ReferenceEquals(pin, issued);
    public Task CopyOriginalDownloadToFilesWithinSourceAsync(IBrowserOriginalFilesPhysicalPin originalPin,
        IBrowserOriginalDownloadContent originalContent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var pin = RequireFilesPin(originalPin); _ = RequireContent(originalContent);
        TaskCompletionSource start;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || pin.Close is not null, this);
            if (pin.OriginalContent is not null && !ReferenceEquals(pin.OriginalContent, originalContent))
                throw new UnauthorizedAccessException("This one-use destination cannot adopt another original content source.");
            if (pin.Copied is not null) return pin.Copied;
            pin.OriginalContent = originalContent; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pin.CopySource = new(this, body => InvokeFilesPin(pin, () => scope(body)), retain);
            // Capture both the actual driver and its raw/resources before publication.
            pin.Copied = Copy(start.Task);
        }
        start.SetResult(); return pin.Copied!;
        async Task Copy(Task begin)
        {
            await begin.ConfigureAwait(false); var previous = _executingFilesPin.Value; _executingFilesPin.Value = pin;
            var source = pin.CopySource!;
            try
            {
                await source.Read(() => pin.Prepared).ConfigureAwait(false);
                source.Run(() =>
                {
                    token.ThrowIfCancellationRequested(); DemandFilesCommit(pin);
                    var stageLeaf = ".9to1-browser-stage-" + pin.Description.NewRevisionId.ToString("N") + ".tmp";
                    pin.StagePath = Path.Combine(pin.Description.DirectoryPath, stageLeaf);
                    pin.Stage = LinuxOriginalDownloadPaths.CreateStage(pin.Parent!, stageLeaf);
                    source.CaptureOriginalResource(new OriginalDisposable(pin.Stage));
                    LinuxOriginalDownloadPaths.DemandPrivateStage(pin.Stage, pin.Principal);
                    LinuxOriginalDownloadPaths.DemandPath(pin.Stage, pin.StagePath, false);
                    pin.Stream = new FileStream(pin.Stage, FileAccess.ReadWrite, 64 * 1024, isAsync: false);
                    source.CaptureOriginalResource(pin.Stream);
                });
                // The SAME original content owner verifies hash/length on the held
                // source handle. Every actual destination write has the held Home demand.
                using var guarded = new CommitGuardedDestination(pin.Stream!, () => source.Run(() => DemandFilesCommit(pin)));
                await source.Read(() => CopyOriginalContentWithinSourceAsync(originalContent, guarded, source.Run, source.Retain, token)).ConfigureAwait(false);
                await source.Read(() => pin.Stream!.FlushAsync(token)).ConfigureAwait(false);
                source.Run(() =>
                {
                    token.ThrowIfCancellationRequested(); DemandFilesCommit(pin); pin.Stream!.Flush(flushToDisk: true);
                    LinuxOriginalDownloadPaths.DemandPrivateStage(pin.Stage!, pin.Principal);
                    var actual = LinuxOriginalDownloadPaths.ReadIdentity(pin.Stage!);
                    if (!actual.IsRegular || actual.Links != 1 || actual.Size != (ulong)originalContent.OriginalRecord.SizeBytes)
                        throw new IOException("The held Files stage does not contain the exact original length.");
                    pin.PublishedIdentity = LinuxOriginalDownloadPaths.PublishCreateOnly(pin.Stage!, pin.Parent!,
                        pin.Description.DirectoryPath, Path.GetFileName(pin.StagePath!), pin.Description.RelativeContentReference, actual);
                    pin.Published = true; LinuxOriginalDownloadPaths.Flush(pin.Parent!);
                    if (!pin.PublishedIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(pin.Stage!)))
                        throw new IOException("Preserve the changed original Linux Files publication outcome.");
                    LinuxOriginalDownloadPaths.DemandPath(pin.Stage!, Path.Combine(pin.Description.DirectoryPath, pin.Description.RelativeContentReference), false);
                    DemandFilesCommit(pin);
                });
                await VerifyFilesBytes(pin, originalContent.OriginalRecord, source, token).ConfigureAwait(false);
            }
            finally { _executingFilesPin.Value = previous; }
        }
    }
    private async Task VerifyFilesBytes(FilesPin pin, Haven.Core.BrowserDownloadRecord record,
        CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        System.Security.Cryptography.IncrementalHash hash = null!; OriginalDisposable close = null!;
        source.Run(() =>
        {
            DemandFilesCommit(pin); DemandPublishedFiles(pin); pin.Stream!.Position = 0;
            hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            close = new(hash); source.CaptureOriginalResource(close);
        });
        long length = 0; var bytes = new byte[64 * 1024];
        while (true)
        {
            source.Run(() => DemandFilesCommit(pin));
            var read = await source.Read(() => pin.Stream!.ReadAsync(bytes.AsMemory(), token).AsTask()).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
            source.Run(() =>
            {
                if (length > record.SizeBytes || length > MaximumBytes) throw new IOException("The original Linux Files content grew.");
                hash.AppendData(bytes, 0, read);
            });
        }
        var digest = source.Invoke(() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        await source.CloseAsync(close).ConfigureAwait(false);
        source.Run(() =>
        {
            token.ThrowIfCancellationRequested(); DemandFilesCommit(pin); DemandPublishedFiles(pin);
            if (length != record.SizeBytes || digest != record.Sha256)
                throw new IOException("Preserve changed original Files bytes; no successful registration receipt is issued.");
        });
    }
    private void DemandPublishedFiles(FilesPin pin)
    {
        if (!pin.Published || pin.Stage is null || !pin.PublishedIdentity.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(pin.Stage)))
            throw new IOException("The SAME promoted original Files inode changed.");
        LinuxOriginalDownloadPaths.DemandPrivateStage(pin.Stage, pin.Principal);
        LinuxOriginalDownloadPaths.DemandPath(pin.Stage, Path.Combine(pin.Description.DirectoryPath, pin.Description.RelativeContentReference), false);
    }
    private sealed class CommitGuardedDestination(Stream stream, Action demand) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { demand(); stream.Flush(); }
        public override Task FlushAsync(CancellationToken token) { demand(); return stream.FlushAsync(token); }
        public override void Write(byte[] buffer, int offset, int count) { demand(); stream.Write(buffer, offset, count); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) { demand(); return stream.WriteAsync(buffer, offset, count, token); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default) { demand(); return stream.WriteAsync(bytes, token); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // The original pin owns the actual stream and exact cached close.
    }
    private FilesPin RequireFilesPin(IBrowserOriginalFilesPhysicalPin actual) => IsIssuedOriginalFilesPin(actual)
        ? (FilesPin)actual : throw new UnauthorizedAccessException("The SAME actual physical Files pin is required.");
    private void DemandFilesParent(FilesPin pin)
    {
        LinuxOriginalDownloadPaths.DemandCurrentUid(pin.Principal); pin.Root!.DemandCurrent();
        LinuxOriginalDownloadPaths.DemandPath(pin.Parent!, pin.Description.DirectoryPath, true);
        LinuxOriginalDownloadPaths.DemandOwner(pin.Parent!, pin.Principal);
        if (!pin.ParentIdentity.SameFile(LinuxOriginalDownloadPaths.ReadIdentity(pin.Parent!)))
            throw new UnauthorizedAccessException("The actual registered Files parent changed.");
    }
    private void DemandFilesCommit(FilesPin pin)
    {
        DemandFilesParent(pin);
        var source = _filesDestinationSource ?? throw new UnauthorizedAccessException("The actual Files issuer is unavailable.");
        if (!source.IsIssuedOriginalPhysicalDestination(pin.OriginalDestination))
            throw new UnauthorizedAccessException("The original Files destination was revoked.");
        source.DemandOriginalFilesCommit(pin.OriginalDestination);
    }
    private static void InvokeFilesPin(FilesPin pin, Action body)
    {
        var physical = _physicalFilesPins ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(pin, out var depth); physical[pin] = depth + 1;
        try { body(); } finally { if (depth == 0) physical.Remove(pin); else physical[pin] = depth; }
    }
    private Task CloseFilesPin(FilesPin pin)
    {
        if (ReferenceEquals(_executingFilesPin.Value, pin) || _physicalFilesPins?.ContainsKey(pin) == true)
            throw new InvalidOperationException("The actual Files pin cannot join its own original driver or callback.");
        TaskCompletionSource start;
        lock (_gate)
        {
            if (pin.Close is not null) return pin.Close;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously); pin.Close = Close(start.Task);
        }
        start.SetResult(); return pin.Close;
        async Task Close(Task begin)
        {
            await begin.ConfigureAwait(false); var previous = _executingFilesPin.Value; _executingFilesPin.Value = pin;
            var errors = new List<Exception>();
            try
            {
                foreach (var actual in new Task?[] { pin.Prepared, pin.Copied }.OfType<Task>())
                    try { await actual.ConfigureAwait(false); } catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, actual, cause); }
                foreach (var source in new[] { pin.CopySource, pin.Source }.OfType<CanonicalSqliteOriginalSourceScope>())
                {
                    try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                    try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                }
                CanonicalSqliteOriginalStoreOwner.Throw(errors);
            }
            finally { _executingFilesPin.Value = previous; }
        }
    }
}
