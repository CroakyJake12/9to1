using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private string _conversationSearchQuery = "";
    private long _conversationSearchQueryRevision;
    private bool _conversationSearchBusy, _showConversationSearch, _hasSearchedConversation;
    private Guid? _conversationSearchConversation, _conversationSearchBranch;
    private ConversationSearchRow[] _conversationSearchRows = [];
    private ChatMessage[] _searchedConversationHistory = [];
    private AssistantConversationBinding? _searchedConversationBinding;
    private string? _conversationSearchStatus;

    internal bool CanFindOriginalConversation => Current && !_unavailable && !_snapshot.IsRetiring &&
        _snapshot.ConversationBinding is not null && !Busy && !_streaming;
    internal string OriginalConversationSearchQuery => _conversationSearchQuery;
    internal long OriginalConversationSearchQueryRevision => _conversationSearchQueryRevision;
    internal void SetConversationSearchBusy(bool value) { _conversationSearchBusy = value; Refresh(); }

    private bool SetOriginalConversationSearchQuery(object? value)
    {
        if (!CanFindOriginalConversation || !_showConversationSearch || value is not string query || query.Length > 256) return false;
        if (_conversationSearchQuery != query)
        {
            _conversationSearchQuery = query;
            _conversationSearchQueryRevision = checked(_conversationSearchQueryRevision + 1);
            _conversationSearchRows = []; _conversationSearchStatus = null; _hasSearchedConversation = false;
        }
        RefreshConversationSearch(); return true;
    }

    internal void OpenOriginalConversationSearch()
    { if (CanFindOriginalConversation) { _showConversationSearch = true; RefreshConversationSearch(); } }

    internal void ClearOriginalConversationSearch()
    {
        if (!CanFindOriginalConversation) return;
        _showConversationSearch = false;
        _conversationSearchQuery = ""; _conversationSearchQueryRevision = checked(_conversationSearchQueryRevision + 1);
        _conversationSearchRows = []; _conversationSearchStatus = null; _hasSearchedConversation = false; RefreshConversationSearch();
    }

    internal void FindOriginalConversationText(AssistantConversationBinding actualBinding, long generation)
    {
        if (!Current || !ReferenceEquals(actualBinding, _snapshot.ConversationBinding) || _snapshot.Conversation is not { } data) return;
        var query = _conversationSearchQuery.Trim();
        if (query.Length == 0) return;
        var messages = _values.TryGetValue("Messages", out var published) && published is IReadOnlyList<AssistantMessagePresentation> rows
            ? rows : [];
        var found = new List<ConversationSearchRow>(); var count = 0;
        // ReadConversation provides the complete current branch history. The
        // native scroller's viewport never limits the text searched here.
        foreach (var actual in data.Messages)
        {
            var offset = actual.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (offset < 0) continue;
            var message = messages.SingleOrDefault(value => value.Id == actual.Id && value.Content == actual.Content);
            if (message is null) throw new InvalidOperationException("The current saved message has no matching original presentation.");
            count++;
            if (found.Count < 100) found.Add(new(actualBinding, generation, _conversationSearchQueryRevision, message, count, offset));
        }
        _searchedConversationBinding = actualBinding; _searchedConversationHistory = data.Messages.ToArray(); _hasSearchedConversation = true;
        _conversationSearchRows = found.ToArray();
        _conversationSearchStatus = count == 0 ? "No matching messages in this branch."
            : count > found.Count ? $"Showing the first {found.Count} of {count} matching messages. Refine your search for more specific results."
            : $"{count} matching message{(count == 1 ? "" : "s")} in this branch.";
        RefreshConversationSearch();
    }

    internal ConversationSearchRow? CurrentConversationSearchRow(object? value) => CanFindOriginalConversation && _showConversationSearch &&
        value is ConversationSearchRow row && row.QueryRevision == _conversationSearchQueryRevision &&
        ReferenceEquals(row.Binding, _snapshot.ConversationBinding) &&
        _conversationSearchRows.Any(actual => ReferenceEquals(actual, row)) && ContainsOriginalRow(row.Message, "Messages") &&
        _snapshot.Conversation?.Messages.Any(actual => actual.Id == row.Message.Id && actual.Content == row.Message.Content) == true
            ? row : null;

    private void RefreshConversationSearch()
    {
        var data = _snapshot.Conversation; var branch = AssistantDraftAttachmentProjection.CurrentBranch(data);
        if (_conversationSearchConversation != data?.Conversation.Id || _conversationSearchBranch != branch)
        {
            _conversationSearchConversation = data?.Conversation.Id; _conversationSearchBranch = branch;
            _conversationSearchQuery = ""; _conversationSearchQueryRevision = checked(_conversationSearchQueryRevision + 1);
            _conversationSearchRows = []; _conversationSearchStatus = null; _hasSearchedConversation = false; _showConversationSearch = false;
        }
        else if (_hasSearchedConversation &&
            (!ReferenceEquals(_searchedConversationBinding, _snapshot.ConversationBinding) ||
             data is null || !_searchedConversationHistory.SequenceEqual(data.Messages) ||
             _conversationSearchRows.Any(row => !ContainsOriginalRow(row.Message, "Messages"))))
        {
            _conversationSearchRows = []; _hasSearchedConversation = false;
            _conversationSearchStatus = "The conversation changed. Find again to use its current messages.";
        }
        Set("ShowConversationSearch", _showConversationSearch);
        Set("ConversationSearchQuery", _conversationSearchQuery);
        Set("CanFindConversation", CanFindOriginalConversation);
        Set("CanRunConversationSearch", CanFindOriginalConversation && _conversationSearchQuery.Trim().Length != 0);
        Set("ConversationSearchResults", _conversationSearchRows);
        Set("HasConversationSearchResults", _conversationSearchRows.Length != 0);
        Set("ConversationSearchStatus", _conversationSearchBusy ? "Reading this branch’s saved messages…"
            : _conversationSearchStatus ?? "Find text in this branch’s saved messages. Your draft stays unchanged.");
    }

    public sealed class ConversationSearchRow
    {
        internal ConversationSearchRow(AssistantConversationBinding binding, long generation, long queryRevision,
            AssistantMessagePresentation message, int position, int offset)
        {
            Binding = binding; Generation = generation; QueryRevision = queryRevision; Message = message;
            Label = $"Match {position} · {message.RoleLabel}";
            var start = Math.Max(0, offset - 40); var length = Math.Min(message.Content.Length - start, 220);
            Snippet = (start != 0 ? "…" : "") + message.Content.Substring(start, length) +
                (start + length < message.Content.Length ? "…" : "");
        }
        internal AssistantConversationBinding Binding { get; }
        internal long Generation { get; }
        internal long QueryRevision { get; }
        internal AssistantMessagePresentation Message { get; }
        public Guid Id => Message.Id;
        public string Label { get; }
        public string Snippet { get; }
    }
}
