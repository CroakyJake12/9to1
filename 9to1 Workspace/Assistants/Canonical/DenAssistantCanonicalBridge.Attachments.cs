using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantCanonicalBridge
{
    private async Task<IChatOriginalAttachmentInput?> PrepareOriginalAttachmentInputAsync(
        AssistantConversationBinding binding, AssistantTaskInput input, CancellationToken token)
    {
        if (input.AttachmentIds is not { Count: > 0 }) return null;
        if (_attachments is not IAssistantOriginalAttachmentInputOwner owner ||
            _attachments is not IAssistantOriginalAttachmentCommandSource commands ||
            !_originals.Invoke(() => commands.HasOriginalComposition(_home, _conversations) &&
                owner.HasOriginalInputComposition(_chat, _tasks)))
            throw new AssistantCommandRefusedException("Attachment input is unavailable. Your prompt and selected attachments remain saved.");
        await ValidateBindingAsync(binding, token).ConfigureAwait(false);
        var ids = _originals.Invoke(() => Array.AsReadOnly(input.AttachmentIds.ToArray()));
        var prepared = await _originals.Source(() => owner.PrepareOriginalAttachmentInputWithinSourceAsync(
            binding, input.Prompt, ids, MembershipScope, _originals.Retain, token)).ConfigureAwait(false);
        if (!_originals.Invoke(() => owner.IsIssuedOriginalAttachmentInput(prepared)))
            throw new UnauthorizedAccessException("The actual saved attachment source did not issue this input.");
        await ValidateBindingAsync(binding, token).ConfigureAwait(false); return prepared;
    }
    private GenerationOptions OriginalAttachmentRoutingOptions(GenerationOptions options, IChatOriginalAttachmentInput? input)
    {
        if (input is null) return options;
        var owner = _attachments as IAssistantOriginalAttachmentInputOwner
            ?? throw new InvalidOperationException("The actual attachment source is unavailable.");
        if (!_originals.Invoke(() => owner.HasOriginalInputComposition(_chat, _tasks) && owner.IsIssuedOriginalAttachmentInput(input)))
            throw new UnauthorizedAccessException("The configured attachment input source changed before dispatch.");
        return options with
        {
            OriginalAttachmentInput = input,
            RequestedOriginalAttachmentLineage = _originals.Invoke(() => owner.ObserveOriginalAttachmentLineage(input)),
            // This enables only the configured Task disclosure workflow. Its actual
            // frame still requires separate Home approval plus model/provider policy.
            // Ordinary Chat remains local-only, and an existing local restriction stays.
            RequestedRoutingConstraints = new(options.RequestedRoutingConstraints?.AllowCloud != false &&
                _originals.Invoke(() => owner is ITaskOriginalAttachmentEgressSource egress &&
                    egress.CanUseOriginalTaskAttachmentEgress(input)), false)
        };
    }
}
