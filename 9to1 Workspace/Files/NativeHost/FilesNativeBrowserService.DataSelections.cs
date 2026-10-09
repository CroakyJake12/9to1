using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService : ICanonicalOriginalDataFileSelectionSource
{
    private sealed class DataSelection : ICanonicalOriginalDataFileSelection
    {
        internal readonly FilesNativeBrowserService Owner;
        internal readonly NativeFilesWorkspace Workspace;
        internal readonly FilesNativeBrowserPage Page;
        internal readonly HostedItemMetadata Row;
        internal readonly FilesMaterializedFile Materialization;
        public AuthenticatedResourceActor OriginalActor => Workspace.Actor;
        public Guid OriginalFileId => Row.Id.Value;
        public Guid OriginalRevisionId => Row.CurrentRevisionId!.Value.Value;
        public string OriginalName => Row.Name;
        public long OriginalSizeBytes => Materialization.SizeBytes;
        public string OriginalContentSha256 => Materialization.ContentHash!;
        public ResourceStoreIdentity OriginalStoreIdentity { get; }
        public VerifiedResourceStoreOwnership OriginalStoreOwnership { get; }
        public ResourceScope OriginalScope { get; }

        internal DataSelection(FilesNativeBrowserService owner, NativeFilesWorkspace workspace,
            FilesNativeBrowserPage page, HostedItemMetadata row, FilesMaterializedFile materialization,
            FilesStoreEvidence evidence, VerifiedResourceStoreOwnership ownership)
        {
            Owner = owner; Workspace = workspace; Page = page; Row = row; Materialization = materialization;
            OriginalStoreIdentity = new(evidence.SchemaVersion, evidence.StoreId, evidence.CreatedAtUtc, evidence.NewlyCreated);
            OriginalStoreOwnership = ownership;
            OriginalScope = new("files.item", row.Id.ToString(), row.CurrentRevisionId!.Value.ToString(), ResourceAccess.Read);
        }
    }
    private readonly ConditionalWeakTable<ICanonicalOriginalDataFileSelection, DataSelection> _dataSelections = new();

    public bool IsIssuedOriginalDataFileSelection(ICanonicalOriginalDataFileSelection sameSelection)
    {
        lock (_dataSourceGate)
            return !_dataSourceRetiring && sameSelection is DataSelection actual && ReferenceEquals(actual.Owner, this) &&
                _dataSelections.TryGetValue(sameSelection, out var retained) && ReferenceEquals(actual, retained) &&
                _originalPages.TryGetValue(actual.Page, out var issuedPage) && issuedPage.Actor == actual.OriginalActor &&
                ReferenceEquals(issuedPage.Workspace.Provider, actual.Workspace.Provider) &&
                ReferenceEquals(issuedPage.Workspace.Materializations, actual.Workspace.Materializations) &&
                ReferenceEquals(issuedPage.Workspace.Directories, actual.Workspace.Directories) &&
                actual.Page.Items.Any(row => ReferenceEquals(row, actual.Row));
    }

    /// <summary>Only metadata from the exact current privately issued Files page
    /// and row is selected. Bytes, worker launch, edit and save require separate admission.</summary>
    public Task<ICanonicalOriginalDataFileSelection> SelectOriginalDataFileWithinSourceAsync(
        FilesNativeBrowserPage samePage, HostedItemMetadata sameRow, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        AdmitDataSource(scope, retain, async source =>
        {
            var original = source.Invoke(() => _originalPages.TryGetValue(samePage, out var issued) &&
                issued.Actor == actualActor && samePage.Items.Any(row => ReferenceEquals(row, sameRow)) &&
                sameRow.Kind == HostedItemKind.File && sameRow.CurrentRevisionId is not null
                    ? issued : throw new UnauthorizedAccessException("Select the current original Files row for Data."));
            var workspace = await RequireDataWorkspace(actualActor, samePage.StoreID, source, token).ConfigureAwait(false);
            source.Invoke(() => { DemandDataWorkspace(original.Workspace, workspace); return true; });
            var ownership = await source.Observe(() => workspaces.CaptureOriginalDataOwnershipWithinSourceAsync(
                workspace, source, token)).ConfigureAwait(false);
            var mapping = await source.Observe(() => workspace.Materializations.GetExistingOriginalDataByItemIdWithinSourceAsync(
                sameRow.Id, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Make the selected file available locally in Files first.");
            var observed = await ReadDataRow(workspace, mapping, source, token).ConfigureAwait(false);
            source.Invoke(() =>
            {
                if (observed.Row != sameRow || observed.OriginalStore.Revision != samePage.StoreRevision)
                    throw new UnauthorizedAccessException("The selected Files page changed. Refresh it before opening Data.");
                return true;
            });
            var selection = new DataSelection(this, workspace, samePage, sameRow, mapping, observed.OriginalStore, ownership);
            await DemandDataSelection(selection, source, token).ConfigureAwait(false);
            source.Invoke(() => { _dataSelections.Add(selection, selection); return true; });
            return (ICanonicalOriginalDataFileSelection)selection;
        });

    public Task RevalidateOriginalDataFileSelectionWithinSourceAsync(ICanonicalOriginalDataFileSelection sameSelection,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        AdmitDataSource(scope, retain, async source =>
        {
            var actual = source.Invoke(() => IsIssuedOriginalDataFileSelection(sameSelection)
                ? (DataSelection)sameSelection : throw new UnauthorizedAccessException("Retain the original Data file selection."));
            await DemandDataSelection(actual, source, token).ConfigureAwait(false); return true;
        });

    private async Task<NativeFilesWorkspace> RequireDataWorkspace(AuthenticatedResourceActor actor, Guid store,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (actors is not IOriginalScopedResourceActorSource actualActors)
            throw new NotSupportedException("The actual scoped Files actor is required.");
        if (await source.Observe(() => actualActors.GetCurrentWithinOriginalSourceAsync(source.OriginalSynchronousScope,
            source.RetainOriginalTask, token)).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The selected Files profile changed.");
        var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(store, source, token)).ConfigureAwait(false);
        if (workspace?.Actor != actor || actor.AccountId is not null || actor.OrganisationId is not null ||
            await source.Observe(() => actualActors.GetCurrentWithinOriginalSourceAsync(source.OriginalSynchronousScope,
                source.RetainOriginalTask, token)).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The current profile has no verified personal Files store.");
        return workspace;
    }
    private static void DemandDataWorkspace(NativeFilesWorkspace original, NativeFilesWorkspace current)
    {
        if (original.Actor != current.Actor || original.Configuration != current.Configuration ||
            !ReferenceEquals(original.Provider, current.Provider) || !ReferenceEquals(original.Materializations, current.Materializations) ||
            !ReferenceEquals(original.Directories, current.Directories))
            throw new UnauthorizedAccessException("The original Files workspace changed.");
    }
    private async Task<FilesOriginalDataMetadata> ReadDataRow(NativeFilesWorkspace workspace, FilesMaterializedFile mapping,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        var result = await source.Observe(() => workspace.Provider.GetOriginalDataMetadataWithinSourceAsync(
            workspace.Configuration.StoreId, mapping.ItemId, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
        var row = result.IsSuccess ? result.Value?.Row : null;
        var principal = "local-profile:" + workspace.Actor.ProfileId;
        if (row is not { Kind: HostedItemKind.File, CurrentRevisionId: { } revision } || row.OwnerPrincipalId != principal ||
            row.Scope != "personal" || row.LocationId != workspace.Configuration.LocationId || mapping.ItemId != row.Id ||
            mapping.BaseRemoteRevisionId != revision || mapping.CurrentRemoteRevisionId != revision || mapping.LocalRevisionId is not null ||
            mapping.State is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable) ||
            mapping.SizeBytes <= 0 || row.SizeBytes != mapping.SizeBytes || !IsDataContentHash(mapping.ContentHash) ||
            !string.Equals(row.ContentHash, mapping.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Select an unchanged, locally available personal Files revision.");
        return result.Value!;
    }
    private static bool IsDataContentHash(string? value) => value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    private async Task DemandDataSelection(DataSelection selected, FilesOriginalReadSourceScope source, CancellationToken token)
    {
        var workspace = await RequireDataWorkspace(selected.OriginalActor, selected.OriginalStoreIdentity.StoreId, source, token).ConfigureAwait(false);
        source.Invoke(() => { DemandDataWorkspace(selected.Workspace, workspace); return true; });
        await source.ObserveVoid(() => workspaces.ValidateOriginalDataOwnershipWithinSourceAsync(workspace,
            selected.OriginalStoreOwnership, source, token)).ConfigureAwait(false);
        var mapping = await source.Observe(() => workspace.Materializations.GetExistingOriginalDataByItemIdWithinSourceAsync(
            selected.Row.Id, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
        if (mapping is null || mapping != selected.Materialization)
            throw new UnauthorizedAccessException("The selected Files materialization changed.");
        var actual = await ReadDataRow(workspace, mapping, source, token).ConfigureAwait(false);
        source.Invoke(() =>
        {
            if (actual.Row != selected.Row || actual.OriginalStore.Revision != selected.Page.StoreRevision ||
                actual.OriginalStore.StoreId != selected.OriginalStoreIdentity.StoreId ||
                actual.OriginalStore.CreatedAtUtc != selected.OriginalStoreIdentity.CreatedAtUtc ||
                actual.OriginalStore.SchemaVersion != selected.OriginalStoreIdentity.SchemaVersion)
                throw new UnauthorizedAccessException("The selected Files row or store identity changed.");
            return true;
        });
        var final = await RequireDataWorkspace(selected.OriginalActor, selected.OriginalStoreIdentity.StoreId, source, token).ConfigureAwait(false);
        source.Invoke(() => { DemandDataWorkspace(selected.Workspace, final); return true; });
    }
}

public sealed partial class NativeFilesWorkspaceAuthority
{
    internal async Task<VerifiedResourceStoreOwnership> CaptureOriginalDataOwnershipWithinSourceAsync(
        NativeFilesWorkspace sameWorkspace, FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (!workspaces.IsOriginalReadComposition(profiles, ownership) || ownership is not IResourceStoreOriginalScopedOwnershipAuthority actual)
            throw new NotSupportedException("The SAME original Home Files ownership issuer is required.");
        var captured = await source.Observe(() => actual.GetVerifiedWithinOriginalSourceAsync("files",
            sameWorkspace.Configuration.StoreId.ToString("D"), source.OriginalSynchronousScope, source.RetainOriginalTask, token).AsTask()).ConfigureAwait(false);
        if (captured?.Receipt is null || captured.ProfileId != sameWorkspace.Actor.ProfileId || captured.ResourceKind != "files" ||
            captured.StoreId != sameWorkspace.Configuration.StoreId.ToString("D"))
            throw new UnauthorizedAccessException("The original Home Files receipt is unavailable.");
        await ValidateOriginalDataOwnershipWithinSourceAsync(sameWorkspace, captured, source, token).ConfigureAwait(false);
        return captured;
    }
    internal async Task ValidateOriginalDataOwnershipWithinSourceAsync(NativeFilesWorkspace sameWorkspace,
        VerifiedResourceStoreOwnership captured, FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (!workspaces.IsOriginalReadComposition(profiles, ownership) || ownership is not IResourceStoreOriginalScopedOwnershipAuthority actual ||
            !await source.Observe(() => actual.IsCurrentWithinOriginalSourceAsync(captured, sameWorkspace.Actor,
                source.OriginalSynchronousScope, source.RetainOriginalTask, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The selected Home Files receipt changed.");
    }
}
