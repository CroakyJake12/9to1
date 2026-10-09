using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

/// <summary>Optional READ operation on the SAME configured authenticated root endpoint.
/// The endpoint independently authenticates its existing installed Home control session,
/// actual OS principal and enrolled publisher policy. It verifies the signed catalogue,
/// receipt and complete activation before returning a channel. This operation acquires
/// no install approval, capability, mutation or replay of an old installer operation.</summary>
public interface IHomePackageOriginalRootActivationPort
{
    Task<IHomePackageOriginalRootActivation?> OpenOriginalActivationAsync(
        HomePackageOriginalReadActivationRequest originalRequest, CancellationToken cancellationToken);
}

/// <summary>Device-issued selection, never an authentication or installation grant.</summary>
public sealed class HomePackageOriginalReadActivationRequest
{
    private readonly HomePackageOriginalDeviceOwner _issuer;
    internal HomePackageOriginalReadActivationRequest(HomePackageOriginalDeviceOwner issuer,
        AuthenticatedResourceActor actor, string osPrincipal, string appId)
    { _issuer = issuer; Actor = actor; OsPrincipal = osPrincipal; AppId = appId; }
    public AuthenticatedResourceActor Actor { get; }
    public string OsPrincipal { get; }
    public string AppId { get; }
    public bool IsIssuedBy(HomePackageOriginalDeviceOwner sameDevice) => ReferenceEquals(_issuer, sameDevice);
}

/// <summary>A genuinely authenticated READ channel. Public fields remain observations.
/// Its SAME artifact provider must recognize OriginalArtifact, and its currentness
/// check must verify the actual root/installed Home session and publisher enrollment.
/// A closed installation mutation or database row cannot implement this evidence.</summary>
public interface IHomePackageOriginalRootActivation : IAsyncDisposable
{
    HomePackageOriginalReadActivationRequest OriginalRequest { get; }
    HomePackageOriginalArtifactObservation OriginalArtifact { get; }
    HomeNativeInstalledPeer OriginalInstalledHomeCaller { get; }
    string OriginalHomeSessionId { get; }
    string OriginalActivationOperationId { get; }
    string OriginalPackageId { get; }
    Task DemandOriginalChannelCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Privately issued channel custody. Only the original endpoint/provider
/// establish trust. Dispose joins the SAME admitted read/currentness/close originals;
/// it never performs an installation, Home grant or root mutation.</summary>
public sealed class HomePackageOriginalReadActivation : IAsyncDisposable
{
    internal readonly HomePackageOriginalDeviceOwner Issuer;
    internal readonly HomePackageOriginalDeviceOwner.ReadActivationEntry Entry;
    internal HomePackageOriginalReadActivation(HomePackageOriginalDeviceOwner issuer,
        HomePackageOriginalDeviceOwner.ReadActivationEntry entry, HomePackageArtifactSelection artifact,
        string operationId)
    { Issuer = issuer; Entry = entry; Selection = artifact; OriginalActivationOperationId = operationId; }
    internal HomePackageArtifactSelection Selection { get; }
    public HomePackageOriginalReadActivationRequest OriginalRequest => Entry.Request;
    public HomePackageArtifactDescriptor Artifact => Selection.Descriptor;
    public string CatalogueRevision => Selection.CatalogueRevision;
    public string OriginalActivationOperationId { get; }
    public ReadOnlyMemory<byte> CopySignedDescriptor() => Selection.CopySignedDescriptor();
    public ReadOnlyMemory<byte> CopyDescriptorPayload() => Selection.CopyDescriptorPayload();
    public Task? OriginalClose { get { lock (Issuer.ReadActivationGate) return Entry.Close; } }
    public Task DemandOriginalCurrentWithinSourceAsync(Action<Action> scope, Action<Task> retain,
        CancellationToken token) => Issuer.DemandReadActivationCurrent(this, scope, retain, token);
    public Task CloseAndDrainAsync() => Issuer.CloseReadActivation(Entry);
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}

public sealed partial class HomePackageOriginalDeviceOwner
{
    internal object ReadActivationGate => _gate;
    internal sealed class ReadActivationEntry(HomePackageOriginalReadActivationRequest request)
    {
        internal readonly HomePackageOriginalReadActivationRequest Request = request;
        internal IHomePackageOriginalRootActivation? Root;
        internal HomePackageOriginalArtifactObservation? Artifact;
        internal HomePackageArtifactSelection? Selection;
        internal HomeNativeInstalledPeer? Caller;
        internal string? Session;
        internal string? ActivationOperationId, PackageId;
        internal Task<HomePackageOriginalReadActivation?> Open = null!;
        internal ActivationSource OpenSource = null!;
        internal readonly List<ActivationSource> Calls = [];
        internal Task? Close, RawClose;
        internal ActivationSource? RawCloseSource;
        internal Task? OriginalRawRootDispose;
        internal bool Sealed;
        internal bool IsHealthyClosed => Close?.IsCompletedSuccessfully == true ||
            Open.IsCompletedSuccessfully && Open.GetAwaiter().GetResult() is null && Root is null && RawClose?.IsCompletedSuccessfully == true &&
            OpenSource.Ledger.OriginalErrors.Count == 0 && OpenSource.Ledger.OriginalTasks.All(value => value.IsCompletedSuccessfully);
    }
    internal sealed class ActivationSource(CloudflareOriginalTaskLedger ledger, Action<Task> retain)
    {
        internal readonly CloudflareOriginalTaskLedger Ledger = ledger; internal readonly Action<Task> Retain = retain;
        internal Task Driver = null!; internal CancellationTokenSource? LinkedLifetime; internal Task? LinkedLifetimeClose;
    }
    private readonly List<ReadActivationEntry> _readActivations = [];
    private readonly ConditionalWeakTable<HomePackageOriginalReadActivation, ReadActivationEntry> _issuedReadActivations = new();

    /// <summary>Fresh READ admission. Absence of the optional real root endpoint returns
    /// unavailable before any root/artifact IO. The actual platform must supply enrollment;
    /// merely implementing an interface or passing a principal string supplies no trust.</summary>
    public Task<HomePackageOriginalReadActivation?> OpenOriginalReadActivationWithinSourceAsync(
        AuthenticatedResourceActor actor, string observedOsPrincipal, string canonicalAppId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!HomePackageArtifactSelection.Identifier(canonicalAppId) || string.IsNullOrWhiteSpace(observedOsPrincipal) ||
            observedOsPrincipal.Length > 256) throw new ArgumentException("Actual bounded OS principal/app selection required.");
        var source = CreateActivationSource(scope, retain); ReadActivationEntry entry;
        TaskCompletionSource start;
        lock (_gate)
        {
            foreach (var healthy in _readActivations.Where(value => value.IsHealthyClosed).ToArray())
            {
                _readActivations.Remove(healthy); _originalTasks.Remove(healthy.Open);
                foreach (var call in healthy.Calls) _originalTasks.Remove(call.Driver);
            }
            RequireAdmission();
            if (_readActivations.Count >= 128) throw new InvalidOperationException("Retain all unresolved original activation channels.");
            entry = new(new(this, actor, observedOsPrincipal, canonicalAppId)) { OpenSource = source };
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Open = OpenReadActivation(entry, source, start.Task, token); source.Driver = entry.Open;
            _readActivations.Add(entry); _originalTasks.Add(entry.Open);
        }
        PublishActivation(source, entry.Open, retain, start); return entry.Open;
    }
    private ActivationSource CreateActivationSource(Action<Action> scope, Action<Task> retain)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var ledger = new CloudflareOriginalTaskLedger(); ledger.BindOriginalOwner(this);
        ledger.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            var active = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
            try
            {
                scope(() =>
                {
                    try
                    {
                        if (Volatile.Read(ref active) != 1 || thread != Environment.CurrentManagedThreadId ||
                            Interlocked.Exchange(ref used, 1) != 0) throw new InvalidOperationException("Actual activation callback is inactive, foreign-thread or consumed.");
                        if (ledger.OriginalErrors.Count != 0) throw new AggregateException("Prior activation source failed.", ledger.OriginalErrors);
                        body();
                    }
                    catch (Exception cause) { ledger.Retain(cause); throw; }
                });
                if (used != 1) throw new InvalidOperationException("Actual activation callback was omitted.");
            }
            finally { Interlocked.Exchange(ref active, 0); }
            return true;
        }));
        return new(ledger, retain);
    }
    private static void PublishActivation(ActivationSource source, Task actual, Action<Task> retain, TaskCompletionSource start)
    {
        try { source.Ledger.Invoke(() => { retain(actual); return true; }); }
        catch (Exception cause) { source.Ledger.Retain(cause); }
        finally { start.SetResult(); }
    }
    private async Task<HomePackageOriginalReadActivation?> OpenReadActivation(ReadActivationEntry entry,
        ActivationSource source, Task start, CancellationToken token)
    {
        await start.ConfigureAwait(false); using var physical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        HomePackageOriginalReadActivation? result = null;
        try
        {
            token = CreateReadActivationToken(source, token); token.ThrowIfCancellationRequested();
            if (_root is IHomePackageOriginalRootActivationPort endpoint)
            {
                entry.Root = await ReadActivationStage(source, () => endpoint.OpenOriginalActivationAsync(entry.Request, token),
                    actual => entry.Root = actual).ConfigureAwait(false);
                if (entry.Root is not null)
                {
                    source.Ledger.Invoke(() =>
                    {
                        if (!ReferenceEquals(entry.Root.OriginalRequest, entry.Request)) throw new UnauthorizedAccessException("SAME privately issued root READ request required.");
                        entry.Artifact = entry.Root.OriginalArtifact; entry.Caller = entry.Root.OriginalInstalledHomeCaller;
                        entry.Session = entry.Root.OriginalHomeSessionId;
                        entry.ActivationOperationId = entry.Root.OriginalActivationOperationId; entry.PackageId = entry.Root.OriginalPackageId;
                        if (entry.Artifact is null || entry.Caller is null || !HomePackageArtifactSelection.Text(entry.Session, 1024))
                            throw new UnauthorizedAccessException("Actual root artifact and installed Home session are unavailable.");
                        var material = new HomePackageOriginalArtifactMaterial(entry.Artifact.SignedDescriptorBytes.ToArray(),
                            entry.Artifact.DescriptorPayloadBytes.ToArray(), entry.Artifact.CatalogueRevision, entry.Artifact.OriginalProviderEvidence);
                        var selection = HomePackageArtifactSelection.Capture(this, material, new(entry.PackageId!,
                            HomePackageAction.Launch, "readonly-observation")); // Parse only; no Launch action is admitted or dispatched.
                        if (selection.Descriptor.AppId != entry.Request.AppId || !HomePackageArtifactSelection.Text(entry.ActivationOperationId, 256))
                            throw new UnauthorizedAccessException("Actual root READ selected another app or unsupported activation identity.");
                        entry.Selection = selection; result = new(this, entry, selection, entry.ActivationOperationId!); return true;
                    });
                    await DemandReadActivationRaw(entry, source, token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception cause) { source.Ledger.Retain(cause); }
        await source.Ledger.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        CloseReadActivationToken(source);
        await source.Ledger.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (source.Ledger.OriginalErrors.Count != 0)
        {
            try { await CloseReadActivationRaw(entry).ConfigureAwait(false); } catch (Exception cause) { source.Ledger.Retain(cause); }
            throw new AggregateException("Actual activation READ acquisition failed; all original sources/resources are retained.", source.Ledger.OriginalErrors);
        }
        if (result is not null) lock (_gate) _issuedReadActivations.Add(result, entry);
        else await CloseReadActivationRaw(entry).ConfigureAwait(false);
        return result;
    }
    private CancellationToken CreateReadActivationToken(ActivationSource source, CancellationToken caller)
        => source.Ledger.Invoke(() =>
        {
            source.LinkedLifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
            return source.LinkedLifetime.Token;
        });
    private void CloseReadActivationToken(ActivationSource source)
    {
        if (source.LinkedLifetimeClose is not null) return;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.LinkedLifetimeClose = settled.Task; source.Ledger.Track(settled.Task);
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { source.LinkedLifetime?.Dispose(); return true; }); settled.SetResult(); }
        catch (Exception cause) { source.Ledger.Retain(cause); settled.SetException(cause); }
    }
    private static async Task<T> ReadActivationStage<T>(ActivationSource source, Func<Task<T>> factory, Action<T>? capture = null)
    {
        Task<T>? raw = null;
        try { source.Ledger.Invoke(() => { raw = factory(); source.Ledger.Track(raw); source.Retain(raw); return true; }); }
        catch (Exception cause) { source.Ledger.Retain(cause); }
        T value = default!;
        if (raw is not null)
            try { value = await source.Ledger.AwaitAsync(raw).ConfigureAwait(false); capture?.Invoke(value); }
            catch (Exception cause) { source.Ledger.Capture(raw, cause); }
        if (source.Ledger.OriginalErrors.Count != 0) throw new AggregateException("Actual activation source failed.", source.Ledger.OriginalErrors);
        return raw is null ? throw new InvalidOperationException("No actual activation source task returned.") : value;
    }
    private async Task DemandReadActivationRaw(ReadActivationEntry entry, ActivationSource source, CancellationToken token)
    {
        await ReadActivationStage(source, async () =>
        {
            Task? raw = null;
            source.Ledger.Invoke(() => { raw = entry.Root!.DemandOriginalChannelCurrentAsync(token); source.Ledger.Track(raw); source.Retain(raw); return true; });
            await source.Ledger.AwaitAsync(raw!).ConfigureAwait(false); return true;
        }).ConfigureAwait(false);
        await ReadActivationStage(source, async () =>
        {
            Task? raw = null;
            source.Ledger.Invoke(() => { raw = _artifacts.DemandOriginalCurrentAsync(entry.Artifact!, entry.Request.Actor,
                entry.Caller!, entry.Session!, token); source.Ledger.Track(raw); source.Retain(raw); return true; });
            await source.Ledger.AwaitAsync(raw!).ConfigureAwait(false); return true;
        }).ConfigureAwait(false);
        source.Ledger.Invoke(() =>
        {
            if (!ReferenceEquals(entry.Root!.OriginalRequest, entry.Request) ||
                !ReferenceEquals(entry.Root.OriginalArtifact, entry.Artifact) ||
                entry.Root.OriginalInstalledHomeCaller != entry.Caller || entry.Root.OriginalHomeSessionId != entry.Session ||
                entry.Root.OriginalActivationOperationId != entry.ActivationOperationId || entry.Root.OriginalPackageId != entry.PackageId ||
                entry.PackageId != entry.Selection!.Descriptor.PackageId ||
                entry.Artifact!.CatalogueRevision != entry.Selection!.CatalogueRevision ||
                HomePackageArtifactSelection.Digest(entry.Artifact.SignedDescriptorBytes.Span) != entry.Selection.SignedDescriptorSha256 ||
                HomePackageArtifactSelection.Digest(entry.Artifact.DescriptorPayloadBytes.Span) != entry.Selection.DescriptorPayloadSha256)
                throw new UnauthorizedAccessException("Actual root READ artifact/session changed during verification.");
            return true;
        });
    }
    internal Task DemandReadActivationCurrent(HomePackageOriginalReadActivation same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = CreateActivationSource(scope, retain); TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            RequireAdmission();
            if (!ReferenceEquals(same.Issuer, this) || !_issuedReadActivations.TryGetValue(same, out var entry) ||
                !ReferenceEquals(entry, same.Entry) || entry.Sealed || entry.Open.IsCompletedSuccessfully != true || entry.Root is null)
                throw new UnauthorizedAccessException("Actual live issued root READ channel required.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DemandReadPublished(entry, source, start.Task, token); source.Driver = actual;
            entry.Calls.Add(source); _originalTasks.Add(actual);
        }
        PublishActivation(source, actual, retain, start); return actual;
    }
    private async Task DemandReadPublished(ReadActivationEntry entry, ActivationSource source, Task start, CancellationToken token)
    {
        await start.ConfigureAwait(false); using var physical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { token = CreateReadActivationToken(source, token); await DemandReadActivationRaw(entry, source, token).ConfigureAwait(false); }
        catch (Exception cause) { source.Ledger.Retain(cause); }
        await source.Ledger.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        CloseReadActivationToken(source);
        await source.Ledger.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (source.Ledger.OriginalErrors.Count != 0) throw new AggregateException("Actual activation currentness failed.", source.Ledger.OriginalErrors);
    }
    internal Task CloseReadActivation(ReadActivationEntry entry)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this); TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (entry.Close is null)
            {
                entry.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                entry.Close = CloseReadPublished(entry, start.Task, entry.Calls.ToArray());
            }
            actual = entry.Close;
        }
        start?.SetResult(); return actual;
    }
    private async Task CloseReadPublished(ReadActivationEntry entry, Task start, ActivationSource[] calls)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { await entry.Open.ConfigureAwait(false); } catch (Exception cause) { errors.Add(entry.Open.Exception ?? cause); }
        foreach (var call in calls) try { await call.Driver.ConfigureAwait(false); } catch (Exception cause) { errors.Add(call.Driver.Exception ?? cause); }
        Task? raw = null;
        try { raw = CloseReadActivationRaw(entry); await raw.ConfigureAwait(false); } catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual root READ retirement failed; original channel retained.", errors);
    }
    private Task CloseReadActivationRaw(ReadActivationEntry entry)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (entry.RawClose is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var ledger = new CloudflareOriginalTaskLedger(); ledger.BindOriginalOwner(this);
                entry.RawCloseSource = new(ledger, _ => { });
                entry.RawClose = CloseRawPublished(entry, entry.RawCloseSource, start.Task); entry.RawCloseSource.Driver = entry.RawClose; }
            actual = entry.RawClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task CloseRawPublished(ReadActivationEntry entry, ActivationSource source, Task start)
    {
        await start.ConfigureAwait(false); using var physical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var ledger = source.Ledger;
        Task? raw = null;
        try { ledger.Invoke(() => { if (entry.Root is not null) { raw = entry.Root.DisposeAsync().AsTask(); entry.OriginalRawRootDispose = raw; ledger.Track(raw); } return true; }); }
        catch (Exception cause) { ledger.Retain(cause); }
        if (raw is not null) try { await ledger.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { ledger.Capture(raw, cause); }
        await ledger.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (ledger.OriginalErrors.Count != 0) throw new AggregateException("Actual root READ close failed; channel/raw close retained.", ledger.OriginalErrors);
    }
    private async Task JoinOriginalReadActivationsAsync()
    {
        ReadActivationEntry[] entries; lock (_gate) entries = _readActivations.ToArray();
        var errors = new List<Exception>();
        foreach (var entry in entries)
        {
            Task? actual = null;
            try { actual = CloseReadActivation(entry); await actual.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(actual?.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("All actual root READ channels remain retained on failed retirement.", errors);
    }
}
