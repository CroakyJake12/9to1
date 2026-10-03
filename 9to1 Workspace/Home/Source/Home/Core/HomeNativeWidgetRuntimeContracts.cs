using System.Collections.Frozen;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Capture inputs are placement descriptors, never approval or owner execution authority.
/// Launcher must retain/revalidate its original nonserializable displayed session around each capture.</summary>
public sealed record HomeNativeWidgetCaptureRequest(HomeNativeWidgetReference Reference,
    string SurfaceReference, HomeNativeWidgetSize GridSize, double ViewportWidth, double ViewportHeight);

/// <summary>Trusted transport composition supplies this endpoint with the actual observed peer during
/// the same registry registration. Neither endpoint nor observed peer may come from request JSON.</summary>
public interface IHomeNativeWidgetRuntimeEndpoint
{
    ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Actual owner backend preserves the exact authenticated actor throughout its data reads.
/// A connected native owner client requires this port and independently authenticates the original
/// actor and declared source scopes; wire identity fields alone never grant access. No ambient fallback.</summary>
public interface IHomeNativeWidgetOriginalActorRuntimeEndpoint : IHomeNativeWidgetRuntimeEndpoint
{
    ValueTask<HomeNativeWidgetSurface?> CaptureForActorAsync(HomeNativeWidgetCaptureRequest request,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}

/// <summary>Detached bounded owner-authored data. This is not a renderer or action capability. The
/// owning platform validates supported CUI controls/binding keys before materialization and dispatches
/// only declared exact actions through its canonical Home broker.</summary>
public sealed class HomeNativeWidgetSurface
{
    private readonly IReadOnlyDictionary<string, JsonElement> _data;
    public const int SchemaVersion = 1;
    private HomeNativeWidgetSurface(HomeNativeWidgetReference reference, string surfaceReference,
        string authoredCui, IReadOnlyDictionary<string, JsonElement> data)
    { Reference = reference; SurfaceReference = surfaceReference; AuthoredCui = authoredCui; _data = data; }
    public int Schema => SchemaVersion;
    public HomeNativeWidgetReference Reference { get; }
    public string SurfaceReference { get; }
    public string AuthoredCui { get; }
    public IReadOnlyDictionary<string, JsonElement> Data => _data;

    public static HomeNativeWidgetSurface Capture(HomeNativeWidgetReference reference, string surfaceReference,
        string authoredCui, IReadOnlyDictionary<string, JsonElement> data)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(surfaceReference) || surfaceReference.Length > 4096 ||
            string.IsNullOrWhiteSpace(authoredCui) || Encoding.UTF8.GetByteCount(authoredCui) > 262144)
            throw new InvalidDataException("Widget surface must be explicit bounded owner-authored CUI.");
        var entries = data.Take(257).ToArray();
        if (entries.Length > 256 || entries.Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
            pair.Key.Length > 256 || pair.Value.ValueKind == JsonValueKind.Undefined))
            throw new InvalidDataException("Widget binding data must be bounded and explicit.");
        // Callers must keep source documents alive throughout capture, including Clone below.
        // Inspect raw UTF-8 lengths without materializing strings or cloning unbounded owner values.
        long rawValueBytes = 0;
        foreach (var pair in entries)
        {
            rawValueBytes += JsonMarshal.GetRawUtf8Value(pair.Value).Length;
            if (rawValueBytes > 262144)
                throw new InvalidDataException("Widget binding data exceeds the surface bound.");
        }
        var detached = entries.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(detached);
        if (bytes.Length > 262144) throw new InvalidDataException("Widget binding data exceeds the surface bound.");
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        return new(reference, surfaceReference, authoredCui, detached.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
