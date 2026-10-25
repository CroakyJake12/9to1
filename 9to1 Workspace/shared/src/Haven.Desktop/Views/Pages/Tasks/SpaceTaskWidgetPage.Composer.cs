using Haven.Desktop.Views.Pages.Chat;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Pages.Tasks;

public sealed partial class SpaceTaskWidgetPage
{
    /// <summary>Live typed port to the SAME widget's actual command originals. It owns no
    /// task, run, scheduler, continuation factory or cloned draft/availability state.</summary>
    private sealed class TaskComposerSource(SpaceTaskWidgetPage page) : IChatComposerSource
    {
        public string Draft
        {
            get { lock (page._gate) return page._instruction; }
            set => page.TrySetValue("Instruction", value ?? "");
        }
        public string Placeholder => "Steer this task or queue a follow-up";
        public bool IsSending => false; // This port never submits a new conversation turn.
        public bool IsSafetyLocked => false; // Each canonical command reauthorizes its own scope.
        public bool HasAdd => false;
        public bool HasSettings => false;
        public bool HasFormatting => false;
        public bool CanSend => false;
        public bool CanStop => page.IsActionAvailable("task.stop") == true;
        public string StopAccessibleName => "Stop task";
        public string StopTooltip => "Ask the owning service to stop this same task and retain its recorded work";
        public bool CanSteer => page.IsActionAvailable("task.steer") == true;
        public bool CanQueue => page.IsActionAvailable("task.queue") == true;
        public bool CanPause => page.IsActionAvailable("task.pause") == true;
        public bool CanResume => page.IsActionAvailable("task.resume-original") == true;
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips => [];
        // The shared Queue action borrows the source's existing submission method;
        // on this task-only port it is the genuine durable canonical Queue command.
        public ValueTask Send() => ForwardAsync("task.queue");
        public ValueTask Steer() => ForwardAsync("task.steer");
        public ValueTask Pause() => ForwardAsync("task.pause");
        public ValueTask Stop() => ForwardAsync("task.stop");
        public ValueTask Resume() => ForwardAsync("task.resume-original");
        private async ValueTask ForwardAsync(string command)
        {
            Exception? sourceFailure = null;
            try { await page.DispatchAsync(command, null).ConfigureAwait(false); }
            catch (Exception error) { sourceFailure = error; }
            try
            {
                // The same live source controls can retire before another task event.
                // Join projection independently without discarding an owner failure.
                await Dispatcher.UIThread.InvokeAsync(page._composerView.RefreshSurface).GetTask().ConfigureAwait(false);
            }
            catch (Exception projectionFailure) when (sourceFailure is not null)
            { throw new AggregateException("The original task command and its composer projection failed.", sourceFailure, projectionFailure); }
            if (sourceFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(sourceFailure).Throw();
        }
        public ValueTask Add() => Unavailable();
        public ValueTask OpenSettings() => Unavailable();
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => Unavailable();
        public ValueTask SetResponseMode(string mode) => Unavailable();
        public ValueTask InvokeChip(string id) => Unavailable();
        public ValueTask RemoveChip(string id) => Unavailable();
        public ValueTask FormatSelection(string command) => Unavailable();
        public ValueTask InsertEmoji(string emoji) => Unavailable();
        public ValueTask CancelEdit() => Unavailable();
        private static ValueTask Unavailable() => ValueTask.FromException(new NotSupportedException("This task input does not expose that composer operation."));
    }
}
