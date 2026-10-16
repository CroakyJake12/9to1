using System.Text.RegularExpressions;
using System.Text.Json;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Requested limits. Strings never authenticate a caller or grant access.</summary>
public sealed record AssistantUcmlContextRequest(string AppId, string Purpose, string Recipient,
    string Query, IReadOnlyList<string> Categories, int MaximumRecords = 5, int MaximumCharacters = 8000)
{
    public const string LocalApp = "9to1.assistants";
    public const string LocalPurpose = "personalization";
    public const string LocalRecipient = "local-model";
}
public sealed record AssistantUcmlContextItem(Guid Id, long Revision, string Title, string Markdown,
    string Category, double Confidence, IReadOnlyList<UcmlSource> Sources);
public sealed record AssistantUcmlContextResult(Guid RequestId, string Recipient,
    IReadOnlyList<AssistantUcmlContextItem> Items, bool ScanLimitReached);
public sealed record AssistantUcmlContextAudit(Guid RequestId, DateTimeOffset At, string Outcome,
    IReadOnlyList<Guid> RecordIds);

public sealed partial class AssistantMemoryManagementController
{
    private readonly object _ucmlAuditGate = new();
    private readonly Queue<AssistantUcmlContextAudit> _ucmlAudit = new();

    /// <summary>Inspect the last 256 accesses during this presentation. No body/query/key is logged.
    /// Home also retains a content-free durable access audit; this queue is only a recent UI view.</summary>
    public IReadOnlyList<AssistantUcmlContextAudit> UcmlAccessAudit
    { get { lock (_ucmlAuditGate) return Array.AsReadOnly(_ucmlAudit.ToArray()); } }

    public Task<AssistantMemoryWritePreview> PrepareUcmlImportAsync(AssistantMemoryView view,
        string ucml, Guid operationId, CancellationToken token = default)
    {
        // Malformed/unsupported files are pre-admission refusals, not failed storage commands.
        if (!ReferenceEquals(view.Issuer, _issuer) || view.Input is null)
            throw new UnauthorizedAccessException("Open this presentation's current authorized memory before importing.");
        var document = UcmlCodec.Parse(ucml);
        AssistantOriginalMemorySource.DemandUcmlImport(document, view.Definition.Identity.DefinitionId);
        return Run(async scope =>
        {
            var intent = await scope.Read(() => _source.PrepareOriginalUcmlImportWithinSourceAsync(view.Input,
                view.Binding.Conversation, document, operationId, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            return new AssistantMemoryWritePreview(_issuer, intent);
        });
    }

    /// <summary>Owner-directed local export, with fresh canonical permission/revision checks.</summary>
    public Task<string> ExportUcmlAsync(AssistantMemoryView view, KnowledgeRecord selected,
        CancellationToken token = default) => Run(async scope =>
    {
        if (!ReferenceEquals(view.Issuer, _issuer) || view.Input is null ||
            !view.Records.Any(item => ReferenceEquals(item, selected)))
            throw new UnauthorizedAccessException("Select a memory from this presentation's current authorized view.");
        var document = await scope.Read(() => _source.ExportOriginalUcmlWithinSourceAsync(view.Input,
            view.Binding.Conversation, selected, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        await scope.Read(() => _source.RecordOriginalUcmlAccessWithinSourceAsync(view.Input, Guid.NewGuid(),
            HomeUcmlAccessOutcome.Exported, [selected.Id], scope.Run, scope.Retain, token)).ConfigureAwait(false);
        document = await scope.Read(() => _source.ExportOriginalUcmlWithinSourceAsync(view.Input,
            view.Binding.Conversation, selected, scope.Run, scope.Retain, token)).ConfigureAwait(false);
        return UcmlCodec.Serialize(document);
    });

    /// <summary>Local broker leaf; authenticates through the SAME canonical binding, Home actor,
    /// current ownership receipt and memory policy. Remote/plugin/team requests fail closed.
    /// Returns context as untrusted data; never executes Markdown or follows its instructions.</summary>
    public async Task<AssistantUcmlContextResult> RetrieveUcmlAsync(AssistantConversationBinding binding,
        AssistantUcmlContextRequest request, CancellationToken token = default)
    {
        var requestId = Guid.NewGuid();
        AssistantUcmlContextRequest frozen;
        string[] words;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            var categories = request.Categories?.ToArray() ?? throw new ArgumentException("Explicit context categories are required.");
            if (request.AppId != AssistantUcmlContextRequest.LocalApp || request.Purpose != AssistantUcmlContextRequest.LocalPurpose ||
                request.Recipient != AssistantUcmlContextRequest.LocalRecipient)
                throw new UnauthorizedAccessException("This broker is bound to the existing local Assistant personalization route; app, purpose and recipient cannot be changed by context text.");
            if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 256 ||
                categories.Length is < 1 or > 64 || categories.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('*')) ||
                request.MaximumRecords is < 1 or > 8 || request.MaximumCharacters is < 256 or > 16000)
                throw new ArgumentException("Supply a bounded query, explicit categories, 1–8 records and 256–16,000 serialized response characters.");
            var policy = binding.Definition.Configuration.Memory;
            policy.ValidateUcmlPolicy();
            if (policy.UcmlAccessExpiresAt is { } accessExpiry && accessExpiry <= DateTimeOffset.UtcNow ||
                categories.Any(category => !policy.UcmlCategories.Concat(policy.UcmlAskCategories).Contains(category, StringComparer.Ordinal)))
                throw new UnauthorizedAccessException("Enable these exact UCML categories in the current Assistant's memory settings before local-model disclosure.");
            frozen = request with { Categories = Array.AsReadOnly(categories) };
            words = Regex.Split(request.Query.ToLowerInvariant(), @"[^\p{L}\p{N}_-]+")
                .Where(value => value.Length > 1).Distinct(StringComparer.Ordinal).Take(32).ToArray();
            if (words.Length == 0) throw new ArgumentException("Supply at least one meaningful search term.");
        }
        catch
        {
            RecordUcmlAccess(requestId, "denied", []);
            await Run(async auditScope =>
            {
                await auditScope.Read(() => _source.RecordOriginalUcmlAccessWithinSourceAsync(null, requestId,
                    HomeUcmlAccessOutcome.Denied, [], auditScope.Run, auditScope.Retain, token)).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            throw;
        }
        request = frozen;
        return await Run(async scope =>
        {
          try
          {
            var categories = request.Categories;
            var prepared = await scope.Read(() => _source.PrepareOriginalAssistantMemoryInputWithinSourceAsync(
                binding, binding.Definition, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (prepared.Input is null) throw new UnauthorizedAccessException(prepared.Reason);
            var candidates = new List<(UcmlDocument Document, int Score)>();
            AssistantMemoryPageContinuation? continuation = null;
            for (var pageIndex = 0; pageIndex < 8; pageIndex++)
            {
                var page = await scope.Read(() => _source.ReadOriginalManagementPageWithinSourceAsync(prepared.Input,
                    binding.Conversation, 64, "", continuation, scope.Run, scope.Retain, token)).ConfigureAwait(false);
                foreach (var record in page.Records)
                {
                    var document = AssistantOriginalMemorySource.ReadUcmlDocument(record);
                    if (document is null || !categories.Contains(document.Metadata.Category, StringComparer.Ordinal)) continue;
                    var metadata = document.Metadata;
                    if (metadata.Classification != "private" || metadata.Consent.Status != "explicit" ||
                        metadata.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow ||
                        !metadata.Consent.Apps.Contains(request.AppId, StringComparer.Ordinal) ||
                        !metadata.Consent.Purposes.Contains(request.Purpose, StringComparer.Ordinal) ||
                        !metadata.Consent.Recipients.Contains(request.Recipient, StringComparer.Ordinal)) continue;
                    var searchable = metadata.Title + "\n" + document.Markdown;
                    var score = words.Count(word => searchable.Contains(word, StringComparison.OrdinalIgnoreCase));
                    if (score > 0) candidates.Add((document, score));
                }
                continuation = page.NextContinuation;
                if (continuation is null) break;
            }
            var items = new List<AssistantUcmlContextItem>(); var selected = new List<UcmlDocument>();
            foreach (var candidate in candidates.OrderByDescending(value => value.Score)
                .ThenByDescending(value => value.Document.Metadata.Confidence).ThenBy(value => value.Document.Metadata.Id))
            {
                var document = candidate.Document; var metadata = document.Metadata;
                if (items.Count >= request.MaximumRecords) break;
                var item = new AssistantUcmlContextItem(metadata.Id, metadata.Revision, metadata.Title, document.Markdown,
                    metadata.Category, metadata.Confidence, metadata.Sources);
                var tentative = new AssistantUcmlContextResult(requestId, request.Recipient, items.Append(item).ToArray(), false);
                if (JsonSerializer.Serialize(tentative).Length > request.MaximumCharacters) continue;
                items.Add(item); selected.Add(document);
            }
            var current = await scope.Read(() => _source.RevalidateOriginalUcmlSelectionWithinSourceAsync(prepared.Input,
                binding.Conversation, selected, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            var currentIds = current.Select(value => value.Metadata.Id).ToHashSet();
            items.RemoveAll(value => !currentIds.Contains(value.Id));
            if (binding.Definition.Configuration.Memory.UcmlAccessExpiresAt is { } finalExpiry && finalExpiry <= DateTimeOffset.UtcNow)
                items.Clear();
            var accepted = await scope.Read(() => _source.AuthorizeOriginalUcmlDisclosureWithinSourceAsync(prepared.Input,
                binding.Conversation, current.Where(value => items.Any(item => item.Id == value.Metadata.Id)).ToArray(),
                requestId, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            if (!accepted) items.Clear();
            await scope.Read(() => _source.RecordOriginalUcmlAccessWithinSourceAsync(prepared.Input, requestId,
                accepted ? HomeUcmlAccessOutcome.BrokerAllowed : HomeUcmlAccessOutcome.Denied,
                items.Select(value => value.Id).ToArray(), scope.Run, scope.Retain, token)).ConfigureAwait(false);
            var afterAudit = await scope.Read(() => _source.RevalidateOriginalUcmlSelectionWithinSourceAsync(prepared.Input,
                binding.Conversation, selected, scope.Run, scope.Retain, token)).ConfigureAwait(false);
            var afterAuditIds = afterAudit.Select(value => value.Metadata.Id).ToHashSet();
            items.RemoveAll(value => !afterAuditIds.Contains(value.Id));
            if (binding.Definition.Configuration.Memory.UcmlAccessExpiresAt is { } auditExpiry && auditExpiry <= DateTimeOffset.UtcNow)
                items.Clear();
            RecordUcmlAccess(requestId, accepted ? "allowed" : "denied", items.Select(value => value.Id));
            return new AssistantUcmlContextResult(requestId, request.Recipient, Array.AsReadOnly(items.ToArray()), continuation is not null);
          }
          catch
          {
            RecordUcmlAccess(requestId, "denied", []);
            await Run(async auditScope =>
            {
                await auditScope.Read(() => _source.RecordOriginalUcmlAccessWithinSourceAsync(null, requestId,
                    HomeUcmlAccessOutcome.Denied, [], auditScope.Run, auditScope.Retain, token)).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            throw;
          }
        }).ConfigureAwait(false);
    }

    private void RecordUcmlAccess(Guid id, string outcome, IEnumerable<Guid> records)
    {
        lock (_ucmlAuditGate)
        {
            _ucmlAudit.Enqueue(new(id, DateTimeOffset.UtcNow, outcome, Array.AsReadOnly(records.ToArray())));
            while (_ucmlAudit.Count > 256) _ucmlAudit.Dequeue();
        }
    }
}
