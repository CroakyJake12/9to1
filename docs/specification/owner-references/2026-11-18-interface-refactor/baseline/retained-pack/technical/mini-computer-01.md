# Mini Computer — VM and remote desktop control — Mini Computer; Product role and shared virtualisation engine (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1818 · v2 lines 10234–10521

Mini Computer
Product role and shared virtualisation engine
9-1 Mini Computer is the native CUI virtual-computer and virtual-machine management app and the shared virtualisation engine used by Dev, Terminal and other authorised 9to1 surfaces. It supports creation, installation, import, execution, inspection, configuration, snapshots, cloning, networking, device integration, templates, developer workflows and recovery of supported virtual machines.
Mini Computer owns the canonical VirtualMachine/VirtualisationProvider model and VM lifecycle. Dev owns code/build/debug authoring, Terminal owns terminal sessions, Files owns user-addressable files/media identity and Home owns protected-action permission brokering.
A VM remains a real provider-backed virtual machine. Mini Computer MUST NOT replace mature hypervisor functionality with a simplified simulation merely to obtain a uniform UI.
Built-in default provider: 9to1 VirtualBox fork
The built-in and default provider is a maintained 9to1 fork of the VirtualBox Platform Package source. Mini Computer MUST include this provider so a user can create and run supported VMs without first installing or connecting a third-party hypervisor.
The implementation MUST fork and maintain the latest appropriate VirtualBox source in accordance with the global donor/upstream contract and the applicable open-source licence/third-party licence obligations. The fork remains an auditable upstream-derived backend rather than an opaque binary dependency with no source provenance.
VirtualBox's mature virtual-machine creation, configuration, storage, networking, snapshot, cloning, import/export, console and management behaviours are mandatory donor-parity inputs where supported by the target architecture/platform.
VirtualBox licensing and excluded separately licensed components
The 9to1 fork covers only VirtualBox source/components that CAKE is legally permitted to modify and distribute under their applicable licences. Separately licensed proprietary components such as the Oracle Extension Pack MUST NOT be copied, bundled or represented as part of the fork unless CAKE has obtained the required rights.
If a useful donor capability exists only in a separately licensed component, Mini Computer must either provide a legally independent 9to1-native replacement, support a separately user-installed/connected compatible component where lawful, or expose the capability as unavailable with a documented licensing/platform reason.
The implementation architecture SHOULD keep the fork/provider boundary sufficiently clear for licence compliance, update/rebase management and optional alternative-provider support.
Provider abstraction and optional connected providers
Mini Computer exposes a canonical VirtualisationProvider interface. The built-in 9to1 VirtualBox fork is selected by default, while users may connect/install additional compatible providers such as QEMU, KVM, Windows hypervisor integrations or extension-contributed providers when supported.
Provider has stable ProviderID, type/vendor, version, connection/install state, supported hosts/guest architectures, acceleration capabilities, firmware/security capabilities, device/network/storage features, import/export formats, debug/serial support and Revision.
Connecting another provider does not change the canonical VM semantics automatically. Each VM records ProviderID, and provider migration/conversion is an explicit operation with a preview of unsupported/lossy features.
Provider-specific advanced controls remain accessible through typed provider configuration without leaking arbitrary implementation flags into the canonical cross-provider model.
Provider capability matrix
Every provider reports exact capability state rather than relying on feature hiding alone. Capabilities include supported guest architectures, hardware acceleration, nested virtualisation, snapshots, live snapshots, saved state, cloning, disk formats/controllers, BIOS/UEFI, Secure Boot, TPM, USB/device passthrough, shared folders, clipboard/drag-drop, virtual networking, graphics acceleration, multi-monitor, audio, serial/debug interfaces and live configuration changes.
A setting unsupported by the active provider/platform is marked Unsupported/Unavailable with a reason and remediation/alternative where applicable. Mini Computer MUST NOT accept a setting and silently ignore it.
Capabilities may depend on host architecture and current host configuration, so provider/version support and effective host support are distinct states.
Canonical VirtualMachine model
VirtualMachine has stable VMID, name, ProviderID, EnvironmentID, guest OS metadata, architecture, machine/chipset type, firmware/security configuration, CPU configuration, memory configuration, storage controllers/disks/media, network adapters, display/GPU, audio, USB/devices, integration policy, shared folders, clipboard/drag-drop policy, snapshot graph, template/source provenance, lifecycle state, Files/resource references and Revision.
Renaming, moving in UI or changing provider display labels MUST NOT alter VMID. Provider-internal identifiers are retained as mappings rather than becoming the only canonical identity.
VM configuration is versioned and migratable. Unsupported/unknown future configuration is preserved/rejected explicitly rather than silently dropped.
VM lifecycle states
Canonical lifecycle states include at least PoweredOff, Starting, Running, Pausing, Paused, SavingState, Saved, Resuming, ShuttingDown, Stopping, Crashed, Disconnected/ViewerUnavailable and ProviderUnavailable where distinct.
Viewer state and VM state are separate. Closing or losing a viewer MUST NOT cause the UI to report PoweredOff if the VM remains running.
Lifecycle transitions are driven by verified provider state. Mini Computer MUST NOT report Start/Shutdown/Save successful until the provider confirms the resulting state or the operation explicitly remains Pending.
New VM wizard
Create VM provides a guided workflow for guest OS family/version, architecture, installation media, CPU, RAM, storage, firmware/security, networking and optional unattended installation, with Advanced settings available before creation.
Defaults are recommendations based on detected/selected guest and current host/provider capabilities. Users can review/change them before the VM is created.
Creating a VM is atomic at the user-visible level where possible; partial provider/storage creation is either rolled back or returned with precise created resources and recovery actions.
Guest/ISO detection
When installation media is selected, Mini Computer attempts to detect OS family, edition/version, architecture and boot compatibility using trustworthy metadata/content inspection available without executing the image.
Detected values are recommendations and are shown with confidence/source state where useful. Unknown media remains Unknown rather than being guessed.
Detection may propose suitable CPU, memory, disk, firmware, TPM/Secure Boot and guest-integration settings while preserving user control.
Curated ISO Browser
Mini Computer includes a first-class ISO Browser/Discover OS experience for acquiring trusted operating-system installation media. It is a curated publisher catalogue, not an unrestricted Internet ISO search engine.
Catalogue entries may include operating-system family/distribution, publisher, edition/channel, version, architecture, desktop/server variant, approximate size, source/provider, acquisition policy, integrity metadata availability, licence/redistribution policy and branding/trademark usage metadata where relevant.
Representative trusted publisher sources may include Microsoft, Canonical/Ubuntu, Debian, Fedora, Arch, openSUSE, FreeBSD, 9to1 OS and other audited providers. Addition of a catalogue source requires explicit provider/provenance configuration.
ISO acquisition policies
Every ISO catalogue entry declares one of the canonical acquisition policies or an equivalent explicit policy:
OfficialDirect — the user's Mini Computer downloads from the publisher's authorised source and 9to1 does not rehost the file.
MirrorPermitted — CAKE may mirror/cache the exact permitted unmodified image only when redistribution rights/policy expressly allow it and all required licence/notices/provenance are preserved.
UserSupplied — Mini Computer can identify/configure the guest but the user must select media they already lawfully possess.
ProviderFlow — acquisition requires a publisher-specific authenticated/licensed/download flow that Mini Computer opens or integrates without bypassing its conditions.
An OS being open source does not by itself authorise ISO redistribution or logo/trademark use. Acquisition/mirroring and branding rights are evaluated separately.
ISO download, integrity and provenance
Downloaded/registerable installation media uses stable ISOImageID/MediaID and records publisher/source, product/version/architecture, acquisition provider/URL reference, Files identity, cryptographic digest/signature state, download timestamp, redistribution/acquisition policy, licence/provenance metadata and Revision.
When the publisher provides authoritative checksum/signature information, Mini Computer verifies it before labelling the download Verified. A mismatch blocks automatic VM creation/use until the user deliberately handles the failed integrity state.
If trustworthy verification data is unavailable, the UI states Integrity not independently verified rather than inventing trust.
One immutable ISO can be referenced by multiple VMs without creating duplicate copies unnecessarily.
ISO Browser user flow
A supported catalogue flow can perform Select OS → choose version/architecture → acquire from authorised source → verify integrity → register media → create/configure VM → mount media → boot installer.
The user can inspect the source/publisher and acquisition policy before download. Any required login/product/licence flow remains the publisher's official flow.
Open local ISO/import media remains available for images not represented in the curated catalogue.
Media library and cleanup
Mini Computer exposes reusable installation media through a media library backed by Files/provider storage. Media usage shows which VMs currently reference/mount the item.
Removing media does not delete installed VM disks. If the medium is still mounted or required for a configured boot workflow, deletion/removal shows the affected VMs and requires appropriate confirmation.
Derived download caches can be cleaned independently from canonical retained media where provenance/re-download permits.
Unattended installation
Where the guest/provider supports it, users may opt into unattended OS installation. Configuration can include hostname, locale, time zone, user/account setup, product/licence information where required, disk install choices and guest integration installation.
Passwords/product keys and equivalent sensitive values use secure credential/secret handling rather than being written into logs, screenshots, AI context or plain project metadata.
Manual installation is always available and remains the fallback when unattended flow is unsupported or fails.
Interactive VM viewer
Running VMs can open in an interactive Mini Computer viewer supporting keyboard, pointer capture/release, scaling, fullscreen, aspect handling, dynamic resolution when guest integration supports it and multi-monitor when the provider supports it.
The viewer may be embedded or popped out to a dedicated window while retaining the same VMID/session state.
Viewer disconnect/reconnect is separate from VM lifecycle and must not power off or duplicate the VM unless explicitly requested.
Power and state controls
Mini Computer distinguishes Start, Pause, Resume, Save State, ACPI/Guest Shutdown, Restart/Reset where supported and Power Off/Force Stop. The UI/API MUST NOT label destructive power-off as graceful shutdown.
Potential data-loss actions such as Power Off while a guest is running receive risk-appropriate confirmation unless a valid trust/policy explicitly covers them.
Power actions are idempotent or return AlreadyInState/InvalidState semantics rather than creating contradictory provider operations.
Snapshots
Snapshots are first-class canonical objects with stable SnapshotID, VMID, parent SnapshotID, name, description, creation time, provider reference, disk/memory-state coverage, guest state, size/retention metadata and Revision.
Users can create, inspect, restore, rename/descriptively edit where supported and delete/consolidate snapshots. Snapshot hierarchy is shown as a tree/graph rather than a flat list.
Restore explicitly states which disk/memory/config state is affected and what current unsaved state will be lost. External/shared resources not captured by the snapshot are clearly excluded.
Deleting/consolidating a snapshot MUST NOT silently delete a dependent branch/linked clone.
Cloning and linked clones
Mini Computer supports full clone and provider-supported linked clone from current state or a selected snapshot. Clone receives a new stable VMID and clear provenance.
Linked-clone dependency on base disks/snapshots is visible. Deleting/moving a required base is blocked or performs a deliberate consolidation/migration first.
Clone options include identity/network regeneration where needed to avoid duplicate machine identifiers.
Virtual disks and block storage
Mini Computer supports provider-compatible disk formats such as VDI plus supported VHD/VHDX, VMDK, QCOW2 and raw images through the active provider/conversion layer.
Disk operations include create, attach/detach, resize where safe, compact where supported, clone, convert/import/export, inspect and delete.
VirtualDisk has stable DiskID, provider reference, format/type, logical/physical size, backing/base relationship, encryption state where supported, Files/storage-provider identity, attached VM/controller references and Revision.
Destructive resize/shrink/delete/raw-device operations require explicit validation and risk-appropriate verification.
Storage controllers and boot order
Mini Computer exposes provider-supported controllers such as NVMe, SATA, SCSI and legacy IDE/other controllers when required. Optical/floppy media appears only where supported/meaningful.
Normal VM creation configures conventional controller topology automatically; Advanced settings expose the real attachment slots/controllers.
Boot order and one-time boot choices are explicit. Mounting a new ISO MUST NOT silently rewrite persistent boot policy unless requested.
CPU and memory configuration
VM settings include virtual CPU count/topology where supported, RAM and provider-specific advanced execution/CPU capabilities such as execution cap or nested virtualisation.
Mini Computer warns when requested allocation is likely to leave insufficient host resources or exceeds provider/host capabilities. It must not silently overcommit resources while presenting them as guaranteed.
Settings that require VM shutdown are marked Requires shutdown; supported live changes show real applied/pending state.
Firmware, Secure Boot and TPM
Mini Computer supports BIOS/legacy firmware and UEFI according to provider capabilities, plus Secure Boot and virtual TPM where supported.
Guest templates/detection can recommend required firmware/security configuration, especially for modern operating systems, while exposing the actual setting.
TPM/Secure Boot backing state is included in snapshot/export/migration semantics only where the provider guarantees it; otherwise exclusions are explicit.
Networking
VM networking supports provider-capable modes such as NAT, bridged, host-only, internal/private, NAT network/custom virtual network and disconnected state.
Each VirtualNetworkAdapter has stable AdapterID, mode/network binding, virtual NIC type, MAC configuration, cable/link state, port-forward rules and Revision.
NAT port forwarding exposes host address/port, guest address/port and protocol and validates collisions before applying.
Virtual network manager
Mini Computer includes a network-management surface for provider-defined NAT networks, host-only networks, internal/private networks, DHCP ranges, forwarding and adapters.
Deleting/modifying a network lists dependent VMs/adapters and blocks or migrates them explicitly rather than leaving hidden broken references.
Bridged network/device selection is capability- and permission-aware and does not silently expose a VM to external networks.
USB and device passthrough
Where provider/platform support exists, users may attach/detach USB devices and configure explicit passthrough/filter policies. Additional PCI/GPU/device passthrough is exposed only through providers with a supported implementation.
Attaching a physical device that becomes unavailable to the host is communicated before execution. Auto-attach rules identify exactly which VM/device criteria they affect.
Device passthrough is treated as a high-trust boundary; Mini Computer does not grant broad host-device access to untrusted guests by default.
Display and graphics
Display configuration can expose virtual display controller, VRAM, monitor count, scaling, 2D/3D acceleration and provider-supported graphics features.
Ordinary virtual graphics acceleration is distinct from physical GPU passthrough. The latter uses its own provider/security/capability requirements.
Unsupported graphics settings remain visibly unavailable rather than being written into configuration and ignored.
Audio and microphone
Mini Computer exposes guest audio output/input routing where supported. Microphone input follows host privacy/device permission rules and MUST NOT silently activate because the VM starts.
Per-VM audio policy can disable input/output entirely for untrusted/test guests.
Clipboard and drag/drop trust policy
Shared clipboard and drag/drop support provider/guest-integration capabilities and explicit policies such as Disabled, HostToGuest, GuestToHost and Bidirectional.
These capabilities are disabled or conservatively configured by default according to security policy for untrusted guests. Guest integration installation alone MUST NOT silently enable bidirectional host data access.
Clipboard/drag-drop state is visible while the VM is running.
Shared folders
Users can expose host/Files-backed folders to guests through stable SharedFolder configurations with host/File identity, guest name/mount path, read-only/read-write, auto-mount and persistence policy.
Writable host sharing is a security boundary because guest software can modify host/user data. The UI/AI verification must make writable scope clear.
Deleting/moving a shared host resource resolves through Files identity where possible or leaves an explicit BrokenBinding state.
Guest integrations
Mini Computer detects and, where lawfully distributable/supported, can install appropriate guest integration tooling for dynamic resolution, clipboard, drag/drop, shared folders, guest status/shutdown and file transfer.
Guest integration version/health is visible. The VM remains operable when integrations are unavailable.
Mini Computer MUST NOT bundle separately licensed donor guest components without the necessary rights.
Import and export
Mini Computer supports import/export of mature provider formats such as OVF/OVA and supported virtual-disk formats through the active provider/conversion layer.
Import previews guest OS metadata, CPU/RAM, disks, networks, device requirements, unsupported features and resulting provider mapping before creation.
Export records exact VM revision/snapshot/configuration scope and warns about excluded secrets, external disks/shared folders/device state.
Templates and reusable machine profiles
Users can save reusable VM templates/profiles for common environments such as Windows testing, Linux development, server labs or 9to1-OS development.
Template has stable TemplateID, guest/provider requirements, hardware defaults, media/acquisition references, optional bootstrap/unattended settings and Revision.
Instantiating a template creates a new VM identity. The template itself is not accidentally run/mutated as an ordinary machine unless explicitly converted.
First-party guest templates
Mini Computer may ship configuration templates for common operating systems without bundling proprietary installation images unless distribution rights permit it.
A template may state ISO required and route the user into the ISO Browser/provider flow rather than implying the operating system is included.
Templates are updated independently from the hypervisor and preserve backward compatibility for already-created VMs.
Files and storage architecture
User-addressable Mini Computer resources are organised in Files through stable identities, conceptually including Machines, Disks, ISOs/Media, Templates and Exports. Exact physical layout may vary by storage provider but remains user-inspectable where appropriate.
Very large mutable virtual disks/snapshot chains may use a block/storage provider optimised for VM workloads rather than ordinary sync semantics. Provider-internal block state is not misrepresented as a normal small Files document.
Canonical VM/resource metadata records the mapping between user-visible Files resources and provider-internal storage so recovery/migration can distinguish source/configuration from derived/provider state.
Resource references and deduplication
Immutable installation media can be shared by reference across VMs. Linked/base virtual disks preserve explicit dependency identity rather than being duplicated or lost through path renames.
Moving/renaming a Files-backed ISO/export/template through supported Files/Mini Computer actions preserves references by stable ID.
Provider caches, temporary differencing layers and ephemeral development disks are marked derived/ephemeral and can be cleaned without deleting canonical user resources unless explicitly selected.
Terminal integration
Every supported VM can expose Open in Terminal for guest shell/SSH where configured, serial console, debug console or provider-supported command channel. This uses the shared Terminal engine.
Terminal creates/attaches an EnvironmentID tied to VMID/provider/guest channel and visibly identifies the VM so commands cannot be mistaken for host commands.
Closing Terminal does not power off the VM; VM lifecycle remains owned by Mini Computer.
Dev integration and Run target
Dev uses Mini Computer as the canonical VM runtime for projects whose RunConfiguration targets a VM. Dev MUST NOT maintain a parallel private hypervisor implementation.
For OS/kernel projects, Run can execute Build → package boot artifact → obtain/start configured Mini Computer VM → attach disk/ISO/EFI/kernel artifact → boot → expose viewer/serial/debug state.
Run configuration references stable VM/template/provider identities and can declare reset/snapshot/discard policy for repeatable development.
Ephemeral development VMs
Dev/CI can request ephemeral VMs derived from templates/snapshots for isolated testing. Policy can specify discard after success, preserve on failure, always preserve or snapshot-before-run.
Ephemeral instances receive unique VMID/runtime identity even when created from the same template and do not mutate the reusable template/base unintentionally.
Cleanup waits until logs/artifacts/checks referencing the ephemeral environment are retained or explicitly discarded.
OS/kernel debugging integration
Mini Computer exposes provider-supported serial ports/consoles, debug stubs, reset/power control, boot logs and low-level debug channels through typed capabilities.
Dev owns source-level debugger UI such as breakpoints, symbols, registers/memory/disassembly mapping where supported; Mini Computer supplies the VM/provider channel and exact machine state.
Debug integration records the source VM/revision/artifact mapping so Dev cannot accidentally attach symbols from an unrelated build.
Headless operation
VMs may run without an open viewer. Closing Mini Computer follows explicit/configured behaviour such as Keep running, Save state or Ask; it does not inherently terminate a VM.
A background/service component owns provider sessions that must survive the UI process. Reopening Mini Computer discovers/reattaches to existing provider VMs instead of creating duplicates.
User logout/system shutdown semantics are platform-specific and exposed clearly when background VM continuation is unavailable.
Provider and host virtualisation status
Mini Computer includes Virtualisation Status showing selected/default provider, provider install/driver/service state, hardware virtualisation availability, acceleration state, nested virtualisation, host architecture and key feature capability status.
Common blockers such as disabled hardware virtualisation, unavailable provider driver/service, architecture mismatch, acceleration conflict or unsupported host configuration are surfaced with actionable diagnostics rather than opaque provider codes.
Where the built-in VirtualBox fork conflicts with another host hypervisor configuration, Mini Computer reports the specific capability degradation/conflict and supported resolution options.
Resource monitoring
Running VM views expose provider-backed CPU, allocated/used memory where available, disk activity, network activity, uptime and relevant guest/provider health metrics.
Host overview can aggregate VM resource consumption. Unknown/unavailable metrics remain explicitly unavailable rather than estimated and displayed as measurements.
Monitoring is virtualised/streamed to avoid high overhead with many running VMs.
AI / Dulche integration
Dulche context can include VMID, ProviderID/version, lifecycle state, guest metadata, hardware configuration, attached media/disks, network state, snapshot tree, integration state, current diagnostics, Dev/Terminal bindings and Revision subject to permissions.
AI requests such as create a Windows test VM, take a snapshot before this kernel test, explain why boot failed or add disk capacity resolve to typed Mini Computer/provider actions.
AI MUST NOT hide consequential operations behind generic wording. Disk deletion/shrink, snapshot restore, raw-device attachment, writable shared-folder exposure, device passthrough, bridged networking and provider installation/configuration receive the applicable verification/risk treatment.
Safety and high-risk operations
High-risk classes include deleting/overwriting virtual disks, destructive snapshot restore/consolidation, raw host-disk access, physical device/GPU passthrough, writable host sharing, privileged provider installation/driver changes, security-device changes and network changes that materially expose the guest/host.
Impact previews identify VM, affected disks/snapshots/devices/files/networks and whether the operation is reversible where determinable.
Mini Computer cannot use opaque desktop automation to click provider UIs when a typed provider/API operation exists.
Mini Computer API
Mini Computer's first-party API catalogue is exhaustive under the global API-parity rule. Minimum canonical entry points include:
9to1.MiniComputer.ListProviders(options?);
9to1.MiniComputer.GetProviderCapabilities(providerID, hostContext?);
9to1.MiniComputer.SetDefaultProvider(providerID);
9to1.MiniComputer.ConnectProvider(providerConfig);
9to1.MiniComputer.CreateVM(config);
9to1.MiniComputer.ImportVM(source, config?);
9to1.MiniComputer.CloneVM(vmID, config);
9to1.MiniComputer.GetVM(vmID);
9to1.MiniComputer.ListVMs(options?);
9to1.MiniComputer.UpdateVM(vmID, patch, options?);
9to1.MiniComputer.Start(vmID, options?);
9to1.MiniComputer.Pause(vmID);
9to1.MiniComputer.Resume(vmID);
9to1.MiniComputer.Shutdown(vmID, options?);
9to1.MiniComputer.Restart(vmID, options?);
9to1.MiniComputer.PowerOff(vmID, verification?);
9to1.MiniComputer.SaveState(vmID);
9to1.MiniComputer.OpenConsole(vmID, options?);
9to1.MiniComputer.CreateDisk(config);
9to1.MiniComputer.AttachDisk(vmID, diskID, attachment);
9to1.MiniComputer.ResizeDisk(diskID, newSize, options?);
9to1.MiniComputer.AttachMedia(vmID, mediaID, attachment?);
9to1.MiniComputer.CreateSnapshot(vmID, config?);
9to1.MiniComputer.RestoreSnapshot(snapshotID, options?);
9to1.MiniComputer.DeleteSnapshot(snapshotID, options?);
9to1.MiniComputer.ConfigureNetwork(vmID, networkConfig);
9to1.MiniComputer.AttachDevice(vmID, deviceID, options?);
9to1.MiniComputer.ConfigureSharedFolder(vmID, config);
9to1.MiniComputer.CreateTemplate(sourceVMOrConfig, config?);
9to1.MiniComputer.ExportVM(vmID, config);
9to1.MiniComputer.ListISOCatalogue(query?);
9to1.MiniComputer.GetISOEntry(entryID);
9to1.MiniComputer.AcquireISO(entryID, options?);
9to1.MiniComputer.RegisterMedia(fileOrUri, metadata?);
9to1.MiniComputer.VerifyMedia(mediaID, options?);
9to1.MiniComputer.GetMetrics(vmID, options?);
9to1.MiniComputer.GetVirtualisationStatus(options?);
Terminal, Dev, Files, credentials and Home permissions remain canonical in their owning namespaces.
Automation and typed events
Mini Computer emits typed events such as VMCreated/Updated/Deleted, VMStateChanged, SnapshotCreated/Restored/Deleted, ProviderConnected/Unavailable, MediaAcquisitionStarted/Completed/Failed, MediaVerificationChanged, DeviceAttached/Detached, NetworkChanged, MetricsUpdated and provider/guest integration events.
Events use stable IDs and avoid embedding secret credentials or unbounded VM console output. Consumers request additional permitted data explicitly.
Extension system
Mini Computer participates in the shared 9to1 extension platform. Extensions may contribute VirtualisationProvider implementations, ISO catalogue/acquisition providers, guest templates/integration providers, device/network/storage adapters, inspectors and diagnostics.
Provider extensions declare host/platform compatibility, required privileges and permissions. Installing a provider UI extension does not automatically grant kernel driver, raw-disk, device or network privileges.
Provider failure is isolated where feasible and does not corrupt unrelated VM canonical metadata.
Accessibility
VM list, create wizard, settings, snapshots, networks, ISO Browser, provider status and power controls are keyboard-operable with screen-reader semantics.
VM lifecycle/provider/security state MUST NOT rely solely on colour or icons. Dangerous Power Off/Delete/Restore/Raw device actions have explicit accessible labels and consequences.
The embedded viewer provides accessible host controls around the guest even when guest OS accessibility itself is outside Mini Computer's control.
Performance and scalability
VM lists, snapshots, ISO/media libraries and monitoring views are virtualised/paged where needed. UI rendering MUST NOT require loading every virtual disk/snapshot into memory.
Provider operations such as import/export, clone, disk conversion, large download, snapshot consolidation and migration run as cancellable/resumable jobs where the provider supports it, with stable JobID/progress/error state.
Mini Computer remains responsive while one or more VMs generate heavy I/O or console output.
Recovery and failure behaviour
Mini Computer distinguishes viewer disconnect, provider service failure, VM crash, host-resource exhaustion, disk/media missing, storage corruption and configuration incompatibility rather than collapsing them into VM failed.
Reopening after UI crash/restart re-discovers provider-managed running/saved VMs by stable mapping and reattaches without duplication where the provider can prove identity.
Structured failures include VMNotFound, ProviderUnavailable, ProviderCapabilityUnavailable, HardwareVirtualisationUnavailable, AccelerationUnavailable, InvalidVMState, DiskNotFound, DiskDependencyBlocked, MediaNotFound, MediaIntegrityFailed, MediaAcquisitionNotPermitted, SnapshotDependencyBlocked, SnapshotRestoreConflict, NetworkConflict, DeviceUnavailable, HostResourceRisk, CredentialUnavailable, ImportUnsupported, ExportFailed, ViewerDisconnected, GuestIntegrationUnavailable, RevisionConflict, PermissionDenied and CapabilityUnavailable plus provider-specific diagnostic codes.
Partial provider/storage operations return succeeded/failed/remaining resources and recovery steps; Mini Computer MUST NOT report rollback/cleanup success unless verified.
Platform capability behaviour
Windows, 9to1-OS and other supported desktop hosts preserve the same canonical VM/provider/media model while capability-negotiating the built-in VirtualBox fork, hardware acceleration, host networking, device passthrough and filesystem/storage behaviour.
Web surfaces may manage/view remote Mini Computer providers/VMs when an authorised remote execution provider exists but MUST NOT pretend the browser itself can provide unrestricted local hardware virtualisation.
Unsupported architecture/provider combinations are explicit release/platform exceptions rather than silent missing UI.
When Home is absent at process start, Mini Computer follows the global Install Home/bootstrap contract.
Acceptance requirements
Mini Computer is implementation-complete only when acceptance coverage verifies at least:
* The maintained 9to1 VirtualBox fork is available as the built-in/default provider and a new supported VM can be created without connecting another hypervisor.
* The VirtualBox donor-parity audit records preserved/expanded/replaced/not-applicable mature VM capabilities and separately licensed proprietary components are not silently bundled.
* A user can connect at least one alternative provider implementation through the VirtualisationProvider contract and provider capability differences are surfaced accurately.
* A VM retains stable VMID across rename/reopen and provider-internal identity is mapped rather than becoming the only identity.
* A supported guest can be created, install media attached, started, installed, shut down, reopened and run again without configuration loss.
* Start, Pause, Resume, Save State, graceful Shutdown and Power Off have distinct verified lifecycle semantics.
* Closing/disconnecting the viewer leaves a headless-running VM running according to policy and reopening Mini Computer reconnects rather than creating a duplicate.
* Snapshot creation/restoration preserves the provider-declared disk/memory state and clearly excludes state the provider cannot capture.
* Deleting/consolidating snapshots cannot silently break a dependent linked clone.
* Full clone and provider-supported linked clone create distinct VM identities and preserve base dependencies correctly.
* Representative virtual-disk create/attach/resize/convert/delete operations preserve dependencies and require appropriate verification for destructive changes.
* BIOS/UEFI and supported Secure Boot/TPM configurations are represented accurately for a representative modern guest.
* NAT, bridged/host-only/private and representative port-forward configuration work on a capable provider and network dependencies block unsafe deletion.
* Clipboard, drag/drop and shared folders enforce configured direction/read-write policy rather than enabling broad host access automatically.
* Physical device passthrough exposes the host-impact warning/permission and does not silently detach devices from the host.
* Provider/host Virtualisation Status distinguishes disabled hardware acceleration, provider unavailable and feature unsupported states with actionable diagnostics.
* The ISO Browser lists only registered/curated publisher providers rather than arbitrary unverified Internet downloads.
* An OfficialDirect ISO acquisition downloads from the publisher-authorised source without CAKE rehosting it and records acquisition provenance.
* A MirrorPermitted catalogue entry can be mirrored only when its configured redistribution policy explicitly permits the exact image; otherwise mirroring is blocked.
* A downloaded ISO with authoritative checksum/signature metadata is verified before being labelled Verified and a mismatch blocks normal trusted acquisition flow.
* UserSupplied and ProviderFlow catalogue policies work without falsely claiming CAKE supplies the operating system image.
* The same immutable ISO can be referenced by multiple VMs without unnecessary duplicate canonical copies and removal identifies active mounts/dependencies.
* Unattended installation keeps passwords/product keys out of ordinary logs, project source and AI context.
* OVF/OVA or representative mature donor import/export previews hardware/network differences and preserves the declared source/revision scope.
* Mini Computer Files resources remain addressable while large mutable block data can use an appropriate provider without being misrepresented as ordinary sync documents.
* Terminal opens a VM-specific EnvironmentID/serial or guest shell and clearly distinguishes VM from host command execution.
* Dev OS Run uses Mini Computer's canonical engine to build/package → attach boot artifact → boot VM → expose viewer/serial/debug state rather than launching a separate hidden hypervisor.
* An ephemeral Dev VM can be discarded/preserved according to run policy without mutating the base template.
* AI VM actions route through typed Mini Computer/provider APIs and dangerous disk/snapshot/device/network operations receive the applicable impact verification.
* Import/export/download/clone/snapshot consolidation jobs report real progress/cancellation/recovery state without fabricating completion.
* Keyboard/screen-reader workflows cover VM creation, ISO Browser, VM state controls, settings, snapshots and provider status.
* Windows, 9to1-OS and supported remote/web management preserve the same canonical Mini Computer model while capability-negotiating local execution.
* When Home is absent at process start, Mini Computer follows the global Install Home/bootstrap contract.
Utilities
