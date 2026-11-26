using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Dev.Autonomous;

namespace Haven.Desktop.Services;

/// <summary>Catalogue and inference through the same supervised device-local Dulche owner.</summary>
internal sealed class DulcheSocketModelProvider(DulcheLocalClient client) : IModelProvider
{
    internal const string ProviderId = "dulche-local";
    private static readonly IReadOnlySet<ToolCapability> TextCapabilities =
        new[] { ToolCapability.Text, ToolCapability.Streaming }.ToFrozenSet();

    public string Id => ProviderId;
    public string DisplayName => "Dulche on this device";
    public ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    public bool IsLocal => true;
    public bool CanManageModels => false;

    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var raw = await client.GetAsync("status", cancellationToken).ConfigureAwait(false) as JsonObject ?? new JsonObject();
        var status = DulcheUnixSocketGateway.ParseStatus(raw);
        if (status.StopAll || !string.Equals(status.Phase, "ready", StringComparison.Ordinal)) return [];
        var rows = raw["models"] as JsonArray ?? new JsonArray();
        // Unloaded entries require an explicit admission promise from the current daemon.
        // Older daemons remain usable for loaded text models without pretending they can cold-start.
        return status.Models.Where(model => model.IsLoaded || model.CanStart == true)
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Select(model =>
            {
                var row = rows.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.ToString() == model.Id);
                var caps = ReadCapabilities(row?["capabilities"]);
                return new ProviderModelDescriptor(Id, IsLocal,
                    new(model.Id, 0, DisplayName, "", "", caps, DateTimeOffset.UnixEpoch),
                    model.ContextLength > 0 ? model.ContextLength : null, model.Name);
            }).ToArray();
    }

    private static IReadOnlySet<ToolCapability> ReadCapabilities(JsonNode? value)
    {
        if (value is not JsonArray list) return TextCapabilities;
        var result = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming };
        foreach (var item in list)
        {
            var cap = item?.ToString().ToLowerInvariant();
            if (cap is "tools" or "tool_calls") result.Add(ToolCapability.Tools);
            if (cap is "vision" or "images") result.Add(ToolCapability.Vision);
            if (cap is "structured_output") result.Add(ToolCapability.StructuredOutput);
            if (cap is "usage") result.Add(ToolCapability.UsageReporting);
        }
        return result.ToFrozenSet();
    }

    private async Task<IReadOnlySet<ToolCapability>> RequireCapabilityAsync(
        string model, ToolCapability capability, CancellationToken token)
    {
        // Keep this action's snapshot local so a concurrent catalogue refresh cannot
        // clear or replace its capabilities halfway through rich-request validation.
        var models = await GetModelsAsync(token).ConfigureAwait(false);
        var caps = models.SingleOrDefault(item => item.Name == model)?.Capabilities;
        if (caps is null || !caps.Contains(capability))
            throw new NotSupportedException($"Dulche does not currently advertise {capability} for {model}.");
        return caps;
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var ready = (await GetModelsAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
            return new(Id, ready, ready ? "Dulche has models available for inference." : "Dulche has no eligible model.",
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            return new(Id, false, "The local Dulche status is unavailable.", Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        if (request.EnableTools || !string.IsNullOrWhiteSpace(request.ReasoningId) ||
            request.Messages.Any(message => message.Role is not ("system" or "user" or "assistant")))
            throw new NotSupportedException("Use the typed Dulche tool route; explicit named reasoning is not advertised by this route.");
        if (request.Messages.Any(message => message.Images is { Count: > 0 }))
            await RequireCapabilityAsync(request.Model, ToolCapability.Vision, cancellationToken).ConfigureAwait(false);

        var messages = OpenAiCompatibleModelProviderBase.BuildToolMessages(
            request.Messages.Select(message => new OllamaToolTurn(message.Role, message.Content, Images: message.Images)).ToArray(),
            request.SystemPrompt);
        await foreach (var piece in client.StreamLocalModelAsync(new
        {
            model = request.Model, messages, stream = true,
            specialist_id = request.Options?.SpecialistId?.ToString("D"),
            temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2),
        }, cancellationToken).ConfigureAwait(false)) yield return piece;
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        await foreach (var delta in StreamChatAsync(request, cancellationToken).ConfigureAwait(false)) result.Append(delta);
        return result.ToString();
    }

    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        if (!string.IsNullOrWhiteSpace(request.ReasoningId))
            throw new NotSupportedException("Dulche has not advertised this explicit named reasoning selection.");
        var capabilities = await RequireCapabilityAsync(request.Model, ToolCapability.Tools, cancellationToken).ConfigureAwait(false);
        if (request.Messages.Any(message => message.Images is { Count: > 0 }) &&
            !capabilities.Contains(ToolCapability.Vision))
            throw new NotSupportedException("Dulche has not advertised vision for the selected tool model.");
        var calls = new SortedDictionary<int, (StringBuilder Id, StringBuilder Name, StringBuilder Arguments)>();
        string? finish = null;
        void Observe(JsonNode frame)
        {
            var choice = frame["choices"]?.AsArray().FirstOrDefault();
            finish = choice?["finish_reason"]?.GetValue<string>() ?? finish;
            if (choice?["delta"]?["tool_calls"] is not JsonArray deltas) return;
            foreach (var delta in deltas)
            {
                var index = delta?["index"]?.GetValue<int>() ?? -1;
                if (index is < 0 or >= 64) throw new InvalidDataException("Dulche returned an invalid tool-call index.");
                if (!calls.TryGetValue(index, out var call))
                    calls[index] = call = (new StringBuilder(), new StringBuilder(), new StringBuilder());
                var id = delta?["id"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(id))
                {
                    if (call.Id.Length != 0 && call.Id.ToString() != id)
                        throw new InvalidDataException("Dulche changed an issued tool-call identity.");
                    if (call.Id.Length == 0) call.Id.Append(id);
                }
                call.Name.Append(delta?["function"]?["name"]?.GetValue<string>());
                call.Arguments.Append(delta?["function"]?["arguments"]?.GetValue<string>());
                if (call.Name.Length > 256 || call.Arguments.Length > 4 * 1024 * 1024)
                    throw new InvalidDataException("Dulche returned an oversized tool call.");
            }
        }
        var content = new StringBuilder();
        var payload = new
        {
            model = request.Model,
            specialist_id = request.Options?.SpecialistId?.ToString("D"),
            messages = OpenAiCompatibleModelProviderBase.BuildToolMessages(request.Messages, request.SystemPrompt),
            tools = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name, description = tool.Description,
                    parameters = tool.InputSchema is { } schema ? (object)schema : new Dictionary<string, object>
                    { ["type"] = "object", ["properties"] = tool.Properties, ["required"] = tool.Required }
                }
            }).ToArray(),
            tool_choice = "auto", stream = true,
            temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2),
        };
        await foreach (var delta in client.StreamLocalToolModelAsync(payload, Observe, cancellationToken).ConfigureAwait(false))
            content.Append(delta);
        if ((calls.Count > 0) != (finish == "tool_calls"))
            throw new InvalidDataException("Dulche's tool response and terminal reason disagree.");
        var parsed = new List<OllamaToolCall>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, call) in calls)
        {
            var id = call.Id.ToString();
            var name = call.Name.ToString();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || !request.Tools.Any(tool => tool.Name == name))
                throw new InvalidDataException("Dulche returned an ambiguous or unrequested tool call.");
            using var arguments = JsonDocument.Parse(call.Arguments.ToString());
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Dulche tool arguments must be a complete JSON object.");
            parsed.Add(new(name, arguments.RootElement.EnumerateObject().ToDictionary(
                item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal), id));
        }
        return new(content.ToString(), parsed);
    }
}
