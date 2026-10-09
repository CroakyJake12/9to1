using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

[assembly: InternalsVisibleTo("HavenOS.Apps.Assistants.Migration")]

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Optional trusted original source, composed by the app alongside its SAME
/// protected SQLite owner. A stored Agent ID, preference or serialized receipt cannot
/// implement this live permission. Preparation grants no memory READ or WRITE.</summary>
public interface IAssistantOriginalLegacyMemorySource
{
    bool HasOriginalLegacyMemoryStore(object actualStore, HomeLocalProfileIdentity actualProfiles);
    bool IsIssuedOriginalLegacyMemoryReceipt(AssistantOriginalLegacyMemoryReceipt receipt);
    Task<AssistantOriginalLegacyMemoryReceipt?> PrepareOriginalLegacyMemoryWithinSourceAsync(
        AssistantConversationBinding sameBinding, HomePersonalDenFactory actualHome,
        HomeResourceStoreOwnershipAuthority actualOwnership, ResourceStoreIdentity actualStore,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task<bool> ValidateOriginalLegacyMemoryWithinSourceAsync(AssistantOriginalLegacyMemoryReceipt sameReceipt,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>Opaque live provenance issued only after the actual canonical migration,
/// current Den revision, original source and current source ownership are checked.
/// It is not serializable authority and never substitutes for assistant.memory permission.</summary>
public sealed class AssistantOriginalLegacyMemoryReceipt
{
    internal AssistantOriginalLegacyMemoryReceipt(object issuer, object original)
    { Issuer = issuer; Original = original; }
    internal object Issuer { get; }
    internal object Original { get; }
}
