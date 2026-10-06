using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceDevelopmentLinkTests
{
    [Fact]
    public void Restored_link_preserves_the_exact_project_root_repository_task_and_run()
    {
        var project = new DeveloperProjectReference(Guid.NewGuid(), 4, Guid.NewGuid(), 7, Guid.NewGuid(), "original repository binding");
        var link = new SpaceDevelopmentLink(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), project);
        var row = Row(link);
        var restored = SpaceDevelopmentWorkspace.ReadLink(row);
        Assert.Equal(link, restored);
        Assert.Equal(project, restored.Project);
        Assert.NotEqual(row.ContextId, restored.ConversationId);
        Assert.NotEqual(row.ContextId, restored.TaskId);
        Assert.NotEqual(restored.ConversationId, restored.TaskId);
        Assert.NotEqual(restored.TaskId, restored.ExecutionId);
        Assert.Equal(SpaceContextPermission.Unknown, row.Permission);
        Assert.DoesNotContain("workspaceRoot", row.CanonicalEntityId, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("task")]
    [InlineData("run")]
    [InlineData("context")]
    [InlineData("revision")]
    [InlineData("root")]
    public void Unknown_or_incomplete_reference_never_reconstructs_identity(string failure)
    {
        var project = new DeveloperProjectReference(Guid.NewGuid(), 4, Guid.NewGuid(), 7, Guid.NewGuid());
        var link = new SpaceDevelopmentLink(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), project);
        link = failure switch
        {
            "schema" => link with { SchemaVersion = 2 },
            "task" => link with { TaskId = Guid.Empty },
            "run" => link with { ExecutionId = Guid.Empty },
            "context" => link with { ConversationId = Guid.Empty },
            "revision" => link with { Project = project with { WorkspaceRevision = 0 } },
            "root" => link with { Project = project with { RootId = Guid.Empty } },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<InvalidDataException>(() => SpaceDevelopmentWorkspace.ReadLink(Row(link)));
    }

    [Fact]
    public void Foreign_owner_malformed_json_or_mismatched_revision_stamp_is_refused()
    {
        var link = new SpaceDevelopmentLink(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DeveloperProjectReference(Guid.NewGuid(), 4, Guid.NewGuid(), 7, Guid.NewGuid()));
        var row = Row(link);
        Assert.Throws<InvalidDataException>(() => SpaceDevelopmentWorkspace.ReadLink(row with { OwnerAppId = "another app" }));
        Assert.Throws<InvalidDataException>(() => SpaceDevelopmentWorkspace.ReadLink(row with { CanonicalEntityId = "{" }));
        Assert.Throws<InvalidDataException>(() => SpaceDevelopmentWorkspace.ReadLink(row with { RevisionToken = "dev-project-v1:3:7" }));
    }

    private static SpaceContextReference Row(SpaceDevelopmentLink link) => new(
        Guid.NewGuid(), SpaceContextReferenceKind.ConnectedEntity, "dev",
        JsonSerializer.Serialize(link, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        FormattableString.Invariant($"dev-project-v1:{link.Project.WorkspaceRevision}:{link.Project.ProjectRevision}"),
        SpaceContextPermission.Unknown, SpaceContextIndexState.NotRequired, false, DateTimeOffset.UtcNow);
}
