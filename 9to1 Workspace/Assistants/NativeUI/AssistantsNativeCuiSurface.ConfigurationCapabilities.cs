using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private AssistantConfigurationDraft.Submission? _configurationCapabilityTarget;
    private long _configurationCapabilityRequest;

    private bool IsConfigurationCapabilityCurrent(AssistantConfigurationDraft.Submission submitted, long request) =>
        !IsRetiring && ReferenceEquals(_configurationCapabilityTarget, submitted) &&
        _configurationCapabilityRequest == request &&
        AssistantConfigurationCapabilityPreferences.IsCurrent(Bindings.OriginalDraft, submitted) &&
        _controller.Snapshot.SelectedAssistant is { } selected &&
        selected.Identity == submitted.Identity && selected.Revision == submitted.ExpectedRevision;

    private async Task ReadConfigurationCapabilitiesAsync(CancellationToken token)
    {
        if (Bindings.IsActionAvailable("assistants.configuration.capabilities.read") != true) return;
        var draft = Bindings.OriginalDraft!; var submitted = draft.CaptureSubmission();
        var request = checked(++_configurationCapabilityRequest); _configurationCapabilityTarget = submitted;
        PublishSynchronous(() => { Bindings.BeginConfigurationCapabilityReview(submitted); Bindings.SetConfigurationCapabilityBusy(true); });
        try
        {
            var actual = await SourceAsync(() => _controller.ReadOriginalConfigurationCapabilitiesAsync(token));
            if (IsConfigurationCapabilityCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationCapabilityCurrent(submitted, request))
                    Bindings.PublishConfigurationCapabilityCatalogue(submitted, actual);
            });
        }
        finally
        {
            if (!IsRetiring && ReferenceEquals(_configurationCapabilityTarget, submitted) && request == _configurationCapabilityRequest)
                PublishSynchronous(() =>
                {
                    if (ReferenceEquals(_configurationCapabilityTarget, submitted) && request == _configurationCapabilityRequest)
                        Bindings.SetConfigurationCapabilityBusy(false);
                });
        }
    }

    private async Task ChooseConfigurationCapabilityAsync(object? parameter, CancellationToken token)
    {
        var submitted = _configurationCapabilityTarget; var request = _configurationCapabilityRequest;
        if (submitted is null || !IsConfigurationCapabilityCurrent(submitted, request) ||
            Bindings.FindConfigurationCapabilityRow(parameter) is not { } row) return;
        // The opaque row comes from one issued catalogue. Its Guid is a lookup
        // within that source, never a public permission or a provider-wide choice.
        PublishSynchronous(() => Bindings.SetConfigurationCapabilityBusy(true));
        try
        {
            var choice = await SourceAsync(() => _controller.SelectOriginalConfigurationCapabilityAsync(
                row.Catalogue, row.Original.Id, token));
            if (IsConfigurationCapabilityCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationCapabilityCurrent(submitted, request))
                    Bindings.ApplyOriginalConfigurationCapabilityChoice(submitted, choice);
            });
        }
        finally
        {
            if (!IsRetiring && ReferenceEquals(_configurationCapabilityTarget, submitted) && request == _configurationCapabilityRequest)
                PublishSynchronous(() =>
                {
                    if (ReferenceEquals(_configurationCapabilityTarget, submitted) && request == _configurationCapabilityRequest)
                        Bindings.SetConfigurationCapabilityBusy(false);
                });
        }
    }
}
