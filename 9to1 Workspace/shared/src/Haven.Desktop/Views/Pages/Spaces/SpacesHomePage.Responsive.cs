using System.ComponentModel;
using Avalonia.Controls;
using Haven.Application;

namespace Haven.Desktop.Views.Pages.Spaces;

public sealed partial class SpacesHomePage
{
    private double _sidebarViewportWidth = double.PositiveInfinity;
    // Ownership of a responsive collapse, never a second sidebar preference.
    private bool _sidebarAutoCollapsed;
    private bool _sidebarResizePending;

    private string ResponsiveSidebarColumns => WorkspaceOwnsContextualNavigation || _controller.State.SidebarCollapsed
        ? "60 0 1fr" : _sidebarViewportWidth < 1000 ? "60 210 1fr" : "60 232 1fr";

    private static int SidebarWidthBand(double width) => width < 750 ? 0 : width < 1000 ? 1 : 2;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateResponsiveSidebarWidth(e.NewSize.Width);
    }

    private void ReconcilePendingSidebarResize()
    {
        lock (_gate)
            if (!_sidebarResizePending) return;
        // Aborting reversible close preparation refreshes bindings without a new
        // SizeChanged event. Apply the actual viewport once admission reopens.
        UpdateResponsiveSidebarWidth(Bounds.Width);
    }

    private void UpdateResponsiveSidebarWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        long generation;
        lock (_gate)
        {
            if (!CanAdmitHomeMutation())
            {
                _sidebarResizePending = !_work.IsRetiring;
                return;
            }
            _sidebarResizePending = false;
            // Pixels inside a band only affect native arrangement. They do not need
            // a new page original, binding refresh, or controller publication.
            if (SidebarWidthBand(width) == SidebarWidthBand(_sidebarViewportWidth))
            {
                _sidebarViewportWidth = width;
                return;
            }
            generation = _generation;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            original.RunAcceptedPublicationCallback(() => ApplyResponsiveSidebarWidth(width));
        });
    }

    // Also applied before the original CUI show, for a page measured while inactive.
    private void ApplyResponsiveSidebarWidth(double width)
    {
        bool toggle;
        bool columnsChanged;
        lock (_gate)
        {
            if (!double.IsFinite(width) || width <= 0) return;
            if (!CanAdmitHomeMutation())
            {
                _sidebarResizePending = !_work.IsRetiring;
                return;
            }
            _sidebarResizePending = false;
            var previousColumns = ResponsiveSidebarColumns;
            var wasCompact = _sidebarViewportWidth < 750;
            var compact = width < 750;
            _sidebarViewportWidth = width;
            toggle = false;
            if (compact != wasCompact)
            {
                if (compact && !_controller.State.SidebarCollapsed)
                {
                    _sidebarAutoCollapsed = true;
                    toggle = true;
                }
                else if (!compact && _sidebarAutoCollapsed)
                {
                    _sidebarAutoCollapsed = false;
                    toggle = _controller.State.SidebarCollapsed;
                }
            }
            columnsChanged = previousColumns != ResponsiveSidebarColumns;
        }

        // Controller callbacks can enter the session owner, so publish outside the page gate.
        // Its ordinary refresh also updates all visibility and column bindings.
        if (toggle) InvokeOriginalSource(_controller.ToggleSidebar);
        else if (columnsChanged)
            InvokeOriginalSource(() => PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs("spaces.layoutColumns")));
    }
}
