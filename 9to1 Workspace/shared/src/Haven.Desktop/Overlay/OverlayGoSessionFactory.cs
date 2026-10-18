#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Creates the real production Go surface for Overlay and populates it with the same
/// catalogue/suggestion services used by the main Haven shell.
/// </summary>
internal sealed class OverlayGoSessionFactory
{
    private readonly HavenEventBus _bus;
    private readonly ICatalogRepository _catalog;
    private readonly CapabilityRegistryService _capabilities;
    private readonly IModeRegistry _modes;
    private readonly GoSuggestionService _suggestions;
    private readonly Func<GoPage> _createPage;

    public OverlayGoSessionFactory(
        HavenEventBus bus,
        ICatalogRepository catalog,
        CapabilityRegistryService capabilities,
        IModeRegistry modes,
        IConversationRepository conversations,
        IOllamaClient models,
        UserPreferencesService preferences,
        Func<GoPage>? createPage = null)
    {
        _bus = bus;
        _catalog = catalog;
        _capabilities = capabilities;
        _modes = modes;
        _suggestions = new GoSuggestionService(conversations, models, preferences);
        _createPage = createPage ?? (() => new GoPage(_bus));
    }

    public async Task<GoPage> CreateAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        var page = _createPage();
        try
        {
            ApplyOverlayPresentation(page);
            page.SetSuggestions(GoSuggestionService.ImmediateDefaults);
            await PopulateCatalogueAsync(page, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return page;
        }
        catch (Exception creationFailure)
        {
            // Catalogue acquisition has joined every raw task before releasing this
            // unpublished route. A canceled caller wait is never a drain witness.
            var failures = new List<Exception> { creationFailure };
            Task? close = null;
            try { close = page.CloseAndDrainAsync(); }
            catch (Exception cleanupFailure) { AddOriginalFailure(failures, cleanupFailure); }
            if (close is not null) await JoinOriginalAsync(close, failures);
            ThrowOriginalFailures(failures);
            throw;
        }
    }

    public async Task RefreshSuggestionsAsync(
        GoPage page,
        string activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.SetRefreshInProgress(true);
        try
        {
            var generated = await _suggestions.GenerateAsync(activity, cancellationToken).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => page.SetSuggestions(generated));
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => page.SetRefreshInProgress(false));
        }
    }

    private async Task PopulateCatalogueAsync(GoPage page, CancellationToken cancellationToken)
    {
        var actualSources = new List<Task>();
        var failures = new List<Exception>();
        Task<IReadOnlyList<AgentDefinition>>? agentsTask = null;
        Task<IReadOnlyList<CapabilityDefinition>>? capabilityTask = null;
        Task<IReadOnlyList<PromptDefinition>>? promptsTask = null;
        Task<IReadOnlyList<ModeDefinition>>? appsTask = null;
        T Retain<T>(T actual) where T : Task
        {
            ArgumentNullException.ThrowIfNull(actual);
            actualSources.Add(actual); // Retain before the next acquisition can throw.
            return actual;
        }
        try
        {
            agentsTask = Retain(_catalog.GetAgentsAsync(cancellationToken));
            capabilityTask = Retain(_capabilities.DiscoverAsync(
                OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Linux, cancellationToken));
            promptsTask = Retain(_catalog.GetPromptsAsync(cancellationToken));
            appsTask = Retain(_modes.GetModesAsync(cancellationToken));
        }
        catch (Exception acquisitionFailure) { AddOriginalFailure(failures, acquisitionFailure); }
        // Do not abandon earlier sources when a later factory throws, cancel a
        // wrapper wait, or lose siblings through WhenAll's single await exception.
        foreach (var actual in actualSources) await JoinOriginalAsync(actual, failures);
        ThrowOriginalFailures(failures);
        cancellationToken.ThrowIfCancellationRequested();
        page.SetAddCatalogue(
            agentsTask!.GetAwaiter().GetResult(),
            capabilityTask!.GetAwaiter().GetResult(),
            promptsTask!.GetAwaiter().GetResult(),
            appsTask!.GetAwaiter().GetResult());
    }

    private static async Task JoinOriginalAsync(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddOriginalFailure(failures, cause);
            else AddOriginalFailure(failures, observed);
        }
    }

    private static void AddOriginalFailure(List<Exception> failures, Exception cause)
    { if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause); }

    private static void ThrowOriginalFailures(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0)
            throw new AggregateException("Original Go creation or unpublished-page cleanup failed.", failures);
    }

    private static void ApplyOverlayPresentation(GoPage page)
    {
        page.SceneRoot.SetValue(HavenProperties.Background, "SurfaceRaised");
        page.SceneRoot.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(26)));
        page.SceneRoot.SetValue(HavenProperties.Shadow, "Card");
        page.SceneRoot.SetValue(HavenProperties.Clip, true);
    }
}
#endif
