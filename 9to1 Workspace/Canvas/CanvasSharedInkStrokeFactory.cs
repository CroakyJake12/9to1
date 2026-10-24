using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

public sealed record CanvasSharedInkOwnerSnapshot(HomeProductivityContext Context, Guid LayerId,
    string OwnerRevision, IReadOnlyList<JsonElement> Bundles)
{
    public double LogicalWidth { get; init; } = CanvasSharedInkStrokeFactory.LogicalWidth;
    public double LogicalHeight { get; init; } = CanvasSharedInkStrokeFactory.LogicalHeight;
}
public sealed record CanvasSharedInkEraseGesture(IReadOnlyList<RnotePointerSample> Samples, double Width);
public sealed record CanvasSharedInkStrokeRequest(CanvasSharedInkOwnerSnapshot Expected, JsonElement Bundle)
{
    public CanvasSharedInkEraseGesture? Erasure { get; init; }
    public IReadOnlyList<JsonElement>? ReplacementBundles { get; init; }
}
public sealed record CanvasSharedInkCommitResult(bool Succeeded, string Code, string Message);
public sealed record CanvasSharedInkScene(HomeProductivityObjectRenderResult Rendered,
    double X, double Y, double Width, double Height, bool IsEmpty);

/// <summary>Reusable canonical ink preparation and donor rendering. No artifact, Files identity,
/// persistence or history is created here; the current app owns the resulting Home bundles.</summary>
public static class CanvasSharedInkStrokeFactory
{
    public const int MaximumObjects = 256;
    public const int MaximumSamples = 100_000;
    public const double LogicalWidth = 960, LogicalHeight = 540;
    private static readonly CanvasInkObjectHandler Handler = new();

    public static JsonElement Capture(HomeProductivityEngine engine, HomeProductivityContext context,
        Guid layerId, IReadOnlyList<RnotePointerSample> samples, CanvasRnoteInkStyle? style = null) =>
        Capture(engine, context, layerId, samples, style, LogicalWidth, LogicalHeight);

    public static JsonElement Capture(HomeProductivityEngine engine, HomeProductivityContext context,
        Guid layerId, IReadOnlyList<RnotePointerSample> samples, CanvasRnoteInkStyle? style, double logicalWidth, double logicalHeight)
    {
        DemandOwner(engine, context); DemandDimensions(logicalWidth, logicalHeight);
        if (layerId == Guid.Empty) throw new ArgumentException("The owning drawing layer must have a stable identity.", nameof(layerId));
        var captured = RnoteCanvasEngine.CaptureSamples(samples);
        if (captured.Length > MaximumSamples) throw new InvalidDataException("This shared stroke exceeds the supported sample count.");
        if (captured.Any(sample => sample.X < 0 || sample.X > logicalWidth || sample.Y < 0 || sample.Y > logicalHeight))
            throw new InvalidDataException("The stroke lies outside the shared drawing viewport.");
        var resolved = style ?? CanvasRnoteInkStyle.Default;
        var id = Guid.NewGuid();
        var stroke = new CanvasInkStroke
        {
            StrokeId = id, LayerId = layerId, RevisionId = Guid.NewGuid(),
            ToolDefinitionId = resolved.Kind == CanvasRnoteInkKind.Solid ? "pen" : "highlighter",
            Samples = captured.Select(sample => new CanvasStrokeSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToList(),
            ResolvedBrushProperties = resolved.BrushSnapshot(), Transform = new CanvasTransform()
        };
        var value = engine.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke));
        var bundle = engine.SerializeSelection(context with { SelectedObjectIds = [id] }, [value]);
        using var document = JsonDocument.Parse(engine.SerializeBundle(bundle));
        return CaptureBundle(engine, context, document.RootElement);
    }

    /// <summary>Retain the entire versioned bundle, including optional fields; never flatten it.</summary>
    public static JsonElement CaptureBundle(HomeProductivityEngine engine, HomeProductivityContext context, JsonElement source)
    {
        DemandOwner(engine, context);
        if (source.ValueKind != JsonValueKind.Object || source.GetRawText().Length > 16 * 1024 * 1024)
            throw new InvalidDataException("The shared drawing bundle is absent or exceeds its supported bound.");
        var bundle = engine.ParseBundle(source.GetRawText());
        if (bundle.Objects.Count is < 1 or > MaximumObjects)
            throw new InvalidDataException("The shared drawing has an unsupported object count.");
        if (!engine.CanPaste(bundle, context, out var code))
            throw new InvalidDataException("Home cannot admit this shared drawing (" + code + "). Its complete original bundle must be retained.");
        var ids = new HashSet<Guid>();
        var samples = 0;
        foreach (var value in bundle.Objects)
        {
            var stroke = Handler.ReadStroke(value);
            if (!ids.Add(value.ObjectId) || stroke.Transform != new CanvasTransform() || stroke.PathGeometry is not null)
                throw new NotSupportedException("This shared viewport cannot render duplicate identities, transformed or split-path strokes; their original bundles are retained.");
            if (stroke.Samples.Any(sample => Math.Abs(sample.X) > 100_000 || Math.Abs(sample.Y) > 100_000))
                throw new InvalidDataException("The shared drawing geometry exceeds the supported coordinate bounds.");
            samples = checked(samples + stroke.Samples.Count);
            if (samples > MaximumSamples) throw new InvalidDataException("The shared drawing exceeds the supported sample count.");
        }
        using var copy = JsonDocument.Parse(engine.SerializeBundle(bundle));
        return copy.RootElement.Clone();
    }

    public static CanvasSharedInkOwnerSnapshot Snapshot(HomeProductivityEngine engine, CanvasSharedInkOwnerSnapshot owner)
    {
        DemandOwner(engine, owner.Context); DemandDimensions(owner.LogicalWidth, owner.LogicalHeight);
        if (owner.LayerId == Guid.Empty || string.IsNullOrWhiteSpace(owner.OwnerRevision) || owner.Bundles is null || owner.Bundles.Count > MaximumObjects)
            throw new InvalidDataException("A current artifact, layer and unsaved revision are required for shared ink.");
        var characters = 0L;
        foreach (var bundle in owner.Bundles)
        {
            characters += bundle.GetRawText().Length;
            if (characters > 16 * 1024 * 1024) throw new InvalidDataException("The shared drawing scene exceeds its supported payload bound.");
        }
        var context = owner.Context with { SelectedObjectIds = owner.Context.SelectedObjectIds.ToArray(),
            SupportedObjectTypes = owner.Context.SupportedObjectTypes.ToHashSet(StringComparer.Ordinal) };
        var bundles = owner.Bundles.Select(bundle => CaptureBundle(engine, context, bundle)).ToArray();
        foreach (var value in bundles.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects))
            if (Handler.ReadStroke(value).LayerId != owner.LayerId)
                throw new InvalidDataException("The shared stroke belongs to another drawing layer. Its original bundle is retained; placement must be explicitly rebound by the owner before editing.");
        return owner with { Context = context, Bundles = bundles };
    }

    public static bool SameOwner(CanvasSharedInkOwnerSnapshot left, CanvasSharedInkOwnerSnapshot right) =>
        left.Context.AppId == right.Context.AppId && left.Context.ArtifactId == right.Context.ArtifactId &&
        left.Context.CanonicalRevision == right.Context.CanonicalRevision && left.LayerId == right.LayerId &&
        left.OwnerRevision == right.OwnerRevision && left.LogicalWidth == right.LogicalWidth && left.LogicalHeight == right.LogicalHeight && left.Bundles.Count == right.Bundles.Count &&
        left.Bundles.Zip(right.Bundles).All(pair => JsonElement.DeepEquals(pair.First, pair.Second));

    /// <summary>The SAME Rnote renderer and handler rules for an entire drawing, preserving native
    /// bounds. The resulting raster is transient presentation, never a replacement canonical store.</summary>
    public static CanvasSharedInkScene RenderScene(HomeProductivityEngine engine, HomeProductivityContext context,
        Guid layerId, IReadOnlyList<JsonElement> sources)
    {
        var captured = Snapshot(engine, new(context, layerId, "render", sources));
        var objects = captured.Bundles.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).ToArray();
        if (objects.Length > MaximumObjects || objects.Select(value => value.ObjectId).Distinct().Count() != objects.Length)
            throw new InvalidDataException("The shared scene exceeds its object bound or repeats an object identity.");
        var sampleCount = 0;
        var unsupported = new List<string>();
        using var native = RnoteCanvasEngine.Create();
        foreach (var value in objects)
        {
            var stroke = Handler.ReadStroke(value);
            unsupported.AddRange(CanvasInkObjectHandler.RetainedUnsupportedProperties(value, stroke).Select(path => value.ObjectId.ToString("D") + ":" + path));
            sampleCount = checked(sampleCount + stroke.Samples.Count);
            if (sampleCount > MaximumSamples) throw new InvalidDataException("The shared scene exceeds its sample bound.");
            native.DrawStroke(stroke.Samples.Select(sample => new RnotePointerSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToArray(),
                CanvasInkObjectHandler.ResolveStyle(stroke));
        }
        // Real transient capability probe for an empty canvas. It is not published, inserted or saved
        // to any user artifact. Both the donor and raster/materializer must work before pen is enabled.
        if (objects.Length == 0) native.DrawStroke([new(1, 1, .5), new(2, 2, .5)]);
        using var durable = RnoteCanvasEngine.Open(native.Save());
        var vector = durable.Render();
        if (!double.IsFinite(vector.X) || !double.IsFinite(vector.Y) || !double.IsFinite(vector.Width) ||
            !double.IsFinite(vector.Height) || vector.Width <= 0 || vector.Height <= 0 || vector.Width > 100_000 || vector.Height > 100_000)
            throw new InvalidDataException("The donor returned invalid shared drawing bounds.");
        var scale = Math.Min(2, 2048 / Math.Max(vector.Width, vector.Height));
        var pixels = CanvasSvgRasterizer.Render(vector.Svg, Math.Max(1, (int)Math.Ceiling(vector.Width * scale)), Math.Max(1, (int)Math.Ceiling(vector.Height * scale)));
        var id = "shared-ink-" + layerId.ToString("N");
        var rendered = new HomeProductivityObjectRenderResult($"<Cui><Object Type=\"spe.raster\" id=\"{id}\" /></Cui>", unsupported)
        { RasterBindings = [new(id, layerId, new(pixels.Width, pixels.Height, pixels.Stride, pixels.PremultipliedBgra))] };
        return new(rendered, vector.X, vector.Y, vector.Width, vector.Height, objects.Length == 0);
    }

    /// <summary>Owner-neutral preparation using the SAME real Rnote whole-stroke
    /// eraser. Canonical IDs are mapped to actual donor keys, never inferred from
    /// sample distance. The owning app must revalidate this gesture before commit.</summary>
    public static CanvasSharedInkStrokeRequest CaptureWholeErase(HomeProductivityEngine engine,
        CanvasSharedInkOwnerSnapshot owner, IReadOnlyList<RnotePointerSample> samples, double width)
    {
        var captured = Snapshot(engine, owner);
        var gesture = RnoteCanvasEngine.CaptureSamples(samples);
        if (gesture.Length > MaximumSamples || !double.IsFinite(width) || width is < 1 or > 500 ||
            gesture.Any(point => point.X < 0 || point.Y < 0 || point.X > captured.LogicalWidth || point.Y > captured.LogicalHeight))
            throw new InvalidDataException("The eraser gesture exceeds the current drawing bounds.");
        var objects = captured.Bundles.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).ToArray();
        if (objects.Length > MaximumObjects || objects.Select(value => value.ObjectId).Distinct().Count() != objects.Length)
            throw new InvalidDataException("The original drawing repeats an identity or exceeds its supported bound.");
        using var native = RnoteCanvasEngine.Create();
        var keys = new Dictionary<ulong, Guid>();
        var totalSamples = 0;
        foreach (var value in objects)
        {
            var prior = native.ReadStrokeKeys().ToHashSet();
            var stroke = Handler.ReadStroke(value);
            totalSamples = checked(totalSamples + stroke.Samples.Count);
            if (totalSamples > MaximumSamples) throw new InvalidDataException("The eraser scene exceeds its supported sample bound.");
            native.DrawStroke(stroke.Samples.Select(point => new RnotePointerSample(point.X, point.Y, point.Pressure, point.TiltX, point.TiltY)).ToArray(),
                CanvasInkObjectHandler.ResolveStyle(stroke));
            var after = native.ReadStrokeKeys().ToHashSet();
            var added = after.Except(prior).ToArray();
            if (added.Length != 1 || prior.Except(after).Any() || !keys.TryAdd(added[0], value.ObjectId))
                throw new NotSupportedException("The donor cannot map this shared stroke to one exact eraser identity.");
        }
        // Match the same save/reopened donor geometry used by RenderScene, so
        // edge hits agree with the actual displayed/reopened precision.
        using var durable = RnoteCanvasEngine.Open(native.Save());
        var beforeErase = durable.ReadStrokeKeys().ToHashSet();
        if (!beforeErase.SetEquals(keys.Keys)) throw new InvalidDataException("The donor changed canonical key mapping during reopen.");
        durable.EraseWholeStrokes(gesture, width);
        var surviving = durable.ReadStrokeKeys().ToHashSet();
        if (surviving.Except(beforeErase).Any())
            throw new NotSupportedException("Partial/split erasure requires exact shared path support; original ink is retained.");
        var removed = beforeErase.Except(surviving).Select(key => keys.TryGetValue(key, out var id) ? id
            : throw new InvalidDataException("The donor erased an unmapped shared stroke.")).ToHashSet();
        var retained = new List<JsonElement>();
        foreach (var source in captured.Bundles)
        {
            var bundle = engine.ParseBundle(source.GetRawText());
            var remaining = bundle.Objects.Where(value => !removed.Contains(value.ObjectId)).ToArray();
            if (remaining.Length == bundle.Objects.Count) { retained.Add(source.Clone()); continue; }
            if (remaining.Length == 0) continue;
            // Preserve styles, assets, accessibility and all unknown fields. Only
            // the known portable fallback is regenerated for the exact survivors.
            var fallback = engine.SerializeSelection(captured.Context with { SelectedObjectIds = remaining.Select(value => value.ObjectId).ToArray() }, remaining).PortableFallback;
            using var document = JsonDocument.Parse(engine.SerializeBundle(bundle with { Objects = remaining, PortableFallback = fallback }));
            retained.Add(CaptureBundle(engine, captured.Context, document.RootElement));
        }
        return new(captured, default)
        {
            Erasure = new(Array.AsReadOnly(gesture.ToArray()), width),
            ReplacementBundles = Array.AsReadOnly(retained.ToArray())
        };
    }

    private static void DemandDimensions(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 1 or > 100_000 || height is < 1 or > 100_000)
            throw new InvalidDataException("The shared logical viewport must have finite supported dimensions.");
    }

    private static void DemandOwner(HomeProductivityEngine engine, HomeProductivityContext context)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.AppId) || string.IsNullOrWhiteSpace(context.ArtifactId) ||
            !context.CanonicalRevision.IsValid || !context.SupportedObjectTypes.Contains("drawing.ink") ||
            engine.EngineVersion != HomeProductivityEngine.CurrentEngineVersion ||
            !engine.GetCompatibility(context.AppId, "development", ["drawing.ink"]).Compatible)
            throw new NotSupportedException("The current Home engine and actual shared ink handler are required.");
    }
}
