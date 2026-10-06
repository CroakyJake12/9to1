using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace Haven.Infrastructure;

public sealed partial class McpConnectionClient
{
    private readonly ConditionalWeakTable<CloudflareMcpDispatchResult, CloudflareCompiledInvocation> _cloudflareResults = new();
    public bool IsIssuedOriginalResult(CloudflareCompiledInvocation original, CloudflareMcpDispatchResult result) =>
        _cloudflareResults.TryGetValue(result, out var recorded) && ReferenceEquals(recorded, original);

    public async Task<CloudflareMcpDispatchResult> InvokeOriginalAsync(CloudflareOriginalTaskBinding original,
        ICloudflareSavedServiceSource sameServices, ITaskRunOriginalActionAdmissionSource sameActionClaims,
        ICloudflareOriginalPermission permission, CancellationToken token)
    {
        var invocation = original.Invocation;
        if (!CloudflareTypedToolCatalogue.IsIssuedOriginal(invocation)) throw new UnauthorizedAccessException("Actual compiled Cloudflare invocation required.");
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(original.OriginalCallerCallback);
        var connection = invocation.Service.Connection;
        var configuration = ReadConfiguration(connection);
        var gate = configuration.SerializeInvocations ? GetGate(connection.Id) : null;
        var enteredGate = false;
        var dispatchStarted = false;
        McpClient? client = null;
        ICloudflareOriginalFinalDispatch? entry = null;
        IAsyncDisposable? attemptPin = null;
        Task<CallToolResult>? actualCall = null;
        JsonElement? response = null;
        try
        {
            if (permission is ICloudflareOriginalBorrowedBindingPermission borrowed)
                await stages.AwaitAsync(stages.Invoke(() => borrowed.RevalidateOriginalBorrowedBindingAsync(original, this, token))).ConfigureAwait(false);
            if (gate is not null) { await stages.AwaitAsync(gate.WaitAsync(token)).ConfigureAwait(false); enteredGate = true; }
            await stages.AwaitAsync(stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(sameServices, invocation.Service, original.OriginalCallerCallback, token))).ConfigureAwait(false);
            client = await stages.CaptureOriginalAcquisitionAsync(() => CreateClientAsync(connection, configuration, token, stages, actual => client = actual), actual => client = actual).ConfigureAwait(false);
            var tools = await stages.AwaitAsync(stages.Invoke(() => client.ListToolsAsync(cancellationToken: token))).ConfigureAwait(false);
            var tool = tools.SingleOrDefault(x => x.Name == invocation.Service.ExecuteTool) ?? throw new InvalidOperationException("The configured fixed API service tool is unavailable.");
            // A host binding selects this exact Code Mode transport. No discovered description/annotation is authority.
            if (!tool.JsonSchema.TryGetProperty("properties", out var schemaProperties) ||
                !schemaProperties.TryGetProperty("code", out var codeSchema) || !codeSchema.TryGetProperty("type", out var codeType) || codeType.GetString() != "string" ||
                !schemaProperties.TryGetProperty("account_id", out var accountSchema) || !accountSchema.TryGetProperty("type", out var accountType) || accountType.GetString() != "string")
                throw new InvalidOperationException("The exact configured account-scoped API transport schema is unsupported.");
            await stages.AwaitAsync(stages.Invoke(() => CloudflareCallerScopedServiceRead.RevalidateOriginalAsync(sameServices, invocation.Service, original.OriginalCallerCallback, token))).ConfigureAwait(false);
            await stages.AwaitAsync(stages.Invoke(() => sameActionClaims.ValidateOriginalActionAdmissionAsync(original.ActionAdmission, original.OriginalPreparation, original.OriginalAttempt, token))).ConfigureAwait(false);
            await stages.AwaitAsync(stages.Invoke(() => original.OriginalAttempt.Lease.RevalidateAsync(token))).ConfigureAwait(false);
            await stages.AwaitAsync(stages.Invoke(() => permission.RevalidateOriginalAsync(original, token))).ConfigureAwait(false);
            if (original.OriginalAttempt.Lease is not ITaskRunAdmissionCommitLease pinnable) throw new UnauthorizedAccessException("Original attempt commit lifetime pin required.");
            // All context/policy reads precede the pure attempt pin. The final Home owner captures/holds its entry first.
            entry = await stages.CaptureOriginalAcquisitionAsync(() => permission.EnterOriginalFinalDispatchAsync(invocation, token), actual => entry = actual).ConfigureAwait(false);
            attemptPin = await stages.CaptureOriginalAcquisitionAsync(() => pinnable.AcquireOriginalCommitPinAsync(token), actual => attemptPin = actual).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Original attempt is retiring.");
            stages.Invoke(() => { permission.DemandOriginalFinalDispatch(entry, invocation, token); return true; });
            var values = new Dictionary<string, object?>(StringComparer.Ordinal)
            { ["account_id"] = invocation.Service.AccountId, ["code"] = invocation.Code };
            actualCall = stages.Invoke(() => entry.RunOriginalDispatch(invocation, () =>
            {
                token.ThrowIfCancellationRequested();
                sameActionClaims.DemandOriginalActionAdmission(original.ActionAdmission, original.OriginalPreparation, original.OriginalAttempt);
                dispatchStarted = true;
                actualCall = tool.CallAsync(values, cancellationToken: token);
                return stages.Track(actualCall);
            }, token));
        }
        catch (Exception error) { stages.Retain(error); }
        finally
        {
            // Join both actual closes independently, in reverse acquisition order, before any SDK network wait.
            if (attemptPin is not null)
                try { await stages.ObserveOriginalCloseAsync(attemptPin.DisposeAsync).ConfigureAwait(false); }
                catch (Exception error) { stages.Retain(error); }
            if (entry is not null)
                try { await stages.ObserveOriginalCloseAsync(entry.DisposeAsync).ConfigureAwait(false); }
                catch (Exception error) { stages.Retain(error); }
        }
        if (actualCall is not null)
        {
            try
            {
                var returned = await stages.AwaitAsync(actualCall).ConfigureAwait(false);
                if (returned.IsError is true) throw new InvalidOperationException("The typed service response is unconfirmed; no automatic retry.");
                JsonElement envelope;
                if (returned.StructuredContent is { } structured) envelope = structured.Clone();
                else
                {
                    var text = string.Join("\n", returned.Content.OfType<TextContentBlock>().Select(x => x.Text));
                    if (text.Length > 128_000) throw new InvalidOperationException("Typed Cloudflare response exceeded its bound.");
                    using var document = JsonDocument.Parse(text); envelope = document.RootElement.Clone();
                }
                // Drop all raw content/error strings. Only the closed compiled response may cross to the model/UI.
                _ = CloudflareTypedToolCatalogue.DemandBoundedResponse(invocation, envelope); response = envelope;
            }
            catch (Exception error) { stages.Retain(error); }
        }
        await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (client is not null)
            try { await stages.ObserveOriginalCloseAsync(client.DisposeAsync).ConfigureAwait(false); }
            catch (Exception error) { stages.Retain(error); }
        await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (enteredGate)
            try { gate!.Release(); } catch (Exception error) { stages.Retain(error); }
        var result = new CloudflareMcpDispatchResult(response, dispatchStarted, stages.OriginalTasks, stages.OriginalErrors);
        _cloudflareResults.Add(result, invocation); return result;
    }
}
