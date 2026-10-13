using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Exact profile/session-bound CAS for canonical production metadata.
/// The same private transport owns actor→storage→actor and its original drain.</summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserConversationProductionStore
{
    private readonly BrowserTaskExecutionTransport transport;
    private readonly IAuthenticatedResourceActorSource actors;
    internal BrowserConversationProductionStore(BrowserTaskExecutionTransport transport, BrowserTaskActorSource actors)
        : this(transport, (IAuthenticatedResourceActorSource)actors) { }
    // Fixture actor observations issue no authentication. Actual composition above
    // always passes the sealed live source; original custody is identical.
    internal BrowserConversationProductionStore(BrowserTaskExecutionTransport transport, IAuthenticatedResourceActorSource actors)
    { this.transport = transport; this.actors = actors; }
    private AuthenticatedResourceActor? _actor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true };
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal async Task<JsonElement> CallAsync(string action, object args, bool write, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var actual = await transport.InvokeAuthenticatedControlledAsync(actors, action, JsonSerializer.SerializeToElement(args, WireJson), token, _actor);
            _actor ??= actual.Actor;
            if (_actor != actual.Actor) throw new UnauthorizedAccessException("The original conversation production account/session changed.");
            var reply = actual.Reply;
            if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            {
                if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("code", out var code) && code.GetString() == "Cancelled" && !write)
                    throw new OperationCanceledException("The original file selection was cancelled.", token);
                throw new IOException("The original conversation production storage refused or returned no valid receipt; inspect state before retrying.");
            }
            if (write && (!reply.TryGetProperty("committed", out var committed) || committed.ValueKind != JsonValueKind.True))
                throw new IOException("The original conversation production mutation has no committed acknowledgement.");
            return reply.GetProperty("value").Clone();
        }
        finally { _gate.Release(); }
    }
    internal async Task<IReadOnlyList<(Guid Id, string Json)>> ExportAsync(Guid? conversationId, CancellationToken token)
    {
        var rows = await CallAsync("ConversationProduction.Export", new { conversationId }, false, token);
        var result = rows.EnumerateArray().Select(Decode).ToArray();
        if (result.Select(row => row.Id).Distinct().Count() != result.Length || conversationId is { } expected && result.Any(row => row.Id != expected))
            throw new InvalidDataException("The production census supplied duplicate or different conversation identities.");
        return result;
    }
    internal void DemandOriginalProfile(JsonElement row)
    {
        if (_actor is null || row.GetProperty("profileId").GetString() != _actor.ProfileId)
            throw new InvalidDataException("The original production profile differs.");
    }
    private (Guid Id, string Json) Decode(JsonElement row)
    {
        var id = row.GetProperty("conversationId").GetGuid();
        var json = row.GetProperty("json").GetString() ?? throw new InvalidDataException("Missing production payload.");
        var revision = row.GetProperty("revision").GetString();
        if (id == Guid.Empty || row.GetProperty("profileId").GetString() != _actor?.ProfileId || row.GetProperty("sha256").GetString() != Hash(json) ||
            revision is null || !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || number.ToString(CultureInfo.InvariantCulture) != revision)
            throw new InvalidDataException("The production payload, profile or storage revision differs.");
        return (id, json);
    }
    internal async Task CompareExchangeAsync(Guid conversationId, string? expected, string replacement, CancellationToken token)
    {
        if (replacement.Length > 16 * 1024 * 1024) throw new ArgumentException("The conversation production record exceeds its bounded size.");
        var value = await CallAsync("ConversationProduction.CompareExchange", new { conversationId, expected, replacement, sha256 = Hash(replacement) }, true, token);
        var exchanged = value.GetProperty("exchanged");
        if (exchanged.ValueKind == JsonValueKind.False) throw new BrowserConversationProductionConflictException(conversationId);
        if (exchanged.ValueKind != JsonValueKind.True) throw new InvalidDataException("Malformed production exchange acknowledgement.");
        var actual = Decode(value.GetProperty("current"));
        if (actual.Id != conversationId || actual.Json != replacement) throw new InvalidDataException("The acknowledged production mutation differs from the original.");
    }
}
public sealed class BrowserConversationProductionConflictException(Guid conversationId)
    : InvalidOperationException("The saved conversation production state changed; inspect it before retrying.")
{ public Guid ConversationId { get; } = conversationId; }
