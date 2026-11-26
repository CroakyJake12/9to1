using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Haven;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    internal long ModelSelectionRevision => _modelSelectionRevision;

    /// <summary>Apply only a current, actor-bound Home route to this already selected
    /// conversation. This changes future sends, not the model captured by an active send,
    /// and never submits a pending instruction.</summary>
    internal bool ApplyCommittedHomeModelRoute(HavenModelProjectionSnapshot? snapshot,
        AuthenticatedResourceActor actor, long expectedSelectionRevision)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _originalWork.IsRetiring || _modelSelectionRevision != expectedSelectionRevision) return false;
        if (snapshot is not null &&
            (snapshot.Actor != actor || snapshot.ScopeName != "User" ||
             !string.Equals(snapshot.ScopeId, actor.ProfileId, StringComparison.Ordinal)))
            return false;

        var provider = snapshot?.ProviderId;
        var model = snapshot?.ModelId;
        var name = string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)
            ? null
            : string.Equals(provider, "ollama", StringComparison.OrdinalIgnoreCase) ? model : $"{provider}:{model}";
        _modelSelectionRevision++;
        _hasCommittedHomeRoute = true;
        _committedHomeModelName = name;
        _selectedModel = name is null || !_modelCatalogueCurrent ? null : _admittedCatalogue.FirstOrDefault(
            entry => string.Equals(entry.Name, name, StringComparison.Ordinal));
        _modelAvailability = _selectedModel is null ? ChatModelAvailability.Unavailable : ChatModelAvailability.Available;
        if (_selectedModel is not null) _modelAvailabilityCause = null;
        SetProjectionStatus(_selectedModel is null
            ? "The selected model is unavailable in this conversation. Refresh the model list or choose another model."
            : null);
        ModelChanged?.Invoke(this, EventArgs.Empty);
        PublishOriginalModelGateVisual();
        return true;
    }
}
