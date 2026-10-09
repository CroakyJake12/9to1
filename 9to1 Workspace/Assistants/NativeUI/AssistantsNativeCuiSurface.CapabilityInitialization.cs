using Haven.Application;
using System.Runtime.ExceptionServices;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private AssistantConfigurationDraft.Submission? _configurationInitializationTarget;
    private AssistantOriginalCapabilityInitializationIntent? _configurationInitializationIntent;
    private AssistantOriginalCapabilityInitializationObservation? _configurationInitializationDelivery;
    private long _configurationInitializationRequest;

    private bool IsConfigurationInitializationCurrent(AssistantConfigurationDraft.Submission submitted, long request) =>
        !IsRetiring && request == _configurationInitializationRequest &&
        ReferenceEquals(submitted, _configurationInitializationTarget) &&
        AssistantConfigurationCapabilityPreferences.IsCurrent(Bindings.OriginalDraft, submitted) &&
        _controller.Snapshot.SelectedAssistant is { } selected &&
        selected.Identity == submitted.Identity && selected.Revision == submitted.ExpectedRevision;

    private async Task ReviewConfigurationCapabilityInitializationAsync(CancellationToken token)
    {
        if (Bindings.IsActionAvailable("assistants.configuration.capabilities.setup.review") != true) return;
        var submitted = Bindings.OriginalDraft!.CaptureSubmission();
        var request = checked(++_configurationInitializationRequest);
        _configurationInitializationTarget = submitted; _configurationInitializationIntent = null;
        PublishSynchronous(() =>
        {
            Bindings.BeginOriginalCapabilityInitializationReview(submitted);
            Bindings.SetOriginalCapabilityInitializationBusy(true);
        });
        try
        {
            var intent = await SourceAsync(() => _controller.PrepareOriginalConfigurationCapabilityInitializationAsync(Guid.NewGuid(), token));
            if (IsConfigurationInitializationCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationInitializationCurrent(submitted, request) &&
                    Bindings.PublishOriginalCapabilityInitializationIntent(submitted, intent))
                    _configurationInitializationIntent = intent;
            });
        }
        finally { CompleteConfigurationInitializationBusy(submitted, request); }
    }

    private async Task StartConfigurationCapabilityInitializationAsync(CancellationToken token)
    {
        var submitted = _configurationInitializationTarget; var intent = _configurationInitializationIntent;
        var request = _configurationInitializationRequest;
        if (submitted is null || intent is null || !IsConfigurationInitializationCurrent(submitted, request) ||
            Bindings.IsActionAvailable("assistants.configuration.capabilities.setup.start") != true) return;
        AssistantOriginalCapabilityInitializationObservation? delivery = null;
        var failures = new List<Exception>();
        PublishSynchronous(() => Bindings.SetOriginalCapabilityInitializationBusy(true));
        try
        {
            delivery = await SourceAsync(() => _controller.StartOriginalConfigurationCapabilityInitializationAsync(intent, token));
            _configurationInitializationDelivery = delivery; // SAME delivery retained before any pending UI callback.
            if (IsConfigurationInitializationCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (IsConfigurationInitializationCurrent(submitted, request))
                    Bindings.SetOriginalCapabilityInitializationStatus("Setup is running. Review its individual request in Home; this Assistant remains open while you do so.");
            });
            // Business setup now belongs to its process issuer. This action owns
            // observation delivery only; normal close first retires that delivery
            // and then joins the SAME wait. Unsaved form edits remain protected.
            ReleasePreparationBlock();
            var completion = await SourceAsync(() => _controller.WaitOriginalConfigurationCapabilityInitializationAsync(delivery, CancellationToken.None));
            if (!ReferenceEquals(completion.OriginalObservation, delivery))
                throw new UnauthorizedAccessException("The setup result belongs to another original delivery.");
            if (IsConfigurationInitializationCurrent(submitted, request)) PublishSynchronous(() =>
            {
                if (!IsConfigurationInitializationCurrent(submitted, request)) return;
                Bindings.SetOriginalCapabilityInitializationStatus(completion.Kind switch
                {
                    CapabilityOriginalInitializationCompletionKind.Initialized => "Built-in Tools setup completed. Review the current Tools and Apps to choose preferences.",
                    CapabilityOriginalInitializationCompletionKind.DeclinedBeforeEffect => "Setup was declined before any change. Review Home before trying again.",
                    CapabilityOriginalInitializationCompletionKind.ObservationRetired => "This view detached from setup. The original operation remains owned by the application.",
                    _ => throw new InvalidOperationException("The actual setup delivery returned an unsupported outcome.")
                });
            });
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            // Detach only the SAME returned observation. Never close the global
            // initializer, Home WRITE claim, catalogue, registry or SQL store.
            if (delivery is not null)
                try { await SourceAsync(() => _controller.CloseOriginalObservationAsync(delivery)); }
                catch (Exception cause) { failures.Add(cause); }
            try { CompleteConfigurationInitializationBusy(submitted, request); }
            catch (Exception cause) { failures.Add(cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual setup delivery/publication/close originals failed.", failures);
    }

    private void CompleteConfigurationInitializationBusy(AssistantConfigurationDraft.Submission submitted, long request)
    {
        if (IsRetiring || request != _configurationInitializationRequest ||
            !ReferenceEquals(submitted, _configurationInitializationTarget)) return;
        PublishSynchronous(() =>
        {
            if (!IsRetiring && request == _configurationInitializationRequest &&
                ReferenceEquals(submitted, _configurationInitializationTarget))
                Bindings.SetOriginalCapabilityInitializationBusy(false);
        });
    }
}
