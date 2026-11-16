# Dulche Linux handoff — 16 November 2026

> Portable context for a Linux agent. This is self-contained; do not assume access to the preceding conversation. Facts below were checked on 16 November 2026.

## Objective and scope

Continue the existing 9to1/Dulche project and investigate a working Linux integration for an RTX 5080 plus Tesla V100. I want one maintainable AI backend and one canonical model/checkpoint store, avoiding unnecessary Windows/Linux duplication. Prioritise Linux for AI. Preserve native Windows for UEFN, Fortnite playtesting and gaming.

The candidate architecture is:
- Native Linux host retains the RTX 5080 and its supported NVIDIA open kernel driver.
- A small headless Linux KVM/QEMU guest exclusively owns the V100 through VFIO passthrough, using a supported R580 proprietary driver and a Volta-compatible CUDA 12.x runtime.
- The existing Dulche backend communicates with the guest through a secured, version-pinned transport. Investigate actual local-plus-remote inference support before claiming both cards can share one model.
- Keep model files in one controlled store, with explicit ownership and immutable checkpoint outputs.

This is a candidate, not a proven configuration. Do not assume ordinary same-process CUDA multi-GPU operation, automatic pooled 32GB VRAM, peer-to-peer transfers, or training support across the VM boundary. Measure transfer overhead before recommending it for large MoEs.

## Non-negotiable restrictions

1. Do not change Windows drivers, Windows boot configuration, partitions, UEFN projects, anti-cheat settings, Secure Boot or signature enforcement. Preserve the native Windows/UEFN fallback. Do not replace it with a VM without a new explicit decision; Epic currently documents Fortnite/EAC in VMs as unsupported.
2. Inspect first. Before installing/replacing host drivers, rebinding/resetting any PCI device, enabling system virtualisation services, altering GRUB/initramfs/module-loading rules, installing the guest GPU driver, attaching the physical V100 to a VM or rebooting, prepare the exact changes and rollback plan and obtain explicit approval. The preceding instruction was: “don't change the drivers or boot setup yet.”
3. Preserve the 5080 display and any active games or model servers. Never blacklist NVIDIA globally or detach the 5080 to make V100 assignment easier. Do not use ACS-override/unsafe-IOMMU workarounds to conceal inadequate isolation.
4. The V100 cooling shroud is not confirmed installed. No sustained V100 inference, training or benchmarking is permitted. Read-only hardware/temperature observation is allowed.
5. There is ONE previously approved approximately 1B dual-GPU inference proof, at most 30 seconds including GPU initialization/loading and shutdown, with independent thermal/time safeguards. It is still unspent at this handoff. This is one allowance across all agents and operating systems, not one per OS. No extra V100 warmups, CUDA probes that initialize a workload, retries or benchmarks outside it.
6. The 5080 is separately authorised for necessary development/tests under reasonable thermal/resource limits. Stop for active gaming or protected work.
7. Preserve every model, checkpoint, adapter, quantization, original input, configuration, log and result. Use unique output directories, hashes and atomic non-overwriting writes. Pause on insufficient storage; never delete old artifacts automatically.
8. Do not use OpenAI-generated examples, labels, rewards, corrections or filtering signals as training material for competing models. The immediate proof is inference-only.
9. Give concise progress updates at least every 30–60 seconds while working. Clearly distinguish inspected, built, tested, measured and unverified results.

## Confirmed hardware and Windows observations

- CPU: Intel Core i9-14900K; 96GB DDR4.
- Motherboard: MSI B760 GAMING PLUS WIFI DDR4, MS-7D98, board revision4.0.
- Firmware observed: M.80, dated 18 March 2026. Do not assume this identifies a Linux package or a specific MSI download without checking.
- RTX 5080:16GB, PCI ID10de:2c02, subsystem1458:4196. Discover and pin its actual GPU UUID locally before any workload.
- Tesla V100 PCIe16GB: PCI ID10de:1db4, subsystem10de:1214. Its UUID is unknown; discover it rather than inventing one.
- Windows11 Home build26300; 5080 driver610.88. V100 reports Code28, no assigned driver/service. No NVIDIA driver was changed.
- Windows V100 path: PCIROOT(0)#PCI(1C04)#PCI(0000), behind Intel root port5 (7A3C).
- 5080 path: PCIROOT(0)#PCI(0100)#PCI(0000), a different root port. These Windows paths are observations, not Linux BDFs; rediscover the addresses.
- V100 reports ACS-compatible hierarchy3, meaning Supported in Microsoft's SurveyDDA definitions, and no required reserved-memory region. Its upstream port also reports supported hierarchy and no reservation.
- Valid ACPI DMAR table:136bytes, checksum valid, flags5, interrupt-remapping bit set.
- V100 currently reports four PCIe lanes, despite the card's x16 capability. MSI documents the chipset-connected PCI_E3 slot as x4. Do not present nominal x16 card bandwidth as the actual link.
- Windows already had a hypervisor/VBS running. Some CPU WMI virtualization flags reported false; this was not treated as proof virtualization was disabled.
- These checks make the hardware promising. Linux IOMMU groups, safe GPU reset, actual VFIO assignment and guest-driver initialization remain UNVERIFIED.

## Driver and virtualisation facts

Verify current primary documentation before installing anything:
- NVIDIA's R580 Linux documentation says Blackwell requires open kernel modules; Volta requires proprietary modules. Those module flavours cannot coexist in one kernel. Do not install one proprietary R580 host stack and claim that solves both GPUs.
- Volta's last supported driver branch is R580 and its last CUDA toolkit generation is12.x. An R580 driver supporting CUDA13 generally does not make CUDA13 a supported V100 stack.
- VirtualBox7.2 documents PCI passthrough as unavailable. Its virtual3D acceleration is not direct ownership of the V100.
- WSL relies on the Windows GPU driver and does not solve this isolation problem.
- Epic documents Fortnite and EAC as unsupported in VMs. UEFN editor startup alone would not establish a supported editing/playtesting workflow. Do not spoof VM detection or hardware IDs.
- A VM hosted by native Linux stops when this same PC reboots into native Windows. Do not promise an always-running AI service across dual-boot transitions.

## Existing source and artifacts

Repository: CroakyJake12/9to1. Work stays on main; no new implementation branch/worktree without a later explicit instruction.

## Source publication checkpoint

The owner requested publishing the existing local progress so the Linux agent can access it. This document ships with the previously uncommitted Studio, Files, browser, website/account-service and Windows build-guard source. It follows these six development commits on main:

- 6bc28412: segmented training reference core and comparable memory metrics.
- 132913d2: pinned Studio worker client and Qwen catalogue transition policy.
- 4798d47d: preserved CPU training sessions and supervised Studio worker.
- 440395c3: CPU full/LoRA checkpoint recovery.
- 5622c8e0: model family presentation and provider metadata.
- 219fdf9b: automatic Mesh networking gates and desktop startup repair.

Use `git status --short --branch`, `git fetch origin`, and inspect local versus origin/main before integrating. A clean main without divergent commits can use `git merge --ff-only origin/main`. If local changes or divergent commits exist, preserve and reconcile them; do not reset, stash, force-pull, clean or overwrite them. Do not create a second implementation branch. Discover the actual Linux checkout; /home/jacob/Documents/GitHub/9to1 is only a historical candidate.

These are development checkpoints, not a completed release. Model weights, checkpoint outputs, private configuration and machine-local evidence remain outside Git. Their absence from Git is intentional; source and dependency manifests are included. Coordinate artifact transfer separately, with hash verification and no overwriting.

Relevant existing components:
- 9to1 Models/Dulche Alpha/InferenceEngines: common dispatcher, llama.cpp and Strata adapters, calibration and lifetime contracts.
- 9to1 Models/Dulche Alpha/Training: efficient reference methods, custody, HF adapter, worker API.
- 9to1 Workspace/Home/Source/Dulche/llamacpp and shared/src/Haven.Infrastructure/Providers: existing runtime/model registration.
- Keep one backend integration; do not replace it with an unrelated model runner. Preserve Files, accounts, Studio and other WIP.
- The obsolete Sandbox plug-in was removed by the owner. Follow applicable current project guards and serialise heavy builds. Development commits are authorised, and the owner has now explicitly requested this source publication to main.

Windows evidence root:
<Windows task directory>/work/v100-dual-proof

Relevant files there:
- passthrough-readonly-20261116.json: actual motherboard/PCI/firmware observations.
- owner-authorized-one-proof.json: original global one-use proof constraints.
- run_proof.py, guard.py, gguf_metadata.py: reviewed Windows-specific proof/thermal/time design.
- llama-b12359-runtime-pins.json: Windows runtime identities; Windows DLLs/Job Object code are NOT portable Linux executables.
- current-driver-backup-hashes.json and current-61088-driver-backup: preserve Windows recovery files; no downgrade is approved.
- authorization-consumed.json was absent at handoff. Recheck coordinated current state before using the single allowance.

An approved, retained inference-only model already exists:
tinyllama-52e7645ba7c309695bec7ac98f4f005b139cf465/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf
under that evidence root.
Size668,788,096bytes; SHA-256:
9fecc3b3cd76bba89d504f29b616eedf7da85b96540e490ca5824d3f7d2776a0

Reuse it if safely accessible and hash-verified. Do not force-mount hibernated NTFS or attach one writable filesystem concurrently to host and guest. If Windows files are inaccessible, request the specific evidence rather than assuming it transferred.

Full project checkpoint remains locally retained under:
<Windows task directory>/work/dulche-2026-11-16/continuation-20261116-1933.md

Its older pending-input notes are superseded: the owner dismissed the firewall prompt and approved the approximately272MB SmolLM2-135M baseline download. That model has not been downloaded or trained at this handoff. Signed-in Files acceptance remains user-pending. The V100 allowance remains unspent.

## What was actually tested

On Windows, the 5080 passed TinyLlama inference (23/23 reported layers offloaded), a small PyTorch forward/backward check, and one tiny Llama/PEFT LoRA software update. The latter retained21 unchanged frozen tensors and four changed LoRA B tensors; sampled peaks34C/52.668W. CPU full/LoRA checkpoint continuation also matched across two processes. These establish those narrow paths only.

No V100 inference, dual-GPU execution, Linux GPU integration, real-model training-quality improvement or VM passthrough has been demonstrated. The previous Qwen endpoint127.0.0.1:47831 refused connection in Windows; Linux may differ. Preserve existing installations and do not interrupt a running mentor.

## Validation and remaining project work

Recorded checks against the preserved source, before this publication:

- HF CPU suite:33 passed; full/LoRA resume reproduced across separate worker processes.
- Studio:36 passed; native create/save/history/reopen/self-check/recovery observed. Native pause not observed.
- Home model picker:71 passed. Native expand/collapse/refresh acceptance remains incomplete.
- Files controller:26 passed, including Trash/Restore and refused restore without false success; earlier Files backend suite87 passed. Signed-in hosted Restore, cross-tab sign-out, reconnect and quota acceptance remain pending.
- Account service:165 passed with0 skipped; runtime dependency gate and schema/type/UI checks passed. The deployed source matches the preserved gated manifest; registration and private Access holds remain in force.
- Mesh-focused checks:93 passed,1 existing Linux-only skip on Windows; lifecycle5 passed and actual SQLite backup check passed.
- Final Windows Desktop build:0 warnings/errors. Final Browser Release build passed with79 retained IL2026/WASM0001 warnings.

The wider Desktop test project still has unrelated legacy compilation failures. Do not turn selected passing suites into an all-repository acceptance claim. No additional web deployment is implied by this Git publication.

Keep public registration, live payments and public AI held. Existing-account private testing only. Continue Files and Dulche on the same main without undoing either. Use Montserrat and the user-supplied CakeMods/9to1 branding; the shared identity is 9to1 Account. Preserve the first-party sign-in behavior and protected administrator routes.

Dulche still needs real-model training admission, production GPU supervisor integration, independent-data quality/performance baselines, Quant Lab and Neural Explorer integration, and the broader efficient-training milestones. The segmented implementations are reference experiments, not proof that 27B or Flash-class full training is practical. Qwen4 remains the intended public family; any Qwen3.8 development fallback must be clearly identified as the actual underlying model. Verify real releases/licences before exposing executable catalogue entries.

The TinyLlama checksum above is authoritative. A prior pasted prompt had a typo at `da85f965`; the correct sequence is `da85b965`.

## Continuation order

1. Read the repo's current instructions and this handoff. Inventory source, processes, distro/kernel, NVIDIA module flavour, driver versions, libvirt/QEMU availability and free storage without changing anything.
2. Read Linux PCI topology, IOMMU groups, current driver bindings, advertised reset methods and firmware/kernel messages. Identify every device in the V100's group and effects on host-critical storage/network/display. Reading reset_method is allowed; invoking reset is not.
3. Decide whether the candidate split is viable. Report exact blockers rather than claiming success from lspci or nvidia-smi enumeration.
4. Prepare the smallest supported headless guest configuration, isolated transport, explicit device assignment, model-store ownership, resource limits, rollback and evidence plan. Then request the required system-change approvals. No blanket host blacklist or boot edits.
5. Inspect the pinned inference backend's actual RPC/distributed capability. llama.cpp RPC is experimental and upstream warns it is insecure on exposed networks: isolate and protect it, never open it publicly. Pin compatible protocol/builds on both ends. Do not assume Thunder/Strata gains equivalent RPC support automatically.
6. Prepare and CPU-test Linux-owned process/cgroup watchdogs and stale-telemetry failure handling before touching V100 computation. For the one approved proof, start the deadline before V100 CUDA/server initialization, use a25-second work cutoff with5-second teardown reserve, and confirm actual cessation. Obtain V100 telemetry from the guest; fail closed if either GPU's exact identity or fresh thermal data is unavailable. Original thresholds were V100 start<=40C, stop>=55C or10C rise;5080 stop75C;250ms sampling, stale at750ms. Do not silently relax them.
7. Before dispatch, confirm the global allowance remains unspent and the cooling state has not changed. If a safe independently enforceable test is impossible, stop and report that blocker. Do not consume the allowance through uncounted probes/retries.
8. Prove one model performs inference using both cards with backend layer/device allocation and actual execution evidence; device enumeration, weight residency or separate unrelated requests are insufficient. Preserve logs, configurations, hashes, telemetry, output and shutdown evidence.
9. Keep V100 unavailable for sustained workloads until the shroud is installed and cooling verified. Leave host display, native Windows/UEFN, model data and the repository healthy. Report exactly what is implemented, tested and still blocked.

## Primary references

- https://docs.oracle.com/en/virtualization/virtualbox/7.2/user/vboxmanage.html
- https://download.nvidia.com/XFree86/Linux-x86_64/580.95.05/README/kernel_open.html
- https://docs.nvidia.com/datacenter/tesla/drivers/latest/cuda-toolkit-driver-and-architecture-matrix.html
- https://docs.kernel.org/driver-api/vfio.html
- https://github.com/ggml-org/llama.cpp/blob/master/tools/rpc/README.md
- https://www.epicgames.com/help/c-34254770/c-37371353/a25107496
- https://www.epicgames.com/help/c-32735058/c-37477814/a21516107

## Instructions to the receiving agent

Continue from this state. Do not restart the project or repeat rejected approaches without new evidence. Inspect the listed source before editing, preserve all confirmed restrictions and existing work, and report contradictions before making material system changes. The immediate deliverable is a verified Linux feasibility result and a concrete implementation/rollback plan; installation and hardware execution follow only after their applicable approvals and safety gates.
