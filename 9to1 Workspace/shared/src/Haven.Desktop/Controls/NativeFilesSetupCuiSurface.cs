using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Controls;

/// <summary>The compiled native picker is the only source of a setup path; no model action accepts a path or profile.</summary>
public sealed class NativeFilesSetupCuiSurface(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles,
    NativeFilesWorkspaceService files, NativeFilesWorkspaceAuthority authority, HomeLocalStoreOwnership ownership,
    Func<CancellationToken, Task<string?>> chooseEmptyFolder, Func<CancellationToken, Task> reviewPermissions) : UserControl, IDisposable
{
    private readonly CuiSceneHost _host = new();
    private readonly CuiViewModel _model = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private AuthenticatedResourceActor? _actor;
    private NativeFilesWorkspace? _configured;
    private string? _importRequest;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        Content = _host;
        _model.Set("Status", "Checking Home and Files storage…");
        _model.Set("CanConfigure", false);
        _model.Set("NeedsImport", false);
        _model.Set("CanCompleteImport", false);
        using var stream = typeof(NativeFilesSetupCuiSurface).Assembly.GetManifestResourceStream("Haven.Desktop.Resources.Cui.FilesStorageSetup.cui")
            ?? throw new InvalidDataException("The canonical Files storage scene is missing.");
        using var reader = new StreamReader(stream);
        var document = new CuiRichParser().Parse(await reader.ReadToEndAsync(cancellationToken));
        var state = await _host.ShowAsync(new("files.storage", "Files storage", "Files", document, _model,
            new Actions(this), new Readiness(this)), cancellationToken);
        if (state.State == CuiSceneAvailabilityState.Ready) await RefreshAsync(cancellationToken);
    }

    private async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
    {
        var actor = await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null)
            return new(CuiSceneAvailabilityState.Unavailable, "FilesProfileUnavailable", "Recover the personal operating-system profile in Home.");
        var snapshot = await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        if (new[] { "home.core", "home.state", "permissions.trust" }.Any(id => !snapshot.Services.Any(service =>
            service.ServiceId == id && service.IsAvailable && service.State == HomeServiceLifecycleState.Ready &&
            service.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major &&
            service.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor)))
            return new(CuiSceneAvailabilityState.Unavailable, "FilesHomeUnavailable", "Home storage authority is unavailable. Existing data was preserved.");
        if (await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor)
            return new(CuiSceneAvailabilityState.Unavailable, "FilesProfileChanged", "The Home profile changed. Reopen Files storage.");
        _actor = actor;
        return new(CuiSceneAvailabilityState.Ready, "FilesHomeReady", "Home storage authority is ready.");
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RequireActorAsync(cancellationToken).ConfigureAwait(false);
        _configured = await files.GetConfiguredAsync(cancellationToken).ConfigureAwait(false);
        var current = await authority.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            _model.Set("CanConfigure", _configured is null);
            _model.Set("NeedsImport", _configured is not null && current is null);
            _model.Set("CanCompleteImport", _importRequest is not null && current is null);
            _model.Set("Location", _configured is null ? "" : "Folder: " + _configured.Configuration.RootDirectory);
            _model.Set("Status", current is null ? "Files storage is not yet authorised for this profile."
                : $"Files storage is ready. {current.Configuration.AppFolders.Count} canonical app folders are available.");
        });
    }

    private async Task RequireActorAsync(CancellationToken cancellationToken)
    {
        if (_actor is null || await profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != _actor)
            throw new UnauthorizedAccessException("The Home profile changed. Reopen Files storage.");
    }

    private async ValueTask DispatchAsync(string command, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var entered = false;
        try
        {
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            await RequireActorAsync(linked.Token).ConfigureAwait(false);
            string? message = null;
            switch (command)
            {
                case "ChooseFolder":
                    if (await files.GetConfiguredAsync(linked.Token).ConfigureAwait(false) is not null)
                        throw new InvalidOperationException("Files is already configured. Existing storage was preserved.");
                    var selected = await Dispatcher.UIThread.InvokeAsync(() => chooseEmptyFolder(linked.Token));
                    if (selected is null) return;
                    await RequireActorAsync(linked.Token).ConfigureAwait(false);
                    await files.ConfigureNewAsync(selected, ownership, linked.Token).ConfigureAwait(false);
                    break;
                case "RequestOwnership":
                    var configured = await files.GetConfiguredAsync(linked.Token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("No existing Files store is configured.");
                    var request = await ownership.RequestImportAsync("files", configured.Configuration.StoreId.ToString("D"),
                        _actor!.AuthenticationRevision, linked.Token).ConfigureAwait(false);
                    _importRequest = request.State is HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.PendingApproval or
                        HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Approved ? request.RequestId : null;
                    message = request.Message;
                    break;
                case "ReviewPermissions":
                    await Dispatcher.UIThread.InvokeAsync(() => reviewPermissions(linked.Token));
                    break;
                case "CompleteOwnership":
                    await ownership.CompleteImportAsync(_importRequest ?? throw new InvalidOperationException("Request an ownership import first."), linked.Token).ConfigureAwait(false);
                    _importRequest = null;
                    break;
                case "Refresh": break;
                default: throw new InvalidOperationException("Unknown Files setup action.");
            }
            await RefreshAsync(linked.Token).ConfigureAwait(false);
            if (message is not null) await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) _model.Set("Status", message); });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        { if (!_disposed) await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) _model.Set("Status", exception.Message); }); }
        finally { if (entered) _gate.Release(); }
    }

    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _host.Dispose(); _lifetime.Dispose(); }
    private sealed class Readiness(NativeFilesSetupCuiSurface owner) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => owner.CheckAsync(cancellationToken); }
    private sealed class Actions(NativeFilesSetupCuiSurface owner) : ICuiActionDispatcher, ICuiActionAvailability
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => owner.DispatchAsync(command, cancellationToken);
        public bool HasAction(string command) => command is "ChooseFolder" or "RequestOwnership" or "CompleteOwnership" or "ReviewPermissions" or "Refresh";
        public bool? IsActionAvailable(string command) => HasAction(command);
    }
}
