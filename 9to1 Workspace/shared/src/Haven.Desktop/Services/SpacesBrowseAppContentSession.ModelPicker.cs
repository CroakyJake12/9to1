using Avalonia.Threading;
using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    internal bool IsConversationPickerTargetCurrent(OriginalModelTarget target, AuthenticatedResourceActor actor)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
            return _published && !_closing && !_token.IsCancellationRequested && actor == _historyOriginalActor &&
                string.Equals(actor.ProfileId, _request.ProfileId, StringComparison.Ordinal) &&
                _publishedChatRoute is { } route && ReferenceEquals(route.Page, target.Page) &&
                ReferenceEquals(_chatPage, target.Page) && target.Page.ConversationId == target.ConversationId;
    }

    internal Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadConversationPickerAsync(
        OriginalModelTarget target, AuthenticatedResourceActor actor, Func<bool> registeredOwnerCurrent,
        HomeModelPickerSpecialistSelection? selection, CancellationToken observerToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        bool Current() => registeredOwnerCurrent() && IsConversationPickerTargetCurrent(target, actor);
        if (!Current()) return Task.FromResult(new HomeCoreOperationResult<HomeModelPickerConversationSnapshot>(false,
            "ConversationUnavailable", "The originating Chat is no longer published by this Spaces tab."));
        return target.Page.ReadModelPickerConversationAsync(_services.GetRequiredService<ICatalogRepository>(),
            _services.GetRequiredService<HomeNativeWindowsComposition>().Profiles, actor, target.ConversationId,
            Current, selection, observerToken);
    }
}
