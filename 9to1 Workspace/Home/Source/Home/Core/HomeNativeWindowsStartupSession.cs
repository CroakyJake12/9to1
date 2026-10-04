using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Checks the actual connected Home host and canonical compatibility API. The platform
/// owns the pipe; this object retains/drains its original client and never creates a Home host,
/// lease, current actor, service provider or installation action.</summary>
public sealed partial class HomeNativeWindowsStartupSession : IHomeNativeStartupSession, IAsyncDisposable
{
    private readonly HomeWindowsCoreClient _client;
    private readonly HomeCompatibilityRequest _request;
    private readonly CancellationToken _originalConnectionLifetime;
    internal Func<Task>? AfterOriginalClientResponse { get; set; }
    private readonly object _sync = new();
    private Task<HomeNativeStartupObservation>? _check;
    private Task? _close;
    private bool _closing;
    private string? _pendingRequest;

    private HomeNativeWindowsStartupSession(HomeWindowsCoreClient client, HomeCompatibilityRequest request,
        CancellationToken originalConnectionLifetime)
    { _client = client; _request = request; _originalConnectionLifetime = originalConnectionLifetime; }

    public static async ValueTask<HomeNativeWindowsStartupSession?> AttachAsync(NamedPipeClientStream originalConnectedPipe,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHomeHost,
        HomeCompatibilityRequest trustedAppRequirements, CancellationToken originalConnectionLifetime,
        CancellationToken cancellationToken = default)
    {
        // Freeze the trusted app/service tuple before host verification or any request awaits.
        var request = CaptureOriginalRequirements(trustedAppRequirements);
        var client = await HomeWindowsCoreClient.AttachAsync(originalConnectedPipe, verifier, trustedHomeHost,
            originalConnectionLifetime, cancellationToken).ConfigureAwait(false);
        return client is null ? null : new(client, request, originalConnectionLifetime);
    }

    /// <summary>Overlapping checks share the SAME original task and its first caller token.
    /// A later completed check always performs another attested compatibility request.</summary>
    public Task<HomeNativeStartupObservation> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_check is { IsCompleted: false }) return _check;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _check = CheckOriginalAsync(start.Task, cancellationToken);
            start.SetResult();
            return _check;
        }
    }

    private async Task<HomeNativeStartupObservation> CheckOriginalAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        try
        {
        var original = await _client.GetCompatibilityAsync(_request, caller).ConfigureAwait(false);
        // Default-null owning-fixture checkpoint; normal composition never assigns it.
        if (AfterOriginalClientResponse is { } after) await after().ConfigureAwait(false);
        await _client.DemandOriginalCurrentAsync(caller).ConfigureAwait(false);
        caller.ThrowIfCancellationRequested();
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var operation = original.Operation;
            if (operation.Code == "PermissionRequired" && !operation.Succeeded)
            {
                if (!Text(original.PermissionRequestId) || operation.Value is not null)
                    throw new InvalidDataException("Home returned an invalid pending request observation.");
                if (_pendingRequest is not null && _pendingRequest != original.PermissionRequestId)
                    throw new InvalidDataException("The original pending Home compatibility request changed.");
                _pendingRequest = original.PermissionRequestId;
                return Publish(new(HomeNativeStartupState.AwaitingApproval, "HomeStartupApprovalRequired",
                    "Home requires the original service-read review before normal startup.", _pendingRequest), caller);
            }
            if (operation.Succeeded && _pendingRequest is not null && original.PermissionRequestId != _pendingRequest)
                throw new InvalidDataException("The admitted compatibility read does not settle the original pending request.");
            _pendingRequest = null; // A settled request is never reused as an execution grant.
            if (!operation.Succeeded || operation.Value is not { } value)
                return Publish(new(HomeNativeStartupState.Unready, operation.Code,
                    "Home could not confirm the required installed service compatibility.", original.PermissionRequestId), caller);
            if (value.AppId != _request.AppId || value.RegistryRevision < 0 || value.Failures is null ||
                value.AcceptedServices is null || !Enum.IsDefined(value.State))
                throw new InvalidDataException("Home returned an invalid startup compatibility tuple.");
            if (value.State != HomeCompatibilityState.Compatible || value.Failures.Count != 0)
                return Publish(new(HomeNativeStartupState.Unready, "HomeStartupIncompatible",
                    "Required Home services need update, repair or recovery.", original.PermissionRequestId, value.RegistryRevision), caller);
            var requirements = _request.RequiredServices.ToDictionary(row => row.ServiceId, StringComparer.Ordinal);
            HashSet<string> accepted = new(StringComparer.Ordinal);
            foreach (var service in value.AcceptedServices)
            {
                if (service is null || !requirements.TryGetValue(service.ServiceId, out var required) ||
                    !accepted.Add(service.ServiceId) || service.Revision < 0 || !required.Accepts(service.ContractVersion))
                    throw new InvalidDataException("Home returned an unexpected or duplicate accepted service.");
                if (required.Required && (!service.IsAvailable || service.State != HomeServiceLifecycleState.Ready))
                    return Publish(new(HomeNativeStartupState.Unready, "HomeStartupServiceUnready",
                        "A required Home service is not ready.", original.PermissionRequestId, value.RegistryRevision), caller);
            }
            if (_request.RequiredServices.Any(row => row.Required && !accepted.Contains(row.ServiceId)))
                return Publish(new(HomeNativeStartupState.Unready, "HomeStartupServiceMissing",
                    "A required Home service was not confirmed.", original.PermissionRequestId, value.RegistryRevision), caller);
            return Publish(new(HomeNativeStartupState.Compatible, "HomeStartupCompatible",
                "The original installed Home connection confirmed the required service versions.",
                original.PermissionRequestId, value.RegistryRevision), caller);
        }
        }
        catch
        {
            lock (_sync) _closing = true; // Retain this SAME failed attempt; later calls cannot replace it.
            throw;
        }
    }

    private HomeNativeStartupObservation Publish(HomeNativeStartupObservation observation, CancellationToken caller)
    {
        // Called under _sync: refuse a known retired original connection at publication.
        // This check does not make token cancellation and response publication atomic.
        caller.ThrowIfCancellationRequested();
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_closing, this);
        return observation;
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task, _check);
            start.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task CloseOriginalAsync(Task start, Task? originalCheck)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        Task? originalClose = null;
        try { originalClose = _client.DisposeAsync().AsTask(); }
        catch (Exception error) { Add(failures, error); }
        try { if (originalCheck is not null) await originalCheck.ConfigureAwait(false); }
        catch (Exception error) { Add(failures, error); }
        try { if (originalClose is not null) await originalClose.ConfigureAwait(false); }
        catch (Exception error) { Add(failures, error); }
        // Client cancellation errors are retained by reference, including caller-first errors.
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original startup check and client close failed.", failures);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(row => ReferenceEquals(row, error))) errors.Add(error); }

    internal static HomeCompatibilityRequest CaptureOriginalRequirements(HomeCompatibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Text(request.AppId) || !Text(request.AppVersion) || request.RequiredServices is null)
            throw new ArgumentException("The trusted app/version/service requirement tuple is absent.", nameof(request));
        var rows = request.RequiredServices.Take(65).ToArray();
        if (rows.Length is 0 or > 64 || rows.Any(row => row is null || !Text(row.ServiceId) ||
            row.MajorVersion < 0 || row.MinimumMinorVersion < 0 ||
            row.MaximumMinorVersionExclusive is { } maximum && maximum <= row.MinimumMinorVersion) ||
            rows.Select(row => row.ServiceId).Distinct(StringComparer.Ordinal).Count() != rows.Length ||
            !rows.Any(row => row.ServiceId == "home.core" && row.Required) ||
            !rows.Any(row => row.ServiceId == "home.state" && row.Required))
            throw new ArgumentException("Startup requires unique bounded Home Core/state service versions.", nameof(request));
        return request with { RequiredServices = Array.AsReadOnly(rows.Select(row => row with { }).ToArray()) };
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 4096 && value == value.Trim();
}

