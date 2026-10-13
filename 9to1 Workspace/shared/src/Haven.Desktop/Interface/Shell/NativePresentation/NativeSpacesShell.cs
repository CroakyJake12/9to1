using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed record NativeSpacesShellActions(Func<Task>? Account = null, Func<Task>? Search = null,
    Func<Task>? Notifications = null);

/// <summary>Application-owned canonical Spaces frame. The original sidebar and
/// actual page remain separately owned and separately joined by MainView.</summary>
internal sealed class NativeSpacesShell : ContentControl, ICuiBindingContext, ICuiActionDispatcher,
    INotifyPropertyChanged, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly NativeChatSidebar _navigation;
    private readonly NativeSpacesShellActions _actions;
    private readonly CuiOriginalSurfaceSlot _navigationSlot = new(), _workspaceSlot = new();
    private readonly CuiControlLoader _loader;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Control _root;
    private NewChatPage? _activePage;
    private ChatCuiScene? _chat;
    private bool _retiring, _compact;

    internal NativeSpacesShell(NativeChatSidebar navigation, CuiThemePreferenceContext? theme = null,
        NativeSpacesShellActions? actions = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _actions = actions ?? new();
        _originalWork = new(StopAsync, CleanupAsync);
        var registry = new CuiControlRegistry();
        NineToOne.Web.BrowserCanonicalIcons.Register(registry);
        registry.RegisterObjectRenderer("OriginalSpacesNavigation", _ => _navigationSlot);
        registry.RegisterObjectRenderer("OriginalSpacesWorkspace", _ => _workspaceSlot);
        _loader = new CuiControlLoader(registry);
        var appearance = CuiThemeScopeApplier.DetectAppearance();
        _loader.SetSurface("Spaces", "app.spaces"); _loader.SetAppearance(appearance);
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
        var loaded = _loader.TryLoad(SpacesShellCuiDocument.LoadForOriginalNativeSlots());
        _root = loaded.Root ?? throw new InvalidDataException("The canonical Spaces frame did not produce a root.");
        if (loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, loaded.Diagnostics));
        _loader.WireBindings(_root);
        _root.Resources.MergedDictionaries.Add(CuiSceneVisualResources.Create("Spaces", appearance));
        _root.SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
        Content = _root;
        _root.SizeChanged += OnSizeChanged;
        _navigation.Navigation.PropertyChanged += OnNavigationChanged;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    internal NewChatPage? ActiveWorkspace => _activePage;
    internal Control CanonicalRoot => _root;

    /// <summary>Called only after the route owner admits the destination and
    /// detaches the SAME original page/sidebar from the previous platform host.</summary>
    internal void SetWorkspace(NewChatPage? page)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retiring && page is not null) throw new ObjectDisposedException(nameof(NativeSpacesShell));
        if (ReferenceEquals(page, _activePage)) return;
        // Validate target parentage before changing the current observation.
        if (page is not null && page.Parent is not null) throw new InvalidOperationException("Detach the admitted page from its former host first.");
        if (page is not null && _navigation.Parent is not null && !ReferenceEquals(_navigationSlot.OriginalSurface, _navigation))
            throw new InvalidOperationException("Detach the original navigation from its former host first.");
        if (_chat is not null) { _chat.PropertyChanged -= OnChatChanged; _chat.SetShellHosted(false); }
        _workspaceSlot.SetOriginalSurface(page);
        _activePage = page; _chat = page?.CuiPresentation;
        if (_chat is not null) { _chat.SetShellHosted(true); _chat.PropertyChanged += OnChatChanged; }
        if (page is not null) _navigationSlot.SetOriginalSurface(_navigation);
        Changed();
    }
    internal void ReleaseNavigation() => _navigationSlot.SetOriginalSurface(null);
    private void OnChatChanged(object? sender, PropertyChangedEventArgs args) => Changed(args.PropertyName);
    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs args) => Changed();
    private void Changed(string? path = null) => PropertyChanged?.Invoke(this, new(path));
    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_retiring || args.NewSize.Width <= 0) return;
        _compact = args.NewSize.Width < 860;
        _navigation.Navigation.SetViewportWidth(args.NewSize.Width);
        Changed();
    }
    public bool TryGetValue(string path, out object? value)
    {
        var width = _navigation.Navigation.PreferredWidth;
        value = path switch
        {
            "shell.columns" => $"{(_compact ? 70 : width)} 1fr",
            "shell.rows" => _compact ? "172 1fr" : "80 1fr",
            "shell.headerMinHeight" => _compact ? 172d : 80d,
            "shell.navigationWidth" => width,
            "shell.navigationSpan" => _compact && width > 70 ? 2 : 1,
            "shell.headerColumns" => _compact ? "1fr" : "1fr Auto Auto",
            "shell.modelColumn" => _compact ? 0 : 1,
            "shell.modelRow" => _compact ? 1 : 0,
            "shell.headerRows" => _compact ? "Auto Auto Auto" : "1fr",
            "shell.utilityColumn" => _compact ? 0 : 2,
            "shell.utilityRow" => _compact ? 2 : 0,
            "shell.utilitySpan" => 1,
            "shell.hasUsage" or "shell.canOpenUsage" => false,
            "shell.usageLabel" or "shell.usageDescription" => "",
            "shell.canSearch" => !_retiring && _actions.Search is not null,
            "shell.canNotify" => !_retiring && _actions.Notifications is not null,
            "shell.canAccount" => !_retiring && _actions.Account is not null,
            _ => null
        };
        if (value is not null) return true;
        if (_chat is not null) return _chat.TryGetValue(path, out value);
        value = path.EndsWith("Label", StringComparison.Ordinal) ? "" : false;
        return path.StartsWith("chat.", StringComparison.Ordinal);
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
        var chat = _chat;
        return new(_originalWork.RunAsync(async original =>
        {
            original.DemandPublication();
            if (command.StartsWith("chat.", StringComparison.Ordinal))
            {
                if (chat is null || !ReferenceEquals(chat, _chat)) throw new InvalidOperationException("The original chat header owner is no longer current.");
                await original.AwaitAsync(chat.DispatchAsync(command, parameter, cancellationToken).AsTask());
                return;
            }
            var action = command switch
            {
                "shell.account" => _actions.Account, "shell.search" => _actions.Search,
                "shell.notifications" => _actions.Notifications, _ => null
            };
            await original.AwaitAsync((action ?? throw new InvalidOperationException("This shell service is unavailable."))());
        }));
    }
    public void RequestRetirement() { _retiring = true; _originalWork.RequestRetirement(); }
    public void DemandExternalOriginalRetirementJoin() => _originalWork.DemandExternalClose();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); RequestRetirement(); return _originalWork.CloseAndDrainAsync(); }
    private Task StopAsync()
    {
        _retiring = true;
        _root.SizeChanged -= OnSizeChanged;
        _navigation.Navigation.PropertyChanged -= OnNavigationChanged;
        if (_chat is not null) _chat.PropertyChanged -= OnChatChanged;
        return Task.CompletedTask;
    }
    private async Task CleanupAsync()
    {
        var failures = new List<Exception>();
        try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
        Task? original = null;
        try { original = _loader.WhenActionsIdleAsync(); await original; }
        catch (Exception error) { failures.Add(original?.Exception ?? error); }
        if (failures.Count > 0) throw new AggregateException("Spaces frame retirement failed.", failures);
        // Child surfaces deliberately remain attached. Their owning MainView
        // joins each actual page/sidebar before the final platform detach.
    }
}
