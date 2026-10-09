using System.Runtime.ExceptionServices;
using Haven.Application;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
using HomePermissionTrustService = HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService;

namespace HavenOS.Home.NativeUI;

/// <summary>HOME-process-only native review windows over the original owner components.
/// This creates no Home graph, app identity, permission policy or presentation receipt.</summary>
public sealed class HomeNativeApprovalWindowOwner(
    HomeCoreRuntime originalRuntime, HomeLocalProfileIdentity originalProfiles,
    HomePermissionTrustService originalPermissions,
    HomeApprovalOriginalWarningPresentationSource? originalPresentation = null)
{
    private sealed class Review(CancellationToken caller)
    {
        internal HomeApprovalCuiSurface? Surface;
        internal Window? Window;
        internal CancellationTokenSource Lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller);
        internal Task Open = null!;
        internal Task? Initialization;
        internal Task<bool>? Focus;
        internal Task? SurfaceClose;
        internal Task? Close;
        internal EventHandler<WindowClosingEventArgs>? Closing;
        internal bool Released;
        internal List<Task> Focuses { get; } = [];
    }

    private readonly HomeCoreRuntime _runtime = originalRuntime ?? throw new ArgumentNullException(nameof(originalRuntime));
    private readonly HomeLocalProfileIdentity _profiles = originalProfiles ?? throw new ArgumentNullException(nameof(originalProfiles));
    private readonly HomePermissionTrustService _permissions = originalPermissions ?? throw new ArgumentNullException(nameof(originalPermissions));
    private readonly object _sync = new();
    private readonly List<Review> _reviews = [];
    private readonly List<Task> _work = [];
    private Review? _current;
    private Task? _close;
    private volatile bool _retiring;
    private const int MaximumOriginalReviews = 128;

    public Task? OriginalClose { get { lock (_sync) return _close; } }
    public Window? OriginalWindow { get { lock (_sync) return _current?.Window; } }
    public HomeApprovalCuiSurface? OriginalSurface { get { lock (_sync) return _current?.Surface; } }
    public bool OriginalRetirementCapturedAndSettled
    {
        get
        {
            lock (_sync) return _close?.IsCompleted == true && _work.All(task => task.IsCompleted) &&
                _reviews.All(review => review.Close?.IsCompleted == true &&
                    (review.Surface is null || review.SurfaceClose?.IsCompleted == true));
        }
    }

    // Pure pairing only; this is no permission, installed authority or startup proof.
    public bool IsBoundToOriginalComposition(HomeNativeWindowsComposition original) =>
        original is not null && ReferenceEquals(_runtime, original.Runtime) &&
        ReferenceEquals(_profiles, original.Profiles) && ReferenceEquals(_permissions, original.Permissions);

    /// <summary>Returns the actual acquisition/publication task, not the window's lifetime.
    /// Exact request identifiers are observations; the same surface rereads canonical Home.</summary>
    public Task OpenOriginalAsync(string? requestId = null, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (requestId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Review review;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retiring || _close is not null, this);
            _work.RemoveAll(task => task.IsCompletedSuccessfully);
            _reviews.RemoveAll(item => item.Released && item.Open.IsCompletedSuccessfully &&
                item.Close?.IsCompletedSuccessfully == true);
            if (_reviews.Count >= MaximumOriginalReviews || _work.Count >= MaximumOriginalReviews)
                throw new InvalidOperationException("Join original native review failures before opening another window.");
            if (_current is { } active && active.Close is null)
            {
                // A new request uses the same real surface, never a copied model or new Home owner.
                return FocusOriginalAsync(active, requestId, cancellationToken);
            }
            review = new Review(cancellationToken);
            _current = review;
            review.Open = OpenCoreAsync(start.Task, review, requestId, cancellationToken);
            _reviews.Add(review); // Actual frame/task custody before native acquisition.
            _work.Add(review.Open);
        }
        start.SetResult();
        return review.Open;
    }

    private async Task OpenCoreAsync(Task start, Review review, string? requestId, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var originalScope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> errors = [];
        try
        {
            await InvokeOriginalAsync(() =>
            {
                DemandOpen(review, caller);
                var surface = review.Surface = new HomeApprovalCuiSurface(_runtime, _profiles, _permissions)
                {
                    OriginalWarningPresentationSource = originalPresentation,
                    OriginalOwnerAdmissionCurrent = () => !_retiring
                };
                var window = review.Window = new Window { Title = "Home permissions", Width = 900, Height = 700 };
                review.Closing = (_, args) =>
                {
                    args.Cancel = true;
                    _ = CloseReviewOriginal(review); // Request only; never join this accepted native callback.
                };
                window.Closing += review.Closing;
                DemandOpen(review, caller);
                return review.Initialization = surface.InitializeAsync(review.Lifetime.Token);
            }, errors).ConfigureAwait(false);
            if (errors.Count != 0) Throw(errors);
            if (requestId is not null)
            {
                await InvokeOriginalAsync(() =>
                {
                    DemandOpen(review, caller);
                    return review.Focus = review.Surface!.FocusRequestAsync(requestId, review.Lifetime.Token);
                }, errors).ConfigureAwait(false);
                if (errors.Count != 0) Throw(errors);
                if (!review.Focus!.GetAwaiter().GetResult())
                    throw new InvalidOperationException("The exact canonical Home request is no longer reviewable.");
            }
            await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                DemandOpen(review, caller);
                review.Window!.Content = review.Surface;
                DemandOpen(review, caller);
                review.Window.Show();
                DemandOpen(review, caller);
                return true;
            }));
        }
        catch (Exception error) { Add(errors, error); }
        if (errors.Count != 0)
        {
            _ = CloseReviewOriginal(review); // Published close joins this body after it returns.
            Throw(errors);
        }
    }

    private Task FocusOriginalAsync(Review review, string? requestId, CancellationToken caller)
    {
        // Called under the owning gate: publish each actual focus before its callbacks.
        review.Focuses.RemoveAll(task => task.IsCompletedSuccessfully);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = FocusCoreAsync(start.Task, review, requestId, caller);
        review.Focuses.Add(original);
        _work.Add(original);
        start.SetResult();
        return original;
    }

    private async Task FocusCoreAsync(Task start, Review review, string? requestId, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var originalScope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> errors = [];
        CancellationTokenSource? request = null;
        try
        {
            request = CancellationTokenSource.CreateLinkedTokenSource(review.Lifetime.Token, caller);
            await review.Open.ConfigureAwait(false);
            if (requestId is not null)
            {
                Task<bool>? originalFocus = null;
                await InvokeOriginalAsync(() =>
                {
                    DemandOpen(review, caller);
                    return originalFocus = review.Surface!.FocusRequestAsync(requestId, request.Token);
                }, errors).ConfigureAwait(false);
                if (errors.Count != 0) Throw(errors);
                if (!originalFocus!.GetAwaiter().GetResult())
                    throw new InvalidOperationException("The exact canonical Home request is no longer reviewable.");
            }
            await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                DemandOpen(review, caller);
                review.Window!.Activate();
                DemandOpen(review, caller);
                return true;
            }));
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (request is not null)
                try { request.Dispose(); } catch (Exception error) { Add(errors, error); }
        }
        Throw(errors);
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_sync)
            foreach (var review in _reviews) review.Surface?.DemandExternalOriginalRetirementJoin();
    }

    public void RequestRetirement()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_sync) _retiring = true;
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task original;
        lock (_sync)
        {
            if (_close is not null) return _close;
            original = _close = CloseCoreAsync(start.Task); // Seal/publish before cancellation or callbacks.
        }
        start.SetResult();
        return original;
    }

    private Task CloseReviewOriginal(Review review)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task original;
        lock (_sync)
        {
            if (review.Close is not null) return review.Close;
            original = review.Close = CloseReviewCoreAsync(start.Task, review);
        }
        start.SetResult();
        return original;
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        using var originalScope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        Review[] reviews;
        lock (_sync) reviews = _reviews.ToArray();
        List<Exception> errors = [];
        // Start every actual owner stop before joining any acquisition that may need it to settle.
        foreach (var review in reviews)
            try { _ = CloseReviewOriginal(review); } catch (Exception error) { Add(errors, error); }
        foreach (var review in reviews)
        {
            await Collect(review.Open, errors).ConfigureAwait(false);
            if (review.Close is { } original) await Collect(original, errors).ConfigureAwait(false);
        }
        Throw(errors);
    }

    private async Task CloseReviewCoreAsync(Task start, Review review)
    {
        await start.ConfigureAwait(false);
        using var originalScope = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Exception> errors = [];
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                if (review.Surface is { } surface)
                    try { review.SurfaceClose = surface.CloseAndDrainAsync(); }
                    catch (Exception error) { Add(errors, error); }
                try { review.Lifetime.Cancel(); } catch (Exception error) { Add(errors, error); }
                return true;
            }));
        }
        catch (Exception error) { Add(errors, error); }
        await Collect(review.Open, errors).ConfigureAwait(false);
        Task[] focuses;
        lock (_sync) focuses = review.Focuses.ToArray();
        foreach (var originalFocus in focuses) await Collect(originalFocus, errors).ConfigureAwait(false);
        if (review.Initialization is { } initialization) await Collect(initialization, errors).ConfigureAwait(false);
        if (review.Focus is { } focus) await Collect(focus, errors).ConfigureAwait(false);
        if (review.SurfaceClose is { } close) await Collect(close, errors).ConfigureAwait(false);
        if (review.Surface is not null && review.SurfaceClose is null)
            Add(errors, new InvalidOperationException("The original native review close was not captured."));
        // Missing/pending close never authorizes destructive native retirement.
        if (review.Surface is null || review.SurfaceClose?.IsCompleted == true)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                {
                    if (review.Window is { } window)
                    {
                        if (review.Closing is { } closing)
                            try { window.Closing -= closing; } catch (Exception error) { Add(errors, error); }
                        try { if (ReferenceEquals(window.Content, review.Surface)) window.Content = null; }
                        catch (Exception error) { Add(errors, error); }
                        try { window.Close(); } catch (Exception error) { Add(errors, error); }
                    }
                    review.Released = true;
                    lock (_sync) if (ReferenceEquals(_current, review)) _current = null;
                    return true;
                }));
            }
            catch (Exception error) { Add(errors, error); }
            try { review.Lifetime.Dispose(); } catch (Exception error) { Add(errors, error); }
        }
        Throw(errors);
    }

    private void DemandOpen(Review review, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        caller.ThrowIfCancellationRequested();
        review.Lifetime.Token.ThrowIfCancellationRequested();
        lock (_sync)
            ObjectDisposedException.ThrowIf(_retiring || _close is not null || review.Close is not null, this);
    }

    private async Task InvokeOriginalAsync(Func<Task> callback, List<Exception> errors)
    {
        Task? body = null;
        Task<Task>? dispatch = null;
        var joined = false;
        try
        {
            dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                body = callback() ?? throw new InvalidOperationException("The original native review callback returned no task."))).GetTask();
            var returned = await dispatch.ConfigureAwait(false);
            if (!ReferenceEquals(returned, body))
                throw new InvalidOperationException("Retain the same original native review body.");
            joined = true;
            await Collect(returned, errors).ConfigureAwait(false);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (!joined && body is not null) await Collect(body, errors).ConfigureAwait(false);
        }
    }

    private static async Task Collect(Task original, List<Exception> errors)
    {
        try { await original.ConfigureAwait(false); }
        catch (Exception observed)
        {
            var faults = original.Exception;
            if (faults is null) Add(errors, observed);
            else foreach (var error in faults.InnerExceptions) Add(errors, error);
        }
    }

    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original native Home review and independent retirement failed.", errors);
    }
}
