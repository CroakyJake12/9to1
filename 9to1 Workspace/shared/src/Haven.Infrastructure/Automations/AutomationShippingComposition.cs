using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
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
/// This requires the Home composition to have been built with the bounded automation run/effect resolver
/// and action catalogue (see <see cref="WindowsHomeDomainServiceCollectionExtensions.AutomationRunResourceResolvers"/>
/// and <see cref="WindowsHomeDomainServiceCollectionExtensions.AutomationRunActionPolicies"/>). This method wires
/// the same canonical object/action authority from the SAME actual Home tuple.
/// </summary>
public static class AutomationShippingComposition
{
    /// <summary>
    /// Registers the real Home-backed <see cref="IAutomationRunAuthority"/> from the SAME actual Home tuple,
    /// the canonical run/effect resource resolver, the maintained claim/continuation store aliases, the
    /// durable adapter into the shared registry adapter set, the shared <see cref="NodeGraphRuntimeRegistry"/>
    /// itself, and a typed fresh/resume Run/scheduler caller facade over that registry. Call this exactly
    /// once after <see cref="WindowsHomeDomainServiceCollectionExtensions.AddHavenOwnedWindowsHomeDomain"/> in
    /// the shipping composition. When <paramref name="compile"/> is omitted the production schema/bindings
    /// from <see cref="HomeAutomationNodeGraphSchemas"/> are used.
    /// </summary>
    public static IServiceCollection AddAutomationShipping(this IServiceCollection services,
        HomeNativeWindowsComposition home, NodeGraphAutomationAdapter? compile = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(home);
        var schemasResult = HomeAutomationNodeGraphSchemas.Create();
        compile ??= new NodeGraphAutomationAdapter(schemasResult.Schemas, schemasResult.Bindings);

        // The maintained AutomationRepository singleton is the SAME store for runs, continuation and claims.
        if (!services.Any(row => row.ServiceType == typeof(IAutomationRunClaimStore)))
            services.AddSingleton<IAutomationRunClaimStore>(provider =>
                (IAutomationRunClaimStore)provider.GetRequiredService<IAutomationRepository>());
        if (!services.Any(row => row.ServiceType == typeof(IAutomationRunContinuationStore)))
            services.AddSingleton<IAutomationRunContinuationStore>(provider =>
                (IAutomationRunContinuationStore)provider.GetRequiredService<IAutomationRepository>());

        // The canonical graph repository is the SAME Home state store owner (deferred, like the Home resolver
        // pattern), so the durable adapter resolves the real maintained graph at runtime.
        if (!services.Any(row => row.ServiceType == typeof(IVersionedNodeGraphRepository)))
            services.AddSingleton<IVersionedNodeGraphRepository>(provider => new HomeVersionedNodeGraphRepository(
                provider.GetRequiredService<IHomeCoreStateStore>(),
                HomeAutomationNodeGraphSchemas.Create().Schemas));

        // The bounded canonical run/effect resolver, repository-backed and owner/current-profile checked.
        // It is registered here for DI consumers; the Home composition itself receives the same resolver via
        // AutomationRunResourceResolvers() and the production wiring in the shipping host.
        if (!services.Any(row => row.ServiceType == typeof(ICanonicalResourceAccessResolver) &&
            row.ImplementationType == typeof(HomeAutomationRunResourceResolver)))
            services.AddSingleton<ICanonicalResourceAccessResolver>(provider => new HomeAutomationRunResourceResolver(
                () => provider.GetService<IAutomationRepository>(),
                () => provider.GetService<IVersionedNodeGraphRepository>()));

        services.AddHavenAutomationRunAuthority(home);
        services.AddAutomationNodeGraphDurableAdapter(compile);

        // The actual maintained shared registry: SAME production schema + every registered adapter. This is
        // the live registry the typed fresh/resume caller consumes; it is not an isolated test registry.
        services.AddSingleton(provider => new NodeGraphRuntimeRegistry(
            schemasResult.Schemas,
            provider.GetServices<INodeGraphRuntimeAdapter>()));

        // Typed fresh/resume Run/scheduler callers over the shared registry. This is the production seam a
        // host invokes to activate or resume an automation graph with the real Graph type.
        services.AddSingleton<IAutomationRunGateway>(provider =>
            new NodeGraphAutomationRunGateway(provider.GetRequiredService<NodeGraphRuntimeRegistry>()));

        // The real shipping consumer: the production Run/scheduler/resume entry points drive this gateway
        // (SAME shared registry + durable adapter + real Home authority), and it owns the retained Home
        // audit drain for clean close. This is the production caller the gateway previously lacked.
        services.AddSingleton(provider => new AutomationShippingScheduler(
            provider.GetRequiredService<AutomationShippingComposition.IAutomationRunGateway>(),
            provider.GetRequiredService<IAutomationRepository>(),
            provider.GetRequiredService<ScheduledTaskRunner>(),
            provider.GetService<IAutomationRunAuditDrain>(),
            provider.GetService<IAuthenticatedResourceActorSource>()));

        return services;
    }

    /// <summary>Typed fresh/resume caller facade over the shared <see cref="NodeGraphRuntimeRegistry"/>. It
    /// binds the automation GraphType and exposes the registry's ActivateAsync/ResumeAsync as the owner run/
    /// scheduler entry points, so a shipping host never reaches into the registry directly.</summary>
    public interface IAutomationRunGateway
    {
        ValueTask<GraphActivationResult> ActivateAsync(GraphActivationRequest request, CancellationToken cancellationToken);
        ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
            GraphActivationRequest request, CancellationToken cancellationToken);
    }

    private sealed class NodeGraphAutomationRunGateway(NodeGraphRuntimeRegistry registry) : IAutomationRunGateway
    {
        public ValueTask<GraphActivationResult> ActivateAsync(GraphActivationRequest request, CancellationToken cancellationToken)
            => registry.ActivateAsync(EnsureAutomationGraph(request), cancellationToken);

        public ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
            GraphActivationRequest request, CancellationToken cancellationToken)
            => registry.ResumeAsync(continuation, EnsureAutomationGraph(request), cancellationToken);

        private static GraphActivationRequest EnsureAutomationGraph(GraphActivationRequest request) =>
            string.Equals(request.GraphType, AutomationNodeGraphDurableAdapter.GraphTypeValue, StringComparison.Ordinal)
                ? request
                : request with { GraphType = AutomationNodeGraphDurableAdapter.GraphTypeValue };
    }
}
