using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>
/// The Present persistence/compatibility boundary for the existing Rnote-derived drawing.ink object.
/// The full Home bundle is retained; this adapter neither invents ink nor raster-flattens its payload.
/// A live Present surface binding is not installed here, so insertion remains explicitly unavailable.
/// </summary>
public sealed class CanvasPresentInkBridge : IPresentSharedObjectBridge
{
    private readonly HomeProductivityEngine _engine;
    private readonly HomeProductivityContext _target;

    public CanvasPresentInkBridge(HomeProductivityEngine engine, Guid presentationId, long revision)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        if (presentationId == Guid.Empty || revision < 0)
            throw new ArgumentException("Compatibility requires a real Present identity and revision.");
        _target = new("present", presentationId.ToString("D"), revision, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    }

    public PresentSharedObjectCapture Capture(JsonElement canonicalBundle)
    {
        if (canonicalBundle.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shared drawing requires a canonical Home bundle.");
        var parsed = _engine.ParseBundle(canonicalBundle.GetRawText());
        if (_engine.EngineVersion != HomeProductivityEngine.CurrentEngineVersion ||
            parsed.Objects.Count != 1 || parsed.Objects[0].ObjectType != "drawing.ink" ||
            parsed.Objects[0].SchemaVersion != 1)
            throw new PresentSharedObjectCapabilityException("RequiredSchemaUnsupported",
                "This bridge preserves one supported Home drawing.ink object; other required schemas are unavailable.");
        if (!_engine.GetCompatibility("present", "development", ["drawing.ink"]).Compatible)
            throw new PresentSharedObjectCapabilityException("InkOwnerUnavailable",
                "The actual Home/Rnote shared ink handler must be registered; a schema name alone is insufficient.");
        // Validate with the actual canonical Canvas/Rnote contract as well as Home's registry.
        // This is pure stroke validation, not native rendering or a claimed artifact commit.
        _ = new CanvasInkObjectHandler().Create(parsed.Objects[0].ObjectId, parsed.Objects[0].Content);
        if (!_engine.CanPaste(parsed, _target, out var code))
            throw new PresentSharedObjectCapabilityException(code,
                "Home rejected the ink bundle. Keep its original payload; do not flatten unsupported properties.");
        var value = parsed.Objects[0];
        using var snapshot = JsonDocument.Parse(_engine.SerializeBundle(parsed));
        return new(value.ObjectId, value.ObjectType, value.SchemaVersion, snapshot.RootElement.Clone());
    }

    public PresentSharedObjectCapture CloneForPaste(JsonElement canonicalBundle)
    {
        var captured = Capture(canonicalBundle);
        var parsed = _engine.ParseBundle(captured.Bundle.GetRawText());
        // CanvasInkObjectHandler.CloneForPaste owns StrokeId/RevisionId rebinding and optional fields.
        // Canvas LayerId stays in the canonical payload; this bridge never invents a target Canvas layer.
        var cloned = parsed with { Objects = _engine.Paste(parsed, _target) };
        using var snapshot = JsonDocument.Parse(_engine.SerializeBundle(cloned));
        return Capture(snapshot.RootElement);
    }

    public PresentSharedObjectSupport GetSupport(JsonElement canonicalBundle)
    {
        try { _ = Capture(canonicalBundle); }
        catch (PresentSharedObjectCapabilityException error)
        { return new(false, false, error.Code, error.Message); }
        catch (Exception error) when (error is InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(false, false, "InvalidOrUnsupportedInk", error.Message); }
        return new(true, false, "SharedObjectRendererUnavailable",
            "The canonical drawing is compatible and can be preserved, but this Present surface has no live shared-ink renderer. No insertion was performed.");
    }
}
