using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Canvas;

public enum CanvasCompatibilityDisposition { Translated, Degraded, Omitted, Blocked }
public sealed record CanvasCompatibilityIssue(string EntityId, string Feature, CanvasCompatibilityDisposition Disposition, string Reason);
public sealed record CanvasImportCompatibilityReport(string SourceFormat, IReadOnlyList<CanvasCompatibilityIssue> Issues);

/// <summary>
/// Editable donor state inside the versioned Canvas artifact. Native payloads
/// remain structured Rnote snapshots; renderer output is never the source of truth.
/// Storage and permission ownership stay with the authenticated Files host.
/// </summary>
public sealed class CanvasRnoteDocument : IDisposable
{
    private const string StateKey = "9to1.Canvas.RnoteState";
    public const string DonorRevision = "1a728d6a85db3528f9c79dc0990700e91b22696f";
    private readonly object _gate = new();
    private RnoteCanvasEngine _engine;
    private readonly CanvasArtifactSession _session;
    private bool _disposed;
    private Guid _renderedRevision;
    private RnoteRenderFrame? _renderedFrame;

    private CanvasRnoteDocument(CanvasArtifact artifact, RnoteCanvasEngine engine, CanvasImportCompatibilityReport report)
    {
        _engine = engine;
        CompatibilityReport = report;
        try
        {
            ValidateNativeStrokeBindings(artifact, engine);
            artifact.DocumentSettings = SettingsWithEngineState(artifact.DocumentSettings, engine, report);
            _session = new CanvasArtifactSession(artifact);
        }
        catch { engine.Dispose(); throw; }
    }

    public CanvasImportCompatibilityReport CompatibilityReport { get; }
    public CanvasArtifact Snapshot { get { lock (_gate) { EnsureOpen(); return _session.GetArtifactSnapshot(); } } }

    public static CanvasRnoteDocument Create(string? name = null)
    {
        var engine = RnoteCanvasEngine.Create();
        var artifact = CanvasArtifact.Create(name);
        return new CanvasRnoteDocument(artifact, engine, new("9to1c", [PrecisionIssue(artifact.ArtifactId)]));
    }

    public static CanvasRnoteDocument Import(byte[] source, string format, string? name = null, double xoppDpi = 96)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        var normalized = format.TrimStart('.').ToLowerInvariant();
        var engine = normalized switch
        {
            "rnote" => RnoteCanvasEngine.Open(source),
            "xopp" => RnoteCanvasEngine.ImportXopp(source, xoppDpi),
            _ => throw new NotSupportedException($"Rnote import does not support '{format}'.")
        };
        var artifact = CanvasArtifact.Create(name);
        // Imported donor entities remain fully editable by Rnote. Per-entity
        // mapping into shared Canvas IDs is an explicit integration gate.
        var report = new CanvasImportCompatibilityReport(normalized, [
            new(artifact.ArtifactId.ToString(), "Donor entity identity", CanvasCompatibilityDisposition.Blocked,
                "Structured donor content is retained and rendered by Rnote; imported per-entity Canvas API identity mapping is not available yet."),
            new(artifact.ArtifactId.ToString(), "Native page layout", CanvasCompatibilityDisposition.Translated,
                "Donor page geometry is preserved inside the engine snapshot. Native page conversion is blocked until page identity mapping is available."),
            PrecisionIssue(artifact.ArtifactId)
        ]);
        return new CanvasRnoteDocument(artifact, engine, report);
    }

    public static CanvasRnoteDocument Open(byte[] nativeArtifact)
    {
        var artifact = CanvasArtifactCodec.Deserialize(nativeArtifact);
        if (!artifact.DocumentSettings.Properties.TryGetValue(StateKey, out var state))
            throw new NotSupportedException("The artifact has no Rnote engine state; open it using the registered native Canvas renderer.");
        var payload = state.Deserialize<RnotePersistedState>() ?? throw new InvalidDataException("Canvas Rnote state is empty.");
        if (payload.SchemaVersion != 1 || payload.DonorRevision != DonorRevision)
            throw new NotSupportedException("Canvas Rnote state requires an explicit donor/schema migration.");
        if (payload.CompatibilityReport is null || payload.CompatibilityReport.Issues is null)
            throw new InvalidDataException("Canvas Rnote state has no compatibility report.");
        if (string.IsNullOrWhiteSpace(payload.PayloadBase64) || payload.PayloadBase64.Length > 358 * 1024 * 1024)
            throw new InvalidDataException("Canvas Rnote state exceeds the supported payload limit.");
        var bytes = Convert.FromBase64String(payload.PayloadBase64);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), payload.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Canvas Rnote state checksum does not match its payload.");
        return new CanvasRnoteDocument(artifact, RnoteCanvasEngine.Open(bytes), payload.CompatibilityReport);
    }

    public Guid DrawStroke(IReadOnlyList<RnotePointerSample> samples, Guid expectedRevision)
        => DrawStroke(samples, new CanvasMutationRequest(expectedRevision, Guid.NewGuid(), new("canvas.input", "Canvas native input")));

    /// <summary>The host supplies the permission-filtered actor after Home authorization.</summary>
    public Guid DrawStroke(IReadOnlyList<RnotePointerSample> samples, CanvasMutationRequest request, CanvasRnoteInkStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(request);
        var capturedSamples = RnoteCanvasEngine.CaptureSamples(samples);
        var inkStyle = style ?? CanvasRnoteInkStyle.Default;
        var resolvedBrush = inkStyle.BrushSnapshot();
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            var page = artifact.Pages[0];
            var stroke = new CanvasInkStroke
            {
                StrokeId = request.OperationId,
                RevisionId = request.OperationId,
                LayerId = page.LayerOrder[0],
                ToolDefinitionId = inkStyle.Kind == CanvasRnoteInkKind.Solid ? "pen" : "highlighter",
                Samples = capturedSamples.Select(sample => new CanvasStrokeSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToList(),
                ResolvedBrushProperties = resolvedBrush
            };
            RnoteCanvasEngine? candidate = null;
            try
            {
                var result = _session.AddStructuredStroke(request, page.PageId, stroke, () =>
                {
                    candidate = RnoteCanvasEngine.Open(_engine.Save());
                    var bindings = new Dictionary<Guid, ulong>(ReadPersistedState(artifact).NativeStrokeKeys ?? new Dictionary<Guid, ulong>());
                    var priorKeys = candidate.SupportsStructuredSelectionExport ? candidate.ReadStrokeKeys().ToHashSet() : null;
                    candidate.DrawCapturedStroke(capturedSamples, inkStyle);
                    if (priorKeys is not null)
                    {
                        var currentKeys = candidate.ReadStrokeKeys().ToHashSet();
                        var added = currentKeys.Except(priorKeys).ToArray();
                        if (!priorKeys.IsSubsetOf(currentKeys) || added.Length != 1)
                            throw new InvalidDataException("The donor stroke did not produce exactly one stable native entity.");
                        bindings.Add(stroke.StrokeId, added[0]);
                    }
                    return SettingsWithEngineState(artifact.DocumentSettings, candidate, CompatibilityReport, bindings);
                });
                RequireSuccess(result);
                // Idempotent replays do not invoke the donor and do not replace
                // engine state. Rejected candidates never affect the live engine.
                if (candidate is not null)
                {
                    var prior = _engine;
                    _engine = candidate;
                    candidate = null;
                    prior.Dispose();
                }
                return stroke.StrokeId;
            }
            finally { candidate?.Dispose(); }
        }
    }

    public void Rename(string name, Guid expectedRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Rename(name, new CanvasMutationRequest(expectedRevision, Guid.NewGuid(), new("canvas.input", "Canvas native input")));
    }

    public void Rename(string name, CanvasMutationRequest request)
    {
        lock (_gate) { EnsureOpen(); RequireSuccess(_session.RenameArtifact(request, name)); }
    }

    public void Undo(CanvasMutationRequest request) => RestoreHistory(request, undo: true);
    public void Redo(CanvasMutationRequest request) => RestoreHistory(request, undo: false);

    private void RestoreHistory(CanvasMutationRequest request, bool undo)
    {
        lock (_gate)
        {
            EnsureOpen();
            var result = undo ? _session.Undo(request) : _session.Redo(request);
            RequireSuccess(result);
            var state = ReadPersistedState(_session.GetArtifactSnapshot());
            var restored = RnoteCanvasEngine.Open(Convert.FromBase64String(state.PayloadBase64));
            var prior = _engine;
            _engine = restored;
            prior.Dispose();
        }
    }

    public RnoteRenderFrame Render()
    {
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            if (_renderedFrame is null || _renderedRevision != artifact.RevisionId)
            {
                // Render the committed donor representation. Rnote rounds its
                // generated pen paths during serialization; showing the live
                // higher-precision path would visibly change after reopen.
                // Keep the active engine/history intact and cache by revision.
                var state = ReadPersistedState(artifact);
                using var durable = RnoteCanvasEngine.Open(Convert.FromBase64String(state.PayloadBase64));
                _renderedFrame = durable.Render();
                _renderedRevision = artifact.RevisionId;
            }
            return _renderedFrame with { Svg = (byte[])_renderedFrame.Svg.Clone() };
        }
    }
    public byte[] Serialize() { lock (_gate) { EnsureOpen(); return CanvasArtifactCodec.Serialize(_session.GetArtifactSnapshot()); } }
    public byte[] ExportRnote() { lock (_gate) { EnsureOpen(); return _engine.Save(); } }

    /// <summary>
    /// Read-only domain export for an already authorized host. Exact canonical
    /// IDs and current artifact revision select native entities; missing legacy
    /// or imported bindings fail explicitly rather than exporting the document.
    /// This method does not grant Files/Home read authority.
    /// </summary>
    public byte[] ExportCanonicalStrokeSelection(IEnumerable<Guid> strokeIds, Guid expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(strokeIds);
        var captured = new List<Guid>();
        foreach (var id in strokeIds)
        {
            if (captured.Count == 1_000_000) throw new ArgumentException("Canvas selection exceeds its entity limit.", nameof(strokeIds));
            captured.Add(id);
        }
        if (captured.Count == 0 || captured.Any(id => id == Guid.Empty) || captured.Distinct().Count() != captured.Count)
            throw new ArgumentException("Canvas selection requires unique nonempty stroke IDs.", nameof(strokeIds));
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            if (expectedRevision == Guid.Empty || artifact.RevisionId != expectedRevision)
                throw new InvalidOperationException("Canvas selection targets a stale artifact revision.");
            var existing = artifact.Pages.SelectMany(page => page.Strokes).Select(stroke => stroke.StrokeId).ToHashSet();
            if (captured.Any(id => !existing.Contains(id)))
                throw new InvalidOperationException("Canvas selection contains a stroke outside the current canonical artifact.");
            var bindings = ReadPersistedState(artifact).NativeStrokeKeys;
            if (bindings is null || captured.Any(id => !bindings.ContainsKey(id)))
                throw new NotSupportedException("This Canvas selection has no retained canonical-to-native entity binding.");
            return _engine.ExportSelectedStrokes(captured.Select(id => bindings[id]));
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _engine.Dispose(); } }

    private static void RequireSuccess(CanvasApiResult<CanvasMutationResult> result)
    {
        if (!result.IsSuccess) throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message}");
    }
    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static CanvasCompatibilityIssue PrecisionIssue(Guid id) => new(id.ToString(), "Donor numeric precision", CanvasCompatibilityDisposition.Degraded,
        "Rnote serializes applicable generated path geometry and pressure fields to three decimal places. Previews use durable donor precision; canonical native stroke samples retain their original precision and tilt.");
    private static RnotePersistedState ReadPersistedState(CanvasArtifact artifact) =>
        artifact.DocumentSettings.Properties[StateKey].Deserialize<RnotePersistedState>() ?? throw new InvalidDataException("Canvas Rnote state is empty.");

    private static CanvasDocumentSettings SettingsWithEngineState(CanvasDocumentSettings settings, RnoteCanvasEngine engine, CanvasImportCompatibilityReport report,
        IReadOnlyDictionary<Guid, ulong>? nativeStrokeKeys = null)
    {
        var bytes = engine.Save();
        var retained = nativeStrokeKeys ?? (settings.Properties.TryGetValue(StateKey, out var prior)
            ? prior.Deserialize<RnotePersistedState>()?.NativeStrokeKeys : null);
        var payload = new RnotePersistedState(1, DonorRevision, Convert.ToBase64String(bytes), Convert.ToHexString(SHA256.HashData(bytes)), report,
            retained is null ? null : new Dictionary<Guid, ulong>(retained));
        var properties = new Dictionary<string, JsonElement>(settings.Properties, StringComparer.Ordinal) { [StateKey] = JsonSerializer.SerializeToElement(payload) };
        return settings with { Properties = properties };
    }

    private static void ValidateNativeStrokeBindings(CanvasArtifact artifact, RnoteCanvasEngine engine)
    {
        if (!artifact.DocumentSettings.Properties.TryGetValue(StateKey, out var state)) return;
        var bindings = state.Deserialize<RnotePersistedState>()?.NativeStrokeKeys;
        if (bindings is null || bindings.Count == 0) return;
        if (bindings.Count > 1_000_000 || bindings.Keys.Any(id => id == Guid.Empty) || bindings.Values.Distinct().Count() != bindings.Count)
            throw new InvalidDataException("Canvas native stroke bindings have invalid or duplicate identity.");
        var canonical = artifact.Pages.SelectMany(page => page.Strokes).Select(stroke => stroke.StrokeId).ToHashSet();
        if (bindings.Keys.Any(id => !canonical.Contains(id)))
            throw new InvalidDataException("Canvas native stroke binding targets an absent canonical stroke.");
        var native = engine.ReadStrokeKeys().ToHashSet();
        if (bindings.Values.Any(key => !native.Contains(key)))
            throw new InvalidDataException("Canvas native stroke binding targets an absent persisted native entity.");
    }

    private sealed record RnotePersistedState(int SchemaVersion, string DonorRevision, string PayloadBase64, string Sha256,
        CanvasImportCompatibilityReport CompatibilityReport, IReadOnlyDictionary<Guid, ulong>? NativeStrokeKeys = null);
}
