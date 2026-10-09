namespace Haven.Application;

/// <summary>The configured Files owner privately binds the SAME journal-issued intent and
/// source capture to an actual owned destination folder/configuration observation. This is
/// resource ownership evidence for a separate Home review, never a claimed write capability.
/// Copied intent fields, a source READ admission or a persisted checkpoint cannot issue it.</summary>
public interface IDeveloperProjectOriginalSetupScopeSource
{
    bool IsIssuedOriginalSetupBinding(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture);
    Task RevalidateOriginalSetupAsync(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, AuthenticatedResourceActor sameActor,
        CancellationToken cancellationToken);
    IReadOnlyList<ResourceScope> GetOriginalSetupScopes(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture);
    void DemandExternalOriginalSetupScopeJoin();
}
