using Haven.Core;

namespace Haven.Application;

/// <summary>Observational eligibility over the SAME already captured, ordered raw catalogue.
/// This performs no catalogue I/O, fallback execution, actor/context authorization or lease grant.
/// Canonical dispatch still requires its existing issued attempt, policy and original frame.</summary>
public interface IProviderCatalogueEligibility
{
    IReadOnlyList<ProviderModelDescriptor> ObserveOriginalCatalogueEligibility(
        IReadOnlyList<ProviderModelDescriptor> alreadyOrderedCandidates,
        IReadOnlySet<ToolCapability> actualRequiredCapabilities, ModelRoutingPolicy actualPolicy);
}
