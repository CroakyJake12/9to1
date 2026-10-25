using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Validates only dependencies with existing original owners. It never dispatches a
/// model, reads resource bodies, opens Home approvals, or treats a source reference as a grant.</summary>
internal sealed class OriginalSpaceDraftActivationValidator(IServiceProvider services, SpaceRegistry expectedRegistry) : ISpaceDraftActivationValidator
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly SpaceRegistry _expectedRegistry = expectedRegistry ?? throw new ArgumentNullException(nameof(expectedRegistry));

    public async Task<SpaceDraftActivationValidation> ValidateAsync(SpaceDefinition current,
        SpaceConfigurationDocument draft, Action<Action> acquireOriginal, Action<Task> retainOriginal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(acquireOriginal);
        ArgumentNullException.ThrowIfNull(retainOriginal);
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalCallerCallback(acquireOriginal);
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            void Own(Action body) => originals.Invoke(() => { body(); return true; });
            void Retain(Task actual) => originals.Track(actual);
            var registry = originals.Invoke(() => _services.GetRequiredService<SpaceRegistry>());
            if (!ReferenceEquals(registry, _expectedRegistry))
                throw new InvalidOperationException("The original Builder and activation validator use different Space registries.");
            var resolvedTask = originals.Invoke(() => new SpacePolicyResolver(registry)
                .ResolveDraftAsync(current, draft, Own, Retain, cancellationToken));
            var resolved = await originals.AwaitAsync(resolvedTask).ConfigureAwait(false);

            if (draft.LayoutDocument is { Nodes.Count: > 0 } layout)
                _ = SpaceUserMessageGraph.Compile(current.Id, checked(current.Revision + 1), layout);

            await ValidateModelAsync(resolved.Policy.Model, originals, Own, Retain, cancellationToken).ConfigureAwait(false);
            await ValidateCapabilitiesAsync(resolved.Policy.Capabilities, originals, cancellationToken).ConfigureAwait(false);

            // Resource metadata is validated structurally by SpaceRegistry. Its current owner,
            // availability and permission are checked at actual use; publication grants none.
            return new SpaceDraftActivationValidation(current.Id, current.Revision,
                current.DraftConfigurationRevision, current.ActiveConfigurationRevision,
                resolved.Ancestry.ToArray());
        }).ConfigureAwait(false);
    }

    private async Task ValidateModelAsync(SpaceModelPolicy policy, CloudflareOriginalTaskLedger originals,
        Action<Action> own, Action<Task> retain, CancellationToken token)
    {
        var required = new List<ToolCapability>();
        foreach (var name in policy.RequiredCapabilities ?? [])
        {
            if (!Enum.TryParse<ToolCapability>(name, ignoreCase: true, out var capability) || !Enum.IsDefined(capability))
                throw new InvalidOperationException($"Required model capability '{name}' is not supported by the current model router.");
            required.Add(capability);
        }
        if (string.IsNullOrWhiteSpace(policy.DefaultModelId) &&
            string.IsNullOrWhiteSpace(policy.ProviderConstraint) && required.Count == 0) return;

        var providers = originals.Invoke(() => _services.GetService<IModelProviderRegistry>() as IOriginalModelCatalogueSource)
            ?? throw new InvalidOperationException("The maintained original model catalogue is unavailable; this Space draft was not activated.");
        IReadOnlySet<string>? allowedProviders = string.IsNullOrWhiteSpace(policy.ProviderConstraint)
            ? null : new HashSet<string>([policy.ProviderConstraint], StringComparer.OrdinalIgnoreCase);
        var catalogueTask = originals.Invoke(() => providers.GetModelsWithinOriginalSourceAsync(
            new ModelCataloguePolicy(AllowLocal: true, AllowRemote: true, AllowedProviderIds: allowedProviders),
            own, retain, token));
        var catalogue = await originals.AwaitAsync(catalogueTask).ConfigureAwait(false);
        var candidates = catalogue.Where(model => required.All(model.Supports));
        if (!string.IsNullOrWhiteSpace(policy.DefaultModelId))
            candidates = candidates.Where(model => model.Matches(policy.DefaultModelId));
        if (string.IsNullOrWhiteSpace(policy.DefaultModelId) && !string.IsNullOrWhiteSpace(policy.ProviderConstraint))
            candidates = candidates.Where(model => StringComparer.OrdinalIgnoreCase.Equals(model.ProviderId, policy.ProviderConstraint));
        if (!candidates.Any())
            throw new InvalidOperationException("No currently registered model satisfies this Space draft's provider, model and capability requirements.");
    }

    private async Task ValidateCapabilitiesAsync(IReadOnlyList<SpaceCapabilityBinding> capabilities,
        CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var active = capabilities.Where(binding => binding.Override != SpaceCapabilityOverride.Disabled).ToArray();
        var plugins = active.Where(binding => binding.Kind == SpaceCapabilityKind.Plugin).ToArray();
        var skills = active.Where(binding => binding.Kind == SpaceCapabilityKind.Skill).ToArray();
        var unsupported = active.Where(binding => binding.Kind is not (SpaceCapabilityKind.Plugin or SpaceCapabilityKind.Skill)).ToArray();
        if (unsupported.Length != 0)
            throw new InvalidOperationException("This Space host cannot validate every active Agent, MCP or Connected App binding; activation was withheld.");

        if (plugins.Length != 0)
        {
            var extensions = originals.Invoke(() => _services.GetRequiredService<ExtensionManager>());
            foreach (var binding in plugins)
            {
                token.ThrowIfCancellationRequested();
                var packageTask = originals.Invoke(() => extensions.GetPackageAsync(binding.CapabilityId, token));
                var package = await originals.AwaitAsync(packageTask).ConfigureAwait(false);
                if (package is null || !package.IsEnabled || package.State != ExtensionInstallState.Enabled ||
                    package.Manifest.PackageType is not (ExtensionPackageType.Plugin or ExtensionPackageType.PluginAndSkills) ||
                    binding.VersionPin is { Length: > 0 } pin && package.Manifest.Version != pin)
                    throw new InvalidOperationException($"Plugin '{binding.CapabilityId}' is not currently installed, enabled and at its configured version.");
            }
        }
        if (skills.Length != 0)
        {
            var extensions = originals.Invoke(() => _services.GetRequiredService<ExtensionManager>());
            var skillsTask = originals.Invoke(() => extensions.ListSkillsAsync(token));
            var installed = await originals.AwaitAsync(skillsTask).ConfigureAwait(false);
            foreach (var binding in skills)
            {
                var matches = installed.Where(skill => skill.SkillId == binding.CapabilityId && skill.IsAvailable &&
                    (binding.VersionPin is not { Length: > 0 } pin || skill.PackageVersion == pin)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Skill '{binding.CapabilityId}' is not currently installed, available and at its configured version.");
            }
        }
    }
}
