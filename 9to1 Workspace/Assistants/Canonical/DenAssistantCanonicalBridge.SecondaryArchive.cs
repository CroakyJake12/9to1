using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge : IAssistantSecondaryConversationArchiveOwner
{
    public Task SetSecondaryConversationArchivedAsync(AssistantConversationBinding binding, bool archived,
        CancellationToken token = default) => _originals.Admit(() =>
            _originals.Source(() => _ordinary.WithSecondaryLifecycleFenceAsync(async () =>
            {
                var current = await ValidateBindingAsync(binding, token).ConfigureAwait(false);
                var original = current.Conversation;
                if (original.Id == MainIdentity(current.Definition.Identity, "conversation"))
                    throw new AssistantCommandRefusedException("The Assistant's protected Main cannot be archived.");
                if (original.Mode != HavenMode.Chat || original.Kind != ConversationKind.Chat || original.IsTemporary)
                    throw new AssistantCommandRefusedException("Only ordinary secondary conversations are handled here. Tasks retain their original lifecycle.");
                if (_conversations is not IAssistantConversationArchiveStore store)
                    throw new AssistantCommandRefusedException("The actual conversation store does not support compare-and-set archiving.");
                if (await _originals.Source(() => _tasks.GetByContextAsync(original.Id, token)).ConfigureAwait(false) is not null)
                    throw new AssistantCommandRefusedException("The original conversation has associated Task work and cannot be changed here.");
                if (!_ordinary.IsSuccessfullySettledForSecondaryLifecycle(original.Id))
                    throw new AssistantCommandRefusedException("This secondary conversation has unfinished Chat work. Wait for it to settle.");
                if (binding.Conversation.IsArchived != original.IsArchived)
                    throw new AssistantCommandRefusedException("The archive state changed. Reopen the original conversation.");
                if (!await _originals.Source(() => store.TrySetAssistantSecondaryArchiveAsync(original, archived, token)).ConfigureAwait(false))
                    throw new AssistantCommandRefusedException("The secondary conversation changed before archive. Refresh and select it again.");
                return true;
            }, token)));
}
