using Haven.Application.Automations;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// The production shipping registration for the durable Automations Node Graph adapter and its real Home
/// caller/action authority. A shipping host MUST call <see cref="AddAutomationShipping"/> from its real
/// composition (the guarded App integration hunk) so fresh AND resume entry points route through the SAME
/// shared registry/owner with actual Home currentness before admission and immediately before every
/// protected effect.
///
/// This requires that the Home composition was built with the bounded automation run/effect resolver and
/// action catalogue (see <see cref="WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers"/>
/// and <see cref="WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies"/>). Without the
/// authority the adapter preserves its prior behavior, but a shipping host must not omit it.
/// </summary>
public static class AutomationShippingComposition
{
    /// <summary>
    /// Registers the real Home-backed <see cref="IAutomationRunAuthority"/> from the SAME actual Home tuple
    /// and the durable adapter into the shared registry adapter set. Call this exactly once after
    /// <see cref="WindowsHomeDomainServiceCollectionExtensions.AddHavenOwnedWindowsHomeDomain"/> in the
    /// shipping composition.
    /// </summary>
    public static IServiceCollection AddAutomationShipping(this IServiceCollection services,
        HomeNativeWindowsComposition home, NodeGraphAutomationAdapter compile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(compile);
        services.AddHavenAutomationRunAuthority(home);
        // authority resolves from DI (registered above); approvalAuth resolves if the host wired it.
        services.AddAutomationNodeGraphDurableAdapter(compile);
        return services;
    }
}
