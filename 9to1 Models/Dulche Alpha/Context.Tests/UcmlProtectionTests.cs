using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Memory;
using NineToOne.Dulche.Context;
using Xunit;

namespace NineToOne.Dulche.Context.Tests;

public sealed class UcmlProtectionTests
{
    private static readonly DateTimeOffset Today = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private const string Owner = "home-profile:test-owner";
    private static UcmlDocument Document() => new(new()
    {
        SchemaVersion = 1, Id = Guid.Parse("df3d8d73-d3c8-4870-ad0e-337bfbb343a0"), Revision = 1,
        Title = "Private personal preference", OwnerId = Owner, Scope = new("personal", "private-owner-scope"),
        Category = "communication", Classification = "private", CreatedAt = Today, UpdatedAt = Today,
        Retention = "until-revoked", Consent = new("explicit", ["9to1"], ["personalisation"], ["local-model"]),
        Sources = [new("source:1", "User statement", "conversation", null, Today)], Confidence = 1
    }, "I prefer specific, grounded answers and a calm tone.\n");

    [Fact]
    public async Task Envelopes_roundtrip_with_fresh_ciphertext_without_exposing_content_or_keys()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody); var original = Document();
        var first = await crypto.SealAsync(original, Owner, key, Today);
        var second = await crypto.SealAsync(original, Owner, key, Today);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain(original.Markdown, first);
        Assert.DoesNotContain(original.Metadata.Title, first);
        Assert.DoesNotContain(Convert.ToBase64String(custody.Stored.Values.Single()), first);
        Assert.Equal(UcmlCodec.Serialize(original), UcmlCodec.Serialize(await crypto.UnsealAsync(first, Owner)));
    }

    [Fact]
    public async Task Daily_rotation_preserves_old_decryptability_and_never_replaces_root_key()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var old = await crypto.SealAsync(Document(), Owner, key, Today);
        var next = await crypto.SealAsync(Document(), Owner, key, Today.AddDays(1));
        Assert.Equal("2026-10-16", JsonNode.Parse(old)!["header"]!["keyDay"]!.GetValue<string>());
        Assert.Equal("2026-10-17", JsonNode.Parse(next)!["header"]!["keyDay"]!.GetValue<string>());
        Assert.Equal(1, custody.Writes);
        Assert.Single(custody.Stored);
        Assert.Equal(Document().Markdown, (await crypto.UnsealAsync(old, Owner)).Markdown);
        Assert.Equal(Document().Markdown, (await crypto.UnsealAsync(next, Owner)).Markdown);
    }

    [Fact]
    public async Task Different_owners_and_same_named_documents_cannot_share_key_custody()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var sealedText = await crypto.SealAsync(Document(), Owner, key, Today);
        var reads = custody.Reads;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => crypto.UnsealAsync(sealedText, "other-owner"));
        Assert.Equal(reads, custody.Reads);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => crypto.SealAsync(Document(), "other-owner", key, Today));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => crypto.SealAsync(Document(), Owner, key with { OwnerId = "other-owner" }, Today));
    }

    [Theory]
    [InlineData("scopeId", "different-space")]
    [InlineData("keyDay", "2026-10-17")]
    [InlineData("documentId", "550d66d1-e41e-4467-adb8-027dd9bf092d")]
    public async Task Authenticated_headers_reject_substitution(string field, string value)
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var node = JsonNode.Parse(await crypto.SealAsync(Document(), Owner, key, Today))!;
        node["header"]![field] = value;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => crypto.UnsealAsync(node.ToJsonString(), Owner));
    }

    [Theory]
    [InlineData("ciphertext")]
    [InlineData("tag")]
    [InlineData("nonce")]
    public async Task Ciphertext_nonce_and_tag_tampering_never_release_plaintext(string field)
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var node = JsonNode.Parse(await crypto.SealAsync(Document(), Owner, key, Today))!;
        var bytes = Convert.FromBase64String(node[field]!.GetValue<string>()); bytes[0] ^= 1;
        node[field] = Convert.ToBase64String(bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => crypto.UnsealAsync(node.ToJsonString(), Owner));
    }

    [Fact]
    public async Task Missing_or_corrupted_key_fails_closed_without_replacement()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var sealedText = await crypto.SealAsync(Document(), Owner, key, Today);
        custody.Stored.Clear();
        await Assert.ThrowsAsync<CryptographicException>(() => crypto.UnsealAsync(sealedText, Owner));
        Assert.Equal(1, custody.Writes); Assert.Empty(custody.Stored);
        custody.Stored[key] = new byte[7];
        await Assert.ThrowsAsync<CryptographicException>(() => crypto.UnsealAsync(sealedText, Owner));
        Assert.Equal(1, custody.Writes);
    }

    [Fact]
    public async Task Malformed_duplicate_oversized_and_future_envelopes_are_rejected()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var sealedText = await crypto.SealAsync(Document(), Owner, key, Today);
        await Assert.ThrowsAsync<FormatException>(() => crypto.UnsealAsync(sealedText.Replace("\"version\":1", "\"version\":1,\"version\":1"), Owner));
        await Assert.ThrowsAsync<FormatException>(() => crypto.UnsealAsync(sealedText.Replace("\"version\":1", "\"version\":2"), Owner));
        await Assert.ThrowsAsync<FormatException>(() => crypto.UnsealAsync(new string('x', UcmlProtection.MaximumEnvelopeBytes + 1), Owner));
        await Assert.ThrowsAsync<FormatException>(() => crypto.UnsealAsync("null", Owner));
        await Assert.ThrowsAsync<FormatException>(() => crypto.UnsealAsync("{}", Owner));
    }

    [Fact]
    public async Task Cancellation_prevents_new_key_or_plaintext_return()
    {
        var custody = new ControlledCustody(); var key = await custody.CreateKeyAsync(Owner);
        var crypto = new UcmlProtection(custody);
        var sealedText = await crypto.SealAsync(Document(), Owner, key, Today);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => custody.CreateKeyAsync(Owner, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => crypto.UnsealAsync(sealedText, Owner, cancelled.Token));
        Assert.Equal(1, custody.Writes);
    }

    [Fact]
    public async Task Real_linux_secret_owner_survives_restart_and_refuses_tampering_and_missing_root()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("ucml-custody-test-").FullName;
        LinuxProviderSecretStore? secretOwner = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var paths = new Paths(root); secretOwner = new(paths);
            var custody = new ProviderSecretUcmlKeyCustody(secretOwner);
            var key = await custody.CreateKeyAsync(Owner);
            var crypto = new UcmlProtection(custody);
            var file = Path.Combine(root, "profile.ucml.enc");
            await File.WriteAllTextAsync(file, await crypto.SealAsync(Document(), Owner, key, Today));
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.DoesNotContain(Document().Markdown, await File.ReadAllTextAsync(file));
            var credential = Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential").Single();
            Assert.DoesNotContain(Document().Markdown, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(credential)));
            secretOwner.Dispose(); secretOwner = new(paths);
            crypto = new(new ProviderSecretUcmlKeyCustody(secretOwner));
            Assert.Equal(Document().Markdown, (await crypto.UnsealAsync(await File.ReadAllTextAsync(file), Owner)).Markdown);
            var node = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            node["header"]!["revision"] = 2;
            await Assert.ThrowsAnyAsync<CryptographicException>(() => crypto.UnsealAsync(node.ToJsonString(), Owner));
            File.Delete(credential);
            await Assert.ThrowsAsync<CryptographicException>(() => crypto.UnsealAsync(File.ReadAllText(file), Owner));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential"));
        }
        finally { secretOwner?.Dispose(); Directory.Delete(root, recursive: true); }
    }

    private sealed class ControlledCustody : UcmlAesGcmKeyCustody
    {
        public readonly Dictionary<UcmlKeyReference, byte[]> Stored = [];
        public int Writes, Reads;
        protected override Task<byte[]?> ReadRootKeyAsync(UcmlKeyReference key, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult(Stored.TryGetValue(key, out var value) ? (byte[]?)value.ToArray() : null); }
        protected override Task WriteNewRootKeyAsync(UcmlKeyReference key, byte[] rootKey, CancellationToken cancellationToken)
        { Stored.Add(key, rootKey.ToArray()); Writes++; return Task.CompletedTask; }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
    }
}
