using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public bool HasOriginalConfigurationModelCatalogue => _bridge is IAssistantOriginalConfigurationModelCatalogueOwner;

    public Task<AssistantOriginalConfigurationModelCatalogue> ReadOriginalConfigurationModelsAsync(CancellationToken token = default)
    {
        var definition = DemandSelected();
        return CommandAsync(() =>
        {
            var owner = DemandConfigurationModelOwner();
            var sources = CaptureConfigurationCapabilitySources();
            return ObserveSourceAsync(() => owner.ReadOriginalConfigurationModelsWithinSourceAsync(
                definition.Identity, definition.Revision, sources.Scope, sources.Retain, token));
        }, false, token);
    }

    public Task RevalidateOriginalConfigurationModelAsync(AssistantOriginalConfigurationModelCatalogue catalogue,
        AssistantModelChoice model, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue); ArgumentNullException.ThrowIfNull(model);
        var definition = DemandSelected();
        return CommandAsync(async () =>
        {
            var owner = DemandConfigurationModelOwner();
            using (EnterSynchronousSource())
            {
                if (!owner.IsIssuedOriginalConfigurationModels(catalogue) || catalogue.Definition.Identity != definition.Identity)
                    throw new UnauthorizedAccessException("This saved identity owner did not issue the model catalogue.");
                if (catalogue.Definition.Revision != definition.Revision)
                    throw IssueLocalRefusal("The Assistant changed. Refresh models from its current saved configuration.");
            }
            var sources = CaptureConfigurationCapabilitySources();
            await ObserveSourceAsync(() => owner.RevalidateOriginalConfigurationModelWithinSourceAsync(
                catalogue, model, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            return true;
        }, false, token);
    }

    private IAssistantOriginalConfigurationModelCatalogueOwner DemandConfigurationModelOwner() =>
        _bridge as IAssistantOriginalConfigurationModelCatalogueOwner ??
        throw IssueLocalRefusal("Model discovery for a saved Assistant is not configured on this host.");
}
