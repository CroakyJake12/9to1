using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Physical custody for a registered personal Files revision. Construction
/// performs no IO; a separate source-issued Home READ precedes any content access.</summary>
public sealed class CanonicalAttachmentOriginalFileSource(
    ICanonicalAttachmentOriginalSelectionSource selections, HomeLocalProfileIdentity profiles)
    : ICanonicalAttachmentOriginalContentSource, IAsyncDisposable
{
    private readonly ICanonicalAttachmentOriginalSelectionSource _selections = selections;
    private readonly HomeLocalProfileIdentity _profiles = profiles;
    private readonly object _gate = new();
    private readonly List<Command> _commands = [];
    private sealed class Command { internal Task Driver = null!; internal Task Observation = null!; }
    private readonly List<Lease> _leases = [];
    private ICanonicalAttachmentOriginalReadSource? _reads;
    private bool _retiring;
    private Task? _close;
    public void BindOriginalReadSource(ICanonicalAttachmentOriginalReadSource reads)
    {
        ArgumentNullException.ThrowIfNull(reads);
        lock (_gate)
        {
            if (_retiring) throw new ObjectDisposedException(nameof(CanonicalAttachmentOriginalFileSource));
            if (_reads is not null && !ReferenceEquals(_reads, reads))
                throw new InvalidOperationException("The original attachment READ source is already configured.");
            _reads = reads;
        }
    }
    public bool HasOriginalSelectionComposition(ICanonicalAttachmentOriginalSelectionSource actualSelections,
        HomeLocalProfileIdentity actualProfiles) => ReferenceEquals(_selections, actualSelections) && ReferenceEquals(_profiles, actualProfiles);
    public bool HasOriginalComposition(ICanonicalAttachmentOriginalSelectionSource actualSelections,
        HomeLocalProfileIdentity actualProfiles, ICanonicalAttachmentOriginalReadSource actualReads)
    { lock (_gate) return ReferenceEquals(_selections, actualSelections) && ReferenceEquals(_profiles, actualProfiles) && ReferenceEquals(_reads, actualReads); }
    public bool IsIssuedOriginalContent(ICanonicalAttachmentOriginalSelection selection,
        ICanonicalAttachmentOriginalReadAdmission read, ICanonicalAttachmentOriginalContentLease lease)
    {
        lock (_gate) return !_retiring && lease is Lease actual && ReferenceEquals(actual.Owner, this) &&
            ReferenceEquals(actual.OriginalSelection, selection) && ReferenceEquals(actual.OriginalRead, read) &&
            actual.Acquired && actual.OriginalClose is null && _leases.Any(value => ReferenceEquals(value, actual));
    }
    public Task<ICanonicalAttachmentOriginalContentLease> OpenOriginalContentWithinSourceAsync(
        ICanonicalAttachmentOriginalSelection selection, ICanonicalAttachmentOriginalReadAdmission read,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Admit<ICanonicalAttachmentOriginalContentLease>(async () =>
        {
            var source = new AttachmentOriginalSources(this, scope, retain);
            var lease = new Lease(this, selection, read, source);
            lock (_gate) _leases.Add(lease); // Own partial native products before any caller callback.
            try { await lease.Acquire(token).ConfigureAwait(false); }
            catch (Exception error)
            {
                source.Remember(error);
                try { await lease.CloseAndDrainOriginalAsync().ConfigureAwait(false); }
                catch (Exception close) { source.Remember(close); }
            }
            await source.Join().ConfigureAwait(false); return lease;
        });
    private Task<T> Admit<T>(Func<Task<T>> body)
    {
        TaskCompletionSource start; Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            _commands.RemoveAll(command => command.Driver.IsCompletedSuccessfully && command.Observation.IsCompletedSuccessfully);
            _leases.RemoveAll(lease => ObserveHealthyClose(lease.OriginalClose));
            if (_commands.Count >= 128 || _leases.Count >= 64) throw new InvalidOperationException("Settle original attachment readers before another selection.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Drive(start.Task, body);
            var command = new Command { Driver = actual };
            command.Observation = ObserveOriginalCommand(actual); _commands.Add(command);
        }
        start.SetResult(); return actual;
    }
    private static async Task ObserveOriginalCommand(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual.Exception ?? error).Throw(); }
    }
    private static bool ObserveHealthyClose(Task? actual)
    {
        if (actual?.IsCompletedSuccessfully != true) return false;
        actual.GetAwaiter().GetResult(); // Independently consume the SAME terminal receipt before forgetting it.
        return true;
    }
    private async Task<T> Drive<T>(Task start, Func<Task<T>> body)
    { await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this); return await body().ConfigureAwait(false); }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Lease[] leases; lock (_gate) leases = _leases.ToArray();
        foreach (var lease in leases) lease.DemandExternalOriginalJoin();
    }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            _retiring = true;
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); Command[] commands; lock (_gate) commands = _commands.ToArray();
        var errors = new List<Exception>();
        foreach (var command in commands)
            foreach (var task in new[] { command.Driver, command.Observation })
                try { await task.ConfigureAwait(false); } catch (Exception error) { errors.Add(task.Exception ?? error); }
        Lease[] leases; lock (_gate) leases = _leases.ToArray();
        foreach (var lease in leases)
        {
            Task? task = null;
            try { task = lease.CloseAndDrainOriginalAsync(); await task.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(task?.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original attachment readers did not close healthy.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());

    private sealed class Lease(CanonicalAttachmentOriginalFileSource owner,
        ICanonicalAttachmentOriginalSelection selection, ICanonicalAttachmentOriginalReadAdmission read,
        AttachmentOriginalSources acquisition) : ICanonicalAttachmentOriginalContentLease
    {
        internal CanonicalAttachmentOriginalFileSource Owner => owner;
        public ICanonicalAttachmentOriginalSelection OriginalSelection => selection;
        public ICanonicalAttachmentOriginalReadAdmission OriginalRead => read;
        private readonly List<Task> _operations = [];
        private readonly List<AttachmentOriginalSources> _sources = [acquisition];
        private NativePersonalTaskRecoveryStore? _physical;
        private FileStream? _file;
        private MemoryStream? _content;
        private Task? _close, _fileClose;
        private bool _fileCloseEntered, _physicalCloseEntered, _contentCloseEntered;
        internal bool Acquired;
        public Stream OriginalContent
        {
            get { lock (owner._gate) return Acquired && _close is null && _content is not null ? _content :
                throw new ObjectDisposedException("Original attachment content"); }
        }
        internal async Task Acquire(CancellationToken token)
        {
            await ValidateAuthority(acquisition, token).ConfigureAwait(false);
            acquisition.Run(() =>
            {
                if (selection.OriginalFile.SizeBytes is < 1 or > 50L * 1024 * 1024)
                    throw new InvalidDataException("The registered attachment exceeds the local text limit.");
                var path = selection.OriginalMaterializationPath;
                _physical = NativePersonalTaskRecoveryStore.Acquire(Path.GetDirectoryName(path)!, path);
                _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
                _physical.ValidateOriginalAttachmentReadHandle(_file.SafeFileHandle);
            });
            var bytes = await ReadExact(acquisition, token).ConfigureAwait(false);
            await ValidateAuthority(acquisition, token).ConfigureAwait(false);
            acquisition.Run(() => { _content = new MemoryStream(bytes, writable: false); Acquired = true; });
        }
        private async Task ValidateAuthority(AttachmentOriginalSources source, CancellationToken token)
        {
            ICanonicalAttachmentOriginalReadSource reads;
            lock (owner._gate) reads = owner._reads ?? throw new InvalidOperationException("The original Home attachment READ source is unavailable.");
            source.Run(() =>
            {
                if (!owner._selections.IsIssuedOriginalSelection(selection) || !reads.IsIssuedOriginalRead(selection, read))
                    throw new UnauthorizedAccessException("Retain the issued Files selection and its separate current Home READ.");
            });
            if (await source.Read(() => owner._profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != selection.OriginalActor)
                throw new UnauthorizedAccessException("The original attachment Home profile changed.");
            await source.Read(() => owner._selections.RevalidateOriginalSelectionWithinSourceAsync(selection, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.Read(() => read.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (await source.Read(() => owner._profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) != selection.OriginalActor)
                throw new UnauthorizedAccessException("The original attachment Home profile changed.");
        }
        private async Task<byte[]> ReadExact(AttachmentOriginalSources source, CancellationToken token)
        {
            var bytes = source.Invoke(() =>
            {
                _physical!.ValidateOriginalAttachmentReadHandle(_file!.SafeFileHandle);
                if (_file.Length != selection.OriginalFile.SizeBytes) throw new InvalidDataException("The selected file size changed.");
                _file.Position = 0; return new byte[checked((int)_file.Length)];
            });
            await source.Read(() => _file!.ReadExactlyAsync(bytes.AsMemory(), token).AsTask()).ConfigureAwait(false);
            source.Run(() =>
            {
                _physical!.ValidateOriginalAttachmentReadHandle(_file!.SafeFileHandle);
                var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (_file.Length != bytes.Length || !string.Equals(hash, selection.OriginalFile.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The selected Files revision no longer matches its registered content.");
            });
            return bytes;
        }
        public Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            TaskCompletionSource start; Task actual;
            lock (owner._gate)
            {
                if (!Acquired || _close is not null || owner._retiring) throw new ObjectDisposedException("Original attachment reader");
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = Validate(start.Task, scope, retain, token); _operations.Add(actual);
            }
            start.SetResult(); return actual;
        }
        private async Task Validate(Task start, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = new AttachmentOriginalSources(this, scope, retain); lock (owner._gate) _sources.Add(source);
            try
            {
                await ValidateAuthority(source, token).ConfigureAwait(false);
                // The held immutable bytes were hashed before publication; native
                // validation rechecks the exact file/parent/security without path adoption.
                source.Run(() => _physical!.ValidateOriginalAttachmentReadHandle(_file!.SafeFileHandle));
            }
            catch (Exception error) { source.Remember(error); }
            await source.Join().ConfigureAwait(false);
        }
        public Task? OriginalClose { get { lock (owner._gate) return _close; } }
        public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); TaskCompletionSource? start = null; Task actual;
            lock (owner._gate)
            {
                if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Close(start.Task); }
                actual = _close;
            }
            start?.SetResult(); return actual;
        }
        private async Task Close(Task start)
        {
            await start.ConfigureAwait(false); Task[] operations; lock (owner._gate) operations = _operations.ToArray();
            var errors = new List<Exception>();
            foreach (var task in operations) try { await task.ConfigureAwait(false); } catch (Exception error) { errors.Add(task.Exception ?? error); }
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                {
                    if (_content is not null && !_contentCloseEntered) { _contentCloseEntered = true; _content.Dispose(); }
                    return true;
                });
            }
            catch (Exception error) { errors.Add(error); }
            try
            {
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                {
                    if (_file is not null && !_fileCloseEntered) { _fileCloseEntered = true; _fileClose = _file.DisposeAsync().AsTask(); }
                    return true;
                });
                if (_fileClose is not null) await _fileClose.ConfigureAwait(false);
            }
            catch (Exception error) { errors.Add(_fileClose?.Exception ?? error); }
            // Never release native custody when actual reader close is unknown.
            if (_file is null || _fileClose?.IsCompletedSuccessfully == true)
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                { if (_physical is not null && !_physicalCloseEntered) { _physicalCloseEntered = true; _physical.Dispose(); } return true; }); }
                catch (Exception error) { errors.Add(error); }
            AttachmentOriginalSources[] sources; lock (owner._gate) sources = _sources.ToArray();
            foreach (var source in sources) try { await source.Join().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Original attachment content cleanup failed.", errors);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
}
