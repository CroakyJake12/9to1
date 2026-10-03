using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.Controls;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeSpacesPage? _spacesPage;
    private SpaceRegistry? _spaceRegistry;

    private SpaceRegistry SpacesRegistry => _spaceRegistry ??= new SpaceRegistry(_versionedSettings, CaptureSpaceWriteAdmissionAsync);

    private async Task OpenSpacesAsync()
    {
        var workspace = await GetOwnedSpacesWorkspaceAsync(CancellationToken.None);
        foreach (var pending in await workspace.Registry.ReadPendingDeletionsAsync(CancellationToken.None))
            await workspace.Deletion.ResumeAsync(pending.OperationId, CancellationToken.None);
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
            OpenSpaceCanonicalSourceAsync, workspace.RequireCurrentAccessAsync);

        AddOrSelectTab("spaces", "Spaces", _spacesPage, false, HavenSurface.Spaces);
        await _spacesPage.ActivateAsync(CancellationToken.None);
        ApplyShellVisualState();
    }

    private async Task OpenSpaceCanonicalSourceAsync(SpaceDefinition space, SpaceContextReference source)
    {
        if (source.HostedFileId is not { } fileId || source.Kind is not (SpaceContextReferenceKind.CanvasArtifact or SpaceContextReferenceKind.PictureArtifact or SpaceContextReferenceKind.GamesProject or SpaceContextReferenceKind.WriteArtifact))
            throw new NotSupportedException("This source has no available native owning-app surface.");
        var services = global::Haven.Desktop.App.Services ?? throw new InvalidOperationException("Home services are unavailable.");
        var files = services.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var workspace = await files.GetCurrentAsync() ?? throw new UnauthorizedAccessException("Home has not authorised Files storage.");
        var metadata = await workspace.Provider.GetAsync(new(fileId), CancellationToken.None);
        if (!metadata.IsSuccess || metadata.Value!.CurrentRevisionId is not { } revision)
            throw new InvalidOperationException("The canonical Files source is unavailable.");
        var actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
        var resources = services.GetRequiredService<ResourceAuthorizationService>();
        var route = new SpaceFilesArtifactAction(space.Id, space.Revision, source.ContextId, fileId,
            source.CanonicalEntityId, revision, false);
        var router = new SpaceFilesArtifactActionRouter(SpacesRegistry, files, actors, resources);
        var reader = services.GetRequiredService<NativeFilesArtifactContentReader>();
        var home = services.GetRequiredService<HomeCoreRuntime>();
        Control surface;
        Func<Task> initialize;
        if (source.Kind == SpaceContextReferenceKind.CanvasArtifact)
        {
            var canvas = new SpaceCanvasCuiSurface(route, router, reader, home, actors, resources);
            surface = canvas;
            initialize = () => canvas.InitializeAsync();
        }
        else if (source.Kind == SpaceContextReferenceKind.PictureArtifact)
        {
            var picture = new SpacePictureCuiSurface(route, router, reader,
                services.GetRequiredService<NativeFilesMediaAssetSourceResolver>(), home, actors, resources,
                services.GetRequiredService<IMotionPreferenceSource>());
            surface = picture;
            initialize = () => picture.InitializeAsync();
        }
        else if (source.Kind == SpaceContextReferenceKind.WriteArtifact)
        {
            var write = new SpaceWriteCuiSurface(route, router, files,
                services.GetRequiredService<IWriteNativeDocumentPackageStore>(), home, actors, resources);
            surface = write;
            initialize = () => write.InitializeAsync();
        }
        else
        {
            var capability = await services.GetRequiredService<Haven.Infrastructure.Games.GamesInstalledRuntimeResolver>().ResolveAsync();
            var sessions = capability.Runtime is { } runtime ? new Haven.Application.Games.GamesSceneSessionService(resources,
                services.GetRequiredService<Haven.Application.Games.ICanonicalGamesSceneSource>(), runtime) : null;
            var games = new SpaceGamesCuiSurface(route, router,
                services.GetRequiredService<Haven.Application.Games.GamesProjectEditorService>(), sessions, home, actors, resources);
            surface = games;
            initialize = () => games.InitializeAsync();
        }
        try
        {
            await initialize();
            AddOrSelectTab($"space-source-{space.Id:N}-{source.ContextId:N}-{Guid.NewGuid():N}", metadata.Value.Name,
                surface, true, HavenSurface.Spaces, forceNewTab: true);
            ApplyShellVisualState();
        }
        catch { ((IDisposable)surface).Dispose(); throw; }
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
            await _newChatPage.StartFreshConversationAsync(HavenMode.Chat, null, spaceId: space.Id);
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

    private async Task DeleteSpaceAsync(SpaceDefinition displayed)
    {
        var workspace = await GetOwnedSpacesWorkspaceAsync(CancellationToken.None);
        var spaceId = displayed.Id;
        var operation = await workspace.Deletion.BeginAsync(spaceId, displayed.Revision, Guid.NewGuid(), CancellationToken.None);
        if (operation.Stage != SpaceDeletionStage.Complete)
            throw new InvalidOperationException("Space deletion remains pending. Reopen Spaces to resume recovery.");
        if (_newChatPage?.CurrentConversation.SpaceId == spaceId)
        {
            var current = await _conversations.GetAsync(_newChatPage.CurrentConversation.Id, CancellationToken.None);
            if (current is not null) await _newChatPage.LoadConversationAsync(current);
        }
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
