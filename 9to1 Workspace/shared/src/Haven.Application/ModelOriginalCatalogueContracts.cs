using Haven.Core;

namespace Haven.Application;

/// <summary>Finite original catalogue reads over the SAME maintained registry and
/// eligible provider Tasks. Catalogue observations grant no model execution,
/// credentials, tool or resource authority.</summary>
public interface IOriginalModelCatalogueSource
{
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalSourceAsync(
        ModelCataloguePolicy policy, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken);
}
