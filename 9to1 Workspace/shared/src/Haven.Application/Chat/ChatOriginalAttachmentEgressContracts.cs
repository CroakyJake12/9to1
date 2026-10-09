using Haven.Core;

namespace Haven.Application;

/// <summary>Live SAME-Chat selection. Serialized lineage is deliberately insufficient.</summary>
public sealed class ChatOriginalAttachmentInvocation
{
    internal ChatOriginalAttachmentInvocation(ChatSessionService chat, IChatOriginalAttachmentInput input,
        ChatOriginalAttachmentRequest request) { Chat = chat; Input = input; Request = request; }
    internal ChatSessionService Chat { get; }
    public IChatOriginalAttachmentInput Input { get; }
    public ChatOriginalAttachmentRequest Request { get; }
}

/// <summary>Issued by the configured context owner for one actual captured wire request,
/// current Task attempt and selected model. None of the observations issue a grant.</summary>
public sealed class TaskOriginalAttachmentEgressRequest
{
    internal TaskOriginalAttachmentEgressRequest(TaskRunConfiguredCloudAdmissionSource source,
        TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot, ProviderExecutionContext context,
        ChatOriginalAttachmentInvocation invocation, string payloadSha256)
    { Source = source; Admission = admission; Snapshot = snapshot; Context = context;
      Invocation = invocation; PayloadSha256 = payloadSha256; }
    internal TaskRunConfiguredCloudAdmissionSource Source { get; }
    public TaskRunAttemptAdmission Admission { get; }
    public TaskExecutionSnapshot Snapshot { get; }
    public ProviderExecutionContext Context { get; }
    public ChatOriginalAttachmentInvocation Invocation { get; }
    public string PayloadSha256 { get; }
}

public interface ITaskOriginalAttachmentEgressSource
{
    bool HasOriginalEgressComposition(TaskRunConfiguredCloudAdmissionSource source);
    bool CanUseOriginalTaskAttachmentEgress(IChatOriginalAttachmentInput input);
    Task<ITaskOriginalAttachmentEgressLease> AcquireOriginalEgressWithinSourceAsync(
        TaskOriginalAttachmentEgressRequest request, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalEgressLease(TaskOriginalAttachmentEgressRequest request, ITaskOriginalAttachmentEgressLease lease);
    bool IsAcknowledgedOriginalEgressRefusal(Task actualAcquisition);
}

/// <summary>One request only. Current domain approval is additional to actual model/provider
/// permission. The finite start fence returns the original value unchanged. The caller retains
/// and joins the provider body before closing this lease; closing never stops the business Task.</summary>
public interface ITaskOriginalAttachmentEgressLease : IAsyncDisposable
{
    Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token);
    T RunOriginalInvocation<T>(Func<T> originalRawStart);
    Task? OriginalClose { get; }
    Task CloseAndDrainOriginalAsync();
}

public interface ICanonicalAttachmentEgressIntent
{
    Guid OriginalRequestId { get; }
    AuthenticatedResourceActor OriginalHomeActor { get; }
    TaskExecutionOwnerBinding OriginalTaskOwner { get; }
    Guid AttemptId { get; }
    string ProviderId { get; }
    string ModelId { get; }
    string PayloadSha256 { get; }
    ChatOriginalAttachmentLineage OriginalLineage { get; }
}

/// <summary>The actual input owner alone creates this intent after independently checking
/// its current Home/Den READ and protected accepted-message/import snapshot.</summary>
public interface ICanonicalAttachmentEgressProducer
{
    bool IsIssuedOriginalEgressIntent(ICanonicalAttachmentEgressIntent intent);
    string GetOriginalEgressIntentDigest(ICanonicalAttachmentEgressIntent intent);
    Task ValidateOriginalEgressIntentWithinSourceAsync(ICanonicalAttachmentEgressIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public interface ICanonicalAttachmentHomeEgressSource
{
    ICanonicalAttachmentEgressProducer OriginalProducer { get; }
    Task<ICanonicalAttachmentHomeEgressLease> AcquireOriginalEgressWithinSourceAsync(
        ICanonicalAttachmentEgressIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token);
    bool IsIssuedOriginalEgressLease(ICanonicalAttachmentEgressIntent intent, ICanonicalAttachmentHomeEgressLease lease);
    bool IsAcknowledgedOriginalEgressRefusal(Task actualAcquisition);
}

/// <summary>Separately issued manual disclose approval. Acquiring the finite Home entry
/// follows fresh domain validation. Starting releases that entry without awaiting the remote
/// body; the exact release and completion remain independently owned until close.</summary>
public interface ICanonicalAttachmentHomeEgressLease : ITaskOriginalAttachmentEgressLease
{
    string OriginalApprovalRequestId { get; }
    Task AcquireOriginalInvocationEntryWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task ReleaseOriginalInvocationEntryWithinSourceAsync(Action<Action> scope, Action<Task> retain);
}
