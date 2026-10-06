using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime;

public sealed partial class ModelRouteResolver
{
    /// <summary>Metadata eligibility only, before the existing finite dispatch/context fence.
    /// The caller owns its original ordering; there is no second automatic fallback loop.</summary>
    public IReadOnlyList<ProviderModelDescriptor> ObserveOriginalCatalogueEligibility(
        IReadOnlyList<ProviderModelDescriptor> alreadyOrderedCandidates,
        IReadOnlySet<ToolCapability> actualRequiredCapabilities, ModelRoutingPolicy actualPolicy)
    {
        ArgumentNullException.ThrowIfNull(alreadyOrderedCandidates);
        ArgumentNullException.ThrowIfNull(actualRequiredCapabilities);
        ArgumentNullException.ThrowIfNull(actualPolicy);
        var required = actualRequiredCapabilities.Select(capability => capability.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var policy = new ProviderPolicy(AllowLocal: true, AllowRemote: actualPolicy.AllowCloud,
            AllowCloud: actualPolicy.AllowCloud, AllowFallback: false, RequiredCapabilities: required);
        var eligible = new List<ProviderModelDescriptor>();
        foreach (var original in alreadyOrderedCandidates)
        {
            var identity = new ModelIdentity(original.ProviderId, original.Name);
            var route = new ModelRoute("haven-original-provider-catalogue", 1, [identity], policy);
            // No private-context permission is asserted here. The existing context owner performs
            // its actual source capture, fresh revalidation and invocation fence before raw egress.
            var observation = ResolveObservedCatalogue(route, identity, [original]);
            if (observation.Succeeded) eligible.Add(original);
        }
        return eligible;
    }
}
