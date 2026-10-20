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
    /// <summary>Optional typed host seam applied to every page the factory creates. Hosts use
    /// it to wire page events (for example the local-model recovery request) to host-owned
    /// navigation, so the real Spaces/Overlay composition routes recovery instead of relying on
    /// the old MainView path alone.</summary>
    public Action<NewChatPage>? WireHost { get; set; }

    public Task<NewChatPage> CreateNewAsync(CancellationToken cancellationToken) =>
        CreatePageAsync(page => page.StartFreshConversationAsync(), cancellationToken);

    /// <summary>Creates the same real production page but joins the caller's own observation of
    /// the construction discovery AFTER that exact original settles and BEFORE Fresh replaces the
    /// initial conversation target. The two real steps stay unchanged; an owner that must observe
    /// the construction publication itself (a held-retry visual-gate assertion) joins here instead
    /// of racing a background publication against the target replacement.</summary>
    internal Task<NewChatPage> CreateNewWithConstructionJoinAsync(
        Func<NewChatPage, Task> joinConstruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(joinConstruction);
        return CreatePageAsync(page => page.StartFreshConversationAsync(), cancellationToken, joinConstruction);
    }

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
        CancellationToken cancellationToken,
        Func<NewChatPage, Task>? joinConstruction = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        // Overlay chat must use the SAME routed catalogue as the normal Haven chat page. The
        // shared IProviderModelClient is the resilient routed client, not the raw Ollama
        // transport. Inject it explicitly so NewChatPage's IOllamaClient slot binds to the
        // routed source instead of resolving the raw Ollama registration in the container.
        var routed = services.GetRequiredService<IProviderModelClient>();
        var page = ActivatorUtilities.CreateInstance<NewChatPage>(services, routed);
        try
        {
            if (WireHost is not null) WireHost(page);
            // Construction starts original model discovery bound to the initial conversation.
            // Join it before Fresh/Load replaces that target; keep all UI work on this dispatcher.
            await page.OriginalInitialization.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Settle every construction original (including the discovery publication) BEFORE the
            // real Fresh/Load replaces the conversation target. This is the production admission
            // itself; a background publication never races the target replacement.
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (joinConstruction is not null) await joinConstruction(page);
            cancellationToken.ThrowIfCancellationRequested();
            await selectConversation(page);
            cancellationToken.ThrowIfCancellationRequested();
            // Settle the transition's OWN admitted publications (transcript, scroll, draft
            // restore) before the caller receives the page, so a stale queued publication can
            // never race the next target replacement or an external close.
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(cancellationToken);
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
