using CakeOS.Cui;
using HavenOS.Home.Core;

namespace HavenOS.Home;

public sealed partial class HomeModelPickerCuiSurface
{
    private HomeModelPickerConversationSnapshot? _conversation;
    private string _conversationMessage = "Open this picker from a Chat to choose its Specialist.";
    private string _specialistQuery = "";
    private bool _conversationBusy;

    public bool SetSpecialistQuery(string? query)
    {
        if (CompactRole != MainRole || _conversation is null) return false;
        _specialistQuery = query ?? "";
        NotifyChanged();
        return true;
    }

    internal void SetConversationBusy(bool busy)
    {
        _conversationBusy = busy;
        NotifyChanged();
    }

    internal void ApplyConversation(HomeCoreOperationResult<HomeModelPickerConversationSnapshot> result)
    {
        // An unavailable/stale read must never leave the old rows actionable. The user's
        // presentation-only query survives refresh, role changes and compact/settings switches.
        _conversation = result.Succeeded && result.Value is { } snapshot
            ? snapshot with { Specialists = Array.AsReadOnly(snapshot.Specialists.ToArray()) } : null;
        _conversationMessage = result.Succeeded ? "" : result.Message;
        NotifyChanged();
    }

    private bool CanChooseSpecialist => CompactRole == MainRole && !_conversationBusy &&
        _conversation is { IsSafetyLocked: false };

    private IReadOnlyList<HomeModelPickerSpecialistRow> SpecialistRows => _conversation is { } conversation && CompactRole == MainRole
        ? conversation.Specialists.Where(item => string.IsNullOrWhiteSpace(_specialistQuery) ||
                item.Name.Contains(_specialistQuery, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(_specialistQuery, StringComparison.OrdinalIgnoreCase))
            .Select(item => new HomeModelPickerSpecialistRow(item, conversation)).ToArray() : [];

    private HomeModelPickerCatalogueEntry? ConversationModel
    {
        get
        {
            if (_conversation?.SelectedModelName is not { } selected || CompactRole != MainRole) return null;
            // Ollama names can themselves contain a colon. Match the real provider key;
            // never infer a provider by splitting an arbitrary model label.
            var matches = Catalogue.Where(entry => string.Equals(selected,
                string.Equals(entry.ProviderId, "ollama", StringComparison.OrdinalIgnoreCase)
                    ? entry.ModelId : entry.ProviderId + ":" + entry.ModelId, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
    }

    private string ConversationContext => ConversationModel is { ContextWindow: > 0 } model
        ? string.Equals(model.ProviderId, "dulche-local", StringComparison.Ordinal)
            ? $"Configured context: {model.ContextWindow.Value:N0} tokens."
            : $"Selected Chat model capacity: {model.ContextWindow.Value:N0} tokens."
        : _conversation is null ? "Conversation usage is unavailable outside the originating Chat."
        : "The selected Chat model has no current context observation in this catalogue.";

    private string ContextChangeMessage => ConversationModel is { ProviderId: "dulche-local" }
        ? "Context is pinned to Dulche's configured profile here. Changing it needs a guarded runtime transaction; this picker cannot reload it. Conversation usage is not reported here."
        : "Context changes and conversation usage are not available from this model's current owner.";

    private bool TryGetConversationValue(string path, out object? value)
    {
        value = path switch
        {
            "CompactParticipantName" => CompactRole != MainRole ? "Unavailable for this role"
                : _conversation?.SelectedSpecialistName ?? (_conversation is null ? "No Chat connection" : "None"),
            "CompactParticipantMessage" => CompactRole != MainRole
                ? "This Chat has no separate participant owner for this role."
                : _conversationBusy ? "Reading the current Chat and Specialist catalogue…"
                : _conversation is { IsSafetyLocked: true } ? "This Chat is safety locked. Specialist changes are unavailable."
                : !string.IsNullOrWhiteSpace(_conversationMessage) ? _conversationMessage
                : _conversation is { SelectedSpecialistId: { } selected } observation &&
                    !observation.Specialists.Any(item => item.Id == selected && item.Version == observation.SelectedSpecialistVersion)
                    ? "The selected Specialist is no longer available in this catalogue. Choose a current definition or None."
                : "Select a Specialist for future messages in this Chat. Assistant participation is unavailable here.",
            "HasCompactConversation" => CompactRole == MainRole && _conversation is not null,
            "CanChooseCompactSpecialist" => CanChooseSpecialist,
            "CompactSpecialistQuery" => _specialistQuery,
            "CompactSpecialists" => SpecialistRows,
            "HasCompactSpecialists" => SpecialistRows.Count != 0,
            "CompactSpecialistsEmpty" => SpecialistRows.Count == 0,
            "CompactSpecialistEmptyMessage" => _conversation is { Specialists.Count: 0 }
                ? "No enabled Specialists are available in this profile." : "No Specialists match this search.",
            "CompactSpecialistNoneSelection" => _conversation is { } current
                ? new HomeModelPickerSpecialistSelection(current.ConversationId, current.ModelSelectionRevision,
                    current.SelectedSpecialistId, current.SelectedSpecialistVersion, null, null) : null,
            "CompactSpecialistNoneAccent" => _conversation is { SelectedSpecialistId: null } ? "Secondary" : "None",
            "CompactConversationContext" => CompactRole == MainRole ? ConversationContext : "This role has no conversation context owner.",
            "CompactContextChangeMessage" => CompactRole == MainRole ? ContextChangeMessage
                : "Context changes for this role are unavailable.",
            _ => null,
        };
        return path is "CompactParticipantName" or "CompactParticipantMessage" or "HasCompactConversation" or
            "CanChooseCompactSpecialist" or "CompactSpecialistQuery" or "CompactSpecialists" or
            "HasCompactSpecialists" or "CompactSpecialistsEmpty" or "CompactSpecialistEmptyMessage" or "CompactSpecialistNoneSelection" or
            "CompactSpecialistNoneAccent" or "CompactConversationContext" or "CompactContextChangeMessage";
    }
}

public sealed class HomeModelPickerSpecialistRow(HomeModelPickerSpecialist definition,
    HomeModelPickerConversationSnapshot conversation) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Identity" => definition.Id.ToString("D"),
            "Name" => definition.Name,
            "Description" => definition.Description,
            "Accent" => conversation.SelectedSpecialistId == definition.Id ? "Secondary" : "None",
            "Selection" => new HomeModelPickerSpecialistSelection(conversation.ConversationId, conversation.ModelSelectionRevision,
                conversation.SelectedSpecialistId, conversation.SelectedSpecialistVersion, definition.Id, definition.Version),
            _ => null,
        };
        return path is "Identity" or "Name" or "Description" or "Accent" or "Selection";
    }
}

public sealed partial class HomeModelPickerCuiController
{
    private async Task<HomeCoreOperationResult<object>> RefreshConversationAsync(CancellationToken token)
    {
        if (_conversationSource is null)
            return Failure<object>("ConversationUnavailable", "Open this picker from a Chat to choose its Specialist.");
        return await ReadConversationAsync(() => _conversationSource.ReadAsync(token)).ConfigureAwait(false);
    }

    private async Task<HomeCoreOperationResult<object>> ChooseSpecialistAsync(
        HomeModelPickerSpecialistSelection? selection, CancellationToken token)
    {
        if (_conversationSource is null || Surface.CompactRole != HomeModelPickerCuiSurface.MainRole || selection is null)
            return Failure<object>("ConversationUnavailable", "Choose a current Specialist row in the Main Chat role.");
        return await ReadConversationAsync(() => _conversationSource.SelectSpecialistAsync(selection, token)).ConfigureAwait(false);
    }

    private async Task<HomeCoreOperationResult<object>> ReadConversationAsync(
        Func<Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>>> operation)
    {
        if (!CanPublish()) return Failure<object>("PickerUnavailable", "The model picker is no longer current.");
        Surface.SetConversationBusy(true);
        try
        {
            var result = await operation().ConfigureAwait(false);
            if (CanPublish()) Surface.ApplyConversation(result);
            return Box(result);
        }
        catch (Exception error)
        {
            if (CanPublish()) Surface.ApplyConversation(new(false, "ConversationReadFailed",
                "The originating Chat or Specialist catalogue could not be read: " + error.Message));
            throw; // The original source/host keeps the genuine failure and its close obligation.
        }
        finally { if (CanPublish()) Surface.SetConversationBusy(false); }
    }
}
