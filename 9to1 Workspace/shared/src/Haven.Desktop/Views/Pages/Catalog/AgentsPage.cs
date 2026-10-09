using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>Thin Avalonia backend host for the Haven.UI Agents management scene.</summary>
public sealed class AgentsPage : UserControl, IDisposable, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly AgentsHavenScene _scene;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    internal Task? OriginalClose => _originalWork.OriginalClose;

    public AgentsPage(CatalogPageViewModel viewModel, AgentTaskRuntimeService? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (viewModel.Kind != CatalogPageKind.Agents)
            throw new ArgumentException("AgentsPage requires an Agents catalogue view-model.", nameof(viewModel));

        _scene = new AgentsHavenScene(viewModel, runtime);
        Scene = new HavenSceneControl { Root = _scene.Root };
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalAsync, CleanupOriginalAsync);
        AutomationProperties.SetAutomationId(this, "HavenNativeAgentsPage");
        AutomationProperties.SetName(this, "Haven-native Agents management");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeAgentsScene");
        AutomationProperties.SetName(Scene, "Agents management");
        Content = Scene;
    }

    public HavenSceneControl Scene { get; }
    internal AgentsHavenScene HavenScene => _scene;

    private Task StopOriginalAsync()
    {
        // Publish this wrapper's close before requesting the SAME scene stop.
        // Pending producer work remains pending; view retirement is not task cancel.
        _scene.RequestRetirement();
        return _scene.OriginalClose ?? throw new InvalidOperationException("The actual Agent scene close was not published.");
    }

    private Task CleanupOriginalAsync()
    {
        // The retained child close is terminal before renderer detachment. Its
        // complete original/stop/cleanup failures remain part of wrapper close.
        Scene.Root = null;
        return Task.CompletedTask;
    }

    public void RequestRetirement() => _originalWork.RequestRetirement();

    public void DemandExternalOriginalRetirementJoin()
    {
        _originalWork.DemandExternalClose();
        _scene.DemandExternalOriginalRetirementJoin();
    }

    public Task CloseAndDrainAsync()
    {
        // Preflight the child as well as this wrapper before returning any
        // existing close: a child callback cannot join its own encompassing page.
        DemandExternalOriginalRetirementJoin();
        return _originalWork.CloseAndDrainAsync();
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    public void Dispose() => RequestRetirement();
}
