namespace HavenOS.Apps.MiniComputer;

/// <summary>Pure references to this maintained engine's actual constructor owners.
/// These observations do not issue provider, catalogue, VM or Home permission.
/// Hosts must separately obtain source-owned current admission for each operation.</summary>
public sealed partial class MiniComputerEngine
{
    public IVirtualisationProviderRegistry OriginalProviderRegistry => _providers;
    public IMiniComputerCatalogStore OriginalCatalogStore => _store;
    public IHighRiskActionAuthorizer OriginalHighRiskActionAuthorizer => _authorizer;
}
