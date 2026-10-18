using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Exercises the ACTUAL narrow provider-discovery classification. Only a real
/// transport connectivity failure or a concrete non-success HTTP status is an expected
/// provider condition; invariant/parse faults and cancellation are never availability.</summary>
public sealed class ModelProviderDiscoveryAvailabilityTests
{
    [Fact]
    public void Actual_transport_connectivity_failure_is_expected_provider_unavailability()
    {
        var refused = new HttpRequestException("Connection refused (127.0.0.1:11434)",
            new SocketException((int)SocketError.ConnectionRefused));
        Assert.True(ModelProviderDiscoveryAvailability.IsExpectedUnavailable(refused));
        // The exact instance is preserved as the reported cause; classification copies nothing.
        Assert.Same(refused,
            ModelProviderDiscoveryAvailability.IsExpectedUnavailable(refused) ? refused : null);
    }

    [Fact]
    public void Nested_transport_cause_is_still_expected_provider_unavailability()
    {
        var nested = new HttpRequestException("wrapped transport failure",
            new IOException("transport wrapper", new SocketException((int)SocketError.HostUnreachable)));
        Assert.True(ModelProviderDiscoveryAvailability.IsExpectedUnavailable(nested));
    }

    [Fact]
    public void Non_success_http_status_is_expected_provider_unavailability()
    {
        var status = new HttpRequestException("Ollama returned 503: unavailable", null,
            HttpStatusCode.ServiceUnavailable);
        Assert.True(ModelProviderDiscoveryAvailability.IsExpectedUnavailable(status));
    }

    [Fact]
    public void Opaque_http_request_exception_without_actual_transport_evidence_is_not_availability()
    {
        var opaque = new HttpRequestException("vetoed by policy before any transport settled");
        Assert.False(ModelProviderDiscoveryAvailability.IsExpectedUnavailable(opaque));
    }

    [Theory]
    [MemberData(nameof(UnexpectedCauses))]
    public void Unexpected_invariant_parse_and_cancellation_faults_are_never_availability(Exception cause)
    {
        Assert.False(ModelProviderDiscoveryAvailability.IsExpectedUnavailable(cause));
    }

    public static TheoryData<Exception> UnexpectedCauses() => new()
    {
        new InvalidOperationException("original-owner/provenance invariant broken"),
        new IOException("bare IO failure is not waived by category"),
        new JsonException("discovery payload shape was invalid"),
        new OperationCanceledException("cancellation is not availability"),
        new TaskCanceledException("timeout/cancel is not availability"),
        new ArgumentException("programmer fault"),
        new AggregateException("unexpected group", new InvalidOperationException("inner invariant")),
    };
}
