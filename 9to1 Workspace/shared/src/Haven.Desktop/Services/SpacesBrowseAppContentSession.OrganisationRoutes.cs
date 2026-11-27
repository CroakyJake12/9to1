using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private sealed record PublishedOrganisationRoute(SpacesOrganisationPage Page, AppContentAddress Address,
        SpacesHomeSelection Selection, SpaceDefinition Space, HistoryHomeAuthority Authority, HomeNativeWindowsComposition Composition,
        NewChatPage? PriorChat, Guid? PriorConversationId);
    private PublishedOrganisationRoute? _publishedOrganisationRoute;
    private readonly List<SpacesOrganisationPage> _ownedOrganisations = [];

    private bool IsOrganisationCurrent(SpacesOrganisationPage page) => IsPublicationCurrent &&
        _activeDetailsPreparation is null && _publishedOrganisationRoute is { } route && ReferenceEquals(route.Page, page) &&
        ReferenceEquals(_spacesPage?.SpaceWorkspaceContent, page) &&
        ReferenceEquals(_spacesPage.Controller.State.Selection, route.Selection);

    private async Task<SpacesOrganisationReadAuthority?> AdmitOrganisationAsync(SpacesOrganisationPage page, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsOrganisationCurrent(page)) return null;
        var route = _publishedOrganisationRoute!;
        if (!ReferenceEquals(_services.GetRequiredService<HomeNativeWindowsComposition>(), route.Composition)) return null;
        try
        {
            var authority = await ReadHistoryHomeAuthorityAsync(token);
            var space = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(route.Space.Id, token);
            var finalAuthority = await ReadHistoryHomeAuthorityAsync(token);
            if (!IsOrganisationCurrent(page) || !ReferenceEquals(_publishedOrganisationRoute, route) ||
                authority.Actor != route.Authority.Actor || finalAuthority != authority || space is not { IsArchived: false } ||
                space.Revision != route.Space.Revision) return null;
            return new(route.Composition, authority.Actor, authority.ServicesRevision);
        }
        catch (SpacesHistoryAuthorityRefusal) { return null; } // Expected denial; genuine source failures still propagate.
    }

    private async Task OpenOrganisationOnUiAsync(Guid spaceId, SpacesOrganisationKind kind, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var home = _spacesPage ?? throw new SpacesWorkspaceActionRefusal("The original Spaces sidebar is unavailable.");
        var selected = home.Controller.State.Selection;
        if (selected is null || selected.Id != spaceId || !SpacesHomePage.SupportsOrganisationDestinations(spaceId))
            throw new SpacesWorkspaceActionRefusal("Choose a supported conversation Space before opening this catalogue.");
        if (_publishedAssistantRoute is not null)
            throw new SpacesWorkspaceActionRefusal("Return from the Assistant workspace before opening this conversation catalogue.");
        var epoch = _routeEpoch;
        var authority = await ReadHistoryHomeAuthorityAsync(token);
        var space = await _services.GetRequiredService<SpaceRegistry>().ReadExistingAsync(spaceId, token);
        if (space is null || space.IsArchived)
            throw new SpacesWorkspaceActionRefusal("The selected Space is unavailable.");
        var finalAuthority = await ReadHistoryHomeAuthorityAsync(token);
        token.ThrowIfCancellationRequested(); DemandAlive();
        if (!ReferenceEquals(home, _spacesPage) || !ReferenceEquals(home.Controller.State.Selection, selected) ||
            _routeEpoch != epoch || authority != finalAuthority)
            throw new SpacesWorkspaceActionRefusal("The Space or profile changed before this catalogue could open.");
        var priorChat = _publishedOrganisationRoute?.PriorChat ?? _chatPage;
        if (priorChat?.CurrentConversation.SpaceId != spaceId) priorChat = null;
        var priorId = priorChat?.ConversationId;
        SpacesOrganisationPage? page = null;
        bool Current() => page is not null && IsOrganisationCurrent(page);
        Task<SpacesOrganisationReadAuthority?> Admit(CancellationToken caller) => page is null
            ? Task.FromResult<SpacesOrganisationReadAuthority?>(null) : AdmitOrganisationAsync(page, caller);
        async Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> Conversation(
            HomeModelPickerSpecialistSelection? choice, CancellationToken caller)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (priorChat is null || priorId is null || !Current() || priorChat.ConversationId != priorId)
                return new(false, "ConversationUnavailable", "The original conversation changed. Return to it and reopen Specialists.");
            var currentAuthority = await Admit(caller);
            if (currentAuthority is null)
                return new(false, "ConversationUnavailable", "The original Space or Home authority changed. Reopen Specialists.");
            return await priorChat.ReadModelPickerConversationAsync(_services.GetRequiredService<ICatalogRepository>(),
                _services.GetRequiredService<HomeNativeWindowsComposition>().Profiles, authority.Actor, priorId.Value,
                () => Current() && priorChat.ConversationId == priorId && _ownedChats.Contains(priorChat) &&
                    currentAuthority.Composition.Runtime.Current.Revision == currentAuthority.ServicesRevision, choice, caller);
        }
        page = new(kind, spaceId, _services.GetRequiredService<IContainerRepository>(),
            _services.GetRequiredService<IConversationRepository>(), _services.GetRequiredService<IContainerResourceRepository>(),
            _services.GetRequiredService<ICatalogRepository>(), Current, Admit,
            () => page is not null && ReturnFromOrganisation(page),
            _hostQueueNavigation is null ? null : (member, settlement, caller) => QueueOrganisationMemberAsync(page!, member, settlement, caller),
            priorChat is null ? null : Conversation, _interfaceTheme);
        _ownedOrganisations.Add(page); // Capture before mounting or activating any real source.
        var address = AppContentAddress.Parse($"9to1://spaces/selected/{spaceId:D}/{(kind == SpacesOrganisationKind.Groups ? "groups" : "specialists")}");
        try
        {
            if (!home.TryMountSpaceWorkspace(page))
                throw new SpacesWorkspaceActionRefusal("The original Spaces workspace region is unavailable.");
            SetRootContent(home);
            lock (_gate)
            {
                _publishedOrganisationRoute = new(page, address, selected, space, authority,
                    _services.GetRequiredService<HomeNativeWindowsComposition>(), priorChat, priorId);
                _publishedChatRoute = null; _chatPage = null; _publishedDetailsRoute = null; _publishedBuilderRoute = null;
                _address = address; _routeEpoch++;
            }
            home.NotifyNavigationPublished(); PublishStateChanged();
            await page.ActivateAsync(token);
        }
        catch (Exception failure)
        {
            // An activated page retains its genuine failed source and close custody.
            // A constructor/mount failure that acquired no source is retired immediately.
            if (page.OriginalInitialization is null)
            {
                try { await page.CloseAndDrainAsync(); }
                catch (Exception cleanupFailure)
                { throw new AggregateException("The organisation destination failed and its original cleanup also failed.", failure, cleanupFailure); }
                _ownedOrganisations.Remove(page);
                if (ReferenceEquals(home.SpaceWorkspaceContent, page)) home.ClearSpaceWorkspace(page);
            }
            throw;
        }
    }

    private bool ReturnFromOrganisation(SpacesOrganisationPage page)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsOrganisationCurrent(page) || _publishedOrganisationRoute is not { } route || _spacesPage is not { } home)
            return false;
        if (route.PriorChat is { } chat)
        {
            if (!_ownedChats.Contains(chat) || chat.ConversationId != route.PriorConversationId)
                return false; // Never restore a replacement conversation under a retained target.
            lock (_gate) PublishChatPage(chat);
        }
        else
        {
            home.ClearSpaceWorkspace(page); DeactivatePublishedOrganisation();
            lock (_gate) { _routeEpoch++; UpdateAddress(home.Controller.State.Selection?.Id); }
            SetRootContent(home);
        }
        PublishStateChanged(); return true;
    }

    private async Task<bool> QueueOrganisationMemberAsync(SpacesOrganisationPage page, SpacesGroupMember member,
        Task settlement, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_hostQueueNavigation is null) return false;
        var authority = await AdmitOrganisationAsync(page, token);
        if (authority is null) return false;
        var route = _publishedOrganisationRoute!;
        var groups = await _services.GetRequiredService<IContainerRepository>().GetByModeAsync(HavenMode.Chat, token);
        if (groups.Count(group => group.Id == member.Group.Id) != 1) return false;
        var group = groups.Single(group => group.Id == member.Group.Id);
        var row = await _services.GetRequiredService<IConversationRepository>().GetAsync(member.Conversation.Id, token);
        var finalGroups = await _services.GetRequiredService<IContainerRepository>().GetByModeAsync(HavenMode.Chat, token);
        if (await AdmitOrganisationAsync(page, token) != authority || !ReferenceEquals(_publishedOrganisationRoute, route) ||
            finalGroups.Count(item => item.Id == group.Id) != 1 || finalGroups.Single(item => item.Id == group.Id) != group ||
            group.IsArchived || group.Mode != HavenMode.Chat || SpacesOrganisationPage.GroupTarget(group) != member.Group ||
            row != member.Conversation || row is null || row.ContainerId != group.Id || row.IsArchived || row.IsTemporary ||
            row.Mode != HavenMode.Chat || row.Kind != ConversationKind.Chat ||
            !(row.SpaceId == route.Space.Id || row.SpaceId is null && route.Space.Id == SpaceRegistry.ChatSpaceId)) return false;
        var address = AppContentAddress.Parse(row.SpaceId is { } spaceId
            ? $"9to1://spaces/selected/{spaceId:D}/chat/{row.Id:D}" : $"9to1://spaces/chat/{row.Id:D}");
        // The host waits for the real page action and loader before admitting the destination.
        // A new tab preserves the original conversation, input and active run in this tab.
        return _hostQueueNavigation(new(_request.TabId, address, OpenInNewTab: true), settlement);
    }

    private void DeactivatePublishedOrganisation()
    {
        if (_publishedOrganisationRoute is not { } current) return;
        current.Page.Deactivate(); _publishedOrganisationRoute = null;
    }

    private Task BeginOrganisationRetirementAsync(List<Exception> failures) => OnUiAsync(() =>
    {
        // Signal the same page owners before joining a close preparation that may itself
        // be waiting for a real catalogue read to acknowledge their cancellation.
        foreach (var page in _ownedOrganisations.ToArray())
            try { page.RequestRetirement(); }
            catch (Exception failure) { failures.Add(failure); }
        return Task.CompletedTask;
    });

    private async Task<bool> TryRestoreRequestedOrganisationAsync(CancellationToken token)
    {
        var parts = _request.Address.ResourceSegments;
        if (parts.Count < 3 || parts[0] != "selected" || parts[2] is not ("groups" or "specialists")) return false;
        if (parts.Count != 3 || !Guid.TryParseExact(parts[1], "D", out var spaceId) || spaceId == Guid.Empty)
            throw new FormatException("The saved Spaces organisation address is invalid.");
        await OpenOrganisationOnUiAsync(spaceId, parts[2] == "groups" ? SpacesOrganisationKind.Groups : SpacesOrganisationKind.Specialists, token);
        return true;
    }
}
