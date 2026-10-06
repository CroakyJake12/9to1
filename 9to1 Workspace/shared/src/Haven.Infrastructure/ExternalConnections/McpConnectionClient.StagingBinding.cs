using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace Haven.Infrastructure;

public sealed partial class McpConnectionClient : ICloudflareWorkerBindingClient
{
    private sealed record OriginalBinding(CloudflareSavedService Service, CloudflareStagingBindingSelection Selection);
    private readonly ConditionalWeakTable<CloudflareWorkerBindingObservation, OriginalBinding> _originalWorkerBindings = new();
    public bool IsIssuedOriginalWorkerBinding(CloudflareSavedService service, CloudflareStagingBindingSelection selection,
        CloudflareWorkerBindingObservation observation) => _originalWorkerBindings.TryGetValue(observation, out var original) &&
        ReferenceEquals(original.Service, service) && ReferenceEquals(original.Selection, selection) &&
        observation.OriginalErrors.Count == 0 && observation.OriginalTasks.All(x => x.IsCompletedSuccessfully);
    public async Task<CloudflareWorkerBindingObservation> ReadOriginalWorkerBindingAsync(CloudflareSavedService service,
        CloudflareStagingBindingSelection selection, ICloudflareOriginalWorkerReadAuthority authority,
        Action<Action>? callerScope, CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(callerScope);
        var code = CloudflareStagingBindingContract.CompileOriginal(service, selection);
        var config = ReadConfiguration(service.Connection); var gate = config.SerializeInvocations ? GetGate(service.Connection.Id) : null;
        bool heldGate = false; ICloudflareOriginalWorkerReadEntry? entry = null; McpClient? client = null; Task<CallToolResult>? call = null; JsonElement? projection = null;
        try
        {
            if (gate is not null) { await stages.AwaitAsync(gate.WaitAsync(token)).ConfigureAwait(false); heldGate = true; }
            client = await stages.CaptureOriginalAcquisitionAsync(() => CreateClientAsync(service.Connection, config, token, stages, value => client = value), value => client = value).ConfigureAwait(false);
            var tools = await stages.AwaitAsync(stages.Invoke(() => client.ListToolsAsync(cancellationToken: token))).ConfigureAwait(false);
            var tool = tools.SingleOrDefault(x => x.Name == service.ExecuteTool) ?? throw new NotSupportedException("The exact saved execute tool is unavailable.");
            if (!tool.JsonSchema.TryGetProperty("properties", out var props) || !props.TryGetProperty("code", out var codeProp) ||
                codeProp.GetProperty("type").GetString() != "string" || !props.TryGetProperty("account_id", out var accountProp) || accountProp.GetProperty("type").GetString() != "string")
                throw new NotSupportedException("Unsupported official account-scoped execute schema.");
            entry = await stages.CaptureOriginalAcquisitionAsync(() => authority.EnterOriginalWorkerReadAsync(service, selection, token), value => entry = value).ConfigureAwait(false);
            stages.Invoke(() => { authority.DemandOriginalWorkerReadEntry(service, selection, entry); return true; });
            stages.Invoke(() => entry.RunOriginalRead(() =>
            {
                token.ThrowIfCancellationRequested(); authority.DemandOriginalWorkerReadEntry(service, selection, entry);
                call = tool.CallAsync(new Dictionary<string, object?> { ["account_id"] = service.AccountId, ["code"] = code }, cancellationToken: token);
                return stages.Track(call);
            }, token));
        }
        catch (Exception cause) { stages.Retain(cause); }
        finally
        {
            // Actual Home entry/completion close before the network await; SDK retains exact read Task separately.
            if (entry is not null) try { await stages.ObserveOriginalCloseAsync(entry.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { stages.Retain(cause); }
        }
        if (call is not null)
            try
            {
                var returned = await stages.AwaitAsync(call).ConfigureAwait(false);
                if (returned.IsError is true) throw new InvalidOperationException("The original fixed Worker binding read failed.");
                JsonElement originalProjection;
                if (returned.StructuredContent is { } structured) originalProjection = structured.Clone();
                else
                {
                    var text = string.Join("\n", returned.Content.OfType<TextContentBlock>().Select(x => x.Text));
                    if (text.Length > 2_000) throw new InvalidDataException("Worker binding projection exceeded its bound.");
                    using var parsed = JsonDocument.Parse(text); originalProjection = parsed.RootElement.Clone();
                }
                projection = CloudflareStagingBindingContract.DetachValidatedOriginalProjection(originalProjection, selection);
            }
            catch (Exception cause) { stages.Retain(cause); }
        await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (client is not null) try { await stages.ObserveOriginalCloseAsync(client.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { stages.Retain(cause); }
        if (heldGate) try { gate!.Release(); } catch (Exception cause) { stages.Retain(cause); }
        await stages.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        if (projection is null && stages.OriginalErrors.Count == 0) stages.Retain(new InvalidOperationException("No actual successful Worker binding response was observed."));
        var observed = new CloudflareWorkerBindingObservation(stages.OriginalErrors.Count == 0 ? CloudflareStagingBindingContract.DemandNamespace(projection!.Value, selection) : "",
            stages.OriginalErrors.Count == 0 ? projection!.Value : JsonSerializer.SerializeToElement(new { }), stages.OriginalTasks, stages.OriginalErrors);
        _originalWorkerBindings.Add(observed, new(service, selection)); return observed;
    }
}
