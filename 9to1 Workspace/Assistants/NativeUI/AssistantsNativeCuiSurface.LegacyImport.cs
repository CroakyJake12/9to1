using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task DispatchLegacyImportAsync(string command, CancellationToken token)
    {
        var owner = DemandMigrationOwner() as ILegacyAgentMigrationImportController
            ?? throw new InvalidOperationException("The actual Home import owner is unavailable in this presentation.");
        var previous = MigrationBindings.OriginalImportPreview;
        if (command != "assistants.legacy.import.inspect" && previous is null)
            throw new InvalidOperationException("Inspect this configured original store before reviewing import.");
        var generation = checked(++_migrationNavigationGeneration);
        if (_executing.Value is { } original) original.MigrationPublicationGeneration = generation;
        PublishSynchronous(() => MigrationBindings.SetBusy(true));
        LegacyAgentImportPreview? observed = null;
        try
        {
            observed = await SourceAsync(() => command switch
            {
                "assistants.legacy.import.inspect" => owner.InspectImportAsync(token),
                "assistants.legacy.import.request" => owner.RequestImportAsync(previous!, token),
                "assistants.legacy.import.refresh" => owner.RefreshImportAsync(previous!, token),
                "assistants.legacy.import.complete" => owner.CompleteImportAsync(previous!, token),
                "assistants.legacy.import.audit" => owner.RetryImportAuditAsync(previous!, token),
                _ => throw new InvalidOperationException("Unknown legacy store import action.")
            });
            if (IsMigrationPublicationCurrent(generation)) PublishSynchronous(() =>
            { if (IsMigrationPublicationCurrent(generation)) MigrationBindings.SetImportPreview(observed); });
        }
        finally
        {
            if (IsMigrationPublicationCurrent(generation)) PublishSynchronous(() =>
            { if (IsMigrationPublicationCurrent(generation)) MigrationBindings.SetBusy(false); });
        }
        if (observed?.CanBrowse == true && IsMigrationPublicationCurrent(generation))
            await LoadMigrationListAsync(null, token); // Rechecks actual import before content, then uses existing canonical migration.
    }
}
