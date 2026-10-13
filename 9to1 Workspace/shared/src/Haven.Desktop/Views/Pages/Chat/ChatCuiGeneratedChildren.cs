using Avalonia.Controls;
using CakeOS.Cui.Runtime;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Narrow bridge for the actual child engine owned by ChatGenUiSurfaceMount.
/// This owns presentation attachment only, never the child's operation lifetime.</summary>
internal sealed class ChatCuiGeneratedChildren : ContentControl, ICuiTextBindingTarget
{
    private readonly ChatCuiScene _owner;
    private readonly IHavenAvaloniaNativeControlResolver _resolver;
    private HavenSceneControl? _host;
    private Haven.UI.Components.Container? _root;
    private string _text = "";
    public ChatCuiGeneratedChildren(ChatCuiScene owner, IHavenAvaloniaNativeControlResolver resolver)
    { _owner = owner; _resolver = resolver; }
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            _owner.RegisterGeneratedPresentation(this);
            Refresh();
        }
    }
    internal Guid MessageId => Guid.TryParse(_text, out var id) ? id : Guid.Empty;
    internal void Refresh()
    {
        var children = _owner.GetOriginalGeneratedChildren(MessageId);
        if (_root is not null)
            foreach (var previous in _root.Children.ToArray()) _root.Remove(previous);
        if (children.Count == 0) { Content = null; return; }
        if (_host is null)
        {
            _root = new Haven.UI.Components.Container { Layout = HavenLayout.Vertical };
            _root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            _host = new HavenSceneControl(new HavenAvaloniaImageResolver(), _resolver) { Root = _root };
            _host.InputSubmitted += _owner.Host.ForwardOriginalInput;
        }
        foreach (var child in children) _root!.Add(child);
        Content = _host;
    }
    internal void DetachAfterOriginalChildrenJoined()
    {
        if (_host is not null) { _host.InputSubmitted -= _owner.Host.ForwardOriginalInput; _host.Root = null; }
        if (_root is not null) foreach (var child in _root.Children.ToArray()) _root.Remove(child);
        Content = null;
    }
}
