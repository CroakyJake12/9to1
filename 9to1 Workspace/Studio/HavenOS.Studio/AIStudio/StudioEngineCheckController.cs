using System.Text.Json;
using NineToOne.Dulche.Training;

namespace HavenOS.AIStudio;

public sealed record StudioEngineAttempt(LocalTrainingAttempt Identity, string Status, string? Reason,
    int? CompletedUpdates, IReadOnlyList<LocalTrainingArtifact> Artifacts, LocalTrainingArtifact? RecoveryCheckpoint)
{
    public bool IsActive => Status is "prepared" or "running";
}

public sealed record StudioEngineCheckState(bool? Ready, bool RunInFlight, bool PauseRequested,
    bool OutcomeUnknown, StudioEngineAttempt? Selected, IReadOnlyList<StudioEngineAttempt> History,
    LocalTrainingAttempt? Next, string? Error)
{
    public bool CanLaunch => Ready == true && !RunInFlight && !OutcomeUnknown;
    public bool CanPause => Selected?.IsActive == true && !PauseRequested;
    public bool CanCheckRecovery => CanLaunch && Selected?.RecoveryCheckpoint is not null;
    public const string Title = "CPU engine self-check — software test only";
    public const string Description = "Checks tiny arithmetic updates and checkpoint recovery. It does not train a language model or validate GPU readiness.";
}

/// <summary>Studio-owned projection of the canonical worker API. No runtime paths,
/// dataset selection, process spawning or artifact mutation are exposed here.
/// Notifications can arrive on a worker thread; the CUI host owns dispatch/lifetime.</summary>
public sealed class StudioEngineCheckController(ILocalTrainingClient? client) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _reads = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private StudioEngineCheckState _state = new(null, false, false, false, null, [], null,
        client is null ? "The local engine self-check is not configured on this device." : null);
    private Task? _activeRun;
    private bool _closing;
    private LocalTrainingAttempt? _accepted;

    public StudioEngineCheckState State { get { lock (_gate) return _state; } }
    public event Action<StudioEngineCheckState>? Changed;

    private void Change(Func<StudioEngineCheckState, StudioEngineCheckState> update)
    {
        StudioEngineCheckState next;
        lock (_gate)
        {
            _state = next = update(_state);
            if (_closing) return;
        }
        Changed?.Invoke(next);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (client is null) { Change(s => s with { Ready = false }); return; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _reads.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var status = await client.ExecuteAsync(LocalTrainingCommand.Status, cancellationToken: linked.Token).ConfigureAwait(false);
            if (!AcceptResponse(status)) return;
            var ready = status.Terminal.Payload.GetProperty("ready").GetBoolean();
            Change(s => s with { Ready = ready, OutcomeUnknown = s.OutcomeUnknown && !ready, Error = null });
            await LoadPageCoreAsync(null, linked.Token).ConfigureAwait(false);
            if (State.Selected is { } selected)
                await ReadAttemptCoreAsync(selected.Identity, false, linked.Token).ConfigureAwait(false);
        }
        catch (IOException error) { ReportFailure(error); }
        catch (OperationCanceledException) { Change(s => s with { Error = "Refresh cancelled." }); }
        finally { _reads.Release(); }
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (client is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _reads.WaitAsync(linked.Token).ConfigureAwait(false);
        try { if (State.Next is { } cursor) await LoadPageCoreAsync(cursor, linked.Token).ConfigureAwait(false); }
        catch (IOException error) { ReportFailure(error); }
        finally { _reads.Release(); }
    }

    public async Task SelectAsync(LocalTrainingAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (client is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _reads.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await ReadAttemptCoreAsync(attempt, true, linked.Token).ConfigureAwait(false); }
        catch (IOException error) { ReportFailure(error); }
        finally { _reads.Release(); }
    }

    private async Task ReadAttemptCoreAsync(LocalTrainingAttempt attempt, bool select, CancellationToken token)
    {
        var response = await client!.ExecuteAsync(LocalTrainingCommand.Status, attempt, cancellationToken: token).ConfigureAwait(false);
        if (AcceptResponse(response))
        {
            Publish(Read(response.Terminal.Payload), select);
            Change(s => s with { OutcomeUnknown = false });
        }
    }

    private async Task LoadPageCoreAsync(LocalTrainingAttempt? cursor, CancellationToken token)
    {
        var response = await client!.ExecuteAsync(LocalTrainingCommand.List, cursor, limit: 20, cancellationToken: token).ConfigureAwait(false);
        if (!AcceptResponse(response)) return;
        var result = response.Terminal.Payload;
        var rows = result.GetProperty("attempts").EnumerateArray().Select(Read).ToArray();
        var next = result.GetProperty("next").ValueKind == JsonValueKind.Null ? null
            : LocalTrainingProtocol.ReadAttempt(result.GetProperty("next"));
        Change(s => s with { History = Merge(s.History, rows), Next = next });
    }

    public Task LaunchAsync(CancellationToken cancellationToken = default) => Start(null, cancellationToken);

    public Task CheckRecoveryAsync(CancellationToken cancellationToken = default)
    {
        var checkpoint = State.Selected?.RecoveryCheckpoint
            ?? throw new InvalidOperationException("Select an eligible self-check checkpoint first.");
        return Start(checkpoint, cancellationToken);
    }

    private Task Start(LocalTrainingArtifact? checkpoint, CancellationToken token)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (client is null || !_state.CanLaunch) throw new InvalidOperationException("Refresh worker readiness before launching a check.");
            _state = _state with { RunInFlight = true, PauseRequested = false, Error = null };
            _accepted = null;
            // An asynchronous boundary makes the task owned before callbacks run.
            _activeRun = RunAsync(checkpoint, token);
            return _activeRun;
        }
    }

    private async Task RunAsync(LocalTrainingArtifact? checkpoint, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Change(s => s);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var response = await client!.ExecuteAsync(checkpoint is null ? LocalTrainingCommand.Launch : LocalTrainingCommand.Resume,
                checkpoint: checkpoint, progress: new InlineProgress(Observe), cancellationToken: linked.Token).ConfigureAwait(false);
            if (AcceptResponse(response)) Publish(Read(response.Terminal.Payload));
        }
        catch (IOException error) { ReportFailure(error); }
        catch (OperationCanceledException)
        {
            Change(s => s with { OutcomeUnknown = _accepted is not null, Ready = false,
                Error = "Check request cancelled. Refresh retained status before another launch." });
        }
        finally { Change(s => s with { RunInFlight = false }); }
    }

    private void Observe(LocalTrainingEvent update)
    {
        if (update.Kind == LocalTrainingEventKind.Accepted)
        {
            var attempt = LocalTrainingProtocol.ReadAttempt(update.Payload);
            lock (_gate) _accepted = attempt;
            Publish(new(attempt, "prepared", null, null, [], null), select: true);
        }
        else if (update.Kind == LocalTrainingEventKind.Progress) Publish(Read(update.Payload));
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        var selected = State.Selected;
        if (client is null || selected?.IsActive != true) return;
        await RequestPauseAsync(selected.Identity, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequestPauseAsync(LocalTrainingAttempt attempt, CancellationToken cancellationToken)
    {
        Change(s => s with { PauseRequested = true, Error = null });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            var response = await client!.ExecuteAsync(LocalTrainingCommand.Pause, attempt, cancellationToken: linked.Token).ConfigureAwait(false);
            if (AcceptResponse(response)) Publish(Read(response.Terminal.Payload));
        }
        catch (IOException error) { ReportFailure(error); }
        catch (OperationCanceledException) { Change(s => s with { OutcomeUnknown = true, Error = "Pause outcome is unknown. Refresh retained status." }); }
    }

    private bool AcceptResponse(LocalTrainingResponse response)
    {
        if (response.OutcomeUnknown)
        {
            Change(s => s with { OutcomeUnknown = true, Ready = false, Error = "Worker outcome is not confirmed. Refresh retained status." });
            return false;
        }
        if (response.Terminal.Kind == LocalTrainingEventKind.Error)
        {
            var error = response.Terminal.Payload;
            Change(s => s with { Ready = false, Error = error.GetProperty("code").GetString() + ": " + error.GetProperty("message").GetString() });
            return false;
        }
        return true;
    }

    private void ReportFailure(IOException error)
    {
        var detail = error as LocalTrainingClientException;
        if (detail?.AcceptedAttempt is { } attempt)
        {
            lock (_gate) _accepted = attempt;
            if (State.Selected?.Identity != attempt) Publish(new(attempt, "prepared", null, null, [], null), select: true);
        }
        Change(s => s with { OutcomeUnknown = s.OutcomeUnknown || detail?.OutcomeUnknown == true,
            Ready = false, Error = "Local worker transport failed. Refresh retained status before retrying." });
    }

    private void Publish(StudioEngineAttempt row, bool select = false)
    {
        Change(s =>
        {
            var prior = s.History.FirstOrDefault(item => item.Identity == row.Identity);
            // Late status reads cannot replace an observed terminal result with a
            // running/prepared state from an earlier point in the same attempt.
            if (prior is { IsActive: false } && row.IsActive) row = prior;
            return s with { History = Merge(s.History, [row]),
                Selected = select || s.Selected?.Identity == row.Identity ? row : s.Selected,
                PauseRequested = !row.IsActive && s.Selected?.Identity == row.Identity ? false : s.PauseRequested };
        });
    }

    private static IReadOnlyList<StudioEngineAttempt> Merge(IEnumerable<StudioEngineAttempt> left, IEnumerable<StudioEngineAttempt> right) =>
        Array.AsReadOnly(left.Concat(right).GroupBy(row => row.Identity)
            .Select(group => group.Aggregate((prior, next) => !prior.IsActive && next.IsActive ? prior : next))
            .OrderBy(row => row.Identity.RunId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(row => row.Identity.AttemptId.ToString("D"), StringComparer.Ordinal).ToArray());

    private static StudioEngineAttempt Read(JsonElement value) => new(LocalTrainingProtocol.ReadAttempt(value, exact: false),
        value.GetProperty("status").GetString()!, value.GetProperty("reason").ValueKind == JsonValueKind.Null ? null : value.GetProperty("reason").GetString(),
        value.GetProperty("completed_updates").ValueKind == JsonValueKind.Null ? null : value.GetProperty("completed_updates").GetInt32(),
        Array.AsReadOnly(value.GetProperty("artifacts").EnumerateArray().Select(LocalTrainingProtocol.ReadArtifact).ToArray()),
        value.GetProperty("resume_checkpoint").ValueKind == JsonValueKind.Null ? null : LocalTrainingProtocol.ReadArtifact(value.GetProperty("resume_checkpoint")));

    public async ValueTask DisposeAsync()
    {
        Task? active;
        LocalTrainingAttempt? accepted;
        lock (_gate) { if (_closing) return; _closing = true; active = _activeRun; accepted = _accepted; }
        // Closing the owning Studio app requests a cooperative pause first.
        using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            if (active is { IsCompleted: false } && accepted is not null)
            {
                await RequestPauseAsync(accepted, grace.Token).ConfigureAwait(false);
                // Pause acknowledges the request, not necessarily its checkpoint.
                // Let the original stream settle within the remaining grace.
                try { await active.WaitAsync(grace.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (grace.IsCancellationRequested) { }
            }
        }
        finally
        {
            _lifetime.Cancel();
            if (active is not null) await active.ConfigureAwait(false);
        }
        // Other outstanding read calls share cancellation; their own finally
        // releases the semaphore. Do not dispose it beneath those calls.
        Changed = null;
    }

    private sealed class InlineProgress(Action<LocalTrainingEvent> action) : IProgress<LocalTrainingEvent>
    { public void Report(LocalTrainingEvent value) => action(value); }
}
