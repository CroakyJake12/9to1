using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenBoardCollaborationContractTests
{
    [Fact]
    public void Batch_factory_freezes_structural_commands_and_retains_identity()
    {
        var mutationId = Guid.NewGuid();
        var batch = HavenBoardCollaborationBatch.Create(
            "board-main",
            7,
            "owner_1",
            [new MoveCardCommand("todo", 0, "doing", 1)],
            mutationId);

        Assert.Equal(mutationId, batch.MutationId);
        Assert.Equal("board-main", batch.BoardId);
        Assert.Equal(7, batch.BaseVersion);
        Assert.Equal("owner_1", batch.ActorId);
        Assert.False(batch.Commands is HavenBoardCommand[]);

        var list = Assert.IsAssignableFrom<IList<HavenBoardCommand>>(batch.Commands);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            list[0] = new RenameGroupCommand("todo", "Tampered"));
    }

    [Fact]
    public void Batch_factory_rejects_attachment_smuggling_unsafe_ids_and_oversized_batches()
    {
        var attachment = new HavenBoardAttachment(
            "att-one",
            "brief.txt",
            "sha256:" + new string('a', 64));

        Assert.Throws<InvalidOperationException>(() => HavenBoardCollaborationBatch.Create(
            "board-main",
            1,
            "owner",
            [new AddAttachmentCommand("card-1", attachment)]));
        Assert.Throws<ArgumentException>(() => HavenBoardCollaborationBatch.Create(
            "../outside",
            1,
            "owner",
            [new MoveGroupCommand(0, 0)]));
        Assert.Throws<ArgumentException>(() => HavenBoardCollaborationBatch.Create(
            "board-main",
            1,
            "owner/other",
            [new MoveGroupCommand(0, 0)]));

        var oversized = Enumerable.Range(0, HavenBoardCollaborationBatch.MaxCommandsPerBatch + 1)
            .Select(_ => (HavenBoardCommand)new MoveGroupCommand(0, 0))
            .ToArray();
        Assert.Throws<InvalidOperationException>(() => HavenBoardCollaborationBatch.Create(
            "board-main",
            1,
            "owner",
            oversized));
    }

    [Fact]
    public async Task Disabled_sync_adapter_never_claims_remote_delivery_or_returns_remote_data()
    {
        IHavenBoardSyncAdapter adapter = new DisabledHavenBoardSyncAdapter();
        var batch = HavenBoardCollaborationBatch.Create(
            "board-main",
            1,
            "owner",
            [new MoveGroupCommand(0, 0)]);

        var publish = await adapter.PublishAsync(batch);
        var pulled = await adapter.PullAsync("board-main", 1);

        Assert.False(adapter.IsEnabled);
        Assert.Equal("disabled", adapter.AdapterId);
        Assert.Equal(HavenBoardSyncPublishStatus.Disabled, publish.Status);
        Assert.Contains("no data left the device", publish.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(pulled);
    }

    [Fact]
    public async Task Owner_only_permission_provider_allows_only_exact_configured_actor()
    {
        var policy = new OwnerOnlyHavenBoardPermissionProvider("owner_1");
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var command = new MoveGroupCommand(0, 0);

        var owner = await policy.AuthorizeAsync(snapshot, "owner_1", command);
        var stranger = await policy.AuthorizeAsync(snapshot, "other", command);

        Assert.True(owner.Allowed);
        Assert.False(stranger.Allowed);
    }
}
