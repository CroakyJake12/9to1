using System.Collections.Frozen;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Dulche.Den;

namespace Dulche.Runtime.Agents;

/// <summary>Current registry/model observations for one exact saved Den revision.
/// A successful validation does not authorize activation, invocation, sharing or tool work.</summary>
public sealed record DenAgentRuntimeValidation(DenAgentReference Reference,
    AgentDependencyLookup Dependencies, ProviderModelDescriptor? Model,
    IReadOnlyList<AgentDependencyDiagnostic> Diagnostics)
{
    public bool ConfigurationResolved => Dependencies.DependenciesResolved && Model is not null && Diagnostics.Count == 0;
}

/// <summary>Reads the original canonical Den record and reuses maintained dependency discovery
/// and model routing. Bound Read authority suffices for observations; Execute still requires the
/// separate original Home context, canonical run/step and per-tool admission.</summary>
public sealed class DenAgentRuntimeValidator(DulcheDen den, string namespaceId,
    AgentDependencyCatalogService dependencies, IModelProviderRegistry providers, IModelRouter router)
{
    public async ValueTask<AgentResult<DenAgentRuntimeValidation>> ValidateCurrentAsync(
        DenAgentReference expected, CapabilityPlatform platform, string currentScope,
        string? originalInheritedModelKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var authority = await den.Store.ReadAuthoritySnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (expected.DenId != authority.DenId || expected.NamespaceId != namespaceId ||
                !authority.Namespaces.Any(value => value.Id == namespaceId))
                return Failure(AgentFailureCode.AgentUnavailableInScope, "The reference belongs to another bound Den or namespace.", expected.AgentId);
            var record = await den.GetAsync<AgentDefinitionRecord>(namespaceId, expected.AgentId, cancellationToken).ConfigureAwait(false);
            if (record is null) return Failure(AgentFailureCode.AgentNotFound, "The canonical Agent was not found.", expected.AgentId);
            if (record.Revision != expected.DefinitionRevision || expected.DefinitionRevision < 1)
                return Failure(AgentFailureCode.DefinitionRevisionUnavailable, "The canonical Agent changed; use its current revision.", expected.AgentId);
            if (!Guid.TryParseExact(record.Id, "D", out var id) || id == Guid.Empty || record.Id != id.ToString("D"))
                return Failure(AgentFailureCode.CapabilityUnavailable, "The current shared execution adapter requires the original lowercase UUID Agent ID.", expected.AgentId);
            var policy = string.IsNullOrWhiteSpace(record.ModelPolicyJson) ? new AgentModelPolicy(true) :
                JsonSerializer.Deserialize<AgentModelPolicy>(record.ModelPolicyJson, DenJson.Options)
                    ?? throw new JsonException("The canonical model policy is null.");
            var budget = string.IsNullOrWhiteSpace(record.BudgetJson) ? new AgentBudgetLimits() :
                JsonSerializer.Deserialize<AgentBudgetLimits>(record.BudgetJson, DenJson.Options)
                    ?? throw new JsonException("The canonical budget is null.");
            if (budget.Validate(record.Id) is { } invalidBudget)
                return AgentResult<DenAgentRuntimeValidation>.Failure(invalidBudget);
            var lookup = await dependencies.ResolveCurrentAsync(new(record.ToolIds, record.SkillIds,
                record.PluginIds, record.McpCapabilityIds, currentScope), platform, cancellationToken).ConfigureAwait(false);
            var diagnostics = new List<AgentDependencyDiagnostic>();
            if (budget.MaxTokens is not null || budget.MaxCost is not null)
                diagnostics.Add(new("UsageMeasurementUnavailable", "budget",
                    "The current Chat stream has no authoritative token/cost usage for enforcing this finite allowance."));
            var required = new HashSet<ToolCapability> { ToolCapability.Text };
            foreach (var value in policy.RequiredCapabilities ?? new HashSet<string>(StringComparer.Ordinal))
                if (!Enum.TryParse<ToolCapability>(value, false, out var capability) || !Enum.IsDefined(capability) || capability.ToString() != value)
                    diagnostics.Add(new("UnknownModelCapability", value, "The required model feature is not an exact canonical ToolCapability."));
                else required.Add(capability);

            ProviderModelDescriptor? selected = null;
            var providerIds = policy.ProviderId is null ? null : new[] { policy.ProviderId }.ToFrozenSet(StringComparer.Ordinal);
            if (policy.ProviderId is { } providerId && providers.Providers.Count(value => value.Id == providerId) != 1)
                diagnostics.Add(new("ProviderUnavailable", providerId, "The exact canonical provider is not currently registered."));
            string? requestedKey = null;
            if (policy.Inherit)
            {
                requestedKey = originalInheritedModelKey;
                if (string.IsNullOrWhiteSpace(requestedKey))
                    diagnostics.Add(new("InheritedSelectionRequired", "model", "The actual current host must supply its inherited provider-qualified model selection."));
            }
            else if (string.IsNullOrWhiteSpace(policy.ModelId))
                diagnostics.Add(new("ModelSelectionRequired", "model", "A non-inherited policy requires its exact saved model identity."));
            else requestedKey = policy.ProviderId is { } savedProvider ? savedProvider + ":" + policy.ModelId : policy.ModelId;

            var cataloguePolicy = new ModelCataloguePolicy(AllowLocal: true, AllowRemote: policy.AllowCloud,
                AllowedProviderIds: providerIds);
            if (diagnostics.Count == 0 && lookup.DependenciesResolved)
            {
                var currentModels = await providers.GetModelsAsync(cataloguePolicy, cancellationToken).ConfigureAwait(false);
                var exact = currentModels.Where(value => value.Key == requestedKey).ToArray();
                if (exact.Length > 1)
                    diagnostics.Add(new("AmbiguousModelIdentity", requestedKey!, "The current catalogue contains competing exact model identities."));
                else if (exact.Length == 0 && !policy.AllowFallback)
                    diagnostics.Add(new("ModelUnavailable", requestedKey!, "The exact selected model is unavailable and fallback is disabled."));
                else
                {
                    var fallbacks = policy.AllowFallback ? currentModels.Where(value => required.All(value.Supports))
                        .OrderByDescending(value => value.IsLocal).ThenBy(value => value.Key, StringComparer.Ordinal)
                        .Select(value => value.Key).ToArray() : [];
                    // The existing router owns compatibility and actual provider choice. The
                    // saved provider restriction is fenced against its returned observation.
                    try
                    {
                        var routed = await router.RouteAsync(new(exact.SingleOrDefault(), required,
                            new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, policy.AllowCloud,
                                new[] { requestedKey! }.Concat(fallbacks).Distinct(StringComparer.Ordinal).ToArray(),
                                policy.AllowFallback)), cancellationToken).ConfigureAwait(false);
                        if ((!policy.AllowCloud && !routed.Model.IsLocal) ||
                            (providerIds is not null && !providerIds.Contains(routed.Model.ProviderId)))
                            diagnostics.Add(new("ModelPolicyMismatch", routed.Model.Key, "The current router result is outside the saved locality/provider policy."));
                        else selected = routed.Model with
                        { Model = routed.Model.Model with { Capabilities = routed.Model.Capabilities.ToFrozenSet() } };
                    }
                    catch (InvalidOperationException)
                    { diagnostics.Add(new("ModelUnavailable", requestedKey!, "The maintained router cannot satisfy this current model policy.")); }
                }
            }

            // These are observations across I/O, not leases. Any revision/ACL/model change
            // is explicit; actual execution will re-admit through Home at each work boundary.
            if (selected is not null)
            {
                var current = await providers.GetModelsAsync(cataloguePolicy, cancellationToken).ConfigureAwait(false);
                var exact = current.Where(value => value.Key == selected.Key).ToArray();
                if (exact.Length != 1 || !SameModel(exact[0], selected) || !required.All(exact[0].Supports))
                { diagnostics.Add(new("ModelChanged", selected.Key, "The selected model changed or disappeared during validation.")); selected = null; }
            }
            var finalRecord = await den.GetAsync<AgentDefinitionRecord>(namespaceId, expected.AgentId, cancellationToken).ConfigureAwait(false);
            var finalAuthority = await den.Store.ReadAuthoritySnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (finalRecord is null || finalRecord.Revision != expected.DefinitionRevision ||
                finalAuthority.DenId != authority.DenId || finalAuthority.ManifestRevision != authority.ManifestRevision)
                return Failure(AgentFailureCode.RevisionConflict, "The Agent or its bound namespace changed during validation.", expected.AgentId);
            if (!await den.AccessPolicy.IsAllowedAsync(den.PrincipalId, namespaceId, expected.AgentId,
                DenPermission.Read, cancellationToken).ConfigureAwait(false))
                return Failure(AgentFailureCode.PermissionDenied, "Current canonical read authority changed during validation.", expected.AgentId);
            cancellationToken.ThrowIfCancellationRequested();
            return AgentResult<DenAgentRuntimeValidation>.Success(new(expected, lookup, selected,
                Array.AsReadOnly(diagnostics.ToArray())));
        }
        catch (DenException error)
        {
            return Failure(error.Code == DenErrorCode.Forbidden ? AgentFailureCode.PermissionDenied :
                error.Code == DenErrorCode.NotFound ? AgentFailureCode.AgentNotFound : AgentFailureCode.StateStoreUnavailable,
                "The current canonical Den read could not be admitted.", expected.AgentId);
        }
        catch (JsonException)
        { return Failure(AgentFailureCode.InvalidInvocationContext, "The saved canonical policy is invalid.", expected.AgentId); }
    }

    private static bool SameModel(ProviderModelDescriptor left, ProviderModelDescriptor right) =>
        left.ProviderId == right.ProviderId && left.IsLocal == right.IsLocal && left.ContextWindow == right.ContextWindow &&
        left.DisplayName == right.DisplayName && left.Model.Name == right.Model.Name &&
        left.Model.SizeBytes == right.Model.SizeBytes && left.Model.Family == right.Model.Family &&
        left.Model.ParameterSize == right.Model.ParameterSize && left.Model.Quantization == right.Model.Quantization &&
        left.Model.ModifiedAt == right.Model.ModifiedAt && left.Capabilities.SetEquals(right.Capabilities);

    private static AgentResult<DenAgentRuntimeValidation> Failure(AgentFailureCode code, string message, string target) =>
        AgentResult<DenAgentRuntimeValidation>.Failure(new(code, message, target));
}
