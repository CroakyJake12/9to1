using Haven.Core;
namespace Haven.Application;
public interface IOllamaClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken);
    Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("This model provider does not support model installation."));
    Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("This model provider does not support model removal."));
}

/// <summary>
/// A structured delta from a streaming chat response that can contain
/// content and/or thinking tokens.
/// </summary>
public sealed record ChatDelta(string? Content = null, string? Thinking = null);

/// <summary>
/// Represents generation options and keeps its related state and behavior together.
/// </summary>
public sealed record GenerationOptions(double Temperature = 0.7, int ContextLimit = 32768, int ActionLimit = 24);

/// <summary>
/// Represents ollama chat request and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    EffortLevel Effort,
    string? SystemPrompt = null,
    bool EnableTools = false,
    GenerationOptions? Options = null);

/// <summary>
/// Represents ollama message and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaMessage(string Role, string Content, IReadOnlyList<string>? Images = null);

/// <summary>
/// Represents ollama tool definition and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, object> Properties,
    IReadOnlyList<string> Required,
    System.Text.Json.JsonElement? InputSchema = null);

/// <summary>
/// Represents ollama tool call and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaToolCall(
    string Name,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Arguments,
    string? Id = null);

/// <summary>
/// Represents ollama tool turn and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaToolTurn(
    string Role,
    string Content,
    IReadOnlyList<OllamaToolCall>? ToolCalls = null,
    string? ToolName = null,
    IReadOnlyList<string>? Images = null);

/// <summary>
/// Represents ollama tool request and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaToolRequest(
    string Model,
    IReadOnlyList<OllamaToolTurn> Messages,
    IReadOnlyList<OllamaToolDefinition> Tools,
    EffortLevel Effort,
    string? SystemPrompt = null,
    GenerationOptions? Options = null);

/// <summary>
/// Represents ollama tool response and keeps its related state and behavior together.
/// </summary>
public sealed record OllamaToolResponse(string Content, IReadOnlyList<OllamaToolCall> ToolCalls);

