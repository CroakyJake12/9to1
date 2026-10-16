using System.Globalization;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantConfigurationDraft
{
    private string? _ucmlCategoryText, _ucmlAskCategoryText, _ucmlExpiryText;
    public string UcmlCategoryText => _ucmlCategoryText ?? string.Join("\n", Configuration.Memory.UcmlCategories);
    public string UcmlAskCategoryText => _ucmlAskCategoryText ?? string.Join("\n", Configuration.Memory.UcmlAskCategories);
    public string UcmlExpiryText => _ucmlExpiryText ?? Configuration.Memory.UcmlAccessExpiresAt?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? "";
    public string UcmlPreferenceError { get; private set; } = "";
    private void ResetUcmlPreferenceText() { _ucmlCategoryText = _ucmlAskCategoryText = _ucmlExpiryText = null; UcmlPreferenceError = ""; }
    private bool TrySetUcmlPreferenceText(string field, string value)
    {
        if (field is not ("DraftUcmlCategories" or "DraftUcmlAskCategories" or "DraftUcmlExpiry")) return false;
        if (value.Length > (field == "DraftUcmlExpiry" ? 64 : 8192)) return false;
        if (field == "DraftUcmlCategories") _ucmlCategoryText = value;
        else if (field == "DraftUcmlAskCategories") _ucmlAskCategoryText = value;
        else _ucmlExpiryText = value;
        _generation = checked(_generation + 1);
        var categories = UcmlCategoryText.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var askCategories = UcmlAskCategoryText.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expiryText = UcmlExpiryText.Trim(); DateTimeOffset? expiry = null;
        if (expiryText.Length != 0)
        {
            if (!expiryText.EndsWith('Z') || !DateTimeOffset.TryParse(expiryText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            { UcmlPreferenceError = "Use a UTC expiry such as 2027-01-01T00:00:00Z, or leave it blank."; return true; }
            expiry = parsed;
        }
        var preference = Configuration.Memory with { UcmlCategories = Array.AsReadOnly(categories), UcmlAskCategories = Array.AsReadOnly(askCategories), UcmlAccessExpiresAt = expiry };
        try { preference.ValidateUcmlPolicy(); }
        catch (AssistantCommandRefusedException failure) { UcmlPreferenceError = failure.Message; return true; }
        UcmlPreferenceError = ""; Configuration = Configuration with { Memory = preference }; return true;
    }

    // Preferences only. The same original draft submission, CAS save and observed
    // acknowledgement own persistence; neither checkbox issues a memory input/grant.
    private AssistantConfiguration? MemoryPreferenceReplacement(string field, bool value) => field switch
    {
        "DraftMemoryEnabled" => Configuration with { Memory = Configuration.Memory with { Enabled = value } },
        "DraftMemoryIncludeProjectContext" => Configuration with { Memory = Configuration.Memory with { IncludeProjectContext = value } },
        _ => null
    };
}
