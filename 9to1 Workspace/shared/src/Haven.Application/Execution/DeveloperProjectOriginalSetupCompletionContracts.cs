namespace Haven.Application;

/// <summary>Opaque source-private acknowledgment of the actual complete original journal.
/// Public IDs, persisted all-ACK rows, bools and interface implementations cannot issue it.
/// This confirms only original setup-step/journal custody, never command/test/business success.</summary>
public interface IDeveloperProjectOriginalSetupCompletion { }

public interface IDeveloperProjectOriginalSetupCompletionSource
{
    // Null means this SAME original is still incomplete; it never authorizes replay/recovery.
    Task<IDeveloperProjectOriginalSetupCompletion?> GetOriginalCompletionAsync(
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        CancellationToken cancellationToken);
    // Pure private source/reference recognition only; current actor/source/store validation
    // requires the actual asynchronous validator before whole Home setup completion.
    bool IsIssuedOriginalCompletion(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupCompletion sameCompletion);
    Task ValidateOriginalCompletionAsync(DeveloperProjectSetupIntent sameIntent,
        IDeveloperProjectOriginalSourceCapture sameCapture, IDeveloperProjectOriginalSetupCompletion sameCompletion,
        CancellationToken cancellationToken);
    void DemandExternalOriginalSetupCompletionJoin();
}
