using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HavenOS.Apps.MiniComputer;

public interface IVirtualDiskLocationProvider
{
    ValueTask<string> AllocateDiskPathAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
}

public sealed class LocalVirtualDiskLocationProvider : IVirtualDiskLocationProvider
{
    private readonly string _root;
    public LocalVirtualDiskLocationProvider(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
    }

    public ValueTask<string> AllocateDiskPathAsync(VirtualMachineId vmID, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(Path.Combine(_root, vmID.ToString(), "system.vdi"));
        var rootPrefix = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resolved disk path escaped the managed storage root.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return ValueTask.FromResult(path);
    }
}

/// <summary>External-host adapter. It never reports the separately installed CLI as a bundled fork.</summary>
public sealed class VirtualBoxCliProvider : IVirtualisationProvider
{
    public static ProviderId DefaultProviderID { get; } = new(Guid.Parse("df2f9a5a-60d3-4cf1-8d51-9ee128a040f4"));
    private readonly string? _executablePath;
    private readonly IProviderIdentityMapStore _identityMap;
    private readonly IVirtualDiskLocationProvider _diskLocations;
    private readonly TimeProvider _timeProvider;

    public VirtualBoxCliProvider(IProviderIdentityMapStore identityMap, IVirtualDiskLocationProvider diskLocations, string? executablePath = null, TimeProvider? timeProvider = null)
    {
        _identityMap = identityMap ?? throw new ArgumentNullException(nameof(identityMap));
        _diskLocations = diskLocations ?? throw new ArgumentNullException(nameof(diskLocations));
        _executablePath = ResolveExecutable(executablePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ProviderId ProviderID => DefaultProviderID;

    public async ValueTask<ProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken)
    {
        if (_executablePath is null)
            return new(ProviderID, "9to1 VirtualBox", "VirtualBox", null, false, true, false,
                "The VirtualBox provider executable is not installed in the application runtime or host PATH.",
                await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
        var version = await RunAsync(["--version"], cancellationToken).ConfigureAwait(false);
        if (!version.Success)
            return new(ProviderID, "9to1 VirtualBox", "VirtualBox", null, false, true, false,
                "VBoxManage did not return a provider version.", await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
        var connected = await RunAsync(["list", "vms", "--machinereadable"], cancellationToken).ConfigureAwait(false);
        return new(ProviderID, "9to1 VirtualBox", "VirtualBox", version.StandardOutput.Trim(), false, true, connected.Success,
            connected.Success ? null : "VBoxManage is installed, but its VM management service did not respond successfully.",
            await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
    }

    public ValueTask<ProviderCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var available = _executablePath is not null;
        var features = ImmutableDictionary.CreateBuilder<VirtualisationFeature, CapabilityStatus>();
        foreach (var feature in Enum.GetValues<VirtualisationFeature>())
        {
            var status = !available
                ? new CapabilityStatus(CapabilityState.Unavailable, "VirtualBox provider executable is not installed.")
                : IsImplemented(feature)
                    ? new CapabilityStatus(CapabilityState.Supported)
                    : new CapabilityStatus(CapabilityState.Unavailable, "This provider operation is not implemented by the current adapter.");
            features.Add(feature, status);
        }
        return ValueTask.FromResult(new ProviderCapabilities(features.ToImmutable(), RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            available ? ["x64", "x86", "arm64"] : [], false,
            available ? "Hardware acceleration has not been verified on this host." : "VirtualBox runtime is unavailable."));
    }

    public async ValueTask<VirtualMachinePage> ListVirtualMachinesAsync(string? continuationToken, int pageSize, CancellationToken cancellationToken)
    {
        if (pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var list = await RunAsync(["list", "vms", "--machinereadable"], cancellationToken).ConfigureAwait(false);
        EnsureSuccess(list, "ProviderUnavailable");
        var machines = ParseMachineList(list.StandardOutput);
        var offset = DecodeOffset(continuationToken);
        if (offset < 0) throw new InvalidDataException("Invalid provider page token.");
        var items = new List<VirtualMachine>(pageSize + 1);
        foreach (var machine in machines.Skip(offset).Take(pageSize + 1))
            items.Add(await ReadMachineAsync(machine.Id, machine.Name, cancellationToken).ConfigureAwait(false));
        var hasMore = items.Count > pageSize;
        return new(items.Take(pageSize).ToImmutableArray(), hasMore ? EncodeOffset(offset + pageSize) : null);
    }

    public async ValueTask<ProviderOperation<VirtualMachine>> CreateVirtualMachineAsync(VirtualMachineId vmID, VirtualMachineConfiguration configuration, CancellationToken cancellationToken)
    {
        var externalID = Guid.NewGuid().ToString("D");
        var diskPath = await _diskLocations.AllocateDiskPathAsync(vmID, cancellationToken).ConfigureAwait(false);
        var create = await RunAsync(["createvm", "--name", configuration.Name, "--uuid", externalID, "--register"], cancellationToken).ConfigureAwait(false);
        if (!create.Success) return ProviderOperation<VirtualMachine>.Failed(MapError(create), SafeMessage(create));
        var diskCreated = false;
        try
        {
            var memory = configuration.MemoryMiB.ToString(CultureInfo.InvariantCulture);
            var cpus = configuration.CpuCount.ToString(CultureInfo.InvariantCulture);
            var firmware = configuration.Firmware == "uefi" ? "efi" : "bios";
            var modify = await RunAsync(["modifyvm", externalID, "--ostype", ChooseOsType(configuration.GuestFamily, configuration.Architecture),
                "--cpus", cpus, "--memory", memory, "--firmware", firmware, "--nic1", ToVBoxNetwork(configuration.NetworkMode)], cancellationToken).ConfigureAwait(false);
            if (!modify.Success) throw new ProviderException(MapError(modify), SafeMessage(modify));
            var diskMiB = checked((ulong)Math.Ceiling(configuration.DiskSizeBytes / 1_048_576d));
            var disk = await RunAsync(["createmedium", "disk", "--filename", diskPath, "--format", "VDI", "--size", diskMiB.ToString(CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait(false);
            if (!disk.Success) throw new ProviderException(MapError(disk), SafeMessage(disk));
            diskCreated = File.Exists(diskPath);
            if (!diskCreated) throw new ProviderException("ProviderStateUnverified", "VirtualBox reported disk creation without creating the disk file.");
            var controller = await RunAsync(["storagectl", externalID, "--name", "SATA", "--add", "sata", "--controller", "IntelAhci"], cancellationToken).ConfigureAwait(false);
            if (!controller.Success) throw new ProviderException(MapError(controller), SafeMessage(controller));
            var attach = await RunAsync(["storageattach", externalID, "--storagectl", "SATA", "--port", "0", "--device", "0", "--type", "hdd", "--medium", diskPath], cancellationToken).ConfigureAwait(false);
            if (!attach.Success) throw new ProviderException(MapError(attach), SafeMessage(attach));
            if (configuration.InstallationMediaPath is { } iso)
            {
                if (!File.Exists(iso)) throw new ProviderException("MediaNotFound", "The selected installation medium does not exist.");
                var optical = await RunAsync(["storagectl", externalID, "--name", "Optical", "--add", "ide"], cancellationToken).ConfigureAwait(false);
                if (!optical.Success) throw new ProviderException(MapError(optical), SafeMessage(optical));
                var media = await RunAsync(["storageattach", externalID, "--storagectl", "Optical", "--port", "0", "--device", "0", "--type", "dvddrive", "--medium", iso], cancellationToken).ConfigureAwait(false);
                if (!media.Success) throw new ProviderException(MapError(media), SafeMessage(media));
            }
            var vm = await ReadMachineAsync(externalID, configuration.Name, cancellationToken).ConfigureAwait(false);
            if (vm.LifecycleState != VirtualMachineLifecycleState.PoweredOff)
                throw new ProviderException("ProviderStateUnverified", "The newly created VM did not report the expected PoweredOff state.");
            return ProviderOperation<VirtualMachine>.Completed(vm with
            {
                VMID = vmID, GuestFamily = configuration.GuestFamily, GuestVersion = configuration.GuestVersion,
                Architecture = configuration.Architecture, CpuCount = configuration.CpuCount, MemoryMiB = configuration.MemoryMiB,
                Firmware = configuration.Firmware, DiskIDs = [new VirtualDiskId(vmID.Value)],
                NetworkAdapterIDs = [new VirtualNetworkAdapterId(vmID.Value)]
            });
        }
        catch (Exception exception) when (exception is ProviderException or IOException or UnauthorizedAccessException or OverflowException)
        {
            var cleanup = await RunAsync(["unregistervm", externalID, "--delete"], CancellationToken.None).ConfigureAwait(false);
            var cleanupVerified = cleanup.Success && !await MachineExistsAsync(externalID, CancellationToken.None).ConfigureAwait(false);
            return ProviderOperation<VirtualMachine>.Failed(
                !cleanupVerified ? "CreateVMPartialFailureCleanupUnverified" : MapException(exception),
                $"VM creation failed: {SafeExceptionMessage(exception)}{(cleanupVerified ? " Provider cleanup was verified." : diskCreated ? " Cleanup was not verified; preserve VM and disk diagnostics." : " VM cleanup was not verified.")}");
        }
    }

    public ValueTask<ProviderOperation<VirtualMachine>> ImportVirtualMachineAsync(string source, VirtualMachineConfiguration? configuration, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<VirtualMachine>.Failed("ImportUnsupported", "OVF/OVA import preview and conversion are not implemented by this adapter."));

    public ValueTask<ProviderOperation<VirtualMachine>> CloneVirtualMachineAsync(VirtualMachineId sourceVMID, SnapshotId? snapshotID, string name, bool linked, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<VirtualMachine>.Failed(linked ? "LinkedCloneUnsupported" : "CloneUnsupported", "Clone identity and base dependency mapping are not implemented by this adapter."));

    public async ValueTask<ProviderStateObservation> GetStateAsync(VirtualMachineId vmID, CancellationToken cancellationToken)
    {
        var externalID = await FindExternalIDAsync(vmID, cancellationToken).ConfigureAwait(false);
        var result = await RunAsync(["showvminfo", externalID, "--machinereadable"], cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "VMNotFound");
        var fields = ParseMachineReadable(result.StandardOutput);
        fields.TryGetValue("VMState", out var rawState);
        return new(MapState(rawState), _timeProvider.GetUtcNow(), rawState, null);
    }

    public ValueTask<ProviderOperation<ProviderStateObservation>> StartAsync(VirtualMachineId vmID, CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(vmID, ["startvm", "--type", "headless"], VirtualMachineLifecycleState.Running, cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> PauseAsync(VirtualMachineId vmID, CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(vmID, ["controlvm", "pause"], VirtualMachineLifecycleState.Paused, cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> ResumeAsync(VirtualMachineId vmID, CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(vmID, ["controlvm", "resume"], VirtualMachineLifecycleState.Running, cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> SaveStateAsync(VirtualMachineId vmID, CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(vmID, ["controlvm", "savestate"], VirtualMachineLifecycleState.Saved, cancellationToken);

    public async ValueTask<ProviderOperation<ProviderStateObservation>> ShutdownAsync(VirtualMachineId vmID, GracefulShutdownMode mode, CancellationToken cancellationToken)
    {
        if (mode == GracefulShutdownMode.GuestTools)
            return ProviderOperation<ProviderStateObservation>.Failed("GuestIntegrationUnavailable", "Guest-tools shutdown requires a verified guest integration channel.");
        var externalID = await FindExternalIDAsync(vmID, cancellationToken).ConfigureAwait(false);
        var result = await RunAsync(["controlvm", externalID, "acpipowerbutton"], cancellationToken).ConfigureAwait(false);
        if (!result.Success) return ProviderOperation<ProviderStateObservation>.Failed(MapError(result), SafeMessage(result));
        var state = await GetStateAsync(vmID, cancellationToken).ConfigureAwait(false);
        return state.State == VirtualMachineLifecycleState.PoweredOff
            ? ProviderOperation<ProviderStateObservation>.Completed(state)
            : ProviderOperation<ProviderStateObservation>.Pending(JobId.New(), $"ACPI shutdown was sent. Provider reports {state.ProviderState ?? state.State.ToString()}.");
    }

    public ValueTask<ProviderOperation<ProviderStateObservation>> PowerOffAsync(VirtualMachineId vmID, CancellationToken cancellationToken) =>
        ExecuteTransitionAsync(vmID, ["controlvm", "poweroff"], VirtualMachineLifecycleState.PoweredOff, cancellationToken);

    public ValueTask<ProviderOperation<Snapshot>> CreateSnapshotAsync(VirtualMachineId vmID, string name, string? description, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<Snapshot>.Failed("SnapshotUnavailable", "Snapshot identity and branch reconciliation are not implemented by this provider adapter."));

    public ValueTask<ProviderOperation<Snapshot>> RestoreSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<Snapshot>.Failed("SnapshotUnavailable", "Snapshot restore is not implemented by this provider adapter."));

    public ValueTask<ProviderOperation<bool>> DeleteSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<bool>.Failed("SnapshotUnavailable", "Snapshot deletion is not implemented by this provider adapter."));

    public ValueTask<ProviderOperation<VirtualDisk>> CreateDiskAsync(VirtualDiskId diskID, string path, long sizeBytes, VirtualDiskFormat format, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<VirtualDisk>.Failed("DiskOperationUnavailable", "Standalone disks and Files resource mapping are not implemented by this adapter."));

    public ValueTask<ProviderOperation<VirtualDisk>> ResizeDiskAsync(VirtualDiskId diskID, long newSizeBytes, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<VirtualDisk>.Failed("DiskOperationUnavailable", "Disk resize is not implemented by this adapter."));

    public async ValueTask<ProviderOperation<bool>> DeleteVirtualMachineAsync(VirtualMachineId vmID, bool deleteDisks, CancellationToken cancellationToken)
    {
        var externalID = await FindExternalIDAsync(vmID, cancellationToken).ConfigureAwait(false);
        var args = deleteDisks ? new[] { "unregistervm", externalID, "--delete" } : ["unregistervm", externalID];
        var result = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return ProviderOperation<bool>.Failed(MapError(result), SafeMessage(result));
        if (await MachineExistsAsync(externalID, cancellationToken).ConfigureAwait(false))
            return ProviderOperation<bool>.Failed("DeleteStateUnverified", "VirtualBox completed unregistervm but still lists the machine.");
        await _identityMap.RemoveAsync(ProviderID, externalID, cancellationToken).ConfigureAwait(false);
        return ProviderOperation<bool>.Completed(true);
    }

    private async ValueTask<ProviderOperation<ProviderStateObservation>> ExecuteTransitionAsync(
        VirtualMachineId vmID, string[] command, VirtualMachineLifecycleState expected, CancellationToken cancellationToken)
    {
        var externalID = await FindExternalIDAsync(vmID, cancellationToken).ConfigureAwait(false);
        var args = command.ToList();
        args.Insert(1, externalID);
        var result = await RunAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success) return ProviderOperation<ProviderStateObservation>.Failed(MapError(result), SafeMessage(result));
        var observed = await GetStateAsync(vmID, cancellationToken).ConfigureAwait(false);
        return observed.State == expected
            ? ProviderOperation<ProviderStateObservation>.Completed(observed)
            : ProviderOperation<ProviderStateObservation>.Pending(JobId.New(), $"VirtualBox accepted the request and reports {observed.ProviderState ?? observed.State.ToString()}; expected {expected}.");
    }

    private async ValueTask<VirtualMachine> ReadMachineAsync(string externalID, string name, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["showvminfo", externalID, "--machinereadable"], cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "VMNotFound");
        var fields = ParseMachineReadable(result.StandardOutput);
        var vmID = await _identityMap.GetOrCreateAsync(ProviderID, externalID, cancellationToken).ConfigureAwait(false);
        fields.TryGetValue("VMState", out var rawState);
        fields.TryGetValue("ostype", out var guestType);
        fields.TryGetValue("cpus", out var cpuValue);
        fields.TryGetValue("memory", out var memoryValue);
        fields.TryGetValue("firmware", out var firmware);
        var now = _timeProvider.GetUtcNow();
        return new(vmID, ProviderID, externalID, name, guestType ?? "unknown", null, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            ParseInt(cpuValue), ParseInt(memoryValue), string.Equals(firmware, "EFI", StringComparison.OrdinalIgnoreCase) ? "uefi" : "bios",
            MapState(rawState), 1, 1, now, now, [], [], []);
    }

    private async ValueTask<string> FindExternalIDAsync(VirtualMachineId vmID, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["list", "vms", "--machinereadable"], cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "ProviderUnavailable");
        foreach (var machine in ParseMachineList(result.StandardOutput))
            if (await _identityMap.GetOrCreateAsync(ProviderID, machine.Id, cancellationToken).ConfigureAwait(false) == vmID)
                return machine.Id;
        throw new ProviderException("VMNotFound", "The provider no longer lists this registered VM.");
    }

    private async ValueTask<bool> MachineExistsAsync(string externalID, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["list", "vms", "--machinereadable"], cancellationToken).ConfigureAwait(false);
        return result.Success && ParseMachineList(result.StandardOutput).Any(machine => string.Equals(machine.Id, externalID, StringComparison.OrdinalIgnoreCase));
    }

    private async ValueTask<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (_executablePath is null) return new(false, -1, "", "VBoxManage is not installed.");
        var start = new ProcessStartInfo { FileName = _executablePath, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return new(false, -1, "", "Provider process did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                cancellationToken.ThrowIfCancellationRequested();
                return new(false, -1, "", "Provider command exceeded the five-minute operation limit.");
            }
            return new(process.ExitCode == 0, process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, -1, "", exception.Message);
        }
    }

    private static string? ResolveExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var name = OperatingSystem.IsWindows() ? "VBoxManage.exe" : "VBoxManage";
        var found = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), name)).FirstOrDefault(File.Exists);
        if (found is not null) return Path.GetFullPath(found);
        if (OperatingSystem.IsWindows())
        {
            var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Oracle", "VirtualBox", name);
            if (File.Exists(standard)) return standard;
        }
        return null;
    }

    private static IReadOnlyList<(string Id, string Name)> ParseMachineList(string output)
    {
        var machines = new List<(string Id, string Name)>();
        string? id = null;
        string? name = null;
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            if (key.Equals("UUID", StringComparison.OrdinalIgnoreCase))
            {
                if (id is not null) machines.Add((id, name ?? id));
                id = value;
                name = null;
            }
            else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = value;
        }
        if (id is not null) machines.Add((id, name ?? id));
        return machines;
    }

    private static Dictionary<string, string> ParseMachineReadable(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            result[line[..separator].Trim()] = Unquote(line[(separator + 1)..].Trim());
        }
        return result;
    }

    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
        : value;

    private static VirtualMachineLifecycleState MapState(string? value) => value?.ToLowerInvariant() switch
    {
        "poweroff" => VirtualMachineLifecycleState.PoweredOff,
        "running" => VirtualMachineLifecycleState.Running,
        "paused" => VirtualMachineLifecycleState.Paused,
        "saved" => VirtualMachineLifecycleState.Saved,
        "starting" => VirtualMachineLifecycleState.Starting,
        "stopping" => VirtualMachineLifecycleState.Stopping,
        "saving" => VirtualMachineLifecycleState.SavingState,
        "restoring" => VirtualMachineLifecycleState.Resuming,
        "aborted" or "gurumeditation" or "stuck" => VirtualMachineLifecycleState.Crashed,
        _ => VirtualMachineLifecycleState.Unknown
    };

    private static string ChooseOsType(string family, string architecture) => (family.ToLowerInvariant(), architecture.ToLowerInvariant()) switch
    {
        ("linux", "x64") => "Ubuntu_64",
        ("linux", "arm64") => "Ubuntu_arm64",
        ("windows", "x64") => "Windows11_64",
        ("windows", "x86") => "Windows10",
        ("freebsd", "x64") => "FreeBSD_64",
        _ => "Other_64"
    };

    private static string ToVBoxNetwork(string mode) => mode switch
    {
        "nat" => "nat",
        "bridged" => "bridged",
        "host-only" => "hostonly",
        "internal" => "intnet",
        "nat-network" => "natnetwork",
        "disconnected" => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static bool IsImplemented(VirtualisationFeature feature) =>
        feature is VirtualisationFeature.CreateMachine or VirtualisationFeature.SavedState or VirtualisationFeature.NatNetworking;

    private static string EncodeOffset(int offset) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"vbox-v1:{offset}"));
    private static int DecodeOffset(string? token)
    {
        if (token is null) return 0;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return value.StartsWith("vbox-v1:", StringComparison.Ordinal) && int.TryParse(value.AsSpan(8), out var offset) && offset >= 0 ? offset : -1;
        }
        catch (FormatException) { return -1; }
    }
    private static int ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static void EnsureSuccess(ProcessResult result, string code)
    {
        if (!result.Success) throw new ProviderException(code, SafeMessage(result));
    }
    private static string MapError(ProcessResult result) => result.ExitCode == -1 ? "ProviderUnavailable" : "ProviderOperationFailed";
    private static string MapException(Exception exception) => exception is ProviderException providerException ? providerException.Code : "ProviderOperationFailed";
    private static string SafeMessage(ProcessResult result) => string.IsNullOrWhiteSpace(result.StandardError) ? "Provider command failed; consult provider diagnostics." : Sanitize(result.StandardError);
    private static string SafeExceptionMessage(Exception exception) => Sanitize(exception.Message);
    private static string Sanitize(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = string.IsNullOrWhiteSpace(profile) ? value : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return text.Length > 1000 ? text[..1000] : text;
    }

    private sealed record ProcessResult(bool Success, int ExitCode, string StandardOutput, string StandardError);
    private sealed class ProviderException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
