using Haven.Application;

namespace Haven.Core.Tests;

public sealed class CanvasChangeFeedTests
{
    [Fact]
    public void Changes_page_without_duplication_and_reject_future_cursor()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        for (var index = 0; index < 3; index++) Rename(session, index);
        var first = session.GetChanges(pageSize: 2).Value!;
        var second = session.GetChanges(first.NextCursor, 2).Value!;
        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.Equal(3, first.Events.Concat(second.Events).Select(change => change.EventId).Distinct().Count());
        Assert.Equal(3, second.NextCursor);
        Assert.Empty(session.GetChanges(second.NextCursor).Value!.Events);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument, session.GetChanges(4).Error!.Code);
    }

    [Fact]
    public void Retention_preserves_lifetime_cursor_and_requires_snapshot_for_expired_client()
    {
        var session = new CanvasArtifactSession(CanvasArtifact.Create());
        for (var index = 0; index < 2050; index++) Rename(session, index);
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, session.GetChanges(1).Error!.Code);
        var tail = session.GetChanges(2048).Value!;
        Assert.Equal(2, tail.Events.Count);
        Assert.Equal(2050, tail.NextCursor);
        Assert.False(tail.HasMore);
    }

    private static void Rename(CanvasArtifactSession session, int index)
    {
        var result = session.RenameArtifact(new(session.CurrentRevisionId, Guid.NewGuid(), new("test", "Test")), $"Canvas {index}");
        Assert.True(result.IsSuccess);
    }
}
