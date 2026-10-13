using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Chat.Rendering;

/// <summary>Genuine shared Markdown content behind the canonical Spaces Object type.
/// The caller retains clipboard, execution and permission authority. Without a
/// code-action owner those actions are disabled, never simulated.</summary>
public static class ChatSpaceCuiRuntime
{
    public static CuiControlRegistry CreateRegistry(
        Action<CuiMarkdownView, CuiMarkdownCodeActionRequest>? codeAction = null)
    {
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("ChatMessageContent", _ => new ChatMarkdownContent(codeAction));
        return registry;
    }

    /// <summary>Routes Enter only from this surface's real composer through its
    /// authored send button. Shift+Enter and input elsewhere keep their native
    /// text behavior. The same loader/owner availability and async action lifetime
    /// apply to pointer and keyboard submission.</summary>
    public static IDisposable AttachComposer(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var controls = SpacesShellCuiRuntime.AuthoredControls(root).ToArray();
        var input = controls.OfType<TextBox>().Single(control => control.Name == "composer-input");
        var send = controls.OfType<Button>().Single(control => control.Name == "send");
        return AttachComposer(root, input, send);
    }

    /// <summary>Uses the exact retained controls supplied by the canonical loader
    /// when the authored surface has not yet joined a native visual tree.</summary>
    public static IDisposable AttachComposer(Control root, TextBox input, Button send)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(send);
        return new ComposerKeys(root, input, send);
    }

    private sealed class ComposerKeys : IDisposable
    {
        private readonly Control _root;
        private readonly TextBox _input;
        private readonly Button _send;
        private bool _disposed;
        public ComposerKeys(Control root, TextBox input, Button send)
        { _root = root; _input = input; _send = send; input.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel); }
        private void OnKeyDown(object? sender, KeyEventArgs args)
        {
            if (_disposed || args.Handled || args.Key != Key.Enter || args.KeyModifiers != KeyModifiers.None
                || !_input.IsFocused || !_input.IsEffectivelyEnabled || !_input.IsEffectivelyVisible
                || !_root.IsVisualAncestorOf(_input) || !_root.IsVisualAncestorOf(_send)) return;
            // Enter is submission, including when submission is unavailable;
            // Shift+Enter is the deliberate newline path.
            args.Handled = true;
            if (_send.IsEffectivelyEnabled && _send.IsEffectivelyVisible)
                _send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        public void Dispose()
        { if (_disposed) return; _disposed = true; _input.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown); }
    }

    private sealed class ChatMarkdownContent : CuiMarkdownView
    {
        protected override double ParagraphFontSize => 18;
        protected override Avalonia.Media.FontWeight ParagraphFontWeight => Avalonia.Media.FontWeight.SemiBold;
        private readonly bool _hasCodeOwner;
        public ChatMarkdownContent(Action<CuiMarkdownView, CuiMarkdownCodeActionRequest>? codeAction)
        {
            _hasCodeOwner = codeAction is not null;
            if (codeAction is not null)
                CodeActionRequested += request =>
                {
                    if (IsCurrentOriginalCodeAction(request) && TopLevel.GetTopLevel(this) is not null && IsEffectivelyVisible && IsEffectivelyEnabled)
                        codeAction(this, request);
                };
        }
        protected override Border CreateSurface() => Own(new Border());
        protected override TextBox CreateCodeInput() => Own(new TextBox());
        protected override CheckBox CreateTaskCheckbox() => Own(new CheckBox());
        protected override Button CreateCodeActionButton()
        {
            var button = Own(new Button());
            button.IsEnabled = _hasCodeOwner;
            return button;
        }
        private static T Own<T>(T control) where T : Control
        {
            // Shared specialized Markdown content retains its semantics; its
            // control affordances use the exact CUI cascade/templates/states.
            CuiPropertyPresentation.Attach(control);
            return control;
        }
    }
}
