using System.Collections.Immutable;

namespace HavenOS.Apps.MiniComputer;

public readonly record struct ProviderId(Guid Value)
{
    public static ProviderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct VirtualMachineId(Guid Value)
{
    public static VirtualMachineId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct SnapshotId(Guid Value)
{
    public static SnapshotId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct VirtualDiskId(Guid Value)
{
    public static VirtualDiskId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct VirtualNetworkAdapterId(Guid Value)
{
    public static VirtualNetworkAdapterId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct MediaId(Guid Value)
{
    public static MediaId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct TemplateId(Guid Value)
{
    public static TemplateId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("D");
}

public enum VirtualMachineLifecycleState
{
    Unknown = 0,
    PoweredOff = 1,
    Starting = 2,
    Running = 3,
    Pausing = 4,
    Paused = 5,
    SavingState = 6,
    Saved = 7,
    Resuming = 8,
    ShuttingDown = 9,
    Stopping = 10,
    Crashed = 11,
    Disconnected = 12,
    ProviderUnavailable = 13
}

public enum VirtualisationFeature
{
    CreateMachine,
    ImportExport,
    Snapshots,
    LiveSnapshots,
    SavedState,
    FullClone,
    LinkedClone,
    DiskCreate,
    DiskResize,
    DiskConvert,
    Uefi,
    SecureBoot,
    VirtualTpm,
    NatNetworking,
    BridgedNetworking,
    HostOnlyNetworking,
    InternalNetworking,
    PortForwarding,
    UsbPassthrough,
    SharedFolders,
    Clipboard,
    DragAndDrop,
    GraphicsAcceleration,
    MultiMonitor,
    Audio,
    SerialConsole,
    DynamicResolution,
    GuestIntegration
}

public enum CapabilityState
{
    Supported,
    Unsupported,
    Unavailable
}

public sealed record CapabilityStatus(CapabilityState State, string? Reason = null, string? Remediation = null);

public sealed record ProviderCapabilities(
    ImmutableDictionary<VirtualisationFeature, CapabilityStatus> Features,
    string HostArchitecture,
    ImmutableArray<string> GuestArchitectures,
    bool HardwareAccelerationAvailable,
    string? HardwareAccelerationReason)
{
    public CapabilityStatus Get(VirtualisationFeature feature) =>
        Features.TryGetValue(feature, out var status)
            ? status
            : new(CapabilityState.Unavailable, "The provider has not reported this capability.");
}

public sealed record ProviderDescriptor(
    ProviderId ProviderID,
    string Name,
    string Type,
    string? Version,
    bool IsBuiltIn,
    bool IsDefault,
    bool IsConnected,
    string? UnavailableReason,
    ProviderCapabilities Capabilities,
    int Revision = 1);

public sealed record VirtualMachineConfiguration(
    string Name,
    string GuestFamily,
    string? GuestVersion,
    string Architecture,
    int CpuCount,
    int MemoryMiB,
    long DiskSizeBytes,
    string Firmware,
    string? InstallationMediaPath = null,
    string NetworkMode = "nat");

public sealed record VirtualMachine(
    VirtualMachineId VMID,
    ProviderId ProviderID,
    string ProviderMachineID,
    string Name,
    string GuestFamily,
    string? GuestVersion,
    string Architecture,
    int CpuCount,
    int MemoryMiB,
    string Firmware,
    VirtualMachineLifecycleState LifecycleState,
    int ConfigurationVersion,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ImmutableArray<VirtualDiskId> DiskIDs,
    ImmutableArray<VirtualNetworkAdapterId> NetworkAdapterIDs,
    ImmutableArray<SnapshotId> SnapshotIDs,
    string? EnvironmentID = null,
    bool IsEphemeral = false);

public sealed record Snapshot(
    SnapshotId SnapshotID,
    VirtualMachineId VMID,
    SnapshotId? ParentSnapshotID,
    string ProviderSnapshotID,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    bool IncludesMemoryState,
    ImmutableArray<VirtualDiskId> CapturedDisks,
    ImmutableArray<string> ExcludedExternalResources,
    int Revision = 1);

public enum VirtualDiskFormat { Unknown, Vdi, Vhd, Vhdx, Vmdk, Qcow2, Raw }
public enum DiskBackingKind { Standalone, Differencing, LinkedClone, External }

public sealed record VirtualDisk(
    VirtualDiskId DiskID,
    ProviderId ProviderID,
    string ProviderDiskID,
    VirtualDiskFormat Format,
    DiskBackingKind BackingKind,
    long LogicalSizeBytes,
    long? PhysicalSizeBytes,
    VirtualDiskId? ParentDiskID,
    ImmutableArray<VirtualMachineId> AttachedVMIDs,
    string? FilesResourceID,
    bool IsDerived,
    int Revision = 1);

public enum VirtualNetworkMode { Nat, Bridged, HostOnly, Internal, NatNetwork, Disconnected }

public sealed record PortForwardRule(string Name, string Protocol, string HostAddress, ushort HostPort, string GuestAddress, ushort GuestPort);

public sealed record VirtualNetworkAdapter(
    VirtualNetworkAdapterId AdapterID,
    VirtualMachineId VMID,
    VirtualNetworkMode Mode,
    string? NetworkID,
    string MacAddress,
    bool CableConnected,
    ImmutableArray<PortForwardRule> PortForwardRules,
    int Revision = 1);

public enum MediaAcquisitionPolicy { OfficialDirect, MirrorPermitted, UserSupplied, ProviderFlow }
public enum MediaIntegrityState { Unknown, NotIndependentlyVerified, Verifying, Verified, Failed }

public sealed record InstallationMedia(
    MediaId MediaID,
    string Publisher,
    string Product,
    string Version,
    string Architecture,
    Uri? SourceUri,
    MediaAcquisitionPolicy AcquisitionPolicy,
    MediaIntegrityState IntegrityState,
    string? ExpectedSha256,
    string? ActualSha256,
    string? FilesResourceID,
    ImmutableArray<VirtualMachineId> ReferencingVMIDs,
    int Revision = 1);

public sealed record VirtualMachinePage(ImmutableArray<VirtualMachine> Items, string? ContinuationToken);

public sealed record ProviderStateObservation(
    VirtualMachineLifecycleState State,
    DateTimeOffset ObservedAt,
    string? ProviderState,
    string? Diagnostic);

public enum ProviderOperationState { Completed, Pending, Failed }

public sealed record ProviderOperation<T>(
    ProviderOperationState State,
    T? Value,
    JobId? JobID,
    string? ErrorCode,
    string? Message,
    bool RollbackVerified = false)
{
    public static ProviderOperation<T> Completed(T value) => new(ProviderOperationState.Completed, value, null, null, null);
    public static ProviderOperation<T> Pending(JobId jobID, string message) => new(ProviderOperationState.Pending, default, jobID, null, message);
    public static ProviderOperation<T> Failed(string code, string message) => new(ProviderOperationState.Failed, default, null, code, message);
}

public sealed record MiniComputerEvent(
    string EventType,
    ProviderId? ProviderID,
    VirtualMachineId? VMID,
    SnapshotId? SnapshotID,
    DateTimeOffset Timestamp,
    ImmutableDictionary<string, string> Data);

public interface IMiniComputerEventSource
{
    event EventHandler<MiniComputerEvent>? EventPublished;
}
