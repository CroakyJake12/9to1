using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Actual browser-selected local bytes. The legacy path parameter is only
/// an opaque token issued by this SAME authenticated module, never a host filepath.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserMessageAttachmentService : IMessageAttachmentService
{
    private readonly BrowserConversationProductionStore _store;
    private readonly IndexedDbConversationProductionRepository _production;
    private readonly Dictionary<string, ImportOriginal> _imports = new(StringComparer.Ordinal);
    private sealed record ImportOriginal(Guid ConversationId, Guid? MessageId, Guid? BranchId, AttachmentProcessingOptions Options, Task<MessageAttachment> Task);
    private readonly object _gate = new();
    private bool _revoked;
    internal void RevokePrivateContext() { lock (_gate) _revoked = true; }
    internal async Task DrainOriginalImportsAsync()
    {
        Task[] actual;
        lock (_gate) { _revoked = true; actual = _imports.Values.Select(item => (Task)item.Task).ToArray(); }
        var all = Task.WhenAll(actual);
        try { await all; }
        catch
        {
            var failures = actual.Where(task => task.IsFaulted).SelectMany(task => task.Exception!.InnerExceptions)
                .Distinct(ReferenceEqualityComparer.Instance).Cast<Exception>().ToArray();
            if (failures.Length != 0) throw new AggregateException("Original attachment import sources failed.", failures);
            throw;
        }
    }
    internal BrowserMessageAttachmentService(BrowserConversationProductionStore store, IndexedDbConversationProductionRepository production)
    { _store = store; _production = production; }
    public async Task<string> SelectFileAsync(CancellationToken token = default)
    {
        lock (_gate) if (_revoked) throw new ObjectDisposedException(nameof(BrowserMessageAttachmentService));
        var selected = await _store.CallAsync("ConversationProduction.SelectAttachment", new { }, false, token);
        var path = selected.GetProperty("token").GetString();
        if (path is null || !path.StartsWith("browser-selection:", StringComparison.Ordinal)) throw new InvalidDataException("No original selected-file token was returned.");
        return path;
    }
    public Task<MessageAttachment> ImportAsync(Guid conversationId, Guid? messageId, Guid? branchId, string path, AttachmentProcessingOptions? options, CancellationToken token)
    {
        if (!path.StartsWith("browser-selection:", StringComparison.Ordinal)) throw new ArgumentException("Select a file using the actual browser picker first.", nameof(path));
        options ??= new(); ValidateOptions(options);
        TaskCompletionSource<MessageAttachment> completion; Task<MessageAttachment> actual;
        lock (_gate)
        {
            if (_revoked) throw new ObjectDisposedException(nameof(BrowserMessageAttachmentService));
            if (_imports.TryGetValue(path, out var issued))
            {
                if (issued.ConversationId != conversationId || issued.MessageId != messageId || issued.BranchId != branchId || issued.Options != options)
                    throw new UnauthorizedAccessException("The original selected-file import belongs to a different invocation.");
                return issued.Task; // Same original outcome; no replay after lost reply.
            }
            if (_imports.Count >= 128) throw new InvalidOperationException("Close this original attachment owner before importing more files.");
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = completion.Task; _imports.Add(path, new(conversationId, messageId, branchId, options, actual));
        }
        _ = CompleteOriginalAsync(completion, ImportOriginalAsync(conversationId, messageId, branchId, path, options ?? new(), token));
        return actual;
    }
    private static async Task CompleteOriginalAsync(TaskCompletionSource<MessageAttachment> completion, Task<MessageAttachment> original)
    {
        try { completion.TrySetResult(await original); }
        catch (Exception error)
        {
            if (original.IsCanceled) completion.TrySetCanceled();
            else completion.TrySetException(original.Exception is { } group ? group.InnerExceptions : new[] { error });
        }
    }
    private async Task<MessageAttachment> ImportOriginalAsync(Guid conversationId, Guid? messageId, Guid? branchId, string path, AttachmentProcessingOptions options, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        var pending = new MessageAttachment(id, conversationId, messageId, branchId, "Selected file", "browser-attachment:" + id.ToString("D"),
            "application/octet-stream", MessageAttachmentKind.Other, 0, "", AttachmentProcessingState.Pending, AttachmentAnalysisMethod.None, "",
            JsonSerializer.Serialize(new { schema = 1, processingOptions = options,
                recovery = "Original selected file import has not acknowledged both bytes and metadata." }), now, now);
        await _production.UpsertAttachmentAsync(pending, token); // Durable recovery marker precedes actual byte import.
        var receipt = await _store.CallAsync("ConversationProduction.ImportAttachment", new { id, conversationId, token = path,
            maximumBytes = Math.Min(50L * 1024 * 1024, Math.Max(options.MaxDocumentBytes, options.MaxImageBytes)) }, true, token);
        _store.DemandOriginalProfile(receipt);
        if (receipt.GetProperty("id").GetGuid() != id || receipt.GetProperty("conversationId").GetGuid() != conversationId)
            throw new InvalidDataException("The actual attachment receipt identifies another source.");
        return await RecoverAsync(pending, options, token);
    }
    public async Task<MessageAttachment> RecoverPendingAsync(Guid conversationId, Guid attachmentId, CancellationToken token = default)
    {
        var pending = (await _production.GetAttachmentsAsync(conversationId, null, token)).Single(item => item.Id == attachmentId);
        if (pending.ProcessingState != AttachmentProcessingState.Pending)
        {
            var bytes = await ReadBytes(pending.Id, token);
            if (bytes.GetProperty("conversationId").GetGuid() != conversationId || bytes.GetProperty("sha256").GetString() != pending.Sha256 || bytes.GetProperty("size").GetInt64() != pending.SizeBytes)
                throw new InvalidDataException("The acknowledged attachment differs from its original bytes.");
            return pending; // A lost final reply never reprocesses an already-acknowledged record with different policy.
        }
        using var metadata = JsonDocument.Parse(pending.MetadataJson);
        if (!metadata.RootElement.TryGetProperty("schema", out var schema) || schema.GetInt32() != 1 ||
            !metadata.RootElement.TryGetProperty("processingOptions", out var savedOptions))
            throw new InvalidDataException("The pending import has no original processing policy; an explicit recovery decision is required.");
        var options = savedOptions.Deserialize<AttachmentProcessingOptions>() ?? throw new InvalidDataException("Missing original attachment policy.");
        ValidateOptions(options);
        return await RecoverAsync(pending, options, token);
    }
    private async Task<MessageAttachment> RecoverAsync(MessageAttachment pending, AttachmentProcessingOptions options, CancellationToken token)
    {
        var value = await ReadBytes(pending.Id, token);
        if (value.GetProperty("conversationId").GetGuid() != pending.ConversationId) throw new InvalidDataException("The attachment conversation differs.");
        var name = value.GetProperty("name").GetString() ?? "Attachment";
        var media = value.GetProperty("mediaType").GetString() ?? "application/octet-stream";
        var bytes = Convert.FromBase64String(value.GetProperty("base64").GetString()!);
        var hash = value.GetProperty("sha256").GetString()!;
        var kind = ImageKind(bytes) ? MessageAttachmentKind.Image : IsText(name, media) ? MessageAttachmentKind.PlainText : MessageAttachmentKind.Other;
        if (bytes.LongLength > (kind == MessageAttachmentKind.Image ? options.MaxImageBytes : options.MaxDocumentBytes))
            throw new InvalidDataException("The selected attachment exceeds its configured kind limit; pending evidence was preserved.");
        var text = ""; var state = AttachmentProcessingState.Ready; var method = AttachmentAnalysisMethod.None; string? notice = null;
        if (kind == MessageAttachmentKind.PlainText)
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes); method = AttachmentAnalysisMethod.TextExtracted; }
            catch (DecoderFallbackException) { state = AttachmentProcessingState.Unsupported; notice = "This file is not valid UTF-8 text; no extracted text was invented."; }
            if (text.Length > options.MaxExtractedCharacters) { text = text[..Math.Max(0, options.MaxExtractedCharacters)]; notice = "Extracted text was truncated to the configured character limit."; }
        }
        else if (kind == MessageAttachmentKind.Other) { state = AttachmentProcessingState.Unsupported; notice = "This host has no parser for this file type. Original bytes are retained; no analysis was simulated."; }
        var ready = pending with { OriginalName = name, MediaType = media, Kind = kind, SizeBytes = bytes.LongLength, Sha256 = hash,
            ProcessingState = state, AnalysisMethod = method, ExtractedText = text,
            MetadataJson = JsonSerializer.Serialize(new { schema = 1, processingOptions = options, notice, source = "Actual browser file selection", synchronized = false }), UpdatedAt = DateTimeOffset.UtcNow };
        return await _production.UpsertAttachmentAsync(ready, token);
    }
    public async Task<AttachmentPromptContext> BuildPromptContextAsync(Guid conversationId, IReadOnlyCollection<Guid>? attachmentIds, AttachmentProcessingOptions? options, CancellationToken token)
    {
        options ??= new(); var all = await _production.GetAttachmentsAsync(conversationId, null, token);
        var chosen = attachmentIds is null ? all : all.Where(item => attachmentIds.Contains(item.Id)).ToArray();
        if (attachmentIds is not null && attachmentIds.Distinct().Count() != chosen.Count) throw new UnauthorizedAccessException("An attachment is unavailable in this original conversation.");
        var images = new List<string>(); var notices = new List<string>(); var text = new StringBuilder();
        foreach (var attachment in chosen)
        {
            if (attachment.ProcessingState == AttachmentProcessingState.Pending) throw new InvalidOperationException("An attachment import needs original-byte recovery before sending.");
            if (attachment.ProcessingState != AttachmentProcessingState.Ready) { notices.Add(attachment.OriginalName + ": unsupported on this host; no content was sent."); continue; }
            var value = await ReadBytes(attachment.Id, token);
            if (value.GetProperty("conversationId").GetGuid() != conversationId || value.GetProperty("sha256").GetString() != attachment.Sha256 || value.GetProperty("size").GetInt64() != attachment.SizeBytes)
                throw new InvalidDataException("The stored attachment differs from its acknowledged metadata.");
            if (attachment.Kind == MessageAttachmentKind.Image) images.Add(value.GetProperty("base64").GetString()!);
            else if (!string.IsNullOrEmpty(attachment.ExtractedText))
            {
                var remaining = Math.Max(0, options.MaxExtractedCharacters - text.Length);
                text.AppendLine("Source file: " + attachment.OriginalName); text.AppendLine(attachment.ExtractedText[..Math.Min(remaining, attachment.ExtractedText.Length)]);
                if (remaining < attachment.ExtractedText.Length) notices.Add(attachment.OriginalName + ": text truncated to prompt limit.");
            }
        }
        return new(images, text.ToString(), notices, chosen);
    }
    private async Task<JsonElement> ReadBytes(Guid id, CancellationToken token)
    {
        var value = await _store.CallAsync("ConversationProduction.ReadAttachment", new { id }, false, token);
        if (value.ValueKind != JsonValueKind.Object || value.GetProperty("id").GetGuid() != id) throw new InvalidDataException("Original attachment bytes are unavailable; metadata was preserved for recovery.");
        _store.DemandOriginalProfile(value);
        var bytes = Convert.FromBase64String(value.GetProperty("base64").GetString()!);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        if (hash != value.GetProperty("sha256").GetString() || bytes.LongLength != value.GetProperty("size").GetInt64()) throw new InvalidDataException("Original attachment integrity check failed.");
        return value;
    }
    public async Task DeleteAsync(Guid attachmentId, CancellationToken token)
    {
        // Metadata disappears only after actual byte deletion acknowledges. Unknown
        // outcomes remain visible as missing-byte recovery, never false successful delete.
        await _store.CallAsync("ConversationProduction.DeleteAttachment", new { id = attachmentId }, true, token);
        await _production.DeleteAttachmentAsync(attachmentId, token);
    }
    private static void ValidateOptions(AttachmentProcessingOptions options)
    {
        if (options.MaxDocumentBytes < 1 || options.MaxImageBytes < 1 || options.MaxExtractedCharacters is < 0 or > 500_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Original attachment processing limits are invalid.");
    }
    private static bool IsText(string name, string media) => media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".txt", ".md", ".json", ".csv", ".cs", ".js", ".ts", ".py", ".xml", ".html", ".css", ".yaml", ".yml" }.Contains(Path.GetExtension(name).ToLowerInvariant());
    private static bool ImageKind(byte[] bytes) => bytes.Length > 12 && (bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
        bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 || Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a" ||
        Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP");
}
