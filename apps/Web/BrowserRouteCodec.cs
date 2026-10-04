using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Browser addressing for existing Home requests; never an action executor or authority.</summary>
public static class BrowserRouteCodec
{
    private const int MaximumFragmentLength = 8192;
    private static readonly HashSet<string> Fields = ["entityType", "entityId", "action", "deepLink"];

    public static string Encode(HomeFeatureNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidRouteId(request.RouteId))
            throw new ArgumentException("A canonical route ID is required.", nameof(request));
        if (request.ModelPickerTarget is not null)
            throw new ArgumentException("Model picker targeting requires an explicit browser adapter.", nameof(request));
        var fields = new List<string>();
        Add("entityType", request.EntityType);
        Add("entityId", request.EntityId);
        Add("action", request.Action);
        Add("deepLink", request.DeepLink);
        var fragment = "#/" + request.RouteId + (fields.Count == 0 ? "" : "?" + string.Join('&', fields));
        if (fragment.Length > MaximumFragmentLength)
            throw new ArgumentException("The browser route exceeds the supported length.", nameof(request));
        return fragment;

        void Add(string key, string? value)
        {
            if (value is null) return;
            if (value.Any(char.IsControl)) throw new ArgumentException("Browser route fields cannot contain control characters.", nameof(request));
            fields.Add(key + "=" + Uri.EscapeDataString(value));
        }
    }

    public static bool TryDecode(string? fragment, out HomeFeatureNavigationRequest? request, out string? errorCode)
    {
        request = null;
        errorCode = null;
        if (string.IsNullOrEmpty(fragment) || fragment == "#" || fragment == "#/")
        {
            request = new(HomeFeatureRouteIds.Dashboard);
            return true;
        }
        if (fragment.Length > MaximumFragmentLength || !fragment.StartsWith("#/", StringComparison.Ordinal))
            return Fail("BrowserRouteInvalid", out errorCode);
        var separator = fragment.IndexOf('?');
        var routeId = separator < 0 ? fragment[2..] : fragment[2..separator];
        if (!IsValidRouteId(routeId)) return Fail("BrowserRouteInvalid", out errorCode);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (separator >= 0)
        {
            foreach (var field in fragment[(separator + 1)..].Split('&', StringSplitOptions.None))
            {
                var equals = field.IndexOf('=');
                if (equals <= 0) return Fail("BrowserRouteInvalid", out errorCode);
                var key = field[..equals];
                if (!Fields.Contains(key) || values.ContainsKey(key))
                    return Fail("BrowserRouteInvalid", out errorCode);
                var encoded = field[(equals + 1)..];
                if (!HasValidPercentEscapes(encoded)) return Fail("BrowserRouteInvalid", out errorCode);
                var value = Uri.UnescapeDataString(encoded);
                if (value.Any(char.IsControl)) return Fail("BrowserRouteInvalid", out errorCode);
                values.Add(key, value);
            }
        }
        request = new(routeId, Get("entityType"), Get("entityId"), Get("action"), Get("deepLink"));
        return true;

        string? Get(string name) => values.GetValueOrDefault(name);
    }

    private static bool IsValidRouteId(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static bool HasValidPercentEscapes(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) return false;
            i += 2;
        }
        return true;
    }

    private static bool Fail(string code, out string? errorCode)
    {
        errorCode = code;
        return false;
    }
}
