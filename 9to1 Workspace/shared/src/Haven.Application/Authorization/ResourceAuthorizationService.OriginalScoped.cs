using System.Runtime.ExceptionServices;

namespace Haven.Application;

public sealed partial class ResourceAuthorizationService
{
    /// <summary>Original-only current actor/registered resolver intersection. Unknown deep
    /// producers refuse; ordinary Authorize methods retain their exact behavior.</summary>
    public Task<AuthenticatedResourceActor?> AuthorizeForActorWithinOriginalSourceAsync(
        AuthenticatedResourceActor expectedActor, string actionId, IReadOnlyList<ResourceScope> scopes,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var callbacks = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        Action<Task> retain = raw => RetainResourceOriginal(callbacks, retainOriginalTask, raw);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = AuthorizeOriginalPublishedAsync(begin.Task, callbacks, retain, expectedActor, actionId, scopes, token);
        begin.SetResult(); return actual;
    }
    private async Task<AuthenticatedResourceActor?> AuthorizeOriginalPublishedAsync(Task begin,
        TaskRunOriginalResponseSourceCallbacks callbacks, Action<Task> retain, AuthenticatedResourceActor expectedActor,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken token)
    {
        await begin.ConfigureAwait(false);
        Exception? bodyFailure = null; AuthenticatedResourceActor? result = null;
        try { result = await AuthorizeOriginalCoreAsync(callbacks, retain, expectedActor, actionId, scopes, token).ConfigureAwait(false); }
        catch (Exception cause) { bodyFailure = cause; }
        // Every nested actor/resolver task retained before a scope failure is independently
        // joined even when no encompassing factory Task could cross its callback boundary.
        Task[] originals; lock (callbacks.Originals) originals = callbacks.Originals.ToArray();
        var failures = new List<Exception>();
        void Add(Exception cause) { if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
        foreach (var cause in callbacks.Errors) Add(cause);
        if (bodyFailure is not null) Add(bodyFailure);
        foreach (var raw in originals)
            try { await raw.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (raw.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(direct);
                else Add(cause);
            }
        if (failures.Count != 0)
        {
            if (bodyFailure is OperationCanceledException && originals.Any(raw => raw.IsCanceled) &&
                originals.All(raw => !raw.IsFaulted) && callbacks.Errors.Length == 0 &&
                failures.All(cause => cause is OperationCanceledException))
                ExceptionDispatchInfo.Capture(bodyFailure).Throw();
            throw new AggregateException("Actual scoped resource actor/resolver sources failed.", failures);
        }
        return result;
    }
    private async Task<AuthenticatedResourceActor?> AuthorizeOriginalCoreAsync(
        TaskRunOriginalResponseSourceCallbacks callbacks, Action<Task> retain, AuthenticatedResourceActor expectedActor,
        string actionId, IReadOnlyList<ResourceScope> scopes, CancellationToken token)
    {
        var captured = callbacks.Invoke(() => scopes?.ToArray());
        if (string.IsNullOrWhiteSpace(actionId) || captured is null || captured.Length == 0 || captured.Length > 1000) return null;
        if (actors is not IOriginalScopedResourceActorSource originalActors)
            throw new InvalidOperationException("The actual resource actor lacks its scoped original producer.");
        var actor = await ReadResourceOriginalAsync(callbacks, retain, () => originalActors.GetCurrentWithinOriginalSourceAsync(
            callbacks.Run, retain, token)).ConfigureAwait(false);
        if (actor is null || actor != expectedActor || string.IsNullOrWhiteSpace(actor.ActorId) ||
            string.IsNullOrWhiteSpace(actor.ProfileId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision) ||
            actor.AccountId == Guid.Empty || actor.OrganisationId == Guid.Empty ||
            actor.OrganisationId is not null && actor.AccountId is null) return null;
        foreach (var scope in captured)
        {
            if (scope is null || string.IsNullOrWhiteSpace(scope.Kind) || string.IsNullOrWhiteSpace(scope.Id) ||
                string.IsNullOrWhiteSpace(scope.Revision) || !Enum.IsDefined(scope.Access)) return null;
            var owners = callbacks.Invoke(() => _resolvers.Where(value => value.ResourceKind == scope.Kind).Take(2).ToArray());
            if (owners.Length != 1 || owners[0] is not IOriginalScopedCanonicalResourceAccessResolver originalOwner)
                throw new InvalidOperationException("The SAME canonical resource owner lacks its scoped original producer.");
            var decision = await ReadResourceOriginalAsync(callbacks, retain, () => originalOwner.EvaluateWithinOriginalSourceAsync(
                actor, actionId, scope, callbacks.Run, retain, token)).ConfigureAwait(false);
            if (!decision.Allowed || decision.ActorId != actor.ActorId || decision.ResourceRevision != scope.Revision ||
                decision.OrganisationId != actor.OrganisationId) return null;
        }
        // Actual actor is freshly read LAST after every canonical owner await.
        return await ReadResourceOriginalAsync(callbacks, retain, () => originalActors.GetCurrentWithinOriginalSourceAsync(
            callbacks.Run, retain, token)).ConfigureAwait(false) == actor ? actor : null;
    }
    private static void RetainResourceOriginal(TaskRunOriginalResponseSourceCallbacks callbacks,
        Action<Task> retainOriginalTask, Task raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        lock (callbacks.Originals)
            if (!callbacks.Originals.Any(value => ReferenceEquals(value, raw))) callbacks.Originals.Add(raw);
        // Capture precedes every external retention callback. Its synchronous cause is
        // recorded even if the parent swallows/replaces it.
        callbacks.Run(() => retainOriginalTask(raw));
    }
    private static async Task<T> ReadResourceOriginalAsync<T>(TaskRunOriginalResponseSourceCallbacks callbacks,
        Action<Task> retain, Func<Task<T>> factory)
    {
        Task<T>? actual = null; Exception? invocation = null;
        try { _ = callbacks.Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual resource Task returned."); retain(actual); return actual; }); }
        catch (Exception cause) { invocation = cause; }
        T result = default!; Exception? rawFailure = null;
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause) { rawFailure = actual.Exception ?? cause; }
        if (invocation is not null || rawFailure is not null)
        {
            if (invocation is null && actual?.IsCanceled == true && rawFailure is OperationCanceledException)
                ExceptionDispatchInfo.Capture(rawFailure).Throw();
            throw new AggregateException("Original resource callback/raw task failed.",
                new[] { invocation, rawFailure }.Where(cause => cause is not null).Cast<Exception>());
        }
        return actual is null ? throw new InvalidOperationException("No original resource Task acquired.") : result;
    }
}
