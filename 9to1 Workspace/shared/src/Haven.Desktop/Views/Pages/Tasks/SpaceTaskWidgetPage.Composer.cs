using Haven.Desktop.Views.Pages.Chat;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Tasks;

public sealed partial class SpaceTaskWidgetPage
{
    private UserPreferencesService? _composerPreferences;
    internal void BindOriginalComposerPreferences(UserPreferencesService actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        _work.DemandAdmission();
        lock (_gate)
        {
            if (_active) throw new InvalidOperationException("Bind the same-profile preference owner before task activation.");
            if (_composerPreferences is not null && !ReferenceEquals(_composerPreferences, actual))
                throw new InvalidOperationException("The task composer already borrows a different preference owner.");
            _composerPreferences = actual;
        }
        _composerView.RefreshSurface();
    }
    private TaskFollowUpMode? PreferredTaskSubmitMode => _composerPreferences?.GetSpaceSubmitMode(_composerPreferenceSpaceId,
        _composerPreferenceSpaceId == SpaceRegistry.ResearchSpaceId) switch
        { SpaceSubmitMode.Steer => TaskFollowUpMode.Steer, SpaceSubmitMode.Queue => TaskFollowUpMode.Queue, _ => null };
    private ValueTask SetTaskSubmitMode(TaskFollowUpMode mode)
    {
        var preferences = _composerPreferences;
        if (!Enum.IsDefined(mode) || preferences is null)
            return ValueTask.FromException(new InvalidOperationException("The actual Space preference owner is unavailable."));
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            InvokeOriginalSource(() =>
            {
                preferences.SetSpaceSubmitMode(_composerPreferenceSpaceId,
                    mode == TaskFollowUpMode.Steer ? SpaceSubmitMode.Steer : SpaceSubmitMode.Queue);
                return true;
            });
            NotifyOriginal(original, generation);
        });
        return ValueTask.CompletedTask;
    }
    /// <summary>Live typed port to the SAME widget's actual command originals. It owns no
    /// task, run, scheduler, continuation factory or cloned draft/availability state.</summary>
    private sealed class TaskComposerSource(SpaceTaskWidgetPage page) : IChatComposerSource
    {
        public object? ActionIdentity
        {
            get
            {
                lock (page._gate) return (page._spaceId, page._conversationId,
                    page._observation?.Snapshot?.TaskId, page._observation?.Snapshot?.ExecutionId);
            }
        }
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
        public bool HasSendModePreference => page._composerPreferences is not null && page._active && !page._work.IsRetiring && page._observation?.Snapshot is not null;
        public TaskFollowUpMode? PreferredSendMode => page.PreferredTaskSubmitMode;
        public bool CanSubmit => PreferredSendMode is { } mode && (mode == TaskFollowUpMode.Steer ? CanSteer : CanQueue);
        public ValueTask SetPreferredSendMode(TaskFollowUpMode mode) => page.SetTaskSubmitMode(mode);
        public ValueTask Submit(bool alternate)
        {
            if (PreferredSendMode is not { } mode)
                return ValueTask.FromException(new InvalidOperationException("Choose Steer or Queue before using this Space's send shortcut."));
            var steer = alternate ? mode != TaskFollowUpMode.Steer : mode == TaskFollowUpMode.Steer;
            return steer ? Steer() : Send();
        }
        public bool HasFormatting => false;
        public bool CanSend => false;
        public bool CanStop => page.IsActionAvailable("task.stop") == true;
        public string StopAccessibleName => "Stop task";
        public string StopTooltip => "Ask the owning service to stop this same task and retain its recorded work";
        public bool CanSteer => page.IsActionAvailable("task.steer") == true;
        public bool CanQueue => page.IsActionAvailable("task.queue") == true;
        public bool CanPause => !IsPassPaused && page.IsActionAvailable("task.pause") == true;
        public bool IsPassPaused => page.AcknowledgedOriginalRunControl is { Kind: TaskRunOriginalRunControlKind.Pause,
            Disposition: TaskRunOriginalRunControlDisposition.Suspended } ack &&
            page._observation?.Snapshot is { State: TaskExecutionLifecycle.Suspended } current &&
            current.TaskId == ack.CanonicalTaskContext.TaskId && current.ExecutionId == ack.CanonicalTaskContext.ExecutionId;
        public bool CanEndPass => page.IsActionAvailable("task.end-pass") == true;
        public ValueTask EndPass() => ForwardAsync("task.end-pass");
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
