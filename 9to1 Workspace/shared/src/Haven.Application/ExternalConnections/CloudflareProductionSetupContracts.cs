using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

public enum CloudflareSetupStage { HomeProfileRequired, SavedOAuthConnectionRequired, AccountSelectionRequired, ApprovalRequired, ConfigurationConflict, NamespaceRecoveryRequired, RawValueTransportUnsupported }
public sealed class CloudflareSetupRequiredException : InvalidOperationException
{
    public CloudflareSetupRequiredException(CloudflareSetupStage stage, string code, string message, string? reviewRequestId = null)
        : base(message) { Stage = stage; Code = code; ReviewRequestId = reviewRequestId; }
    public CloudflareSetupStage Stage { get; }
    public string Code { get; }
    public string? ReviewRequestId { get; }
}

/// <summary>Nonsecret metadata only. A saved connection and Home review remain separate authorities.</summary>
public sealed record CloudflareSetupSelection(Guid ConnectionId, string Endpoint, string AccountId, string ExecuteTool);
public sealed record CloudflareSetupObservation(bool Configured, string Code, string? ReviewRequestId, long Revision);

/// <summary>Only the actual Home app owner may issue this setup handle. Public metadata is not a decision.</summary>
public interface ICloudflareOriginalSetupReview
{
    string RequestId { get; }
    Task<CloudflareSetupObservation> SubmitOriginalAsync(CancellationToken cancellationToken);
    Task<CloudflareSetupObservation> CommitOriginalAsync(CancellationToken cancellationToken);
}
public interface ICloudflareProductionSetupSource
{
    Task<CloudflareSetupObservation> GetSetupAsync(CancellationToken cancellationToken);
    Task<ICloudflareOriginalSetupReview> PrepareSetupAsync(CloudflareSetupSelection explicitSelection,
        long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>Actual original fixed runtime call; a copied preparation cannot dispatch.</summary>
public interface ICloudflareOriginalToolPreparation : ITaskRunToolActionPreparation
{
    CloudflareCompiledInvocation OriginalInvocation { get; }
    Task<WorkspaceToolResult> RunOriginalRuntimeAsync(CloudflareTypedToolRuntime sameRuntime,
        OllamaToolCall sameCall, CancellationToken cancellationToken);
}

/// <summary>The issuing Home owner validates an actual retained runtime result before recording
/// a namespace created or reconciled for this exact Task/Run. No caller-provided ID creates ownership.</summary>
public interface ICloudflareOriginalNamespaceOwner
{
    ValueTask DemandOriginalNamespaceAsync(CloudflareCompiledInvocation sameInvocation, CancellationToken cancellationToken);
}

/// <summary>Actual permission issuer housekeeping after genuine canonical ACK and healthy original close.
/// This port releases custody only; it grants no operation and cannot reconcile an uncertain effect.</summary>
public interface ICloudflareOriginalPermissionRetirement
{
    Task RetireAcknowledgedOriginalAsync(CloudflareCompiledInvocation sameInvocation,
        ICloudflareOriginalPermission samePermission, CancellationToken cancellationToken);
}
