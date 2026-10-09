using System.Runtime.CompilerServices;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

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
        internal WindowsOriginalRoot? Root;
        internal string Principal = null!;
        internal SafeFileHandle? Parent, Stage;
        internal FileStream? Stream;
        internal WindowsOriginalFileIdentity ParentIdentity;
        internal WindowsOriginalFileIdentity PublishedIdentity;
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
                        !Path.IsPathFullyQualified(description.DirectoryPath) || !WindowsOriginalFileCustody.IsSafeLeaf(description.RelativeContentReference))
                        throw new UnauthorizedAccessException("The original Files destination descriptor is incomplete.");
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original Files materialization requires Windows.");
                    WindowsOriginalFileCustody.RequireCapabilities(); pin.Principal = WindowsOriginalFileCustody.CurrentSid();
                    pin.Root = WindowsOriginalFileCustody.RetainRoot(description.DirectoryPath);
                    pin.Source.CaptureOriginalResource(new OriginalDisposable(pin.Root));
                    pin.Parent = pin.Root.OpenDirectory(description.DirectoryPath, mutable: true);
                    pin.Source.CaptureOriginalResource(new OriginalDisposable(pin.Parent));
                    pin.ParentIdentity = WindowsOriginalFileCustody.ReadIdentity(pin.Parent);
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
                    var security = WindowsOriginalFileCustody.CreatePrivateDescriptor(pin.Principal);
                    source.CaptureOriginalResource(new OriginalDisposable(security));
                    var stageLeaf = ".9to1-browser-stage-" + pin.Description.NewRevisionId.ToString("N") + ".tmp";
                    pin.StagePath = Path.Combine(pin.Description.DirectoryPath, stageLeaf);
                    pin.Stage = WindowsOriginalFileCustody.OpenRelative(pin.Parent!, stageLeaf,
                        WindowsOriginalFileCustody.ExclusiveStage, 0, WindowsOriginalCreateDisposition.CreateNew,
                        WindowsOriginalFileKind.File, security);
                    source.CaptureOriginalResource(new OriginalDisposable(pin.Stage));
                    WindowsOriginalFileCustody.DemandPrivateStage(pin.Stage, pin.Principal);
                    WindowsOriginalFileCustody.DemandPath(pin.Stage, pin.StagePath, false);
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
                    DemandFilesCommit(pin); pin.Stream!.Flush(flushToDisk: true);
                    var actual = WindowsOriginalFileCustody.ReadIdentity(pin.Stage!);
                    if (!actual.IsRegular || actual.Links != 1 || actual.Size != (ulong)originalContent.OriginalRecord.SizeBytes)
                        throw new IOException("The held Files stage does not contain the exact original length.");
                    WindowsOriginalFileCustody.PublishCreateOnly(pin.Stage!, pin.Parent!, pin.Description.RelativeContentReference);
                    pin.Published = true; WindowsOriginalFileCustody.Flush(pin.Parent!);
                    pin.PublishedIdentity = WindowsOriginalFileCustody.ReadIdentity(pin.Stage!);
                    WindowsOriginalFileCustody.DemandPath(pin.Stage!, Path.Combine(pin.Description.DirectoryPath, pin.Description.RelativeContentReference), false);
                    DemandFilesCommit(pin);
                });
            }
            finally { _executingFilesPin.Value = previous; }
        }
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
        WindowsOriginalFileCustody.DemandCurrentSid(pin.Principal); pin.Root!.DemandCurrent();
        WindowsOriginalFileCustody.DemandPath(pin.Parent!, pin.Description.DirectoryPath, true);
        WindowsOriginalFileCustody.DemandOwner(pin.Parent!, pin.Principal);
        if (!pin.ParentIdentity.SameFile(WindowsOriginalFileCustody.ReadIdentity(pin.Parent!)))
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
