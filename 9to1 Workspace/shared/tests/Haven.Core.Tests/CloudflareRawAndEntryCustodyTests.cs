using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;
namespace Haven.Core.Tests;

public sealed class CloudflareRawAndEntryCustodyTests
{
    [Fact] public async Task Actual_body_group_and_each_independent_partial_close_survive()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var first = new OperationCanceledException("faulted entry");
        var sibling = new IOException("actual entry sibling"); var home = new IOException("actual held Home close"); var completion = new IOException("actual completion close");
        var body = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); body.SetException([first, sibling]);
        var homeClose = Task.FromException(home); var completionClose = Task.FromException(completion);
        var original = CloudflareOriginalPartialEntryCustody.RunOriginalAsync(ledger, () => body.Task,
            () => new Func<ValueTask>[] { () => new(homeClose), () => new(completionClose) });
        var error = await Assert.ThrowsAsync<AggregateException>(() => original);
        Assert.True(original.IsFaulted); Assert.True(body.Task.IsFaulted);
        foreach (var exact in new Exception[] { first, sibling, home, completion }) { Assert.Contains(exact, Leaves(error)); Assert.Contains(exact, ledger.OriginalErrors); }
        foreach (var exact in new Task[] { body.Task, homeClose, completionClose }) Assert.Contains(ledger.OriginalTasks, task => ReferenceEquals(task, exact));
    }
    [Fact] public async Task Synchronous_OCE_after_real_token_cancel_is_fault_and_still_closes_both_entries()
    {
        var ledger = new CloudflareOriginalTaskLedger(); using var stop = new CancellationTokenSource(); var actual = new OperationCanceledException("synchronous producer cause", stop.Token);
        var homeClose = Task.CompletedTask; var completionClose = Task.CompletedTask; int homeCalls = 0, completionCalls = 0;
        var original = CloudflareOriginalPartialEntryCustody.RunOriginalAsync<int>(ledger, () => { stop.Cancel(); throw actual; },
            () => new Func<ValueTask>[] { () => { homeCalls++; return new(homeClose); }, () => { completionCalls++; return new(completionClose); } });
        var error = await Assert.ThrowsAsync<AggregateException>(() => original);
        Assert.True(original.IsFaulted); Assert.Contains(actual, Leaves(error)); Assert.Contains(actual, ledger.OriginalErrors);
        Assert.Equal(1, homeCalls); Assert.Equal(1, completionCalls);
    }
    [Fact] public async Task Actual_held_cleanup_keeps_whole_original_pending_and_is_retained_once()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var body = Task.FromResult(3);
        var original = CloudflareOriginalPartialEntryCustody.RunOriginalAsync(ledger, () => body,
            () => new Func<ValueTask>[] { () => { entered.SetResult(); return new(close.Task); } });
        try { await entered.Task; Assert.False(original.IsCompleted); Assert.Contains(ledger.OriginalTasks, task => ReferenceEquals(task, close.Task)); }
        finally { close.TrySetResult(); await original; }
        Assert.Equal(3, await original); Assert.Equal(1, ledger.OriginalTasks.Count(task => ReferenceEquals(task, close.Task)));
    }
    [Fact] public void Raw_compile_is_fixed_official_endpoint_and_exact_Task_Run_bytes_only()
    {
        var original = Get(); Assert.Contains("fetch(\"https://api.cloudflare.com/client/v4/accounts/", original.Code);
        Assert.Contains("redirect:'error'", original.Code); Assert.Contains("response.body.getReader()", original.Code);
        Assert.DoesNotContain("response.text", original.Code); Assert.DoesNotContain("access_token", original.Code);
        Assert.DoesNotContain("cloudflare.request", original.Code); Assert.Contains(JsonSerializer.Serialize(ExpectedMarker(original)), original.Code);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Raw_result_requires_match_exact_length_and_exact_SHA_with_no_foreign_fields(int kind)
    {
        var original = Get(); var bytes = Encoding.UTF8.GetBytes(ExpectedMarker(original)); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        object data = kind switch { 0 => new { matches = false, length = bytes.Length, sha256 = hash },
            1 => new { matches = true, length = bytes.Length + 3, sha256 = hash },
            2 => new { matches = true, length = bytes.Length, sha256 = new string('0', 64) },
            _ => new { matches = true, length = bytes.Length, sha256 = hash, foreign = "never accepted" } };
        var response = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 200, data });
        Assert.Throws<InvalidOperationException>(() => CloudflareTypedToolCatalogue.DemandBoundedResponse(original, response));
    }
    [Fact] public void Exact_generated_marker_digest_is_confirmed_without_returning_raw_bytes()
    {
        var original = Get(); var bytes = Encoding.UTF8.GetBytes(ExpectedMarker(original));
        var response = JsonSerializer.SerializeToElement(new { operation_key = original.OperationKey, ok = true, status = 200,
            data = new { matches = true, length = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() } });
        var data = CloudflareTypedToolCatalogue.DemandBoundedResponse(original, response); Assert.Equal(3, data.EnumerateObject().Count()); Assert.True(data.GetProperty("matches").GetBoolean());
    }
    // Independently construct the declared marker format from the SAME public Task/Run
    // observations; the production internal marker and issuing authority stay private.
    private static string ExpectedMarker(CloudflareCompiledInvocation original)
    {
        var taskId = original.TaskId; var executionId = original.ExecutionId;
        var taskRunDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(taskId.ToString("N") + ":" + executionId.ToString("N")))).ToLowerInvariant();
        return JsonSerializer.Serialize(new { taskId, executionId, taskRunDigest });
    }
    private static CloudflareCompiledInvocation Get()
    {
        var connection = new ExternalConnection(Guid.NewGuid(), "synthetic official fixture", "mcp", ExternalConnectionKind.Mcp, "cloudflare", true,
            ExternalConnectionState.Ready, "synthetic", JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, CloudflareRawTaskMarkerTransport.SupportedEndpoint, UseOAuth: true)),
            null, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var service = CloudflareSavedService.ObserveSavedConfiguration(connection, new string('a', 32), "execute");
        return CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new("cloudflare_kv_get_task_marker", new Dictionary<string, JsonElement> { ["namespace_id"] = JsonSerializer.SerializeToElement(new string('b', 32)) }));
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
