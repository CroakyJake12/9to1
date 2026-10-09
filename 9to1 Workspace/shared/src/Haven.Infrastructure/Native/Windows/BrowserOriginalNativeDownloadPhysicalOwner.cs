using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure.Native.Windows;

// Physical Browser custody only. Neither this object, its paths nor Browser approval
// grants access to a Files workspace. The Files registration owner has separate READ/WRITE.
public sealed partial class BrowserOriginalNativeDownloadPhysicalOwner : IBrowserOriginalNativeDownloadPhysicalOwner, IBrowserOriginalDownloadFilesPhysicalOwner, IAsyncDisposable
{
    private const long MaximumBytes = 250L * 1024 * 1024;
    private readonly string _downloadDirectory;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<IBrowserOriginalNativeDownloadTransportPlan, Plan> _issued = new();
    private readonly ConditionalWeakTable<IBrowserOriginalDownloadContent, Content> _contents = new();
    private readonly List<Plan> _plans = [];
    private readonly List<Invocation> _originals = [];
    private readonly AsyncLocal<Plan?> _executing = new();
    [ThreadStatic] private static Dictionary<Plan, int>? _physicalPlans;
    private IBrowserOriginalNativeDownloadTransportSource? _transport;
    private bool _retiring;
    private Task? _close;

    public BrowserOriginalNativeDownloadPhysicalOwner(string actualDownloadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualDownloadDirectory);
        _downloadDirectory = Path.GetFullPath(actualDownloadDirectory);
    }
    public IBrowserOriginalNativeDownloadTransportSource? OriginalTransportSource
    { get { lock (_gate) return _transport; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    private sealed class Plan(IBrowserOriginalNativeDownloadTransportPlan original)
    {
        internal readonly IBrowserOriginalNativeDownloadTransportPlan Original = original;
        internal BrowserOriginalNativeDownloadPlanDescription Description = null!;
        internal string Principal = null!;
        internal WindowsOriginalRoot? Root;
        internal SafeFileHandle? Parent, File;
        internal FileStream? Stream;
        internal WindowsOriginalFileIdentity Identity;
        internal string? PublishedPath;
        internal Task? Prepared, Close;
        internal Task<IBrowserOriginalDownloadContent>? Finalized;
        internal Content? Content;
        internal bool Retiring;
        internal readonly SemaphoreSlim Reads = new(1, 1);
    }
    private sealed class Invocation
    {
        internal Plan Plan = null!;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
        internal Task Driver = null!;
    }
    private sealed class Content(BrowserOriginalNativeDownloadPhysicalOwner owner, Plan plan,
        BrowserDownloadRecord record, IBrowserOriginalNativeDownloadCompletion originalCompletion) : IBrowserOriginalDownloadContent
    {
        internal readonly BrowserOriginalNativeDownloadPhysicalOwner Owner = owner;
        internal readonly Plan Plan = plan;
        internal readonly IBrowserOriginalNativeDownloadCompletion Completion = originalCompletion;
        public BrowserDownloadRecord OriginalRecord => record;
        public IBrowserOriginalNativeDownloadTransportPlan OriginalPlan => Plan.Original;
        public Task? OriginalClose { get { lock (Owner._gate) return Plan.Close; } }
        public void RequestRetirement() { lock (Owner._gate) Plan.Retiring = true; }
        public void DemandExternalOriginalRetirementJoin() => Owner.DemandExternalPlanJoin(Plan);
        public Task CloseAndDrainAsync() => Owner.ClosePlan(Plan);
    }
    private sealed class OriginalDisposable(IDisposable actual) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task? _close;
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource? receipt;
            lock (_gate)
            {
                if (_close is not null) return new(_close);
                receipt = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = receipt.Task;
            }
            try { actual.Dispose(); receipt.SetResult(); }
            catch (Exception cause) { receipt.SetException(cause); }
            return new(receipt.Task);
        }
    }

    public void BindOriginalTransportSource(IBrowserOriginalNativeDownloadTransportSource sameSource)
    {
        ArgumentNullException.ThrowIfNull(sameSource);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_transport is not null && !ReferenceEquals(_transport, sameSource))
                throw new UnauthorizedAccessException("A physical download owner cannot be rebound to another transport.");
            _transport = sameSource;
        }
    }

    public Task PrepareOriginalTransportPlanWithinSourceAsync(IBrowserOriginalNativeDownloadTransportPlan originalPlan,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalPlan);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (!_issued.TryGetValue(originalPlan, out var plan))
            {
                _plans.RemoveAll(value => value.Close?.IsCompletedSuccessfully == true);
                if (_plans.Count >= 64) throw new InvalidOperationException("Retire existing original download custody before another transfer.");
                plan = new(originalPlan); _issued.Add(originalPlan, plan); _plans.Add(plan);
            }
            return plan.Prepared ??= Admit(plan, scope, retain, async source =>
            {
                source.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original native download custody requires Windows.");
                    WindowsOriginalFileCustody.RequireCapabilities();
                    var transport = _transport ?? throw new InvalidOperationException("Bind the SAME original Browser transport first.");
                    if (!transport.IsIssuedOriginalTransportPlan(originalPlan))
                        throw new UnauthorizedAccessException("The actual configured transport did not issue this plan.");
                    plan.Description = transport.GetOriginalTransportPlanDescription(originalPlan);
                    ValidateDescription(plan.Description);
                    plan.Principal = WindowsOriginalFileCustody.CurrentSid();
                    plan.Root = WindowsOriginalFileCustody.RetainRoot(_downloadDirectory);
                    source.CaptureOriginalResource(new OriginalDisposable(plan.Root));
                    plan.Parent = plan.Root.OpenDirectory(_downloadDirectory, mutable: true);
                    source.CaptureOriginalResource(new OriginalDisposable(plan.Parent));
                    DemandParent(plan);
                });
                await Task.CompletedTask.ConfigureAwait(false);
                return 0;
            });
        }
    }

    public Task<IBrowserOriginalDownloadContent> FinalizeOriginalDownloadWithinSourceAsync(
        IBrowserOriginalNativeDownloadTransportPlan originalPlan,
        IBrowserOriginalNativeDownloadCompletionSource sameCompletionSource,
        IBrowserOriginalNativeDownloadCompletion originalCompletion, string? contentType,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        var plan = RequirePlan(originalPlan);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || plan.Retiring, this);
            return plan.Finalized ??= Admit<IBrowserOriginalDownloadContent>(plan, scope, retain, async source =>
            {
                await source.Read(() => plan.Prepared ?? throw new InvalidOperationException("The actual retained parent preparation is missing.")).ConfigureAwait(false);
                await RevalidateCompletion(plan, sameCompletionSource, originalCompletion, source, cancellationToken).ConfigureAwait(false);
                source.Run(() =>
                {
                    DemandParent(plan); sameCompletionSource.DemandOriginalCompletedDownload(originalCompletion);
                    plan.File = WindowsOriginalFileCustody.OpenRelative(plan.Parent!, Path.GetFileName(plan.Description.PartialPath),
                        WindowsOriginalFileCustody.FileRead | WindowsOriginalFileCustody.Delete, 1,
                        WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);
                    source.CaptureOriginalResource(new OriginalDisposable(plan.File));
                    WindowsOriginalFileCustody.DemandPath(plan.File, plan.Description.PartialPath, false);
                    WindowsOriginalFileCustody.DemandOwner(plan.File, plan.Principal);
                    plan.Identity = WindowsOriginalFileCustody.ReadIdentity(plan.File);
                    if (!plan.Identity.IsRegular || plan.Identity.Links != 1 || plan.Identity.Size > MaximumBytes)
                        throw new UnauthorizedAccessException("The original native partial must be a bounded regular file with one link.");
                    plan.Stream = new FileStream(plan.File, FileAccess.Read, 64 * 1024, isAsync: false);
                    source.CaptureOriginalResource(plan.Stream);
                    DemandPinned(plan, published: false);
                });
                IncrementalHash hash = null!; OriginalDisposable hashClose = null!;
                source.Run(() =>
                {
                    hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    hashClose = new OriginalDisposable(hash); source.CaptureOriginalResource(hashClose);
                });
                var bytes = new byte[64 * 1024]; long length = 0;
                while (true)
                {
                    var read = await source.Read(() => plan.Stream!.ReadAsync(bytes.AsMemory(), cancellationToken).AsTask()).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                    source.Run(() =>
                    {
                        if (length > MaximumBytes) throw new InvalidOperationException("The native download exceeds the maintained 250 MB limit.");
                        hash.AppendData(bytes, 0, read);
                    });
                }
                string sha256 = source.Invoke(() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
                await source.CloseAsync(hashClose).ConfigureAwait(false);
                await RevalidateCompletion(plan, sameCompletionSource, originalCompletion, source, cancellationToken).ConfigureAwait(false);
                source.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested(); DemandPinned(plan, published: false);
                    if (length != (long)plan.Identity.Size) throw new IOException("The actual held native content length changed.");
                    sameCompletionSource.DemandOriginalCompletedDownload(originalCompletion);
                    var leaf = Path.GetFileName(plan.Description.FinalPath);
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            WindowsOriginalFileCustody.PublishCreateOnly(plan.File!, plan.Parent!, leaf);
                            plan.PublishedPath = Path.Combine(_downloadDirectory, leaf); break;
                        }
                        catch (Exception cause) when (attempt < 16 && WindowsOriginalFileCustody.IsOriginalNameCollision(cause))
                        {
                            leaf = Path.GetFileNameWithoutExtension(plan.Description.FileName) + "-" + Guid.NewGuid().ToString("N") +
                                Path.GetExtension(plan.Description.FileName);
                            if (!WindowsOriginalFileCustody.IsSafeLeaf(leaf)) throw;
                        }
                    }
                    WindowsOriginalFileCustody.Flush(plan.Parent!);
                    var afterPromotion = WindowsOriginalFileCustody.ReadIdentity(plan.File!);
                    if (!plan.Identity.SameFile(afterPromotion) || afterPromotion.Size != plan.Identity.Size ||
                        afterPromotion.LastWrite != plan.Identity.LastWrite || !afterPromotion.IsRegular || afterPromotion.Links != 1)
                        throw new IOException("Preserve the unknown original native promotion outcome.");
                    plan.Identity = afterPromotion; DemandPinned(plan, published: true);
                });
                await RevalidateCompletion(plan, sameCompletionSource, originalCompletion, source, cancellationToken).ConfigureAwait(false);
                return source.Invoke<IBrowserOriginalDownloadContent>(() =>
                {
                    DemandPinned(plan, published: true);
                    var record = new BrowserDownloadRecord(Guid.NewGuid(), plan.Description.ActionId,
                        plan.Description.RecordAddress, Path.GetFileName(plan.PublishedPath!), plan.PublishedPath!, length, sha256,
                        string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(), DateTimeOffset.UtcNow);
                    var content = new Content(this, plan, record, originalCompletion);
                    plan.Content = content; _contents.Add(content, content);
                    return content;
                });
            });
        }
    }

    private async Task RevalidateCompletion(Plan plan, IBrowserOriginalNativeDownloadCompletionSource issuer,
        IBrowserOriginalNativeDownloadCompletion actual, CanonicalSqliteOriginalSourceScope source, CancellationToken token)
    {
        source.Run(() =>
        {
            if (!issuer.IsIssuedOriginalCompletion(actual, plan.Original) || !ReferenceEquals(actual.OriginalPlan, plan.Original) ||
                !actual.OriginalApprovalSource.IsIssuedOriginalApproval(actual.OriginalApproval, actual.OriginalExecution) ||
                !actual.OriginalApprovalSource.IsOriginalExecutionAdmission(actual.OriginalApproval, actual.OriginalExecution) ||
                actual.OriginalApproval.ActionId != plan.Description.ActionId)
                throw new UnauthorizedAccessException("The SAME approved actual native completion and transport plan are required.");
        });
        await source.Read(() => issuer.RevalidateOriginalCompletionWithinSourceAsync(actual, source.Run, source.Retain, token)).ConfigureAwait(false);
        source.Run(() => issuer.DemandOriginalCompletedDownload(actual));
    }

    public bool IsIssuedOriginalContent(IBrowserOriginalDownloadContent originalContent) =>
        _contents.TryGetValue(originalContent, out var actual) && ReferenceEquals(actual, originalContent) &&
        ReferenceEquals(actual.Owner, this);
    private Content RequireContent(IBrowserOriginalDownloadContent content) =>
        IsIssuedOriginalContent(content) ? (Content)content : throw new UnauthorizedAccessException("Use the SAME actual pinned Browser content source.");
    private Plan RequirePlan(IBrowserOriginalNativeDownloadTransportPlan plan) =>
        _issued.TryGetValue(plan, out var actual) ? actual : throw new UnauthorizedAccessException("The actual Browser physical owner did not prepare this plan.");

    public Task WaitOriginalApprovedContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = RequireContent(originalContent);
        return Admit(actual.Plan, scope, retain, async source =>
        {
            var completion = actual.Completion; var issuer = completion.OriginalApprovalSource;
            await source.Read(() => issuer.WaitOriginalApprovalSettlementWithinSourceAsync(completion.OriginalApproval,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() =>
            {
                var execute = issuer.GetOriginalExecutionTask(completion.OriginalApproval);
                if (execute is null || !issuer.IsOriginalApprovedCompletion(completion.OriginalApproval,
                    completion.OriginalExecution, execute, actual.OriginalRecord))
                    throw new UnauthorizedAccessException("The SAME original native execution and approval did not settle successfully.");
                DemandPinned(actual.Plan, published: true);
            });
            return 0;
        });
    }

    public Task RevalidateOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        var actual = RequireContent(originalContent);
        return Admit(actual.Plan, scope, retain, source =>
        {
            source.Run(() => { cancellationToken.ThrowIfCancellationRequested(); DemandPinned(actual.Plan, published: true); });
            return Task.FromResult(0);
        });
    }

    public Task CopyOriginalContentWithinSourceAsync(IBrowserOriginalDownloadContent originalContent,
        Stream actualDestination, Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken)
    {
        var actual = RequireContent(originalContent); ArgumentNullException.ThrowIfNull(actualDestination);
        return Admit(actual.Plan, scope, retain, async source =>
        {
            Task? wait = null; var entered = false; var admissionErrors = new List<Exception>();
            try
            {
                try { source.Run(() => { wait = actual.Plan.Reads.WaitAsync(cancellationToken); source.Retain(wait); }); }
                catch (Exception error) { admissionErrors.Add(error); }
                if (wait is not null)
                    try { await wait.ConfigureAwait(false); entered = true; }
                    catch (Exception error) { CanonicalSqliteOriginalStoreOwner.Capture(admissionErrors, wait, error); }
                CanonicalSqliteOriginalStoreOwner.Throw(admissionErrors);
                source.Run(() =>
                {
                    if (!actualDestination.CanWrite) throw new InvalidOperationException("The actual registration destination is not writable.");
                    DemandPinned(actual.Plan, published: true); actual.Plan.Stream!.Position = 0;
                });
                var bytes = new byte[64 * 1024]; long copied = 0;
                IncrementalHash hash = null!; OriginalDisposable hashClose = null!;
                source.Run(() =>
                {
                    hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    hashClose = new OriginalDisposable(hash); source.CaptureOriginalResource(hashClose);
                });
                while (true)
                {
                    var read = await source.Read(() => actual.Plan.Stream!.ReadAsync(bytes.AsMemory(), cancellationToken).AsTask()).ConfigureAwait(false);
                    if (read == 0) break;
                    copied += read;
                    source.Run(() => { if (copied > actual.OriginalRecord.SizeBytes) throw new IOException("The held source content grew."); hash.AppendData(bytes, 0, read); });
                    await source.Read(() => actualDestination.WriteAsync(bytes.AsMemory(0, read), cancellationToken).AsTask()).ConfigureAwait(false);
                }
                var digest = source.Invoke(() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
                await source.CloseAsync(hashClose).ConfigureAwait(false);
                source.Run(() =>
                {
                    DemandPinned(actual.Plan, published: true);
                    if (copied != actual.OriginalRecord.SizeBytes || digest != actual.OriginalRecord.Sha256)
                        throw new IOException("The actual pinned Browser content no longer matches its original result.");
                });
                return 0;
            }
            finally { if (entered) actual.Plan.Reads.Release(); }
        });
    }

    private void ValidateDescription(BrowserOriginalNativeDownloadPlanDescription actual)
    {
        if (actual.ActionId == Guid.Empty || Path.GetFullPath(actual.DownloadDirectory) != _downloadDirectory ||
            Path.GetDirectoryName(Path.GetFullPath(actual.FinalPath)) != _downloadDirectory ||
            Path.GetDirectoryName(Path.GetFullPath(actual.PartialPath)) != _downloadDirectory ||
            Path.GetFileName(actual.FinalPath) != actual.FileName ||
            !WindowsOriginalFileCustody.IsSafeLeaf(actual.FileName) ||
            !WindowsOriginalFileCustody.IsSafeLeaf(Path.GetFileName(actual.PartialPath)) ||
            !actual.PartialPath.StartsWith(actual.FinalPath + ".haven-download-", StringComparison.OrdinalIgnoreCase) ||
            !actual.PartialPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The actual original Browser plan escaped its configured parent or partial namespace.");
    }
    private void DemandParent(Plan plan)
    {
        WindowsOriginalFileCustody.DemandCurrentSid(plan.Principal);
        plan.Root!.DemandCurrent();
        WindowsOriginalFileCustody.DemandPath(plan.Parent!, _downloadDirectory, true);
        WindowsOriginalFileCustody.DemandOwner(plan.Parent!, plan.Principal);
    }
    private void DemandPinned(Plan plan, bool published)
    {
        DemandParent(plan);
        var actual = WindowsOriginalFileCustody.ReadIdentity(plan.File!);
        if (!plan.Identity.SameReadVersion(actual)) throw new IOException("The SAME held original native content changed.");
        WindowsOriginalFileCustody.DemandPath(plan.File!, published ? plan.PublishedPath! : plan.Description.PartialPath, false);
        WindowsOriginalFileCustody.DemandOwner(plan.File!, plan.Principal);
    }

    private Task<T> Admit<T>(Plan plan, Action<Action> caller, Action<Task> retain,
        Func<CanonicalSqliteOriginalSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retain);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new Invocation { Plan = plan };
        original.Source = new(this, action => InvokePlan(plan, () => caller(action)), retain);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || plan.Retiring, this);
            _originals.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Source.IsHealthySettled);
            if (_originals.Count >= 256) throw new InvalidOperationException("Retire unresolved Browser native original custody.");
            var driver = Run(); original.Driver = driver; _originals.Add(original); start.SetResult(); return driver;
        }
        async Task<T> Run()
        {
            await start.Task.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = plan;
            try { return await body(original.Source).ConfigureAwait(false); }
            finally { _executing.Value = previous; }
        }
    }
    private static void InvokePlan(Plan plan, Action body)
    {
        var physical = _physicalPlans ??= new(ReferenceEqualityComparer.Instance);
        physical.TryGetValue(plan, out var depth); physical[plan] = depth + 1;
        try { body(); }
        finally { if (depth == 0) physical.Remove(plan); else physical[plan] = depth; }
    }
    private void DemandExternalPlanJoin(Plan plan)
    {
        if (ReferenceEquals(_executing.Value, plan) || _physicalPlans?.ContainsKey(plan) == true)
            throw new InvalidOperationException("The actual native content cannot join its own original close.");
    }
    private Task ClosePlan(Plan plan)
    {
        DemandExternalPlanJoin(plan);
        lock (_gate)
        {
            plan.Retiring = true;
            if (plan.Close is not null) return plan.Close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            plan.Close = DrainPlan(plan, start.Task); start.SetResult(); return plan.Close;
        }
    }
    private async Task DrainPlan(Plan plan, Task start)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = plan;
        try
        {
            Invocation[] originals; lock (_gate) originals = _originals.Where(value => ReferenceEquals(value.Plan, plan)).ToArray();
            var errors = new List<Exception>();
            foreach (var original in originals)
                try { await original.Driver.ConfigureAwait(false); }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, original.Driver, cause); }
            foreach (var original in originals.Reverse())
            {
                try { await original.Source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await original.Source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await original.Source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
        }
        finally { _executing.Value = previous; }
    }
    public void RequestRetirement() { lock (_gate) { _retiring = true; foreach (var plan in _plans) plan.Retiring = true; } }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_executing.Value is not null || _executingFilesPin.Value is not null || CanonicalSqliteOriginalSourceScope.IsPhysicalSource(this))
            throw new InvalidOperationException("The actual native download owner cannot join its own source/close.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            RequestRetirement(); if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task Drain(Task start)
    {
        await start.ConfigureAwait(false); Plan[] plans; FilesPin[] destinations; lock (_gate) { plans = _plans.ToArray(); destinations = _filesPins.ToArray(); } var errors = new List<Exception>();
        foreach (var destination in destinations)
            try { await CloseFilesPin(destination).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        foreach (var plan in plans)
            try { await ClosePlan(plan).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
