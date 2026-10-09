using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantOriginalDevelopmentOwner
{
    private readonly ConditionalWeakTable<CompatibleTaskContextCreationStage, CompatibleTaskContextCreationStage> _compatibleTaskStages = new();

    // The private source issues this historical finite-stage observation. It grants
    // no binding, permission, replay or durable pause on its own. The bridge must
    // independently ACK the exact Den checkpoint before reporting DeclinedPending.
    internal sealed class CompatibleTaskContextCreationStage
    {
        internal DenAssistantOriginalDevelopmentOwner Issuer { get; }
        internal ICanonicalProjectTaskContextCreationIntent OriginalIntent { get; }
        internal string OriginalIntentSha256 { get; }
        internal ICanonicalProjectTaskContextCreation? Creation { get; }
        internal DeveloperResolvedProject Project { get; }
        internal Conversation StudioConversation { get; }
        internal ContainerDefinition StudioContainer { get; }
        internal Task? OriginalDeclinedSqlTask { get; }
        internal Exception? OriginalDeclinedSqlCause { get; }
        internal bool IsDeclinedNoSql => Creation is null && OriginalDeclinedSqlTask is not null && OriginalDeclinedSqlCause is not null;
        internal CompatibleTaskContextCreationStage(DenAssistantOriginalDevelopmentOwner issuer,
            ICanonicalProjectTaskContextCreationIntent intent, string digest, ICanonicalProjectTaskContextCreation? creation,
            DeveloperResolvedProject project, Conversation studioConversation, ContainerDefinition studioContainer,
            Task? declinedSqlTask, Exception? declinedSqlCause)
        {
            if (creation is null ? declinedSqlTask is null || declinedSqlCause is null : declinedSqlTask is not null || declinedSqlCause is not null)
                throw new InvalidOperationException("An actual stage requires one acknowledged SQL creation or one exact declined raw occurrence.");
            Issuer = issuer; OriginalIntent = intent; OriginalIntentSha256 = digest; Creation = creation;
            Project = project; StudioConversation = studioConversation; StudioContainer = studioContainer;
            OriginalDeclinedSqlTask = declinedSqlTask; OriginalDeclinedSqlCause = declinedSqlCause;
        }
    }

    internal bool IsIssuedOriginalCompatibleTaskContextStage(CompatibleTaskContextCreationStage sameStage) =>
        sameStage is not null && ReferenceEquals(sameStage.Issuer, this) && _compatibleTaskStages.TryGetValue(sameStage, out _);

    // Pure source-issued tuple digest, needed to reserve full pending metadata before
    // entering the manual WRITE. It does not perform IO or admit another original.
    internal string GetOriginalCompatibleTaskIntentDigest(ICanonicalProjectTaskContextCreationIntent sameIntent)
    {
        var creator = _compatibleTaskContexts ?? throw new InvalidOperationException("No actual compatible Tasks producer is configured.");
        if (!creator.IsIssuedOriginalCreationIntent(sameIntent))
            throw new UnauthorizedAccessException("The configured creation producer did not issue this SAME intent.");
        return creator.GetOriginalCreationIntentDigest(sameIntent);
    }
}
