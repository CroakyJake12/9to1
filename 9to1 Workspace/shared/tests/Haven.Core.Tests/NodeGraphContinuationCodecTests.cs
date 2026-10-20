using System.Text;
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

        AssertContinuationEquivalent(env, back);
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
        Assert.Contains("Unsupported continuation protocol version", error ?? string.Empty);
    }

    [Fact]
    public void Oversized_envelope_exceeding_limit_is_rejected_fail_closed()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var jsonOverLimit = "{\"pad\":\"" + new string(' ', GraphRuntimeContinuationCodec.MaximumSerializedBytes - 9) + "\"}";
        Assert.True(Encoding.UTF8.GetByteCount(jsonOverLimit) > GraphRuntimeContinuationCodec.MaximumSerializedBytes);
        var ok = codec.TryDeserialize(jsonOverLimit, out _, out var error);
        Assert.False(ok);
        Assert.Contains("maximum encoded size", error ?? string.Empty);
    }

    [Fact]
    public void Exact_boundary_at_limit_is_not_size_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var overhead = Encoding.UTF8.GetByteCount("{\"pad\":\"\"}"); // 10
        var innerSpaces = GraphRuntimeContinuationCodec.MaximumSerializedBytes - overhead;
        var jsonAtLimit = "{\"pad\":\"" + new string(' ', innerSpaces) + "\"}";
        Assert.Equal(GraphRuntimeContinuationCodec.MaximumSerializedBytes, Encoding.UTF8.GetByteCount(jsonAtLimit));

        // At exactly the limit, the payload is parsed; it fails for structural reasons, NOT the size guard.
        var ok = codec.TryDeserialize(jsonAtLimit, out _, out var error);
        Assert.False(ok);
        Assert.DoesNotContain("maximum encoded size", error ?? string.Empty);
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

    [Fact]
    public void Duplicate_fields_are_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(Envelope(default, default));
        var duplicated = json.Replace("\"protocolVersion\":", "\"protocolVersion\":1,\"protocolVersion\":");
        Assert.False(codec.TryDeserialize(duplicated, out _, out var error));
        Assert.Contains("duplicate fields", error ?? string.Empty);
    }

    [Fact]
    public void Unknown_kind_enum_is_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(Envelope(default, default)).Replace("\"kind\":\"Wait\"", "\"kind\":\"Banana\"");
        Assert.False(codec.TryDeserialize(json, out _, out _));
    }

    [Fact]
    public void Invalid_date_is_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(Envelope(default, default)).Replace("\"resumeAt\":\"2026-10-20T12:00:00.0000000+00:00\"", "\"resumeAt\":\"not-a-date\"");
        Assert.False(codec.TryDeserialize(json, out _, out _));
    }

    [Fact]
    public void Malformed_typed_state_is_rejected()
    {
        var codec = new GraphRuntimeContinuationCodec();
        var json = codec.Serialize(Envelope(default, default)).Replace("\"ownerState\":{\"frontier\":[1,2]}", "\"ownerState\":[1,2]");
        Assert.False(codec.TryDeserialize(json, out _, out var error));
        Assert.Contains("ownerState", error ?? string.Empty);
    }

    private static void AssertContinuationEquivalent(GraphRuntimeContinuation expected, GraphRuntimeContinuation actual)
    {
        Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
        Assert.Equal(expected.GraphType, actual.GraphType);
        Assert.Equal(expected.OwnerRunId, actual.OwnerRunId);
        Assert.Equal(expected.GraphId, actual.GraphId);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.CapabilityProfileId, actual.CapabilityProfileId);
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.StartNodeId, actual.StartNodeId);
        Assert.Equal(expected.EventType, actual.EventType);
        Assert.Equal(expected.SessionId, actual.SessionId);
        Assert.Equal(expected.EntityId, actual.EntityId);
        Assert.Equal(expected.ParticipantId, actual.ParticipantId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.NodeId, actual.NodeId);
        Assert.Equal(expected.ResumeAt, actual.ResumeAt);
        Assert.Equal(expected.StepsConsumed, actual.StepsConsumed);
        Assert.Equal(expected.StepLimit, actual.StepLimit);
        Assert.Equal(expected.Digest, actual.Digest);
        Assert.Equal(Canonical(expected.Condition), Canonical(actual.Condition));
        Assert.Equal(Canonical(expected.OwnerState), Canonical(actual.OwnerState));
    }

    private static string Canonical(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? "<absent>" : element.GetRawText();

    private static GraphRuntimeContinuation Envelope(JsonElement condition, JsonElement ownerState) =>
        new(GraphRuntimeContinuation.CurrentProtocolVersion, "TestGraph", "run-1", Guid.NewGuid(), 1, "profile1", 1,
            Guid.NewGuid(), "TestEvent", "session1", "entity1", "participant1", GraphContinuationKind.Wait,
            Guid.NewGuid(), FixedAt, condition, ownerState, 3, 1000,
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
}
