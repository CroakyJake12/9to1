using NineToOne.Dulche.Den;
using Xunit;
namespace HavenOS.AIStudio.Tests;

public sealed class AgentAvatarAssetAuthorityTests
{
    [Fact]
    public async Task Retained_asset_rejects_changed_reference_deleted_owner_mismatch_and_revoked_acl()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-studio-asset-authority-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var policy = new RevocablePolicy(); var den = new DulcheDen(store, policy, "owner");
            var agent = await den.SaveAsync(new AgentDefinitionRecord { Id = "first", NamespaceId = "personal", DisplayName = "First", Version = "1" }, 0, "first");
            var other = await den.SaveAsync(new AgentDefinitionRecord { Id = "second", NamespaceId = "personal", DisplayName = "Second", Version = "1" }, 0, "second");
            var reference = await den.AddAttachmentAsync("personal", agent.Id, DenAgentPresentationAssets.AgentOwnerKind, "image/png", new byte[] { 1, 2, 3 }, "attach");
            Assert.Equal(DenErrorCode.InvalidRecord, (await Assert.ThrowsAsync<DenException>(() => den.ReadAttachmentAsync("personal", reference.Id, 2))).Code);
            var bounded = await den.ReadAttachmentAsync("personal", reference.Id, 3);
            try { Assert.Equal(new byte[] { 1, 2, 3 }, bounded); }
            finally { Array.Clear(bounded); }
            var assets = new DenAgentPresentationAssets(den);
            var captured = await assets.ReadAsync("personal", agent.Id, reference.Id, agent.Revision);
            try
            {
                Assert.Equal(DenErrorCode.Forbidden, (await Assert.ThrowsAsync<DenException>(() => assets.ReadAsync("personal", other.Id, reference.Id, other.Revision))).Code);
                var foreignPresentation = new AgentPresentationDefinition(1, AgentIconPresentation.Static, reference.Id, "Foreign", "", [], [], []);
                Assert.Equal(DenErrorCode.Forbidden, (await Assert.ThrowsAsync<DenException>(() => new AgentPresentationService(den, assets).SetAsync("personal", other.Id, other.Revision, foreignPresentation, "foreign-save"))).Code);
                policy.Allowed = false;
                Assert.False(await assets.CanReadAsync("owner", "personal", reference.Id, default));
                await Assert.ThrowsAsync<DenException>(() => assets.ValidateAsync("personal", agent.Id, agent.Revision, captured));
                policy.Allowed = true;
                var changed = await den.AddAttachmentAsync("personal", agent.Id, DenAgentPresentationAssets.AgentOwnerKind, "image/gif", new byte[] { 1, 2, 3 }, "reference-change");
                Assert.Equal(reference.Id, changed.Id);
                Assert.Equal(DenErrorCode.Conflict, (await Assert.ThrowsAsync<DenException>(() => assets.ValidateAsync("personal", agent.Id, agent.Revision, captured))).Code);
                await den.DeleteAttachmentReferenceAsync("personal", reference.Id, changed.Revision, "delete");
                Assert.False(await assets.CanReadAsync("owner", "personal", reference.Id, default));
                await Assert.ThrowsAsync<DenException>(() => assets.ReadAsync("personal", agent.Id, reference.Id, agent.Revision));
            }
            finally { Array.Clear(captured.Content); }
            var dishonestLength = await den.AddAttachmentAsync("personal", agent.Id, DenAgentPresentationAssets.AgentOwnerKind, "image/png", new byte[] { 4, 5, 6 }, "bounded-attach");
            // Corrupt only this fixture's persisted metadata; production generic Save
            // correctly forbids bypassing the owning attachment operation.
            var recordPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "records", "personal"), dishonestLength.Id + ".json", SearchOption.AllDirectories));
            var persisted = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(recordPath))!;
            persisted["length"] = 1;
            await File.WriteAllTextAsync(recordPath, persisted.ToJsonString());
            Assert.Equal(DenErrorCode.InvalidRecord, (await Assert.ThrowsAsync<DenException>(() => den.ReadAttachmentAsync("personal", dishonestLength.Id, 2))).Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class RevocablePolicy : IDenAccessPolicy
    {
        public bool Allowed = true;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(Allowed && principalId == "owner" && namespaceId == "personal"); }
    }
}
