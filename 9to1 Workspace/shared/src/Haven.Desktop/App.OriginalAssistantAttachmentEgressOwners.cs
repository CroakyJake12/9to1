#if !ANDROID
using Haven.Application;
using HavenOS.Apps.Assistants.Attachments;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private TaskRunConfiguredCloudAdmissionSource? _actualAssistantAttachmentEgressCloud;
    private HomeCanonicalAssistantAttachmentEgressSource? _actualAssistantAttachmentEgressHome;
    private HomeAssistantAttachmentEgressResourceResolver? _actualAssistantAttachmentEgressResolver;
    private HomeAssistantAttachmentEgressActionPolicySource? _actualAssistantAttachmentEgressPolicy;
    private Task? _actualAssistantAttachmentFramesClose;
    private Task? _actualAssistantAttachmentEgressClose;

    private void PrepareOriginalAssistantAttachmentEgressPolicies()
    {
        _actualAssistantAttachmentEgressResolver = new(() => _actualAssistantAttachmentEgressHome
            ?? throw new InvalidOperationException("The actual attachment disclosure Home source is not retained."));
        _actualAssistantAttachmentEgressPolicy = new();
    }
    // Called within the same original App acquisition, after the actual Chat/input
    // pairing. Resolve only the configured singleton and its existing three aliases.
    private void RetainOriginalAssistantAttachmentEgressOwners(IServiceProvider provider,
        HomeNativeWindowsComposition home, AssistantOriginalAttachmentSource source)
    {
        if (!HasOriginalAssistantAttachmentProvider(provider) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(source, _actualAssistantAttachments) ||
            !source.HasOriginalInputComposition(provider.GetRequiredService<ChatSessionService>(),
                provider.GetRequiredService<TaskExecutionCoordinator>()))
            throw new UnauthorizedAccessException("Use the SAME current App/Home/accepted attachment input source.");
        if (_actualAssistantAttachmentEgressCloud is not null || _actualAssistantAttachmentEgressHome is not null)
            throw new InvalidOperationException("Retain the original disclosure owners, including any partial acquisition.");
        var cloud = _actualAssistantAttachmentEgressCloud = provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>();
        DemandConfiguredOriginalAttachmentCloud(provider, cloud);
        if (cloud.OriginalAttachmentFramesClose is not null)
            throw new ObjectDisposedException(nameof(TaskRunConfiguredCloudAdmissionSource));
        var disclosure = _actualAssistantAttachmentEgressHome = new(home.StateStore, home.Profiles,
            home.Resources, home.Broker, home.Permissions, source);
        cloud.BindOriginalAttachmentEgressSource(source);
        source.BindOriginalEgressOwners(cloud, disclosure);
        DemandOriginalAssistantAttachmentEgressComposition(provider, home, source);
    }
    private static void DemandConfiguredOriginalAttachmentCloud(IServiceProvider provider,
        TaskRunConfiguredCloudAdmissionSource cloud)
    {
        if (!ReferenceEquals(cloud, provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>()) ||
            !ReferenceEquals(cloud, provider.GetRequiredService<ITaskRunCloudAdmissionSource>()) ||
            !ReferenceEquals(cloud, provider.GetRequiredService<ITaskRunProviderContextCapture>()) ||
            !ReferenceEquals(cloud, provider.GetRequiredService<ITaskRunProviderContextAuthority>()))
            throw new UnauthorizedAccessException("Retain the SAME configured Task cloud admission and context aliases.");
    }
    private void DemandOriginalAssistantAttachmentEgressComposition(IServiceProvider provider,
        HomeNativeWindowsComposition home, AssistantOriginalAttachmentSource source)
    {
        if (!HasOriginalAssistantAttachmentProvider(provider) || !ReferenceEquals(home, _actualWindowsHome) ||
            !ReferenceEquals(source, _actualAssistantAttachments) ||
            _actualAssistantAttachmentEgressCloud is not { } cloud ||
            _actualAssistantAttachmentEgressHome is not { } disclosure ||
            cloud.OriginalAttachmentFramesClose is not null || disclosure.OriginalClose is not null ||
            !cloud.HasOriginalAttachmentEgressSource(source) || !source.HasOriginalEgressComposition(cloud) ||
            !disclosure.HasOriginalComposition(home.StateStore, home.Profiles, source) ||
            _actualAssistantAttachmentEgressResolver is not { } resolver || !resolver.IsBoundToOriginalOwner(disclosure))
            throw new UnauthorizedAccessException("Retain the SAME accepted input/Task cloud/Home disclosure tuple.");
        DemandConfiguredOriginalAttachmentCloud(provider, cloud);
    }
    private void DemandOriginalAssistantAttachmentEgressRetirementJoin()
    {
        _actualAssistantAttachmentEgressCloud?.DemandExternalOriginalAttachmentFramesJoin();
        _actualAssistantAttachmentEgressHome?.DemandExternalOriginalJoin();
    }
}
#endif
