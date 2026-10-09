#if !ANDROID
using Haven.Application;
using Haven.Infrastructure;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private OriginalAssistantGeneratedUiHost AcquireOriginalAssistantGeneratedUiHost(
        IServiceProvider actualProvider, AssistantsWorkspaceController actualController,
        Action<OriginalAssistantGeneratedUiHost> captureOriginalHost)
    {
        // Called by the SAME guarded App acquisition after the actual Home/Den
        // composition. These are the maintained singleton services, not copies.
        var router = actualProvider.GetRequiredService<GenerativeUiEventRouter>();
        var instances = actualProvider.GetRequiredService<GenUiInstanceStore>();
        var writer = actualProvider.GetRequiredService<CanonicalGeneratedUiInteractionOriginalOwner>();
        var origins = _actualGeneratedUiOrigins ?? throw new InvalidOperationException("The actual process origin was not retained.");
        if (!HasOriginalGeneratedUiProvider(actualProvider) || !ReferenceEquals(writer, _actualGeneratedUiInteractions) ||
            !ReferenceEquals(writer.OriginalMessageAuthority, origins) || !ReferenceEquals(origins.OriginalInstances, instances) ||
            !ReferenceEquals(actualProvider.GetRequiredService<ICanonicalGeneratedUiInteractionOriginalReadSource>(), writer) ||
            !ReferenceEquals(actualProvider.GetRequiredService<ICanonicalGeneratedUiInteractionOriginalWriteSource>(), writer) ||
            !ReferenceEquals(actualProvider.GetRequiredService<ICanonicalGeneratedUiInteractionOriginalProcessSource>(), writer))
            throw new UnauthorizedAccessException("Use the SAME actual configured generated interaction origin/store aliases.");
        var host = new OriginalAssistantGeneratedUiHost(actualController, router, instances,
            actualProvider.GetRequiredService<CalculatorTemplateRuntime>(),
            actualProvider.GetRequiredService<ChecklistTemplateRuntime>(),
            actualProvider.GetRequiredService<DataGridTemplateRuntime>(),
            actualProvider.GetRequiredService<CustomTemplateRuntime>());
        captureOriginalHost(host); // Root the partial host before process binding callbacks can fail.
        host.BindOriginalInteractionOrigins(origins); host.BindOriginalInteractionStore(writer); return host;
    }
}
#endif
