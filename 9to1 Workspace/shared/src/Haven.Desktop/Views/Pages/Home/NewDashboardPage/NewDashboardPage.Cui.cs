using Avalonia.Controls;
using Avalonia.Layout;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private readonly StackPanel PageTabsPanel = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private readonly StackPanel DynamicRowsPanel = new() { Spacing = 24 };
    private readonly WrapPanel PinnedPanel = new() { ItemWidth = 220, ItemHeight = 148 };
    private readonly TextBlock WelcomeText = new();
    private readonly TextBlock DateText = new();
    private readonly TextBlock PinnedEmptyText = new();
    private readonly Button ConfigurePageButton = new();
    private readonly Button AddPageButton = new();
    private readonly Button EditPinnedButton = new();
    private readonly Button EditWithHavenButton = new();
    private readonly Button DiscoverModelsButton = new();
    private readonly Button AddWidgetButton = new();
    private readonly Button RefreshWidgetsButton = new();
    private CuiContainerControl? _widgetControls;
    private CuiControlLoader? _dashboardLoader;
    private CuiContainerControl? _dashboardRoot;

    private void LoadDashboardCui()
    {
        using var stream = typeof(NewDashboardPage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Dashboard.cui")
            ?? throw new InvalidDataException("The Home dashboard CUI document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Dashboard.cui");
        if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home dashboard CUI document is invalid.");
        var loader = _dashboardLoader = new CuiControlLoader();
        // The authored briefing subtree is consumed through the canonical CUI binding path, not
        // through native control substitution: it reads projected snapshot state and raises typed
        // commands via NewDashboardPageBriefingBindings.
        _briefingBindings = new NewDashboardPageBriefingBindings(() => _briefingState, DispatchBriefingCommandAsync);
        loader.SetBindingContext(_briefingBindings);
        loader.SetActionDispatcher(_briefingBindings);
        loader.BindAuthoredControl("dashboard-root", _ => _dashboardRoot = new CuiContainerControl("Grid"));
        loader.BindAuthoredControl("dashboard-widget-controls", _ => _widgetControls = new CuiContainerControl("Vertical Stack"));
        // Reuse the same live widget canvas, page state and typed callbacks. CUI owns their
        // layout; these retained host controls preserve the existing editing behavior.
        loader.BindAuthoredControl("dashboard-page-tabs", _ => PageTabsPanel);
        loader.BindAuthoredControl("dashboard-live-widgets", _ => DynamicRowsPanel);
        loader.BindAuthoredControl("dashboard-pinned-apps", _ => PinnedPanel);
        loader.BindAuthoredControl("dashboard-welcome", _ => WelcomeText);
        loader.BindAuthoredControl("dashboard-date", _ => DateText);
        loader.BindAuthoredControl("dashboard-pinned-empty", _ => PinnedEmptyText);
        loader.BindAuthoredControl("dashboard-edit-page", _ => ConfigurePageButton);
        loader.BindAuthoredControl("dashboard-add-page", _ => AddPageButton);
        loader.BindAuthoredControl("dashboard-edit-pinned", _ => EditPinnedButton);
        loader.BindAuthoredControl("dashboard-edit-with-ai", _ => EditWithHavenButton);
        loader.BindAuthoredControl("dashboard-discover", _ => DiscoverModelsButton);
        loader.BindAuthoredControl("dashboard-add-widget", _ => AddWidgetButton);
        loader.BindAuthoredControl("dashboard-refresh-widgets", _ => RefreshWidgetsButton);
        loader.BindAuthoredControl("dashboard-undo-layout", _ => _widgetUndoButton = new Button());
        loader.BindAuthoredControl("dashboard-redo-layout", _ => _widgetRedoButton = new Button());
        loader.BindAuthoredControl("dashboard-arrange-widgets", _ => _widgetArrangeButton = new Button());
        loader.BindAuthoredControl("dashboard-widget-status", _ => _widgetStatus = new TextBlock());
        var loaded = loader.TryLoad(document);
        if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The Home dashboard CUI surface could not be rendered: " +
                string.Join("; ", loaded.Diagnostics.Select(item => item.Message)));
        loader.WireBindings(loaded.Root);
        Content = loaded.Root;
    }

    private static void SetDashboardValue(Control control, string property, object? value) =>
        CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Home dashboard state", order: 1);
}
