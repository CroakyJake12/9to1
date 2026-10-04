using System.Text.Json;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
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
    }

    public string ReadSnapshot()
    {
        Dispatcher.UIThread.VerifyAccess();
        var elements = new List<Element>();
        var unsupportedPeers = new List<UnsupportedPeer>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var current = new HashSet<string>(StringComparer.Ordinal);
        if (_root is not null) Visit(ControlAutomationPeer.CreatePeerForElement(_root));
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
            var kind = peer.GetAutomationControlType();
            if (peer is not ControlAutomationPeer controlPeer)
            {
                unsupported.Add($"{kind}:{peer.GetClassName()}");
                return;
            }
            if (!controlPeer.Owner.IsEffectivelyVisible || _root is null
                || !(ReferenceEquals(controlPeer.Owner, _root) || controlPeer.Owner.GetVisualAncestors().Contains(_root))) return;
            var role = kind switch
            {
                AutomationControlType.Button when peer is IInvokeProvider => "button",
                AutomationControlType.Edit when peer is IValueProvider && controlPeer.Owner is not TextBox { PasswordChar: not '\0' } => "textbox",
                AutomationControlType.Text => "text",
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
                // Match the maintained CUI inspector for the actual control peer.
                // Virtual child peers share an owner and retain their own names.
                var authoredName = ReferenceEquals(peer, ControlAutomationPeer.FromElement(controlPeer.Owner))
                    ? Avalonia.Automation.AutomationProperties.GetName(controlPeer.Owner) : null;
                var name = string.IsNullOrWhiteSpace(authoredName) ? peer.GetName() : authoredName;
                elements.Add(new(id, role, name, peer.GetAutomationId(), peer.IsEnabled(),
                    peer.IsKeyboardFocusable(), value?.Value, value?.IsReadOnly ?? true, peer.GetHelpText(), peer.HasKeyboardFocus(), ReadBounds(controlPeer)));
                // Names and values already belong to the actual parent provider;
                // projecting template children would duplicate its semantics.
                return;
            }
            if (kind is not (AutomationControlType.None or AutomationControlType.Custom or AutomationControlType.Pane or AutomationControlType.Group)
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
            || !peer.Owner.IsEffectivelyVisible || !peer.IsEnabled()
            || !(ReferenceEquals(peer.Owner, _root) || peer.Owner.GetVisualAncestors().Contains(_root))) return false;
        switch (operation)
        {
            case "focus" when peer.IsKeyboardFocusable():
                peer.BringIntoView();
                peer.SetFocus();
                return true;
            case "invoke" when peer.GetAutomationControlType() == AutomationControlType.Button && peer is IInvokeProvider invoke:
                invoke.Invoke();
                return true;
            case "value" when peer.GetAutomationControlType() == AutomationControlType.Edit && peer is IValueProvider { IsReadOnly: false } edit:
                edit.SetValue(value);
                return true;
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
        bool Focusable, string? Value, bool ReadOnly, string Help, bool Focused, Bounds? Bounds);
}
