/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ProviderRoutingModelClient.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ProviderRoutingModelClient, ResolvedProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Compatibility bridge for the existing chat pipeline. It exposes the established
/// IOllamaClient request DTOs while routing provider-qualified model keys to the
/// registered provider. Local Ollama model names remain unchanged and first-class.
/// </summary>
public sealed class ProviderRoutingModelClient(
    IOllamaClient localOllama,
    IModelProviderRegistry providers,
    IPrivacyPreferenceStore privacy) : IProviderModelClient, IOriginalPrivateContextModelClient
{
    /// <summary>
    /// Reports whether available async applies to the current state.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (await localOllama.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return true;
        foreach (var provider in providers.Providers.Where(item =>
                     (!RuntimeSafetyState.IsSafeMode || item.IsLocal)
                     && (!privacy.Current.LocalOnlyMode || item.IsLocal)))
        {
            try
            {
                if ((await provider.CheckHealthAsync(cancellationToken).ConfigureAwait(false)).IsHealthy)
                    return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                // A provider that cannot complete its health check is unavailable.
            }
        }
        return false;
    }

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var descriptors = new List<ProviderModelDescriptor>();
        foreach (var provider in EligibleProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                descriptors.AddRange(await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException
                                             || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // One unavailable eligible provider must not hide the others.
            }
        }

        return descriptors
            .Where(item => (!RuntimeSafetyState.IsSafeMode || item.IsLocal) && (!privacy.Current.LocalOnlyMode || item.IsLocal))
            .Select(ToCompatibilityDescriptor)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => !item.Name.Contains(':', StringComparison.Ordinal))
            .ThenBy(item => item.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<IModelProvider> EligibleProviders() => providers.Providers.Where(item =>
        (!RuntimeSafetyState.IsSafeMode || item.IsLocal)
        && (!privacy.Current.LocalOnlyMode || item.IsLocal));

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        ValidateExplicitBinding(resolved.Provider.Id, resolved.ModelName, request.ReasoningId,
            request.ReasoningTargetProviderId, request.ReasoningTargetModelName);
        await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, resolved.Provider, cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in NamedReasoningDispatch.Stream(resolved.Provider, request with { Model = resolved.ModelName }, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, resolved.Provider, cancellationToken).ConfigureAwait(false);
        return await NamedReasoningDispatch.Complete(resolved.Provider, request with { Model = resolved.ModelName }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        ValidateExplicitBinding(resolved.Provider.Id, resolved.ModelName, request.ReasoningId,
            request.ReasoningTargetProviderId, request.ReasoningTargetModelName);
        await OriginalPrivateContextDispatch.ValidateProviderAsync(request.Options, resolved.Provider, cancellationToken).ConfigureAwait(false);
        return await NamedReasoningDispatch.Tools(resolved.Provider, request with { Model = resolved.ModelName }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs pull model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
        localOllama.PullModelAsync(UnqualifyLocal(model), progress, cancellationToken);

    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
        localOllama.DeleteModelAsync(UnqualifyLocal(model), cancellationToken);

    /// <summary>
    /// Performs the to compatibility descriptor step owned by this component.
    /// </summary>
    public ModelDescriptor ToCompatibilityDescriptor(ProviderModelDescriptor descriptor)
    {
        var requestName = descriptor.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? descriptor.Name
            : descriptor.Key;
        var family = descriptor.IsLocal
            ? descriptor.Model.Family
            : string.IsNullOrWhiteSpace(descriptor.DisplayName)
                ? descriptor.ProviderId
                : descriptor.DisplayName;
        return descriptor.Model with
        {
            Name = requestName,
            Family = family,
            Capabilities = descriptor.Capabilities
        };
    }

    /// <summary>
    /// Validates an explicit named-reasoning binding against the ACTUAL resolved primary identity
    /// before either an ordinary or a named provider invocation. Rules:
    ///  - absent/blank <paramref name="reasoningId"/> keeps ordinary behavior (no check);
    ///  - present id with BOTH binding fields absent/blank preserves legacy named behavior;
    ///  - exactly one binding field supplied is a typed refusal before invocation;
    ///  - both supplied fields must exactly (Ordinal) match the resolved provider id and resolved model
    ///    name, otherwise the selection is refused before any provider invocation. The resolved model
    ///    name is the native name (e.g. Ollama "qwen:tag") and is never split or guessed.
    /// </summary>
    private static void ValidateExplicitBinding(
        string resolvedProviderId,
        string resolvedModelName,
        string? reasoningId,
        string? targetProviderId,
        string? targetModelName)
    {
        if (string.IsNullOrWhiteSpace(reasoningId)) return; // ordinary generation
        var providerSupplied = !string.IsNullOrWhiteSpace(targetProviderId);
        var modelSupplied = !string.IsNullOrWhiteSpace(targetModelName);
        if (providerSupplied != modelSupplied)
            throw new UnsupportedCapabilityException(
                "A named-reasoning selection must supply both the genuine provider and model identity.",
                reasoningId);
        if (!providerSupplied && !modelSupplied) return; // legacy named behavior
        if (!string.Equals(resolvedProviderId, targetProviderId, StringComparison.Ordinal)
            || !string.Equals(resolvedModelName, targetModelName, StringComparison.Ordinal))
            throw new UnsupportedCapabilityException(
                "The named-reasoning selection does not match the resolved primary provider/model.",
                reasoningId);
    }

    /// <summary>
    /// Performs the resolve step owned by this component.
    /// </summary>
    private ResolvedProvider Resolve(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model is required.", nameof(model));
        var trimmed = model.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator > 0)
        {
            var providerId = trimmed[..separator];
            if (providers.Find(providerId) is { } provider)
            {
                if (!provider.IsLocal && (RuntimeSafetyState.IsSafeMode || privacy.Current.LocalOnlyMode))
                    throw new InvalidOperationException("Cloud model providers are disabled by the active safety or local-only privacy policy. Select a local Ollama model or change the applicable policy first.");
                var actualModel = trimmed[(separator + 1)..];
                if (string.IsNullOrWhiteSpace(actualModel)) throw new ArgumentException("The provider-qualified model key is incomplete.", nameof(model));
                return new ResolvedProvider(provider, actualModel);
            }
        }
        return new ResolvedProvider(providers.GetRequired("ollama"), trimmed);
    }

    /// <summary>
    /// Performs the unqualify local step owned by this component.
    /// </summary>
    private static string UnqualifyLocal(string model) => model.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase)
        ? model["ollama:".Length..]
        : model;

    /// <summary>
    /// Represents resolved provider and keeps its related state and behavior together.
    /// </summary>
    private sealed record ResolvedProvider(IModelProvider Provider, string ModelName);
}
