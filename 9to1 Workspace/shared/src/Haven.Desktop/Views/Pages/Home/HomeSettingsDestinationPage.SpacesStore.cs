using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeSettingsDestinationPage
{
    internal SpacesOriginalStoreSetupBackend? SpacesStoreSetup { get; init; }
    private SpacesOriginalStoreSetupBackend.Snapshot? _spacesStoreSnapshot;
    private bool _spacesStoreBusy;
    private string? _spacesStoreError;

    private static bool IsSpacesStoreCommand(string command) => command is
        "SettingsSpacesStoreInspect" or "SettingsSpacesStoreRequest" or "SettingsSpacesStoreReview" or
        "SettingsSpacesStoreComplete" or "SettingsSpacesStoreRetryAudit";

    private bool SpacesStoreActionAvailable(string command) =>
        _category == "security-permissions" && SpacesStoreSetup is not null && !_spacesStoreBusy && !IsBusy && !_unavailable &&
        (command switch
        {
            "SettingsSpacesStoreInspect" => true,
            "SettingsSpacesStoreRequest" => _spacesStoreSnapshot?.CanRequestImport == true,
            "SettingsSpacesStoreReview" => _spacesStoreSnapshot?.CanOpenReview == true,
            "SettingsSpacesStoreComplete" => _spacesStoreSnapshot?.CanCompleteImport == true,
            "SettingsSpacesStoreRetryAudit" => _spacesStoreSnapshot?.CanRetryAudit == true,
            _ => false
        });

    private void RefreshSpacesStoreBindings()
    {
        _bindings.Set("SettingsSpacesStoreVisible", _category == "security-permissions" && SpacesStoreSetup is not null);
        _bindings.Set("SettingsSpacesStoreStatus", _spacesStoreError ?? _spacesStoreSnapshot?.Message ??
            "Inspect the current Spaces store, then review its ownership with Home. Saved Spaces and chat drafts remain in place.");
        _bindings.Set("SettingsSpacesStoreIdentity", _spacesStoreSnapshot is { Metadata: not null } actual
            ? "Store: " + actual.StoreId + " · Profile: " + actual.Actor.ProfileId : string.Empty);
        _bindings.Set("SettingsSpacesStoreCanInspect", IsActionAvailable("SettingsSpacesStoreInspect") == true);
        _bindings.Set("SettingsSpacesStoreCanRequest", IsActionAvailable("SettingsSpacesStoreRequest") == true);
        _bindings.Set("SettingsSpacesStoreCanReview", IsActionAvailable("SettingsSpacesStoreReview") == true);
        _bindings.Set("SettingsSpacesStoreCanComplete", IsActionAvailable("SettingsSpacesStoreComplete") == true);
        _bindings.Set("SettingsSpacesStoreCanRetryAudit", IsActionAvailable("SettingsSpacesStoreRetryAudit") == true);
    }

    private Task RunSpacesStoreActionAsync(string command, CancellationToken caller) => _work.RunAsync(async original =>
    {
        var backend = SpacesStoreSetup ?? throw new InvalidOperationException("The original Spaces setup owner is unavailable.");
        var generation = _generation;
        var category = _category;
        original.BindPublicationGuard(() => _active && IsAlive && generation == _generation && category == _category);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller);
        var token = linked.Token;
        _spacesStoreBusy = true; _spacesStoreError = null; RefreshBindings();
        try
        {
            if (!await original.AwaitAsync(IsAdmittedAsync(token)))
            {
                if (original.IsAcceptedPublicationCurrent)
                    original.RunAcceptedPublicationCallback(() =>
                    {
                        _spacesStoreError = "Reopen Home Settings for the current profile, then inspect Spaces storage again.";
                        _unavailable = true;
                    });
                return;
            }
            if (!original.IsAcceptedPublicationCurrent) return;
            token.ThrowIfCancellationRequested();
            var operation = command switch
            {
                "SettingsSpacesStoreInspect" => SpacesOriginalStoreSetupOperation.Inspect,
                "SettingsSpacesStoreRequest" => SpacesOriginalStoreSetupOperation.RequestImport,
                "SettingsSpacesStoreReview" => SpacesOriginalStoreSetupOperation.OpenReview,
                "SettingsSpacesStoreComplete" => SpacesOriginalStoreSetupOperation.CompleteImport,
                "SettingsSpacesStoreRetryAudit" => SpacesOriginalStoreSetupOperation.RetryAudit,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
            sources.BindOriginalCallerCallback(original.RunAcceptedSourceCallback);
            var accepted = _spacesStoreSnapshot;
            var result = await original.AwaitAsync(sources.RunToOriginalSettlementAsync(() =>
                backend.ExecuteWithinOriginalSourceAsync(operation, accepted, original.RunAcceptedSourceCallback,
                    raw => sources.Track(raw), token)));
            // Retain the actual result even if the view was hidden while Home review was open.
            // The App-owned session keeps its original request; reactivation can inspect it.
            _spacesStoreSnapshot = result;
        }
        catch
        {
            _spacesStoreSnapshot = null;
            _spacesStoreError = "Spaces storage could not be verified. Inspect Spaces storage to check its current state before continuing.";
            throw;
        }
        finally
        {
            _spacesStoreBusy = false;
            if (original.IsAcceptedPublicationCurrent)
                original.RunAcceptedPublicationCallback(RefreshBindings);
            else if (_active && IsAlive)
                // A retained action may settle after this Settings page was reactivated.
                // Publish current busy/result state through a fresh presentation-only scope;
                // do not replay its import, approval or inspection work.
                _work.RunSynchronous(current =>
                {
                    current.BindPublicationGuard(() => _active && IsAlive);
                    current.RunAcceptedPublicationCallback(RefreshBindings);
                });
        }
    });
}
