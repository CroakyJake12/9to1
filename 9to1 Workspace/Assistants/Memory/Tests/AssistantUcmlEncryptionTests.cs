using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Context;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    // Non-Linux test hosts avoid writing credentials to a real user vault. The Linux
    // integration rig above uses the production AES-GCM credential owner and reopens it.
    private sealed class EphemeralUcmlSecrets : IProviderSecretStore
    {
        private readonly Dictionary<(string, string), string> _values = [];
        public Task SetAsync(string provider, string name, string value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values[(provider,name)] = value; return Task.CompletedTask; }
        public Task<string?> GetAsync(string provider, string name, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_values.GetValueOrDefault((provider,name))); }
        public Task DeleteAsync(string provider, string name, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values.Remove((provider,name)); return Task.CompletedTask; }
    }

    private static void AssertNoUcmlPlaintextFiles(Rig rig, params string[] needles)
    {
        // SQLite database + live WAL/SHM, Home audit/approval JSON, Den revisions,
        // indexes and actual retained credential files are all included.
        foreach (var file in Directory.EnumerateFiles(rig.Root, "*", SearchOption.AllDirectories))
        {
            var bytes = File.ReadAllBytes(file);
            foreach (var needle in needles)
                Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0,
                    "Private UCML content appeared in durable file: " + Path.GetRelativePath(rig.Root, file));
        }
    }

    [Fact]
    public Task Ucml_canonical_content_provenance_and_receipts_are_encrypted_including_live_WAL_and_Home_audit() => RunAsync(async rig =>
    {
        var title = "Private title marker citrine 83167";
        var body = "Private body marker cobalt 49172. Prefer examples.";
        var provenance = "Private provenance marker verdigris 26914";
        var correctedBody = "Private correction marker magenta 75283. Prefer examples and diagrams.";
        var binding = await rig.CreateAsync(DispatchConfiguration(["communication"]));
        var document = UcmlFixture(title, body);
        document = document with { Metadata = document.Metadata with
        { Sources = [document.Metadata.Sources[0] with { Title = provenance }] } };
        await using (var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge))
        {
            var view = await management.ReadAsync(binding, Token);
            var preview = await management.PrepareUcmlImportAsync(view, UcmlCodec.Serialize(document), Guid.NewGuid(), Token);
            var write = management.CommitAsync(preview, Token);
            await DecideMemoryWrite(rig, write, HomeApprovalChoice.Accept); Assert.True((await write).Saved);
            AssertNoUcmlPlaintextFiles(rig, title, body, provenance);
            view = await management.ReadAsync(binding, Token);
            Assert.Equal(body, Assert.Single(view.Records).Summary);
            var searched = await management.ReadAsync(binding, Token); // scoped decryption preserved
            Assert.Equal(title, Assert.Single(searched.Records).Title);
            var revision = await management.PrepareRevisionAsync(view, view.Records[0], CanonicalAssistantMemoryMutationKind.Correct,
                title, correctedBody, Guid.NewGuid(), Token);
            var correcting = management.CommitAsync(revision.Preview!, Token);
            await DecideMemoryRevision(rig, correcting, HomeCanonicalAssistantMemoryWriteSource.CorrectAction, HomeApprovalChoice.Accept);
            Assert.True((await correcting).Saved);
            AssertNoUcmlPlaintextFiles(rig, title, body, provenance, correctedBody);
        }
        await rig.ReopenAsync();
        binding = await rig.Bridge.OpenConversationAsync(binding.Definition.Identity, binding.Conversation.Id, Token);
        await using var reopened = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var fresh = await reopened.ReadAsync(binding, Token);
        Assert.Equal(correctedBody, Assert.Single(fresh.Records).Summary);
        var exported = UcmlCodec.Parse(await reopened.ExportUcmlAsync(fresh, fresh.Records[0], Token));
        Assert.Equal(provenance, exported.Metadata.Sources[0].Title);
        var reject = await reopened.PrepareRevisionAsync(fresh, fresh.Records[0], CanonicalAssistantMemoryMutationKind.Reject,
            "", "", Guid.NewGuid(), Token);
        var rejecting = reopened.CommitAsync(reject.Preview!, Token);
        await DecideMemoryRevision(rig, rejecting, HomeCanonicalAssistantMemoryWriteSource.RejectAction, HomeApprovalChoice.Accept);
        Assert.True((await rejecting).Saved);
        Assert.Empty((await reopened.RetrieveUcmlAsync(binding, UcmlRequest(), Token)).Items);
        AssertNoUcmlPlaintextFiles(rig, title, body, provenance, correctedBody);
    });

    [Fact]
    public Task Ucml_access_audit_survives_restart_without_query_or_content_and_deduplicates_dispatch_validation() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(DispatchConfiguration(["communication"]));
        var title = "Audit private title 48219"; var body = "Audit private body 67253. Use examples.";
        var query = "examples AUDIT_PRIVATE_QUERY_97458";
        var document = UcmlFixture(title, body);
        await using (var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge))
        {
            var view = await management.ReadAsync(binding, Token);
            var preview = await management.PrepareUcmlImportAsync(view, UcmlCodec.Serialize(document), Guid.NewGuid(), Token);
            var write = management.CommitAsync(preview, Token); await DecideMemoryWrite(rig, write, HomeApprovalChoice.Accept); await write;
            Assert.Single((await management.RetrieveUcmlAsync(binding, UcmlRequest(query), Token)).Items);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => management.RetrieveUcmlAsync(binding,
                UcmlRequest(query) with { Recipient = "unapproved-remote-recipient" }, Token));
            view = await management.ReadAsync(binding, Token);
            await management.ExportUcmlAsync(view, view.Records[0], Token);
            var prepared = await rig.PrepareAsync(binding);
            var selected = await rig.Memory.SelectOriginalUcmlContextWithinSourceAsync(prepared.Input!, binding.Conversation,
                query, Scope, rig.Retain, Token);
            Assert.NotNull(selected);
            await selected.ValidateOriginalWithinSourceAsync(Scope, rig.Retain, Token);
            await selected.ValidateOriginalWithinSourceAsync(Scope, rig.Retain, Token);
            AssertNoUcmlPlaintextFiles(rig, title, body, query);
        }
        await rig.ReopenAsync();
        var audit = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).RecentAuditEvents;
        Assert.Contains(audit, item => item.ResultCode == "HOME_UCML_BROKER_ALLOWED");
        Assert.Contains(audit, item => item.ResultCode == "HOME_UCML_ACCESS_DENIED");
        Assert.Contains(audit, item => item.ResultCode == "HOME_UCML_OWNER_EXPORTED");
        var dispatch = Assert.Single(audit, item => item.ResultCode == "HOME_UCML_LOCAL_DISPATCH_VALIDATED");
        Assert.Equal(document.Metadata.Id.ToString("D"), Assert.Single(dispatch.AffectedObjects).ObjectId);
        AssertNoUcmlPlaintextFiles(rig, title, body, query);
    });

    [Theory]
    [InlineData("routing")]
    [InlineData("ciphertext")]
    [InlineData("wrong-owner")]
    [InlineData("missing-key")]
    [InlineData("legacy-plaintext")]
    public Task Ucml_canonical_tamper_or_key_loss_fails_closed_without_plaintext_fallback(string mode) => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var document = UcmlFixture();
        var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var view = await management.ReadAsync(binding, Token);
        var preview = await management.PrepareUcmlImportAsync(view, UcmlCodec.Serialize(document), Guid.NewGuid(), Token);
        var write = management.CommitAsync(preview, Token); await DecideMemoryWrite(rig, write, HomeApprovalChoice.Accept); await write;
        string? lostKey = null;
        await using (var connection = await rig.Database.OpenAsync(Token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT learned_because FROM knowledge_records WHERE id=$id";
            command.Parameters.AddWithValue("$id", document.Metadata.Id.ToString());
            var stored = (string)(await command.ExecuteScalarAsync(Token))!;
            const string prefix = "ucml.v1:sealed:";
            Assert.StartsWith(prefix, stored);
            var envelope = JsonNode.Parse(stored[prefix.Length..])!;
            if (mode == "missing-key")
            {
                var header = envelope["header"]!;
                var owner = header["ownerId"]!.GetValue<string>();
                lostKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))).ToLowerInvariant()[..32]
                    + "." + Guid.Parse(header["keyId"]!.GetValue<string>()).ToString("N");
                await rig.UcmlSecrets.DeleteAsync("dulche.ucml", lostKey, Token);
            }
            else if (mode == "routing")
            {
                command.CommandText = "UPDATE knowledge_records SET title='tampered selection' WHERE id=$id";
                Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
            }
            else
            {
                if (mode == "wrong-owner") envelope["header"]!["ownerId"] = "home:another-profile";
                if (mode == "ciphertext")
                {
                    var tag = Convert.FromBase64String(envelope["tag"]!.GetValue<string>()); tag[0] ^= 1;
                    envelope["tag"] = Convert.ToBase64String(tag);
                }
                var replacement = mode == "legacy-plaintext" ? "ucml.v1:" + UcmlCodec.SerializeMetadata(document.Metadata)
                    : prefix + envelope.ToJsonString();
                command.CommandText = "UPDATE knowledge_records SET learned_because=$payload WHERE id=$id";
                command.Parameters.AddWithValue("$payload", replacement);
                Assert.Equal(1, await command.ExecuteNonQueryAsync(Token));
            }
        }
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => management.ReadAsync(binding, Token));
        Assert.True(OnlyProtectionFailures(failure), "The read must fail at the authenticated UCML storage boundary.");
        if (lostKey is not null)
            Assert.True(await rig.UcmlSecrets.GetAsync("dulche.ucml", lostKey, Token) is null,
                "A failed read must never regenerate a missing retained key.");
        // Failed raw sources remain failed in original custody. Observe and join those
        // exact cleanup receipts rather than swallowing or rewriting their failure.
        var controllerClose = management.CloseAndDrainAsync();
        Assert.True(OnlyProtectionFailures(await Assert.ThrowsAnyAsync<Exception>(() => controllerClose)));
        var memoryClose = rig.Memory.CloseAndDrainAsync();
        Assert.True(OnlyProtectionFailures(await Assert.ThrowsAnyAsync<Exception>(() => memoryClose)));
        rig.ExpectedMemoryClose = memoryClose;
        var storeClose = rig.Store.CloseAndDrainAsync();
        Assert.True(OnlyProtectionFailures(await Assert.ThrowsAnyAsync<Exception>(() => storeClose)));
        rig.ExpectedStoreClose = storeClose;
    });

    private static bool OnlyProtectionFailures(Exception failure) => failure is AggregateException aggregate
        ? aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(OnlyProtectionFailures)
        : failure is CryptographicException or UnauthorizedAccessException;
}
