using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeSpacesPage? _spacesPage;
    private SpaceRegistry? _spaceRegistry;
    private OwnedSpacesSession? _ownedSpacesSession;
    private readonly Dictionary<Guid, RevisionBankPage> _revisionBankPages = [];

    private SpaceRegistry SpacesRegistry
    {
        get
        {
            var owner = RequireOriginalSpacesOwner();
            if (!ReferenceEquals(_spaceRegistry, owner.Registry))
                throw new UnauthorizedAccessException("The original guarded Spaces registry is unavailable.");
            return owner.Registry;
        }
    }

    /// <summary>Called only by the actual trusted in-process composition. This never creates
    /// a provider, actor, receipt or Space write decision; missing registration refuses.</summary>
    public void ConfigureOriginalSpacesOwner(IServiceProvider originalProvider)
    {
        if (_spacesPage is not null || _revisionBankPages.Count != 0)
            throw new InvalidOperationException("Retire acquired Spaces pages before replacing their original owner.");
        var owner = OwnedSpacesSession.AttachOriginal(originalProvider, _versionedSettings);
        _spaceRegistry = owner.Registry;
        _ownedSpacesSession = owner;
    }

    private OwnedSpacesSession RequireOriginalSpacesOwner()
    {
        var owner = _ownedSpacesSession
            ?? throw new UnauthorizedAccessException("The configured original Spaces action owner is unavailable.");
        owner.RequireCurrent();
        if (!ReferenceEquals(owner, _ownedSpacesSession))
            throw new UnauthorizedAccessException("The original Spaces owner changed during observation.");
        return owner;
    }

    private async Task OpenSpacesAsync()
    {
        var originalOwner = RequireOriginalSpacesOwner();
        _spacesPage ??= new NativeSpacesPage(
            SpacesRegistry,
            new SpaceGeneratedSurfaceRenderer(
                _genUiRouter,
                _genUiInstances,
                _checklistTemplate,
                _dataGridTemplate,
                _cardDeckTemplate,
                _dashboardTemplate,
                _assessmentTemplate,
                _workflowTemplate,
                _customTemplate),
            new SpaceEditPlanner(_ollama, () => _preferences.DefaultModel),
            LaunchSpaceAsync,
            DeleteSpaceAsync,
            OpenSpaceLayoutAsync,
            _conversations,
            OpenSpaceConversationAsync,
            OpenRevisionBankAsync);

        if (!ReferenceEquals(originalOwner, RequireOriginalSpacesOwner()))
            throw new UnauthorizedAccessException("The original Spaces owner changed before publication.");
        AddOrSelectTab("spaces", "Spaces", _spacesPage, false, HavenSurface.Spaces);
        await _spacesPage.ActivateAsync(CancellationToken.None);
        ApplyShellVisualState();
    }

    private async Task OpenRevisionBankAsync(SpaceDefinition space)
    {
        var originalOwner = RequireOriginalSpacesOwner();
        if (space.IsArchived) throw new InvalidOperationException("Restore this Space before editing its Bank.");
        foreach (var finished in _revisionBankPages.Where(item =>
            item.Value.OriginalCloseTask is { IsCompletedSuccessfully: true }).ToArray())
            if (ReferenceEquals(_revisionBankPages.GetValueOrDefault(finished.Key), finished.Value))
                _revisionBankPages.Remove(finished.Key);
        if (_revisionBankPages.TryGetValue(space.Id, out var retiring) && retiring.IsRetiring)
        {
            await retiring.CloseAndDrainAsync();
            if (ReferenceEquals(_revisionBankPages.GetValueOrDefault(space.Id), retiring))
                _revisionBankPages.Remove(space.Id);
        }
        _revisionBankPages.TryGetValue(space.Id, out var page);
        var acquiredHere = false;
        try
        {
            if (page is null)
            {
                if (_revisionBankPages.Count >= 128)
                    throw new InvalidOperationException("Original Bank page capacity is full; pending and failed owners were retained.");
                page = new RevisionBankPage(originalOwner, () => _ownedSpacesSession, space.Id);
                acquiredHere = true;
                _revisionBankPages.Add(space.Id, page);
            }
            if (!ReferenceEquals(originalOwner, RequireOriginalSpacesOwner()))
                throw new UnauthorizedAccessException("The original Bank owner changed before tab publication.");
            AddOrSelectTab("revision-bank-" + space.Id.ToString("N"), space.Name + " Bank", page, false, HavenSurface.Spaces);
            await page.ActivateAsync(CancellationToken.None);
            if (!ReferenceEquals(originalOwner, RequireOriginalSpacesOwner()))
                throw new UnauthorizedAccessException("The original Bank owner changed after activation.");
            ApplyShellVisualState();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (acquiredHere && page is not null)
            {
                try
                {
                    var originalClose = page.CloseAndDrainAsync();
                    await originalClose;
                    if (ReferenceEquals(_revisionBankPages.GetValueOrDefault(space.Id), page))
                        _revisionBankPages.Remove(space.Id);
                }
                catch (Exception close)
                {
                    if (!failures.Any(previous => ReferenceEquals(previous, close))) failures.Add(close);
                }
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
            throw new AggregateException(failures);
        }
    }

    private async Task LaunchSpaceAsync(SpaceDefinition space)
    {
        var plan = SpaceLaunchPolicy.Resolve(space);
        if (plan.Destination == SpaceLaunchDestination.StudyProduct)
        {
            await OpenStudyHomeAsync();
            return;
        }

        if (_nativeChatSidebar is not null)
        if (_nativeChatSidebar is not null)
        {
            _nativeChatSidebar.SetMode(HavenMode.Chat);
            await _nativeChatSidebar.SelectSpaceFromShellAsync(space.Id);
            ApplyShellVisualState();
            return;
        }

        await SpacesRegistry.SetCurrentSpaceIdAsync(space.Id, CancellationToken.None);
        await OpenNewChatAsync();
        if (_newChatPage is null) return;

        var existing = (await _conversations.GetRecentAsync(HavenMode.Chat, int.MaxValue, CancellationToken.None))
            .Where(item => !item.IsArchived && item.Kind != ConversationKind.Call && item.SpaceId == space.Id)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            await _newChatPage.LoadConversationAsync(existing);
        }
        else
        {
            await _newChatPage.StartFreshConversationAsync(HavenMode.Chat, null);
            await _newChatPage.AssignSpaceAsync(space.Id);
            _newChatPage.ConfigureRegisteredContext(plan.RegisteredContext, plan.EffortOverride);
            if (plan.Files.Count > 0)
                await _newChatPage.AddFilesAsync(plan.Files.Select(file => file.Path));
        }

        if (!string.IsNullOrWhiteSpace(plan.ModelName))
        {
            try
            {
                var models = await _ollama.GetModelsAsync(CancellationToken.None);
                var selected = models.FirstOrDefault(model => model.Name.Equals(plan.ModelName, StringComparison.OrdinalIgnoreCase));
                if (selected is not null) _newChatPage.SelectModel(selected);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
            {
                // Keep Chat's normal model fallback when the preferred model is unavailable.
            }
        }
        ApplyShellVisualState();
    }

    private async Task DeleteSpaceAsync(Guid spaceId)
    {
        var conversations = await _conversations.GetRecentAsync(HavenMode.Chat, int.MaxValue, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        foreach (var conversation in conversations.Where(item => item.SpaceId == spaceId))
            await _conversations.UpsertConversationAsync(conversation with { SpaceId = null, UpdatedAt = now }, CancellationToken.None);

        if (_newChatPage?.CurrentConversation.SpaceId == spaceId)
            await _newChatPage.AssignSpaceAsync(null);

        await SpacesRegistry.DeleteAsync(spaceId, CancellationToken.None);
        if (_nativeChatSidebar is not null)
            await _nativeChatSidebar.ReloadSpaceScopeAsync();
    }

    private async Task OpenSpaceConversationAsync(Conversation conversation)
    {
        var page = CreateNewChatPage();
        await ConfigureAddMenuAsync(page);
        await page.LoadConversationAsync(conversation);
        AddOrSelectTab(
            $"space-chat-{conversation.Id:N}",
            conversation.Title,
            page,
            false,
            HavenSurface.Spaces,
            forceNewTab: true);
        page.FocusComposer();
        ApplyShellVisualState();
    }
    private Task OpenSpaceLayoutAsync(SpaceDefinition space)
    {
        var page = new SpaceLayoutEditorPage(SpacesRegistry, space);
        AddOrSelectTab(
            $"space-layout-{space.Id:N}",
            $"{space.Name} layout",
            page,
            true,
            HavenSurface.Spaces,
            forceNewTab: true);
        ApplyShellVisualState();
        return Task.CompletedTask;
    }
}
