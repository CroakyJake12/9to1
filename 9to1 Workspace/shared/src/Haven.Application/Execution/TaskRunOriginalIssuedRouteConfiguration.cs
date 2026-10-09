using System.Text.Json.Serialization;
using Haven.Core;
namespace Haven.Application;

/// <summary>Historical membership of the actual configuration captured by the original
/// permission issuer. This is neither a current lease/permission nor the later wire configuration.</summary>
public sealed class TaskRunOriginalIssuedRouteConfiguration
{
    internal TaskRunOriginalIssuedRouteConfiguration(TaskRunAttemptAdmission admission, ProviderConfiguration configuration)
    { OriginalAdmission = admission; OriginalCapturedConfiguration = configuration; }
    [JsonIgnore] internal TaskRunAttemptAdmission OriginalAdmission { get; }
    [JsonIgnore] internal ProviderConfiguration OriginalCapturedConfiguration { get; }
    public Guid TaskId => OriginalAdmission.Snapshot.TaskId;
    public Guid ExecutionId => OriginalAdmission.Snapshot.ExecutionId;
    public Guid AttemptId => OriginalAdmission.AttemptId;
    public string ProviderId => OriginalCapturedConfiguration.Id;
    public ModelProviderKind ProviderKind => OriginalCapturedConfiguration.Kind;
}
public interface ITaskRunOriginalIssuedRouteConfigurationSource
{
    TaskRunOriginalIssuedRouteConfiguration? TryObserveOriginalIssuedRouteConfiguration(TaskRunAttemptAdmission sameAdmission);
    bool IsIssuedOriginalRouteConfiguration(TaskRunOriginalIssuedRouteConfiguration sameObservation, TaskRunAttemptAdmission sameAdmission);
}
