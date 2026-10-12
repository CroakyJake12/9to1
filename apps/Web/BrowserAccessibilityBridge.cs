using System.Text.Json;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NineToOne.Web;

/// <summary>Nonvisual browser semantics for the current rendered native controls.</summary>
internal sealed class BrowserAccessibilityBridge
{
    private Control? _root;
    private long _generation;
    private int _nextId;
    private readonly Dictionary<AutomationPeer, string> _ids = new();
    private readonly Dictionary<string, ControlAutomationPeer> _peers = new(StringComparer.Ordinal);
    private readonly Dictionary<ContextMenu, Control> _openContextOwners = new(ReferenceEqualityComparer.Instance);

    public void Bind(Control root)
    {
        Clear();
        _root = root;
    }

    public void Clear()
    {
        Dispatcher.UIThread.VerifyAccess();
        ++_generation;
        _root = null;
        _nextId = 0;
        _ids.Clear();
        _peers.Clear();
        _openContextOwners.Clear();
    }

    public string ReadSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        var elements = new List<Element>();
        var unsupportedPeers = new List<UnsupportedPeer>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var current = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<AutomationPeer>();
        _openContextOwners.Clear();
        if (_root is not null)
        {
            Visit(ControlAutomationPeer.CreatePeerForElement(_root));
            foreach (var menu in _openContextOwners.Keys.ToArray()) Visit(ControlAutomationPeer.CreatePeerForElement(menu));
        }
        foreach (var id in _peers.Keys.Except(current).ToArray())
        {
            _ids.Remove(_peers[id]);
            _peers.Remove(id);
        }
        return JsonSerializer.Serialize(new Snapshot(_generation, elements.ToArray(), unsupported.Order().ToArray(),
            ReadViewport(), unsupportedPeers.ToArray()),
            BrowserAccessibilityJsonContext.Default.Snapshot);

        void Visit(AutomationPeer peer)
        {
            if (!visited.Add(peer)) return;
            var kind = peer.GetAutomationControlType();
            if (peer is not ControlAutomationPeer controlPeer)
            {
                unsupported.Add($"{kind}:{peer.GetClassName()}");
                return;
            }
            if (!InScope(controlPeer.Owner)) return;
            if (controlPeer.Owner.IsEffectivelyEnabled && controlPeer.Owner.ContextMenu is { IsOpen: true } contextMenu)
                _openContextOwners.TryAdd(contextMenu, controlPeer.Owner);
            var role = kind switch
            {
                AutomationControlType.Button when peer is IInvokeProvider => "button",
                AutomationControlType.CheckBox when peer is IToggleProvider => "checkbox",
                AutomationControlType.Edit when peer is IValueProvider && controlPeer.Owner is not TextBox { PasswordChar: not '\0' } => "textbox",
                AutomationControlType.Text => "text",
                AutomationControlType.MenuItem when peer is IInvokeProvider => "menuitem",
                AutomationControlType.ComboBox when peer is IExpandCollapseProvider => "combobox",
                AutomationControlType.ListItem or AutomationControlType.ComboBoxItem when peer is ISelectionItemProvider => "option",
                AutomationControlType.Slider when peer is IRangeValueProvider => "slider",
                AutomationControlType.Image => "img",
                _ => null
            };
            if (role is not null)
            {
                if (!_ids.TryGetValue(peer, out var id))
                {
                    id = $"{_generation}:{++_nextId}";
                    _ids.Add(peer, id);
                    _peers.Add(id, controlPeer);
                }
                current.Add(id);
                var value = peer as IValueProvider;
                var range = peer as IRangeValueProvider;
                var expansion = peer as IExpandCollapseProvider;
                elements.Add(new(id, role, peer.GetName(), peer.GetAutomationId(), peer.IsEnabled(),
                    peer.IsKeyboardFocusable(), peer is IToggleProvider toggle ? toggle.ToggleState == ToggleState.On ? "true" : toggle.ToggleState == ToggleState.Off ? "false" : "mixed"
                        : range?.Value.ToString(CultureInfo.InvariantCulture) ?? value?.Value,
                    peer is IToggleProvider ? false : range?.IsReadOnly ?? value?.IsReadOnly ?? true, peer.GetHelpText(), peer.HasKeyboardFocus(), ReadBounds(controlPeer),
                    expansion is null || expansion.ExpandCollapseState == ExpandCollapseState.LeafNode ? null : expansion.ExpandCollapseState == ExpandCollapseState.Expanded,
                    range?.Minimum, range?.Maximum, peer is ISelectionItemProvider selected ? selected.IsSelected : null));
                if (expansion?.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    foreach (var child in peer.GetChildren()) Visit(child);
                    // A popup has its own visual root, but its real item containers
                    // remain logically owned by the current native menu/select.
                    if (controlPeer.Owner is ItemsControl items)
                        foreach (var child in items.GetRealizedContainers()) Visit(ControlAutomationPeer.CreatePeerForElement(child));
                }
                // Names and values already belong to the actual parent provider;
                // projecting template children would duplicate its semantics.
                return;
            }
            if (kind is not (AutomationControlType.None or AutomationControlType.Custom or AutomationControlType.Pane or AutomationControlType.Group or AutomationControlType.Menu or AutomationControlType.List)
                || peer is IInvokeProvider or IValueProvider)
            {
                unsupported.Add(kind.ToString());
                // Geometry diagnoses the actual rendered peer; it does not
                // advertise an unsupported value, focus or action provider.
                unsupportedPeers.Add(new(peer.GetAutomationId(), peer.GetName(), peer.GetClassName(), kind.ToString(), ReadBounds(controlPeer)));
                return;
            }
            foreach (var child in peer.GetChildren()) Visit(child);
        }
    }

    private bool InScope(Control owner) => _root is not null && owner.IsEffectivelyVisible &&
        (ReferenceEquals(owner, _root) || owner.GetVisualAncestors().Contains(_root)
         || TopLevel.GetTopLevel(owner) is not null && (owner.GetLogicalAncestors().Contains(_root)
            || owner is ContextMenu ownMenu && _openContextOwners.ContainsKey(ownMenu)
            || owner.GetLogicalAncestors().OfType<ContextMenu>().Any(_openContextOwners.ContainsKey)));

    private Viewport? ReadViewport()
    {
        var topLevel = _root is null ? null : TopLevel.GetTopLevel(_root);
        if (topLevel is null || !PositiveFinite(topLevel.ClientSize.Width) || !PositiveFinite(topLevel.ClientSize.Height)
            || !PositiveFinite(topLevel.RenderScaling)) return null;
        return new(topLevel.ClientSize.Width, topLevel.ClientSize.Height, topLevel.RenderScaling);
    }

    private static Bounds? ReadBounds(ControlAutomationPeer peer)
    {
        if (TopLevel.GetTopLevel(peer.Owner) is null) return null;
        var bounds = peer.GetBoundingRectangle();
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y)
            || !PositiveFinite(bounds.Width) || !PositiveFinite(bounds.Height)) return null;
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    public bool Perform(string id, string operation, string? value)
    {
        Dispatcher.UIThread.VerifyAccess();
        // Virtual peers can share one Control owner while individual semantic
        // children are detached or hidden. Re-read the actual peer tree at use,
        // rather than trusting membership from the preceding polling snapshot.
        ReadSnapshot();
        if (_root is null || !_peers.TryGetValue(id, out var peer)
            || !InScope(peer.Owner) || !peer.IsEnabled()) return false;
        switch (operation)
        {
            case "focus" when peer.IsKeyboardFocusable():
                peer.BringIntoView();
                peer.SetFocus();
                return true;
            case "invoke" when peer.GetAutomationControlType() is AutomationControlType.Button or AutomationControlType.MenuItem && peer is IInvokeProvider invoke:
                invoke.Invoke();
                return true;
            case "toggle" when peer.GetAutomationControlType() == AutomationControlType.CheckBox && peer is IToggleProvider toggle:
                toggle.Toggle();
                return true;
            case "value" when peer.GetAutomationControlType() == AutomationControlType.Edit && peer is IValueProvider { IsReadOnly: false } edit:
                edit.SetValue(value);
                return true;
            case "expand" when peer is IExpandCollapseProvider expand:
                expand.Expand(); return true;
            case "collapse" when peer is IExpandCollapseProvider collapse:
                collapse.Collapse(); return true;
            case "select" when peer is ISelectionItemProvider selection:
                selection.Select(); return true;
            case "value" when peer is IRangeValueProvider { IsReadOnly: false } range
                && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number) && number >= range.Minimum && number <= range.Maximum:
                range.SetValue(number); return true;
            default:
                return false;
        }
    }

    internal sealed record Snapshot(long Generation, Element[] Elements, string[] Unsupported,
        Viewport? Viewport, UnsupportedPeer[] UnsupportedPeers);

    internal sealed record Viewport(double Width, double Height, double RenderScaling);
    internal sealed record Bounds(double X, double Y, double Width, double Height);
    internal sealed record UnsupportedPeer(string? AutomationId, string Name, string ClassName, string ControlType, Bounds? Bounds);

    internal sealed record Element(string Id, string Role, string Name, string? AutomationId, bool Enabled,
        bool Focusable, string? Value, bool ReadOnly, string Help, bool Focused, Bounds? Bounds,
        bool? Expanded = null, double? Minimum = null, double? Maximum = null, bool? Selected = null);
}
