using System.Text.Json;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private bool _hasAttachmentPicker, _attachmentBusy;
    private IReadOnlyList<AttachmentRow> _attachmentRows = [];
    private string? _attachmentStatus;
    internal void ConfigureOriginalAttachmentPicker() { _hasAttachmentPicker = true; Refresh(); }
    internal void SetAttachmentBusy(bool value) { _attachmentBusy = value; Refresh(); }
    internal void SetAttachmentStatus(string value) { _attachmentStatus = value; Refresh(); }
    private bool CanChangeAttachments => Current && !_unavailable && !_snapshot.IsRetiring &&
        _snapshot.ConversationBinding is not null && !_streaming && !Busy;
    private void RefreshAttachments()
    {
        var data = _snapshot.Conversation;
        var valid = AssistantDraftAttachmentProjection.TryRead(data, out var selected);
        _attachmentRows = valid ? selected.Select(item => new AttachmentRow(item)).ToArray() : [];
        Set("DraftAttachments", _attachmentRows);
        Set("HasDraftAttachments", _attachmentRows.Count != 0);
        Set("CanPickAttachment", CanChangeAttachments && _hasAttachmentPicker && valid);
        Set("CanDetachAttachment", CanChangeAttachments && valid);
        Set("AttachmentStatus", !valid ? "This saved attachment list needs attention before it can be changed." :
            _attachmentStatus ?? (_hasAttachmentPicker ? "Choose a text, DOCX, PPTX or XLSX file already saved in Files. Review access in Home before it is attached. Attached documents currently require a local model." :
            "File selection is not available in this window."));
    }
    internal AttachmentRow? CurrentAttachmentRow(object? item) => item is AttachmentRow row && Current &&
        _attachmentRows.Any(actual => ReferenceEquals(actual, row)) &&
        _snapshot.Conversation?.Attachments.Any(actual => ReferenceEquals(actual, row.Original)) == true ? row : null;
    private bool TryGetAttachmentItemValue(object item, string path, out object? value)
    {
        value = null;
        if (CurrentAttachmentRow(item) is not { } row) return false;
        value = path switch { "Id" => row.Id, "Name" => row.Name, "Details" => row.Details, "Preview" => row.Preview, _ => null };
        return path is "Id" or "Name" or "Details" or "Preview";
    }
    public sealed class AttachmentRow
    {
        internal AttachmentRow(MessageAttachment actual) => Original = actual;
        internal MessageAttachment Original { get; }
        public string Id => Original.Id.ToString("D");
        public string Name => Original.OriginalName;
        public string Details => $"{Original.SizeBytes:N0} bytes · {Original.MediaType}";
        public string Preview => Original.ExtractedText is { } text ? text[..Math.Min(text.Length, 1200)] : "";
    }
}

internal static class AssistantDraftAttachmentProjection
{
    internal static bool TryRead(AssistantConversationData? data, out IReadOnlyList<MessageAttachment> selected)
    {
        selected = [];
        if (data?.Draft is not { } draft) return true;
        if (draft.ConversationId != data.Conversation.Id) return false;
        Guid[]? ids;
        try { ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson); }
        catch (JsonException) { return false; }
        if (ids is null || ids.Length > 64 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return false;
        var result = new List<MessageAttachment>();
        foreach (var id in ids)
        {
            var matches = data.Attachments.Where(row => row.Id == id && row.ConversationId == draft.ConversationId &&
                row.BranchId == draft.BranchId && row.MessageId is null).ToArray();
            if (matches.Length != 1) return false;
            result.Add(matches[0]);
        }
        selected = result.AsReadOnly(); return true;
    }
    internal static IReadOnlyList<Guid> DemandIds(AssistantConversationData? data)
    {
        if (!TryRead(data, out var selected)) throw new InvalidDataException("The current saved draft attachment list is inconsistent.");
        return selected.Select(row => row.Id).ToArray();
    }
    internal static Guid? CurrentBranch(AssistantConversationData? data)
    {
        if (data?.Draft is { } draft) return draft.BranchId;
        var branches = data?.Branches.Where(branch => branch.IsCurrent).ToArray() ?? [];
        if (branches.Length > 1) throw new InvalidDataException("The current conversation branch is ambiguous.");
        return branches.SingleOrDefault()?.Id;
    }
}
