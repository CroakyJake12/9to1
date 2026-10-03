using System.Security.Cryptography;
using System.IO.Compression;
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
    private Guid? _selectedUserLayer;
    private Guid _renderedRevision;
    private RnoteRenderFrame? _renderedFrame;

    private CanvasRnoteDocument(CanvasArtifact artifact, RnoteCanvasEngine engine, CanvasImportCompatibilityReport report)
    {
        _engine = engine;
        CompatibilityReport = report;
        try
        {
            ValidateNativeStrokeBindings(artifact, engine);
            if (ReadPersistedLayerRanks(artifact) is not null) ValidateUserLayerBindings(artifact, engine, allowLegacySingleLayer: false);
            artifact.DocumentSettings = SettingsWithEngineState(artifact.DocumentSettings, engine, report);
            _session = new CanvasArtifactSession(artifact);
        }
        catch { engine.Dispose(); throw; }
    }

    /// <summary>Editor-local stable layer selection; never persisted as canonical content.</summary>
    public Guid ActiveNativeUserLayerId
    {
        get
        {
            lock (_gate)
            {
                EnsureOpen();var page=_session.GetArtifactSnapshot().Pages[0];
                return _selectedUserLayer is Guid selected && page.Layers.Any(layer=>layer.LayerId==selected) ? selected : page.LayerOrder[0];
            }
        }
    }

    public string? NativeUserLayerUnavailableReason
    {
        get
        {
            lock (_gate)
            {
                EnsureOpen();
                try { ValidateUserLayerBindings(_session.GetArtifactSnapshot(),_engine,allowLegacySingleLayer:true);return null; }
                catch (NotSupportedException error) { return error.Message; }
                catch (InvalidDataException error) { return error.Message; }
                catch (InvalidOperationException error) { return $"Native user-layer navigation is unavailable: {error.Message}"; }
            }
        }
    }

    public void SelectNativeUserLayer(Guid pageId, Guid layerId)
    {
        lock (_gate)
        {
            EnsureOpen();var artifact=_session.GetArtifactSnapshot();
            ValidateUserLayerBindings(artifact,_engine,allowLegacySingleLayer:true);
            if(artifact.Pages[0].PageId!=pageId || !artifact.Pages[0].Layers.Any(layer=>layer.LayerId==layerId))
                throw new ArgumentException("The selected layer is not part of the original canonical page.",nameof(layerId));
            _selectedUserLayer=layerId;
        }
    }

    public CanvasImportCompatibilityReport CompatibilityReport { get; }
    public CanvasArtifact Snapshot { get { lock (_gate) { EnsureOpen(); return _session.GetArtifactSnapshot(); } } }
    /// <summary>Exact current identity/revision without copying retained donor bytes. Not a resource grant.</summary>
    public (Guid ArtifactId, Guid RevisionId) Identity
    { get { lock (_gate) { EnsureOpen(); return (_session.ArtifactId, _session.CurrentRevisionId); } } }

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
    public Guid DrawStroke(IReadOnlyList<RnotePointerSample> samples, CanvasMutationRequest request, CanvasRnoteInkStyle? style = null) =>
        DrawStrokeCore(samples,request,null,style);

    /// <summary>Typed owning intent supplies its exact captured layer; later local selection cannot redirect it.</summary>
    public Guid DrawStrokeIntoNativeUserLayer(IReadOnlyList<RnotePointerSample> samples, CanvasMutationRequest request,
        Guid layerId, CanvasRnoteInkStyle? style = null) => DrawStrokeCore(samples,request,layerId,style);

    private Guid DrawStrokeCore(IReadOnlyList<RnotePointerSample> samples, CanvasMutationRequest request, Guid? capturedLayerId, CanvasRnoteInkStyle? style)
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
                LayerId = capturedLayerId ?? page.LayerOrder[0],
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
                        var ranks=ReadPersistedLayerRanks(artifact);
                        if(ranks is not null)
                        {
                            ValidateUserLayerBindings(artifact,_engine,allowLegacySingleLayer:false);
                            candidate.AssignUserLayerRanks([(added[0],ranks[stroke.LayerId])]);
                        }
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

    /// <summary>Materialize a genuine split against exact retained canonical donor state.
    /// Original document/native state and history remain unchanged.</summary>
    public CanvasSplitErasePreview PreviewSplitErase(IReadOnlyList<RnotePointerSample> samples, double width, Guid expectedRevision,
        IReadOnlyDictionary<ulong, Guid>? retainedFragmentIdentities = null)
    {
        var captured = RnoteCanvasEngine.CaptureSamples(samples);
        if (!double.IsFinite(width) || width is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(width));
        lock (_gate)
        {
            EnsureOpen(); var artifact = _session.GetArtifactSnapshot();
            if (expectedRevision == Guid.Empty || artifact.RevisionId != expectedRevision)
                throw new InvalidOperationException("Canvas changed before genuine split preview.");
            var state = ReadPersistedState(artifact);
            var bindings = state.NativeStrokeKeys ?? throw new NotSupportedException("Split needs original canonical/native stroke identity bindings.");
            // View-only camera movement is not a new owning snapshot. Resolve
            // geometry against exact canonical native bytes, not a live view grant.
            using var canonicalNative = RnoteCanvasEngine.Open(Convert.FromBase64String(state.PayloadBase64));
            var candidate = canonicalNative.CreateSplitEraseCandidate(captured, width);
            var materialization = CanvasRnoteSplitMaterializer.Materialize(artifact, bindings, candidate.CopyReceipt(), retainedFragmentIdentities);
            return new(artifact.ArtifactId, artifact.RevisionId, artifact.Pages[0].PageId, captured, width, candidate, materialization);
        }
    }

    /// <summary>One atomic canonical replace/create/delete, one shared history frame.
    /// Exact replay/stale/locked refusals never invoke or adopt the donor callback.</summary>
    public void ApplySplitErase(CanvasSplitErasePreview preview, CanvasMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(preview); ArgumentNullException.ThrowIfNull(request);
        if (request.BaseRevisionId != preview.BaseRevisionId) throw new ArgumentException("Split preview does not bind this mutation revision.");
        lock (_gate)
        {
            EnsureOpen();
            if (_session.ArtifactId != preview.ArtifactId) throw new InvalidOperationException("Split preview belongs to another artifact.");
            var artifact = _session.GetArtifactSnapshot();
            RnoteCanvasEngine? candidate = null;
            try
            {
                CanvasDocumentSettings Capture()
                {
                    candidate = RnoteCanvasEngine.Open(preview.Candidate.CopyNative());
                    if (!candidate.ReadStrokeKeys().ToHashSet().SetEquals(preview.NativeBindings.Values))
                        throw new InvalidDataException("Split candidate identities disagree with the exact canonical materialization.");
                    return SettingsWithEngineState(artifact.DocumentSettings, candidate, CompatibilityReport, preview.NativeBindings);
                }
                var result = _session.ReplaceStructuredStrokes(request, preview.PageId, preview.CopyReplacements(),
                    preview.RemovedStrokeIds, preview.StrokeOrder, Capture);
                RequireSuccess(result);
                if (candidate is not null)
                {
                    var prior = _engine; _engine = candidate; candidate = null; prior.Dispose();
                }
            }
            finally { candidate?.Dispose(); }
        }
    }

    /// <summary>Actual detached donor selector mapped to canonical identities. No document/history adoption.</summary>
    public IReadOnlyList<Guid> PreviewSelection(CanvasSelectionStyle style, IEnumerable<RnotePointerSample> samples, Guid expectedRevision)
    {
        lock (_gate)
        {
            EnsureOpen(); var artifact = _session.GetArtifactSnapshot();
            if (expectedRevision == Guid.Empty || artifact.RevisionId != expectedRevision) throw new InvalidOperationException("Selection targets a stale canonical revision.");
            if (artifact.Pages.Count != 1 || artifact.SharedResources.Count != 0 || artifact.Pages[0].Objects.Count != 0)
                throw new NotSupportedException("This selector requires exact single-page canonical ink bindings.");
            var page = artifact.Pages[0];
            // Reuse selected49's complete persisted canonical/native layer-rank admission.
            // An unrelated empty restricted layer cannot redirect or block original insertion.
            ValidateUserLayerBindings(artifact, _engine, allowLegacySingleLayer: true);
            var bindings = ReadPersistedState(artifact).NativeStrokeKeys;
            if (bindings is null || page.StrokeOrder.Any(id => !bindings.ContainsKey(id)) ||
                !_engine.ReadRenderedStrokeKeys().SequenceEqual(page.StrokeOrder.Select(id => bindings[id])))
                throw new NotSupportedException("Selector canonical/native identity and render order require reconciliation.");
            var reverse = bindings.ToDictionary(pair => pair.Value, pair => pair.Key);
            var keys = _engine.PreviewSelection(style, samples);
            if (keys.Any(key => !reverse.ContainsKey(key))) throw new InvalidDataException("Selected native entity has no canonical identity.");
            var selected = keys.Select(key => reverse[key]).ToArray();
            var strokes = page.Strokes.ToDictionary(stroke => stroke.StrokeId);
            var layers = page.Layers.ToDictionary(layer => layer.LayerId);
            if (selected.Any(id => !strokes.TryGetValue(id, out var stroke) || !layers.TryGetValue(stroke.LayerId, out var layer) ||
                !layer.IsVisible || layer.IsLocked))
                throw new InvalidOperationException("This editable selection cannot target hidden or locked canonical ink.");
            return selected;
        }
    }

    /// <summary>Resolve one genuine topmost hit, without changing history or native bytes.
    /// Unsupported/unbound/locked top hits refuse; they never expose a lower neighbor.</summary>
    public Guid? PreviewQuickErase(double x, double y, Guid expectedRevision)
    {
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            if (artifact.RevisionId != expectedRevision) throw new InvalidOperationException("Canvas changed before Quick erase targeting.");
            if (artifact.Pages.Count != 1 || artifact.Pages[0].Layers.Count != 1 ||
                artifact.SharedResources.Count != 0 || artifact.Pages[0].Objects.Count != 0)
                throw new NotSupportedException("Quick eraser needs an ink-only single-layer native mapping; broader layer/object parity is not established.");
            var bindings = ReadPersistedState(artifact).NativeStrokeKeys;
            var page = artifact.Pages[0];
            if (bindings is null || page.StrokeOrder.Any(id => !bindings.ContainsKey(id)) ||
                !_engine.ReadRenderedStrokeKeys().SequenceEqual(page.StrokeOrder.Select(id => bindings[id])))
                throw new NotSupportedException("Canonical stroke order and retained donor render order need explicit reconciliation before Quick erasing.");
            var key = _engine.FindQuickEraseTarget(x, y);
            if (key is null) return null;
            var matches = bindings?.Where(pair => pair.Value == key.Value).Select(pair => pair.Key).ToArray() ?? [];
            if (matches.Length != 1) throw new NotSupportedException("The topmost donor entity has no unique canonical stroke identity.");
            var stroke = page.Strokes.SingleOrDefault(value => value.StrokeId == matches[0])
                ?? throw new InvalidDataException("The topmost donor identity has no canonical stroke.");
            if (page.Layers.Single(value => value.LayerId == stroke.LayerId).IsLocked)
                throw new InvalidOperationException("The topmost Quick erase target is locked.");
            return stroke.StrokeId;
        }
    }

    /// <summary>Read-only preview on a detached genuine donor candidate. No document/history mutation.</summary>
    public IReadOnlyList<Guid> PreviewWholeStrokeErase(IReadOnlyList<RnotePointerSample> samples, double width, Guid expectedRevision)
    {
        var captured = RnoteCanvasEngine.CaptureSamples(samples);
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            if (artifact.RevisionId != expectedRevision) throw new InvalidOperationException("The Canvas changed before eraser preview.");
            using var candidate = RnoteCanvasEngine.Open(_engine.Save());
            candidate.EraseWholeStrokes(captured, width);
            return Array.AsReadOnly(IdentifyWholeStrokeErasure(artifact,candidate));
        }
    }

    /// <summary>Apply exactly the previewed canonical IDs as one owning revision/history boundary.</summary>
    public void EraseWholeStrokes(IReadOnlyList<RnotePointerSample> samples, double width,
        IReadOnlyList<Guid> expectedStrokeIds, CanvasMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(expectedStrokeIds);
        var captured = RnoteCanvasEngine.CaptureSamples(samples);
        var keys = new List<Guid>();
        foreach (var id in expectedStrokeIds) { keys.Add(id); if(keys.Count==1025) break; }
        if(keys.Count is < 1 or > 1024 || keys.Any(id=>id==Guid.Empty) || keys.Distinct().Count()!=keys.Count)
            throw new ArgumentException("Erasure requires a bounded exact set of previewed canonical stroke IDs.",nameof(expectedStrokeIds));
        keys.Sort();
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot(); var page = artifact.Pages[0];
            RnoteCanvasEngine? candidate = null;
            try
            {
                CanvasDocumentSettings Capture()
                {
                    candidate = RnoteCanvasEngine.Open(_engine.Save());
                    candidate.EraseWholeStrokes(captured,width);
                    var removed = IdentifyWholeStrokeErasure(artifact,candidate);
                    if(!keys.SequenceEqual(removed)) throw new InvalidDataException("The donor erasure differs from its exact canonical preview.");
                    var bindings = new Dictionary<Guid,ulong>(ReadPersistedState(artifact).NativeStrokeKeys!);
                    foreach(var id in keys) bindings.Remove(id);
                    return SettingsWithEngineState(artifact.DocumentSettings,candidate,CompatibilityReport,bindings);
                }
                var result = _session.DeleteStructuredStrokes(request,page.PageId,keys,Capture);
                RequireSuccess(result);
                if(candidate is not null)
                {
                    var prior = _engine; _engine = candidate; candidate = null; prior.Dispose();
                }
            }
            finally { candidate?.Dispose(); }
        }
    }

    private Guid[] IdentifyWholeStrokeErasure(CanvasArtifact artifact,RnoteCanvasEngine candidate)
    {
        var before = _engine.ReadStrokeKeys().ToHashSet(); var after = candidate.ReadStrokeKeys().ToHashSet();
        if(after.Except(before).Any()) throw new NotSupportedException("Split erasure requires canonical reconstruction of genuine new donor paths.");
        var removed = before.Except(after).ToHashSet();
        var bindings = ReadPersistedState(artifact).NativeStrokeKeys ?? new Dictionary<Guid,ulong>();
        var reverse = bindings.ToDictionary(binding=>binding.Value,binding=>binding.Key);
        if(removed.Any(key=>!reverse.ContainsKey(key))) throw new NotSupportedException("Erased imported donor entities require explicit canonical migration.");
        var ids = removed.Select(key=>reverse[key]).Order().ToArray();
        var page = artifact.Pages[0];
        if(ids.Length>1024 || ids.Any(id=>!page.Strokes.Any(stroke=>stroke.StrokeId==id)))
            throw new NotSupportedException("This eraser operation requires bounded same-page canonical ink.");
        if(page.Strokes.Where(stroke=>ids.Contains(stroke.StrokeId)).Any(stroke=>page.Layers.First(layer=>layer.LayerId==stroke.LayerId).IsLocked))
            throw new UnauthorizedAccessException("The eraser cannot modify locked-layer ink.");
        return ids;
    }

    public void DeleteStroke(Guid strokeId, CanvasMutationRequest request) =>
        EditStroke(strokeId, request, delete: true, 0, 0);

    public void TranslateStroke(Guid strokeId, double deltaX, double deltaY, CanvasMutationRequest request) =>
        EditStroke(strokeId, request, delete: false, deltaX, deltaY);

    private void EditStroke(Guid strokeId, CanvasMutationRequest request, bool delete, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            EnsureOpen();
            var artifact = _session.GetArtifactSnapshot();
            var page = artifact.Pages[0];
            if (!delete && page.Strokes.Any(stroke => stroke.StrokeId == strokeId && stroke.PathGeometry is not null))
                throw new NotSupportedException("Exact donor path translation requires an authoritative path replacement transaction.");
            RnoteCanvasEngine? candidate = null;
            try
            {
                CanvasDocumentSettings Capture()
                {
                    var bindings = new Dictionary<Guid, ulong>(ReadPersistedState(artifact).NativeStrokeKeys ?? new Dictionary<Guid, ulong>());
                    if (!bindings.TryGetValue(strokeId, out var key))
                        throw new NotSupportedException("The canonical stroke has no retained native identity; an explicit migration is required.");
                    candidate = RnoteCanvasEngine.Open(_engine.Save());
                    var priorKeys = candidate.ReadStrokeKeys().ToHashSet();
                    if (delete)
                    {
                        candidate.DeleteStroke(key);
                        bindings.Remove(strokeId);
                        priorKeys.Remove(key);
                    }
                    else candidate.TranslateStroke(key, deltaX, deltaY);
                    if (!priorKeys.SetEquals(candidate.ReadStrokeKeys()))
                        throw new InvalidDataException("The donor edit changed unrelated native entity identities.");
                    return SettingsWithEngineState(artifact.DocumentSettings, candidate, CompatibilityReport, bindings);
                }
                var result = delete
                    ? _session.DeleteStructuredStroke(request, page.PageId, strokeId, Capture)
                    : _session.TranslateStructuredStroke(request, page.PageId, strokeId, deltaX, deltaY, Capture);
                RequireSuccess(result);
                if (candidate is not null)
                {
                    var prior = _engine;
                    _engine = candidate;
                    candidate = null;
                    prior.Dispose();
                }
            }
            finally { candidate?.Dispose(); }
        }
    }

    /// <summary>Owning canonical/native user-layer creation. The exact new LayerID
    /// must be captured by the caller before review. This low-level document API
    /// is not a Home/Files capability or persistence acknowledgement.</summary>
    public void SetNativeUserLayerVisibility(Guid pageId, Guid layerId, bool isVisible, CanvasMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            EnsureOpen();
            if (!_engine.SupportsVisibleKeysRender) throw new NotSupportedException("The maintained donor does not expose visible-layer rendering.");
            ApplyNativeUserLayerMutation(capture=>_session.SetLayerVisibilityWithDonor(request,pageId,layerId,isVisible,capture));
        }
    }

    public void SetNativeUserLayerLocked(Guid pageId, Guid layerId, bool isLocked, CanvasMutationRequest request) =>
        ApplyNativeUserLayerMutation(capture=>_session.SetLayerLockedWithDonor(request,pageId,layerId,isLocked,capture));

    public void MoveNativeStrokeToUserLayer(Guid pageId, Guid strokeId, Guid destinationLayerId, CanvasMutationRequest request) =>
        ApplyNativeUserLayerMutation(capture => _session.MoveStrokeToLayerWithDonor(request, pageId, strokeId, destinationLayerId, capture));

    public void RenameNativeUserLayer(Guid pageId, Guid layerId, string name, CanvasMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            EnsureOpen();
            ValidateUserLayerBindings(_session.GetArtifactSnapshot(), _engine, allowLegacySingleLayer: true);
            RequireSuccess(_session.RenameLayer(request, pageId, layerId, name));
        }
    }

    public void CreateNativeUserLayer(Guid pageId, Guid newLayerId, string name, int? insertAt, CanvasMutationRequest request) =>
        ApplyNativeUserLayerMutation(capture => _session.CreateLayerWithDonor(request, pageId, newLayerId, name, insertAt, capture));

    public void ReorderNativeUserLayer(Guid pageId, Guid layerId, int toIndex, CanvasMutationRequest request) =>
        ApplyNativeUserLayerMutation(capture => _session.ReorderLayerWithDonor(request, pageId, layerId, toIndex, capture));

    public void DeleteNativeUserLayer(Guid pageId, Guid layerId, CanvasMutationRequest request) =>
        ApplyNativeUserLayerMutation(capture => _session.DeleteLayerWithDonor(request,pageId,layerId,capture),allowDeletedInk:true);

    private void ApplyNativeUserLayerMutation(Func<Func<CanvasArtifact, CanvasDocumentSettings>, CanvasApiResult<CanvasMutationResult>> apply,
        bool allowDeletedInk=false)
    {
        lock (_gate)
        {
            EnsureOpen();var original = _session.GetArtifactSnapshot();
            // Authenticate the original stored canonical/native mapping before preparing
            // a candidate. Never adopt ranks inferred from a replacement provider/view.
            ValidateUserLayerBindings(original, _engine, allowLegacySingleLayer: true);
            var state = ReadPersistedState(original);
            var bindings = state.NativeStrokeKeys ?? new Dictionary<Guid, ulong>();
            RnoteCanvasEngine? candidate = null;
            try
            {
                CanvasDocumentSettings Capture(CanvasArtifact proposed)
                {
                    if (proposed.ArtifactId != original.ArtifactId || proposed.Pages.Count != 1
                        || proposed.Pages[0].PageId != original.Pages[0].PageId)
                        throw new InvalidDataException("Layer proposal changed its original canonical document.");
                    var page = proposed.Pages[0];
                    var ranks = page.LayerOrder.Select((id,index) => (id,rank: checked((uint)index))).ToDictionary(value=>value.id,value=>value.rank);
                    candidate = RnoteCanvasEngine.Open(_engine.Save());
                    var retainedBindings=new Dictionary<Guid,ulong>(bindings);
                    var proposedStrokeIds=page.Strokes.Select(stroke=>stroke.StrokeId).ToHashSet();
                    var removedIds=retainedBindings.Keys.Where(id=>!proposedStrokeIds.Contains(id)).ToArray();
                    if(removedIds.Length!=0 && !allowDeletedInk)
                        throw new InvalidDataException("A non-delete layer transaction removed original canonical ink.");
                    foreach(var id in removedIds)
                    {
                        candidate.DeleteStroke(retainedBindings[id]);
                        retainedBindings.Remove(id);
                    }
                    if(!candidate.ReadStrokeKeys().ToHashSet().SetEquals(retainedBindings.Values))
                        throw new InvalidDataException("Layer deletion changed unrelated native entity identities.");
                    var assignments = page.Strokes.Select(stroke => (Key: retainedBindings[stroke.StrokeId], Rank: ranks[stroke.LayerId])).ToArray();
                    if (assignments.Length != 0) candidate.AssignUserLayerRanks(assignments);
                    var settings = SettingsWithEngineState(proposed.DocumentSettings, candidate, CompatibilityReport, retainedBindings, ranks);
                    proposed.DocumentSettings = settings;
                    ValidateUserLayerBindings(proposed, candidate, allowLegacySingleLayer: false);
                    return settings;
                }
                RequireSuccess(apply(Capture));
                if (candidate is not null)
                {
                    var prior = _engine;_engine = candidate;candidate = null;prior.Dispose();
                }
            }
            finally { candidate?.Dispose(); }
        }
    }

    private static IReadOnlyDictionary<Guid,uint>? ReadPersistedLayerRanks(CanvasArtifact artifact) =>
        artifact.DocumentSettings.Properties.TryGetValue(StateKey,out var value) ? value.Deserialize<RnotePersistedState>()?.NativeLayerRanks : null;

    private static void ValidateUserLayerBindings(CanvasArtifact artifact, RnoteCanvasEngine engine, bool allowLegacySingleLayer)
    {
        if (artifact.Pages.Count != 1) throw new NotSupportedException("The maintained user-layer adapter currently requires one canonical page.");
        if (!engine.SupportsUserLayerRanks) throw new NotSupportedException("The maintained donor has no complete user-layer rank adapter.");
        var page = artifact.Pages[0];var persisted = ReadPersistedLayerRanks(artifact);
        if (persisted is null && (!allowLegacySingleLayer || page.LayerOrder.Count != 1))
            throw new NotSupportedException("This document has no authenticated original canonical-to-native layer mapping.");
        var ranks = persisted ?? new Dictionary<Guid,uint> { [page.LayerOrder[0]] = 0 };
        if (ranks.Count != page.LayerOrder.Count || page.LayerOrder.Where((id,index) => !ranks.TryGetValue(id,out var rank) || rank != (uint)index).Any())
            throw new InvalidDataException("Canonical layer order disagrees with its retained native rank mapping.");
        var bindings = ReadPersistedState(artifact).NativeStrokeKeys ?? new Dictionary<Guid,ulong>();
        if (bindings.Count != page.Strokes.Count || page.Strokes.Any(stroke=>!bindings.ContainsKey(stroke.StrokeId))
            || !bindings.Values.ToHashSet().SetEquals(engine.ReadStrokeKeys()))
            throw new NotSupportedException("The document requires an exact original native binding for every user-layer entity.");
        if (page.Strokes.Count == 0) return;
        var keys = page.Strokes.Select(stroke=>bindings[stroke.StrokeId]).ToArray();
        var actual = engine.ReadUserLayerRanks(keys);
        for (var index=0;index<page.Strokes.Count;index++)
            if (actual[index] != ranks[page.Strokes[index].LayerId]) throw new InvalidDataException("Retained donor entity rank disagrees with its original canonical LayerID.");
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
            RnoteCanvasEngine? candidate = null;
            try
            {
                void Prepare(CanvasArtifact proposed)
                {
                    // Validate donor version, checksum and canonical native-key bindings
                    // before the session changes either current content or its stacks.
                    using var validated = Open(CanvasArtifactCodec.Serialize(proposed));
                    var state = ReadPersistedState(proposed);
                    candidate = RnoteCanvasEngine.Open(Convert.FromBase64String(state.PayloadBase64));
                }
                var result = undo ? _session.Undo(request, Prepare) : _session.Redo(request, Prepare);
                RequireSuccess(result);
                if (candidate is not null)
                {
                    var prior = _engine;
                    _engine = candidate;
                    candidate = null;
                    prior.Dispose();
                }
            }
            finally { candidate?.Dispose(); }
        }
    }

    public RnoteRenderFrame Render()
    {
        lock (_gate)
        {
            EnsureOpen();
            var revision = _session.CurrentRevisionId;
            if (_renderedFrame is null || _renderedRevision != revision)
            {
                var artifact = _session.GetArtifactSnapshot();
                // Render the committed donor representation. Rnote rounds its
                // generated pen paths during serialization; showing the live
                // higher-precision path would visibly change after reopen.
                // Keep the active engine/history intact and cache by revision.
                var state = ReadPersistedState(artifact);
                using var durable = RnoteCanvasEngine.Open(Convert.FromBase64String(state.PayloadBase64));
                if (state.NativeLayerRanks is null) _renderedFrame = durable.Render();
                else
                {
                    ValidateUserLayerBindings(artifact, durable, allowLegacySingleLayer: false);
                    var page = artifact.Pages[0];
                    var visibleLayers = page.Layers.Where(layer => layer.IsVisible).Select(layer => layer.LayerId).ToHashSet();
                    var bindings = state.NativeStrokeKeys ?? new Dictionary<Guid, ulong>();
                    _renderedFrame = durable.RenderVisibleKeys(page.Strokes.Where(stroke => visibleLayers.Contains(stroke.LayerId)).Select(stroke => bindings[stroke.StrokeId]));
                }
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
        IReadOnlyDictionary<Guid, ulong>? nativeStrokeKeys = null, IReadOnlyDictionary<Guid,uint>? nativeLayerRanks = null)
    {
        var bytes = engine.Save();
        var retained = nativeStrokeKeys ?? (settings.Properties.TryGetValue(StateKey, out var prior)
            ? prior.Deserialize<RnotePersistedState>()?.NativeStrokeKeys : null);
        var retainedLayers = nativeLayerRanks ?? (settings.Properties.TryGetValue(StateKey, out var priorLayers)
            ? priorLayers.Deserialize<RnotePersistedState>()?.NativeLayerRanks : null);
        if (retainedLayers is not null && retained is not null && retained.Count != 0)
        {
            var actualRanks = engine.ReadUserLayerRanks(retained.Values);
            if (actualRanks.Any(rank => !retainedLayers.Values.Contains(rank))) throw new InvalidDataException("Donor entity rank is outside the retained canonical layer map.");
        }
        var payload = new RnotePersistedState(1, DonorRevision, Convert.ToBase64String(bytes), Convert.ToHexString(SHA256.HashData(bytes)), report,
            retained is null ? null : new Dictionary<Guid, ulong>(retained),
            retainedLayers is null ? null : new Dictionary<Guid,uint>(retainedLayers));
        var properties = new Dictionary<string, JsonElement>(settings.Properties, StringComparer.Ordinal) { [StateKey] = JsonSerializer.SerializeToElement(payload) };
        return settings with { Properties = properties };
    }

    private static void ValidateNativeStrokeBindings(CanvasArtifact artifact, RnoteCanvasEngine engine)
    {
        if (!artifact.DocumentSettings.Properties.TryGetValue(StateKey, out var state)) return;
        var bindings = state.Deserialize<RnotePersistedState>()?.NativeStrokeKeys;
        if (bindings is null || bindings.Count == 0)
        {
            if (artifact.Pages.SelectMany(page => page.Strokes).Any(stroke => stroke.PathGeometry is not null))
                throw new InvalidDataException("Authoritative paths require retained native identity.");
            return;
        }
        if (bindings.Count > 1_000_000 || bindings.Keys.Any(id => id == Guid.Empty) || bindings.Values.Distinct().Count() != bindings.Count)
            throw new InvalidDataException("Canvas native stroke bindings have invalid or duplicate identity.");
        var canonical = artifact.Pages.SelectMany(page => page.Strokes).Select(stroke => stroke.StrokeId).ToHashSet();
        if (bindings.Keys.Any(id => !canonical.Contains(id)))
            throw new InvalidDataException("Canvas native stroke binding targets an absent canonical stroke.");
        var native = engine.ReadStrokeKeys().ToHashSet();
        if (bindings.Values.Any(key => !native.Contains(key)))
            throw new InvalidDataException("Canvas native stroke binding targets an absent persisted native entity.");
        foreach (var stroke in artifact.Pages.SelectMany(page => page.Strokes).Where(stroke => stroke.PathGeometry is not null))
        {
            if (!bindings.TryGetValue(stroke.StrokeId, out var key))
                throw new InvalidDataException("Authoritative path has no retained native identity.");
            var exported = engine.ExportSelectedStrokes([key]);
            using var compressed = new MemoryStream(exported, writable: false);
            using var zip = new GZipStream(compressed, CompressionMode.Decompress);
            using var expanded = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = zip.Read(buffer)) != 0)
            {
                if (expanded.Length + count > 256L * 1024 * 1024)
                    throw new InvalidDataException("Authoritative path native expansion exceeds its bound.");
                expanded.Write(buffer, 0, count);
            }
            expanded.Position = 0;
            using var document = JsonDocument.Parse(expanded);
            var brushes = document.RootElement.GetProperty("data").GetProperty("engine_snapshot")
                .GetProperty("stroke_components").EnumerateArray()
                .Select(slot => slot.GetProperty("value"))
                .Where(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("brushstroke", out _)).ToArray();
            if (brushes.Length != 1)
                throw new InvalidDataException("Authoritative path export must contain exactly its owning brush stroke.");
            long segmentBudget = 8192;
            var genuine = CanvasRnoteSplitMaterializer.DecodePath(brushes[0].GetProperty("brushstroke").GetProperty("path"), ref segmentBudget);
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(genuine), JsonSerializer.SerializeToElement(stroke.PathGeometry)))
                throw new InvalidDataException("Canonical authoritative path disagrees with its retained donor geometry.");
        }
    }

    private sealed record RnotePersistedState(int SchemaVersion, string DonorRevision, string PayloadBase64, string Sha256,
        CanvasImportCompatibilityReport CompatibilityReport, IReadOnlyDictionary<Guid, ulong>? NativeStrokeKeys = null,
        IReadOnlyDictionary<Guid,uint>? NativeLayerRanks = null);
}
