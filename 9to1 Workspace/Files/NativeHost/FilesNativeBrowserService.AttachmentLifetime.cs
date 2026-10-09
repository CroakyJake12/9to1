using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    private readonly object _attachmentSourceGate = new();
    private readonly List<AttachmentSource> _attachmentSources = [];
    private readonly List<NativeFilesWorkspace> _attachmentStores = [];
    private bool _attachmentSourceRetiring;
    private Task? _attachmentSourceClose;
    private sealed class AttachmentSource
    {
        internal Task Driver = null!;
        internal Task Observation = null!;
        internal readonly List<Task> Raw = [];
        internal readonly List<Exception> Errors = [];
    }
    private void RememberAttachmentStores(NativeFilesWorkspace workspace)
    {
        lock (_attachmentSourceGate)
            if (!_attachmentStores.Any(prior => ReferenceEquals(prior.Materializations, workspace.Materializations)))
                _attachmentStores.Add(workspace);
    }
    private Task<T> AdmitAttachmentSource<T>(Action<Action> scope, Action<Task> retain,
        Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        AttachmentSource record; TaskCompletionSource start; Task<T> actual;
        lock (_attachmentSourceGate)
        {
            ObjectDisposedException.ThrowIf(_attachmentSourceRetiring, this);
            _attachmentSources.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Observation.IsCompletedSuccessfully);
            if (_attachmentSources.Count >= 128) throw new InvalidOperationException("Settle the original attachment selections before continuing.");
            record = new(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DriveAttachmentSource(start.Task, record, scope, retain, body);
            record.Driver = actual; record.Observation = ObserveAttachmentSource(actual); _attachmentSources.Add(record);
        }
        start.SetResult(); return actual;
    }
    private static async Task ObserveAttachmentSource(Task actual) => await actual.ConfigureAwait(false);
    private async Task<T> DriveAttachmentSource<T>(Task start, AttachmentSource record, Action<Action> scope,
        Action<Task> retain, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        void Capture(Exception error) { lock (record.Errors) if (!record.Errors.Any(prior => ReferenceEquals(prior, error))) record.Errors.Add(error); }
        var source = FilesOriginalParentSourceCallbacks.Create(callback =>
        {
            try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(callback); return true; }); }
            catch (Exception error) { Capture(error); throw; }
        }, raw =>
        {
            lock (record.Raw) if (!record.Raw.Any(prior => ReferenceEquals(prior, raw))) record.Raw.Add(raw);
            try { retain(raw); } catch (Exception error) { Capture(error); throw; }
        });
        T value = default!;
        try { value = await body(source).ConfigureAwait(false); } catch (Exception error) { Capture(error); }
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] pending; lock (record.Raw) pending = record.Raw.Where(joined.Add).ToArray();
            if (pending.Length == 0) break;
            foreach (var actual in pending)
                try { await actual.ConfigureAwait(false); } catch (Exception error) { Capture(actual.Exception ?? error); }
        }
        Exception[] errors; lock (record.Errors) errors = record.Errors.ToArray();
        if (errors.Length != 0) throw new AggregateException("The original Files attachment selection failed.", errors);
        return value;
    }
    public Task? OriginalAttachmentSelectionsClose { get { lock (_attachmentSourceGate) return _attachmentSourceClose; } }
    public void DemandExternalOriginalAttachmentJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public Task CloseOriginalAttachmentSelectionsAndDrainAsync()
    {
        DemandExternalOriginalAttachmentJoin(); TaskCompletionSource? start = null; Task actual;
        lock (_attachmentSourceGate)
        {
            _attachmentSourceRetiring = true;
            if (_attachmentSourceClose is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _attachmentSourceClose = DrainAttachmentSources(start.Task); }
            actual = _attachmentSourceClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task DrainAttachmentSources(Task start)
    {
        await start.ConfigureAwait(false); AttachmentSource[] sources; NativeFilesWorkspace[] stores;
        lock (_attachmentSourceGate) sources = _attachmentSources.ToArray();
        var errors = new List<Exception>();
        foreach (var source in sources)
        {
            try { await source.Driver.ConfigureAwait(false); } catch (Exception error) { errors.Add(source.Driver.Exception ?? error); }
            try { await source.Observation.ConfigureAwait(false); } catch (Exception error) { errors.Add(source.Observation.Exception ?? error); }
        }
        // All productive commands are terminal before sealing their maintained
        // metadata read cohorts. No provider, registry, mapping or file is deleted.
        lock (_attachmentSourceGate) stores = _attachmentStores.ToArray();
        foreach (var store in stores)
        {
            Task? close = null;
            try { close = store.Materializations.CloseOriginalAttachmentReadsAndDrainAsync(); await close.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(close?.Exception ?? error); }
            close = null;
            try { close = store.Provider.CloseOriginalAttachmentReadsAndDrainAsync(); await close.ConfigureAwait(false); }
            catch (Exception error) { errors.Add(close?.Exception ?? error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original Files attachment reads did not close healthy.", errors);
    }
}
