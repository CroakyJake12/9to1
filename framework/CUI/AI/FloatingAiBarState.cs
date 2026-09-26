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
    public string RequestStateLabel => RequestState switch
    {
        AppAiRequestState.CapturingContext => "Reading authorised app context…",
        AppAiRequestState.Generating => "Thinking…",
        AppAiRequestState.WaitingForApproval => "Waiting for approval…",
        AppAiRequestState.ExecutingAction => "Applying an approved app action…",
        AppAiRequestState.Completed => "Ready",
        AppAiRequestState.Cancelled => "Stopped",
        AppAiRequestState.Failed => "Could not complete the request",
        _ => string.Empty
    };
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; private set; } = string.Empty;
    public string? ContextLabel { get; private set; }
    public string? Error { get; private set; }
    public AppAiRequestState RequestState { get; private set; } = AppAiRequestState.Idle;
    public IReadOnlyList<AppAiModelOption> Models { get; private set; } = [];
    public AppAiModelSelection? ModelSelection { get; private set; }
    public bool ModelPickerAvailable => Models.Count > 0;
    public string? SelectedModelId
    {
        get => ModelSelection?.ModelId;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, ModelSelection?.ModelId, StringComparison.Ordinal))
                _ = SelectModelAsync(value);
        }
    }
    public string SelectedModelLabel => ModelSelection is null
        ? "Use current model"
        : Models.FirstOrDefault(model => string.Equals(model.Id, ModelSelection.ModelId, StringComparison.Ordinal))?.DisplayName
            ?? ModelSelection.ModelId;
    public string ModelPickerLabel => ModelPickerAvailable
        ? $"Model: {SelectedModelLabel} · change"
        : "Model selection unavailable";

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
        try
        {
            Models = await coordinator.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            ModelSelection = await coordinator.GetModelSelectionAsync(cancellationToken).ConfigureAwait(false);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Error = "AI model options could not be loaded.";
            SetMode(FloatingAiBarMode.Error);
        }
    }

    public async ValueTask<bool> SelectModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        try
        {
            var selected = await coordinator.SelectModelAsync(modelId, cancellationToken).ConfigureAwait(false);
            if (selected)
            {
                ModelSelection = await coordinator.GetModelSelectionAsync(cancellationToken).ConfigureAwait(false);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            return selected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Error = "That model could not be selected.";
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    public async ValueTask SelectNextModelAsync(CancellationToken cancellationToken = default)
    {
        var available = Models.Where(model => model.IsAvailable).ToArray();
        if (available.Length == 0) return;
        var currentIndex = Array.FindIndex(available, model =>
            string.Equals(model.Id, ModelSelection?.ModelId, StringComparison.Ordinal));
        var next = available[(currentIndex + 1) % available.Length];
        await SelectModelAsync(next.Id, cancellationToken).ConfigureAwait(false);
    }

    public void Expand()
    {
        if (Mode == FloatingAiBarMode.Collapsed)
            SetMode(FloatingAiBarMode.Ready);
        _ = RefreshModelsAsync();
        _ = RefreshContextAsync();
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
        try
        {
            var snapshot = await coordinator.CaptureContextAsync(cancellationToken).ConfigureAwait(false);
            ContextLabel = string.IsNullOrWhiteSpace(snapshot.Summary) ? snapshot.AppId : snapshot.Summary;
            Error = null;
            RequestState = AppAiRequestState.Idle;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestState = AppAiRequestState.Cancelled;
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        catch (Exception)
        {
            Error = "App context is unavailable. Try again when the app is ready.";
            RequestState = AppAiRequestState.Failed;
            SetMode(FloatingAiBarMode.Error);
        }
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
        catch (Exception)
        {
            if (version != Volatile.Read(ref _requestVersion))
                return;

            Error = "The AI request could not be completed. Check model availability and try again.";
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
        RequestState = AppAiRequestState.Cancelled;
        if (Mode == FloatingAiBarMode.Streaming)
            SetMode(FloatingAiBarMode.Ready);
    }

    public void Dispose() => Cancel();

    private void SetMode(FloatingAiBarMode mode)
    {
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
