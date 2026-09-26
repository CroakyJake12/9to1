using System.Text;

namespace NineToOne.Cui.AI;

public enum FloatingAiBarMode
{
    Collapsed,
    Ready,
    Streaming,
    Review,
    Error,
}

public sealed class FloatingAiBarState(AppAiCoordinator coordinator) : IDisposable
{
    private CancellationTokenSource? _requestCancellation;
    private long _requestVersion;

    public FloatingAiBarMode Mode { get; private set; } = FloatingAiBarMode.Collapsed;
    public AppAiAccessMode AccessMode { get; private set; } = AppAiAccessMode.ReadOnly;
    public bool IsReadOnly => AccessMode == AppAiAccessMode.ReadOnly;
    public bool IsWriteMode => AccessMode == AppAiAccessMode.Write;
    public string AccessModeLabel => IsReadOnly ? "Read-only" : "Write mode";
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; private set; } = string.Empty;
    public string? ContextLabel { get; private set; }
    public string? Error { get; private set; }
    public AppAiRequestState RequestState { get; private set; } = AppAiRequestState.Idle;
    public IReadOnlyList<AppAiModelOption> Models { get; private set; } = [];
    public AppAiModelSelection? ModelSelection { get; private set; }
    public string SelectedModelLabel => ModelSelection?.ModelId ?? "Use current model";

    public event EventHandler? Changed;

    public void SetAccessMode(AppAiAccessMode accessMode)
    {
        if (!Enum.IsDefined(accessMode)) throw new ArgumentOutOfRangeException(nameof(accessMode));
        if (AccessMode == accessMode) return;

        // A mode change cancels any in-flight model turn before it can dispatch a later action.
        Cancel();
        AccessMode = accessMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetReadOnly() => SetAccessMode(AppAiAccessMode.ReadOnly);

    public void SetWriteMode() => SetAccessMode(AppAiAccessMode.Write);

    public async ValueTask RefreshModelsAsync(CancellationToken cancellationToken = default)
    {
        Models = await coordinator.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        ModelSelection = await coordinator.GetModelSelectionAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask<bool> SelectModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var selected = await coordinator.SelectModelAsync(modelId, cancellationToken).ConfigureAwait(false);
        if (selected)
        {
            ModelSelection = await coordinator.GetModelSelectionAsync(cancellationToken).ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return selected;
    }

    public void Expand()
    {
        if (Mode == FloatingAiBarMode.Collapsed)
            SetMode(FloatingAiBarMode.Ready);
    }

    public void Collapse()
    {
        Cancel();
        SetMode(FloatingAiBarMode.Collapsed);
    }

    public async ValueTask RefreshContextAsync(CancellationToken cancellationToken = default)
    {
        RequestState = AppAiRequestState.CapturingContext;
        Changed?.Invoke(this, EventArgs.Empty);
        var snapshot = await coordinator.CaptureContextAsync(cancellationToken).ConfigureAwait(false);
        ContextLabel = string.IsNullOrWhiteSpace(snapshot.Summary) ? snapshot.AppId : snapshot.Summary;
        RequestState = AppAiRequestState.Idle;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SubmitAsync(CancellationToken cancellationToken = default)
    {
        var submittedPrompt = Prompt.Trim();
        if (submittedPrompt.Length == 0)
        {
            Error = "Enter a request first.";
            SetMode(FloatingAiBarMode.Error);
            return;
        }

        Cancel();
        var version = Interlocked.Increment(ref _requestVersion);
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _requestCancellation.Token;
        var response = new StringBuilder();
        Response = string.Empty;
        Error = null;
        RequestState = AppAiRequestState.Generating;
        SetMode(FloatingAiBarMode.Streaming);

        try
        {
            await foreach (var chunk in coordinator.StreamAsync(
                submittedPrompt,
                Guid.NewGuid().ToString("N"),
                AccessMode,
                token).ConfigureAwait(false))
            {
                if (version != Volatile.Read(ref _requestVersion))
                    return;

                response.Append(chunk.Text);
                Response = response.ToString();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (version == Volatile.Read(ref _requestVersion))
            {
                RequestState = AppAiRequestState.Completed;
                SetMode(FloatingAiBarMode.Ready);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _requestVersion))
            {
                RequestState = AppAiRequestState.Cancelled;
                SetMode(FloatingAiBarMode.Ready);
            }
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _requestVersion))
                return;

            Error = exception.Message;
            RequestState = AppAiRequestState.Failed;
            SetMode(FloatingAiBarMode.Error);
        }
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _requestVersion);
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        if (cancellation is null)
            return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    public void Dispose() => Cancel();

    private void SetMode(FloatingAiBarMode mode)
    {
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
