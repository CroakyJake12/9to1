using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    private readonly record struct ProfileDraftKey(string ModelId, string ProfileId);
    private sealed record ProfileDraft(string Settings, string Device, string Context, string Name)
    {
        internal static readonly ProfileDraft Empty = new("{}", "", "", "");
    }

    // These are uncommitted editor values, owned by this retained panel. Only the
    // existing explicit profile commands can write a profile to Dulche.
    private readonly Dictionary<ProfileDraftKey, ProfileDraft> _profileDrafts = new();
    private ProfileDraftKey? _profileInputOwner;
    private ProfileDraft? _profileInputBaseline;
    private string? _profileCatalogueModel;

    private void InitializeProfileDrafts() => WriteProfileDraft(ProfileDraft.Empty);

    private void ChangeProfileDraftModel(string? modelId)
    {
        if (!string.Equals(Model?.Id, modelId, StringComparison.Ordinal)) return;
        RememberProfileDraft();
        _profileInputOwner = null;
        _profileInputBaseline = null;
        _profileCatalogueModel = null;
        // The previous model's ComboBox rows may remain until the new read settles.
        // Its editable name and settings must not be attributed to that new model.
        WriteProfileDraft(ProfileDraft.Empty);
    }

    private void AdmitProfileDraftCatalogue(string modelId)
    {
        if (!string.Equals(Model?.Id, modelId, StringComparison.Ordinal)) return;
        RememberProfileDraft();
        _profileCatalogueModel = modelId;
    }

    private void ApplySelectedProfileDraft()
    {
        RememberProfileDraft();
        if (Model is not { } model ||
            !string.Equals(_profileCatalogueModel, model.Id, StringComparison.Ordinal) ||
            Select("profiles").SelectedItem is not Item profile)
        {
            _profileInputOwner = null;
            _profileInputBaseline = null;
            WriteProfileDraft(ProfileDraft.Empty);
            RenderModelPresentation();
            return;
        }

        var settings = profile.Data["settings"] as JsonObject;
        var baseline = new ProfileDraft(
            settings?.ToJsonString() ?? "{}",
            settings?["gpu"]?.ToString() ?? Value(model.Data, "gpu"),
            (settings?["context"] ?? settings?["context_length"])?.ToString() ?? Value(model.Data, "context"),
            "");
        var key = new ProfileDraftKey(model.Id, profile.Id);
        _profileInputOwner = key;
        _profileInputBaseline = baseline;
        if (_profileDrafts.TryGetValue(key, out var draft) && draft != baseline)
            WriteProfileDraft(draft);
        else
        {
            _profileDrafts.Remove(key);
            WriteProfileDraft(baseline);
        }
        RenderModelPresentation();
    }

    private void RememberProfileDraft()
    {
        if (_profileInputOwner is not { } owner || _profileInputBaseline is not { } baseline) return;
        var current = new ProfileDraft(
            Input("profile-settings").Text ?? "",
            Input("profile-device").Text ?? "",
            Input("profile-context").Text ?? "",
            Input("profile-name").Text ?? "");
        if (current == baseline) _profileDrafts.Remove(owner);
        else _profileDrafts[owner] = current;
    }

    private void WriteProfileDraft(ProfileDraft value)
    {
        Input("profile-settings").Text = value.Settings;
        Input("profile-device").Text = value.Device;
        Input("profile-context").Text = value.Context;
        Input("profile-name").Text = value.Name;
    }
}
