using System.IO.Pipes;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeWindowsStartupSession
{
    private bool _originalScoped;
    private ScopedStartupAttachment? _originalAttachment;
    private Task? _originalResourceClose;
    private HomeNativeOriginalStartupScope? _originalResourceCleanup;
    private readonly List<Exception> _originalCallbackErrors = [];
    private void KeepOriginalCallbackError(Exception cause)
    { lock (_sync) if (!_originalCallbackErrors.Any(known => ReferenceEquals(known, cause))) _originalCallbackErrors.Add(cause); }
    private void ThrowOriginalCallbackErrors()
    {
        Exception[] all; lock (_sync) all = _originalCallbackErrors.ToArray();
        if (all.Length != 0) throw new AggregateException("Original native startup callback did not settle.", all);
    }
    private HomeNativeWindowsAppConnection? _originalConnectionOwner;
    internal void BindOriginalConnectionOwner(HomeNativeWindowsAppConnection actual)
    {
        lock (_sync)
        {
            if (_originalConnectionOwner is not null && !ReferenceEquals(_originalConnectionOwner, actual))
                throw new InvalidOperationException("The original startup connection owner changed.");
            _originalConnectionOwner = actual;
        }
    }
    private readonly List<ScopedStartupCheck> _originalChecks = [];
    private HomeNativeOriginalStartupScope? _originalCleanup;
    private sealed class ScopedStartupCheck
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal ScopedStartupCheck(HomeNativeWindowsStartupSession owner, Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => owner, scope, retain, owner.KeepOriginalCallbackError);
    }
    private sealed class ScopedStartupAttachment
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal HomeWindowsCoreClient? Client;
        internal HomeNativeWindowsStartupSession? Session;
        internal Task? ClientClose, SessionClose;
        internal ScopedStartupAttachment(Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => Session, scope, retain, cause => Session?.KeepOriginalCallbackError(cause));
    }
    private static readonly object OriginalStartupAttachmentsGate = new();
    private static readonly List<ScopedStartupAttachment> OriginalStartupAttachments = [];

    internal static Task<HomeNativeWindowsStartupSession?> AttachWithinOriginalSourceAsync(NamedPipeClientStream pipe,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHost,
        HomeCompatibilityRequest requirements, CancellationToken lifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var request = CaptureOriginalRequirements(requirements);
        var work = new ScopedStartupAttachment(scope, retain);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<HomeNativeWindowsStartupSession?> actual;
        lock (OriginalStartupAttachmentsGate)
        {
            if (OriginalStartupAttachments.Count >= 128) throw new InvalidOperationException("Unconfirmed original Home session attachments remain retained.");
            actual = Drive(); work.Driver = actual; OriginalStartupAttachments.Add(work);
        }
        work.Source.Publish(actual); start.SetResult(); return actual;
        async Task<HomeNativeWindowsStartupSession?> Drive()
        {
            await start.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(work);
            var source = work.Source;
            try
            {
                source.Throw();
                await source.Read(() => HomeWindowsCoreClient.AttachWithinOriginalSourceAsync(pipe, verifier, trustedHost,
                    lifetime, source.Run, source.Retain, token), actualClient => work.Client = actualClient).ConfigureAwait(false);
                if (work.Client is { } sameClient)
                    source.Run(() => work.Session = new(sameClient, request, lifetime) { _originalScoped = true, _originalAttachment = work });
            }
            catch (Exception cause) { source.Keep(cause); }
            finally
            {
                if (work.Session is null || source.Errors.Length != 0)
                {
                    if (work.Session is { } sameSession)
                        await source.Cleanup(() => sameSession.CloseFailedOriginalAttachment(work), raw => work.SessionClose = raw).ConfigureAwait(false);
                    else if (work.Client is { } sameClient)
                        await source.Cleanup(() => sameClient.DisposeAsync().AsTask(), raw => work.ClientClose = raw).ConfigureAwait(false);
                }
                await source.JoinAll().ConfigureAwait(false);
            }
            source.Throw();
            lock (OriginalStartupAttachmentsGate) OriginalStartupAttachments.Remove(work);
            return work.Session;
        }
    }

    public Task<HomeNativeStartupObservation> CheckWithinOriginalSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        token.ThrowIfCancellationRequested(); _originalConnectionLifetime.ThrowIfCancellationRequested();
        var parent = _originalConnectionOwner;
        var work = new ScopedStartupCheck(this,
            body => { if (parent is null) scope(body); else CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () => { scope(body); return true; }); },
            raw => { if (parent is null) retain(raw); else CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () => { retain(raw); return true; }); });
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HomeNativeStartupObservation>? overlapping; Task<HomeNativeStartupObservation> actual;
        lock (_sync)
        {
            ThrowOriginalCallbackErrors();
            ObjectDisposedException.ThrowIf(_closing, this);
            _originalChecks.RemoveAll(old =>
            {
                var same = old.Driver; if (!same.IsCompletedSuccessfully) return false;
                same.GetAwaiter().GetResult(); return old.Source.Errors.Length == 0;
            });
            if (_originalChecks.Count >= 128) throw new InvalidOperationException("Unconfirmed original startup checks remain retained.");
            overlapping = _check is { IsCompleted: false } ? _check : null;
            actual = Drive(); work.Driver = actual; _originalChecks.Add(work);
            if (overlapping is null) _check = actual;
        }
        work.Source.Publish(actual); begin.SetResult(); return actual;
        async Task<HomeNativeStartupObservation> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var ancestor = parent is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(parent);
            HomeNativeStartupObservation value = null!;
            try
            {
                ThrowOriginalCallbackErrors(); work.Source.Throw();
                value = overlapping is not null ? await work.Source.Read(() => overlapping).ConfigureAwait(false) :
                    await CheckOriginalAsync(Task.CompletedTask, token, work.Source).ConfigureAwait(false);
                ThrowOriginalCallbackErrors();
            }
            catch (Exception cause) { work.Source.Keep(cause); lock (_sync) _closing = true; }
            await work.Source.JoinAll().ConfigureAwait(false); work.Source.Throw(); return value;
        }
    }
    private Task CloseFailedOriginalAttachment(ScopedStartupAttachment same)
    {
        if (!ReferenceEquals(_originalAttachment, same) || !ReferenceEquals(same.Session, this) || same.Driver.IsCompleted)
            throw new InvalidOperationException("The actual live original startup attachment is required for parent cleanup.");
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
            var source = _originalResourceCleanup = new HomeNativeOriginalStartupScope(this, () => null, body => body(), _ => { });
            // Acquire client cancellation/drain before waiting checks. The pipe
            // remains borrowed from the actual connection until this close joins.
            Task? clientClose = null;
            try { clientClose = _client.DisposeAsync().AsTask(); source.Retain(clientClose); }
            catch (Exception cause) { source.Keep(cause); }
            ScopedStartupCheck[] all; lock (_sync) all = _originalChecks.ToArray();
            foreach (var check in all)
            {
                await source.Cleanup(() => check.Driver).ConfigureAwait(false);
                await check.Source.JoinAll().ConfigureAwait(false);
                foreach (var cause in check.Source.Errors) source.Keep(cause);
            }
            if (clientClose is not null) await source.Cleanup(() => clientClose).ConfigureAwait(false);
            await source.JoinAll().ConfigureAwait(false); source.Throw();
        }
    }
    private async Task CloseWithinOriginalSourceAsync(Task start)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var source = _originalCleanup = new HomeNativeOriginalStartupScope(this, () => null, body => body(), _ => { });
        await source.Cleanup(AcquireOriginalResourceClose).ConfigureAwait(false);
        if (_originalAttachment is { } attachment)
        {
            await source.Cleanup(() => attachment.Driver).ConfigureAwait(false);
            await attachment.Source.JoinAll().ConfigureAwait(false);
            foreach (var cause in attachment.Source.Errors) source.Keep(cause);
        }
        Exception[] callbacks; lock (_sync) callbacks = _originalCallbackErrors.ToArray();
        foreach (var cause in callbacks) source.Keep(cause);
        await source.JoinAll().ConfigureAwait(false); source.Throw();
    }
}
