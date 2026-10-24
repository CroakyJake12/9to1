using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public interface IExternalConnectionRepository
{
    Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken);
    Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Publishes only while the complete expected canonical record still matches.</summary>
    Task<bool> CompareExchangeAsync(ExternalConnection? expected, ExternalConnection replacement, CancellationToken cancellationToken) =>
        Task.FromException<bool>(new NotSupportedException("This connection repository does not support guarded publication."));

    /// <summary>True when this repository can publish conditionally; otherwise callers use the unguarded path.</summary>
    bool SupportsGuardedPublication => false;
}

/// <summary>
/// Serializes local credential-cache operations with connection removal and revocation.
/// This is in-process coordination for one store object; backing stores remain responsible for
/// atomicity across processes and distinct wrapper instances.
/// </summary>
public static class ExternalConnectionCredentialLifecycle
{
    // A store-wide gate serializes short local credential I/O across short-lived cache adapters.
    // Retain per-provider revocation so stale adapters cannot recreate removed credentials; the
    // weak outer key releases all state with its store.
    private static readonly ConditionalWeakTable<IProviderSecretStore, StoreState> Stores = new();

    public static async ValueTask<Lease> AcquireAsync(IProviderSecretStore store, string providerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var state = Stores.GetValue(store, static _ => new StoreState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(state, providerId);
    }

    /// <summary>Prevents later cache reads/writes for a removed connection before cleanup waits for active leases.</summary>
    public static void Revoke(IProviderSecretStore store, string providerId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var state = Stores.GetValue(store, static _ => new StoreState());
        // Mark synchronously before the removing caller queues for the gate so waiting writers cannot run first.
        state.Revoked.TryAdd(providerId, 0);
    }

    public sealed class Lease : IDisposable
    {
        private StoreState? _state;
        private readonly string _providerId;
        internal Lease(StoreState state, string providerId) { _state = state; _providerId = providerId; }
        public bool IsRevoked => _state?.Revoked.ContainsKey(_providerId) ?? false;
        public void Dispose() => Interlocked.Exchange(ref _state, null)?.Gate.Release();
    }

    internal sealed class StoreState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ConcurrentDictionary<string, byte> Revoked { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Serializes negotiated MCP capabilities with bounded memory and a stable character limit.</summary>
public static class McpCapabilitySnapshotSerializer
{
    public const int MaximumCharacters = 256_000;
    // Null items plus JSON separators already exceed the limit above this count, even before object fields.
    public const int MaximumCollectionItems = (MaximumCharacters - 1) / 5;
    // Raw JsonElement values may retain Unicode bytes; six bytes per UTF-16 code unit is a safe escape bound.
    private const int MaximumUtf8Bytes = MaximumCharacters * 6;
    private static readonly JsonSerializerOptions SerializerOptions = new() { MaxDepth = 64, Encoder = JavaScriptEncoder.Default };
    private static readonly JsonWriterOptions WriterOptions = new() { MaxDepth = 64, Encoder = JavaScriptEncoder.Default };

    public static void EnsureCollectionCountWithinLimit(long itemCount, int maximumCharacters = MaximumCharacters, string? refusalMessage = null)
    {
        if (maximumCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var maximumItems = (maximumCharacters - 1) / 5;
        if (itemCount > maximumItems)
            throw new InvalidOperationException(refusalMessage ?? "The MCP provider returned an invalid capability snapshot.");
    }

    public static string Serialize(McpServerDiscovery discovery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return Serialize(discovery.Identity, discovery.Tools, discovery.Resources, discovery.Prompts, cancellationToken);
    }

    public static string Serialize(McpServerIdentity identity, IReadOnlyList<McpExternalTool> tools,
        IReadOnlyList<McpExternalResource>? resources, IReadOnlyList<McpExternalPrompt>? prompts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(tools);
        cancellationToken.ThrowIfCancellationRequested();
        var itemCount = (long)tools.Count + (resources?.Count ?? 0) + (prompts?.Count ?? 0);
        EnsureCollectionCountWithinLimit(itemCount);

        try
        {
            return SerializeBounded(new { identity, tools, resources, prompts }, MaximumCharacters, cancellationToken);
        }
        catch (SnapshotLimitExceededException)
        {
            throw new InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
        }
    }

    public static bool TrySerializeBounded<T>(T value, int maximumCharacters, out string serialized,
        CancellationToken cancellationToken = default)
    {
        try
        {
            serialized = SerializeBounded(value, maximumCharacters, cancellationToken);
            return true;
        }
        catch (SnapshotLimitExceededException)
        {
            serialized = string.Empty;
            return false;
        }
    }

    private static string SerializeBounded<T>(T value, int maximumCharacters, CancellationToken cancellationToken)
    {
        if (maximumCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var maximumBytes = checked(maximumCharacters * 6);
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new BoundedJsonBufferWriter(maximumBytes, cancellationToken);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            JsonSerializer.Serialize(writer, value, SerializerOptions);
        return buffer.ToBoundedString(maximumCharacters, cancellationToken);
    }

    private sealed class BoundedJsonBufferWriter(int maximumBytes, CancellationToken cancellationToken) : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer = [];
        private int _length;

        public void Advance(int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count < 0 || count > _buffer.Length - _length || count > maximumBytes - _length)
                throw new SnapshotLimitExceededException();
            _length += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_length);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
            var requiredBytes = (long)_length + Math.Max(sizeHint, 1);
            if (requiredBytes > maximumBytes)
                throw new SnapshotLimitExceededException();
            var required = (int)requiredBytes;
            if (required <= _buffer.Length) return;
            var doubled = _buffer.Length == 0 ? 4096 : _buffer.Length * 2;
            Array.Resize(ref _buffer, Math.Min(maximumBytes, Math.Max(required, doubled)));
        }

        public string ToBoundedString(int maximumCharacters, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var characterCount = Encoding.UTF8.GetCharCount(_buffer, 0, _length);
            if (characterCount > maximumCharacters)
                throw new SnapshotLimitExceededException();
            var result = Encoding.UTF8.GetString(_buffer, 0, _length);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public void Dispose()
        {
            Array.Clear(_buffer);
            _buffer = [];
        }
    }

    private sealed class SnapshotLimitExceededException() : InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
}

public interface IMcpConnectionClient
{
    Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);

    async Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var (identity, tools) = await DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
        var snapshot = McpCapabilitySnapshotSerializer.Serialize(identity, tools, null, null, cancellationToken);
        return new McpServerDiscovery(identity, tools, null, null, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant());
    }

    Task<McpResourceReadResult> ReadResourceAsync(ExternalConnection connection, string resourceUri, CancellationToken cancellationToken) =>
        Task.FromException<McpResourceReadResult>(new NotSupportedException("This MCP client does not support resources."));

    Task<McpPromptGetResult> GetPromptAsync(ExternalConnection connection, string promptName, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken) =>
        Task.FromException<McpPromptGetResult>(new NotSupportedException("This MCP client does not support prompts."));
}

/// <summary>Produces transient catalogue capabilities backed by current runtime state.</summary>
public interface IDynamicCapabilityProvider
{
    Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CapabilityPlatform platform, CancellationToken cancellationToken);
}
