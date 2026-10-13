using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>The cloud workspace's Ollama is hosted, not device-local to the browser.
/// Keep that identity through catalogue and policy; unwrap only at wire dispatch.</summary>
internal sealed class BrowserHostedOllamaClient(IOllamaClient actual, Action demandCurrent, BrowserPrivacyPreferenceStore privacy) : IOllamaClient
{
    private const string Prefix = "hosted-ollama:";
    private async Task<bool> RemoteAllowedAsync(CancellationToken token)
    {
        demandCurrent();
        if (RuntimeSafetyState.IsSafeMode) return false;
        var current = await privacy.RefreshAsync(token); demandCurrent();
        return !RuntimeSafetyState.IsSafeMode && !current.LocalOnlyMode;
    }
    private void DemandOutputPolicy()
    {
        demandCurrent();
        if (RuntimeSafetyState.IsSafeMode || privacy.Current.LocalOnlyMode)
            throw new UnauthorizedAccessException("Hosted model output was stopped by the current privacy policy.");
    }
    public async Task<bool> IsAvailableAsync(CancellationToken token) =>
        await RemoteAllowedAsync(token) && await actual.IsAvailableAsync(token);
    public async Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token)
    {
        if (!await RemoteAllowedAsync(token)) return [];
        var rows = await actual.GetModelsAsync(token);
        if (!await RemoteAllowedAsync(token)) return [];
        return rows.Select(row => row with { Name = Prefix + row.Name }).ToArray();
    }
    private async Task<string> DemandAsync(string model, GenerationOptions? options, ProviderExecutionContext? context, CancellationToken token)
    {
        if (!await RemoteAllowedAsync(token)) throw new UnauthorizedAccessException("Hosted models are disabled by the current safe-mode or local-only privacy policy.");
        if (context is not null) throw new InvalidOperationException("This browser Chat connection does not admit canonical Tasks.");
        if (!model.StartsWith(Prefix, StringComparison.Ordinal) || model.Length == Prefix.Length)
            throw new InvalidOperationException("Select an actual hosted model from this Chat catalogue.");
        if (options?.RequestedRoutingConstraints is not { AllowCloud: true, AllowFallback: false })
            throw new InvalidOperationException("The hosted model requires this request's explicit cloud route with fallback disabled.");
        return model[Prefix.Length..];
    }
    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var model = await DemandAsync(request.Model, request.Options, request.ExecutionContext, token);
        await foreach (var chunk in actual.StreamChatAsync(request with { Model = model }, token).WithCancellation(token))
        { DemandOutputPolicy(); yield return chunk; }
    }
    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
    {
        var model = await DemandAsync(request.Model, request.Options, request.ExecutionContext, token);
        var result = await actual.CompleteAsync(request with { Model = model }, token);
        DemandOutputPolicy();
        return result;
    }
    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
    {
        var model = await DemandAsync(request.Model, request.Options, request.ExecutionContext, token);
        var result = await actual.ChatWithToolsAsync(request with { Model = model }, token);
        DemandOutputPolicy();
        return result;
    }
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken token) =>
        Task.FromException(new NotSupportedException("Hosted model installation is available only to its authorized host owner."));
    public Task DeleteModelAsync(string model, CancellationToken token) =>
        Task.FromException(new NotSupportedException("Hosted model removal is available only to its authorized host owner."));
}
