using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Application.Canvas;

namespace HavenOS.Apps.Canvas;

/// <summary>Detached data only; no actor, grant, execution capability or private history.</summary>
internal sealed record CanvasRnoteSplitMaterialization(
    IReadOnlyList<CanvasInkStroke> Replacements, IReadOnlyList<Guid> RemovedStrokeIds,
    IReadOnlyList<Guid> StrokeOrder, IReadOnlyDictionary<Guid, ulong> NativeBindings,
    IReadOnlyDictionary<ulong, Guid> FragmentIdentities);

/// <summary>Lossless donor-to-canonical typed path conversion. Requires W1 Schema2 codec/session successor.</summary>
internal static class CanvasRnoteSplitMaterializer
{
    private const int MaximumAffectedEntities = CanvasStructuredStrokeTransactionLimits.MaximumEntriesPerSet;
    private const int MaximumReceiptBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static CanvasRnoteSplitMaterialization Materialize(CanvasArtifact artifact,
        IReadOnlyDictionary<Guid, ulong> originalBindings, byte[] receiptBytes,
        IReadOnlyDictionary<ulong, Guid>? retainedFragmentIdentities = null)
    {
        if (receiptBytes.Length is 0 or > MaximumReceiptBytes) throw new InvalidDataException("Split receipt byte budget exceeded.");
        var receipt = JsonSerializer.Deserialize<Receipt>(receiptBytes, Json) ?? throw new InvalidDataException("Split receipt is absent.");
        if (receipt.BeforeRenderKeys is null || receipt.AfterRenderKeys is null || receipt.BeforeLayers is null ||
            receipt.RemovedKeys is null || receipt.Changes is null || receipt.BeforeLayers.Any(value => value is null) ||
            receipt.Changes.Any(value => value is null || value.MatchingSourceSegmentOffsets is null))
            throw new InvalidDataException("Split required receipt collections are absent.");
        if (receipt.SchemaVersion != 1 || receipt.Geometry != "exact-retained-donor-penpath" ||
            receipt.SamplesRole != "original-input-provenance-not-fragment-polyline")
            throw new InvalidDataException("Unsupported split materialization contract.");
        if (artifact.Pages.Count != 1 || artifact.SharedResources.Count != 0 || artifact.Pages[0].Objects.Count != 0)
            throw new NotSupportedException("Split currently requires one fully bound native ink page; referenced objects need their owning admission.");
        var page = artifact.Pages[0];
        if (receipt.BeforeRenderKeys.Length > 16384 || receipt.AfterRenderKeys.Length > 16384 ||
            receipt.RemovedKeys.Length + receipt.Changes.Length > MaximumAffectedEntities)
            throw new InvalidDataException("Split entity budget exceeded.");
        if (originalBindings.Count != page.Strokes.Count || originalBindings.Values.Any(key => key == 0) ||
            originalBindings.Values.Distinct().Count() != originalBindings.Count ||
            page.StrokeOrder.Any(id => !originalBindings.ContainsKey(id)) ||
            !receipt.BeforeRenderKeys.SequenceEqual(page.StrokeOrder.Select(id => originalBindings[id])))
            throw new NotSupportedException("Canonical stroke order and complete donor identities require reconciliation before split erasing.");
        var reverse = originalBindings.ToDictionary(pair => pair.Value, pair => pair.Key);
        var strokes = page.Strokes.ToDictionary(stroke => stroke.StrokeId);
        var layers = page.Layers.ToDictionary(layer => layer.LayerId);
        ValidateLayerMapping(page, strokes, reverse, receipt.BeforeLayers);
        var removed = receipt.RemovedKeys;
        var changed = receipt.Changes;
        if (removed.Distinct().Count() != removed.Length || removed.Any(key => !reverse.ContainsKey(key)) ||
            changed.Select(change => change.NativeKey).Distinct().Count() != changed.Length ||
            changed.Any(change => change.NativeKey == 0 || !reverse.ContainsKey(change.SourceNativeKey) || removed.Contains(change.NativeKey)))
            throw new InvalidDataException("Split receipt entity identities are inconsistent.");
        var created = changed.Where(change => change.IsNew).Select(change => change.NativeKey).ToHashSet();
        if (changed.Any(change => change.IsNew == reverse.ContainsKey(change.NativeKey) ||
            (!change.IsNew && change.SourceNativeKey != change.NativeKey)))
            throw new InvalidDataException("Split cannot substitute a surviving native or canonical identity.");
        var expectedKeys = reverse.Keys.Except(removed).Concat(created).ToHashSet();
        if (receipt.AfterRenderKeys.Distinct().Count() != receipt.AfterRenderKeys.Length || !expectedKeys.SetEquals(receipt.AfterRenderKeys))
            throw new InvalidDataException("Split full donor render order must cover all resulting entities exactly once.");
        var fragmentIds = retainedFragmentIdentities is null
            ? created.ToDictionary(key => key, _ => Guid.NewGuid())
            : new Dictionary<ulong, Guid>(retainedFragmentIdentities);
        if (!created.SetEquals(fragmentIds.Keys) || fragmentIds.Values.Any(id => id == Guid.Empty || strokes.ContainsKey(id)) ||
            fragmentIds.Values.Distinct().Count() != fragmentIds.Count)
            throw new InvalidDataException("Split fragment identities do not match the exact captured proposal.");
        var bindings = new Dictionary<Guid, ulong>(originalBindings);
        var removals = removed.Select(key => reverse[key]).ToArray();
        foreach (var id in removals)
        {
            if (layers[strokes[id].LayerId].IsLocked) throw new InvalidOperationException("Split cannot remove locked source ink.");
            bindings.Remove(id);
        }
        var replacements = new List<CanvasInkStroke>();
        long segmentBudget = 1_000_000;
        long aggregateSampleAndSegmentEntries=0;
        foreach (var change in changed)
        {
            var source = strokes[reverse[change.SourceNativeKey]];
            if (layers[source.LayerId].IsLocked) throw new InvalidOperationException("Split cannot edit or fragment locked source ink.");
            if (source.Transform != new CanvasTransform())
                throw new NotSupportedException("Nonidentity canonical transforms need an exact native coordinate-space mapping before split materialization.");
            var sourceLayer = receipt.BeforeLayers.Single(item => item.NativeKey == change.SourceNativeKey).Layer;
            if (!JsonElement.DeepEquals(sourceLayer, change.Layer)) throw new InvalidDataException("Split changed its original donor layer.");
            if (change.MatchingSourceSegmentOffsets.Length == 0 || change.MatchingSourceSegmentOffsets.Any(offset => offset < 0) ||
                change.MatchingSourceSegmentOffsets.Distinct().Count() != change.MatchingSourceSegmentOffsets.Length)
                throw new InvalidDataException("Split has no exact original path witness.");
            var geometry = DecodePath(change.Path, ref segmentBudget);
            // Shared structural admission before cloning duplicated input provenance or
            // exposing any Home review. This is data validation, never an access grant.
            foreach(var sample in source.Samples)
                if(++aggregateSampleAndSegmentEntries > CanvasStructuredStrokeTransactionLimits.MaximumAggregateSampleAndSegmentEntries)
                    throw new InvalidDataException("This eraser change exceeds the supported stroke transaction size.");
            foreach(var segment in geometry.Segments)
                if(++aggregateSampleAndSegmentEntries > CanvasStructuredStrokeTransactionLimits.MaximumAggregateSampleAndSegmentEntries)
                    throw new InvalidDataException("This eraser change exceeds the supported stroke transaction size.");
            var id = change.IsNew ? fragmentIds[change.NativeKey] : source.StrokeId;
            var extension = source.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) ?? [];
            extension["9to1.Canvas.SplitSource"] = JsonSerializer.SerializeToElement(new
            {
                sourceStrokeId = source.StrokeId, sourceRevisionId = source.RevisionId,
                matchingSourceSegmentOffsets = change.MatchingSourceSegmentOffsets,
                samplesRole = "original-input-provenance", donorRevision = CanvasRnoteDocument.DonorRevision
            });
            var replacement = source with
            {
                StrokeId = id, RevisionId = Guid.NewGuid(), Samples = source.Samples.ToList(),
                // Schema2 geometry is authoritative. Samples retain source tilt/time/input
                // exactly; they are never relabeled as a fragment-render polyline.
                PathGeometry = geometry, ExtensionData = extension
            };
            replacements.Add(replacement); bindings[id] = change.NativeKey;
        }
        var resultingReverse = bindings.ToDictionary(pair => pair.Value, pair => pair.Key);
        var order = receipt.AfterRenderKeys.Select(key => resultingReverse[key]).ToArray();
        var layerIndices = page.LayerOrder.Select((id, index) => (id, index)).ToDictionary(value => value.id, value => value.index);
        var resulting = strokes.Values.Where(stroke => !removals.Contains(stroke.StrokeId)).ToDictionary(stroke => stroke.StrokeId);
        foreach (var replacement in replacements) resulting[replacement.StrokeId] = replacement;
        var previousLayer = -1;
        foreach (var id in order)
        {
            var index = layerIndices[resulting[id].LayerId];
            if (index < previousLayer) throw new NotSupportedException("Generated donor chronology conflicts with canonical layer order.");
            previousLayer = index;
        }
        return new(Array.AsReadOnly(replacements.ToArray()), Array.AsReadOnly(removals), Array.AsReadOnly(order),
            new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, ulong>(bindings),
            new System.Collections.ObjectModel.ReadOnlyDictionary<ulong, Guid>(fragmentIds));
    }

    private static void ValidateLayerMapping(CanvasPage page, IReadOnlyDictionary<Guid, CanvasInkStroke> strokes,
        IReadOnlyDictionary<ulong, Guid> reverse, NativeLayerRow[] nativeLayers)
    {
        if (nativeLayers.Length != reverse.Count || nativeLayers.Select(item => item.NativeKey).Distinct().Count() != nativeLayers.Length ||
            nativeLayers.Any(item => !reverse.ContainsKey(item.NativeKey))) throw new InvalidDataException("Split donor layer mapping is incomplete.");
        var mapping = new Dictionary<Guid, string>(); var reverseLayers = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var item in nativeLayers)
        {
            var layerId = strokes[reverse[item.NativeKey]].LayerId;
            var value = CanonicalLayer(item.Layer);
            if ((mapping.TryGetValue(layerId, out var prior) && prior != value) ||
                (reverseLayers.TryGetValue(value, out var priorId) && priorId != layerId))
                throw new NotSupportedException("Split requires exact one-to-one canonical/native layer bindings.");
            mapping[layerId] = value; reverseLayers[value] = layerId;
        }
        var occupied = page.LayerOrder.Where(mapping.ContainsKey).Select(id => mapping[id]).ToArray();
        if (!occupied.SequenceEqual(occupied.Order(StringComparer.Ordinal)))
            throw new NotSupportedException("Canonical/native layer orders require reconciliation.");
    }
    private static string CanonicalLayer(JsonElement layer)
    {
        if (layer.ValueKind == JsonValueKind.String) return layer.GetString() switch
        { "document" => "0", "image" => "1", "highlighter" => "2", _ => throw new InvalidDataException("Unknown donor layer.") };
        var properties = layer.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "user_layer" || !properties[0].Value.TryGetUInt32(out var number))
            throw new InvalidDataException("Unknown donor layer.");
        return "3:" + number.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static CanvasStrokePathGeometry DecodePath(JsonElement path, ref long budget)
    {
        RequireMembers(path, "start", "segments");
        var start = Element(path.GetProperty("start")); var segments = new List<CanvasStrokePathSegment>();
        foreach (var segment in path.GetProperty("segments").EnumerateArray())
        {
            if (--budget < 0 || segments.Count == CanvasStructuredStrokeTransactionLimits.MaximumSegmentsPerPath) throw new InvalidDataException("Split exact path segment budget exceeded.");
            var variants = segment.EnumerateObject().ToArray();
            if (variants.Length != 1) throw new InvalidDataException("Split segment variant is ambiguous.");
            var body = variants[0].Value;
            var kind = variants[0].Name switch
            {
                "lineto" => CanvasStrokePathSegmentKind.Line,
                "quadbezto" => CanvasStrokePathSegmentKind.Quadratic,
                "cubbezto" => CanvasStrokePathSegmentKind.Cubic,
                _ => throw new InvalidDataException("Split segment variant is unsupported.")
            };
            if (kind == CanvasStrokePathSegmentKind.Line) RequireMembers(body, "end");
            else if (kind == CanvasStrokePathSegmentKind.Quadratic) RequireMembers(body, "cp", "end");
            else RequireMembers(body, "cp1", "cp2", "end");
            segments.Add(new CanvasStrokePathSegment
            {
                Kind = kind, End = Element(body.GetProperty("end")),
                Control1 = kind == CanvasStrokePathSegmentKind.Line ? null : Point(body.GetProperty(kind == CanvasStrokePathSegmentKind.Quadratic ? "cp" : "cp1")),
                Control2 = kind == CanvasStrokePathSegmentKind.Cubic ? Point(body.GetProperty("cp2")) : null
            });
        }
        if (segments.Count == 0) throw new InvalidDataException("A genuine split fragment requires retained segments.");
        return new CanvasStrokePathGeometry { SchemaVersion = 1, Start = start, Segments = segments.ToList() };
    }
    private static CanvasStrokePathElement Element(JsonElement element)
    {
        RequireMembers(element, "pos", "pressure"); var point = Point(element.GetProperty("pos"));
        var pressure = element.GetProperty("pressure").GetDouble();
        if (!double.IsFinite(pressure) || pressure is < 0 or > 1) throw new InvalidDataException("Invalid genuine donor pressure.");
        return new(point.X, point.Y, pressure);
    }
    private static CanvasStrokePathPoint Point(JsonElement value)
    {
        var coordinates = value.EnumerateArray().ToArray();
        if (coordinates.Length != 2) throw new InvalidDataException("Invalid donor point dimensions.");
        var x = coordinates[0].GetDouble(); var y = coordinates[1].GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new InvalidDataException("Invalid donor path coordinate.");
        return new(x, y);
    }
    private static void RequireMembers(JsonElement value, params string[] names)
    {
        var members = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (members.Length != names.Length || members.Distinct(StringComparer.Ordinal).Count() != members.Length ||
            !members.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Unknown, duplicate or missing genuine path member.");
    }
    private sealed class Receipt
    {
        [JsonRequired] public int SchemaVersion { get; set; }
        [JsonRequired] public ulong[] BeforeRenderKeys { get; set; } = [];
        [JsonRequired] public NativeLayerRow[] BeforeLayers { get; set; } = [];
        [JsonRequired] public ulong[] AfterRenderKeys { get; set; } = [];
        [JsonRequired] public ulong[] RemovedKeys { get; set; } = [];
        [JsonRequired] public Change[] Changes { get; set; } = [];
        [JsonRequired] public string Geometry { get; set; } = "";
        [JsonRequired] public string SamplesRole { get; set; } = "";
    }
    private sealed class NativeLayerRow
    {
        [JsonRequired] public ulong NativeKey { get; set; }
        [JsonRequired] public JsonElement Layer { get; set; }
    }
    private sealed class Change
    {
        [JsonRequired] public ulong NativeKey { get; set; }
        [JsonRequired] public ulong SourceNativeKey { get; set; }
        [JsonRequired] public bool IsNew { get; set; }
        [JsonRequired] public int[] MatchingSourceSegmentOffsets { get; set; } = [];
        [JsonRequired] public JsonElement Path { get; set; }
        [JsonRequired] public JsonElement Style { get; set; }
        [JsonRequired] public JsonElement Layer { get; set; }
    }
}
