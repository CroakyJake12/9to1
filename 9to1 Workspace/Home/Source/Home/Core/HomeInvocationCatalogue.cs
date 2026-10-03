using Haven.Application;
using Haven.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

/// <summary>Scope-filtered additional resources (Agents, Files, user applications) come from their owning service.</summary>
public interface IHomeInvocationResourceSource
{
    ValueTask<IReadOnlyList<InvocationResource>> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>Shared menu and dispatch revalidation over currently enabled first-party Apps and extensions.</summary>
public sealed class HomeInvocationCatalogue(IModeRegistry apps, IExtensionRepository extensions,
    IEnumerable<IHomeInvocationResourceSource>? resourceSources = null, string scope = "device") : IInvocationCatalogue, IInvocationResolver
{
    public async ValueTask<IReadOnlyList<InvocationResource>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var resources = new List<InvocationResource>();
        foreach (var app in await apps.GetModesAsync(cancellationToken).ConfigureAwait(false))
        {
            var declaration = BuiltInModeSeed.Modes.SingleOrDefault(seed => seed.Id == app.Id && seed.Key == app.Key && seed.Version == app.Version);
            if (app.IsEnabled && app.Source == ModeSource.BuiltIn && declaration?.InvocationOperability is { } operability)
                resources.Add(new(InvocationKind.App, app.Id.ToString("D"), app.Name, app.Version, app.IconKey,
                    operability.Path switch
                    {
                        AppOperabilityPath.TypedApi => AppInteractionPath.TypedApi,
                        AppOperabilityPath.ComputerUseRequired => AppInteractionPath.ComputerUseRequired,
                        AppOperabilityPath.TypedApiAndComputerUse => AppInteractionPath.TypedApiAndComputerUse,
                        _ => null
                    }, operability.Classification switch
                    {
                        AppOperabilityClassification.OrdinaryApplication => AppClassification.OrdinaryApplication,
                        AppOperabilityClassification.Game => AppClassification.Game,
                        AppOperabilityClassification.AntiCheatProtected => AppClassification.AntiCheatProtected,
                        _ => AppClassification.Unknown
                    }));
        }
        foreach (var package in await extensions.GetInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!(package.EnabledScopes ?? (package.IsEnabled ? new[] { package.EnablementScope } : [])).Contains(scope, StringComparer.OrdinalIgnoreCase) || !package.IsEnabled || package.State is not (ExtensionInstallState.Installed or ExtensionInstallState.Enabled or ExtensionInstallState.UpdateAvailable)) continue;
            var manifest = package.Manifest;
            if (manifest is null) continue;
            if (manifest.PackageType is ExtensionPackageType.Plugin or ExtensionPackageType.PluginAndSkills)
                resources.Add(new(InvocationKind.Plugin, manifest.PackageId, manifest.DisplayName, manifest.Version, "plugin"));
            foreach (var skill in manifest.Skills.Where(s => (package.SkillEnablementScopes?.GetValueOrDefault(s.Id) ?? []).Contains(scope, StringComparer.OrdinalIgnoreCase)))
                resources.Add(new(InvocationKind.Skill, $"{manifest.PackageId}:{skill.Id}", skill.DisplayName, manifest.Version, "skills"));
        }
        foreach (var source in resourceSources ?? []) resources.AddRange(await source.SearchAsync(query, cancellationToken).ConfigureAwait(false));
        resources.Add(InvocationCompose.ComputerUse);
        return resources.Where(InvocationCompose.IsEligible).Where(r => r.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            r.CanonicalId.Contains(query, StringComparison.OrdinalIgnoreCase)).DistinctBy(r => (r.Kind, r.CanonicalId, r.Revision)).ToArray();
    }

    public async ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken cancellationToken)
    {
        var current = await SearchAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        var resolved = new List<InvocationToken>();
        foreach (var token in tokens)
        {
            var resource = current.SingleOrDefault(r => r.Kind == token.Resource.Kind && r.CanonicalId == token.Resource.CanonicalId && r.Revision == token.Resource.Revision)
                ?? throw new InvalidOperationException("An invoked resource was revoked, disabled, changed revision or is no longer authorised.");
            if (resource.Kind == InvocationKind.System && string.IsNullOrWhiteSpace(token.ComputerUseInvocationId))
                throw new InvalidOperationException("An explicit Computer Use invocation identity is required.");
            resolved.Add(token with { Resource = resource });
        }
        if (resolved.Any(t => t.Resource.InteractionPath == AppInteractionPath.ComputerUseRequired) &&
            !resolved.Any(t => t.Resource.CanonicalId == InvocationCompose.ComputerUseCapabilityId && t.ComputerUseInvocationId is not null))
            throw new InvalidOperationException("Explicit @Computer Use is required for this application's interaction path.");
        return resolved;
    }
}
