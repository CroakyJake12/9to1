using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource
{
    private HomeUcmlContextApprovalOwner? _ucmlApprovalOwner;

    public void BindOriginalUcmlContextApproval(HomeUcmlContextApprovalOwner actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualOwner);
        lock (_writeGate)
        {
            if (_ucmlInputsIssued || _ucmlApprovalOwner is not null || !actualOwner.HasOriginalComposition(this, _profiles))
                throw new UnauthorizedAccessException("Bind the SAME Home UCML approval owner once before issuing memory inputs.");
            _ucmlApprovalOwner = actualOwner;
        }
    }

    internal Task<bool> AuthorizeOriginalUcmlDisclosureWithinSourceAsync(IChatOriginalPersistentMemoryInput input,
        Conversation conversation, IReadOnlyList<UcmlDocument> selected, Guid requestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunAsync(scope, retain, async source =>
        {
            var original = DemandInput(input);
            await ValidateMembershipAsync(original, conversation, null, source, token).ConfigureAwait(false);
            if (selected.Count == 0) return true;
            await DemandCurrentUcmlSelectionAsync(original, conversation, selected, source, token).ConfigureAwait(false);
            var approval = _ucmlApprovalOwner ?? throw new UnauthorizedAccessException("UCML disclosure requires the original Home approval owner.");
            if (!await source.Read(() => approval.IsOriginalCallerAllowedWithinSourceAsync(original,
                original.Actor, source.Run, source.Retain, token)).ConfigureAwait(false)) return false;
            var preferences = original.Binding.Definition.Configuration.Memory;
            var askCategories = selected.Select(value => value.Metadata.Category)
                .Where(value => preferences.UcmlAskCategories.Contains(value, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (askCategories.Length > 0)
            {
                var policyHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    preferences, App = AssistantUcmlContextRequest.LocalApp,
                    Purpose = AssistantUcmlContextRequest.LocalPurpose, Recipient = AssistantUcmlContextRequest.LocalRecipient
                })));
                var accepted = await source.Read(() => approval.AuthorizeOriginalAsync(original, requestId, original.Actor,
                    original.Binding.Definition.Identity.DefinitionId, conversation.Id, policyHash,
                    selected.Select(value => value.Metadata.Id).ToArray(), askCategories, preferences.UcmlAccessExpiresAt,
                    source.Run, source.Retain, token)).ConfigureAwait(false);
                if (!accepted) return false;
            }
            // An approval is for this exact selection. Waiting for it never preserves old
            // membership, category policy, source revisions, expiry or store permissions.
            await DemandCurrentUcmlSelectionAsync(original, conversation, selected, source, token).ConfigureAwait(false);
            return true;
        });
}
