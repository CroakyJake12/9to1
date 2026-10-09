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
    private long _searchVersion;
    private bool _disposed;
    private readonly object _auditSync = new();
    private readonly List<AppAiActionResult> _pendingAudits = [];
    private bool _hasUnconfirmedActionAudit;
    public bool HasUnconfirmedActionAudit { get { lock (_auditSync) return _hasUnconfirmedActionAudit; } }
    private bool _unsupportedAudit;
    public bool HasPendingActionAudit { get { lock (_auditSync) return _pendingAudits.Count > 0; } }
    private void RetainAudit(AppAiActionResult result)
    {
        lock (_auditSync)
        {
            if (result.CompletionAuditPending) _hasUnconfirmedActionAudit = true;
            if (result.AuditRecovery is null)
            { if (result.CompletionAuditPending) _unsupportedAudit = true; return; }
            if (!_pendingAudits.Any(item => ReferenceEquals(item.AuditRecovery, result.AuditRecovery))) _pendingAudits.Add(result);
        }
    }

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
        AppAiRequestState.Completed => HasUnconfirmedActionAudit ? "Owner outcome retained; Home audit pending" : "Ready",
        AppAiRequestState.Cancelled => "Stopped",
        AppAiRequestState.Failed => "Could not complete the request",
        _ => string.Empty
    };
    public InvocationCompose Compose { get; } = new();
    public string Prompt
    {
        get => Compose.Text;
        set { Compose.SetText(value); Changed?.Invoke(this, EventArgs.Empty); }
    }
    public IReadOnlyList<InvocationSection> InvocationSections { get; private set; } = [];
    public async ValueTask SearchInvocationsAsync(IInvocationCatalogue catalogue, int caret, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var version = Interlocked.Increment(ref _searchVersion);
        Compose.UpdateCaret(caret);
        var query = Compose.Query;
        var text = Compose.Text;
        var resources = await catalogue.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || version != Volatile.Read(ref _searchVersion) || text != Compose.Text || query != Compose.Query) return;
        InvocationSections = Compose.Sections(resources);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void InsertInvocation(InvocationResource resource, int caret)
    {
        Compose.Insert(resource, caret);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void RemoveInvocation(string tokenId)
    {
        Compose.Remove(tokenId);
        Changed?.Invoke(this, EventArgs.Empty);
    }
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
        IReadOnlyList<InvocationToken> invocations;
        try { invocations = Compose.Resolve(); }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
            SetMode(FloatingAiBarMode.Error);
            return;
        }
        var submittedPrompt = Prompt;
        if (string.IsNullOrWhiteSpace(submittedPrompt))
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
                token,
                invocations).ConfigureAwait(false))
            {
                if (chunk.ActionObservation is { } observed) RetainAudit(observed);
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

    /// <summary>Executes or retries the host's retained exact typed request through the same coordinator.
    /// This does not ask a model to regenerate a plan and never treats a copied token as an owner grant.</summary>
    public async ValueTask<AppAiActionResult> ExecuteActionAsync(AppAiActionRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(request);
        if (AccessMode != AppAiAccessMode.Write)
            return AppAiActionResult.Rejected("Read-only mode does not allow app actions.", "read-only-mode");
        var captured = request with { Arguments = request.Arguments.Clone(), ApprovalToken = null, AccessMode = AccessMode };
        Cancel();
        var version = Interlocked.Increment(ref _requestVersion);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _requestCancellation = cancellation;
        RequestState = AppAiRequestState.ExecutingAction; Error = null; SetMode(FloatingAiBarMode.Review);
        try
        {
            var result = await coordinator.ExecuteAsync(captured, cancellation.Token).ConfigureAwait(false);
            RetainAudit(result); // retain even if this view was replaced while the owner finished
            if (version != Volatile.Read(ref _requestVersion)) return result;
            Response = result.Summary;
            if (result.ErrorCode == "approval-pending")
            { RequestState = AppAiRequestState.WaitingForApproval; SetMode(FloatingAiBarMode.Review); }
            else if (result.Succeeded)
            { RequestState = AppAiRequestState.Completed; SetMode(FloatingAiBarMode.Ready); }
            else
            { Error = result.Summary; RequestState = AppAiRequestState.Failed; SetMode(FloatingAiBarMode.Error); }
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _requestVersion))
            { RequestState = AppAiRequestState.Cancelled; SetMode(FloatingAiBarMode.Ready); }
            throw;
        }
        catch
        {
            if (version == Volatile.Read(ref _requestVersion))
            { Error = "The app action could not be completed. Inspect its current result before retrying."; RequestState = AppAiRequestState.Failed; SetMode(FloatingAiBarMode.Error); }
            throw;
        }
        finally
        {
            Interlocked.CompareExchange(ref _requestCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    /// <summary>Retries only an already-issued completion audit. Never executes, verifies, or begins an action.</summary>
    public async ValueTask<AppAiCompletionObservation> FinishActionAuditAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var version = Volatile.Read(ref _requestVersion);
        AppAiActionResult observed;
        lock (_auditSync) observed = _pendingAudits.FirstOrDefault()
            ?? throw new InvalidOperationException("This bar has no retained completion audit recovery.");
        var recovery = observed.AuditRecovery!;
        var completion = await recovery.FinishAsync(cancellationToken).ConfigureAwait(false);
        if (completion.AuditRecorded) lock (_auditSync)
        {
            _pendingAudits.Remove(observed);
            // Unsupported legacy audit transports have no owned handle and remain explicitly unconfirmed.
            _hasUnconfirmedActionAudit = _pendingAudits.Count > 0 || _unsupportedAudit;
        }
        // Always acknowledge actual durable recovery, but never overwrite a newer or disposed view.
        if (_disposed || version != Volatile.Read(ref _requestVersion)) return completion;
        // Audit recovery updates audit availability only. The retained outcome must not replace
        // any response/error belonging to another request, even one started before Finish.
        Changed?.Invoke(this, EventArgs.Empty);
        return completion;
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

    public void Dispose() { _disposed = true; Interlocked.Increment(ref _searchVersion); Cancel(); }

    private void SetMode(FloatingAiBarMode mode)
    {
        Mode = mode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
