# Dulche Linux GPU integration preparation

The 16 November source under this directory prepares disabled Linux VFIO plans against Windows publication `41a4ddf0973f9601026701d72bb6a6e314d94680`. Its fixture evidence describes plan generation and host/guest boundaries. Preserve the original safety and proof-authority checks when reviewing a candidate; a generated plan alone does not spend or grant hardware admission. [Source-specific safety and proof gates](SAFETY.md)

## Feasibility recorded on 16 November 2026

**The host/guest driver split is technically plausible on this machine. It is
not yet ready for the physical proof or sustained V100 use.** Current Linux
inspection found a clean device-isolation boundary and the required host driver.
Physical reset recovery, current guest initialization and combined inference
remain unverified. No pooled VRAM, ordinary cross-VM CUDA process, Thunder RPC
interoperation, training scalability or throughput result is claimed.

| Item | Current read-only evidence |
| --- | --- |
| Host | Ubuntu 24.04.5; kernel `7.0.0-42-generic`; Secure Boot enabled |
| Board | MSI B760 GAMING PLUS WIFI DDR4 / MS-7D98 rev 4.0; BIOS M.80, 18 March 2026 |
| Host GPU | RTX 5080 `0000:01:00.0`, `10de:2c02`, NVIDIA **595.99.02 open**; boot/display GPU |
| 5080 group | Group 14 contains `01:00.0` and its audio function `01:00.1`; preserve both |
| 5080 UUID | `GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2`, read from the already-loaded driver's proc metadata |
| V100 | `0000:05:00.0`, `10de:1db4`, subsystem `10de:1214`; unbound, empty override |
| V100 group | Group 18 contains **only `0000:05:00.0`** |
| Reset scope | Only `bus` advertised; upstream `0000:00:1c.4`, bus 05 contains only the V100 |
| Host dependencies | No storage, networking or display device shares that group or observed subordinate reset domain |
| Link | V100 **PCIe 3.0 ×4**, 8 GT/s; card maximum ×16 is not the observed link |
| IOMMU | Linux reports enabled interrupt remapping; group's reserved region is MSI, with no direct reservation reported |
| Virtualization | QEMU 8.2.2 package already present; `/dev/kvm` accessible via existing user ACL; no libvirt service required for the minimal candidate |
| Resources | Approximately 88 GiB available RAM and 428 GiB free filesystem space at inspection; these are snapshots, not launch reservations |

VFIO modules were not loaded, so their eventual unsafe-mode parameters could not
be validated. The plan correctly retains that gate. No ACS override or unsafe
IOMMU workaround is proposed. Device IDs, groups, reset scope, workloads and free
resources must be rediscovered immediately before any approved system action.

NVIDIA requires open modules for Blackwell and proprietary modules for Volta;
the two flavours cannot coexist in one kernel. Separate host and guest kernels
provide that driver boundary. R580 is Volta's final driver branch and CUDA 12.x
its final toolkit generation. The candidate guest driver is **580.196.07
proprietary**, with **CUDA 12.9 Update 1**, subject to guest kernel/package and
signature verification. NVIDIA's named KVM support in those release notes is
RHEL 9 KVM; this is not a claim that the whole Ubuntu host combination is vendor
certified. [NVIDIA module requirements](https://download.nvidia.com/XFree86/Linux-x86_64/580.196.07/README/kernel_open.html),
[architecture matrix](https://docs.nvidia.com/datacenter/tesla/drivers/latest/cuda-toolkit-driver-and-architecture-matrix.html),
[R580 release notes](https://docs.nvidia.com/datacenter/tesla/tesla-release-notes-580-196-07/index.html).

## Historical evidence and source reconciliation

Linux `main` was `0ce067c02e924151ae482a6f19511c6b8ca1b68a`. After the owner's
publication, it fast-forwarded to `41a4ddf0`, including `219fdf9b` and the retained
Studio, Files, account and training source. The 15 pre-existing dirty-file hashes
were checked before and after; none overlapped incoming paths. No reset, stash,
forced pull, branch/worktree creation or Windows edit occurred.

The 11 November Linux handoff and retained log record a small guest initialization
and a one-thread CUDA result. They do not prove this current candidate or any
dual-GPU inference. The old launcher has weaker timing, unpinned SSH host keys
and guest-driver signature warnings; it must not be used for this proof.
The retained host journal instead attributes a confirmed OOM kill to
`strata-host` on **9 November at 21:48 GMT**. A search across 9–12 November found
no corresponding 11 November OOM entry. That does not exclude an unlogged failure.

The new handoff corrects the model checksum typo. The approved inference-only
TinyLlama file is 668,788,096 bytes, with SHA-256:

```text
9fecc3b3cd76bba89d504f29b616eedf7da85b96540e490ca5824d3f7d2776a0
```

The segment is `da85b965`, not the earlier `da85f965`. Model bytes and the original
Windows authorization/download receipts are outside Git and have not transferred
with source. The Windows partition remains unmounted. This task cannot read the
raw partition without OS administrator authentication and has not mounted it,
forced it writable or changed its hibernation state.

## Implemented preparation

- `inspect_host.py`: metadata-only inspection with no CUDA/NVML/backend launch.
  It retains every group member and reset-domain descendant, including non-NVIDIA
  dependencies, distinguishes unknown bindings, and refuses evidence overwrites.
- `prepare.py`: validates inspection/model evidence and writes an inert review
  plan with explicit blockers. It never runs a command, downloads a model,
  consumes the real allowance or changes a system setting.
- `backend-lock.json`: exact upstream candidate source and build options, with
  binary/package hashes deliberately unresolved until actual builds are reviewed.
- `templates/`: inactive guest and SSH/RPC option maps, not launchers.
- `proof_safety.py`: Linux CPU lifecycle fixtures and strict thermal/deadline
  policy. Ordinary invocation without a CPU fixture refuses real execution.
  See `SAFETY.md` for implemented controls and the remaining guest-specific gates.
- `test_qemu_control.py`: an actual halted, diskless 64 MiB QEMU/TCG fixture
  verifies QMP quit and owned-process fallback without a guest OS, network or
  hardware assignment. It does not prove guest or GPU quiescence.

All source changes in this directory are project-local. No driver, package,
module, binding, reset, guest disk, boot configuration, service, firewall rule,
Secure Boot setting or GPU workload was changed by this preparation.

## One backend and one host-owned storage hierarchy

Keep `InferenceEngineDispatcher` and the existing configured llama.cpp adapter
as the application owners. They currently choose an engine; they do not combine
Strata and llama.cpp. Add a future **typed** RPC candidate profile through the
existing C# launch planner and Python broker. Their present fixed commands have
no RPC controls, and the packaged broker allows only `AF_UNIX`. Do not bypass
these boundaries with environment variables or arbitrary extra arguments.

Keep application-facing inference on its owned Unix socket. Pin the host
`llama-server` and guest `ggml-rpc-server` to the same upstream commit:

```text
llama.cpp v1.0.0
b1b770d90d94cad9e9ea5b993bdbc383a522fb28
RPC protocol from source: 10.0.0
CUDA toolkit: 12.9.1; nvcc: 12.9.86
Host architecture: 120a-real; guest: 70-real
```

The exact build choices and source URLs are in `backend-lock.json`. These are
source-reviewed candidates, **not compiled or hardware-verified binaries**.
Both ends disable RDMA/NCCL and use private TCP through SSH. The guest RPC server
binds only guest loopback; the host tunnel binds only host loopback. Require a
separately verified SSH host key, dedicated identity, no agent/X11 forwarding,
and no automatic reconnect/restart for the proof. Loopback still admits local
users; this design assumes no untrusted tenants. RPC remains experimental and
insecure, even behind a tunnel. [Pinned upstream RPC guidance](https://github.com/ggml-org/llama.cpp/blob/b1b770d90d94cad9e9ea5b993bdbc383a522fb28/tools/rpc/README.md).

Do not invoke CUDA binaries even with `--help` before the timer: this pinned
RPC server loads backends before argument parsing. `--no-warmup` does not remove
driver/device initialization. A future proof must show actual work on both
devices, not just enumeration, allocated memory or two unrelated requests.

Use the same explicit `XDG_DATA_HOME` for the existing broker and model manager.
Their default shared root is `~/.local/share/haven/models`; `HAVEN_MODEL_ROOT`
alone is unsuitable because the broker does not honour that override. Retain
modelctl's content-addressed blobs/manifests and the broker's shared lifetime
lease. The default store directory was absent on this host; existing weights in
other locations have not been moved, duplicated, deduplicated or removed.

Keep checkpoints under a sibling host-owned training-artifact root configured
through the existing `ArtifactStore`, preserving run IDs, provenance and
non-overwriting hashes. This is a storage integration plan, not an already
unified catalogue. The guest needs neither model files nor a writable model
share for the RPC proof; leave its tensor cache off. Windows can later use the
host service when Linux is running, but dual boot cannot keep that service alive
while the same PC runs native Windows.

The new Windows training supervisor, runtime verification, client and worker
host are deliberately Windows-specific. A future Linux training port must adapt
those existing owners together; no duplicate training runner or relaxation of
the V100 cooling hold is introduced here.

## Minimal guest, changes and rollback

Retain the existing disk, backing image and NVRAM under
`/home/jacob/Documents/Default Project/vm/`. Any approved candidate uses a new,
uniquely named overlay and NVRAM copy, with original hashes retained. Never
attach a host storage disk or the Windows partition to it. Use Q35, two vCPUs,
4 GiB guest RAM, 39-bit physical addressing, and the 64 GiB root-port MMIO window
documented by the earlier experiment. That window is address space, not RAM.
Use a private QMP Unix socket and restricted user-mode networking; no host
bridge, libvirt daemon installation or boot change is necessary for this design.

| Stage | Exact intended scope | Rollback / refusal |
| --- | --- | --- |
| Guest preparation | Fresh overlay/NVRAM; no physical GPU. Verify guest OS, kernel, signature state, trusted SSH identity and CPU-only stop chain. Preserve source disk and firmware choices. | Stop only the owned candidate QEMU process; retain overlay, logs and original hashes. No host package/service changes. |
| Guest software, separate approval | Install **guest-only** R580 proprietary 580.196.07 and CUDA 12.9.1 after presenting exact package versions, hashes and dependency transaction for that inspected guest. No host driver install. | Retire the candidate overlay; original guest remains unchanged. No disabling signature enforcement to force a load. |
| Device assignment plan, separate approval | Recheck IDs/group/reset scope; plan only required VFIO modules with safe parameters, only V100's per-device override and binding of `0000:05:00.0`. Record exact prior state. Actual device binding belongs inside the guarded proof, because it may reset or touch the card. | After verified cessation and released device ownership, unbind only this V100 and clear only this session's override to restore the observed unbound state. Never unload NVIDIA globally. |
| Physical proof, all gates required | Consume the single shared authorization and start the timer before any V100 binding, attachment or initialization. Attach only the V100; one small inference; stop at 25 s and finish teardown by 30 s. | Independent guest stop plus owned host VM fallback. No retry or reset probe. If teardown/reset fails, stop automation and report; no unapproved host reboot. |

The GPU-changing stages have **not** been approved or applied. GPU-free local
preparation and CPU tests are already authorized. Before each consequential
system stage, present its final command/package manifest and rollback to the owner.
The guest package transaction is intentionally not invented before inspecting
the actual guest. Do not install a broad CUDA metapackage that replaces the
host's working open driver.

Proposed hard budgets are guest RAM 4 GiB, QEMU cgroup memory maximum 6 GiB,
host inference maximum 4 GiB, guard reserve 1 GiB and at least 16 GiB host
headroom (27 GiB admission floor). Use two inference threads, one request,
512-token context, 32-token batches, eight generated tokens, no swap allocation
and bounded PID/log/disk usage. Reserve 20 GiB free disk plus a 12 GiB candidate
budget. These are **proposed caps, not currently enforced**; no large-model test
is authorized by them.

## Why the physical proof stays disabled

The V100 UUID, fresh starting temperature and current guest-side telemetry are
unknown. Initializing its driver to obtain telemetry is itself inside the one
allowance; it is not a free preparatory probe. No exception to the 750 ms
freshness bound or 40°C starting limit has been established. Cold initialization
may therefore prevent this proof under the current limits.

The CPU tests do not prove guest telemetry latency, a guest-local independent
stopper, QEMU/cgroup teardown, physical GPU quiescence or bus-reset recovery.
The Windows receipt and single cross-OS compare-and-consume authority also remain
unreconciled. A hash or read-only local file is not a globally immutable one-use
record; `SAFETY.md` specifies the required protocol without claiming it exists.
No agent may substitute an independent local token or spend a test to discover
whether these controls work.

The next work is to obtain the original proof receipts and model safely, test
the candidate guest's CPU-only telemetry/stop chain, and resolve its signed-driver
configuration. Only then can a precise GPU-changing approval be useful. Sustained
V100 work remains prohibited until directed cooling is installed and verified.

## Reproduce the safe checks

Use a fresh private evidence directory outside the repository; do not overwrite
the earlier captures. From the repository root:

```sh
python3 -I -S -B -m unittest discover -s eng/dulche/linux-vfio -p 'test_*.py' -v
python3 -I -S -B eng/dulche/linux-vfio/inspect_host.py --output /fresh/private/inspection.json
python3 -I -S -B eng/dulche/linux-vfio/prepare.py --inspection /fresh/private/inspection.json --output /fresh/private/plan.json
```

Tests use synthetic telemetry, fixed CPU children and a halted diskless QEMU
fixture. They do not run inference, initialize a GPU, boot a guest OS or consume
the real allowance. Exit zero from the
planner means a disabled review artifact was written, not that its gates passed.
The exact current run counts and output hashes are retained with the delivered
evidence. Native Windows/UEFN remains the intended gaming environment; Epic
documents Fortnite in virtual machines as unsupported.
[Epic support](https://www.epicgames.com/help/c-34254770/c-37371353/a25107496).
