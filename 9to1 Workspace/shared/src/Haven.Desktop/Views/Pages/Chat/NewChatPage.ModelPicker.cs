using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    /// <summary>The actual page owns the complete actor/catalogue/apply chain. A panel may stop
    /// observing it, but the page and its registered session still join the original read on close.</summary>
    internal Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadModelPickerConversationAsync(
        ICatalogRepository catalogue, IAuthenticatedResourceActorSource actors, AuthenticatedResourceActor actor,
        Guid conversationId, Func<bool> ownerCurrent, HomeModelPickerSpecialistSelection? selection,
        CancellationToken observerToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _originalWork.IsRetiring || _conversation.Id != conversationId || !ownerCurrent())
            return Task.FromResult(PickerConversationUnavailable("The originating Chat is no longer current."));
        var target = CaptureOriginalConversationTarget();
        var modelRevision = _modelSelectionRevision;
        var selected = _activeAgent;
        return _originalWork.RunAsync(async original =>
        {
            BindOriginalConversationTarget(original, target);
            bool Current() => original.IsPublicationCurrent && _modelSelectionRevision == modelRevision &&
                Equals(_activeAgent, selected) && ownerCurrent();
            HomeCoreOperationResult<HomeModelPickerConversationSnapshot> Refused() =>
                PickerConversationUnavailable("The Chat, model or Specialist selection changed. Refresh this picker before choosing again.");

            if (!Current() || observerToken.IsCancellationRequested) return Refused();
            if (selection is not null && (selection.ConversationId != conversationId ||
                selection.ModelSelectionRevision != modelRevision || selection.ExpectedSpecialistId != selected?.Id ||
                !string.Equals(selection.ExpectedSpecialistVersion, SpecialistVersion(selected), StringComparison.Ordinal)))
                return Refused();
            if (selection is not null && _safetyLocked)
                return PickerConversationUnavailable("This Chat is safety locked. Specialist changes are unavailable.");

            // These are real source tasks enrolled in this same original. Supersession before
            // an effect is an ordinary refusal; a genuine read/fault retains its original cause.
            if (await original.AwaitAsync(actors.GetCurrentAsync(original.Token).AsTask()) != actor || !Current())
                return PickerConversationUnavailable("The authenticated Home profile changed. Reopen the picker.");
            var definitions = await original.AwaitAsync(catalogue.GetAgentsAsync(original.Token));
            if (!Current() || observerToken.IsCancellationRequested) return Refused();
            if (await original.AwaitAsync(actors.GetCurrentAsync(original.Token).AsTask()) != actor || !Current())
                return PickerConversationUnavailable("The authenticated Home profile changed. Reopen the picker.");
            if (observerToken.IsCancellationRequested) return Refused();
            if (definitions.Any(item => item.Id == Guid.Empty) || definitions.GroupBy(item => item.Id).Any(group => group.Count() != 1))
                throw new InvalidDataException("The canonical Specialist catalogue contains an invalid or duplicate identity.");
            var enabled = definitions.Where(item => item.IsEnabled).ToArray();

            if (selection is not null)
            {
                if (_safetyLocked) return PickerConversationUnavailable("This Chat is safety locked. Specialist changes are unavailable.");
                if (selection.SpecialistId is { } id)
                {
                    var definition = enabled.SingleOrDefault(item => item.Id == id);
                    if (definition is null || !string.Equals(SpecialistVersion(definition), selection.DefinitionVersion, StringComparison.Ordinal))
                        return PickerConversationUnavailable("That Specialist was removed, disabled or changed. Refresh the catalogue before choosing again.");
                    // Canonical Chat selection, not workspace navigation, execution or a tool grant.
                    ApplyAddSelection(new(AddMenu.AddMenuAction.Agent, definition));
                }
                else
                {
                    if (selection.DefinitionVersion is not null)
                        return PickerConversationUnavailable("The None choice is not a current Specialist selection.");
                    if (_activeAgent is { } current)
                        await original.AwaitAsync(OnAttachmentRemoveRequestedOriginalBodyAsync(null, $"agent:{current.Id:D}"));
                }
            }

            return new(true, selection is null ? "ConversationObserved" : "SpecialistSelected",
                selection is null ? "Current Chat and Specialist catalogue." : "Specialist selection updated for future messages.",
                new(conversationId, modelRevision, _selectedModel?.Name, _activeAgent?.Id, _activeAgent?.Name,
                    SpecialistVersion(_activeAgent), _safetyLocked, Array.AsReadOnly(enabled.Select(item =>
                        new HomeModelPickerSpecialist(item.Id, item.Name, item.Description, SpecialistVersion(item)!)).ToArray())));
        });
    }

    private static string? SpecialistVersion(AgentDefinition? definition) => definition is null ? null
        : Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition)));

    private static HomeCoreOperationResult<HomeModelPickerConversationSnapshot> PickerConversationUnavailable(string message) =>
        new(false, "ConversationSelectionUnavailable", message);
}
