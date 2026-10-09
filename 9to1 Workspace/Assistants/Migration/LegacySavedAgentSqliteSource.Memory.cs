using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Migration;

public sealed partial class LegacySavedAgentSqliteSource : IAssistantOriginalLegacyMemorySource
{
    private sealed record OriginalMemoryReceipt(AssistantConversationBinding Binding,
        HomePersonalDenFactory Home, HomeResourceStoreOwnershipAuthority Ownership,
        ResourceStoreIdentity Store, AuthenticatedResourceActor Actor,
        LegacyAgentMigrationController.OriginalMemoryLineage Lineage);

    public bool HasOriginalLegacyMemoryStore(object actualStore, HomeLocalProfileIdentity actualProfiles) =>
        ReferenceEquals(actualStore, _store) && ReferenceEquals(actualProfiles, _profiles) && _store.HasOriginalProfiles(actualProfiles);
    public bool IsIssuedOriginalLegacyMemoryReceipt(AssistantOriginalLegacyMemoryReceipt receipt) =>
        receipt is not null && ReferenceEquals(receipt.Issuer, this) && receipt.Original is OriginalMemoryReceipt;

    public Task<AssistantOriginalLegacyMemoryReceipt?> PrepareOriginalLegacyMemoryWithinSourceAsync(
        AssistantConversationBinding sameBinding, HomePersonalDenFactory actualHome,
        HomeResourceStoreOwnershipAuthority actualOwnership, ResourceStoreIdentity actualStore,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        RunOriginalMemoryAsync<AssistantOriginalLegacyMemoryReceipt?>(originalSynchronousScope, retainOriginalTask, async callbacks =>
        {
            var current = await ReadOriginalMemoryReceiptAsync(sameBinding, actualHome, actualOwnership, actualStore, callbacks, token).ConfigureAwait(false);
            return current is null ? null : new AssistantOriginalLegacyMemoryReceipt(this, current);
        });

    public Task<bool> ValidateOriginalLegacyMemoryWithinSourceAsync(AssistantOriginalLegacyMemoryReceipt sameReceipt,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        RunOriginalMemoryAsync(originalSynchronousScope, retainOriginalTask, async callbacks =>
        {
            if (!IsIssuedOriginalLegacyMemoryReceipt(sameReceipt))
                throw new UnauthorizedAccessException("Only the SAME saved Agent source's live original memory receipt is accepted.");
            var original = (OriginalMemoryReceipt)sameReceipt.Original;
            var current = await ReadOriginalMemoryReceiptAsync(original.Binding, original.Home,
                original.Ownership, original.Store, callbacks, token).ConfigureAwait(false);
            return current is not null && current.Actor == original.Actor && current.Store == original.Store &&
                current.Lineage == original.Lineage;
        });

    private async Task<OriginalMemoryReceipt?> ReadOriginalMemoryReceiptAsync(AssistantConversationBinding binding,
        HomePersonalDenFactory actualHome, HomeResourceStoreOwnershipAuthority actualOwnership,
        ResourceStoreIdentity actualStore, Callbacks callbacks, CancellationToken token)
    {
        var membership = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        if (membership is null || !ReferenceEquals(membership.OriginalHomeDenFactory, actualHome) ||
            actualOwnership is not IResourceStoreOriginalScopedOwnershipAuthority scoped) return null;
        var observed = await callbacks.Read(() => membership.ValidateOriginalWithinSourceAsync(
            binding, callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
        var actor = await CurrentActorAsync(callbacks, token).ConfigureAwait(false);
        if (observed.Actor != actor) throw new UnauthorizedAccessException("The saved Agent source and current Assistant have different actual Home actors.");
        var home = await callbacks.Read(() => membership.OpenHomeWithinSourceAsync(callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
        var definition = await callbacks.Read(() => membership.DefinitionWithinSourceAsync(home,
            binding.Definition.Identity, callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
        if (home.Actor != actor || definition.Revision != binding.Definition.Revision)
            throw new UnauthorizedAccessException("The actual migrated definition revision changed.");
        LegacyAgentMigrationController.OriginalMemoryLineage? lineage = null;
        callbacks.Run(() => lineage = LegacyAgentMigrationController.ReadOriginalMemoryLineage(definition, actualStore.StoreId, actor.ProfileId));
        if (lineage is null) return null;
        // The memory READ receipt never grants access to legacy definitions. This
        // independent current original import must exist BEFORE source content reads.
        var permission = await callbacks.Read(() => scoped.GetVerifiedWithinOriginalSourceAsync(
            LegacyResourceKind, actualStore.StoreId.ToString("D"), callbacks.Run, callbacks.Retain, token).AsTask()).ConfigureAwait(false);
        if (permission?.Receipt is null || permission.ResourceKind != LegacyResourceKind ||
            permission.StoreId != actualStore.StoreId.ToString("D") || permission.ProfileId != actor.ProfileId ||
            !await callbacks.Read(() => scoped.IsCurrentWithinOriginalSourceAsync(permission, actor,
                callbacks.Run, callbacks.Retain, token).AsTask()).ConfigureAwait(false)) return null;
        return await callbacks.Read<OriginalMemoryReceipt?>(() => ReadOwnedAsync<OriginalMemoryReceipt?>(actor, actualOwnership, false, async owned =>
        {
            if (owned.Identity != actualStore) return null;
            var saved = await owned.ReadAsync(lineage.AgentId, token).ConfigureAwait(false);
            if (saved is null || saved.DefinitionSha256 != lineage.SourceSha256 || saved.RawDefinitionJson != lineage.SourceJson) return null;
            var final = await callbacks.Read(() => membership.ValidateOriginalWithinSourceAsync(
                binding, callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
            if (final.Actor != actor || final.Definition.Revision != definition.Revision)
                throw new UnauthorizedAccessException("The current migrated identity changed during its original source read.");
            return new OriginalMemoryReceipt(binding, actualHome, actualOwnership, actualStore, actor, lineage);
        }, callbacks.Run, callbacks.Retain, token)).ConfigureAwait(false);
    }

    private Task<T> RunOriginalMemoryAsync<T>(Action<Action> caller, Action<Task> retain,
        Func<Callbacks, Task<T>> body) => _setupOriginals.Admit(async () =>
    {
        ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retain);
        var callbacks = new Callbacks(action => _setupOriginals.Run(() => WithinOriginalMemoryCaller(caller, action)),
            task => { _setupOriginals.Retain(task); retain(task); });
        // Capture and await the encompassing source body as well as every raw child.
        return await callbacks.Read(() => body(callbacks)).ConfigureAwait(false);
    });

    private static void WithinOriginalMemoryCaller(Action<Action> caller, Action body)
    {
        var active = true; var used = false; var thread = Environment.CurrentManagedThreadId;
        var failures = new List<Exception>();
        try
        {
            caller(() =>
            {
                try
                {
                    if (!active || used || thread != Environment.CurrentManagedThreadId)
                        throw new InvalidOperationException("The original legacy memory callback is inactive, repeated or on a foreign thread.");
                    used = true; body();
                }
                catch (Exception failure) { failures.Add(failure); throw; }
            });
            if (!used) failures.Add(new InvalidOperationException("The original legacy memory callback was not entered."));
        }
        catch (Exception failure) { if (!failures.Any(prior => ReferenceEquals(prior, failure))) failures.Add(failure); }
        finally { active = false; }
        MigrationOriginals.ThrowCombined(failures);
    }
}
