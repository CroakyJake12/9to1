using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>An exact prepared Notes action; preparation grants no write authority.</summary>
internal sealed class WriteNotesWriteIntent
{
    private readonly byte[] _original, _candidate;
    private readonly JsonElement _arguments;
    private WriteNotesWriteIntent(HostedItemId fileId, FilesRevisionId filesRevision, Guid owningRevision,
        NotesDocument document, HomeProductivityAction action)
    {
        FileId = fileId; FilesRevision = filesRevision; OwningRevision = owningRevision; DocumentId = document.Id;
        Action = action with { ObjectIds = Array.AsReadOnly(action.ObjectIds.ToArray()), Arguments = action.Arguments.Clone() };
        _original = JsonSerializer.SerializeToUtf8Bytes(document);
        var candidate = Read(_original);
        var targets = candidate.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Where(block => Action.ObjectIds.Contains(block.Id)).ToArray();
        if (targets.Length != Action.ObjectIds.Count || targets.Select(block => block.Id).Distinct().Count() != targets.Length)
            throw new InvalidDataException("The canonical Write targets are missing or ambiguous.");
        var handler = new HomeNotesObjectHandler(Action.ObjectType);
        foreach (var page in candidate.Sections.SelectMany(section => section.Pages))
            for (var index = 0; index < page.Blocks.Count; index++)
            {
                var block = page.Blocks[index];
                if (!Action.ObjectIds.Contains(block.Id)) continue;
                var source = HomeNotesSharedObjects.Project(block);
                if (source.ObjectType != Action.ObjectType) throw new InvalidDataException("The action targets another Notes family.");
                page.Blocks[index] = HomeNotesSharedObjects.Read(handler.Transform(source, Action));
            }
        _candidate = JsonSerializer.SerializeToUtf8Bytes(candidate);
        _arguments = JsonSerializer.SerializeToElement(new
        {
            fileId = fileId.Value, filesRevision = filesRevision.Value, owningRevision, documentId = DocumentId,
            action = Action, originalHash = Hash(_original), candidateHash = Hash(_candidate)
        });
        Scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", fileId.ToString(), filesRevision.ToString(), ResourceAccess.Write) });
    }
    public const string AppId = "write";
    public const string ActionId = "write.file.save";
    public HostedItemId FileId { get; }
    public FilesRevisionId FilesRevision { get; }
    public Guid OwningRevision { get; }
    public Guid DocumentId { get; }
    public HomeProductivityAction Action { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => _arguments.Clone();
    internal NotesDocument Original() => Read(_original);
    internal NotesDocument Candidate() => Read(_candidate);
    internal bool MatchesOriginal(NotesDocument document) =>
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(document), JsonSerializer.SerializeToElement(Original()));

    public static WriteNotesWriteIntent Capture(HostedItemId fileId, FilesRevisionId filesRevision, Guid owningRevision,
        NotesDocument document, HomeProductivityAction action)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(action);
        if (fileId.Value == Guid.Empty || filesRevision.Value == Guid.Empty || owningRevision == Guid.Empty || document.Id == Guid.Empty ||
            action.Version != 1 || action.CanonicalExpectedRevision != new HomeProductivityArtifactRevision(VersionId: owningRevision) ||
            action.ObjectIds.Count == 0 || action.ObjectIds.Any(id => id == Guid.Empty) || action.ObjectIds.Distinct().Count() != action.ObjectIds.Count)
            throw new ArgumentException("Write actions require exact canonical identities, owning revision and unique targets.");
        // Capture caller-owned graphs before the first asynchronous operation.
        return new(fileId, filesRevision, owningRevision, Read(JsonSerializer.SerializeToUtf8Bytes(document)), action);
    }
    private static NotesDocument Read(byte[] bytes) => JsonSerializer.Deserialize<NotesDocument>(bytes)
        ?? throw new InvalidDataException("The captured Write document is unavailable.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed class WriteNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == WriteNotesWriteIntent.AppId && actionId == WriteNotesWriteIntent.ActionId
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}

internal sealed record WriteNotesCommitResult(FilesRevision Revision, bool AuditRecorded);

internal sealed class WriteHomeNotesOperation(WriteFilesArtifactBridge files, NativeFilesArtifactContentReader reader,
    HomeResourceOperationBroker home)
{
    public async Task<WriteNotesCommitResult> ExecuteAsync(WriteNotesWriteIntent intent, HomeResourceExecutionCapability capability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capability);
        if (!files.HasOwnershipReceiptCommitGuard)
            throw new UnauthorizedAccessException("Home Write actions require the canonical Files ownership receipt commit guard.");
        var current = await reader.ReadAsync("write", intent.FileId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (current.Metadata.CurrentRevisionId != intent.FilesRevision ||
            !Guid.TryParse(current.Revision.OwningAppRevisionId, out var owningRevision) || owningRevision != intent.OwningRevision)
            throw new InvalidOperationException("The canonical Write revision changed before execution.");
        var opened = await files.OpenAsync(intent.FileId, cancellationToken).ConfigureAwait(false);
        if (opened.Id != intent.DocumentId || !intent.MatchesOriginal(opened))
            throw new InvalidDataException("The approved Write source does not match the canonical package.");
        var claimed = await home.ClaimExecutionAsync(capability, WriteNotesWriteIntent.AppId, WriteNotesWriteIntent.ActionId,
            intent.Scopes, intent.Arguments, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home did not grant this exact Write action.");
        FilesRevision committed;
        try
        {
            committed = await files.SaveForActorAsync(claimed, intent.FileId, intent.Candidate(), intent.FilesRevision, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RecordOutcomeAsync(capability, new(HomePermissionRequestState.PartiallyCompleted,
                "WRITE_OUTCOME_UNCONFIRMED", "The Write save did not return a confirmed outcome. Reload its canonical revision before another edit.", [])).ConfigureAwait(false);
            throw;
        }
        var audited = await RecordOutcomeAsync(capability, new(HomePermissionRequestState.Succeeded,
            "WRITE_COMMITTED", "The canonical Write action was saved.", [new("files.item", intent.FileId.ToString())])).ConfigureAwait(false);
        return new(committed, audited);
    }

    private async Task<bool> RecordOutcomeAsync(HomeResourceExecutionCapability capability, HomeExecutionOutcome outcome)
    {
        try { return (await home.CompleteExecutionAsync(capability, outcome, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException)
        { return false; } // An audit failure must never repeat or obscure an acknowledged durable mutation.
    }
}

/// <summary>One approved shared action, bound to its captured canonical Notes graph and issuer-sealed capability.</summary>
internal sealed class WriteHomeNotesActionProvider(WriteHomeNotesOperation owner, WriteNotesWriteIntent intent,
    HomeResourceExecutionCapability capability) : IHomeProductivityArtifactActionProvider
{
    public string AppId => WriteNotesWriteIntent.AppId;
    public async ValueTask<HomeProductivityActionResult> ApplyAsync(HomeProductivityContext context, HomeProductivityAction action,
        Func<HomeProductivityObject, HomeProductivityObject> sharedTransformation, CancellationToken cancellationToken)
    {
        if (context.AppId != AppId || !Guid.TryParse(context.ArtifactId, out var documentId) || documentId != intent.DocumentId ||
            context.CanonicalRevision != new HomeProductivityArtifactRevision(VersionId: intent.OwningRevision) ||
            !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(action), JsonSerializer.SerializeToElement(intent.Action)))
            return Reject(context, "ActionBindingMismatch");
        var original = intent.Original(); var candidate = intent.Candidate();
        var approved = candidate.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Where(block => action.ObjectIds.Contains(block.Id)).ToDictionary(block => block.Id);
        foreach (var block in original.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                     .Where(block => action.ObjectIds.Contains(block.Id)))
        {
            var transformed = sharedTransformation(HomeNotesSharedObjects.Project(block));
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(transformed),
                    JsonSerializer.SerializeToElement(HomeNotesSharedObjects.Project(approved[block.Id]))))
                return Reject(context, "TransformationBindingMismatch");
        }
        try
        {
            var committed = await owner.ExecuteAsync(intent, capability, cancellationToken).ConfigureAwait(false);
            if (!Guid.TryParse(committed.Revision.OwningAppRevisionId, out var revision))
                return new(false, "WriteNeedsRecovery", "Reload the acknowledged Write revision.", context.Revision, action.ObjectIds)
                    { Outcome = HomeProductivityArtifactOutcome.NeedsRecovery };
            return new(true, committed.AuditRecorded ? "Committed" : "CommittedAuditPending",
                committed.AuditRecorded ? "The canonical Write action was saved." : "Write saved; Home audit is pending. Do not repeat the save.", context.Revision, action.ObjectIds)
                { ArtifactRevision = new(VersionId: revision), Outcome = HomeProductivityArtifactOutcome.Committed };
        }
        catch (UnauthorizedAccessException) { return Reject(context, "PermissionDenied"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
        {
            return new(false, "WriteNeedsRecovery", "Reload the canonical Write revision before retrying.", context.Revision, [])
                { Outcome = HomeProductivityArtifactOutcome.NeedsRecovery };
        }
    }
    private static HomeProductivityActionResult Reject(HomeProductivityContext context, string code) =>
        new(false, code, "The approved Write action could not be applied.", context.Revision, [])
            { ArtifactRevision = context.CanonicalRevision, Outcome = HomeProductivityArtifactOutcome.Rejected };
}
