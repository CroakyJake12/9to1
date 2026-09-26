namespace HavenOS.Apps.MiniComputer;

public enum GracefulShutdownMode { Acpi, GuestTools }

public sealed record ImpactVerification(bool Approved, string? VerificationID, string? Summary)
{
    public static ImpactVerification NotRequired { get; } = new(true, null, null);
}

/// <summary>Provider boundary; implementations must return observations from the real hypervisor.</summary>
public interface IVirtualisationProvider
{
    ProviderId ProviderID { get; }
    ValueTask<ProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken);
    ValueTask<ProviderCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken);
    ValueTask<VirtualMachinePage> ListVirtualMachinesAsync(string? continuationToken, int pageSize, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<VirtualMachine>> CreateVirtualMachineAsync(VirtualMachineId vmID, VirtualMachineConfiguration configuration, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<VirtualMachine>> ImportVirtualMachineAsync(string source, VirtualMachineConfiguration? configuration, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<VirtualMachine>> CloneVirtualMachineAsync(VirtualMachineId sourceVMID, SnapshotId? snapshotID, string name, bool linked, CancellationToken cancellationToken);
    ValueTask<ProviderStateObservation> GetStateAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> StartAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> PauseAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> ResumeAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> SaveStateAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> ShutdownAsync(VirtualMachineId vmID, GracefulShutdownMode mode, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<ProviderStateObservation>> PowerOffAsync(VirtualMachineId vmID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<Snapshot>> CreateSnapshotAsync(VirtualMachineId vmID, string name, string? description, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<Snapshot>> RestoreSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<bool>> DeleteSnapshotAsync(SnapshotId snapshotID, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<VirtualDisk>> CreateDiskAsync(VirtualDiskId diskID, string path, long sizeBytes, VirtualDiskFormat format, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<VirtualDisk>> ResizeDiskAsync(VirtualDiskId diskID, long newSizeBytes, CancellationToken cancellationToken);
    ValueTask<ProviderOperation<bool>> DeleteVirtualMachineAsync(VirtualMachineId vmID, bool deleteDisks, CancellationToken cancellationToken);
}

public interface IHighRiskActionAuthorizer
{
    ValueTask<ProviderOperation<ImpactVerification>> RequestAuthorizationAsync(
        string action,
        VirtualMachineId? vmID,
        IReadOnlyCollection<string> affectedResources,
        CancellationToken cancellationToken);
}

public sealed class DenyWithoutHomeAuthorization : IHighRiskActionAuthorizer
{
    public ValueTask<ProviderOperation<ImpactVerification>> RequestAuthorizationAsync(
        string action,
        VirtualMachineId? vmID,
        IReadOnlyCollection<string> affectedResources,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(ProviderOperation<ImpactVerification>.Failed(
            "PermissionBrokerUnavailable",
            "This operation is pending approval because the Home permission broker is not connected."));
}

public interface IVirtualisationProviderRegistry
{
    IReadOnlyCollection<IVirtualisationProvider> Providers { get; }
    IVirtualisationProvider? Find(ProviderId providerID);
    IVirtualisationProvider? DefaultProvider { get; }
}

public sealed class VirtualisationProviderRegistry : IVirtualisationProviderRegistry
{
    private readonly Dictionary<ProviderId, IVirtualisationProvider> _providers;
    private readonly ProviderId _defaultProviderID;

    public VirtualisationProviderRegistry(IEnumerable<IVirtualisationProvider> providers, ProviderId defaultProviderID)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToDictionary(provider => provider.ProviderID);
        if (_providers.Count == 0) throw new ArgumentException("At least one provider implementation must be registered.", nameof(providers));
        _defaultProviderID = defaultProviderID;
    }

    public IReadOnlyCollection<IVirtualisationProvider> Providers => _providers.Values;
    public IVirtualisationProvider? Find(ProviderId providerID) => _providers.GetValueOrDefault(providerID);
    public IVirtualisationProvider? DefaultProvider => Find(_defaultProviderID);
}

public enum MiniComputerRiskLevel { Low, Elevated, High, Critical }

public sealed record MiniComputerActionDefinition(
    string Name,
    string ArgumentsSchema,
    string ResultSchema,
    string RequiredPermissions,
    MiniComputerRiskLevel Risk,
    bool Reversible,
    bool HasExternalSideEffects,
    string AffectedObjectScope);

public static class MiniComputerApiCatalog
{
    private static MiniComputerActionDefinition Read(string name, string args, string result, string scope) => new(name, args, result, "MiniComputer.Read", MiniComputerRiskLevel.Low, true, false, scope);
    private static MiniComputerActionDefinition Write(string name, string args, string result, string scope, bool reversible = true, MiniComputerRiskLevel risk = MiniComputerRiskLevel.Elevated) => new(name, args, result, "MiniComputer.Write", risk, reversible, true, scope);

    public static IReadOnlyList<MiniComputerActionDefinition> Actions { get; } =
    [
        Read("ListProviders", "options?", "ProviderPage", "providers"),
        Read("GetProviderCapabilities", "providerID,hostContext?", "ProviderCapabilities", "provider"),
        Write("SetDefaultProvider", "providerID", "ProviderDescriptor", "provider preferences"),
        Write("ConnectProvider", "providerConfig", "ProviderDescriptor", "provider configuration", false, MiniComputerRiskLevel.High),
        Write("CreateVM", "config", "VirtualMachine", "new VM and allocated resources"),
        Write("ImportVM", "source,config?", "Job<ImportPreview,VirtualMachine>", "imported VM and resources"),
        Write("CloneVM", "vmID,config", "Job<VirtualMachine>", "source VM, snapshot and clone resources"),
        Read("GetVM", "vmID", "VirtualMachine", "VM"),
        Read("ListVMs", "options?", "VirtualMachinePage", "VMs"),
        Write("UpdateVM", "vmID,patch,options?", "VirtualMachine", "VM configuration"),
        Write("Start", "vmID,options?", "ProviderStateObservation", "VM"),
        Write("Pause", "vmID", "ProviderStateObservation", "VM"),
        Write("Resume", "vmID", "ProviderStateObservation", "VM"),
        Write("Shutdown", "vmID,options?", "ProviderStateObservation", "VM"),
        Write("Restart", "vmID,options?", "ProviderStateObservation", "VM"),
        Write("PowerOff", "vmID,verification", "ProviderStateObservation", "VM and unsaved guest state", false, MiniComputerRiskLevel.High),
        Write("SaveState", "vmID", "ProviderStateObservation", "VM and state file"),
        Write("OpenConsole", "vmID,options?", "ViewerSession", "viewer session"),
        Write("CreateDisk", "config", "VirtualDisk", "disk storage"),
        Write("AttachDisk", "vmID,diskID,attachment", "VirtualMachine", "VM and disk attachment"),
        Write("ResizeDisk", "diskID,newSize,options?", "VirtualDisk", "disk and attached VMs", false, MiniComputerRiskLevel.High),
        Write("AttachMedia", "vmID,mediaID,attachment?", "VirtualMachine", "VM and media"),
        Write("CreateSnapshot", "vmID,config?", "Snapshot", "VM snapshot graph"),
        Write("RestoreSnapshot", "snapshotID,options?", "ProviderStateObservation", "VM disks, memory and current unsaved state", false, MiniComputerRiskLevel.High),
        Write("DeleteSnapshot", "snapshotID,options?", "SnapshotDependencyReport", "snapshot graph and linked disks", false, MiniComputerRiskLevel.High),
        Write("ConfigureNetwork", "vmID,networkConfig", "VirtualNetworkAdapter", "VM and host/guest network boundary", false, MiniComputerRiskLevel.High),
        Write("AttachDevice", "vmID,deviceID,options?", "DeviceAttachment", "host device and VM", false, MiniComputerRiskLevel.Critical),
        Write("ConfigureSharedFolder", "vmID,config", "SharedFolder", "host files and VM", false, MiniComputerRiskLevel.High),
        Write("CreateTemplate", "sourceVMOrConfig,config?", "Template", "template and source"),
        Write("ExportVM", "vmID,config", "Job<ExportResult>", "VM and export resources"),
        Read("ListISOCatalogue", "query?", "MediaCataloguePage", "registered catalogue entries"),
        Read("GetISOEntry", "entryID", "MediaCatalogueEntry", "catalogue entry"),
        Write("AcquireISO", "entryID,options?", "Job<MediaAcquisition>", "installation media and download destination", false, MiniComputerRiskLevel.Elevated),
        Write("RegisterMedia", "fileOrUri,metadata?", "InstallationMedia", "media registration"),
        Write("VerifyMedia", "mediaID,options?", "MediaIntegrityResult", "installation media"),
        Read("GetMetrics", "vmID,options?", "VirtualMachineMetrics", "VM metrics"),
        Read("GetVirtualisationStatus", "options?", "VirtualisationStatus", "host and provider status")
    ];
}
