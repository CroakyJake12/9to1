using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Chat.Rendering;

/// <summary>Shared shell input and real viewport observation. The host retains
/// the one loader/action queue and its original async retirement lifetime.</summary>
public static class SpacesShellCuiRuntime
{
    public static IDisposable Attach(Control root, Action<double> viewportChanged)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(viewportChanged);
        return new Attachment(root, viewportChanged);
    }

    internal static IEnumerable<Control> AuthoredControls(Control root)
    {
        var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Control>(); pending.Push(root);
        while (pending.TryPop(out var control))
        {
            if (!seen.Add(control)) continue;
            yield return control;
            IEnumerable<Control> children = control switch
            {
                CuiContainerControl container => container.AuthoredChildren,
                Panel panel => panel.Children,
                Decorator { Child: { } child } => [child],
                ContentControl { Content: Control child } => [child],
                ItemsControl items => items.Items.OfType<Control>(),
                _ => []
            };
            foreach (var child in children) pending.Push(child);
        }
    }

    private sealed class Attachment : IDisposable
    {
        private readonly Control _root;
        private readonly Action<double> _viewport;
        private readonly IDisposable _composer, _navigation;
        private bool _disposed;
        public Attachment(Control root, Action<double> viewport)
        {
            _root = root; _viewport = viewport;
            var authored = AuthoredControls(root).ToArray();
            var input = authored.OfType<TextBox>().Single(c => c.Name == "composer-input");
            var send = authored.OfType<Button>().Single(c => c.Name == "send");
            _composer = ChatSpaceCuiRuntime.AttachComposer(root, input, send);
            try { _navigation = new SpacesNavigationCuiInput(root); }
            catch { _composer.Dispose(); throw; }
            root.SizeChanged += SizeChanged;
            root.AttachedToVisualTree += Attached;
            PublishWidth();
        }
        private void SizeChanged(object? sender, SizeChangedEventArgs args) => PublishWidth();
        private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => PublishWidth();
        private void PublishWidth()
        {
            if (!_disposed && double.IsFinite(_root.Bounds.Width) && _root.Bounds.Width > 0)
                _viewport(_root.Bounds.Width);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _root.SizeChanged -= SizeChanged;
            _root.AttachedToVisualTree -= Attached;
            try { _composer.Dispose(); } finally { _navigation.Dispose(); }
        }
    }
}
