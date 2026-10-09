using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private IAssistantGeneratedUiHost? _generatedUiHost;
    private readonly List<GeneratedMessageView> _generatedMessageViews = [];
    private readonly List<IAssistantGeneratedUiMount> _generatedUiMounts = [];
    public IAssistantGeneratedUiHost? OriginalGeneratedUiHost => _generatedUiHost;
    private readonly List<Task> _originalGeneratedUiRenderTasks = [];
    public IReadOnlyList<Task> OriginalGeneratedUiRenderTasks
    { get { lock (_gate) return Array.AsReadOnly(_originalGeneratedUiRenderTasks.ToArray()); } }

    /// <summary>Bind before initialization; the Desktop supplier owns the SAME canonical renderer and router.</summary>
    public void BindOriginalGeneratedUiHost(IAssistantGeneratedUiHost actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        using var physical = EnterPhysical();
        DemandCurrent();
        if (!actual.IsOriginalController(_controller))
            throw new InvalidOperationException("Generated UI must borrow this SAME scoped canonical controller.");
        lock (_gate)
        {
            if (_initialization is not null) throw new InvalidOperationException("Bind generated UI before native initialization.");
            if (_generatedUiHost is not null && !ReferenceEquals(_generatedUiHost, actual))
                throw new InvalidOperationException("The original generated UI host cannot be replaced.");
            _generatedUiHost = actual;
        }
        DemandCurrent();
    }

    private Control CreateOriginalMessageView()
    {
        using var physical = EnterPhysical();
        return new GeneratedMessageView(this, actual => _generatedMessageViews.Add(actual));
    }

    private bool IsOriginalGeneratedMessageCurrent(GeneratedMessageView view, AssistantMessagePresentation message,
        AssistantConversationBinding binding, long generation, long textGeneration)
    {
        using var physical = EnterPhysical();
        return IsPresentationCurrent(binding, generation) && view.TextGeneration == textGeneration &&
            view.Text == message.Content && _conversation.IsCurrentMessage(_conversationTarget, message) &&
            Bindings.TryGetItemValue(message, "Content", out var content) && content as string == message.Content &&
            view.DataContext is ICuiBindingContext context && context.TryGetValue("message", out var actual) &&
            ReferenceEquals(actual, message) && _richConversationScene is not null &&
            view.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, _richConversationScene));
    }

    private bool IsOriginalGeneratedMarkdownText(CuiMarkdownView markdown, AssistantMessagePresentation message) =>
        _generatedMessageViews.Any(view => ReferenceEquals(view.Markdown, markdown) && view.Text == message.Content &&
            markdown.Text == view.OriginalDisplayContent);

    private void RenderOriginalGeneratedMessage(GeneratedMessageView view)
    {
        using var physical = EnterPhysical();
        if (IsRetiring || _binding is not { } binding || view.DataContext is not ICuiBindingContext context ||
            !context.TryGetValue("message", out var item) || item is not AssistantMessagePresentation message) return;
        var generation = PresentationGeneration; var textGeneration = view.TextGeneration;
        if (!IsOriginalGeneratedMessageCurrent(view, message, binding, generation, textGeneration)) return;
        // Full saved history may show compacted messages. Their current canonical
        // projection is readable Markdown; it cannot issue a live generated origin.
        var canonical = _controller.Snapshot.Conversation;
        if (canonical?.Conversation.Id == binding.Conversation.Id && canonical.Messages.Any(actual =>
            actual.Id == message.Id && actual.ConversationId == binding.Conversation.Id &&
            actual.Content == message.Content && actual.IsCompacted))
        { view.PublishOriginalMarkdownHistory(message.Content); return; }
        if (!view.TryBeginOriginalRender(message, textGeneration)) return;
        var host = _generatedUiHost;
        if (host is null) return; // Existing rich text stays usable on an unconfigured standalone host.
        _ = RunAsync(async () =>
        {
            bool Current() => IsOriginalGeneratedMessageCurrent(view, message, binding, generation, textGeneration);
            if (!Current()) return;
            var mount = await SourceAsync(() =>
            {
                var actual = host.CreateOriginalMessageAsync(binding, message, Current,
                    mounted => { using var physical = EnterPhysical(); _generatedUiMounts.Add(mounted); view.RetainOriginalMount(mounted); });
                lock (_gate) _originalGeneratedUiRenderTasks.Add(actual);
                return actual; // SAME raw host task before SourceAsync's post-acquisition guard.
            });
            if (mount is null || !Current()) return;
            PublishSynchronous(() =>
            {
                if (!Current()) return;
                if (mount.OriginalSourceContent != message.Content || !_generatedUiMounts.Any(actual => ReferenceEquals(actual, mount)))
                    throw new InvalidOperationException("The returned generated surface differs from the retained original message.");
                view.PublishOriginalMount(mount);
            });
        }, actual => _originalGeneratedUiRenderTasks.Add(actual)); // SAME admitted driver before opening its start gate.
    }

    private void RequestOriginalGeneratedUiRetirement() => _generatedUiHost?.RequestRetirement();
    private void DemandOriginalGeneratedUiJoin() => _generatedUiHost?.DemandExternalOriginalRetirementJoin();
    private Task? AcquireOriginalGeneratedUiClose() => _generatedUiHost?.CloseAndDrainAsync();

    private sealed class GeneratedMessageView : UserControl, ICuiTextBindingTarget
    {
        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<GeneratedMessageView, string>(nameof(Text), "");
        private readonly AssistantsNativeCuiSurface _owner;
        private readonly StackPanel _root;
        private readonly List<IAssistantGeneratedUiMount> _originalMounts = [];
        private AssistantMessagePresentation? _renderedMessage;
        private long _renderedGeneration = -1;
        internal CuiMarkdownView Markdown { get; }
        internal long TextGeneration { get; private set; }
        internal string OriginalDisplayContent { get; private set; } = "";
        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

        internal GeneratedMessageView(AssistantsNativeCuiSurface owner, Action<GeneratedMessageView> capture)
        {
            _owner = owner; capture(this); // Partial native view before child/property callbacks.
            Markdown = new CuiMarkdownView();
            Markdown.CodeActionRequested += request => owner.OnOriginalMarkdownAction(Markdown, request);
            _root = new StackPanel { Spacing = 8, Children = { Markdown } };
            Content = _root;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_root is null || (change.Property != TextProperty && change.Property != DataContextProperty)) return;
            if (change.Property == TextProperty)
            {
                TextGeneration = checked(TextGeneration + 1);
                OriginalDisplayContent = Text; Markdown.Text = Text;
                _root.Children.Clear(); _root.Children.Add(Markdown);
            }
            _owner.RenderOriginalGeneratedMessage(this);
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
        { base.OnAttachedToVisualTree(args); _owner.RenderOriginalGeneratedMessage(this); }
        internal bool TryBeginOriginalRender(AssistantMessagePresentation message, long generation)
        {
            if (ReferenceEquals(message, _renderedMessage) && generation == _renderedGeneration) return false;
            _renderedMessage = message; _renderedGeneration = generation; return true;
        }
        internal void PublishOriginalMarkdownHistory(string sameContent)
        {
            // A same-key native view can retain an earlier live mount. Detach its
            // presentation while keeping that actual child in the owning close cohort.
            OriginalDisplayContent = sameContent; Markdown.Text = sameContent;
            _root.Children.Clear(); _root.Children.Add(Markdown);
        }
        internal void RetainOriginalMount(IAssistantGeneratedUiMount actual) => _originalMounts.Add(actual);
        internal void PublishOriginalMount(IAssistantGeneratedUiMount mount)
        {
            OriginalDisplayContent = mount.DisplayContent; Markdown.Text = mount.DisplayContent;
            _root.Children.Clear(); _root.Children.Add(Markdown); _root.Children.Add(mount.View);
            if (mount.Status.Length != 0) _root.Children.Add(new TextBlock { Text = mount.Status, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        }
    }
}
