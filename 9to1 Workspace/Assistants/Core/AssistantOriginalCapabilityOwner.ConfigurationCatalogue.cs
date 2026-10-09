using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantOriginalCapabilityOwner : IAssistantOriginalConfigurationCapabilityCatalogueOwner
{
    private readonly object _configurationIssuer = new();
    private readonly object _configurationSourceGate = new();
    private AssistantCanonicalMembershipSource? _configurationDefinitions;
    private readonly ConditionalWeakTable<AssistantOriginalConfigurationCapabilityCatalogue, ConfigurationCatalogueOriginal> _configurationCatalogues = new();
    private readonly ConditionalWeakTable<AssistantOriginalConfigurationCapabilityChoice, ConfigurationChoiceOriginal> _configurationChoices = new();
    private sealed record ConfigurationCatalogueOriginal(AssistantCanonicalMembershipSource Source,
        AssistantDefinitionSnapshot Definition, string DefinitionJson, AuthenticatedResourceActor Actor,
        CapabilityOriginalCatalogueObservation Catalogue, string CatalogueJson);
    private sealed record ConfigurationChoiceOriginal(AssistantOriginalConfigurationCapabilityCatalogue Catalogue,
        ConfigurationCatalogueOriginal Original, CapabilityDefinition Selected);

    private AssistantCanonicalMembershipSource OriginalConfigurationDefinitionSource()
    {
        lock (_configurationSourceGate) return _configurationDefinitions ??= new(_home, _conversations, []);
    }
    public bool IsIssuedOriginalConfigurationCatalogue(AssistantOriginalConfigurationCapabilityCatalogue sameActual) =>
        Volatile.Read(ref _retired) == 0 && sameActual is not null && ReferenceEquals(sameActual.Issuer, _configurationIssuer) &&
        _configurationCatalogues.TryGetValue(sameActual, out var original) && ReferenceEquals(sameActual.Original, original) &&
        ReferenceEquals(sameActual.Definition, original.Definition) && sameActual.Actor == original.Actor;
    public bool IsIssuedOriginalConfigurationChoice(AssistantOriginalConfigurationCapabilityChoice sameActual) =>
        Volatile.Read(ref _retired) == 0 && sameActual is not null && ReferenceEquals(sameActual.Issuer, _configurationIssuer) &&
        _configurationChoices.TryGetValue(sameActual, out var original) && ReferenceEquals(sameActual.Original, original) &&
        ReferenceEquals(sameActual.Catalogue, original.Catalogue) && ReferenceEquals(sameActual.Selected, original.Selected) &&
        IsIssuedOriginalConfigurationCatalogue(original.Catalogue);

    public Task<AssistantOriginalConfigurationCapabilityCatalogue> ReadOriginalConfigurationCatalogueWithinSourceAsync(
        AssistantIdentity identity, long expectedRevision, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            var definitions = Invoke(scope, OriginalConfigurationDefinitionSource);
            var before = await ReadConfigurationDefinitionAsync(definitions, identity, expectedRevision, scope, retain, token).ConfigureAwait(false);
            var platform = Invoke(scope, () => OperatingSystem.IsWindows() ? CapabilityPlatform.Windows :
                OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : OperatingSystem.IsLinux() ? CapabilityPlatform.Linux :
                throw new AssistantCommandRefusedException("This host has no maintained capability metadata platform."));
            var observed = await Read(() => _registry.DiscoverWithinOriginalSourceAsync(_catalogue, before.Actor, platform,
                body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
            var after = await ReadConfigurationDefinitionAsync(definitions, identity, expectedRevision, scope, retain, token).ConfigureAwait(false);
            return Invoke(scope, () =>
            {
                if (after.Actor != before.Actor || DefinitionFingerprint(after.Definition) != DefinitionFingerprint(before.Definition) ||
                    !_registry.IsIssuedOriginalCatalogue(observed) || observed.Actor != before.Actor)
                    throw new AssistantCommandRefusedException("The actual saved identity, actor or capability catalogue changed. Refresh configuration.");
                if (observed.Definitions.Count > MaximumCatalogueEntries)
                    throw new AssistantCommandRefusedException("The actual configuration catalogue exceeds its bounded display size.");
                var rows = Array.AsReadOnly(observed.Definitions.ToArray());
                var original = new ConfigurationCatalogueOriginal(definitions, before.Definition,
                    DefinitionFingerprint(before.Definition), before.Actor, observed, JsonSerializer.Serialize(rows, DenJson.Options));
                var result = new AssistantOriginalConfigurationCapabilityCatalogue(_configurationIssuer, original,
                    before.Definition, before.Actor, observed.State, observed.Detail,
                    rows);
                _configurationCatalogues.Add(result, original); return result;
            });
        });

    public Task RevalidateOriginalConfigurationCatalogueWithinSourceAsync(AssistantOriginalConfigurationCapabilityCatalogue sameActual,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
        {
            var original = Invoke(scope, () => IsIssuedOriginalConfigurationCatalogue(sameActual) &&
                _configurationCatalogues.TryGetValue(sameActual, out var actual) ? actual :
                throw new AssistantCommandRefusedException("This live configuration source did not issue that catalogue."));
            var current = await ReadConfigurationDefinitionAsync(original.Source, original.Definition.Identity,
                original.Definition.Revision, scope, retain, token).ConfigureAwait(false);
            var fresh = await Read(() => _registry.DiscoverWithinOriginalSourceAsync(_catalogue, current.Actor,
                original.Catalogue.Platform, body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
            var after = await ReadConfigurationDefinitionAsync(original.Source, original.Definition.Identity,
                original.Definition.Revision, scope, retain, token).ConfigureAwait(false);
            Invoke(scope, () =>
            {
                if (current.Actor != original.Actor || after.Actor != original.Actor ||
                    DefinitionFingerprint(current.Definition) != original.DefinitionJson ||
                    DefinitionFingerprint(after.Definition) != original.DefinitionJson ||
                    DefinitionFingerprint(sameActual.Definition) != original.DefinitionJson ||
                    !_registry.IsIssuedOriginalCatalogue(fresh) || fresh.Actor != original.Actor ||
                    fresh.State != original.Catalogue.State ||
                    JsonSerializer.Serialize(fresh.Definitions, DenJson.Options) != original.CatalogueJson ||
                    JsonSerializer.Serialize(sameActual.Definitions, DenJson.Options) != original.CatalogueJson)
                    throw new AssistantCommandRefusedException("The current saved definition or observed catalogue changed. Refresh before saving its preferences.");
                return true;
            });
            return true;
        });

    public Task<AssistantOriginalConfigurationCapabilityChoice> SelectOriginalConfigurationCapabilityWithinSourceAsync(
        AssistantOriginalConfigurationCapabilityCatalogue sameActual, Guid observedCapabilityId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
        {
            await Read(() => RevalidateOriginalConfigurationCatalogueWithinSourceAsync(sameActual,
                body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
            return Invoke(scope, () =>
            {
                if (sameActual.State != CapabilityOriginalCatalogueState.Available ||
                    !_configurationCatalogues.TryGetValue(sameActual, out var original))
                    throw new AssistantCommandRefusedException("The actual capability source requires setup before choosing preferences.");
                var selected = original.Catalogue.Definitions.SingleOrDefault(value => value.Id == observedCapabilityId)
                    ?? throw new AssistantCommandRefusedException("Choose an actual observed capability row from the current catalogue.");
                var receipt = new ConfigurationChoiceOriginal(sameActual, original, selected);
                var choice = new AssistantOriginalConfigurationCapabilityChoice(_configurationIssuer, receipt, sameActual, selected);
                _configurationChoices.Add(choice, receipt); return choice;
            });
        });

    public Task RevalidateOriginalConfigurationChoiceWithinSourceAsync(AssistantOriginalConfigurationCapabilityChoice sameActual,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
        {
            Invoke(scope, () => IsIssuedOriginalConfigurationChoice(sameActual) ? true :
                throw new AssistantCommandRefusedException("This live source did not issue the selected capability preference."));
            await Read(() => RevalidateOriginalConfigurationCatalogueWithinSourceAsync(sameActual.Catalogue,
                body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
            return true;
        });

    private sealed record ConfigurationDefinitionObservation(AuthenticatedResourceActor Actor, AssistantDefinitionSnapshot Definition);
    private async Task<ConfigurationDefinitionObservation> ReadConfigurationDefinitionAsync(AssistantCanonicalMembershipSource source,
        AssistantIdentity identity, long expectedRevision, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var home = await Read(() => source.OpenHomeWithinSourceAsync(body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        var row = await Read(() => source.DefinitionWithinSourceAsync(home, identity,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        var definition = Invoke(scope, () => source.Snapshot(home, row));
        var after = await Read(() => source.OpenHomeWithinSourceAsync(body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        return Invoke(scope, () => definition.Identity == identity && definition.Revision == expectedRevision && after.Actor == home.Actor
            ? new ConfigurationDefinitionObservation(home.Actor, definition) :
            throw new AssistantCommandRefusedException("The actual saved identity/configuration changed. Reopen its current revision."));
    }
}
