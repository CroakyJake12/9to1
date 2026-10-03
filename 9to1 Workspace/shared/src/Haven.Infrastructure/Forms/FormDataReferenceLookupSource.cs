using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Trusted host configuration, not a grant or wire-provided lookup target. Version IDs
/// bind the actual retained Forms column to the original, already configured Data revision.</summary>
public sealed record FormDataReferenceLookupBinding(Guid FormID, Guid FormVersionID, Guid FieldID,
    Guid ColumnID, Guid DataStoreID, Guid WorkbookID, Guid TableID, Guid LabelFieldID,
    Guid DataRevisionID, int DataVersion);

/// <summary>Personal local owner read composition only. This never calls Home Begin/Claim/Complete
/// or Data Save. Public respondents/AI/cloud require separately implemented caller policy.
/// The owning factory uses actual Home/profile/ownership and actual configured stores; no ambient
/// fallback or arbitrary supplied FormResponseSessionService can stand in for that composition.</summary>
public sealed class FormDataReferenceLookupSource : IFormDataReferenceLookupSource
{
    private readonly FileHomeCoreStateStore _home;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly IDataGuardedWorkbookRepository _data;
    private readonly VersionedAtomicSettingsStore _forms;
    private readonly FormResponseSessionService _responses;
    private readonly FormDataReferenceLookupBinding[] _bindings;
    public FormDataReferenceLookupSource(FileHomeCoreStateStore home, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, IDataGuardedWorkbookRepository data,
        VersionedAtomicSettingsStore forms, IReadOnlyList<FormDataReferenceLookupBinding> trustedBindings)
    {
        _home = home; _profiles = profiles; _ownership = ownership; _data = data; _forms = forms;
        ArgumentNullException.ThrowIfNull(trustedBindings);
        _bindings = trustedBindings.Take(257).ToArray();
        if (_bindings.Length > 256 || _bindings.Any(binding => binding.FormID == Guid.Empty
            || binding.FormVersionID == Guid.Empty || binding.FieldID == Guid.Empty || binding.ColumnID == Guid.Empty
            || binding.DataStoreID == Guid.Empty || binding.WorkbookID == Guid.Empty || binding.TableID == Guid.Empty
            || binding.LabelFieldID == Guid.Empty || binding.DataRevisionID == Guid.Empty || binding.DataVersion < 1)
            || _bindings.Select(binding => (binding.FormID, binding.FormVersionID, binding.FieldID, binding.ColumnID)).Distinct().Count() != _bindings.Length)
            throw new ArgumentException("Lookup configuration must identify distinct original published columns and Data revisions.");
        var authority = new FormLocalStoreAuthority(forms, forms, profiles, ownership);
        var publications = new FormPublicationService(forms, forms, authority, new ReadOnlyPublicationValidator(), actors: profiles);
        _responses = new(publications, forms, forms, authority, profiles);
    }
    public async Task<IFormDataReferenceLookupSession?> OpenForOriginalResponseAsync(Guid formID, Guid responseID,
        Guid fieldID, Guid columnID, AuthenticatedResourceActor originalActor, Func<bool> pureOriginalLifetime,
        CancellationToken token = default)
    {
        if (!HomeLocalReadComposition.IsBound(_home, _profiles, _ownership)
            || originalActor.AccountId is not null || originalActor.OrganisationId is not null
            || !Alive(pureOriginalLifetime) || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor) return null;
        var root = await _forms.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (!Alive(pureOriginalLifetime) || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor) return null;
        var formsReceipt = await _ownership.GetVerifiedAsync("forms", root.StoreId.ToString("D"), token).ConfigureAwait(false);
        if (!Alive(pureOriginalLifetime) || formsReceipt?.Receipt is null || formsReceipt.ProfileId != originalActor.ProfileId
            || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor
            || !await _ownership.IsCurrentAsync(formsReceipt, originalActor, token).ConfigureAwait(false)
            || !Alive(pureOriginalLifetime)) return null;
        var scope = new FormResponseSessionScope(root.StoreId, originalActor);
        var loaded = await _responses.ReadSessionAsync(formID, responseID, token, scope).ConfigureAwait(false);
        if (!Alive(pureOriginalLifetime) || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor
            || !loaded.Success || loaded.Response!.State != FormResponseState.InProgress
            || !await _ownership.IsCurrentAsync(formsReceipt, originalActor, token).ConfigureAwait(false)) return null;
        var binding = _bindings.SingleOrDefault(value => value.FormID == formID
            && value.FormVersionID == loaded.Response.FormVersionID && value.FieldID == fieldID && value.ColumnID == columnID);
        var field = loaded.Presentation!.Fields.SingleOrDefault(value => value.FieldID == fieldID);
        var column = field?.Table?.Columns.SingleOrDefault(value => value.ColumnID == columnID);
        if (binding is null || column?.Type != FormTableCellType.Reference || column.ReferencedTableID != binding.TableID) return null;
        var receipt = await _ownership.GetVerifiedAsync("data", binding.DataStoreID.ToString("D"), token).ConfigureAwait(false);
        if (!Alive(pureOriginalLifetime) || receipt?.Receipt is null || receipt.ProfileId != originalActor.ProfileId
            || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor
            || !await _ownership.IsCurrentAsync(receipt, originalActor, token).ConfigureAwait(false)
            || !Alive(pureOriginalLifetime) || await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != originalActor) return null;
        return new Session(this, binding, responseID, scope, pureOriginalLifetime, receipt, formsReceipt);
    }
    private static bool Alive(Func<bool> lifetime) { try { return lifetime(); } catch { return false; } }
    private sealed class ReadOnlyPublicationValidator : IFormProjectPublicationValidator
    { public void Validate(Guid formID, JsonElement project) => throw new NotSupportedException("Read-only lookup source cannot author or publish Forms."); }
    private sealed class Session(FormDataReferenceLookupSource source, FormDataReferenceLookupBinding binding,
        Guid responseID, FormResponseSessionScope scope, Func<bool> lifetime,
        VerifiedResourceStoreOwnership originalReceipt, VerifiedResourceStoreOwnership originalFormsReceipt) : IFormDataReferenceLookupSession
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _retired = new();
        private FormDataReferenceChoice[] _issued = [];
        private int _disposed;
        public Guid FormID => binding.FormID;
        public Guid FormVersionID => binding.FormVersionID;
        public Guid ResponseID => responseID;
        public Guid FieldID => binding.FieldID;
        public Guid ColumnID => binding.ColumnID;
        public Guid TableID => binding.TableID;
        private FormDataReferenceLookupSource OwnerSource => source;
        private FormDataReferenceLookupBinding OriginalBinding => binding;
        private FormResponseSessionScope OriginalScope => scope;
        private VerifiedResourceStoreOwnership OriginalReceipt => originalReceipt;
        private VerifiedResourceStoreOwnership OriginalFormsReceipt => originalFormsReceipt;
        private bool Current => Volatile.Read(ref _disposed) == 0 && Alive(lifetime);
        private async ValueTask<bool> FormCurrentAsync(CancellationToken token)
        {
            if (!Current || await source._profiles.GetCurrentAsync(token).ConfigureAwait(false) != scope.Actor
                || !await source._ownership.IsCurrentAsync(originalFormsReceipt, scope.Actor, token).ConfigureAwait(false)) return false;
            var loaded = await source._responses.ReadSessionAsync(FormID, ResponseID, token, scope).ConfigureAwait(false);
            if (!Current || !loaded.Success || loaded.Response!.FormVersionID != FormVersionID
                || loaded.Response.State != FormResponseState.InProgress
                || await source._profiles.GetCurrentAsync(token).ConfigureAwait(false) != scope.Actor
                || !await source._ownership.IsCurrentAsync(originalFormsReceipt, scope.Actor, token).ConfigureAwait(false) || !Current) return false;
            var column = loaded.Presentation!.Fields.SingleOrDefault(field => field.FieldID == FieldID)?.Table?.Columns.SingleOrDefault(column => column.ColumnID == ColumnID);
            return column?.Type == FormTableCellType.Reference && column.ReferencedTableID == TableID;
        }
        private async Task<DataWorkbook?> ReadOriginalAsync(CancellationToken token)
        {
            if (!await FormCurrentAsync(token).ConfigureAwait(false)) return null;
            var resolver = new LookupResolver(this);
            var resources = new ResourceAuthorizationService(source._profiles, [resolver]);
            var readScope = new ResourceScope(LookupResolver.Kind,
                binding.DataStoreID.ToString("D") + "/" + binding.WorkbookID.ToString("D") + "/" + binding.TableID.ToString("D"),
                binding.DataRevisionID.ToString("D"), ResourceAccess.Read);
            if (await resources.AuthorizeForActorAsync(scope.Actor, LookupResolver.ActionID, [readScope], token).ConfigureAwait(false) != scope.Actor
                || !Current) return null;
            var workbook = await resolver.ReadOriginalAsync(scope.Actor, token).ConfigureAwait(false);
            if (workbook is null || !await FormCurrentAsync(token).ConfigureAwait(false)) return null;
            // Verify the actual source again after the Forms await; a completed old repository
            // return cannot certify that a replacement revision is still the configured original.
            return await resolver.ReadOriginalAsync(scope.Actor, token).ConfigureAwait(false);
        }
        public async ValueTask<FormDataReferenceChoices> ReadChoicesAsync(string query, int offset, int maximum,
            CancellationToken cancellationToken = default)
        {
            if (query is null || query.Length > 1024 || offset < 0 || maximum is < 1 or > 200)
                return new(false, "InvalidLookupRange", null, false, binding.DataRevisionID);
            if (!Current) return new(false, "OriginalLookupRetired", null, false, binding.DataRevisionID);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _retired.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                _issued = [];
                var workbook = await ReadOriginalAsync(linked.Token).ConfigureAwait(false);
                if (workbook is null) return new(false, "PermissionDeniedOrSourceChanged", null, false, binding.DataRevisionID);
                var table = workbook.Tables.Single(value => value.Id == TableID);
                if (table.Records.Count > 10000) return new(false, "LookupCapacityUnavailable", null, false, binding.DataRevisionID);
                var choices = new List<FormDataReferenceChoice>(); var matched = 0;
                foreach (var record in table.Records.OrderBy(record => record.SheetRow).ThenBy(record => record.RecordID))
                {
                    var value = DataTableIdentity.ReadCell(workbook, TableID, record.RecordID, binding.LabelFieldID)?.Value ?? "";
                    var label = value.Length == 0 ? record.RecordID.ToString("D") : value;
                    if (label.Length > 1024) return new(false, "LookupLabelUnavailable", null, false, binding.DataRevisionID);
                    if (!label.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    if (matched++ < offset) continue;
                    choices.Add(new(record.RecordID, label));
                    if (choices.Count > maximum) break;
                }
                // Re-observe the original actual Data revision after the final Form await as well.
                // This is an observed read boundary, not an atomic lease spanning both stores.
                if (!Current || await ReadOriginalAsync(linked.Token).ConfigureAwait(false) is null)
                    return new(false, "OriginalLookupRetired", null, false, binding.DataRevisionID);
                var more = choices.Count > maximum;
                _issued = choices.Take(maximum).ToArray();
                return new(true, "LookupRead", Array.AsReadOnly(_issued), more, binding.DataRevisionID);
            }
            catch (IOException) { _issued = []; return new(false, "LookupUnavailable", null, false, binding.DataRevisionID); }
            catch (UnauthorizedAccessException) { _issued = []; return new(false, "PermissionDenied", null, false, binding.DataRevisionID); }
            finally { _gate.Release(); }
        }
        public async ValueTask<JsonElement?> SelectAsync(FormDataReferenceChoice originalChoice,
            CancellationToken cancellationToken = default)
        {
            if (!Current) return null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _retired.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (!_issued.Any(choice => ReferenceEquals(choice, originalChoice))) return null;
                var workbook = await ReadOriginalAsync(linked.Token).ConfigureAwait(false);
                if (workbook is null || !Current || !workbook.Tables.Single(table => table.Id == TableID).Records.Any(record => record.RecordID == originalChoice.RecordID)) return null;
                return JsonSerializer.SerializeToElement(new { tableID = TableID, recordID = originalChoice.RecordID.ToString("D") });
            }
            catch (IOException) { _issued = []; return null; }
            catch (UnauthorizedAccessException) { _issued = []; return null; }
            finally { _gate.Release(); }
        }
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) _retired.Cancel(); }
        private sealed class LookupResolver(Session session) : ICanonicalResourceAccessResolver
        {
            public const string Kind = "data.reference-table";
            public const string ActionID = "data.records.lookup";
            public string ResourceKind => Kind;
            public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
                ResourceScope requested, CancellationToken token)
            {
                var allowed = actionId == ActionID && requested.Access == ResourceAccess.Read && requested.Kind == Kind
                    && requested.Id == session.OriginalBinding.DataStoreID.ToString("D") + "/" + session.OriginalBinding.WorkbookID.ToString("D") + "/" + session.OriginalBinding.TableID.ToString("D")
                    && requested.Revision == session.OriginalBinding.DataRevisionID.ToString("D") && await ReadOriginalAsync(actor, token).ConfigureAwait(false) is not null;
                return new(allowed, allowed ? "Allowed" : "PermissionDenied", actor.ActorId, requested.Revision, actor.OrganisationId);
            }
            public async Task<DataWorkbook?> ReadOriginalAsync(AuthenticatedResourceActor actor, CancellationToken token)
            {
                if (actor != session.OriginalScope.Actor || !session.Current || await session.OwnerSource._profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor) return null;
                var receipt = session.OriginalReceipt;
                if (!session.Current || receipt?.Receipt is null || receipt.ProfileId != actor.ProfileId
                    || !await session.OwnerSource._ownership.IsCurrentAsync(receipt, actor, token).ConfigureAwait(false)) return null;
                var identity = await session.OwnerSource._data.GetStoreIdentityAsync(token).ConfigureAwait(false);
                if (!session.Current || identity.SchemaVersion != 1 || identity.StoreId != session.OriginalBinding.DataStoreID
                    || !await session.OwnerSource._ownership.IsCurrentAsync(receipt, actor, token).ConfigureAwait(false)) return null;
                var workbook = await session.OwnerSource._data.LoadAsync(session.OriginalBinding.WorkbookID, token).ConfigureAwait(false);
                if (!session.Current || workbook?.RevisionId != session.OriginalBinding.DataRevisionID || workbook.Version != session.OriginalBinding.DataVersion
                    || !await session.OwnerSource._ownership.IsCurrentAsync(receipt, actor, token).ConfigureAwait(false)) return null;
                var finalIdentity = await session.OwnerSource._data.GetStoreIdentityAsync(token).ConfigureAwait(false);
                if (!session.Current || finalIdentity.SchemaVersion != 1 || finalIdentity.StoreId != session.OriginalBinding.DataStoreID
                    || await session.OwnerSource._profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor) return null;
                // The final repository identity await cannot carry an earlier Home receipt check forward.
                if (!await session.OwnerSource._ownership.IsCurrentAsync(receipt, actor, token).ConfigureAwait(false)
                    || !session.Current || await session.OwnerSource._profiles.GetCurrentAsync(token).ConfigureAwait(false) != actor) return null;
                if (!await session.OwnerSource._ownership.IsCurrentAsync(session.OriginalFormsReceipt, actor, token).ConfigureAwait(false)
                    || !session.Current) return null;
                var table = workbook.Tables.SingleOrDefault(table => table.Id == session.OriginalBinding.TableID);
                if (table?.RecordIdentityVersion != 1 || !table.Fields.Any(field => field.FieldID == session.OriginalBinding.LabelFieldID)) return null;
                return workbook;
            }
        }
    }
}
