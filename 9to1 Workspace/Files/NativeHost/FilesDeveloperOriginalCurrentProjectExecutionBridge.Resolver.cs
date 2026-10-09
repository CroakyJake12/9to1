using System.Runtime.ExceptionServices;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

/// <summary>Precreate before the SAME domain Resources. This forwards to one configured
/// private Files command binding owner; it issues neither READ nor execution consent.</summary>
public sealed class FilesDeveloperOriginalCurrentProjectExecutionResolver(
    Func<FilesDeveloperOriginalCurrentProjectExecutionBridge> originalSource)
    : IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.workspace.execute";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken token)
        => new(EvaluateWithinOriginalSourceAsync(actor, actionId, scope, body => body(), _ => { }, token));
    public async Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        Task<ResourceAccessDecision>? actual = null; ResourceAccessDecision? decision = null;
        var errors = new List<Exception>(); var callbackFailed = false;
        try
        {
            FilesOriginalSourceCallbackScope.Invoke(() =>
            {
                var source = originalSource() ?? throw new InvalidOperationException("The SAME configured current-project execution owner is unavailable.");
                actual = source.EvaluateOriginalExecutionWithinSourceAsync(actor, actionId, scope,
                    originalSynchronousScope, retainOriginalTask, token);
                retainOriginalTask(actual);
            }, originalSynchronousScope);
        }
        catch (Exception error) { callbackFailed = true; Add(errors, error); }
        if (actual is not null)
            try { decision = await actual.ConfigureAwait(false); }
            catch (Exception error)
            { foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause); }
        if (actual?.IsCanceled == true && !callbackFailed && errors.All(error => error is OperationCanceledException))
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual scoped execution resolver sources did not settle.", errors);
        return decision ?? throw new InvalidOperationException("No actual execution ownership decision was returned.");
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var cause in group.InnerExceptions) Add(errors, cause); }
        else if (!errors.Any(cause => ReferenceEquals(cause, error))) errors.Add(error);
    }
}

public sealed partial class FilesDeveloperOriginalCurrentProjectExecutionBridge
{
    public string ResourceKind => "dev.workspace.execute";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken token)
        => new(EvaluateWithinOriginalSourceAsync(actor, actionId, scope, body => body(), _ => { }, token));
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
        => EvaluateOriginalExecutionWithinSourceAsync(actor, actionId, scope, originalSynchronousScope, retainOriginalTask, token);

    public Task<ResourceAccessDecision> EvaluateOriginalExecutionWithinSourceAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
        => Start(originalSynchronousScope, retainOriginalTask, async (original, sources) =>
        {
            var binding = sources.Invoke(() =>
            {
                Binding[] candidates;
                lock (_gate) candidates = !_retiring && actionId == "dev.workspace.execute" && scope.Kind == "dev.workspace.execute"
                    && scope.Access == ResourceAccess.Execute ? _bindings.ToArray() : [];
                return candidates.SingleOrDefault(value => value.Receipt == scope.Id &&
                    value.DocumentSha + ":" + value.RootFingerprint == scope.Revision && value.OriginalActor == actor);
            });
            if (binding is null)
                return new ResourceAccessDecision(false, "OriginalCurrentProjectExecutionBindingRequired", actor.ActorId, scope.Revision, actor.OrganisationId);
            sources.Invoke(() => { RequireBinding(binding); return true; });
            // Resolver observation repeats fresh genuine actor/document/root authority,
            // outside held Home entries. The later execute review and final pin are separate.
            await RevalidateBody(binding, original, sources, token).ConfigureAwait(false);
            sources.Invoke(() => { token.ThrowIfCancellationRequested(); RequireBinding(binding); return true; });
            return new ResourceAccessDecision(true, "OriginalCurrentProjectExecutionOwned", actor.ActorId, scope.Revision, actor.OrganisationId);
        });
}
