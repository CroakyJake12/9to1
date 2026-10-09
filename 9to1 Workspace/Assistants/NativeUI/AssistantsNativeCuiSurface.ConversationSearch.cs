using Avalonia.Controls;
using CakeOS.Cui;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task FindOriginalConversationAsync(CancellationToken token)
    {
        if (!Bindings.CanFindOriginalConversation || _binding is not { } binding) return;
        var queryRevision = Bindings.OriginalConversationSearchQueryRevision;
        if (string.IsNullOrWhiteSpace(Bindings.OriginalConversationSearchQuery)) return;
        var generation = PresentationGeneration;
        PublishSynchronous(() => Bindings.SetConversationSearchBusy(true));
        try
        {
            await SourceAsync(() => _controller.RefreshWorkAsync(token));
            if (!IsPresentationCurrent(binding, generation) || Bindings.OriginalConversationSearchQueryRevision != queryRevision) return;
            PublishSynchronous(() =>
            {
                if (!IsPresentationCurrent(binding, generation) || Bindings.OriginalConversationSearchQueryRevision != queryRevision) return;
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                if (!IsPresentationCurrent(binding, generation) || current.Conversation is not { } data) return;
                // Bind the complete actual saved history returned by this source,
                // including messages outside the current scroll viewport.
                _conversationTarget = _conversation.Bind(data, includeCompacted: true);
                var freshGeneration = Interlocked.Increment(ref _presentationGeneration);
                Bindings.SetMessages(_conversation.Messages);
                Bindings.FindOriginalConversationText(binding, freshGeneration);
            });
        }
        finally { if (!IsRetiring) PublishSynchronous(() => Bindings.SetConversationSearchBusy(false)); }
    }

    private void ShowOriginalConversationSearchResult(object? item)
    {
        var row = Bindings.CurrentConversationSearchRow(item);
        if (row is null || !IsPresentationCurrent(row.Binding, row.Generation)) return;
        PublishSynchronous(() =>
        {
            if (!ReferenceEquals(row, Bindings.CurrentConversationSearchRow(item)) ||
                !IsPresentationCurrent(row.Binding, row.Generation) || _richConversationScene is null) return;
            var target = _generatedMessageViews.SingleOrDefault(view => view.IsEffectivelyVisible &&
                view.DataContext is ICuiBindingContext context && context.TryGetValue("message", out var actual) &&
                ReferenceEquals(actual, row.Message) && IsOriginalGeneratedMessageCurrent(view, row.Message,
                    row.Binding, row.Generation, view.TextGeneration));
            if (target is null)
            {
                Bindings.SetConversationStatus("The matching message is still being displayed. Try showing it again.");
                return;
            }
            target.BringIntoView();
            if (!ReferenceEquals(row, Bindings.CurrentConversationSearchRow(item)) ||
                !IsPresentationCurrent(row.Binding, row.Generation)) return;
            Bindings.SetConversationStatus("Showing " + row.Label.ToLowerInvariant() + ".");
        });
    }
}
