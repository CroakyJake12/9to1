using System.Buffers;
using System.Text.Json;

namespace Haven.Application.NodeGraph;

/// <summary>Explicit owning-app admission budget, not an execution grant or a new global graph size policy.
/// Depth cannot exceed the existing shared JSON schema validator's depth32 ceiling.</summary>
public sealed record GraphConfigurationCaptureLimits(int MaximumTotalConfigurationBytes, int MaximumDepth);
internal sealed class GraphConfigurationCapture
{
    private int _remaining;
    private readonly int _depth;
    public GraphConfigurationCapture(GraphConfigurationCaptureLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaximumTotalConfigurationBytes is < 1 or > int.MaxValue - 4096 || limits.MaximumDepth is < 1 or > 32)
            throw new ArgumentException("Explicit bounded configuration admission required.", nameof(limits));
        _remaining = limits.MaximumTotalConfigurationBytes; _depth = limits.MaximumDepth;
    }
    public JsonElement Capture(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || _remaining == 0) throw new ArgumentException("Graph configuration admission exceeded.");
        var buffer = new BoundedBuffer(_remaining);
        try
        {
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = _depth }))
            { value.WriteTo(writer); writer.Flush(); }
            _remaining -= buffer.WrittenCount;
            using var document = JsonDocument.Parse(buffer.WrittenMemory, new JsonDocumentOptions { MaxDepth = _depth });
            return document.RootElement.Clone(); // Only AFTER actual byte/depth admission.
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { throw new ArgumentException("Graph configuration admission failed.", nameof(value), error); }
    }
    private sealed class BoundedBuffer(int limit) : IBufferWriter<byte>
    {
        private byte[] _bytes = [];
        public int WrittenCount { get; private set; }
        public ReadOnlyMemory<byte> WrittenMemory => _bytes.AsMemory(0, WrittenCount);
        public void Advance(int count)
        {
            if (count < 0 || count > _bytes.Length - WrittenCount || count > limit - WrittenCount)
                throw new ArgumentException("Configuration byte admission exceeded.");
            WrittenCount += count;
        }
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return _bytes.AsMemory(WrittenCount); }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return _bytes.AsSpan(WrittenCount); }
        private void Ensure(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentException("Invalid writer request.");
            sizeHint = Math.Max(sizeHint, 1);
            // A fixed writer-buffer allowance accommodates Utf8JsonWriter minimum hints without
            // permitting an unbounded scalar-token allocation. Advance still enforces exact bytes.
            var maximumAllocation = checked(limit + 4096);
            if (sizeHint > maximumAllocation - WrittenCount) throw new ArgumentException("Configuration allocation admission exceeded.");
            var required = checked(WrittenCount + sizeHint);
            if (required <= _bytes.Length) return;
            var capacity = (int)Math.Min(maximumAllocation, Math.Max(required, Math.Max(256L, (long)_bytes.Length * 2)));
            Array.Resize(ref _bytes, capacity);
        }
    }
}
