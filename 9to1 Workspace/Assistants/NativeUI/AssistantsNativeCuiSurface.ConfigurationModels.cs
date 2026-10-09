namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private AssistantConfigurationDraft.Submission? _configurationModelTarget;
    private long _configurationModelRequest;

    private bool IsConfigurationModelCurrent(AssistantConfigurationDraft.Submission submitted, long request) =>
        !IsRetiring && ReferenceEquals(_configurationModelTarget, submitted) && request == _configurationModelRequest &&
        AssistantConfigurationCapabilityPreferences.IsCurrent(Bindings.OriginalDraft, submitted) &&
        _controller.Snapshot.SelectedAssistant is { } selected && selected.Identity == submitted.Identity &&
        selected.Revision == submitted.ExpectedRevision;

    private async Task ReadConfigurationModelsAsync(CancellationToken token)
    {
        if (IsRetiring) return;
        PublishSynchronous(() => Bindings.SetConfigurationModelSource(_controller.HasOriginalConfigurationModelCatalogue));
        if (Bindings.IsActionAvailable("assistants.configuration.models.read") != true) return;
        var submitted = Bindings.OriginalDraft!.CaptureSubmission();
        var request = checked(++_configurationModelRequest); _configurationModelTarget = submitted;
        PublishSynchronous(() => { Bindings.BeginConfigurationModelRead(submitted); Bindings.SetConfigurationModelsBusy(true); });
        try
        {
            var actual = await SourceAsync(() => _controller.ReadOriginalConfigurationModelsAsync(token));
            if (IsConfigurationModelCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationModelCurrent(submitted, request)) Bindings.PublishConfigurationModels(submitted, actual);
            });
        }
        finally
        {
            if (!IsRetiring && ReferenceEquals(_configurationModelTarget, submitted) && request == _configurationModelRequest)
                PublishSynchronous(() =>
                {
                    if (ReferenceEquals(_configurationModelTarget, submitted) && request == _configurationModelRequest)
                        Bindings.SetConfigurationModelsBusy(false);
                });
        }
    }

    private async Task ChooseConfigurationModelAsync(object? value, CancellationToken token)
    {
        var submitted = _configurationModelTarget; var request = _configurationModelRequest;
        if (submitted is null || !IsConfigurationModelCurrent(submitted, request) ||
            Bindings.CurrentConfigurationModelRow(value) is not { } row) return;
        PublishSynchronous(() => Bindings.SetConfigurationModelsBusy(true));
        try
        {
            await SourceAsync(() => _controller.RevalidateOriginalConfigurationModelAsync(row.Catalogue, row.Original, token));
            if (IsConfigurationModelCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationModelCurrent(submitted, request)) Bindings.ApplyConfigurationModel(submitted, row);
            });
        }
        finally
        {
            if (!IsRetiring && ReferenceEquals(_configurationModelTarget, submitted) && request == _configurationModelRequest)
                PublishSynchronous(() =>
                {
                    if (ReferenceEquals(_configurationModelTarget, submitted) && request == _configurationModelRequest)
                        Bindings.SetConfigurationModelsBusy(false);
                });
        }
    }
}
