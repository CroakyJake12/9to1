using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

public sealed class CloudflareTypedToolTests
{
    [Theory]
    [InlineData("code")]
    [InlineData("account_id")]
    [InlineData("access_token")]
    public async Task Model_cannot_supply_code_account_or_credentials(string field)
    {
        var (_, service) = await Service();
        var call = Call("cloudflare_kv_list", (field, JsonSerializer.SerializeToElement("injected")));
        Assert.Throws<ArgumentException>(() => CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), call));
    }
    [Theory]
    [InlineData("cloudflare_workers_deploy")]
    [InlineData("cloudflare_dns_write")]
    [InlineData("cloudflare_secret_write_by_reference")]
    public async Task Unconnected_high_impact_owners_are_refused_before_transport(string name)
    {
        var (_, service) = await Service();
        Assert.Throws<NotSupportedException>(() => CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Call(name)));
    }
    [Fact]
    public async Task Put_binds_nonpersonal_Task_Run_marker_and_missing_raw_GET_transport_refuses()
    {
        var (_, service) = await Service(); var task = Guid.NewGuid(); var run = Guid.NewGuid();
        var ns = ("namespace_id", JsonSerializer.SerializeToElement(new string('a', 32)));
        var put = CloudflareTypedToolCatalogue.CompileOriginal(service, task, run, Guid.NewGuid(), Call("cloudflare_kv_put_task_marker", ns));
        Assert.Throws<CloudflareSetupRequiredException>(() => CloudflareTypedToolCatalogue.CompileOriginal(service, task, run, Guid.NewGuid(), Call("cloudflare_kv_get_task_marker", ns)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.ToString("N") + ":" + run.ToString("N")))).ToLowerInvariant();
        var expected = JsonSerializer.Serialize(JsonSerializer.Serialize(new { taskId = task, executionId = run, taskRunDigest = hash }));
        Assert.Contains(expected, put.Code); Assert.Contains("contentType:'application/octet-stream',rawBody:true", put.Code);
        Assert.Contains("expiration_ttl:120", put.Code); Assert.Equal(task.ToString("N") + ":" + run.ToString("N") + ":" + put.ActionId.ToString("N"), put.OperationKey);
    }
    [Fact]
    public async Task Isolated_create_has_actual_exact_title_absence_preflight_and_one_POST()
    {
        var (_, service) = await Service(); var task = Guid.NewGuid();
        var call = CloudflareTypedToolCatalogue.CompileOriginal(service, task, Guid.NewGuid(), Guid.NewGuid(), Call("cloudflare_kv_create_isolated"));
        Assert.Contains("before.result.some", call.Code); Assert.Contains(CloudflareTypedToolCatalogue.OriginalCompleteInventoryExpression, call.Code);
        Assert.Contains("9to1-safety-net-" + task.ToString("N"), call.Code);
        Assert.Equal(1, call.Code.Split("method:\"POST\"").Length - 1);
    }
    [Fact]
    public async Task Response_cannot_substitute_operation_or_reflect_foreign_marker_values()
    {
        var (_, service) = await Service();
        var original = CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Call("cloudflare_kv_inspect", ("namespace_id", JsonSerializer.SerializeToElement(new string('a', 32)))));
        var wrong = JsonSerializer.SerializeToElement(new { operation_key = "wrong", ok = true, status = 200, data = new { matches = true } });
        Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, wrong));
        var foreign = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 200, data = new { matches = true, secret = "must-not-egress" } });
        Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, foreign));
    }
    [Fact]
    public async Task Saved_service_requires_SAME_private_issuer_and_current_saved_configuration()
    {
        var (source, original) = await Service();
        var repo = new Connections(original.Connection);
        var foreign = new CloudflareSavedOAuthServiceSource(repo, original.Connection.Id, new Uri("https://synthetic.invalid/mcp"), original.AccountId, "execute");
        var copy = await foreign.AcquireOriginalAsync(default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.RevalidateOriginalAsync(copy, default));
        repo.Current = repo.Current! with { IsEnabled = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.RevalidateOriginalAsync(copy, default));
    }
    [Fact]
    public async Task Connection_without_OAuth_cannot_become_a_credential_reference()
    {
        var connection = Connection() with { ConfigurationJson = JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://synthetic.invalid/mcp", UseOAuth: false)) };
        var source = new CloudflareSavedOAuthServiceSource(new Connections(connection), connection.Id, new Uri("https://synthetic.invalid/mcp"), new string('b', 32), "execute");
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.AcquireOriginalAsync(default));
    }
    [Fact]
    public async Task Faulted_OCE_and_independent_sibling_remain_faults_with_actual_task_identity()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var oce = new OperationCanceledException("synthetic original fault"); var sibling = new IOException("synthetic sibling");
        var actual = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); actual.SetException(new Exception[] { oce, sibling });
        var wrapper = ledger.AwaitAsync(actual.Task);
        var error = await Assert.ThrowsAsync<AggregateException>(() => wrapper);
        Assert.True(wrapper.IsFaulted); Assert.Same(oce, error.InnerExceptions[0]); Assert.Same(sibling, error.InnerExceptions[1]);
        Assert.Contains(ledger.OriginalTasks, x => ReferenceEquals(x, actual.Task));
        Assert.Contains(ledger.OriginalErrors, x => ReferenceEquals(x, sibling));
    }
    [Fact]
    public async Task Genuinely_canceled_actual_task_is_retained_separately()
    {
        var ledger = new CloudflareOriginalTaskLedger(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var actual = Task.FromCanceled<int>(stop.Token); var wrapper = ledger.AwaitAsync(actual);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper);
        Assert.True(actual.IsCanceled); Assert.True(wrapper.IsCanceled); Assert.Same(actual, Assert.Single(ledger.OriginalTasks));
    }
    private static OllamaToolCall Call(string name, params (string Name, JsonElement Value)[] arguments) =>
        new(name, arguments.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal));
    private static ExternalConnection Connection() => new(Guid.NewGuid(), "Synthetic CF", "synthetic-cf", ExternalConnectionKind.Mcp, "synthetic", true,
        ExternalConnectionState.Ready, "synthetic", JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://synthetic.invalid/mcp", UseOAuth: true)),
        null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static async Task<(CloudflareSavedOAuthServiceSource, CloudflareSavedService)> Service()
    {
        var connection = Connection(); var source = new CloudflareSavedOAuthServiceSource(new Connections(connection), connection.Id, new Uri("https://synthetic.invalid/mcp"), new string('b', 32), "execute");
        return (source, await source.AcquireOriginalAsync(default));
    }
    private sealed class Connections(ExternalConnection current) : IExternalConnectionRepository
    {
        public ExternalConnection? Current = current;
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Current?.Id == id ? Current : null);
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ExternalConnection>>(Current is null ? [] : [Current]);
        public Task UpsertAsync(ExternalConnection connection, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
