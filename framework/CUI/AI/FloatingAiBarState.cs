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
    public string Prompt { get; set; } = string.Empty;
    public string Response { get; private set; } = string.Empty;
    public string? ContextLabel { get; private set; }
    public string? Error { get; private set; }

    public event EventHandler? Changed;

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
        var snapshot = await coordinator.CaptureContextAsync(cancellationToken).ConfigureAwait(false);
        ContextLabel = string.IsNullOrWhiteSpace(snapshot.Summary) ? snapshot.AppId : snapshot.Summary;
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
        SetMode(FloatingAiBarMode.Streaming);

        try
        {
            await foreach (var chunk in coordinator.StreamAsync(
                submittedPrompt,
                Guid.NewGuid().ToString("N"),
                token).ConfigureAwait(false))
            {
                if (version != Volatile.Read(ref _requestVersion))
                    return;

                response.Append(chunk.Text);
                Response = response.ToString();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (version == Volatile.Read(ref _requestVersion))
                SetMode(FloatingAiBarMode.Ready);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (version == Volatile.Read(ref _requestVersion))
                SetMode(FloatingAiBarMode.Ready);
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _requestVersion))
                return;

            Error = exception.Message;
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
