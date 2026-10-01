using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

internal sealed class SpacesStorageSetupCuiSurface(SpacesStorageSetupSession setup, ICuiSceneReadiness readiness,
    Func<string, CancellationToken, Task> reviewPermissions) : UserControl, IDisposable
{
    private readonly CuiSceneHost _host = new();
    private readonly CuiViewModel _model = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = _host;
        _model.Set("Status", "Checking local Spaces ownership…");
        _model.Set("CanBind", false); _model.Set("CanRequest", false); _model.Set("CanReview", false); _model.Set("CanRetryAudit", false);
        var state = await _host.ShowAsync(new("spaces.storage", "Spaces storage", "Spaces",
            new CuiRichParser().Parse(Document), _model, new Actions(this), readiness), token);
        if (state.State == CuiSceneAvailabilityState.Ready) await RefreshAsync(token);
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        var snapshot = await setup.InspectAsync(token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || token.IsCancellationRequested) return;
            _model.Set("CanBind", snapshot.CanBindEmpty);
            _model.Set("CanRequest", !snapshot.IsOwned && snapshot.PendingRequestId is null && !snapshot.CanBindEmpty);
            _model.Set("CanReview", !snapshot.IsOwned && snapshot.PendingRequestId is not null);
            _model.Set("CanRetryAudit", snapshot.PendingAuditRequestId is not null);
            _model.Set("Status", snapshot.PendingAuditRequestId is not null ? "Spaces ownership was imported. Retry its Home audit to finish recording the result."
                : snapshot.IsOwned ? "This local Spaces store belongs to your current Home profile."
                : snapshot.CanBindEmpty ? "Set up this empty Spaces store for your current Home profile."
                : "Existing Spaces are preserved. Review their ownership import in Home before editing.");
        });
    }

    private async ValueTask DispatchAsync(string command, CancellationToken token)
    {
        if (_disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var entered = false;
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false); entered = true;
            if (_disposed) return;
            if ((await readiness.CheckAsync(linked.Token).ConfigureAwait(false)).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("Home is unavailable. Reopen Spaces storage.");
            switch (command)
            {
                case "BindEmpty": await setup.BindEmptyAsync(linked.Token).ConfigureAwait(false); break;
                case "RequestImport": await setup.RequestImportAsync(linked.Token).ConfigureAwait(false); break;
                case "Review":
                    var current = await setup.InspectAsync(linked.Token).ConfigureAwait(false);
                    var requestId = current.PendingRequestId ?? throw new InvalidOperationException("Request an ownership import first.");
                    await Dispatcher.UIThread.InvokeAsync(() => reviewPermissions(requestId, linked.Token)); break;
                case "Complete": await setup.CompleteImportAsync(linked.Token).ConfigureAwait(false); break;
                case "RetryAudit": await setup.RetryAuditAsync(linked.Token).ConfigureAwait(false); break;
                case "Refresh": break;
                default: throw new InvalidOperationException("Unknown Spaces setup action.");
            }
            await RefreshAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) _model.Set("Status", error.Message); });
        }
        finally { if (entered) _gate.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _host.Dispose(); _lifetime.Dispose();
    }
    private sealed class Actions(SpacesStorageSetupCuiSurface owner) : ICuiActionDispatcher, ICuiActionAvailability
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) => owner.DispatchAsync(command, token);
        public bool HasAction(string command) => command is "BindEmpty" or "RequestImport" or "Review" or "Complete" or "RetryAudit" or "Refresh";
        public bool? IsActionAvailable(string command) => !owner._disposed && HasAction(command);
    }
    private const string Document = """
        <Cui><StackPanel margin="24" max-width="760">
          <TextBlock text="Spaces storage" font-size="26" font-weight="SemiBold" />
          <TextBlock text="{Binding Status}" text-wrapping="Wrap" margin="0,12,0,12" />
          <Button action="BindEmpty" content="Set up empty Spaces store" is-visible="{Binding CanBind}" min-height="44" />
          <Button action="RequestImport" content="Request ownership import" is-visible="{Binding CanRequest}" min-height="44" />
          <StackPanel is-visible="{Binding CanReview}">
            <Button action="Review" content="Review in Home" min-height="44" />
            <Button action="Complete" content="Finish approved import" min-height="44" />
          </StackPanel>
          <Button action="RetryAudit" content="Retry Home audit" is-visible="{Binding CanRetryAudit}" min-height="44" />
          <Button action="Refresh" content="Refresh storage status" min-height="44" />
        </StackPanel></Cui>
        """;
}
