using System.Collections.Immutable;

namespace HavenOS.Apps.MiniComputer;

public sealed class MiniComputerEngine : IMiniComputerEventSource
{
    private readonly IVirtualisationProviderRegistry _providers;
    private readonly IMiniComputerCatalogStore _store;
    private readonly IHighRiskActionAuthorizer _authorizer;
    private readonly TimeProvider _timeProvider;

    public MiniComputerEngine(
        IVirtualisationProviderRegistry providers,
        IMiniComputerCatalogStore store,
        IHighRiskActionAuthorizer authorizer,
        TimeProvider? timeProvider = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<MiniComputerEvent>? EventPublished;

    public async ValueTask<IReadOnlyList<ProviderDescriptor>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ProviderDescriptor>(_providers.Providers.Count);
        foreach (var provider in _providers.Providers)
            result.Add(await provider.GetDescriptorAsync(cancellationToken).ConfigureAwait(false));
        return result;
    }

    public async ValueTask<ProviderCapabilities?> GetProviderCapabilitiesAsync(ProviderId providerID, CancellationToken cancellationToken = default) =>
        _providers.Find(providerID) is { } provider
            ? await provider.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false)
            : null;

    public async ValueTask<ProviderOperation<ProviderId>> SetDefaultProviderAsync(ProviderId providerID, CancellationToken cancellationToken = default)
    {
        if (_providers.Find(providerID) is not { } provider)
            return ProviderOperation<ProviderId>.Failed("ProviderUnavailable", "The selected provider is not registered.");
        var descriptor = await provider.GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptor.IsConnected)
            return ProviderOperation<ProviderId>.Failed("ProviderUnavailable", descriptor.UnavailableReason ?? "The selected provider cannot currently connect.");
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(snapshot with { DefaultProviderID = providerID }, cancellationToken).ConfigureAwait(false);
        Publish("DefaultProviderChanged", providerID, null, null);
        return ProviderOperation<ProviderId>.Completed(providerID);
    }

    public async ValueTask<ProviderOperation<VirtualMachine>> CreateVMAsync(
        VirtualMachineConfiguration configuration,
        ProviderId? providerID = null,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(configuration);
        if (validationError is not null) return ProviderOperation<VirtualMachine>.Failed("InvalidConfiguration", validationError);
        var provider = ResolveProvider(providerID, await _store.ReadAsync(cancellationToken).ConfigureAwait(false));
        if (provider is null) return ProviderOperation<VirtualMachine>.Failed("ProviderUnavailable", "No registered default virtualisation provider is available.");
        var descriptor = await provider.GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (!descriptor.IsConnected) return ProviderOperation<VirtualMachine>.Failed("ProviderUnavailable", descriptor.UnavailableReason ?? "Provider is unavailable.");
        var capabilities = await provider.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (capabilities.Get(VirtualisationFeature.CreateMachine).State != CapabilityState.Supported)
            return ProviderOperation<VirtualMachine>.Failed("ProviderCapabilityUnavailable", capabilities.Get(VirtualisationFeature.CreateMachine).Reason ?? "This provider cannot create virtual machines.");

        var vmID = VirtualMachineId.New();
        var created = await provider.CreateVirtualMachineAsync(vmID, configuration, cancellationToken).ConfigureAwait(false);
        if (created.State != ProviderOperationState.Completed || created.Value is null) return created;
        if (created.Value.VMID != vmID)
            return ProviderOperation<VirtualMachine>.Failed("ProviderIdentityMismatch", "The provider returned a virtual machine with an unexpected canonical VMID.");
        try
        {
            var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
            await _store.WriteAsync(snapshot with { VirtualMachines = snapshot.VirtualMachines.Add(created.Value) }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var cleanup = await provider.DeleteVirtualMachineAsync(vmID, deleteDisks: true, CancellationToken.None).ConfigureAwait(false);
            return ProviderOperation<VirtualMachine>.Failed(
                cleanup.State == ProviderOperationState.Completed && cleanup.Value == true
                    ? "CatalogWriteFailedRolledBack"
                    : "CatalogWriteFailedCleanupUnverified",
                cleanup.State == ProviderOperationState.Completed && cleanup.Value == true
                    ? $"The VM was created but catalog persistence failed; provider cleanup was verified. {exception.Message}"
                    : $"The VM was created but catalog persistence failed and cleanup was not verified. Preserve provider diagnostics. {exception.Message}");
        }

        Publish("VMCreated", provider.ProviderID, vmID, null);
        return created;
    }

    public async ValueTask<ProviderOperation<VirtualMachinePage>> ListVMsAsync(
        int pageSize = 50,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 500) return ProviderOperation<VirtualMachinePage>.Failed("InvalidPageSize", "Page size must be between 1 and 500.");
        var snapshot = await RefreshProviderInventoryAsync(cancellationToken).ConfigureAwait(false);
        var offset = ParsePageToken(continuationToken);
        if (offset < 0) return ProviderOperation<VirtualMachinePage>.Failed("InvalidContinuationToken", "The continuation token is invalid.");
        var items = snapshot.VirtualMachines.OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase).ThenBy(vm => vm.VMID.Value).Skip(offset).Take(pageSize + 1).ToArray();
        var hasMore = items.Length > pageSize;
        var page = new VirtualMachinePage(items.Take(pageSize).ToImmutableArray(), hasMore ? CreatePageToken(offset + pageSize) : null);
        return ProviderOperation<VirtualMachinePage>.Completed(page);
    }

    private async ValueTask<MiniComputerCatalogSnapshot> RefreshProviderInventoryAsync(CancellationToken cancellationToken)
    {
        var catalog = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var machines = catalog.VirtualMachines.ToDictionary(vm => vm.VMID);
        var changed = false;
        foreach (var provider in _providers.Providers)
        {
            var descriptor = await provider.GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
            if (!descriptor.IsConnected)
            {
                Publish("ProviderUnavailable", provider.ProviderID, null, null, ("reason", descriptor.UnavailableReason ?? "Provider is unavailable."));
                continue;
            }

            string? token = null;
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await provider.ListVirtualMachinesAsync(token, 100, cancellationToken).ConfigureAwait(false);
                foreach (var observed in page.Items)
                {
                    if (observed.ProviderID != provider.ProviderID) continue;
                    if (machines.TryGetValue(observed.VMID, out var existing))
                    {
                        var updated = observed with
                        {
                            CreatedAt = existing.CreatedAt,
                            Revision = existing.Revision + (HasMaterialChange(existing, observed) ? 1 : 0),
                            UpdatedAt = HasMaterialChange(existing, observed) ? _timeProvider.GetUtcNow() : existing.UpdatedAt,
                            DiskIDs = existing.DiskIDs,
                            NetworkAdapterIDs = existing.NetworkAdapterIDs,
                            SnapshotIDs = existing.SnapshotIDs,
                            EnvironmentID = existing.EnvironmentID,
                            IsEphemeral = existing.IsEphemeral
                        };
                        if (updated != existing) { machines[observed.VMID] = updated; changed = true; }
                    }
                    else
                    {
                        machines.Add(observed.VMID, observed);
                        changed = true;
                        Publish("VMDiscovered", provider.ProviderID, observed.VMID, null);
                    }
                }
                token = page.ContinuationToken;
                if (token is not null && !seenTokens.Add(token))
                {
                    Publish("ProviderInventoryIncomplete", provider.ProviderID, null, null, ("reason", "Provider repeated a continuation token."));
                    break;
                }
            } while (token is not null);
        }

        if (!changed) return catalog;
        var refreshed = catalog with { VirtualMachines = machines.Values.OrderBy(vm => vm.CreatedAt).ThenBy(vm => vm.VMID.Value).ToImmutableArray() };
        await _store.WriteAsync(refreshed, cancellationToken).ConfigureAwait(false);
        return refreshed;
    }

    private static bool HasMaterialChange(VirtualMachine current, VirtualMachine observed) =>
        current.Name != observed.Name || current.GuestFamily != observed.GuestFamily || current.GuestVersion != observed.GuestVersion ||
        current.Architecture != observed.Architecture || current.CpuCount != observed.CpuCount || current.MemoryMiB != observed.MemoryMiB ||
        current.Firmware != observed.Firmware || current.LifecycleState != observed.LifecycleState;

    public async ValueTask<ProviderOperation<VirtualMachine>> GetVMAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var vm = snapshot.VirtualMachines.FirstOrDefault(item => item.VMID == vmID);
        return vm is null
            ? ProviderOperation<VirtualMachine>.Failed("VMNotFound", "The virtual machine is not registered.")
            : ProviderOperation<VirtualMachine>.Completed(vm);
    }

    public ValueTask<ProviderOperation<ProviderStateObservation>> StartAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default) =>
        TransitionAsync(vmID, VirtualMachineLifecycleState.Running, (provider, token) => provider.StartAsync(vmID, token), cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> PauseAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default) =>
        TransitionAsync(vmID, VirtualMachineLifecycleState.Paused, (provider, token) => provider.PauseAsync(vmID, token), cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> ResumeAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default) =>
        TransitionAsync(vmID, VirtualMachineLifecycleState.Running, (provider, token) => provider.ResumeAsync(vmID, token), cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> SaveStateAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default) =>
        TransitionAsync(vmID, VirtualMachineLifecycleState.Saved, (provider, token) => provider.SaveStateAsync(vmID, token), cancellationToken);

    public ValueTask<ProviderOperation<ProviderStateObservation>> ShutdownAsync(VirtualMachineId vmID, GracefulShutdownMode mode, CancellationToken cancellationToken = default) =>
        TransitionAsync(vmID, VirtualMachineLifecycleState.PoweredOff, (provider, token) => provider.ShutdownAsync(vmID, mode, token), cancellationToken);

    public async ValueTask<ProviderOperation<ProviderStateObservation>> PowerOffAsync(VirtualMachineId vmID, CancellationToken cancellationToken = default)
    {
        var vmResult = await GetVMAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (vmResult.Value is not { } vm) return ProviderOperation<ProviderStateObservation>.Failed(vmResult.ErrorCode ?? "VMNotFound", vmResult.Message ?? "VM is unavailable.");
        var auth = await _authorizer.RequestAuthorizationAsync("PowerOff", vmID, [$"VM:{vmID}", "unsaved guest state"], cancellationToken).ConfigureAwait(false);
        if (auth.State != ProviderOperationState.Completed || auth.Value is not { Approved: true })
            return ProviderOperation<ProviderStateObservation>.Failed(auth.ErrorCode ?? "PermissionRequired", auth.Message ?? "Power Off requires explicit approval.");
        return await TransitionAsync(vmID, VirtualMachineLifecycleState.PoweredOff, (provider, token) => provider.PowerOffAsync(vmID, token), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProviderOperation<Snapshot>> CreateSnapshotAsync(VirtualMachineId vmID, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return ProviderOperation<Snapshot>.Failed("InvalidName", "Snapshot name is required.");
        var provider = await FindProviderForVMAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (provider is null) return ProviderOperation<Snapshot>.Failed("ProviderUnavailable", "The VM's provider is unavailable.");
        var result = await provider.CreateSnapshotAsync(vmID, name.Trim(), description, cancellationToken).ConfigureAwait(false);
        if (result.State == ProviderOperationState.Completed && result.Value is { } snapshot)
        {
            var catalog = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
            await _store.WriteAsync(catalog with
            {
                Snapshots = catalog.Snapshots.Add(snapshot),
                VirtualMachines = catalog.VirtualMachines.Select(vm => vm.VMID == vmID ? vm with { SnapshotIDs = vm.SnapshotIDs.Add(snapshot.SnapshotID), Revision = vm.Revision + 1, UpdatedAt = _timeProvider.GetUtcNow() } : vm).ToImmutableArray()
            }, cancellationToken).ConfigureAwait(false);
            Publish("SnapshotCreated", provider.ProviderID, vmID, snapshot.SnapshotID);
        }
        return result;
    }

    public async ValueTask<ProviderOperation<Snapshot>> RestoreSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken = default)
    {
        var catalog = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = catalog.Snapshots.FirstOrDefault(item => item.SnapshotID == snapshotID);
        if (snapshot is null) return ProviderOperation<Snapshot>.Failed("SnapshotNotFound", "The snapshot is not registered.");
        var linkedDependants = catalog.Snapshots.Where(item => item.ParentSnapshotID == snapshotID).Select(item => item.SnapshotID.ToString()).ToArray();
        var resources = new List<string> { $"Snapshot:{snapshotID}", $"VM:{snapshot.VMID}" };
        resources.AddRange(linkedDependants.Select(id => $"DependentSnapshot:{id}"));
        var auth = await _authorizer.RequestAuthorizationAsync("RestoreSnapshot", snapshot.VMID, resources, cancellationToken).ConfigureAwait(false);
        if (auth.State != ProviderOperationState.Completed || auth.Value is not { Approved: true })
            return ProviderOperation<Snapshot>.Failed(auth.ErrorCode ?? "PermissionRequired", auth.Message ?? "Snapshot restore requires explicit approval.");
        var provider = await FindProviderForVMAsync(snapshot.VMID, cancellationToken).ConfigureAwait(false);
        if (provider is null) return ProviderOperation<Snapshot>.Failed("ProviderUnavailable", "The VM's provider is unavailable.");
        var result = await provider.RestoreSnapshotAsync(snapshotID, cancellationToken).ConfigureAwait(false);
        if (result.State == ProviderOperationState.Completed)
            Publish("SnapshotRestored", provider.ProviderID, snapshot.VMID, snapshotID);
        return result;
    }

    public async ValueTask<ProviderOperation<bool>> DeleteSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken = default)
    {
        var catalog = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = catalog.Snapshots.FirstOrDefault(item => item.SnapshotID == snapshotID);
        if (snapshot is null) return ProviderOperation<bool>.Failed("SnapshotNotFound", "The snapshot is not registered.");
        var dependants = catalog.Snapshots.Where(item => item.ParentSnapshotID == snapshotID).ToArray();
        if (dependants.Length > 0)
            return ProviderOperation<bool>.Failed("SnapshotDependencyBlocked", $"Snapshot is required by {dependants.Length} dependent snapshot(s); consolidate or migrate the branch first.");
        var auth = await _authorizer.RequestAuthorizationAsync("DeleteSnapshot", snapshot.VMID, [$"Snapshot:{snapshotID}"], cancellationToken).ConfigureAwait(false);
        if (auth.State != ProviderOperationState.Completed || auth.Value is not { Approved: true })
            return ProviderOperation<bool>.Failed(auth.ErrorCode ?? "PermissionRequired", auth.Message ?? "Snapshot deletion requires explicit approval.");
        var provider = await FindProviderForVMAsync(snapshot.VMID, cancellationToken).ConfigureAwait(false);
        if (provider is null) return ProviderOperation<bool>.Failed("ProviderUnavailable", "The VM's provider is unavailable.");
        var deleted = await provider.DeleteSnapshotAsync(snapshotID, cancellationToken).ConfigureAwait(false);
        if (deleted.State != ProviderOperationState.Completed || deleted.Value != true) return deleted;
        catalog = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(catalog with
        {
            Snapshots = catalog.Snapshots.Remove(snapshot),
            VirtualMachines = catalog.VirtualMachines.Select(vm => vm.VMID == snapshot.VMID ? vm with { SnapshotIDs = vm.SnapshotIDs.Remove(snapshotID), Revision = vm.Revision + 1, UpdatedAt = _timeProvider.GetUtcNow() } : vm).ToImmutableArray()
        }, cancellationToken).ConfigureAwait(false);
        Publish("SnapshotDeleted", provider.ProviderID, snapshot.VMID, snapshotID);
        return deleted;
    }

    public async ValueTask<ProviderOperation<bool>> DeleteVMAsync(VirtualMachineId vmID, bool deleteDisks, CancellationToken cancellationToken = default)
    {
        var vmResult = await GetVMAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (vmResult.Value is not { } vm) return ProviderOperation<bool>.Failed(vmResult.ErrorCode ?? "VMNotFound", vmResult.Message ?? "VM is unavailable.");
        var disks = vm.DiskIDs.Select(id => $"Disk:{id}").ToArray();
        var authorisation = await _authorizer.RequestAuthorizationAsync("DeleteVM", vmID, [$"VM:{vmID}", .. (deleteDisks ? disks : [])], cancellationToken).ConfigureAwait(false);
        if (authorisation.State != ProviderOperationState.Completed || authorisation.Value is not { Approved: true })
            return ProviderOperation<bool>.Failed(authorisation.ErrorCode ?? "PermissionRequired", authorisation.Message ?? "VM deletion requires explicit approval.");
        var provider = _providers.Find(vm.ProviderID);
        if (provider is null) return ProviderOperation<bool>.Failed("ProviderUnavailable", "The VM's provider is not registered.");
        var result = await provider.DeleteVirtualMachineAsync(vmID, deleteDisks, cancellationToken).ConfigureAwait(false);
        if (result.State != ProviderOperationState.Completed || result.Value != true) return result;
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(snapshot with
        {
            VirtualMachines = snapshot.VirtualMachines.Remove(vm),
            Snapshots = snapshot.Snapshots.RemoveRange(snapshot.Snapshots.Where(item => item.VMID == vmID)),
            Disks = deleteDisks ? snapshot.Disks.RemoveRange(snapshot.Disks.Where(disk => disk.AttachedVMIDs.Contains(vmID))) : snapshot.Disks
        }, cancellationToken).ConfigureAwait(false);
        Publish("VMDeleted", provider.ProviderID, vmID, null);
        return result;
    }

    private async ValueTask<ProviderOperation<ProviderStateObservation>> TransitionAsync(
        VirtualMachineId vmID,
        VirtualMachineLifecycleState expected,
        Func<IVirtualisationProvider, CancellationToken, ValueTask<ProviderOperation<ProviderStateObservation>>> operation,
        CancellationToken cancellationToken)
    {
        var vmResult = await GetVMAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (vmResult.Value is not { } vm) return ProviderOperation<ProviderStateObservation>.Failed(vmResult.ErrorCode ?? "VMNotFound", vmResult.Message ?? "VM is unavailable.");
        var provider = _providers.Find(vm.ProviderID);
        if (provider is null) return ProviderOperation<ProviderStateObservation>.Failed("ProviderUnavailable", "The VM's provider is not registered.");
        var observation = await provider.GetStateAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (observation.State == expected) return ProviderOperation<ProviderStateObservation>.Completed(observation);
        var actionResult = await operation(provider, cancellationToken).ConfigureAwait(false);
        if (actionResult.State != ProviderOperationState.Completed || actionResult.Value is null) return actionResult;
        var verified = await provider.GetStateAsync(vmID, cancellationToken).ConfigureAwait(false);
        if (verified.State != expected)
            return ProviderOperation<ProviderStateObservation>.Pending(JobId.New(), $"Provider accepted the request but reports state '{verified.ProviderState ?? verified.State.ToString()}'; expected '{expected}'.");
        var current = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _store.WriteAsync(current with
        {
            VirtualMachines = current.VirtualMachines.Select(item => item.VMID == vmID ? item with { LifecycleState = verified.State, Revision = item.Revision + 1, UpdatedAt = _timeProvider.GetUtcNow() } : item).ToImmutableArray()
        }, cancellationToken).ConfigureAwait(false);
        Publish("VMStateChanged", provider.ProviderID, vmID, null, ("state", verified.State.ToString()));
        return ProviderOperation<ProviderStateObservation>.Completed(verified);
    }

    private async ValueTask<IVirtualisationProvider?> FindProviderForVMAsync(VirtualMachineId vmID, CancellationToken cancellationToken)
    {
        var result = await GetVMAsync(vmID, cancellationToken).ConfigureAwait(false);
        return result.Value is { } vm ? _providers.Find(vm.ProviderID) : null;
    }

    private IVirtualisationProvider? ResolveProvider(ProviderId? requested, MiniComputerCatalogSnapshot snapshot) =>
        requested is { } id
            ? _providers.Find(id)
            : (snapshot.DefaultProviderID is { } selected ? _providers.Find(selected) : null) ?? _providers.DefaultProvider;

    private void Publish(string eventType, ProviderId? providerID, VirtualMachineId? vmID, SnapshotId? snapshotID, params (string Key, string Value)[] data) =>
        EventPublished?.Invoke(this, new MiniComputerEvent(eventType, providerID, vmID, snapshotID, _timeProvider.GetUtcNow(), data.ToImmutableDictionary(pair => pair.Key, pair => pair.Value)));

    private static string? Validate(VirtualMachineConfiguration? configuration)
    {
        if (configuration is null) return "VM configuration is required.";
        if (string.IsNullOrWhiteSpace(configuration.Name) || configuration.Name.Length > 128) return "Name must contain between 1 and 128 characters.";
        if (configuration.CpuCount is < 1 or > 1024) return "CPU count must be between 1 and 1024.";
        if (configuration.MemoryMiB is < 128 or > 1_048_576) return "Memory must be between 128 MiB and 1 TiB.";
        if (configuration.DiskSizeBytes < 1_073_741_824) return "The initial virtual disk must be at least 1 GiB.";
        if (string.IsNullOrWhiteSpace(configuration.Architecture)) return "Guest architecture is required.";
        if (configuration.Firmware is not ("bios" or "uefi")) return "Firmware must be explicitly set to bios or uefi.";
        if (configuration.InstallationMediaPath is { } path && !Path.IsPathFullyQualified(path)) return "Installation media path must be absolute.";
        if (configuration.NetworkMode is not ("nat" or "bridged" or "host-only" or "internal" or "nat-network" or "disconnected")) return "Network mode is not recognised.";
        return null;
    }

    private static string CreatePageToken(int offset) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"mini-computer-v1:{offset}"));
    private static int ParsePageToken(string? token)
    {
        if (token is null) return 0;
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return value.StartsWith("mini-computer-v1:", StringComparison.Ordinal) && int.TryParse(value.AsSpan(16), out var offset) && offset >= 0 ? offset : -1;
        }
        catch (FormatException) { return -1; }
    }
}
