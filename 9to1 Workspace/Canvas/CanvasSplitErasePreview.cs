using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
namespace HavenOS.Apps.Canvas;

/// <summary>Immutable preview data. Authorities and actors are never stored here.</summary>
public sealed class CanvasSplitErasePreview
{
    private readonly byte[] _replacements;
    private readonly ImmutableArray<Guid> _removed;
    private readonly ImmutableArray<Guid> _order;
    private readonly IReadOnlyDictionary<Guid, ulong> _bindings;
    internal CanvasSplitErasePreview(Guid artifactId, Guid revision, Guid pageId,
        ImmutableArray<RnotePointerSample> samples, double width, RnoteSplitCandidate candidate,
        CanvasRnoteSplitMaterialization materialization)
    {
        ArtifactId = artifactId; BaseRevisionId = revision; PageId = pageId;
        Samples = samples; Width = width; Candidate = candidate;
        _replacements = JsonSerializer.SerializeToUtf8Bytes(materialization.Replacements, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _removed = materialization.RemovedStrokeIds.ToImmutableArray(); _order = materialization.StrokeOrder.ToImmutableArray();
        _bindings = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, ulong>(new Dictionary<Guid, ulong>(materialization.NativeBindings));
        FragmentIdentities = new System.Collections.ObjectModel.ReadOnlyDictionary<ulong, Guid>(new Dictionary<ulong, Guid>(materialization.FragmentIdentities));
    }
    public Guid ArtifactId { get; }
    public Guid BaseRevisionId { get; }
    public Guid PageId { get; }
    public ImmutableArray<RnotePointerSample> Samples { get; }
    public double Width { get; }
    public string NativeHash => Candidate.NativeHash;
    public string ReceiptHash => Candidate.ReceiptHash;
    public IReadOnlyDictionary<ulong, Guid> FragmentIdentities { get; }
    public ImmutableArray<Guid> RemovedStrokeIds => _removed;
    public ImmutableArray<Guid> StrokeOrder => _order;
    internal RnoteSplitCandidate Candidate { get; }
    internal IReadOnlyDictionary<Guid, ulong> NativeBindings => _bindings;
    internal CanvasInkStroke[] CopyReplacements() => JsonSerializer.Deserialize<CanvasInkStroke[]>(_replacements,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Captured split replacement data is absent.");
}
