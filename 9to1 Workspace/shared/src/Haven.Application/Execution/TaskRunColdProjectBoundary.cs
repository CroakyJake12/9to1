using Haven.Core;

namespace Haven.Application;

/// <summary>Configured source-reference observation only; private Home issuance and
/// fresh resource checks are separate. No public instance reference is a grant.</summary>
public interface ITaskRunColdConfiguredProjectResourceSource
{
    bool HasOriginalProjectResourceSource(ITaskRunColdProjectResourceSource sameSource);
    ITaskRunColdProjectResourceSource RequireOriginalProjectResourceSource();
}

public static class TaskRunColdProjectBoundary
{
    /// <summary>Only material consistency on a privately authenticated schema2 capsule.
    /// This method cannot issue a Home receipt, open a root, or authorize restoration.</summary>
    public static void DemandOriginalProjectMaterial(TaskRunColdCapsule capsule)
    {
        var project = capsule.OriginalProjectIdentity;
        var input = capsule.OriginalInput;
        if (capsule.SchemaVersion != 2 || capsule.Boundary != TaskRunColdBoundaryKind.SettledUnfinishedToolResponse
            || project is null || project.WorkspaceId == Guid.Empty || project.ProjectId == Guid.Empty
            || project.RootId == Guid.Empty || project.ContainerId == Guid.Empty
            || project.WorkspaceRevision < 0 || project.ProjectRevision < 0
            || string.IsNullOrWhiteSpace(project.CanonicalRoot)
            || string.IsNullOrWhiteSpace(project.OriginalProjectContextJson)
            || string.IsNullOrWhiteSpace(project.RegisteredRootFingerprint)
            || project.RegisteredRootFingerprint.Length > 8192
            || !IsDigest(project.SavedWorkspaceDocumentSha256) || !IsDigest(project.OriginalContainerSha256)
            || project.OriginalHomeResourceActor is null
            || input.WorkspaceRoot != project.CanonicalRoot
            || input.ProjectContext != project.OriginalProjectContextJson
            || input.ProjectInstructions != project.OriginalContainerInstructions
            || input.Conversation.ContainerId != project.ContainerId
            || capsule.AcceptedConversation.ContainerId != project.ContainerId
            || input.RegisteredContext is not null || input.ComputerUseRequest is not null
            || input.Images is { Count: > 0 } || capsule.AcceptedConversation.LessonId is not null)
            throw new InvalidOperationException("This project capsule has no exact source-captured, bounded resource material.");
    }

    private static bool IsDigest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
