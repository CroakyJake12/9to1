using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

public enum CloudflareOperationKind { KvList, KvInspect, KvCreate, KvMarkerPut, KvMarkerGet, KvMarkerDelete, KvDelete, WorkerRead, WorkerDeploy, DnsRead, DnsWrite, SecretNamesRead, SecretWriteByReference, AccountWide, Destructive, KvMarkerVerifyAbsent }
public sealed record CloudflareToolDescriptor(string ToolName, string ActionId, CloudflareOperationKind Kind, bool IsReadOnly, bool IsImplemented);

/// <summary>Non-secret host configuration. Account selection is explicit; a connector credential is not resource ownership.</summary>
public sealed class CloudflareSavedService
{
    internal CloudflareSavedService(ExternalConnection connection, string accountId, string executeTool)
    { Connection = connection; AccountId = accountId; ExecuteTool = executeTool; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ExternalConnection Connection { get; }
    /// <summary>Configuration observation only. The genuine saved-service source must privately issue and revalidate it before dispatch.</summary>
    public static CloudflareSavedService ObserveSavedConfiguration(ExternalConnection connection, string accountId, string executeTool)
        => new(connection, accountId, executeTool);
    public string AccountId { get; }
    public string ExecuteTool { get; }
    public string CredentialReference => ExternalConnectionNaming.SecretProviderId(Connection.Id) + "/" + ExternalConnectionNaming.OAuthTokenSecretName;
}
public interface ICloudflareSavedServiceSource
{
    Task<CloudflareSavedService> AcquireOriginalAsync(CancellationToken cancellationToken);
    Task RevalidateOriginalAsync(CloudflareSavedService sameService, CancellationToken cancellationToken);
}

/// <summary>Compiled by the trusted closed catalogue, never arbitrary model code, URL, method or bearer material.</summary>
public sealed class CloudflareCompiledInvocation
{
    internal CloudflareCompiledInvocation(CloudflareSavedService service, CloudflareToolDescriptor descriptor, Guid task, Guid run, Guid action,
        string callDigest, string key, string? ns, string code, string marker)
    { Service = service; Descriptor = descriptor; TaskId = task; ExecutionId = run; ActionId = action; CallDigest = callDigest;
        OperationKey = key; NamespaceId = ns; Code = code; Marker = marker; }
    public CloudflareSavedService Service { get; }
    public CloudflareToolDescriptor Descriptor { get; }
    public Guid TaskId { get; }
    public Guid ExecutionId { get; }
    public Guid ActionId { get; }
    public string CallDigest { get; }
    public string OperationKey { get; }
    public string? NamespaceId { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Code { get; }
    internal string Marker { get; }
    public string AccountResourceId => "cloudflare:account:" + Service.AccountId;
    public string? NamespaceResourceId => NamespaceId is null ? null : "cloudflare:kv:" + Service.AccountId + "/" + NamespaceId;
}

/// <summary>Actual canonical admission/action acknowledgment, not a public ID grant.</summary>
public sealed class CloudflareOriginalTaskBinding
{
    internal CloudflareOriginalTaskBinding(ITaskRunToolActionPreparation preparation, TaskRunOriginalActionAdmission actionAdmission, TaskExecutionSnapshot acknowledged, CloudflareCompiledInvocation invocation)
    { ActionAdmission = actionAdmission; OriginalPreparation = preparation; OriginalAttempt = preparation.OriginalAttempt; AcknowledgedSnapshot = acknowledged; Invocation = invocation; }
    public TaskRunOriginalActionAdmission ActionAdmission { get; }
    public ITaskRunToolActionPreparation OriginalPreparation { get; }
    public TaskRunAttemptAdmission OriginalAttempt { get; }
    public TaskExecutionSnapshot AcknowledgedSnapshot { get; }
    public CloudflareCompiledInvocation Invocation { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<Action>? OriginalCallerCallback { get; internal init; }
}

/// <summary>Required genuine Home app-resource owner. Prepare uses actor/ACL/approval; Validate checks private issuing reference.
/// No default implementation or metadata/credential-based authority is provided.</summary>
public interface ICloudflareOriginalPermissionSource
{
    Task<ICloudflareOriginalPermission> AcquireOriginalAsync(CloudflareOriginalTaskBinding original, CancellationToken cancellationToken);
    ValueTask ValidateOriginalAsync(CloudflareOriginalTaskBinding original, ICloudflareOriginalPermission samePermission, CancellationToken cancellationToken);
}
public interface ICloudflareOriginalPermission : IAsyncDisposable
{
    ValueTask RevalidateOriginalAsync(CloudflareOriginalTaskBinding original, CancellationToken cancellationToken);
    ValueTask<ICloudflareOriginalFinalDispatch> EnterOriginalFinalDispatchAsync(CloudflareCompiledInvocation original, CancellationToken cancellationToken);
    // Pure issuing-reference/current held-entry validation only; no repository reads or awaits beneath the Home entry.
    void DemandOriginalFinalDispatch(ICloudflareOriginalFinalDispatch sameEntry, CloudflareCompiledInvocation original, CancellationToken cancellationToken);
    ValueTask RecordOriginalOutcomeAsync(CloudflareOriginalTaskBinding original, CloudflareMcpDispatchResult originalOutcome, CancellationToken cancellationToken);
}
/// <summary>Held original Home final entry. Invoke only a finite synchronous SDK task start; release before network wait.</summary>
public interface ICloudflareOriginalFinalDispatch : IAsyncDisposable
{
    T RunOriginalDispatch<T>(CloudflareCompiledInvocation sameInvocation, Func<T> finiteTaskStart, CancellationToken cancellationToken);
}
public sealed class CloudflareMcpDispatchResult
{
    public CloudflareMcpDispatchResult(JsonElement? response, bool started, IReadOnlyList<Task> tasks, IReadOnlyList<Exception> errors)
    { Response = response?.Clone(); DispatchStarted = started; OriginalTasks = tasks; OriginalErrors = errors; }
    public JsonElement? Response { get; }
    public bool DispatchStarted { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Task> OriginalTasks { get; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Exception> OriginalErrors { get; }
}
public interface ICloudflareMcpInvocationClient
{
    Task<CloudflareMcpDispatchResult> InvokeOriginalAsync(CloudflareOriginalTaskBinding original, ICloudflareSavedServiceSource sameServices, ITaskRunOriginalActionAdmissionSource sameActionClaims, ICloudflareOriginalPermission permission, CancellationToken cancellationToken);
    bool IsIssuedOriginalResult(CloudflareCompiledInvocation sameInvocation, CloudflareMcpDispatchResult sameResult);
}
public enum CloudflareObservedDisposition { ConfirmedResponse, KnownNoDispatch, Uncertain }
public sealed record CloudflareObservedOutcome(string OperationKey, CloudflareObservedDisposition Disposition, JsonElement? Data,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<Exception> OriginalErrors);

/// <summary>Known response survives an audit/cleanup fault, but the actual finite task remains Faulted and blocks fallback.
/// Neither this exception nor its outcome accepts a canonical mutation or authorizes retry.</summary>
public sealed class CloudflareOriginalOutcomeException : AggregateException
{
    internal CloudflareOriginalOutcomeException(CloudflareObservedOutcome original) : base("Original Cloudflare operation failed or remains uncertain; reconcile before replay.", original.OriginalErrors)
    { OriginalOutcome = original; }
    [System.Text.Json.Serialization.JsonIgnore]
    public CloudflareObservedOutcome OriginalOutcome { get; }
}

/// <summary>Finite original caller lifetime scope only; never authority or a receipt.
/// Native service reads explicitly propagate this callback to their post-await raw factories.</summary>
public interface ICloudflareCallerScopedSavedServiceSource : ICloudflareSavedServiceSource
{
    Task<CloudflareSavedService> AcquireCallerScopedOriginalAsync(Action<Action>? originalCallerCallback, CancellationToken token);
    Task RevalidateCallerScopedOriginalAsync(CloudflareSavedService original, Action<Action>? originalCallerCallback, CancellationToken token);
}
public static class CloudflareCallerScopedServiceRead
{
    public static Task<CloudflareSavedService> AcquireOriginalAsync(ICloudflareSavedServiceSource source, Action<Action>? callback, CancellationToken token)
        => callback is null ? source.AcquireOriginalAsync(token)
            : source is ICloudflareCallerScopedSavedServiceSource actual ? actual.AcquireCallerScopedOriginalAsync(callback, token)
            : throw new InvalidOperationException("The configured canonical CF service lacks its maintained caller-scope port.");
    public static Task RevalidateOriginalAsync(ICloudflareSavedServiceSource source, CloudflareSavedService service, Action<Action>? callback, CancellationToken token)
        => callback is null ? source.RevalidateOriginalAsync(service, token)
            : source is ICloudflareCallerScopedSavedServiceSource actual ? actual.RevalidateCallerScopedOriginalAsync(service, callback, token)
            : throw new InvalidOperationException("The configured canonical CF service lacks its maintained caller-scope port.");
}
