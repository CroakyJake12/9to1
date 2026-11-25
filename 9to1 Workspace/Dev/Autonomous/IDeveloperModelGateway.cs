namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Platform-neutral gateway to the resident Dulche inference and agent runtime.
/// Implementations observe live models, count exact prompt tokens, stream content and
/// reasoning output, and stop individual runs without altering model or GPU placement.
/// </summary>
public interface IDeveloperModelGateway
{
    Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<int> CountTokensAsync(
        string modelId,
        IReadOnlyList<DeveloperChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<DeveloperCompletionResult> StreamCompletionAsync(
        DeveloperCompletionRequest request,
        Action<DeveloperStreamChunk>? onChunk = null,
        Action<string>? onRunStarted = null,
        CancellationToken cancellationToken = default);

    Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default);
}
