using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private readonly ILegacyAgentMigrationController? _migration;
    private readonly string _migrationUnavailableReason;
    private AssistantsLegacyMigrationCuiBindings? _migrationBindings;
    private long _migrationNavigationGeneration;
    public AssistantsLegacyMigrationCuiBindings MigrationBindings => _migrationBindings ??
        throw new InvalidOperationException("The actual migration presentation was not acquired.");
    public ILegacyAgentMigrationController? OriginalMigrationController => _migration;

    private ILegacyAgentMigrationController DemandMigrationOwner() => _migration ??
        throw new InvalidOperationException(_migrationUnavailableReason);
    private bool IsMigrationPublicationCurrent(long generation) => !IsRetiring &&
        generation == _migrationNavigationGeneration && Bindings.IsLegacyMigrationVisible;

    private async Task LoadMigrationListAsync(string? cursor, CancellationToken token)
    {
        if (_migration is null) return; // The same authored scene displays the genuine missing-source setup state.
        var generation = checked(++_migrationNavigationGeneration);
        if (_executing.Value is { } original) original.MigrationPublicationGeneration = generation;
        PublishSynchronous(() => MigrationBindings.SetBusy(true));
        try
        {
            if (_migration is ILegacyAgentMigrationImportController imports)
            {
                var setup = await SourceAsync(() => imports.InspectImportAsync(token));
                if (IsMigrationPublicationCurrent(generation)) PublishSynchronous(() =>
                { if (IsMigrationPublicationCurrent(generation)) MigrationBindings.SetImportPreview(setup); });
                if (!setup.CanBrowse) return; // No legacy content query before the actual Home receipt exists.
            }
            var page = await SourceAsync(() => _migration.ListPageAsync(cursor, 30, token));
            if (IsMigrationPublicationCurrent(generation))
                PublishSynchronous(() => { if (IsMigrationPublicationCurrent(generation)) MigrationBindings.SetPage(page); });
        }
        finally { if (IsMigrationPublicationCurrent(generation)) PublishSynchronous(() =>
        { if (IsMigrationPublicationCurrent(generation)) MigrationBindings.SetBusy(false); }); }
    }

    private ValueTask DispatchMigrationAsync(string command, object? parameter, CancellationToken token) =>
        new(RunAsync(async () =>
        {
            PublishSynchronous(() =>
            {
                if (!Bindings.IsLegacyMigrationVisible || MigrationBindings.IsActionAvailable(command) != true)
                    throw new InvalidOperationException("This original migration review no longer accepts that action.");
            });
            if (command.StartsWith("assistants.legacy.import.", StringComparison.Ordinal))
            { await DispatchLegacyImportAsync(command, token); return; }
            switch (command)
            {
                case "assistants.legacy.back":
                    PublishSynchronous(() => { _migrationNavigationGeneration = checked(_migrationNavigationGeneration + 1); Bindings.ShowHome(); });
                    return;
                case "assistants.legacy.cancel":
                    PublishSynchronous(() => { _migrationNavigationGeneration = checked(_migrationNavigationGeneration + 1); MigrationBindings.CancelReview(); });
                    return; // No classify/recovery/source mutation occurs.
                case "assistants.legacy.list": await LoadMigrationListAsync(null, token); return;
                case "assistants.legacy.next": await LoadMigrationListAsync(MigrationBindings.NextCursor, token); return;
                case "assistants.legacy.kind.assistant":
                    PublishSynchronous(() => MigrationBindings.ChooseKind(ConfiguredIdentityKind.Assistant)); return;
                case "assistants.legacy.kind.specialist":
                    PublishSynchronous(() => MigrationBindings.ChooseKind(ConfiguredIdentityKind.Specialist)); return;
            }

            var owner = DemandMigrationOwner();
            var navigation = checked(++_migrationNavigationGeneration);
            if (_executing.Value is { } original) original.MigrationPublicationGeneration = navigation;
            PublishSynchronous(() => MigrationBindings.SetBusy(true));
            try
            {
                switch (command)
                {
                    case "assistants.legacy.preview":
                    {
                        var item = MigrationBindings.DemandListItem(parameter);
                        var preview = await SourceAsync(() => owner.PreviewAsync(item.LegacyAgentId, token));
                        if (IsMigrationPublicationCurrent(navigation)) PublishSynchronous(() =>
                        { if (IsMigrationPublicationCurrent(navigation)) MigrationBindings.SetPreview(preview); });
                        break;
                    }
                    case "assistants.legacy.confirm":
                    {
                        var draft = MigrationBindings.OriginalDraft ?? throw new InvalidOperationException("Open the original definition review.");
                        var submitted = draft.CaptureSubmission(); var review = MigrationBindings.ReviewGeneration;
                        var receipt = await SourceAsync(() => owner.ClassifyAsync(submitted.Preview, submitted.Kind,
                            submitted.Configuration, submitted.OperationId, token));
                        if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsReviewCurrent(submitted.Preview, review))
                            PublishSynchronous(() =>
                            {
                                if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsReviewCurrent(submitted.Preview, review))
                                    MigrationBindings.SetResult(submitted, receipt);
                            });
                        if (!IsRetiring) await SourceAsync(() => _controller.InitializeAsync(token)); // Actual receipt first; no private registry.
                        break;
                    }
                    case "assistants.legacy.links.runs":
                    case "assistants.legacy.links.memories":
                    case "assistants.legacy.links.references":
                    case "assistants.legacy.links.conversations":
                    case "assistants.legacy.links.next":
                    {
                        var preview = MigrationBindings.OriginalDraft?.Preview ?? throw new InvalidOperationException("Open the original definition review.");
                        var review = MigrationBindings.ReviewGeneration;
                        var kind = command == "assistants.legacy.links.next" ? MigrationBindings.LinkKind : command[(command.LastIndexOf('.') + 1)..];
                        var cursor = command == "assistants.legacy.links.next" ? MigrationBindings.NextLinksCursor : null;
                        var page = await SourceAsync(() => owner.ReadPreservedLinksAsync(preview, kind, cursor, 30, token));
                        if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsReviewCurrent(preview, review))
                            PublishSynchronous(() =>
                            {
                                if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsReviewCurrent(preview, review))
                                    MigrationBindings.SetLinks(kind, page);
                            });
                        break; // Preserved IDs are displayed, not opened as authoritative resources.
                    }
                    case "assistants.legacy.recovery":
                    {
                        var definition = MigrationBindings.OriginalClassifiedDefinition ?? throw new InvalidOperationException("Open an actual persisted migration result first.");
                        var recovery = await SourceAsync(() => owner.ReadRecoveryAsync(definition.Identity, token));
                        if (IsMigrationPublicationCurrent(navigation) && ReferenceEquals(MigrationBindings.OriginalClassifiedDefinition, definition))
                            PublishSynchronous(() =>
                            {
                                if (IsMigrationPublicationCurrent(navigation) && ReferenceEquals(MigrationBindings.OriginalClassifiedDefinition, definition))
                                    MigrationBindings.SetRecovery(recovery);
                            });
                        break;
                    }
                    case "assistants.legacy.undo":
                    {
                        var preview = MigrationBindings.OriginalRecovery ?? throw new InvalidOperationException("Review the actual recovery first.");
                        var review = MigrationBindings.ReviewGeneration;
                        var receipt = await SourceAsync(() => owner.UndoAsync(preview, Guid.NewGuid(), token));
                        if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsRecoveryCurrent(preview, review))
                            PublishSynchronous(() =>
                            {
                                if (IsMigrationPublicationCurrent(navigation) && MigrationBindings.IsRecoveryCurrent(preview, review))
                                    MigrationBindings.SetUndoResult(receipt);
                            });
                        if (!IsRetiring) await SourceAsync(() => _controller.InitializeAsync(token));
                        if (IsMigrationPublicationCurrent(navigation))
                        {
                            var page = await SourceAsync(() => owner.ListPageAsync(null, 30, token));
                            if (IsMigrationPublicationCurrent(navigation)) PublishSynchronous(() =>
                            {
                                if (IsMigrationPublicationCurrent(navigation))
                                { MigrationBindings.SetPage(page); MigrationBindings.SetStatus("The conversion was undone. Your original definition and history remain preserved."); }
                            });
                        }
                        break;
                    }
                    case "assistants.legacy.result.open":
                    {
                        var definition = MigrationBindings.OriginalClassifiedDefinition ?? throw new InvalidOperationException("Open an actual persisted migration result first.");
                        if (definition.Kind != ConfiguredIdentityKind.Assistant)
                            throw new InvalidOperationException("A Specialist is not opened through the Assistant conversation editor.");
                        await SourceAsync(() => _controller.OpenAssistantAsync(definition.Identity, token));
                        if (IsMigrationPublicationCurrent(navigation)) PublishSynchronous(() =>
                        { if (IsMigrationPublicationCurrent(navigation)) { MigrationBindings.CancelReview(); Bindings.ShowWork(); } });
                        break;
                    }
                    default: throw new InvalidOperationException("This migration action has no actual configured owner.");
                }
            }
            finally { if (IsMigrationPublicationCurrent(navigation)) PublishSynchronous(() =>
            { if (IsMigrationPublicationCurrent(navigation)) MigrationBindings.SetBusy(false); }); }
        }, handlesCommandRefusal: true));
}
