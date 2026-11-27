using Avalonia.Threading;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    private readonly Dictionary<Guid, ToolActivity> _presentedActivities = [];
    private readonly ChatConversationHeaderBindings _headerBindings = new();
    private readonly object _headerCloseGate = new();
    private Task? _headerInitialization, _headerClose, _headerRelease;
    private bool _headerReleased;
    internal ChatConversationHeaderCuiView HeaderView { get; private set; } = null!;
    internal global::Haven.UI.Components.NativeHost HeaderHost { get; private set; } = null!;
    internal Container ConversationViewport { get; private set; } = null!;
    internal Container ConversationBody { get; private set; } = null!;
    internal Task HeaderInitialization => _headerInitialization!;
    internal bool IsFollowingLatest => ChatTranscriptScrollPolicy.ShouldFollow(
        ConversationViewport.MaxScrollY, ConversationViewport.ScrollY);

    private void InitializeConversationPresentation()
    {
        HeaderHost = Root.DescendantsAndSelf().OfType<global::Haven.UI.Components.NativeHost>().Single(item => item.Name == "ConversationHeaderHost");
        ConversationViewport = Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "ConversationViewport");
        ConversationBody = Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "ConversationBody");
        HeaderView = new ChatConversationHeaderCuiView(_headerBindings);
        _headerBindings.ReturnToLatest = ScrollToEnd;
        _headerInitialization = HeaderView.InitializeAsync();
        _nativeResolver.Register(HeaderHost, HeaderView);
        // This is the same overflow owner that receives native wheel/touch input. Reading its
        // invalidation only updates the affordance; it never scrolls or rebuilds the transcript.
        ConversationViewport.Invalidated += OnTranscriptInvalidated;
    }

    internal void BindHeaderActions(Func<object, bool> isCurrentContext, Func<object, ValueTask> toggleTemporary)
    {
        _headerBindings.IsCurrentContext = isCurrentContext;
        _headerBindings.ToggleTemporary = toggleTemporary;
        _headerBindings.Refresh();
    }

    internal void SetConversationHeader(object context, string? title, string? model) =>
        _headerBindings.UpdateConversation(context, title, model);

    internal void SetConversationSummaries(IReadOnlyList<ConversationContextEntry> entries) =>
        _headerBindings.SetSummaries(entries);

    private void RefreshHeaderActivities() => _headerBindings.SetActivities(_presentedActivities.Values);
    private void OnTranscriptInvalidated(object? sender, EventArgs args) => RefreshTranscriptPosition();
    internal void RefreshTranscriptPosition() => _headerBindings.SetReadingEarlier(!IsFollowingLatest);

    internal Task StartHeaderClose()
    {
        lock (_headerCloseGate)
        {
            if (_headerClose is not null) return _headerClose;
            ConversationViewport.Invalidated -= OnTranscriptInvalidated;
            return _headerClose = HeaderView.CloseAsync();
        }
    }

    internal Task JoinHeaderCloseAndReleaseAsync()
    {
        lock (_headerCloseGate)
        {
            if (_headerRelease is not null) return _headerRelease;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _headerRelease = ReleaseHeaderAfterAcknowledgementAsync(start.Task);
            start.SetResult();
            return _headerRelease;
        }
    }

    private async Task ReleaseHeaderAfterAcknowledgementAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await StartHeaderClose().ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_headerReleased) return;
            _nativeResolver.Unregister(HeaderHost, HeaderView);
            HeaderView.Dispose();
            _headerReleased = true;
        }).GetTask().ConfigureAwait(false);
    }
}
