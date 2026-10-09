using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public Task<AssistantOriginalConfigurationCapabilityCatalogue> ReadOriginalConfigurationCapabilitiesAsync(
        CancellationToken token = default)
    {
        var definition = DemandSelected();
        return CommandAsync(() =>
        {
            var owner = DemandConfigurationCapabilityOwner();
            var sources = CaptureConfigurationCapabilitySources();
            return ObserveSourceAsync(() => owner.ReadOriginalConfigurationCatalogueWithinSourceAsync(
                definition.Identity, definition.Revision, sources.Scope, sources.Retain, token));
        }, false, token);
    }

    public Task<AssistantOriginalConfigurationCapabilityChoice> SelectOriginalConfigurationCapabilityAsync(
        AssistantOriginalConfigurationCapabilityCatalogue actualCatalogue, Guid observedCapabilityId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualCatalogue); var definition = DemandSelected();
        return CommandAsync(() =>
        {
            var owner = DemandConfigurationCapabilityOwner();
            using (EnterSynchronousSource())
            {
                if (!owner.IsIssuedOriginalConfigurationCatalogue(actualCatalogue) || actualCatalogue.Definition.Identity != definition.Identity)
                    throw new UnauthorizedAccessException("The current saved identity owner did not issue this capability catalogue.");
                if (actualCatalogue.Definition.Revision != definition.Revision)
                    throw IssueLocalRefusal("The Assistant configuration changed. Refresh its current capability catalogue.");
            }
            var sources = CaptureConfigurationCapabilitySources();
            return ObserveSourceAsync(() => owner.SelectOriginalConfigurationCapabilityWithinSourceAsync(
                actualCatalogue, observedCapabilityId, sources.Scope, sources.Retain, token));
        }, false, token);
    }

    public Task RevalidateOriginalConfigurationChoiceAsync(AssistantOriginalConfigurationCapabilityChoice actualChoice,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualChoice); var definition = DemandSelected();
        return CommandAsync(async () =>
        {
            var owner = DemandConfigurationCapabilityOwner();
            using (EnterSynchronousSource())
            {
                if (!owner.IsIssuedOriginalConfigurationChoice(actualChoice) || actualChoice.Catalogue.Definition.Identity != definition.Identity)
                    throw new UnauthorizedAccessException("The current saved identity owner did not issue this exact capability preference.");
                if (actualChoice.Catalogue.Definition.Revision != definition.Revision)
                    throw IssueLocalRefusal("The Assistant configuration changed. Choose from its current catalogue before saving.");
            }
            var sources = CaptureConfigurationCapabilitySources();
            await ObserveSourceAsync(() => owner.RevalidateOriginalConfigurationChoiceWithinSourceAsync(actualChoice,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            return true;
        }, false, token);
    }

    private IAssistantOriginalConfigurationCapabilityCatalogueOwner DemandConfigurationCapabilityOwner() =>
        _bridge as IAssistantOriginalConfigurationCapabilityCatalogueOwner ??
        throw IssueLocalRefusal("The actual configuration capability catalogue source is unavailable.");

    private (Action<Action> Scope, Action<Task> Retain) CaptureConfigurationCapabilitySources()
    {
        // Capture the admitted parent occurrence before any foreign/suppressed-context
        // callback. Each callback uses the actual physical controller source guard.
        var original = _executing.Value ?? throw new InvalidOperationException("No configuration command original is admitted.");
        void Scope(Action body) { using (EnterSynchronousSource()) body(); }
        void Retain(Task actual)
        {
            ArgumentNullException.ThrowIfNull(actual);
            using (EnterSynchronousSource())
                lock (_gate) if (!original.Sources.Any(value => ReferenceEquals(value, actual))) original.Sources.Add(actual);
        }
        return (Scope, Retain);
    }
}
