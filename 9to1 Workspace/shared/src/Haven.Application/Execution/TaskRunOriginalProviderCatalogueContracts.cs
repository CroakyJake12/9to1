using Haven.Core;
namespace Haven.Application;

/// <summary>SAME configured provider's actual nested catalogue factories. Caller phase/raw
/// custody supplies no model eligibility, artifact residency, route or permission grant.</summary>
public interface ITaskRunOriginalProviderCatalogueSource
{
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalTaskSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
