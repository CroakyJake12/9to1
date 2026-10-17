using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeSpacesPage? _spacesPage;
    private SpaceRegistry? _spaceRegistry;

    private SpaceRegistry SpacesRegistry => _spaceRegistry ??= new SpaceRegistry(_versionedSettings);

    private async Task OpenSpacesAsync()
    {
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
            OpenSpaceConversationAsync);

        AddOrSelectTab("spaces", "Spaces", _spacesPage, false, HavenSurface.Spaces);
        await _spacesPage.ActivateAsync(CancellationToken.None);
        ApplyShellVisualState();
    }

    private async Task LaunchSpaceAsync(SpaceDefinition space)
    {
        if (space.Id == SpaceRegistry.AgentSpaceId)
        {
#if !ANDROID
            await OpenOriginalRegisteredAssistantsAsync();
#else
            _notifications.Show("Assistants setup required", "This host has no configured Assistant owner. Your existing data is preserved.", ToastKind.Warning, TimeSpan.FromSeconds(6));
#endif
            return;
        }
        if (space.Kind == SpaceKind.Tasks)
        {
            await OpenOriginalCanonicalTaskSpaceAsync(space.Id);
            return;
        }
        var plan = SpaceLaunchPolicy.Resolve(space);
        if (plan.Destination == SpaceLaunchDestination.StudyProduct)
        {
            await OpenStudyHomeAsync();
            return;
        }

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
        if (conversation.Mode == HavenMode.Tasks && conversation.SpaceId is not null)
        {
            await OpenOriginalSpaceTaskConversationAsync(conversation);
            return;
        }
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

    /// <summary>
    /// Opens the CUI Chat Space page in a tab. This is the M3 shell-wiring validation surface;
    /// <see cref="NewChatPage"/> remains the live Chat surface until the CUI page demonstrably
    /// covers threading settings, send queueing, dual-model selection, attachments, follow-at-end
    /// scrolling and Select bound-value display.
    /// </summary>
    internal async Task OpenChatSpacePageAsync(Guid? conversationId = null)
    {
        var services = Haven.Desktop.App.Services;
        var backend = new HavenChatSpaceBackend(
            _conversations,
            services?.GetRequiredService<IConversationProductionRepository>()
                ?? throw new InvalidOperationException("The Chat Space backend requires conversation production."),
            _conversationVersioning,
            services?.GetRequiredService<IMessageAttachmentService>()
                ?? throw new InvalidOperationException("The Chat Space backend requires attachment services."),
            new HavenChatSpaceModelCatalog(_ollama, _preferences),
            new HavenChatSpaceTurnExecutor(_sessions, _preferences,
                capabilities: services?.GetService<ICapabilityRepository>(), attachments: services?.GetService<IMessageAttachmentService>()),
            _conversationSafety);

        DemandCanonicalPageAcquisition();
        var page = new ChatSpacePage(backend, ChatSpacePage.ImmediateReadinessInstance, conversationId,
            clipboard: services?.GetService<IPlatformShellService>());
        // A fresh page per open is not reachable from a shell field, so it must join the retained
        // cohort or its retirement would never be joined and the native CUI host would leak.
        RetainOriginalCanonicalPage(page);
        AddOrSelectTab(
            conversationId is { } id ? $"space-chat-cui-{id:N}" : $"space-chat-cui-{Guid.NewGuid():N}",
            "Chat Space (CUI)",
            page,
            true,
            HavenSurface.Spaces,
            forceNewTab: true);
        await page.ActivateAsync(CancellationToken.None);
        ApplyShellVisualState();
    }
}
