using System.Text.Json;

namespace CakeOS.Platform;

/// <summary>Descriptor data consumed from provider-owned JSON; this contract does not create provider-specific definitions.</summary>
public sealed record ProviderDescriptor(
    string ProviderId,
    string DisplayName,
    bool IsLocal,
    string TransportType,
    string? Socket,
    string? ModelKeyFormat,
    string? LegacyUnqualifiedProvider,
    string SourcePath,
    ProviderKind Kind,
    IReadOnlyDictionary<string, JsonElement> TransportDetails,
    IReadOnlyDictionary<string, JsonElement> Discovery,
    IReadOnlyDictionary<string, JsonElement> ModelKeys,
    IReadOnlyDictionary<string, JsonElement> Status,
    IReadOnlyDictionary<string, JsonElement> Execution);

public enum ProviderKind
{
    Local = 0,
    Remote = 1,
    Hybrid = 2
}

public interface IProviderRegistry
{
    IReadOnlyList<ProviderDescriptor> GetAll();
    bool TryGet(string providerId, out ProviderDescriptor? descriptor);
    Task<ProviderDescriptor> RegisterDescriptorAsync(string descriptorPath, CancellationToken cancellationToken = default);
    Task<bool> UnregisterAsync(string providerId, CancellationToken cancellationToken = default);
}

/// <summary>Loads provider descriptors as data. No provider binary, UI, model picker, or backend ownership is implied.</summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderDescriptor> _providers = new(StringComparer.Ordinal);

    public IReadOnlyList<ProviderDescriptor> GetAll()
    {
        lock (_gate)
        {
            return _providers.Values.OrderBy(provider => provider.ProviderId, StringComparer.Ordinal).ToArray();
        }
    }

    public bool TryGet(string providerId, out ProviderDescriptor? descriptor)
    {
        lock (_gate)
        {
            return _providers.TryGetValue(providerId, out descriptor);
        }
    }

    public async Task<ProviderDescriptor> RegisterDescriptorAsync(string descriptorPath, CancellationToken cancellationToken = default)
    {
        var descriptor = await ProviderDescriptorReader.ReadFileAsync(descriptorPath, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (!_providers.TryAdd(descriptor.ProviderId, descriptor))
                throw new InvalidOperationException($"Provider '{descriptor.ProviderId}' is already registered.");
        }
        return descriptor;
    }

    public Task<bool> UnregisterAsync(string providerId, CancellationToken cancellationToken = default)
    {
        PlatformContractValidation.RequireIdentifier(providerId, nameof(providerId));
        lock (_gate)
        {
            return Task.FromResult(_providers.Remove(providerId));
        }
    }
}

public static class ProviderDescriptorReader
{
    public static async Task<ProviderDescriptor> ReadFileAsync(string descriptorPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(descriptorPath) || !Path.IsPathFullyQualified(descriptorPath))
            throw new ArgumentException("Provider descriptor path must be absolute.", nameof(descriptorPath));
        await using var stream = new FileStream(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Read(document.RootElement, Path.GetFullPath(descriptorPath));
    }

    public static ProviderDescriptor Read(JsonElement root, string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Provider descriptor root must be an object.");
        var providerId = RequiredString(root, "providerId");
        PlatformContractValidation.RequireIdentifier(providerId, "providerId");
        var displayName = RequiredString(root, "displayName");
        var kindString = OptionalString(root, "kind") ?? "local";
        var kind = Enum.TryParse<ProviderKind>(kindString, true, out var parsedKind) ? parsedKind : ProviderKind.Local;
        
        var isLocal = root.TryGetProperty("isLocal", out var isLocalValue)
            ? isLocalValue.GetBoolean()
            : kind == ProviderKind.Local;

        if (!root.TryGetProperty("transport", out var transport) || transport.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Provider descriptor requires a transport object.");

        var transportDetails = CloneJsonElements(transport.EnumerateObject());
        var transportType = RequiredString(transport, "type");
        var socket = OptionalString(transport, "socket") ?? OptionalString(transport, "defaultSocket");

        var modelKeysElement = root.TryGetProperty("modelKeys", out var modelKeyValue) && modelKeyValue.ValueKind == JsonValueKind.Object
            ? modelKeyValue
            : default;
        var modelKeys = modelKeysElement.ValueKind == JsonValueKind.Object
            ? CloneJsonElements(modelKeysElement.EnumerateObject())
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var modelKeyFormat = modelKeysElement.ValueKind == JsonValueKind.Object
            ? OptionalString(modelKeysElement, "qualified") ?? OptionalString(modelKeysElement, "format")
            : null;

        var legacyUnqualifiedProvider = modelKeysElement.ValueKind == JsonValueKind.Object
            ? OptionalString(modelKeysElement, "legacyUnqualifiedProvider") ?? OptionalString(modelKeysElement, "unqualifiedResolution")
            : null;

        var discovery = root.TryGetProperty("discovery", out var discoveryValue) && discoveryValue.ValueKind == JsonValueKind.Object
            ? CloneJsonElements(discoveryValue.EnumerateObject())
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var status = root.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.Object
            ? CloneJsonElements(statusValue.EnumerateObject())
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var execution = root.TryGetProperty("execution", out var executionValue) && executionValue.ValueKind == JsonValueKind.Object
            ? CloneJsonElements(executionValue.EnumerateObject())
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        return new ProviderDescriptor(
            providerId,
            displayName,
            isLocal,
            transportType,
            socket,
            modelKeyFormat,
            legacyUnqualifiedProvider,
            sourcePath,
            kind,
            transportDetails,
            discovery,
            modelKeys,
            status,
            execution);
    }

    private static Dictionary<string, JsonElement> CloneJsonElements(IEnumerable<JsonProperty> properties)
    {
        using var doc = JsonDocument.Parse("{}");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in properties)
        {
            // Serialize and re-parse to create owned JsonElement
            var json = prop.Value.GetRawText();
            using var elementDoc = JsonDocument.Parse(json);
            result[prop.Name] = elementDoc.RootElement.Clone();
        }
        return result;
    }

    private static string RequiredString(JsonElement element, string propertyName) =>
        OptionalString(element, propertyName) ?? throw new InvalidDataException($"Provider descriptor requires non-empty '{propertyName}'.");

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var result = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}

public sealed record ModelUseRequest(string ProviderId, string ModelKey, string Capability, PermissionRisk Risk);

public interface IModelGovernance
{
    Task<IReadOnlyList<string>> GetFallbackOrderAsync(CancellationToken cancellationToken = default);
    Task SetFallbackOrderAsync(IReadOnlyCollection<string> providerIds, CancellationToken cancellationToken = default);
    Task<PermissionDecision> EvaluateModelUseAsync(ModelUseRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ResolveModelKeyAsync(string unqualifiedModelKey, CancellationToken cancellationToken = default);
}

/// <summary>Stores provider order and delegates all model-use authorization to the one central permission service.</summary>
public sealed class ModelGovernance(
    IProviderRegistry providers,
    IPermissionService permissions,
    IVersionedSettingsStore settings) : IModelGovernance
{
    public const string FallbackOrderSettingsKey = "platform.model-governance.fallback-order.v1";

    private readonly IProviderRegistry _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly IPermissionService _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<IReadOnlyList<string>> GetFallbackOrderAsync(CancellationToken cancellationToken = default) =>
        await _settings.GetAsync<string[]>(FallbackOrderSettingsKey, cancellationToken).ConfigureAwait(false) ?? [];

    public async Task SetFallbackOrderAsync(IReadOnlyCollection<string> providerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providerIds);
        var normalized = providerIds.Select(providerId => providerId.Trim()).ToArray();
        if (normalized.Length != normalized.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Provider fallback order must not contain duplicates.", nameof(providerIds));
        foreach (var providerId in normalized)
        {
            PlatformContractValidation.RequireIdentifier(providerId, nameof(providerIds));
            if (!_providers.TryGet(providerId, out _))
                throw new KeyNotFoundException($"Provider '{providerId}' is not registered.");
        }
        await _settings.SetAsync(FallbackOrderSettingsKey, normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PermissionDecision> EvaluateModelUseAsync(ModelUseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PlatformContractValidation.RequireIdentifier(request.ProviderId, nameof(request.ProviderId));
        PlatformContractValidation.RequireIdentifier(request.ModelKey, nameof(request.ModelKey));
        PlatformContractValidation.RequireIdentifier(request.Capability, nameof(request.Capability));
        if (!_providers.TryGet(request.ProviderId, out _))
            throw new KeyNotFoundException($"Provider '{request.ProviderId}' is not registered.");

        return await _permissions.EvaluateAsync(
            new PermissionRequest($"provider.{request.ProviderId}", $"model.{request.ModelKey}", request.Capability, request.Risk),
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ResolveModelKeyAsync(string unqualifiedModelKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unqualifiedModelKey))
            return [];

        var fallbackOrder = await GetFallbackOrderAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<string>();

        foreach (var providerId in fallbackOrder)
        {
            if (!_providers.TryGet(providerId, out var descriptor) || descriptor is null)
                continue;

            if (descriptor.ModelKeys.TryGetValue("legacyUnqualifiedProvider", out var legacyProvider) && legacyProvider.ValueKind == JsonValueKind.String)
            {
                var legacyString = legacyProvider.GetString();
                if (legacyString?.Equals("ollama", StringComparison.OrdinalIgnoreCase) == true)
                {
                    candidates.Add($"ollama:{unqualifiedModelKey}");
                }
            }

            if (descriptor.ModelKeys.TryGetValue("qualified", out var qualifiedFormat) && qualifiedFormat.ValueKind == JsonValueKind.String)
            {
                var format = qualifiedFormat.GetString();
                if (!string.IsNullOrWhiteSpace(format))
                {
                    var resolved = format!.Replace("<model-id>", unqualifiedModelKey, StringComparison.OrdinalIgnoreCase);
                    candidates.Add(resolved);
                }
            }
        }

        return candidates.Distinct(StringComparer.Ordinal).ToArray();
    }
}