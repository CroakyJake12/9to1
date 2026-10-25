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
    private readonly object _preparedActionsGate = new();
    private readonly ConcurrentDictionary<Guid, PreparedCodeAction> _preparedActions = new();
    private readonly HashSet<Guid> _applyingActions = [];

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
            (target, token) => _languageService.GetDefinitionAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken, capability => capability.Definition, "go to definition");

    public Task<DeveloperOperationResult<IReadOnlyList<CodeLocation>>> FindReferencesAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodePosition position,
        CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.FindReferencesAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken, capability => capability.References, "find references");

    public Task<DeveloperOperationResult<IReadOnlyList<LanguageServerCompletion>>> GetCompletionsAsync(
        DeveloperCodeDocument document, string currentText, Haven.Core.CodePosition position,
        CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetCompletionsAsync(target.WorkspaceRoot, target.RelativePath, currentText, position, token), cancellationToken, capability => capability.Completion, "completion");

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
            cancellationToken.ThrowIfCancellationRequested();
            var capabilities = await _languageService.GetCapabilitiesAsync(document.WorkspaceRoot, document.RelativePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!capabilities.CodeActions)
                return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The active language provider does not support code actions. Configure a supporting language server.", document.FileId.ToString("D"), requestId);
            var proposals = await _languageService.GetCodeActionsAsync(
                document.WorkspaceRoot, document.RelativePath, currentText, range, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return RetainPreparedActions(document, proposals, requestId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return MapFailure<IReadOnlyList<CodeActionProposal>>(exception, document, requestId); }
    }

    private DeveloperOperationResult<IReadOnlyList<CodeActionProposal>> RetainPreparedActions(DeveloperCodeDocument document,
        IReadOnlyList<CodeActionProposal> proposals, Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The same detached edit payload is both returned for display and retained for
        // Home authorization/application; provider-owned lists cannot change that scope.
        var capturedProposals = Array.AsReadOnly(proposals.Select(proposal => proposal with
            { Files = Array.AsReadOnly(proposal.Files.ToArray()) }).ToArray());
        if (capturedProposals.Any(static proposal => proposal.Files.Any(file =>
                file is null || file.RelativePath is null || file.BeforeContent is null || file.AfterContent is null)))
            return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.CapabilityUnavailable,
                "The language service returned an incomplete workspace edit.", document.FileId.ToString("D"), requestId);
        cancellationToken.ThrowIfCancellationRequested();
        var applicableProposals = capturedProposals.Where(static proposal => proposal.IsApplicable).ToArray();
        if (capturedProposals.Any(static proposal => proposal.Id == Guid.Empty) ||
            capturedProposals.Select(static proposal => proposal.Id).Distinct().Count() != capturedProposals.Count)
            return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.CapabilityUnavailable,
                "The language service returned ambiguous code-action identities.", document.FileId.ToString("D"), requestId);
        lock (_preparedActionsGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpirePreparedActions();
            if (capturedProposals.Any(proposal => _applyingActions.Contains(proposal.Id) ||
                _preparedActions.ContainsKey(proposal.Id)))
                return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The language service reused a code-action identity that is still active; request fresh proposals.",
                    document.FileId.ToString("D"), requestId);
            foreach (var proposal in applicableProposals)
                if (!_preparedActions.TryAdd(proposal.Id, new PreparedCodeAction(document, proposal, DateTimeOffset.UtcNow)))
                    return Failure<IReadOnlyList<CodeActionProposal>>(DeveloperOperationErrorCode.CapabilityUnavailable,
                        "The language service reused a code-action identity; request fresh proposals.",
                        document.FileId.ToString("D"), requestId);
            TrimPreparedActions();
        }
        return DeveloperOperationResult<IReadOnlyList<CodeActionProposal>>.Success(capturedProposals, requestId);
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
        if (requestedProposal is null || requestedProposal.Id == Guid.Empty || string.IsNullOrWhiteSpace(callerIdentity))
            return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                "An exact prepared proposal and stable caller identity are required.", document.FileId.ToString("D"), requestId);
        cancellationToken.ThrowIfCancellationRequested();

        PreparedCodeAction prepared;
        string[] paths;
        lock (_preparedActionsGate)
        {
            if (!_preparedActions.TryGetValue(requestedProposal.Id, out var found) || _applyingActions.Contains(requestedProposal.Id))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.RevisionConflict,
                    "This code action is no longer current or is already being applied. Refresh the language-server proposals and review the new diff.",
                    document.FileId.ToString("D"), requestId);
            prepared = found;
            if (prepared.CreatedAt + ProposalLifetime < DateTimeOffset.UtcNow ||
                prepared.Document.WorkspaceId != document.WorkspaceId ||
                prepared.Document.ProjectId != document.ProjectId ||
                prepared.Document.FileId != document.FileId ||
                prepared.Document.WorkspaceRevision != document.WorkspaceRevision ||
                !StringComparer.Ordinal.Equals(prepared.Document.CanonicalResourceId, document.CanonicalResourceId) ||
                !PathComparer.Equals(Path.GetFullPath(prepared.Document.WorkspaceRoot), Path.GetFullPath(document.WorkspaceRoot)) ||
                !PathComparer.Equals(prepared.Document.RelativePath, document.RelativePath))
            {
                _preparedActions.TryRemove(requestedProposal.Id, out _);
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.RevisionConflict,
                    "This code action is no longer current. Refresh the language-server proposals and review the new diff.",
                    document.FileId.ToString("D"), requestId);
            }

            if (prepared.Proposal.Files is null || prepared.Proposal.Files.Any(static file =>
                    file is null || file.BeforeContent is null || file.AfterContent is null))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                    "The language service returned an incomplete workspace edit.", document.FileId.ToString("D"), requestId);
            var candidatePaths = prepared.Proposal.Files.Select(static file => file.RelativePath).ToArray();
            // Windows colons can select drive-relative paths or NTFS alternate streams.
            if (candidatePaths.Length == 0 || candidatePaths.Any(static path => string.IsNullOrWhiteSpace(path) ||
                    path.Length > 4096 || path.IndexOf('\0') >= 0 || Path.IsPathRooted(path) ||
                    (OperatingSystem.IsWindows() && path.Contains(':')) ||
                    path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(static part => part is "." or "..")))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                    "The language service returned an invalid workspace edit path.", document.FileId.ToString("D"), requestId);
            string[] resolvedPaths;
            try
            {
                resolvedPaths = candidatePaths.Select(path =>
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(document.WorkspaceRoot, path)))).ToArray();
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                    "The language service returned a path that cannot be safely resolved.", document.FileId.ToString("D"), requestId);
            }
            // File mutations are whole-file replacements; aliases must not produce two writes
            // against one file after Home has approved only one canonical resource.
            if (resolvedPaths.Distinct(PathComparer).Count() != resolvedPaths.Length)
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.InvalidInput,
                    "The language service returned multiple edits for the same canonical file path.", document.FileId.ToString("D"), requestId);
            if (!_applyingActions.Add(requestedProposal.Id))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.RevisionConflict,
                    "This code action is already being applied.", document.FileId.ToString("D"), requestId);
            paths = candidatePaths;
        }

        var exactProposal = prepared.Proposal;
        var affectedPaths = Array.AsReadOnly(paths);
        var mutation = new DeveloperMutationRequest(
            requestId, callerIdentity.Trim(), "9to1.Dev.ApplyEdit", "workspace.write",
            document.WorkspaceId, document.WorkspaceRevision, document.CanonicalResourceId,
            affectedPaths, IsReversible: true, HasExternalSideEffects: false);

        var consumePrepared = false;
        try
        {
            var authorization = await _mutationGate.AuthorizeAsync(mutation, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (authorization.Decision == DeveloperAuthorizationDecision.PendingApproval)
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.PermissionRequired,
                    authorization.UserMessage ?? "Home approval is required before applying this code action.",
                    document.FileId.ToString("D"), requestId);
            if (authorization.Decision != DeveloperAuthorizationDecision.Granted || string.IsNullOrWhiteSpace(authorization.GrantId))
                return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.PermissionDenied,
                    authorization.UserMessage ?? "Home did not authorize this code action.",
                    document.FileId.ToString("D"), requestId);

            lock (_preparedActionsGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (prepared.CreatedAt + ProposalLifetime < DateTimeOffset.UtcNow)
                {
                    _preparedActions.TryRemove(requestedProposal.Id, out _);
                    return Failure<DeveloperWorkspaceMutationReceipt>(DeveloperOperationErrorCode.RevisionConflict,
                        "This code action expired while Home authorization was pending. Refresh the language-server proposals and review the new diff.",
                        document.FileId.ToString("D"), requestId);
                }
                // The in-flight reservation now makes this proposal single-use. Retain its
                // bounded entry until the side effect settles so in-flight work counts toward
                // the existing proposal-custody limit.
                consumePrepared = true;
            }
            var applied = await _languageService.ApplyCodeActionAsync(
                document.WorkspaceRoot, exactProposal, cancellationToken).ConfigureAwait(false);
            var appliedPaths = Array.AsReadOnly(applied.Files.Select(static file => file.RelativePath).ToArray());
            var receipt = new DeveloperWorkspaceMutationReceipt(
                requestId, applied.TransactionId, document.WorkspaceId, document.ProjectId, document.FileId,
                appliedPaths, applied.Summary,
                document.WorkspaceRevision);
            return DeveloperOperationResult<DeveloperWorkspaceMutationReceipt>.Success(receipt, requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return MapFailure<DeveloperWorkspaceMutationReceipt>(exception, document, requestId); }
        finally
        {
            lock (_preparedActionsGate)
            {
                _applyingActions.Remove(requestedProposal.Id);
                if (consumePrepared) _preparedActions.TryRemove(requestedProposal.Id, out _);
            }
        }
    }

    public Task<DeveloperOperationResult<IReadOnlyList<CodeSemanticToken>>> GetSemanticTokensAsync(
        DeveloperCodeDocument document, string currentText, CancellationToken cancellationToken = default) =>
        ReadAsync(document,
            (target, token) => _languageService.GetSemanticTokensAsync(target.WorkspaceRoot, target.RelativePath, currentText, token), cancellationToken, capability => capability.SemanticTokens, "semantic tokens");

    /// <summary>Prepare the maintained language provider's semantic rename for review. The SAME
    /// immutable proposal custody and Home-gated ApplyCodeActionAsync own any later mutation.</summary>
    public async Task<DeveloperOperationResult<CodeActionProposal>> PreviewRenameSymbolAsync(DeveloperCodeDocument document,
        string currentText, Haven.Core.CodePosition position, string newName, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        if (document is null || document.Validate() is { } || currentText is null || position is null ||
            position.Line < 0 || position.Character < 0 || string.IsNullOrWhiteSpace(newName) ||
            newName.Trim().Length > 256 || newName.Any(char.IsControl))
            return Failure<CodeActionProposal>(DeveloperOperationErrorCode.InvalidInput,
                "A current code document, source position and valid new symbol name are required.", document?.FileId.ToString("D") ?? "", requestId);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capabilities = await _languageService.GetCapabilitiesAsync(document.WorkspaceRoot, document.RelativePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!capabilities.Rename)
                return Failure<CodeActionProposal>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The active language provider does not support symbol rename.", document.FileId.ToString("D"), requestId);
            CodeActionProposal proposal;
            try
            {
                proposal = await _languageService.PreviewRenameSymbolAsync(document.WorkspaceRoot, document.RelativePath,
                    currentText, position, newName, cancellationToken).ConfigureAwait(false);
            }
            catch (NotSupportedException unavailable)
            { return Failure<CodeActionProposal>(DeveloperOperationErrorCode.CapabilityUnavailable, unavailable.Message, document.FileId.ToString("D"), requestId); }
            cancellationToken.ThrowIfCancellationRequested();
            var retained = RetainPreparedActions(document, [proposal], requestId, cancellationToken);
            if (!retained.Succeeded)
            {
                var error = retained.Error!;
                return DeveloperOperationResult<CodeActionProposal>.Failure(error.Code, error.Message, error.TargetId, error.Recoverable, error.Retryable, requestId);
            }
            return DeveloperOperationResult<CodeActionProposal>.Success(retained.Value![0], requestId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { return MapFailure<CodeActionProposal>(error, document, requestId); }
    }

    /// <summary>
    /// Immediate rename remains unavailable on this compatibility entry point. Use the typed
    /// PreviewRenameSymbolAsync path and the existing Home-gated proposal apply operation.
    /// </summary>
    public DeveloperOperationResult<DeveloperWorkspaceMutationReceipt> RenameSymbolRequiresPreview(
        DeveloperCodeDocument document) =>
        DeveloperOperationResult<DeveloperWorkspaceMutationReceipt>.Failure(
            DeveloperOperationErrorCode.CapabilityUnavailable,
            "Use PreviewRenameSymbolAsync to review the real workspace edit before requesting Home-approved application.",
            document?.FileId.ToString("D") ?? string.Empty);

    private async Task<DeveloperOperationResult<T>> ReadAsync<T>(
        DeveloperCodeDocument document,
        Func<DeveloperCodeDocument, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken, Func<LanguageServerCapabilities, bool>? supports = null, string? feature = null)
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
            cancellationToken.ThrowIfCancellationRequested();
            if (supports is not null)
            {
                var capabilities = await _languageService.GetCapabilitiesAsync(document.WorkspaceRoot, document.RelativePath, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!supports(capabilities))
                    return Failure<T>(DeveloperOperationErrorCode.CapabilityUnavailable,
                        $"The active language provider does not support {feature}. Configure a supporting language server.", document.FileId.ToString("D"), requestId);
            }
            var result = await operation(document, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
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
            if (!_applyingActions.Contains(id) && prepared.CreatedAt < cutoff) _preparedActions.TryRemove(id, out _);
    }

    private void TrimPreparedActions()
    {
        if (_preparedActions.Count <= MaximumPreparedProposals) return;
        foreach (var (id, _) in _preparedActions.Where(pair => !_applyingActions.Contains(pair.Key))
                     .OrderBy(static pair => pair.Value.CreatedAt)
                     .Take(_preparedActions.Count - MaximumPreparedProposals))
            _preparedActions.TryRemove(id, out _);
    }

    private sealed record PreparedCodeAction(
        DeveloperCodeDocument Document,
        CodeActionProposal Proposal,
        DateTimeOffset CreatedAt);
}
