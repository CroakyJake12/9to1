using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Call;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class GlobalCallWidget
{
    private ICallCoordinator? _narrationOwner;
    private CallMonologueNarrationRoute? _originalNarrationRoute;
    private StackPanel? _originalNarrationPanel;
    private OriginalNarrationAttachment? _originalNarrationAttachment;
    private Task _originalNarrationPresentationDelay = Task.CompletedTask;
    private readonly List<Task> _originalNarrationPresentations = [];
    private readonly List<Task> _originalNarrationCleanup = [];
    private sealed class OriginalNarrationAttachment { public volatile bool Retired = false; }
    public Exception? LastOriginalNarrationPresentationFailure { get; private set; }
    public Task WhenOriginalNarrationIdleAsync() => Task.WhenAll(_originalNarrationPresentations.ToArray()
        .Concat(_originalNarrationCleanup).Concat(_originalNarrationPanel?.Children.OfType<CallMonologueNarrationHost>()
            .Select(host => host.WhenActionIdleAsync()) ?? []));

    public void BindOriginalNarrationRoute(InChatCallWidgetViewModel viewModel, ICallCoordinator originalOwner,
        CallMonologueNarrationRoute route)
    {
        ArgumentNullException.ThrowIfNull(viewModel); ArgumentNullException.ThrowIfNull(originalOwner);
        ArgumentNullException.ThrowIfNull(route);
        if (_disposed || _narrationOwner is not null || !ReferenceEquals(_applicationViewModel, viewModel) || !viewModel.IsBoundToCallOwner(originalOwner) ||
            !route.IsBoundTo(originalOwner))
            throw new InvalidOperationException("The native Voice route requires its same original Call owner.");
        _narrationOwner = originalOwner; _originalNarrationRoute = route;
        _originalNarrationPanel = new StackPanel();
        Content = null; _originalNarrationPanel.Children.Add(Scene); Content = _originalNarrationPanel;
        originalOwner.TranscriptChanged += OnOriginalNarrationTranscript;
    }

    private void OnOriginalNarrationTranscript(object? sender, CallTranscriptEventArgs e)
    {
        var attachment = Volatile.Read(ref _originalNarrationAttachment);
        bool Current() => !_disposed && attachment is not null && !attachment.Retired &&
            ReferenceEquals(Volatile.Read(ref _originalNarrationAttachment), attachment);
        var selected = Current() && e.IsFinal && e.Role == MessageRole.Assistant
            ? _originalNarrationRoute?.CaptureOriginalSelection(e.MessageId) : null;
        if (selected is null) return;
        var delay = _originalNarrationPresentationDelay;
        Dispatcher.UIThread.Post(() =>
        {
            _originalNarrationPresentations.RemoveAll(task => task.IsCompleted);
            if (_originalNarrationPresentations.Count < 128)
                _originalNarrationPresentations.Add(PresentOriginalNarrationAsync(selected, Current, delay));
        });
    }
    private async Task PresentOriginalNarrationAsync(CallOriginalNarrationSelection selected,
        Func<bool> originalAttachmentCurrent, Task delay)
    {
        try
        {
            await delay;
            _originalNarrationCleanup.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_originalNarrationCleanup.Count >= 128 || !originalAttachmentCurrent() || _originalNarrationPanel is null ||
                _originalNarrationRoute?.CreateOriginalHost(selected, originalAttachmentCurrent) is not { } host) return;
            foreach (var old in _originalNarrationPanel.Children.OfType<CallMonologueNarrationHost>().ToArray())
            { old.Dispose(); _originalNarrationCleanup.Add(old.WhenActionIdleAsync()); _originalNarrationPanel.Children.Remove(old); }
            _originalNarrationPanel.Children.Add(host);
        }
        catch (Exception error) { LastOriginalNarrationPresentationFailure = error; }
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_originalNarrationAttachment is { } previous) previous.Retired = true;
        Volatile.Write(ref _originalNarrationAttachment, new OriginalNarrationAttachment());
        base.OnAttachedToVisualTree(e);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_originalNarrationAttachment is { } original) original.Retired = true;
        base.OnDetachedFromVisualTree(e);
    }
    private void DisposeOriginalNarrationRoute()
    {
        if (_originalNarrationAttachment is { } original) original.Retired = true;
        if (_narrationOwner is { } owner) owner.TranscriptChanged -= OnOriginalNarrationTranscript;
        if (_originalNarrationPanel is { } panel)
            foreach (var host in panel.Children.OfType<CallMonologueNarrationHost>())
            { host.Dispose(); _originalNarrationCleanup.Add(host.WhenActionIdleAsync()); }
    }
}
