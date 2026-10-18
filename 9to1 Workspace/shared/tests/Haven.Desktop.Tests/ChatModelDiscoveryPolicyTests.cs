using System.Net;
using System.Net.Sockets;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Exercises the ACTUAL discovery classification the production page delegates
/// to: a terminal settlement is degraded only when EVERY direct cause is an expected
/// provider condition, otherwise the COMPLETE payload stays blocking with exact identity.</summary>
public sealed class ChatModelDiscoveryPolicyTests
{
    private static HttpRequestException Refused() =>
        new("Connection refused (127.0.0.1:11434)", new SocketException((int)SocketError.ConnectionRefused));

    [Fact]
    public void Sole_expected_transport_cause_is_a_degraded_observation_with_exact_identity()
    {
        var http = Refused();
        var decision = ChatModelDiscoveryPolicy.Classify([http]);
        Assert.True(decision.Degraded);
        Assert.False(decision.Blocking);
        Assert.Same(http, decision.Cause);
        Assert.Empty(decision.Causes);
    }

    [Fact]
    public void Several_expected_causes_stay_exact_inside_one_degraded_aggregate()
    {
        var first = Refused();
        var second = new HttpRequestException("Ollama returned 502", null, HttpStatusCode.BadGateway);
        var decision = ChatModelDiscoveryPolicy.Classify([first, second]);
        Assert.True(decision.Degraded);
        Assert.False(decision.Blocking);
        var aggregate = Assert.IsType<AggregateException>(decision.Cause);
        Assert.Contains(first, aggregate.InnerExceptions);
        Assert.Contains(second, aggregate.InnerExceptions);
    }

    [Fact]
    public void Mixed_payload_keeps_every_direct_cause_blocking_with_exact_identities()
    {
        var expected = Refused();
        var invariant = new InvalidOperationException("original-owner invariant broken");
        var decision = ChatModelDiscoveryPolicy.Classify([expected, invariant]);
        Assert.False(decision.Degraded);
        Assert.True(decision.Blocking);
        // The complete payload is exposed; the invariant sibling is not erased by the
        // expected transport cause and the expected cause is not dropped either.
        Assert.Equal(2, decision.Causes.Count);
        Assert.Same(expected, decision.Causes[0]);
        Assert.Same(invariant, decision.Causes[1]);
        Assert.Null(decision.Cause);
    }

    [Fact]
    public void Unexpected_only_payload_keeps_every_direct_cause_blocking()
    {
        var parse = new System.Text.Json.JsonException("invalid discovery payload");
        var cleanup = new IOException("cleanup failure");
        var decision = ChatModelDiscoveryPolicy.Classify([parse, cleanup]);
        Assert.False(decision.Degraded);
        Assert.True(decision.Blocking);
        Assert.Equal(2, decision.Causes.Count);
        Assert.Same(parse, decision.Causes[0]);
        Assert.Same(cleanup, decision.Causes[1]);
    }

    [Fact]
    public void Clean_settlement_never_reaches_classification_with_an_empty_payload()
    {
        Assert.Throws<InvalidOperationException>(() => ChatModelDiscoveryPolicy.Classify([]));
    }
}
