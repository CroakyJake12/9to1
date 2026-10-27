using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.Desktop.Views.Pages.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private readonly Dictionary<NewChatPage, EventHandler> _conversationRouteHandlers = [];

    // This is only a target in the existing workspace tab address. It is neither
    // a conversation store nor permission to read another Space's history.
    private readonly record struct SavedConversationRoute(Guid ConversationId, Guid? ExpectedSpaceId);

    private static SavedConversationRoute? ReadConversationRoute(AppContentAddress address)
    {
        var segments = address.ResourceSegments;
        if (segments.Count > 0 && segments[0] == "chat")
        {
            if (segments.Count == 1) return null; // Existing location without a saved Chat identity.
            if (segments.Count == 2 && Guid.TryParseExact(segments[1], "D", out var id) && id != Guid.Empty)
                return new(id, null);
            throw new FormatException("The saved Chat conversation address is invalid.");
        }
        if (segments.Count > 2 && segments[0] == "selected" && segments[2] == "chat")
        {
            if (segments.Count == 4 && Guid.TryParseExact(segments[1], "D", out var spaceId) && spaceId != Guid.Empty
                && Guid.TryParseExact(segments[3], "D", out var id) && id != Guid.Empty)
                return new(id, spaceId);
            throw new FormatException("The saved Space conversation address is invalid.");
        }
        return null;
    }

    private static AppContentAddress ConversationAddress(Conversation conversation)
    {
        // Temporary conversations remain session-only; never persist a saved
        // restoration target for an intentionally non-durable conversation.
        if (conversation.IsTemporary)
            return AppContentAddress.Parse(conversation.SpaceId is { } temporarySpace
                ? $"9to1://spaces/selected/{temporarySpace:D}" : "9to1://spaces");
        return AppContentAddress.Parse(conversation.SpaceId is { } spaceId
            ? $"9to1://spaces/selected/{spaceId:D}/chat/{conversation.Id:D}"
            : $"9to1://spaces/chat/{conversation.Id:D}");
    }

    /// <summary>The actual post-Home-admission initialization step. An explicit
    /// saved target is restored through the existing real Chat factory; unavailable
    /// or mismatched targets fail without creating a replacement conversation.</summary>
    internal async Task<bool> TryRestoreRequestedConversationAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        DemandAlive();
        if (ReadConversationRoute(_request.Address) is not { } route) return false;

        var conversations = _services.GetRequiredService<IConversationRepository>();
        var row = await conversations.GetAsync(route.ConversationId, token)
            ?? throw new InvalidOperationException("The saved Chat conversation is unavailable.");
        await ValidateSavedConversationAsync(row, route, token);
        // Restoration retains the same host recovery wiring as new/sidebar-opened chats.
        // A saved route must not turn the genuine Manage local models action into a no-op.
        var factory = NewChatSessionFactory();
        var chat = await factory.RestoreAsync(route.ConversationId, token)
            ?? throw new InvalidOperationException("The saved Chat conversation is no longer available.");
        _ownedChats.Add(chat); // Retain the real acquired page before validation/publication can fail.
        try
        {
            // RestoreAsync reads the canonical row itself. Validate THAT exact
            // returned target as well: membership may have changed during I/O.
            await ValidateSavedConversationAsync(chat.CurrentConversation, route, token);
            if (chat.CurrentConversation.SpaceId is { } spaceId && _spacesPage is { } spaces)
                await spaces.Controller.SelectSpaceAsync(spaceId, token);
            token.ThrowIfCancellationRequested();
            DemandAlive();
#if !ANDROID
            await RetireAssistantPresentationOnUiAsync();
#endif
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                DemandAlive();
                PublishChatPage(chat);
                _chatsBySpace[chat.CurrentConversation.SpaceId ?? Guid.Empty] = chat;
                ObserveConversationRoute(chat);
            }
            return true;
        }
        catch (Exception restoreFailure)
        {
#if !ANDROID
            UnwireChatAssistants(chat);
#endif
            UnobserveConversationRoute(chat);
            try { await chat.DisposeAsync(); _ownedChats.Remove(chat); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("The saved Chat could not be restored or retired cleanly.",
                    restoreFailure, cleanupFailure);
            }
            throw;
        }
    }

    private async Task ValidateSavedConversationAsync(Conversation row, SavedConversationRoute route, CancellationToken token)
    {
        if (row.Id != route.ConversationId || row.Mode != HavenMode.Chat || row.Kind != ConversationKind.Chat
            || row.IsTemporary || row.IsArchived || route.ExpectedSpaceId is { } expected && row.SpaceId != expected)
            throw new InvalidOperationException("The saved target is not an available Chat in this Space.");
        if (row.SpaceId is { } spaceId)
        {
            var space = await _services.GetRequiredService<SpaceRegistry>().GetAsync(spaceId, token);
            if (space is null || space.IsArchived)
                throw new InvalidOperationException("The saved Chat's Space is unavailable.");
        }
    }

    private void ObserveConversationRoute(NewChatPage chat)
    {
        if (_conversationRouteHandlers.ContainsKey(chat)) return;
        EventHandler handler = (_, _) =>
        {
            lock (_gate)
            {
                if (_closing || !_published || _publishedChatRoute is not { } current || !ReferenceEquals(current.Page, chat)) return;
                // CurrentConversation is a plain immutable domain record. The exact
                // page callback can update its route without reading native controls.
                _publishedChatRoute = new(chat, ConversationAddress(chat.CurrentConversation));
                _address = _publishedChatRoute.Address;
            }
            PublishStateChanged();
        };
        _conversationRouteHandlers.Add(chat, handler);
        chat.ConversationStateChanged += handler;
    }

    private void UnobserveConversationRoute(NewChatPage chat)
    {
        if (_conversationRouteHandlers.Remove(chat, out var handler)) chat.ConversationStateChanged -= handler;
    }
}
