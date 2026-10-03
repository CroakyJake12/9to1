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
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var current = new HashSet<string>(StringComparer.Ordinal);
        if (_root is not null) Visit(ControlAutomationPeer.CreatePeerForElement(_root));
        foreach (var id in _peers.Keys.Except(current).ToArray())
        {
            _ids.Remove(_peers[id]);
            _peers.Remove(id);
        }
        return JsonSerializer.Serialize(new { generation = _generation, elements, unsupported = unsupported.Order().ToArray() }, JsonOptions);

        void Visit(AutomationPeer peer)
        {
            var kind = peer.GetAutomationControlType();
            if (peer is not ControlAutomationPeer controlPeer)
            {
                unsupported.Add($"{kind}:{peer.GetClassName()}");
                return;
            }
            if (!controlPeer.Owner.IsEffectivelyVisible) return;
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
                elements.Add(new(id, role, peer.GetName(), peer.GetAutomationId(), peer.IsEnabled(),
                    peer.IsKeyboardFocusable(), value?.Value, value?.IsReadOnly ?? true, peer.GetHelpText()));
                // Names and values already belong to the actual parent provider;
                // projecting template children would duplicate its semantics.
                return;
            }
            if (kind is not (AutomationControlType.None or AutomationControlType.Custom or AutomationControlType.Pane or AutomationControlType.Group)
                || peer is IInvokeProvider or IValueProvider)
            {
                unsupported.Add(kind.ToString());
                return;
            }
            foreach (var child in peer.GetChildren()) Visit(child);
        }
    }

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

    private sealed record Element(string Id, string Role, string Name, string? AutomationId, bool Enabled,
        bool Focusable, string? Value, bool ReadOnly, string Help);
}
