using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeWindowsCoreClient
{
    private bool _originalScoped;
    private HomeNativeOriginalStartupScope? _originalCleanup, _originalResourceCleanup;
    private ScopedClientAttachment? _originalAttachment;
    private Task? _originalResourceClose;
    private readonly List<Exception> _originalCallbackErrors = [];
    private void KeepOriginalCallbackError(Exception cause)
    { lock (_sync) if (!_originalCallbackErrors.Any(known => ReferenceEquals(known, cause))) _originalCallbackErrors.Add(cause); }
    private void ThrowOriginalCallbackErrors()
    {
        Exception[] all; lock (_sync) all = _originalCallbackErrors.ToArray();
        if (all.Length != 0) throw new AggregateException("Original native client callback did not settle.", all);
    }
    private readonly List<ScopedClientOperation> _originalOperations = [];
    private sealed class ScopedClientOperation
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal CancellationTokenSource? Active;
        internal Task? ActiveClose;
        internal ScopedClientOperation(HomeWindowsCoreClient owner, Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => owner, scope, retain, owner.KeepOriginalCallbackError);
    }
    private sealed class ScopedClientAttachment
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal CancellationTokenSource? Deadline;
        internal HomeWindowsCoreClient? Client;
        internal Task? DeadlineClose, ClientClose;
        internal ScopedClientAttachment(Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => Client, scope, retain, cause => Client?.KeepOriginalCallbackError(cause));
    }
    private static readonly object OriginalAttachmentGate = new();
    private static readonly List<ScopedClientAttachment> OriginalAttachments = [];

    internal static Task<HomeWindowsCoreClient?> AttachWithinOriginalSourceAsync(NamedPipeClientStream pipe,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHost,
        CancellationToken lifetime, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var work = new ScopedClientAttachment(scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HomeWindowsCoreClient?> actual;
        lock (OriginalAttachmentGate)
        {
            if (OriginalAttachments.Count >= 128) throw new InvalidOperationException("Unconfirmed original native client attachments remain retained.");
            actual = Drive(); work.Driver = actual; OriginalAttachments.Add(work);
        }
        work.Source.Publish(actual); begin.SetResult(); return actual;
        async Task<HomeWindowsCoreClient?> Drive()
        {
            await begin.Task.ConfigureAwait(false);
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(work);
            var source = work.Source; var retained = false;
            try
            {
                source.Throw(); ArgumentNullException.ThrowIfNull(pipe); ArgumentNullException.ThrowIfNull(verifier);
                token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows() || !pipe.IsConnected || !lifetime.CanBeCanceled || lifetime.IsCancellationRequested ||
                    trustedHost is null || !Text(trustedHost.AppId, 128) || !Text(trustedHost.OperatingSystemApplicationId, 4096))
                    throw new UnauthorizedAccessException("An actual connected Windows host and owning lifetime are required.");
                if (verifier is not IHomeOriginalScopedNativeSessionHostVerifier scopedVerifier)
                    throw new UnauthorizedAccessException("The actual host verifier must retain its original scoped sources.");
                var requirement = trustedHost with { };
                source.Run(() =>
                {
                    work.Deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime, token);
                    work.Deadline.CancelAfter(TimeSpan.FromSeconds(5));
                });
                var observed = await source.Read(() => HomeNativeWindowsHostObservation.FromConnectedPipeWithinOriginalSourceAsync(pipe, source)).ConfigureAwait(false);
                if (observed is null) throw new UnauthorizedAccessException("The actual connected Windows host could not be observed.");
                var actualHost = await source.Read(() => scopedVerifier.VerifyHostWithinOriginalSourceAsync(
                    observed, requirement, source.Run, source.Retain, work.Deadline!.Token)).ConfigureAwait(false);
                var host = source.Invoke(() => Capture(actualHost, requirement));
                if (host is null) throw new UnauthorizedAccessException("The actual installed Windows host did not authenticate.");
                source.Run(() => { work.Client = new(pipe, verifier, requirement, observed, host, lifetime) { _originalScoped = true, _originalAttachment = work }; });
                await work.Client!.DemandHostWithinOriginalSourceAsync(source, work.Deadline!.Token).ConfigureAwait(false);
                source.Run(() => work.Deadline!.Token.ThrowIfCancellationRequested());
                retained = true;
            }
            catch (Exception cause) { source.Keep(cause); }
            finally
            {
                if (work.Deadline is not null)
                {
                    var same = source.Close(work.Deadline, ref work.DeadlineClose);
                    try { await same.ConfigureAwait(false); } catch (Exception cause) { source.Keep(same.Exception ?? cause); }
                }
                if (!retained || source.Errors.Length != 0)
                    if (work.Client is { } sameClient) await source.Cleanup(() => sameClient.CloseFailedOriginalAttachment(work), raw => work.ClientClose = raw).ConfigureAwait(false);
                await source.JoinAll().ConfigureAwait(false);
            }
            source.Throw();
            lock (OriginalAttachmentGate) OriginalAttachments.Remove(work); // Actual transfer or healthy no-result; failed originals remain rooted.
            return retained ? work.Client : null;
        }
    }

    internal Task<HomeNativeCoreApiResult<HomeCompatibilityResult>> GetCompatibilityWithinOriginalSourceAsync(
        HomeCompatibilityRequest request, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RequestWithinOriginalSourceAsync<HomeCompatibilityResult>("GetCompatibility", null,
            HomeNativeWindowsStartupSession.CaptureOriginalRequirements(request), scope, retain, token);

    private Task<HomeNativeCoreApiResult<T>> RequestWithinOriginalSourceAsync<T>(string operation, string? serviceId,
        HomeCompatibilityRequest? compatibility, Action<Action> scope, Action<Task> retain, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        var request = new HomeUnixCoreRequest(HomeUnixCoreProtocol.Version, Guid.NewGuid().ToString("N"), operation, serviceId, compatibility);
        var payload = HomeUnixCoreProtocol.Payload(request);
        return BeginScopedOperation(source => ExecuteWithinOriginalSourceAsync<T>(source, request, payload, caller), scope, retain);
    }
    internal Task DemandOriginalCurrentWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken caller) =>
        BeginScopedOperation(async work =>
        {
            work.Source.Run(() =>
            {
                work.Active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
                work.Active.CancelAfter(TimeSpan.FromSeconds(5));
            });
            await DemandHostWithinOriginalSourceAsync(work.Source, work.Active!.Token).ConfigureAwait(false); return true;
        }, scope, retain);

    private Task<T> BeginScopedOperation<T>(Func<ScopedClientOperation, Task<T>> body, Action<Action> scope, Action<Task> retain)
    {
        var work = new ScopedClientOperation(this, scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> actual;
        lock (_sync)
        {
            ThrowOriginalCallbackErrors();
            ObjectDisposedException.ThrowIf(_closing || _lifetime.IsCancellationRequested, this);
            if (_pending is { IsCompleted: false }) throw new InvalidOperationException("One original Home pipe operation is already pending.");
            _originalOperations.RemoveAll(old =>
            {
                var same = old.Driver;
                if (!same.IsCompletedSuccessfully) return false;
                same.GetAwaiter().GetResult();
                return old.Source.Errors.Length == 0 && (old.Active is null || old.ActiveClose?.IsCompletedSuccessfully == true);
            });
            if (_originalOperations.Count >= 128) throw new InvalidOperationException("Unconfirmed native client operations remain retained.");
            actual = Drive(); work.Driver = actual; _originalOperations.Add(work); _pending = actual;
        }
        work.Source.Publish(actual); begin.SetResult(); return actual;
        async Task<T> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!;
            try { ThrowOriginalCallbackErrors(); work.Source.Throw(); result = await body(work).ConfigureAwait(false); ThrowOriginalCallbackErrors(); }
            catch (Exception cause)
            {
                work.Source.Keep(cause);
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _lifetime.Cancel(); return true; }); }
                catch (Exception cleanup) { work.Source.Keep(cleanup); }
            }
            finally
            {
                if (work.Active is not null)
                {
                    var same = work.Source.Close(work.Active, ref work.ActiveClose);
                    try { await same.ConfigureAwait(false); } catch (Exception cause) { work.Source.Keep(same.Exception ?? cause); }
                }
                await work.Source.JoinAll().ConfigureAwait(false);
            }
            work.Source.Throw(); return result;
        }
    }
    private async Task<HomeNativeCoreApiResult<T>> ExecuteWithinOriginalSourceAsync<T>(ScopedClientOperation work,
        HomeUnixCoreRequest request, byte[] payload, CancellationToken caller)
    {
        var source = work.Source;
        source.Run(() =>
        {
            work.Active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            work.Active.CancelAfter(TimeSpan.FromSeconds(5));
        });
        var token = work.Active!.Token;
        await DemandHostWithinOriginalSourceAsync(source, token).ConfigureAwait(false);
        await WriteOriginalFrame(source, _pipe, payload, token).ConfigureAwait(false);
        var response = HomeUnixCoreProtocol.ReadResponse(await ReadOriginalFrame(source, _pipe, token).ConfigureAwait(false), request);
        var result = source.Invoke(() => response.Result.Deserialize<HomeNativeCoreApiResult<T>>(HomeUnixCoreProtocol.Json)
            ?? throw new InvalidDataException("Windows Home Core result is absent."));
        if (result.Operation is null) throw new InvalidDataException("Windows Home operation result is absent.");
        await DemandHostWithinOriginalSourceAsync(source, token).ConfigureAwait(false);
        source.Run(token.ThrowIfCancellationRequested); return result;
    }
    private async Task DemandHostWithinOriginalSourceAsync(HomeNativeOriginalStartupScope source, CancellationToken token)
    {
        source.Run(token.ThrowIfCancellationRequested);
        var observed = await source.Read(() => HomeNativeWindowsHostObservation.FromConnectedPipeWithinOriginalSourceAsync(_pipe, source)).ConfigureAwait(false);
        if (observed is null || observed != _observed) throw new UnauthorizedAccessException("Original connected Windows Home peer changed.");
        if (_verifier is not IHomeOriginalScopedNativeSessionHostVerifier verifier)
            throw new UnauthorizedAccessException("The actual host verifier lacks original source custody.");
        var actualCurrent = await source.Read(() => verifier.VerifyHostWithinOriginalSourceAsync(observed, _requirement,
            source.Run, source.Retain, token)).ConfigureAwait(false);
        var current = source.Invoke(() => Capture(actualCurrent, _requirement));
        if (current is null || current.InstalledApplicationId != _host.InstalledApplicationId ||
            current.InstallationRevision != _host.InstallationRevision || current.ExecutableIdentity != _host.ExecutableIdentity ||
            !current.Roles.SetEquals(_host.Roles) || !current.AllowedServiceIds.SetEquals(_host.AllowedServiceIds))
            throw new UnauthorizedAccessException("Original installed Windows Home host is no longer current.");
        var after = await source.Read(() => HomeNativeWindowsHostObservation.FromConnectedPipeWithinOriginalSourceAsync(_pipe, source)).ConfigureAwait(false);
        if (after is null || after != _observed) throw new UnauthorizedAccessException("Original Home pipe peer retired during verification.");
        source.Run(() => { token.ThrowIfCancellationRequested(); _lifetime.Token.ThrowIfCancellationRequested(); });
    }
    private static async Task<byte[]> ReadOriginalFrame(HomeNativeOriginalStartupScope source, Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await source.Read(() => stream.ReadExactlyAsync(header, token).AsTask()).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > 64 * 1024) throw new InvalidDataException("Home discovery frame exceeds the bounded protocol.");
        var payload = new byte[length]; await source.Read(() => stream.ReadExactlyAsync(payload, token).AsTask()).ConfigureAwait(false); return payload;
    }
    private static async Task WriteOriginalFrame(HomeNativeOriginalStartupScope source, Stream stream, byte[] payload, CancellationToken token)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await source.Read(() => stream.WriteAsync(header, token).AsTask()).ConfigureAwait(false);
        await source.Read(() => stream.WriteAsync(payload, token).AsTask()).ConfigureAwait(false);
        await source.Read(() => stream.FlushAsync(token)).ConfigureAwait(false);
    }
    // Only this exact still-live attachment may settle its product resources
    // from inside the product marker. The public close retains its own guard.
    private Task CloseFailedOriginalAttachment(ScopedClientAttachment same)
    {
        if (!ReferenceEquals(_originalAttachment, same) || !ReferenceEquals(same.Client, this) || same.Driver.IsCompleted)
            throw new InvalidOperationException("The actual live original client attachment is required for parent cleanup.");
        return AcquireOriginalResourceClose();
    }
    private Task AcquireOriginalResourceClose()
    {
        lock (_sync)
        {
            if (_originalResourceClose is not null) return _originalResourceClose;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalResourceClose = CloseResources(start.Task); start.SetResult(); return _originalResourceClose;
        }
        async Task CloseResources(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var cleanup = _originalResourceCleanup = new HomeNativeOriginalStartupScope(this, () => null, body => body(), _ => { });
            try { cleanup.Run(_lifetime.Cancel); } catch (Exception cause) { cleanup.Keep(cause); }
            ScopedClientOperation[] all; lock (_sync) all = _originalOperations.ToArray();
            foreach (var operation in all)
            {
                await cleanup.Cleanup(() => operation.Driver).ConfigureAwait(false);
                await operation.Source.JoinAll().ConfigureAwait(false);
                foreach (var cause in operation.Source.Errors) cleanup.Keep(cause);
            }
            Task? actual = null;
            var same = cleanup.Close(_lifetime, ref actual);
            try { await same.ConfigureAwait(false); } catch (Exception cause) { cleanup.Keep(same.Exception ?? cause); }
            await cleanup.JoinAll().ConfigureAwait(false); cleanup.Throw();
        }
    }
    private async Task CloseWithinOriginalSourceAsync(Task start)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var cleanup = _originalCleanup = new HomeNativeOriginalStartupScope(this, () => null, body => body(), _ => { });
        await cleanup.Cleanup(AcquireOriginalResourceClose).ConfigureAwait(false);
        if (_originalAttachment is { } attachment)
        {
            await cleanup.Cleanup(() => attachment.Driver).ConfigureAwait(false);
            await attachment.Source.JoinAll().ConfigureAwait(false);
            foreach (var cause in attachment.Source.Errors) cleanup.Keep(cause);
        }
        Exception[] callbacks; lock (_sync) callbacks = _originalCallbackErrors.ToArray();
        foreach (var cause in callbacks) cleanup.Keep(cause);
        await cleanup.JoinAll().ConfigureAwait(false); cleanup.Throw();
    }
}
