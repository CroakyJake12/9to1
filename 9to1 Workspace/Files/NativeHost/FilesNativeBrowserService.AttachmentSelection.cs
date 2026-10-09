using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService : ICanonicalAttachmentOriginalSelectionSource
{
    private sealed class AttachmentSelection(FilesNativeBrowserService owner, NativeFilesWorkspace workspace,
        FilesNativeBrowserPage page, HostedItemMetadata row, FilesMaterializedFile materialization)
        : ICanonicalAttachmentOriginalSelection
    {
        internal FilesNativeBrowserService Owner => owner;
        internal NativeFilesWorkspace Workspace => workspace;
        internal FilesNativeBrowserPage Page => page;
        internal HostedItemMetadata Row => row;
        internal FilesMaterializedFile Materialization => materialization;
        public AuthenticatedResourceActor OriginalActor => workspace.Actor;
        public string OriginalMaterializationPath => materialization.LocalPath;
        public CanonicalAttachmentFileIdentity OriginalFile { get; } = new(workspace.Configuration.StoreId,
            row.Id.Value, row.CurrentRevisionId!.Value.Value, materialization.SizeBytes,
            materialization.ContentHash!, row.Name);
    }
    private readonly ConditionalWeakTable<ICanonicalAttachmentOriginalSelection, AttachmentSelection> _attachmentSelections = new();

    public bool IsIssuedOriginalSelection(ICanonicalAttachmentOriginalSelection sameSelection)
    {
        lock (_attachmentSourceGate)
            return !_attachmentSourceRetiring && sameSelection is AttachmentSelection actual &&
                ReferenceEquals(actual.Owner, this) && _attachmentSelections.TryGetValue(sameSelection, out var retained) &&
                ReferenceEquals(actual, retained) && _originalPages.TryGetValue(actual.Page, out var page) &&
                ReferenceEquals(page.Workspace.Provider, actual.Workspace.Provider) &&
                actual.Page.Items.Any(row => ReferenceEquals(row, actual.Row));
    }
    public Task<ICanonicalAttachmentOriginalSelection?> ResolveOriginalPickedPathWithinSourceAsync(
        AuthenticatedResourceActor expectedHomeActor, string observedPickedPath,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        AdmitAttachmentSource<ICanonicalAttachmentOriginalSelection?>(scope, retain, async source =>
        {
            var workspace = await RequireAttachmentWorkspace(expectedHomeActor, null, source, token).ConfigureAwait(false);
            source.Invoke(() => { RememberAttachmentStores(workspace); return true; });
            var materialization = await source.Observe(() => workspace.Materializations.GetExistingOriginalAttachmentByPathAsync(
                observedPickedPath, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
            if (materialization is null) return null;
            var observed = await ReadAttachmentRow(workspace, materialization, source, token).ConfigureAwait(false);
            var row = observed.Row;
            // This privately issued page is a current canonical metadata observation.
            // It has one selected row; it grants neither byte READ nor import WRITE.
            var page = new FilesNativeBrowserPage(workspace.Configuration.StoreId,
                observed.StoreRevision, row.ParentId?.Value, Array.AsReadOnly(new[] { row }), null);
            source.Invoke(() => { _originalPages.Add(page, new(workspace, workspace.Actor)); return true; });
            var selected = new AttachmentSelection(this, workspace, page, row, materialization);
            await DemandAttachmentSelection(selected, source, token).ConfigureAwait(false);
            source.Invoke(() => { _attachmentSelections.Add(selected, selected); return true; });
            return selected;
        });
    public Task RevalidateOriginalSelectionWithinSourceAsync(ICanonicalAttachmentOriginalSelection sameSelection,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        AdmitAttachmentSource(scope, retain, async source =>
        {
            var actual = source.Invoke(() => IsIssuedOriginalSelection(sameSelection)
                ? (AttachmentSelection)sameSelection : throw new UnauthorizedAccessException("Retain the current Files attachment selection."));
            await DemandAttachmentSelection(actual, source, token).ConfigureAwait(false); return true;
        });
    private async Task<NativeFilesWorkspace> RequireAttachmentWorkspace(AuthenticatedResourceActor actor, Guid? store,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        if (actors is not IOriginalScopedResourceActorSource actualActors)
            throw new NotSupportedException("The configured Files actor lacks its original scoped source.");
        if (await source.Observe(() => actualActors.GetCurrentWithinOriginalSourceAsync(source.OriginalSynchronousScope,
            source.RetainOriginalTask, token)).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The current Home profile changed.");
        var workspace = await source.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(store, source, token)).ConfigureAwait(false);
        if (workspace?.Actor != actor || await source.Observe(() => actualActors.GetCurrentWithinOriginalSourceAsync(
            source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false) != actor)
            throw new UnauthorizedAccessException("The current Home profile has no verified Files store.");
        return workspace;
    }
    private async Task<FilesOriginalAttachmentMetadata> ReadAttachmentRow(NativeFilesWorkspace workspace, FilesMaterializedFile mapping,
        FilesOriginalReadSourceScope source, CancellationToken token)
    {
        var result = await source.Observe(() => workspace.Provider.GetOriginalAttachmentMetadataWithinSourceAsync(
            workspace.Configuration.StoreId, mapping.ItemId, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
        var row = result.IsSuccess ? result.Value?.Row : null;
        var principal = workspace.Actor.AccountId is { } account ? account.ToString("N") : "local-profile:" + workspace.Actor.ProfileId;
        if (row is not { Kind: HostedItemKind.File, CurrentRevisionId: { } revision } ||
            row.OwnerPrincipalId != principal || row.Scope != "personal" || row.LocationId != workspace.Configuration.LocationId ||
            mapping.ItemId != row.Id || mapping.BaseRemoteRevisionId != revision || mapping.CurrentRemoteRevisionId != revision ||
            mapping.LocalRevisionId is not null || mapping.State is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable) ||
            row.SizeBytes != mapping.SizeBytes || mapping.SizeBytes <= 0 || !IsAttachmentHash(mapping.ContentHash) ||
            !string.Equals(row.ContentHash, mapping.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Select an unchanged, locally available personal Files revision.");
        return result.Value!;
    }
    private static bool IsAttachmentHash(string? value) => value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && value.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    private async Task DemandAttachmentSelection(AttachmentSelection selected, FilesOriginalReadSourceScope source, CancellationToken token)
    {
        var current = await RequireAttachmentWorkspace(selected.OriginalActor, selected.OriginalFile.StoreId, source, token).ConfigureAwait(false);
        if (!ReferenceEquals(current.Provider, selected.Workspace.Provider) ||
            !ReferenceEquals(current.Materializations, selected.Workspace.Materializations) ||
            !ReferenceEquals(current.Directories, selected.Workspace.Directories))
            throw new UnauthorizedAccessException("The original Files workspace changed.");
        var mapping = await source.Observe(() => current.Materializations.GetExistingOriginalAttachmentByPathAsync(
            selected.OriginalMaterializationPath, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
        if (mapping != selected.Materialization || mapping is null)
            throw new UnauthorizedAccessException("The selected Files materialization changed.");
        var observed = await ReadAttachmentRow(current, mapping, source, token).ConfigureAwait(false);
        if (observed.Row != selected.Row || observed.StoreRevision != selected.Page.StoreRevision) throw new UnauthorizedAccessException("The selected Files revision changed.");
        await RequireAttachmentWorkspace(selected.OriginalActor, selected.OriginalFile.StoreId, source, token).ConfigureAwait(false);
    }
}
