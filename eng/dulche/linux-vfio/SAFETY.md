# Linux proof safety preparation

Status: CPU fixture implementation only. No real proof launcher exists here.
The ordinary public invocation without `--cpu-fixture` always refuses,
regardless of configuration or environment. The private `__primary` and
`__worker` entrypoints implement only the fixed CPU fixture children; they are
not GPU launch modes. The reviewed source does not import CUDA/NVML, open a GPU device,
invoke `nvidia-smi`, bind PCI devices, boot QEMU, or launch an inference binary.
No V100 allowance was consumed by these CPU tests.

Use Python's `-I -S -B` flags for direct invocation: isolated mode, no `site`
startup or `.pth` processing, and no bytecode writes. Both child launches enforce
those flags themselves and use a minimal fixed environment, with GPU visibility
disabled and no inherited `PYTHONPATH`, `LD_PRELOAD` or `LD_LIBRARY_PATH`. This
keeps arbitrary inherited startup hooks outside the fixed fixture. It is not a
sandbox against editing the script or replacing the trusted Python executable.

## Review a proof receipt

Match the receipt to the exact caller, profile, fixture mode, authority record and named artifact. Inspect original failures and timeouts before interpreting a later candidate. This source’s fixture checks are independently reproducible in software. The physical proof gates below retain their own provenance. [Safety checks](proof_safety.py) · [Authority owner](proof_authority.py)

## Implemented policy

- One monotonic start instant precedes fixture dispatch; receiving samples does
  not extend it. Work stops at 25 seconds, and cleanup must finish by 30 seconds.
- The first V100 temperature must be at most 40°C. Stop at 55°C or a rise of at
  least 10°C from that first reading. Stop the 5080 workload at 75°C.
- The fixed telemetry producer samples every 250 ms. The policy refuses missing
  or stale data older than 750 ms, replayed/skipped sequences, wrong session/GPU
  identities, invalid timestamps, malformed/nonfinite temperatures and clock
  reversal. A refusal latches: later samples cannot resume the same policy.
- An independent supervisor owns only two newly created, fixed CPU children.
  It stops and reaps them, first with TERM then KILL, without name-based process
  matching. Its own sampling loop never blocks waiting for a telemetry frame.
- Both children use Linux parent-death notification. The workload also watches
  timestamped supervisor heartbeats and its own absolute 25-second deadline, so
  a frozen supervisor cannot leave that CPU workload running indefinitely.
- The supervisor checks acquisition age, not just arrival time. A future guest
  collector must establish a trustworthy bound between the guest's acquisition
  time and the host's monotonic clock. Re-stamping delayed remote data on receipt
  is prohibited. These fixture timestamps share one host clock; they do not
  prove clock synchronization or transport latency across a VM boundary.

These are software checks, not a hard real-time or physical power-off guarantee.
Linux scheduling, a hung driver, a suspended host or a hardware fault can prevent
timely stopping. No fixture here establishes safe cooling or actual GPU quiescence.

## One allowance across operating systems

`consume_fixture_record` exercises exclusive creation and crash-durable writing
of a clearly named `CPU-FIXTURE-CONSUMED.json` before dispatch. A previous or
partial record refuses another attempt. It is only a local CPU test artifact.
Owner-writable files, read-only file modes and hashes are not an immutable global
authority; copying or deleting a directory would defeat such a claim. Never use
this helper or a newly chosen directory to authorize the real V100 proof.

The real authority is **not deployed or reconciled**. Before a real launcher can
exist, all agents and both operating systems must agree on one authority and one
proof ID. The required protocol is:

1. Retain the owner's full Windows authorization, handoff and model download
   receipt with their original hashes. Reconcile every earlier attempt and
   require an unambiguous unspent state; a missing/uncertain record refuses.
2. Pin the model's path, size and complete SHA-256; host and guest GPU UUIDs and
   PCI identities; backend commits and executable/library hashes; source plan;
   thermal limits; and exact stopping authorities in the single authorization.
3. Use a single durable authority with atomic compare-and-consume and immutable
   or independently retained append-only evidence, reachable consistently from
   both Windows and Linux. The Windows launcher must consult that same state;
   a stale offline copy must refuse. A local `chmod` or second per-OS ledger does
   not meet this condition. No cloud service is provisioned by this preparation.
4. Atomically mark the allowance consumed, durably acknowledge the record, and
   retain a hash-linked receipt **before** the first VFIO binding or physical attachment,
   driver initialization, GPU telemetry dispatch, CUDA enumeration, or backend
   startup that can touch the V100. Start the one monotonic timer before that
   first operation. Do not reset the timer between initialization and inference.
5. Any failure or uncertain dispatch after consumption remains consumed. No
   automatic retry, refund, additional warmup, recovery benchmark, or fresh
   directory can authorize another attempt. Append terminal evidence separately;
   never rewrite the original consume record. Fresh user authorization would be
   required for a later attempt.

## Real-run gates still outstanding

The V100 is currently unbound, so a reliable starting temperature cannot be
assumed from old logs. Guest-side driver initialization and any telemetry setup
that touches the GPU count inside the same allowance. There is no free warmup
or startup grace period in this policy: absent first telemetry fails after
750 ms. The first trustworthy reading must be at most 40°C. If these conditions
cannot be established, do not dispatch the proof; review the plan with the owner.

An actual VM implementation additionally needs all of the following:

- a fresh approved system-change and rollback plan, verified IOMMU/reset scope,
  model receipt and checksum, guest-driver configuration and exact build pins;
- a guest-local collector and independent guest-local stopper, tested without
  attaching the V100, plus authenticated sequenced telemetry with bounded age;
- an independent host stopper for the **owned QEMU process and cgroup**, tested
  with a disposable CPU-only guest under the authorized local-test scope, including primary
  guard loss, watchdog freeze/death, transport loss and guest failure;
- authoritative process/cgroup identity and no escaped descendants; stopping
  a local client or SSH connection does not prove guest inference has stopped;
- reliable evidence that guest work stopped, with a bounded host QEMU kill
  fallback and verification that it has exited. VM exit is not proof of a
  successful physical reset, an idle GPU, or cooling qualification;
- host/guest memory, PID and CPU caps that preserve the active desktop and
  existing model services. The fixed CPU fixture's tiny footprint does not
  validate an inference memory budget or OOM recovery;
- a shutdown budget for the complete chain and a refusal if it cannot fit inside
  the five seconds remaining after the work cutoff.

The present tests cover only fixed CPU child processes. They cannot authorize
using the older GPU Control launcher, its looser telemetry windows, a legacy
`pkill` stop script, or Windows Job Object code as a Linux safety mechanism.

## CPU checks

From this directory:

```sh
python3 -I -S -B - <<'PY'
import sys, unittest
sys.path.insert(0, '.')  # This reviewed fixture directory only.
unittest.main(module='test_proof_safety', verbosity=2)
PY
```

The suite includes a real 25-second CPU deadline run, so it normally takes about
28 seconds. It also tests primary guard SIGKILL, supervisor SIGKILL and SIGSTOP,
worker death, malformed/replayed/stale telemetry, both GPU temperature policies,
irreversible local fixture consumption and concurrent fixture consumers. The
suite creates disposable CPU-only directories and never uses the real proof ID.

For an individually retained fixture receipt, create a fresh private directory
outside the repository, then run `python3 -I -S -B proof_safety.py --cpu-fixture
/that/directory --scenario hot`. The available scenarios are fixed; there is no arbitrary
command argument or inference execution mode. Exit zero means the CPU safety
scenario stopped as designed; it is not a successful inference proof.
