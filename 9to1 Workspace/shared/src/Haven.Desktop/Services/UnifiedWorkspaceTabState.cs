using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>Stores only the content address/profile projection inside the existing
/// tab StateJson. Existing document, navigation and extension state remains intact.
/// A restored address is a request; each app still performs fresh admission.</summary>
internal static class UnifiedWorkspaceTabState
{
    private const string Property = "9to1.AppContent";

    internal static bool TryProject(TabSessionSnapshot tab, string currentProfileId, out BrowserTabState? projected)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(currentProfileId);
        projected = null;
        if (tab.Id == Guid.Empty) return false;
        JsonObject state;
        try { if (JsonNode.Parse(tab.StateJson) is not JsonObject value) return false; state = value; }
        catch (JsonException) { return false; }
        string? address;
        var profile = currentProfileId;
        if (state.TryGetPropertyValue(Property, out var stored))
        {
            // Never reinterpret a malformed/future native declaration as a web search
            // or as a legacy route. The original row remains in the canonical snapshot.
            if (stored is not JsonObject content || !TryInt(content["Version"], out var version) || version != 1 ||
                !TryString(content["Address"], out address)) return false;
            if (content["ProfileId"] is not null)
            {
                if (!TryString(content["ProfileId"], out var savedProfile) || savedProfile is null) return false;
                // The existing Browse persistence format uses an empty profile for
                // ordinary web tabs. Resolve that marker against the current host;
                // the resulting metadata still grants no native app authority.
                if (savedProfile.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(savedProfile)) return false;
                    profile = savedProfile;
                }
            }
        }
        else
        {
            address = tab.AppKey.ToLowerInvariant() switch
            {
                "home" => "browse://home",
                "go" => "browse://newtab",
                "chat" or "new" or "new-chat" => LegacyConversationAddress(state),
                _ => "9to1://" + tab.AppKey
            };
        }
        if (!AppContentAddress.TryParse(address, out var parsed) || parsed is null) return false;
        parsed = AppContentRouteMigration.Canonicalize(parsed);
        projected = new(tab.Id, tab.Title, parsed.Value, BrowserTabPrivacy.Standard,
            tab.GroupId?.ToString("D") ?? string.Empty, tab.UpdatedAt) { ProfileId = profile };
        return true;
    }

    internal static TabSessionSnapshot Merge(TabSessionSnapshot? existing, BrowserTabState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Id == Guid.Empty || existing is not null && existing.Id != current.Id)
            throw new InvalidOperationException("A tab projection must retain its original stable identity.");
        if (current.Privacy != BrowserTabPrivacy.Standard)
            throw new InvalidOperationException("Private tabs cannot enter the canonical workspace session.");
        if (current.ProfileId is null || current.ProfileId.Length > 0 && string.IsNullOrWhiteSpace(current.ProfileId))
            throw new InvalidDataException("A saved profile must be an identity or the existing empty-profile marker.");
        if (current.Group.Length > 0 && (!Guid.TryParse(current.Group, out var parsedGroup) || parsedGroup == Guid.Empty))
            throw new InvalidOperationException("Resolve group names through the canonical workspace before merging a tab.");
        var address = AppContentRouteMigration.Canonicalize(AppContentAddress.Parse(current.Address));
        JsonObject state;
        if (existing is null) state = new();
        else
        {
            state = JsonNode.Parse(existing.StateJson) as JsonObject
                ?? throw new InvalidDataException("Preserve the original tab state until it can be migrated.");
            if (state.ContainsKey(Property) && !TryProject(existing, current.ProfileId, out _))
                throw new InvalidDataException("A newer or invalid tab state cannot be overwritten.");
        }
        state[Property] = new JsonObject
        {
            ["Version"] = 1, ["Address"] = address.Value, ["ProfileId"] = current.ProfileId
        };
        var appKey = address.Namespace switch
        {
            AppContentNamespace.Web => "browse",
            AppContentNamespace.NativeApp => address.Authority,
            AppContentNamespace.Browse when address.Authority == "home" => "home",
            AppContentNamespace.Browse when address.Authority == "newtab" => "go",
            _ => "browse"
        };
        var group = Guid.TryParse(current.Group, out var groupId) ? groupId : (Guid?)null;
        return existing is null
            ? new(current.Id, appKey, current.Title, state.ToJsonString(), null, null, group,
                false, address.Value == "browse://home", current.UpdatedAt, current.UpdatedAt)
            : existing with
            {
                AppKey = appKey, Title = current.Title, StateJson = state.ToJsonString(),
                GroupId = group, UpdatedAt = current.UpdatedAt
            };
    }

    private static string LegacyConversationAddress(JsonObject state)
    {
        var node = state["ConversationId"] ?? state["conversationId"];
        return TryString(node, out var value) && Guid.TryParse(value, out var id) && id != Guid.Empty
            ? "9to1://spaces/chat/" + id.ToString("D") : "9to1://spaces/chat";
    }
    private static bool TryString(JsonNode? node, out string? value)
    {
        value = null;
        return node is JsonValue scalar && scalar.TryGetValue(out value);
    }
    private static bool TryInt(JsonNode? node, out int value)
    {
        value = 0;
        return node is JsonValue scalar && scalar.TryGetValue(out value);
    }
}
