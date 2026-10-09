using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

public sealed class CloudflareTaskMarkerAbsenceTests
{
    private static CloudflareSavedService Service()
    {
        var connection = new ExternalConnection(Guid.NewGuid(), "Controlled official transport", "controlled", ExternalConnectionKind.Mcp,
            "controlled", true, ExternalConnectionState.Ready, "controlled",
            JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, CloudflareRawTaskMarkerTransport.SupportedEndpoint, UseOAuth: true)),
            null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return CloudflareSavedService.ObserveSavedConfiguration(connection, new('b', 32), "execute");
    }
    private static CloudflareCompiledInvocation Compile(string name) => CloudflareTypedToolCatalogue.CompileOriginal(Service(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(name, new Dictionary<string, JsonElement> { ["namespace_id"] = JsonSerializer.SerializeToElement(new string('a', 32)) }));
    [Fact]
    public void Separate_absence_read_requires_exact_compiled_namespace_and_actual_404()
    {
        var original = Compile("cloudflare_kv_verify_task_marker_absent");
        Assert.True(original.Descriptor.IsReadOnly);
        var exact = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 404, data = new { absent = true, namespace_id = original.NamespaceId } });
        Assert.True(CloudflareTypedToolCatalogue.DemandBoundedResponse(original, exact).GetProperty("absent").GetBoolean());
        foreach (var status in new[] { 200, 403, 429, 500 })
        {
            var wrong = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status, data = new { absent = true, namespace_id = original.NamespaceId } });
            Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, wrong));
        }
        var foreign = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 404, data = new { absent = true, namespace_id = new string('c', 32) } });
        Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, foreign));
        var extra = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 404, data = new { absent = true, namespace_id = original.NamespaceId, secret = "must-not-egress" } });
        Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, extra));
    }
    [Fact]
    public void Original_marker_read_and_delete_do_not_accept_a_generic_404()
    {
        foreach (var name in new[] { "cloudflare_kv_get_task_marker", "cloudflare_kv_delete_task_marker", "cloudflare_kv_inspect" })
        {
            var original = Compile(name);
            var absent = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 404, data = new { absent = true, namespace_id = original.NamespaceId } });
            Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, absent));
        }
    }
}
