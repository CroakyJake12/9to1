using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Core.Tests;

public sealed class NodeGraphContinuationCodecTests
{
    private static readonly DateTimeOffset FixedAt = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Roundtrip_preserves_false_null_absent_redacted_and_error_distinctions()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var nullCondition = JsonDocument.Parse("null").RootElement;
        var richState = JsonDocument.Parse(
            "{\"enabled\":false,\"value\":null,\"redacted\":\"<redacted>\",\"error\":{\"code\":\"E1\"}}").RootElement;

        var env = Envelope(nullCondition, richState);
        var json = codec.Serialize(env);
        var back = codec.Deserialize(json);

        Assert.Equal(env, back);
        Assert.Equal(JsonValueKind.Null, back.Condition.ValueKind); // present null preserved as null, not coerced
        var state = back.OwnerState;
        Assert.Equal(JsonValueKind.False, state.GetProperty("enabled").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("value").ValueKind);
        Assert.Equal("<redacted>", state.GetProperty("redacted").GetString());
        Assert.Equal("E1", state.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Absent_elements_stay_absent_and_are_not_coerced()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var env = Envelope(default, default); // both Undefined
        var json = codec.Serialize(env);
        Assert.DoesNotContain("\"condition\"", json);
        Assert.DoesNotContain("\"ownerState\"", json);

        var back = codec.Deserialize(json);
        Assert.Equal(JsonValueKind.Undefined, back.Condition.ValueKind);
        Assert.Equal(JsonValueKind.Undefined, back.OwnerState.ValueKind);
    }

    [Fact]
    public void Unsupported_version_is_rejected_fail_closed()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(Envelope(default, default) with { ProtocolVersion = 999 });
        var ok = codec.TryDeserialize(json, out _, out var error);
        Assert.False(ok);
        Assert.Contains("Unsupported continuation protocol version", error);
    }

    [Fact]
    public void Oversized_envelope_is_rejected_fail_closed()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var oversized = new string('z', GraphRuntimeContinuationCodec.MaximumSerializedBytes);
        var ok = codec.TryDeserialize(oversized, out _, out var error);
        Assert.False(ok);
        Assert.Contains("maximum encoded size", error);
    }

    [Fact]
    public void Corrupt_envelope_is_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        Assert.False(codec.TryDeserialize("{not json", out _, out _));
    }

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        Assert.False(codec.TryDeserialize("{\"protocolVersion\":1}", out _, out _));
    }

    private static GraphRuntimeContinuation Envelope(JsonElement condition, JsonElement ownerState) =>
        new(GraphRuntimeContinuation.CurrentProtocolVersion, "TestGraph", "run-1", Guid.NewGuid(), 1, "profile1", 1,
            Guid.NewGuid(), "TestEvent", "session1", "entity1", "participant1", GraphContinuationKind.Wait,
            Guid.NewGuid(), FixedAt, condition, ownerState, 3, 1000,
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
}
