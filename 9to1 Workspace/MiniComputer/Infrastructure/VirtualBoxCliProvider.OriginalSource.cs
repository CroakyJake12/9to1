using System.Diagnostics;
using Haven.Application;

namespace HavenOS.Apps.MiniComputer;

public sealed partial class VirtualBoxCliProvider : IOriginalScopedVirtualisationProvider
{
    private readonly object _originalGate = new();
    private readonly List<Task> _originalOperations = [];
    private readonly Dictionary<Task, MiniComputerOriginalInvocation> _originalSources = new(ReferenceEqualityComparer.Instance);
    private bool _originalRetiring;
    private Task? _originalClose;

    /// <summary>Captures configuration without file discovery, process launch, catalogue
    /// reads or provider status claims. A string path is not original executable custody.</summary>
    public static VirtualBoxCliProvider CreateOriginalDeferred(IProviderIdentityMapStore identityMap,
        IVirtualDiskLocationProvider diskLocations, string? executablePath = null,
        TimeProvider? timeProvider = null) => new(identityMap, diskLocations, executablePath, timeProvider, true);
    private VirtualBoxCliProvider(IProviderIdentityMapStore identityMap,
        IVirtualDiskLocationProvider diskLocations, string? executablePath,
        TimeProvider? timeProvider, bool deferred)
    {
        _identityMap = identityMap ?? throw new ArgumentNullException(nameof(identityMap));
        _diskLocations = diskLocations ?? throw new ArgumentNullException(nameof(diskLocations));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _executablePath = null; _ = deferred; _ = executablePath;
    }

    public Task<MiniComputerOriginalProviderObservation> InvokeOriginalTargetWithinSourceAsync(
        MiniComputerOriginalProviderTarget sameTarget, Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameTarget); ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        Task<MiniComputerOriginalProviderObservation> result; TaskCompletionSource start;
        lock (_originalGate)
        {
            ObjectDisposedException.ThrowIf(_originalRetiring, this);
            // Successful original cohorts can leave the pending set. Failed/held ones
            // remain strongly owned, including their actual processes and pipe readers.
            foreach (var completed in _originalOperations.Where(value => value.IsCompletedSuccessfully).ToArray())
            { _originalOperations.Remove(completed); _originalSources.Remove(completed); }
            if (_originalOperations.Count >= 64)
                throw new InvalidOperationException("Mini Computer provider custody requires inspection.");
            var original = new MiniComputerOriginalInvocation(this, scope, retain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            result = DriveOriginalTarget(start.Task, original, sameTarget, token);
            _originalOperations.Add(result); _originalSources.Add(result, original);
        }
        start.SetResult(); return result;
    }

    private async Task<MiniComputerOriginalProviderObservation> DriveOriginalTarget(Task start,
        MiniComputerOriginalInvocation original, MiniComputerOriginalProviderTarget target, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        using var logical = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        MiniComputerOriginalProviderObservation? result = null;
        try
        {
            original.Run(() => { token.ThrowIfCancellationRequested(); target.Demand(this); });
            var path = original.Invoke(ObserveOriginalAdmittedExecutable);
            if (path is null)
            {
                result = new(new(VirtualMachineLifecycleState.ProviderUnavailable, _timeProvider.GetUtcNow(),
                    null, "The original provider executable/package custody is not configured. PATH discovery and a configured path are not permission or executable identity."),
                    false, false, "No provider command was launched.");
            }
            else
            {
                var before = await ReadOriginalKnownState(original, target, path).ConfigureAwait(false);
                var expected = ExpectedOriginalState(target.Action);
                if (target.Action == CanonicalMiniComputerAction.Inspect || before.State == expected)
                    result = new(before, false, false, target.Action == CanonicalMiniComputerAction.Inspect
                        ? "Current state observed from this exact VM provider." : "The VM already reports the requested state.");
                else if (!CanEnterOriginalTransition(target.Action, before.State))
                    result = new(before, false, false, "The provider's current VM state does not allow this requested transition.");
                else
                {
                    var command = OriginalTransitionArguments(target.Action, target.Original.ProviderMachineID);
                    var operation = await RunOriginalCommand(original, target, path, command).ConfigureAwait(false);
                    // Even a nonzero provider exit may follow a partial effect. Observe
                    // the SAME VM rather than inferring rollback or changing catalog state.
                    var after = await ReadOriginalKnownState(original, target, path).ConfigureAwait(false);
                    result = new(after, true, after.State != expected,
                        operation.ExitCode == 0
                            ? after.State == expected ? "The provider confirms the requested VM state."
                                : "The provider accepted the request; the destination state is not yet observed. Refresh to inspect it."
                            : "The provider returned a failure. Current VM state is shown; no rollback or automatic retry is implied.");
                }
            }
        }
        catch (Exception cause) { original.Remember(cause); }
        await original.CloseAsync().ConfigureAwait(false);
        return result ?? throw new InvalidOperationException("The actual provider supplied no settled observation.");
    }

    // Existing ordinary provider discovery remains unchanged. The new privileged
    // source refuses until a genuine configured executable owner can pin identity
    // through dispatch; neither PATH nor user metadata substitutes for that owner.
    private static string? ObserveOriginalAdmittedExecutable() => null;

    private async Task<ProviderStateObservation> ReadOriginalKnownState(MiniComputerOriginalInvocation source,
        MiniComputerOriginalProviderTarget target, string path)
    {
        var response = await RunOriginalCommand(source, target, path,
            ["showvminfo", target.Original.ProviderMachineID, "--machinereadable"]).ConfigureAwait(false);
        if (response.ExitCode != 0)
            throw new IOException("The configured provider could not observe the selected VM; its identity and state remain unconfirmed.");
        return source.Invoke(() =>
        {
            var fields = ParseMachineReadable(response.StandardOutput);
            if (!fields.TryGetValue("UUID", out var uuid) || !Guid.TryParse(uuid, out var actual) ||
                !Guid.TryParse(target.Original.ProviderMachineID, out var expected) || actual != expected)
                throw new InvalidDataException("The actual provider observation belongs to another VM.");
            fields.TryGetValue("VMState", out var state);
            return new ProviderStateObservation(MapState(state), _timeProvider.GetUtcNow(), state, null);
        });
    }

    private async Task<OriginalCommandResult> RunOriginalCommand(MiniComputerOriginalInvocation source,
        MiniComputerOriginalProviderTarget target, string path, IReadOnlyList<string> arguments)
    {
        Process? process = null; Task<string>? stdout = null; Task<string>? stderr = null;
        Task? exited = null; var started = false;
        // All productive process creation occurs within the SAME original callback.
        // Independently capture each child even if a sibling acquisition/retainer fails.
        try
        {
            source.Run(() =>
            {
                target.Demand(this);
                var info = new ProcessStartInfo { FileName = path, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var argument in arguments) info.ArgumentList.Add(argument);
                process = source.Own(new Process { StartInfo = info });
                started = process.Start();
                if (!started) throw new IOException("The exact provider process did not start.");
                void Capture(Action acquire)
                { try { acquire(); } catch (Exception cause) { source.Remember(cause); } }
                Capture(() => { stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None); source.Retain(stdout); });
                Capture(() => { stderr = process.StandardError.ReadToEndAsync(CancellationToken.None); source.Retain(stderr); });
                Capture(() => { exited = process.WaitForExitAsync(CancellationToken.None); source.Retain(exited); });
            });
        }
        catch (Exception cause) { source.Remember(cause); }
        // A scope postguard can fail after Start. The admitted process is still
        // owned; no presentation cancellation is allowed to orphan its real exit.
        if (started && process is not null && exited is null)
        {
            try { exited = process.WaitForExitAsync(CancellationToken.None); source.Retain(exited); }
            catch (Exception cause) { source.Remember(cause); }
        }
        await source.JoinRawAsync().ConfigureAwait(false);
        if (!started || process is null || stdout is null || stderr is null || exited is null)
            throw new InvalidOperationException("The original provider process and all output/exit tasks were not captured.");
        return source.Invoke(() => new OriginalCommandResult(process.ExitCode, stdout.Result, stderr.Result));
    }
    private sealed record OriginalCommandResult(int ExitCode, string StandardOutput, string StandardError);
    private static VirtualMachineLifecycleState? ExpectedOriginalState(CanonicalMiniComputerAction action) => action switch
    {
        CanonicalMiniComputerAction.Inspect => null,
        CanonicalMiniComputerAction.Start or CanonicalMiniComputerAction.Resume => VirtualMachineLifecycleState.Running,
        CanonicalMiniComputerAction.Pause => VirtualMachineLifecycleState.Paused,
        CanonicalMiniComputerAction.SaveState => VirtualMachineLifecycleState.Saved,
        CanonicalMiniComputerAction.Shutdown => VirtualMachineLifecycleState.PoweredOff,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    private static bool CanEnterOriginalTransition(CanonicalMiniComputerAction action, VirtualMachineLifecycleState state) => action switch
    {
        CanonicalMiniComputerAction.Start => state is VirtualMachineLifecycleState.PoweredOff or VirtualMachineLifecycleState.Saved,
        CanonicalMiniComputerAction.Pause => state is VirtualMachineLifecycleState.Running,
        CanonicalMiniComputerAction.Resume => state is VirtualMachineLifecycleState.Paused,
        CanonicalMiniComputerAction.SaveState => state is VirtualMachineLifecycleState.Running or VirtualMachineLifecycleState.Paused,
        CanonicalMiniComputerAction.Shutdown => state is VirtualMachineLifecycleState.Running,
        _ => false
    };
    private static string[] OriginalTransitionArguments(CanonicalMiniComputerAction action, string id) => action switch
    {
        CanonicalMiniComputerAction.Start => ["startvm", id, "--type", "headless"],
        CanonicalMiniComputerAction.Pause => ["controlvm", id, "pause"],
        CanonicalMiniComputerAction.Resume => ["controlvm", id, "resume"],
        CanonicalMiniComputerAction.SaveState => ["controlvm", id, "savestate"],
        CanonicalMiniComputerAction.Shutdown => ["controlvm", id, "acpipowerbutton"],
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    public Task? OriginalClose { get { lock (_originalGate) return _originalClose; } }
    public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    public void RequestOriginalRetirement() { lock (_originalGate) _originalRetiring = true; }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalRetirementJoin(); Task actual; TaskCompletionSource? start = null;
        lock (_originalGate)
        {
            _originalRetiring = true;
            if (_originalClose is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _originalClose = DrainOriginal(start.Task); }
            actual = _originalClose;
        }
        start?.SetResult(); return actual;
    }
    private async Task DrainOriginal(Task start)
    {
        await start.ConfigureAwait(false); Task[] all;
        lock (_originalGate) all = _originalOperations.ToArray();
        var errors = new List<Exception>();
        foreach (var task in all)
            try { await task.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(task.Exception ?? cause); }
        MiniComputerOriginalInvocation.Throw(errors);
    }
}
