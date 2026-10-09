namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    private AssistantConfigurationDraft? _avatarDraft;
    private long _avatarDraftGeneration;
    private bool _avatarChooserVisible;
    private IReadOnlyList<AvatarRow> _avatarRows = [];
    public sealed class AvatarRow
    {
        internal AvatarRow(AssistantAvatarCatalogue.Choice original) => Original = original;
        internal AssistantAvatarCatalogue.Choice Original { get; }
        public string Id => Original.Key;
        public string IconKey => Original.Key;
        public string Label => Original.Label;
        public string Description => Original.Description;
    }
    private bool CanChooseAvatar => Current && !_unavailable && !_snapshot.IsRetiring &&
        _route == "configuration" && _draft is not null && !Busy;
    private bool HasCurrentAvatarRows => CanChooseAvatar && _avatarChooserVisible &&
        ReferenceEquals(_draft, _avatarDraft) && _draft!.OriginalGeneration == _avatarDraftGeneration;
    internal void ShowAvatarChooser()
    {
        if (!CanChooseAvatar) return;
        _avatarDraft = _draft; _avatarDraftGeneration = _draft!.OriginalGeneration;
        _avatarRows = Array.AsReadOnly(AssistantAvatarCatalogue.Choices.Select(choice => new AvatarRow(choice)).ToArray());
        _avatarChooserVisible = true; Refresh();
    }
    internal void CancelAvatarChooser() { _avatarChooserVisible = false; _avatarRows = []; Refresh(); }
    internal void ChooseOriginalAvatar(object? parameter)
    {
        if (!IsCurrentAvatarRow(parameter) || parameter is not AvatarRow row) return;
        var draft = _draft!;
        draft.SetConfiguration(draft.Configuration with { IconResourceId = row.Original.Key });
        _avatarChooserVisible = false; _avatarRows = []; Refresh();
    }
    private bool IsCurrentAvatarRow(object? value) => HasCurrentAvatarRows && value is AvatarRow row &&
        _avatarRows.Any(original => ReferenceEquals(original, row));
    private bool TryGetAvatarItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!IsCurrentAvatarRow(item) || item is not AvatarRow row) return false;
        value = path switch { "Id" => row.Id, "IconKey" => row.IconKey, "Label" => row.Label, "Description" => row.Description, _ => null };
        return path is "Id" or "IconKey" or "Label" or "Description";
    }
    private void RefreshAvatarConfiguration()
    {
        if (_avatarChooserVisible && (_route != "configuration" || !ReferenceEquals(_draft, _avatarDraft) ||
            _draft?.OriginalGeneration != _avatarDraftGeneration)) { _avatarChooserVisible = false; _avatarRows = []; }
        var saved = _draft?.Configuration.IconResourceId;
        Set("CanChooseAvatar", CanChooseAvatar);
        Set("AvatarChoiceLabel", "Choose icon · " + AssistantAvatarCatalogue.Label(saved));
        Set("DraftAvatarKey", AssistantAvatarCatalogue.DisplayKey(saved));
        Set("DraftAvatarLabel", "Assistant icon: " + AssistantAvatarCatalogue.Label(saved));
        Set("AvatarResourceStatus", AssistantAvatarCatalogue.IsListed(saved)
            ? "Choose a built-in icon and save your changes."
            : "Your saved icon reference is preserved. Custom images need an authorised asset source; this view shows a built-in fallback when that reference is unavailable.");
        Set("ShowAvatarChooser", _avatarChooserVisible); Set("CanSelectAvatar", HasCurrentAvatarRows);
        Set("AvatarChoices", HasCurrentAvatarRows ? _avatarRows : Array.Empty<AvatarRow>());
        var selected = _snapshot.SelectedAssistant?.Configuration.IconResourceId;
        Set("SelectedAssistantAvatarKey", AssistantAvatarCatalogue.DisplayKey(selected));
        Set("SelectedAssistantAvatarLabel", "Assistant icon: " + AssistantAvatarCatalogue.Label(selected));
    }
}
