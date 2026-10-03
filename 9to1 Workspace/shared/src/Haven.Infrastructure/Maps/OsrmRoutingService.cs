/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Maps/OsrmRoutingService.cs, in the Infrastructure layer.
 * What: Owns OsrmRoutingService — route computation over the OSRM HTTP API for the
 *       driving, walking and cycling profiles, decoded from GeoJSON geometry into GeoPoint lists.
 * How: Uses the shared named HttpClient ("Haven.Maps"), builds the /route/v1/{profile}/… request,
 *      parses routes[0] with System.Text.Json, formats MapRoute.SummaryText via MapRouteSummaries,
 *      and returns null on recoverable failures while letting cancellation propagate.
 * Why: OSRM is a best-effort free service; routing integration details stay in Infrastructure with
 *      the endpoint overridable via configuration so providers can be switched without an update.
 * Maintenance: Preserve provider terms — HTTPS endpoint, identifying User-Agent (from the shared
 *              client plus per-request header), attribution surfaced through MapsAttribution by UI.
 */

using System.Globalization;
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Route computation over the OSRM public API; treated as a best-effort free service.</summary>
public sealed record OsrmRoutingConfiguration(Uri BaseAddress, IReadOnlyCollection<MapTravelProfile> SupportedProfiles);

public sealed class OsrmRoutingService : IMapRoutingCapabilities
{
    /// <summary>Default OSRM endpoint; override with HAVEN_MAPS_OSRM_BASE.</summary>
    public const string DefaultBaseAddress = "https://router.project-osrm.org";

    /// <summary>Environment variable overriding the OSRM base address.</summary>
    public const string BaseOverrideVariable = "HAVEN_MAPS_OSRM_BASE";

    private const int MaximumRoutePoints = 2000;

    private readonly HttpClient _client;
    private readonly string _baseAddress;
    public IReadOnlySet<MapTravelProfile> SupportedProfiles { get; }
    private const int MaximumResponseBytes = 2 * 1024 * 1024;

    /// <summary>Creates the routing service over the shared named HttpClient.</summary>
    public OsrmRoutingService(IHttpClientFactory httpClientFactory, OsrmRoutingConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _client = httpClientFactory.CreateClient(OpenStreetMapService.HttpClientName);
        if (configuration is not null && (configuration.BaseAddress is null || configuration.SupportedProfiles is null))
            throw new ArgumentException("Configured routing endpoint/capabilities must be explicit.");
        var endpoint = configuration?.BaseAddress ?? new Uri(ResolveBase());
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Routing endpoint must be an HTTPS service without inline credentials/query/fragment.");
        _baseAddress = endpoint.AbsoluteUri.TrimEnd('/');
        var profiles = configuration?.SupportedProfiles ?? [MapTravelProfile.Driving];
        if (profiles.Any(profile => !Enum.IsDefined(profile))) throw new ArgumentException("Unknown configured routing profile.");
        // OSRM's URL profile name does not switch the loaded routing dataset. The public service
        // is a driving engine; walking/cycling require an explicitly configured matching backend.
        SupportedProfiles = profiles.Where(profile => endpoint.Host.TrimEnd('.') != "router.project-osrm.org" || profile == MapTravelProfile.Driving).ToFrozenSet();
    }

    /// <summary>
    /// Computes a route between two points. Returns null when the service is unreachable, returns a
    /// non-Ok code, or yields unusable geometry; cancellation is always honoured.
    /// </summary>
    public async Task<MapRoute?> GetRouteAsync(GeoPoint start, GeoPoint end, MapTravelProfile profile, CancellationToken cancellationToken)
    {
        if (!SupportedProfiles.Contains(profile) || !MapJourneyLogic.IsCoordinateValid(start) || !MapJourneyLogic.IsCoordinateValid(end)) return null;
        try
        {
            var profileName = MapRouteSummaries.ToProfileName(profile);
            var requestUri = $"{_baseAddress}/route/v1/{profileName}/{FormatCoordinate(start.Longitude)},{FormatCoordinate(start.Latitude)}" +
                             $";{FormatCoordinate(end.Longitude)},{FormatCoordinate(end.Latitude)}?overview=full&geometries=geojson";
            using var request = CreateRequest(requestUri);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var bounded = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (bounded.Length + read > MaximumResponseBytes) return null;
                bounded.Write(buffer, 0, read);
            }
            var payload = JsonSerializer.Deserialize<OsrmResponse>(bounded.GetBuffer().AsSpan(0, checked((int)bounded.Length)));
            var route = payload?.Routes is { Length: > 0 } routes ? routes[0] : null;
            if (!string.Equals(payload?.Code, "Ok", StringComparison.OrdinalIgnoreCase) || route?.Geometry?.Coordinates is not { Length: > 1 } coordinates) return null;

            var points = new List<GeoPoint>(Math.Min(coordinates.Length, MaximumRoutePoints));
            foreach (var coordinate in coordinates)
            {
                if (coordinate is null || coordinate.Length < 2) return null;
                var point = new GeoPoint(coordinate[1], coordinate[0]);
                if (!MapJourneyLogic.IsCoordinateValid(point)) return null;
                points.Add(point);
                if (points.Count > MaximumRoutePoints) return null;
            }
            if (points.Count < 2 || !double.IsFinite(route.Distance) || route.Distance < 0 || !double.IsFinite(route.Duration)
                || route.Duration < 0 || route.Duration > TimeSpan.MaxValue.TotalSeconds) return null;
            return new MapRoute(
                [.. points],
                Math.Max(0d, route.Distance),
                TimeSpan.FromSeconds(Math.Max(0d, route.Duration)),
                profileName,
                MapRouteSummaries.Format(route.Distance, TimeSpan.FromSeconds(Math.Max(0d, route.Duration))));
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or UriFormatException or OverflowException
            || (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateRequest(string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        // The named client also sets this header at registration; setting it per request keeps the
        // provider terms honoured even if the shared registration changes.
        request.Headers.UserAgent.ParseAdd(OpenStreetMapService.UserAgent);
        return request;
    }

    private static string ResolveBase()
    {
        var custom = Environment.GetEnvironmentVariable(BaseOverrideVariable)?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(custom)) return DefaultBaseAddress;
        if (!Uri.TryCreate(custom, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The configured routing endpoint is invalid; no fallback provider was contacted.");
        return custom;
    }

    private static string FormatCoordinate(double degrees) =>
        degrees.ToString("0.#######", CultureInfo.InvariantCulture);

    private sealed record OsrmResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("routes")] OsrmRoute[]? Routes);

    private sealed record OsrmRoute(
        [property: JsonPropertyName("distance")] double Distance,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("geometry")] OsrmGeometry? Geometry);

    private sealed record OsrmGeometry([property: JsonPropertyName("coordinates")] double[][]? Coordinates);
}
