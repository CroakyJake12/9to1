using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

/// <summary>One acquired editor and its actual native controls. No Den or provider authority is acquired here.</summary>
internal sealed class StudioOriginalEditorLifetime
{
    private const int MaximumControls = 64;
    private readonly StudioOriginalCallbackLifetime _callbacks = new();
    private readonly List<AgentAvatarPreviewControl> _controls = [];
    private readonly Func<bool> _isCurrent;
    private Task? _close;
    private bool _retiring;
    internal AgentAvatarEditor Editor { get; }
    internal CuiSceneHost? Host { get; set; }
    internal Task? OriginalCloseTask => _close;
    internal bool IsExecutingOriginal => _callbacks.IsExecutingOriginal;
    internal bool IsRetiring => _retiring;

    internal StudioOriginalEditorLifetime(AgentAvatarEditor editor, Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(isCurrent);
        Editor = editor; _isCurrent = isCurrent;
    }

    internal Task OpenAsync(string namespaceId, string agentId, CancellationToken caller) =>
        _callbacks.Run(async token =>
        {
            RequireCurrent(token);
            await Editor.OpenAsync(namespaceId, agentId, token);
            RequireCurrent(token);
        }, caller);

    internal ICuiActionDispatcher Actions => new OriginalActions(this);

    internal void RequireCurrent(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        if (_retiring || !_isCurrent())
            throw new InvalidOperationException("The original Studio editor is no longer current.");
    }

    internal AgentAvatarPreviewControl AcquirePreviewControl(AgentAvatarPreview preview)
    {
        RequireCurrent(CancellationToken.None);
        if (_controls.Count >= MaximumControls)
            throw new InvalidOperationException("Acquired preview-control custody is full.");
        // Capture before the constructor can publish native notifications.
        return new AgentAvatarPreviewControl(preview, control =>
        {
            _controls.Add(control);
            RequireCurrent(CancellationToken.None);
        });
    }

    internal Task CloseAndDrainAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_close is not null) return _close;
        _retiring = true;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _close = CloseOriginalAsync(start.Task);
        start.SetResult();
        return _close;
    }

    private async Task CloseOriginalAsync(Task start)
    {
        await start;
        List<Exception> errors = [];
        List<Task> actualControlCloses = [];
        Task? actualCallbacks = null;
        try { actualCallbacks = _callbacks.CloseAndDrainAsync(); }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        foreach (var control in _controls)
        {
            try
            {
                var original = control.CloseAndDrainAsync();
                if (original is null) throw new InvalidOperationException("The original preview control supplied no close task.");
                actualControlCloses.Add(original);
            }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        }
        // Clear requests decoder retirement before waiting for an original held preview/action.
        try { Editor.Preview?.Clear(); }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualCallbacks is not null)
            try { await actualCallbacks; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        foreach (var original in actualControlCloses)
            try { await original; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        Task? actualPreviewClose = null;
        try
        {
            if (Editor.Preview is not null)
                actualPreviewClose = Editor.Preview.DisposeAsync().AsTask();
        }
        catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualPreviewClose is not null)
            try { await actualPreviewClose; }
            catch (Exception error) { StudioOriginalCallbackLifetime.Add(errors, error); }
        if (actualCallbacks is null || !_callbacks.OriginalsCapturedAndSettled ||
            actualControlCloses.Count != _controls.Count ||
            Editor.Preview is not null && actualPreviewClose is null)
            StudioOriginalCallbackLifetime.Add(errors, new InvalidOperationException("Actual acquired editor/preview settlement is missing."));
        // A dirty draft or original save failure remains owned. It cannot authorize host destruction.
        StudioOriginalCallbackLifetime.Throw(errors);
        Host?.Dispose();
        Host = null;
        _controls.Clear();
    }

    private sealed class OriginalActions(StudioOriginalEditorLifetime owner) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            // Capture caller intent before publishing or invoking the actual action.
            var capturedCommand = command; var capturedParameter = parameter;
            return new(owner._callbacks.Run(async token =>
            {
                owner.RequireCurrent(token);
                await owner.Editor.DispatchAsync(capturedCommand, capturedParameter, token);
                owner.RequireCurrent(token);
            }, cancellationToken));
        }
    }
}
