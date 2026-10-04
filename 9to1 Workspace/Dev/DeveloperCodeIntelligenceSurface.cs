using System.Collections.Concurrent;
using Haven.Application;

namespace HavenOS.Apps.Dev;

/// <summary>
/// A code document keeps durable Dev/File identity separate from its local path used by the current LSP provider.
/// </summary>
public sealed record DeveloperCodeDocument(
    Guid WorkspaceId,
    Guid ProjectId,
    Guid FileId,
    string CanonicalResourceId,
    string WorkspaceRoot,
    string RelativePath,
    long WorkspaceRevision)
{
    public string? Validate()
    {
        if (WorkspaceId == Guid.Empty || ProjectId == Guid.Empty || FileId == Guid.Empty)
            return "Workspace, project, and file IDs must be stable non-empty identifiers.";
        if (string.IsNullOrWhiteSpace(CanonicalResourceId) || string.IsNullOrWhiteSpace(WorkspaceRoot) ||
            string.IsNullOrWhiteSpace(RelativePath) || WorkspaceRevision < 1)
            return "A canonical resource identity, workspace root, relative path, and positive revision are required.";
        if (!Path.IsPathRooted(WorkspaceRoot) || Path.IsPathRooted(RelativePath))
            return "The workspace root must be absolute and the file location must be relative.";
        if (RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or ".."))
            return "The code document path cannot contain traversal segments.";
        return null;
    }
}

public enum DeveloperAuthorizationDecision { Granted, PendingApproval, Denied }

public sealed record DeveloperMutationRequest(
    Guid RequestId,
    string CallerIdentity,
    string CanonicalAction,
    string RequiredPermissionScope,
    Guid WorkspaceId,
    long WorkspaceRevision,
    string TargetResourceId,
    IReadOnlyList<string> AffectedRelativePaths,
    bool IsReversible,
    bool HasExternalSideEffects);

public sealed record DeveloperMutationAuthorization(
    DeveloperAuthorizationDecision Decision,
    string? GrantId,
    string? UserMessage);

/// <summary>Adapter contract for Home's central permission broker. There is deliberately no permissive default.</summary>
public interface IDeveloperWorkspaceMutationGate
{
    Task<DeveloperMutationAuthorization> AuthorizeAsync(DeveloperMutationRequest request, CancellationToken cancellationToken);
}

public sealed record DeveloperWorkspaceMutationReceipt(
    Guid RequestId,
    Guid TransactionId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid SourceFileId,
    IReadOnlyList<string> AffectedRelativePaths,
    string Summary,
    long StartingWorkspaceRevision);

/// <summary>
/// Provides the Dev API over the maintained LSP service. Mutating code actions are bound to an exact, locally
/// prepared proposal and pass through the required Home authorization adapter before the shared transaction runs.
/// </summary>
public sealed class DeveloperCodeIntelligenceSurface
{
    private static readonly TimeSpan ProposalLifetime = TimeSpan.FromMinutes(5);
    private const int MaximumPreparedProposals = 512;
    private readonly IAdvancedCodeIntelligenceService _languageService;
    private readonly IDeveloperWorkspaceMutationGate _mutationGate;
    private readonly ConcurrentDictionary<Guid, PreparedCodeAction> _preparedActions = new();

    public DeveloperCodeIntelligenceSurface(
        IAdvancedCodeIntelligenceService languageService,
        IDeveloperWorkspaceMutationGate mutationGate)
    {
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _mutationGate = mutationGate ?? throw new ArgumentNullException(nameof(mutationGate));
    }

    public Task<DeveloperOperationResult<LanguageServerCapabilities>> GetCapabilitiesAsync(
        DeveloperCodeDocument document, CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetCapabilitiesAsync(target.WorkspaceRoot, target.RelativePath, token), cancellationToken);

    public Task<DeveloperOperationResult<IReadOnlyList<CodeLocation>>> GetDefinitionsAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodePosition position,
        CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetDefinitionAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken);

    public Task<DeveloperOperationResult<IReadOnlyList<CodeLocation>>> FindReferencesAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodePosition position,
        CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.FindReferencesAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken);

    public Task<DeveloperOperationResult<IReadOnlyList<LanguageServerCompletion>>> GetCompletionsAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodePosition position,
        CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetCompletionsAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken);

    public async Task<DeveloperOperationResult<IReadOnlyList<CodeActionProposal>>> GetCodeActionsAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodeRange range,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        if (document is null)
            return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.InvalidInput,
                "A code document is required.", string.Empty, requestId);
        if (document.Validate() is { } validationError)
            return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.InvalidInput,
                validationError, document.FileId.ToString("D"), requestId);

        try
        {
            var proposals = await _languageService.GetCodeActionsAsync(
                document.WorkspaceRoot, document.RelativePath, currentText, range, cancellationToken).ConfigureAwait(false);
            ExpirePreparedActions();
            foreach (var proposal in proposals.Where(static proposal => proposal.IsApplicable))
                _preparedActions[proposal.Id] = new PreparedCodeAction(document, proposal, DateTimeOffset.UtcNow);
            TrimPreparedActions();
            return DeveloperOperationResult<IReadOnlyList<CodeActionProposal>>.Success(proposals, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return MapFailure<IReadOnlyList<CodeActionProposal>>(exception, document, requestId); }
    }

    public async Task<DeveloperOperationResult<DeveloperWorkspaceMutationReceipt>> ApplyCodeActionAsync(
        DeveloperCodeDocument document,
        CodeActionProposal requestedProposal,
        string callerIdentity,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        if (document is null)
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                "A code document is required.", string.Empty, requestId);
        if (document.Validate() is { } validationError)
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                validationError, document.FileId.ToString("D"), requestId);
        if (requestedProposal is null || string.IsNullOrWhiteSpace(callerIdentity))
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                "An exact prepared proposal and stable caller identity are required.", document.FileId.ToString("D"), requestId);

        if (!_preparedActions.TryGetValue(requestedProposal.Id, out var prepared) ||
            prepared.CreatedAt + ProposalLifetime < DateTimeOffset.UtcNow ||
            prepared.Document.WorkspaceId != document.WorkspaceId ||
            prepared.Document.ProjectId != document.ProjectId ||
            prepared.Document.FileId != document.FileId ||
            prepared.Document.WorkspaceRevision != document.WorkspaceRevision ||
            !StringComparer.Ordinal.Equals(prepared.Document.CanonicalResourceId, document.CanonicalResourceId) ||
            !PathComparer.Equals(Path.GetFullPath(prepared.Document.WorkspaceRoot), Path.GetFullPath(document.WorkspaceRoot)) ||
            !StringComparer.OrdinalIgnoreCase.Equals(prepared.Document.RelativePath, document.RelativePath))
        {
            _preparedActions.TryRemove(requestedProposal.Id, out _);
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.RevisionConflict,
                "This code action is no longer current. Refresh the language-server proposals and review the new diff.",
                document.FileId.ToString("D"), requestId);
        }

        var exactProposal = prepared.Proposal;
        var paths = exactProposal.Files.Select(static file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0 || paths.Any(static path => string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or "..")))
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                "The language service returned an invalid workspace edit path.", document.FileId.ToString("D"), requestId);
        var mutation = new DeveloperMutationRequest(
            requestId, callerIdentity.Trim(), "9to1.Dev.ApplyEdit", "workspace.write",
            document.WorkspaceId, document.WorkspaceRevision, document.CanonicalResourceId,
            paths, IsReversible: true, HasExternalSideEffects: false);

        try
        {
            var authorization = await _mutationGate.AuthorizeAsync(mutation, cancellationToken).ConfigureAwait(false);
            if (authorization.Decision == DeveloperAuthorizationDecision.PendingApproval)
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.PermissionRequired,
                    authorization.UserMessage ?? "Home approval is required before applying this code action.",
                    document.FileId.ToString("D"), requestId);
            if (authorization.Decision != DeveloperAuthorizationDecision.Granted || string.IsNullOrWhiteSpace(authorization.GrantId))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.PermissionDenied,
                    authorization.UserMessage ?? "Home did not authorize this code action.",
                    document.FileId.ToString("D"), requestId);

            var applied = await _languageService.ApplyCodeActionAsync(
                document.WorkspaceRoot, exactProposal, cancellationToken).ConfigureAwait(false);
            _preparedActions.TryRemove(exactProposal.Id, out _);
            var receipt = new DeveloperWorkspaceMutationReceipt(
                requestId, applied.TransactionId, document.WorkspaceId, document.ProjectId, document.FileId,
                applied.Files.Select(static file => file.RelativePath).ToArray(), applied.Summary,
                document.WorkspaceRevision);
            return DeveloperOperationResult<DeveloperWorkspaceMutationReceipt>.Success(receipt, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return MapFailure<DeveloperWorkspaceMutationReceipt>(exception, document, requestId); }
    }

    public Task<DeveloperOperationResult<IReadOnlyList<CodeSemanticToken>>> GetSemanticTokensAsync(
        DeveloperCodeDocument document, string currentText, CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetSemanticTokensAsync(target.WorkspaceRoot, target.RelativePath, currentText, token), cancellationToken);

    /// <summary>
    /// Rename is intentionally unavailable until the shared LSP contract can return a reviewable edit proposal
    /// before applying it. The current service's RenameSymbolAsync applies the transaction immediately.
    /// </summary>
    public DeveloperOperationResult<DeveloperWorkspaceMutationReceipt> RenameSymbolRequiresPreview(
        DeveloperCodeDocument document) =>
        DeveloperOperationResult<DeveloperWorkspaceMutationReceipt>.Failure(
            DeveloperOperationErrorCode.CapabilityUnavailable,
            "Rename is blocked until the language-service contract exposes a reviewable workspace-edit preview for Home approval.",
            document?.FileId.ToString("D") ?? string.Empty);

    private async Task<DeveloperOperationResult<T>> ReadAsync<T>(
        DeveloperCodeDocument document,
        Func<DeveloperCodeDocument, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        if (document is null)
            return Failure<T>(DeveloperOperationErrorCode.InvalidInput,
                "A code document is required.", string.Empty, requestId);
        if (document.Validate() is { } validationError)
            return Failure<T>(DeveloperOperationErrorCode.InvalidInput,
                validationError, document.FileId.ToString("D"), requestId);
        try
        {
            var result = await operation(document, cancellationToken).ConfigureAwait(false);
            return DeveloperOperationResult<T>.Success(result, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return MapFailure<T>(exception, document, requestId); }
    }

    private static DeveloperOperationResult<T> MapFailure<T>(Exception exception, DeveloperCodeDocument document, Guid requestId) =>
        exception switch
        {
            FileNotFoundException => Failure<T>(DeveloperOperationErrorCode.FileNotFound, "The source file no longer exists.", document.FileId.ToString("D"), requestId),
            DirectoryNotFoundException => Failure<T>(DeveloperOperationErrorCode.WorkspaceNotFound, "The workspace root is unavailable.", document.WorkspaceId.ToString("D"), requestId),
            UnauthorizedAccessException => Failure<T>(DeveloperOperationErrorCode.PermissionDenied, "The current process cannot access this source file.", document.FileId.ToString("D"), requestId),
            NotSupportedException => Failure<T>(DeveloperOperationErrorCode.LanguageServiceUnavailable, exception.Message, document.FileId.ToString("D"), requestId),
            InvalidOperationException => Failure<T>(DeveloperOperationErrorCode.LanguageServiceUnavailable, exception.Message, document.FileId.ToString("D"), requestId, retryable: true),
            IOException => Failure<T>(DeveloperOperationErrorCode.LanguageServiceUnavailable, "The language-service request could not complete.", document.FileId.ToString("D"), requestId, retryable: true),
            ArgumentException => Failure<T>(DeveloperOperationErrorCode.InvalidInput, exception.Message, document.FileId.ToString("D"), requestId),
            _ => Failure<T>(DeveloperOperationErrorCode.CapabilityUnavailable, "The requested language-service operation is unavailable.", document.FileId.ToString("D"), requestId)
        };

    private static DeveloperOperationResult<T> Failure<T>(
        DeveloperOperationErrorCode code, string message, string targetId, Guid requestId, bool retryable = false) =>
        DeveloperOperationResult<T>.Failure(code, message, targetId, recoverable: true, retryable, requestId);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private void ExpirePreparedActions()
    {
        var cutoff = DateTimeOffset.UtcNow - ProposalLifetime;
        foreach (var (id, prepared) in _preparedActions)
            if (prepared.CreatedAt < cutoff) _preparedActions.TryRemove(id, out _);
    }

    private void TrimPreparedActions()
    {
        if (_preparedActions.Count <= MaximumPreparedProposals) return;
        foreach (var (id, _) in _preparedActions.OrderBy(static pair => pair.Value.CreatedAt)
                     .Take(_preparedActions.Count - MaximumPreparedProposals))
            _preparedActions.TryRemove(id, out _);
    }

    private sealed record PreparedCodeAction(
        DeveloperCodeDocument Document,
        CodeActionProposal Proposal,
        DateTimeOffset CreatedAt);
}
