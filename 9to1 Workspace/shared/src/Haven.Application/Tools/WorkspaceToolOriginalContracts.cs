using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum WorkspaceToolEffectKind { AtomicWrite, RollbackDelete, ProcessStart }

/// <summary>Private original task/call issuer. Metadata never grants effects by itself.
/// Acquire supplies the SAME attempt retirement pin; checks requiring I/O run before owner locks.
/// RunOriginalEffect uses the SAME central permission gate as Grant/Revoke/SetPolicy and only
/// executes a finite synchronous native Move/Delete/Start operation. No await or observer callback.</summary>
public interface IWorkspaceToolFinalFence
{
    TaskRunAttemptAdmission OriginalAttempt { get; }
    Guid ActionId { get; }
    string CanonicalWorkspaceRoot { get; }
    OllamaToolCall OriginalCall { get; }
    ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken);
    void DemandOriginalEffect(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind,
        string canonicalTarget, string exactContentOrRequestSha256);
    T RunOriginalEffect<T>(string canonicalWorkspaceRoot, WorkspaceToolEffectKind kind,
        string canonicalTarget, string exactContentOrRequestSha256, Func<T> originalNativeEffect);
}

/// <summary>Optional SAME-action read fence. Awaited validation stays outside the finite
/// central permission/native gate; reads do not manufacture a mutation effect or receipt.</summary>
public interface IWorkspaceOriginalReadFence : IWorkspaceToolFinalFence
{
    // Fresh actual canonical action/actor/model I/O runs outside the finite native permission gate.
    ValueTask RevalidateOriginalReadAsync(CancellationToken cancellationToken);
    T RunOriginalRead<T>(string canonicalWorkspaceRoot, string canonicalTarget,
        Func<T> originalNativeRead, CancellationToken cancellationToken);
}

/// <summary>Configured private issuer registry, not a caller-supplied validation Boolean.
/// Physical sources accept only a fence actually issued by this SAME registered authority.</summary>
public interface IWorkspaceToolFinalFenceAuthority
{
    bool IsIssuedOriginal(IWorkspaceToolFinalFence originalFence);
}

/// <summary>Actual physical service issues per-call wrappers over its existing native implementation.
/// Copied outcome objects/strings cannot pass its private issuer registry checks.</summary>
public interface IWorkspaceOriginalInvocationSource : IWorkspaceToolService
{
    IWorkspaceOriginalInvocation AcquireOriginalInvocation(IWorkspaceToolFinalFence originalFence);
    bool IsIssuedOriginal(IWorkspaceOriginalInvocation originalInvocation);
    bool ValidateOriginalOutcome(IWorkspaceOriginalInvocation originalInvocation, WorkspaceToolPhysicalOutcome originalOutcome);
}

/// <summary>Deny-only availability from the actual physical owner, never a tool-name whitelist.
/// Acquisition still requires its private original fence and the supported native ABI.</summary>
public interface IWorkspaceOriginalTraversalSource : IWorkspaceOriginalInvocationSource
{
    bool SupportsOriginalTraversal(string toolName);
}

public sealed record WorkspaceOriginalDirectoryEntry(string RelativePath, bool IsDirectory, long Size);
public sealed record WorkspaceOriginalTextMatch(string RelativePath, int Line, string Text);

/// <summary>Read observations only: explicit bounded coverage, no mutation receipt or grant.</summary>
public sealed record WorkspaceOriginalTraversalResult(
    IReadOnlyList<WorkspaceOriginalDirectoryEntry> Entries, IReadOnlyList<WorkspaceOriginalTextMatch> Matches,
    bool EntryLimitReached, bool DepthLimitReached, bool ResultLimitReached, bool ByteLimitReached,
    int IgnoredDirectoryCount, int LargeFileCount, int BinaryFileCount, bool ValidationLimitReached = false);

public interface IWorkspaceOriginalTraversalService : IWorkspaceToolService
{
    Task<WorkspaceOriginalTraversalResult> ListOriginalFilesAsync(string workspaceRoot, string relativePath,
        int maxDepth, CancellationToken cancellationToken);
    Task<WorkspaceOriginalTraversalResult> SearchOriginalFilesAsync(string workspaceRoot, string relativePath,
        string query, int maxResults, CancellationToken cancellationToken);
}

public interface IWorkspaceOriginalInvocation : IAsyncDisposable
{
    IWorkspaceToolService Tools { get; }
    Task<WorkspaceToolPhysicalOutcome> CompleteOriginalAsync(bool effectBodySucceeded, CancellationToken cancellationToken);
    Task CloseAndDrainAsync();
}

/// <summary>Optional rollback-delete extension used only by the SAME existing change-set engine.
/// The producer must prove this invocation created the SAME still-current file before deleting;
/// mismatch/unknown write outcome is retained, never an excuse to delete another writer's file.</summary>
public interface IWorkspaceOriginalRollbackService : IWorkspaceToolService
{
    Task DeleteOriginalCreatedFileAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken);
}

public sealed record WorkspaceToolPhysicalEffect(WorkspaceToolEffectKind Kind, string CanonicalTarget,
    string ContentOrRequestSha256, bool Admitted, bool EffectKnown, bool TerminalOutcomeKnown,
    int? ProcessId = null, int? ExitCode = null);

/// <summary>Observation produced by the actual physical owner, independently of later UI/history.
/// Acceptance additionally requires its private ValidateOriginalOutcome reference check. A pending
/// or failed terminal/cleanup task never becomes accepted work merely from an output/hash or exit0.</summary>
public sealed record WorkspaceToolPhysicalOutcome(string? OriginalReceiptReference,
    IReadOnlyList<WorkspaceToolPhysicalEffect> Effects, IReadOnlyList<Exception> OriginalErrors,
    bool KnownNoEffect, bool OutcomeUnknown);

/// <summary>Runtime requires an actual privately issued original preparation before using this scope.</summary>
public interface IWorkspaceToolActionPreparation : ITaskRunToolActionPreparation
{
    string CanonicalWorkspaceRoot { get; }
    OllamaToolCall OriginalCall { get; }
    IWorkspaceOriginalInvocation? OriginalInvocation { get; }
    bool IsIssuedOriginalRuntime(IWorkspaceToolService actualService, string actualWorkspaceRoot,
        OllamaToolCall actualCall);
    Task<WorkspaceToolResult> RunOriginalRuntimeAsync(IWorkspaceToolService actualService,
        string actualWorkspaceRoot, OllamaToolCall actualCall,
        Func<CancellationToken, Task<WorkspaceToolResult>> originalRuntimeBody, CancellationToken cancellationToken);
}

public static class WorkspaceToolOriginalDigest
{
    public static string Text(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    public static string Call(OllamaToolCall call) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        call.Name, call.Id,
        Arguments = call.Arguments.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new { value.Key, Value = value.Value }).ToArray()
    })));
    public static string Process(ProcessRequest request)
    {
        if (request.ArgumentList is { } vector)
        {
            if (!string.IsNullOrEmpty(request.Arguments) || vector.Any(value => value is null || value.Contains('\0')))
                throw new ArgumentException("An exact process vector cannot coexist with legacy Arguments or contain NUL.");
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                request.FileName, request.Arguments, ArgumentList = vector.ToArray(), request.WorkingDirectory,
                TimeoutTicks = request.Timeout.Ticks, request.DetachGui,
                Environment = request.Environment?.OrderBy(value => value.Key, StringComparer.Ordinal)
                    .Select(value => new { value.Key, value.Value }).ToArray()
            })));
        }
        // Preserve every original legacy/Windows digest byte when no vector was supplied.
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        request.FileName, request.Arguments, request.WorkingDirectory, TimeoutTicks = request.Timeout.Ticks,
        request.DetachGui,
        Environment = request.Environment?.OrderBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new { value.Key, value.Value }).ToArray()
        })));
    }
}

/// <summary>Additive source proof; never derived from ToolActivity.Succeeded.</summary>
public sealed record WorkspaceToolOriginalRuntimeOutcome(IWorkspaceOriginalInvocation OriginalInvocation,
    WorkspaceToolPhysicalOutcome PhysicalOutcome, Exception? OriginalRuntimeError);
