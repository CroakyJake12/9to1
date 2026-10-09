using System.Text.Json;

namespace Haven.Application;

/// <summary>Current Files metadata issued for Data. Implementing this interface,
/// copying its fields or retaining a displayed path grants no content or worker access.</summary>
public interface ICanonicalOriginalDataFileSelection : ICanonicalOriginalWorkerResource
{
    AuthenticatedResourceActor OriginalActor { get; }
    Guid OriginalFileId { get; }
    Guid OriginalRevisionId { get; }
    string OriginalName { get; }
    long OriginalSizeBytes { get; }
}

/// <summary>Pure private issuance and currentness of actual Files metadata. This
/// cannot open bytes, start a worker or grant a Data operation.</summary>
public interface ICanonicalOriginalDataFileSelectionSource
{
    bool IsIssuedOriginalDataFileSelection(ICanonicalOriginalDataFileSelection sameSelection);
    Task RevalidateOriginalDataFileSelectionWithinSourceAsync(ICanonicalOriginalDataFileSelection sameSelection,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}
