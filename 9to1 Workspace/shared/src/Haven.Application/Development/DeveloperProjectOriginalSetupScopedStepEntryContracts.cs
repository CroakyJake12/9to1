namespace Haven.Application;

/// <summary>Optional same-private-entry extension for actual kernel/Files producers.
/// Scopes protect original callback lifetime; they issue no actor, policy, root or step grant.
/// Both supplied callbacks must run on every actual post-await raw source acquisition.
/// Original step/entry/held Home and native authority checks remain mandatory.</summary>
public interface IDeveloperProjectOriginalSetupScopedStepEntry : IDeveloperProjectOriginalSetupStepEntry
{
    ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep sameStep,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
