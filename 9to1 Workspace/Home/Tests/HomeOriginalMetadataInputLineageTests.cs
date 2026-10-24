using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Pure comparison controls used by the real source-owned lineage. These fixtures are
// not native READs, private issuance, Home approvals or Task/cold-capsule acceptance.
public sealed class HomeOriginalMetadataInputLineageTests
{
    private static TaskRunColdProjectIdentity Identity() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("20000000-0000-0000-0000-000000000001"),
        Guid.Parse("30000000-0000-0000-0000-000000000001"),
        7, 9, "repository-binding", "/actual-registered-root", "{old-context-format}",
        Guid.Parse("40000000-0000-0000-0000-000000000001"), "container-sha", "original instructions",
        new string('a', 64), "registered-root-fingerprint",
        new AuthenticatedResourceActor("actor", "profile", null, null, "authentication-revision"));

    [Fact]
    public void Metadata_comparison_allows_only_saved_hash_and_source_role_reference_formatting()
    {
        var original = Identity();
        Assert.True(HomeColdProjectReadReconciliation.SameMetadataInputResource(original,
            original with { SavedWorkspaceDocumentSha256 = new string('b', 64),
                OriginalProjectContextJson = "{same-typed-context-other-source-format}" }));
        Assert.Equal(new string('a', 64), original.SavedWorkspaceDocumentSha256);
        Assert.Equal("{old-context-format}", original.OriginalProjectContextJson);
    }

    [Fact]
    public void Metadata_comparison_refuses_every_original_resource_and_actor_change()
    {
        var original = Identity();
        TaskRunColdProjectIdentity[] changed =
        [
            original with { WorkspaceId = Guid.NewGuid() },
            original with { ProjectId = Guid.NewGuid() },
            original with { RootId = Guid.NewGuid() },
            original with { WorkspaceRevision = 8 },
            original with { ProjectRevision = 10 },
            original with { RepositoryBindingId = "different-repository" },
            original with { CanonicalRoot = "/different-root" },
            original with { ContainerId = Guid.NewGuid() },
            original with { OriginalContainerSha256 = "different-container" },
            original with { OriginalContainerInstructions = "different-instructions" },
            original with { RegisteredRootFingerprint = "different-native-root" },
            original with { OriginalHomeResourceActor = original.OriginalHomeResourceActor with { ActorId = "other-actor" } },
            original with { OriginalHomeResourceActor = original.OriginalHomeResourceActor with { ProfileId = "other-profile" } },
            original with { OriginalHomeResourceActor = original.OriginalHomeResourceActor with { AccountId = Guid.NewGuid() } },
            original with { OriginalHomeResourceActor = original.OriginalHomeResourceActor with { OrganisationId = Guid.NewGuid() } },
            original with { OriginalHomeResourceActor = original.OriginalHomeResourceActor with { AuthenticationRevision = "new-auth" } }
        ];
        Assert.All(changed, value => Assert.False(HomeColdProjectReadReconciliation.SameMetadataInputResource(original, value)));
    }
}
