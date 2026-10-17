#if !ANDROID
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Overlay;

/// <summary>Creates real production Chat surfaces for independent Overlay sessions.</summary>
internal sealed class OverlayChatSessionFactory(
    IServiceProvider services,
    IConversationRepository conversations,
    CapabilityRegistryService capabilities,
    ICatalogRepository catalog,
    IModeRegistry modes)
{
    public Task<NewChatPage> CreateNewAsync(CancellationToken cancellationToken) =>
        CreatePageAsync(page => page.StartFreshConversationAsync(), cancellationToken);

    public async Task<NewChatPage?> RestoreAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        var conversation = await conversations.GetAsync(conversationId, cancellationToken);
        if (conversation is null) return null;
        return await CreatePageAsync(page => page.LoadConversationAsync(conversation), cancellationToken);
    }

    private async Task<NewChatPage> CreatePageAsync(
        Func<NewChatPage, Task> selectConversation,
        CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        var page = ActivatorUtilities.CreateInstance<NewChatPage>(services);
        try
        {
            // Construction starts original model discovery bound to the initial conversation.
            // Join it before Fresh/Load replaces that target; keep all UI work on this dispatcher.
            await page.OriginalInitialization.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await selectConversation(page);
            cancellationToken.ThrowIfCancellationRequested();
            await PopulateCatalogueAsync(page, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return page;
        }
        catch (Exception creationFailure)
        {
            // Cancellation of our wait does not mean the original initialization has stopped.
            // Drain the acquired, unpublished page, retaining both failures if cleanup also fails.
            try
            {
                await page.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Chat creation failed and the unpublished page did not retire cleanly.",
                    creationFailure, cleanupFailure);
            }
            throw;
        }
    }

    public async Task<CapabilityDefinition?> FindCapabilityAsync(OverlayContextActionDescriptor action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.ImplementationKey)) return null;
        var available = await capabilities.DiscoverAsync(OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Linux, cancellationToken);
        return available.FirstOrDefault(capability =>
            capability.ImplementationKey.Equals(action.ImplementationKey, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PopulateCatalogueAsync(NewChatPage page, CancellationToken cancellationToken)
    {
        var agents = await catalog.GetAgentsAsync(cancellationToken);
        var prompts = await catalog.GetPromptsAsync(cancellationToken);
        var availableCapabilities = await capabilities.DiscoverAsync(OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Linux, cancellationToken);
        var apps = await modes.GetModesAsync(cancellationToken);
        page.SetAddCatalogue(agents, availableCapabilities, prompts, apps);
    }
}
#endif
