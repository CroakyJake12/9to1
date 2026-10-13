using System.Security.Cryptography;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenTemporaryMemoryPolicyTests
{
    [Theory]
    [InlineData("explicit-create")]
    [InlineData("automatic-create")]
    [InlineData("reinforce")]
    [InlineData("correct")]
    public async Task Temporary_memory_attempts_leave_actual_store_unchanged_after_reopen(string operation)
    {
        var root = Path.Combine(Path.GetTempPath(), "den-temporary-memory-" + Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var policy = new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]);
        DenStore? store = null;
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal")], lifetime.Token);
            var den = new DulcheDen(store, policy, "owner");
            var original = await den.RememberAsync(new MemoryEntry
            {
                Id = "retained-preference", NamespaceId = "personal", Content = "Prefers concise writing.",
                Category = "preferences", ScopeKind = MemoryScopeKind.User, Provenance = MemoryProvenanceKind.Inference,
                Confidence = 0.4, SourceId = "durable-origin"
            }, temporaryChat: false, explicitUserAction: false, durableSignalScore: 100,
                independentMentions: 10, "initial-durable-memory", lifetime.Token);
            var before = Snapshot(root);
            var candidate = original with
            {
                Id = "new-temporary-memory", Content = operation == "reinforce" ? original.Content : "Temporary new content.",
                Confidence = 0.99, SourceId = "temporary-origin"
            };
            var refusal = await Assert.ThrowsAsync<DenException>(() => operation == "correct"
                ? den.CorrectMemoryAsync("personal", original.Id, "Temporary corrected content.", "temporary-correction",
                    temporaryChat: true, cancellationToken: lifetime.Token)
                : den.RememberAsync(candidate, temporaryChat: true, explicitUserAction: operation == "explicit-create",
                    durableSignalScore: 100, independentMentions: 10, "temporary-attempt", lifetime.Token));
            Assert.Equal(DenErrorCode.TemporaryChatMemoryWriteBlocked, refusal.Code);
            Assert.Equal(before, Snapshot(root));
            await store.DisposeAsync();
            store = await DenStore.OpenAsync(root, lifetime.Token);
            var reopened = new DulcheDen(store, policy, "owner");
            var retained = Assert.Single(await reopened.ListAsync<MemoryEntry>("personal", lifetime.Token));
            Assert.Equal(original.Id, retained.Id);
            Assert.Equal(original.Revision, retained.Revision);
            Assert.Equal(original.Confidence, retained.Confidence);
            Assert.Equal(original.SourceId, retained.SourceId);
            Assert.Equal(original.Content, retained.Content);
            Assert.Equal(before, Snapshot(root));
        }
        finally
        {
            if (store is not null) await store.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string[] Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();
}
