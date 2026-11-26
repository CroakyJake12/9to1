using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>The read-only catalogue and text inference port for the existing device-local Dulche socket.</summary>
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
        var status = await client.GetStructuredStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.StopAll || !string.Equals(status.Phase, "ready", StringComparison.Ordinal)) return [];

        // The service admits generation only for loaded entries. Its status does not attest which
        // loaded model can produce function tool calls, images, or a named reasoning level.
        return status.Models.Where(model => string.Equals(model.State, "loaded", StringComparison.Ordinal))
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Select(model => new ProviderModelDescriptor(Id, IsLocal,
                new(model.Id, 0, DisplayName, "", "", TextCapabilities, DateTimeOffset.UnixEpoch),
                model.ContextLength > 0 ? model.ContextLength : null, model.Name))
            .ToArray();
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var ready = (await GetModelsAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
            return new(Id, ready, ready ? "Dulche has loaded models." : "Dulche has no ready loaded model.",
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or OperationCanceledException)
        {
            return new(Id, false, "The local Dulche status is unavailable.", Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        if (request.EnableTools || !string.IsNullOrWhiteSpace(request.ReasoningId) ||
            request.Messages.Any(message => message.Images is { Count: > 0 } ||
                message.Role is not ("system" or "user" or "assistant")))
            throw new NotSupportedException("This Dulche route supports ordinary text chat.");

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.AddRange(request.Messages.Select(message => new { role = message.Role, content = message.Content }));
        return client.StreamLocalModelAsync(new
        {
            model = request.Model,
            messages,
            stream = true,
            temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2),
        }, cancellationToken);
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        await foreach (var delta in StreamChatAsync(request, cancellationToken).ConfigureAwait(false))
            result.Append(delta);
        return result.ToString();
    }

    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
        Task.FromException<OllamaToolResponse>(new NotSupportedException(
            "Dulche status does not confirm function tool support for the selected model."));
}
