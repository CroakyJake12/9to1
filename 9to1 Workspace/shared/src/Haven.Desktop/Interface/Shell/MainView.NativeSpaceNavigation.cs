using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.NativePresentation;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private SpaceNavigationPreferences? _nativeNavigationPreferences;
    private SpaceNavigationPreferenceSnapshot? _nativeNavigationSnapshot;

    private void InitializeNativeSpaceNavigation(IServiceProvider services)
    {
        _nativeNavigationPreferences = services.GetService<SpaceNavigationPreferences>();
        if (_nativeChatSidebar is not { } sidebar) return;
        sidebar.NavigationLayoutChanged += OnNativeSpaceNavigationLayoutChanged;
        NativeSidebarHost.Width = sidebar.PreferredWidth;
        if (_nativeNavigationPreferences is null)
        {
            sidebar.Navigation.SetStatus("Space navigation preferences need the current Home profile owner on this host.");
            return;
        }
        StartOriginalShellEvent(async original =>
        {
            try { await RefreshNativeSpaceNavigationAsync(original); }
            catch (UnauthorizedAccessException)
            {
                original.DemandPublication();
                sidebar.Navigation.SetStatus("Home has not yet verified ownership of these local Space preferences. Navigation remains available; saved layout changes are unavailable.");
            }
        });
    }
    private async Task RefreshNativeSpaceNavigationAsync(DesktopOriginalWorkLifetime.Original original)
    {
        var owner = _nativeNavigationPreferences ?? throw new InvalidOperationException("The current Home navigation preference owner is unavailable.");
        var preferences = await original.AwaitAsync(owner.ReadAsync(original.Token));
        original.DemandPublication();
        _nativeNavigationSnapshot = preferences;
        _nativeChatSidebar?.SetNavigationPreferences(preferences);
    }
    private Task SetNativeSpaceSidebarCollapsedAsync(bool collapsed) => _originalShellWork.RunAsync(async original =>
    {
        var owner = _nativeNavigationPreferences ?? throw new InvalidOperationException("The current Home navigation preference owner is unavailable.");
        var sidebar = _nativeChatSidebar ?? throw new InvalidOperationException("The current Space sidebar is unavailable.");
        var scope = sidebar.CurrentSpaceId;
        var snapshot = _nativeNavigationSnapshot ?? await original.AwaitAsync(owner.ReadAsync(original.Token));
        var saved = await original.AwaitAsync(owner.SetCollapsedAsync(scope, collapsed, snapshot.Revision, original.Token));
        original.DemandPublication();
        _nativeNavigationSnapshot = saved;
        sidebar.SetNavigationPreferences(saved);
    });
    private void OnNativeSpaceNavigationLayoutChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || _nativeChatSidebar is not { } sidebar) return;
        NativeSidebarHost.Width = sidebar.PreferredWidth;
        if (_nativeNavigationSnapshot is { } snapshot)
        {
            _isSidebarOpen = !snapshot.IsCollapsed(sidebar.CurrentSpaceId);
            RaisePropertyChanged(nameof(IsSidebarOpen)); RaisePropertyChanged(nameof(IsSidebarClosed));
            RaisePropertyChanged(nameof(HasFullSidebar)); RaisePropertyChanged(nameof(HasCompactSidebar));
        }
        ApplyShellVisualState();
    }
    private Task ConfigureNativeSpaceNavigationAsync() => _originalShellWork.RunAsync(async original =>
    {
        await RefreshNativeSpaceNavigationAsync(original);
        original.DemandPublication();
        ShowNativeSpaceNavigationChoices();
    });
    private void ShowNativeSpaceNavigationChoices()
    {
        var sidebar = _nativeChatSidebar ?? throw new InvalidOperationException("The current Space sidebar is unavailable.");
        var snapshot = _nativeNavigationSnapshot ?? throw new InvalidOperationException("Read the current Home preferences before editing navigation.");
        var choices = new List<(string Label, Func<Task> Action)>
        {
            ("Use Space rail" + (snapshot.Layout == SpaceSwitcherLayout.Rail ? " (selected)" : ""), () => SaveNativeNavigationEditAsync((owner, current, token) => owner.SetLayoutAsync(SpaceSwitcherLayout.Rail, current.Revision, token))),
            ("Use Space dropdown" + (snapshot.Layout == SpaceSwitcherLayout.Dropdown ? " (selected)" : ""), () => SaveNativeNavigationEditAsync((owner, current, token) => owner.SetLayoutAsync(SpaceSwitcherLayout.Dropdown, current.Revision, token)))
        };
        if (sidebar.CurrentSpaceId is { } currentId)
        {
            var pinned = snapshot.PinnedSpaceIds.Contains(currentId);
            choices.Add((pinned ? "Unpin current Space" : "Pin current Space", () => SaveNativeNavigationEditAsync((owner, current, token) => owner.SetPinnedAsync(currentId, !pinned, current.Revision, token))));
        }
        foreach (var id in snapshot.PinnedSpaceIds)
            choices.Add(($"Manage pinned Space {snapshot.PinnedSpaceIds.ToList().IndexOf(id) + 1}", () => ShowNativePinChoicesAsync(id)));
        sidebar.Navigation.ShowChoices("Space navigation · this Home profile", choices);
    }
    private Task ShowNativePinChoicesAsync(Guid id) => _originalShellWork.RunAsync(async original =>
    {
        var registry = App.Services?.GetService<SpaceRegistry>() ?? throw new InvalidOperationException("The canonical Space registry is unavailable.");
        var space = await original.AwaitAsync(registry.ReadExistingAsync(id, original.Token));
        original.DemandPublication();
        var snapshot = _nativeNavigationSnapshot ?? throw new InvalidOperationException("The current navigation observation is unavailable.");
        var index = snapshot.PinnedSpaceIds.ToList().IndexOf(id);
        if (index < 0) throw new InvalidOperationException("This Space is no longer pinned.");
        var choices = new List<(string Label, Func<Task> Action)>();
        if (index > 0) choices.Add(("Move up", () => SaveNativeNavigationEditAsync((owner, current, token) => owner.MovePinAsync(id, index - 1, current.Revision, token))));
        if (index < snapshot.PinnedSpaceIds.Count - 1) choices.Add(("Move down", () => SaveNativeNavigationEditAsync((owner, current, token) => owner.MovePinAsync(id, index + 1, current.Revision, token))));
        choices.Add(("Unpin", () => SaveNativeNavigationEditAsync((owner, current, token) => owner.SetPinnedAsync(id, false, current.Revision, token))));
        _nativeChatSidebar!.Navigation.ShowChoices(space?.Name ?? "Unavailable Space", choices);
    });
    private Task SaveNativeNavigationEditAsync(Func<SpaceNavigationPreferences, SpaceNavigationPreferenceSnapshot, CancellationToken, Task<SpaceNavigationPreferenceSnapshot>> edit) =>
        _originalShellWork.RunAsync(async original =>
        {
            var owner = _nativeNavigationPreferences ?? throw new InvalidOperationException("The current Home navigation preference owner is unavailable.");
            var snapshot = _nativeNavigationSnapshot ?? throw new InvalidOperationException("The current navigation observation is unavailable.");
            var saved = await original.AwaitAsync(edit(owner, snapshot, original.Token));
            original.DemandPublication();
            _nativeNavigationSnapshot = saved; _nativeChatSidebar?.SetNavigationPreferences(saved);
        });
}
