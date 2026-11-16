# Dulche local worker API

The first operation is a supervised **CPU engine self-check** using two fixed
toy tensor updates. Every attempt and checkpoint has purpose
`software_test_only`. Eligible resume checks repeat update 2 and compare all
checkpoint metadata and tensor bytes with the original uninterrupted update.

## Launch and transport

Studio starts the operator-pinned Windows Python and verified CLI ZIP with
`-I -S -B`. The ZIP and its SHA-256, plus the absolute private operator config
path, are deployment settings. Open stdin as a pipe, write one UTF-8 JSON object
up to 32 KiB, and close stdin. Read UTF-8 JSON Lines from stdout. The protocol
accepts one request per invocation. Caller-provided paths, executable settings,
device identifiers, limits and training data are outside this schema.

## Requests

| Command | Schema 1 JSON object fields |
| --- | --- |
| `status` | `schema: 1`, `command: "status"`. Both `run_id` and `attempt_id` are optional together. |
| `list` | `schema: 1`, `command: "list"`, optional `limit` integer 1–50 (default 20), optional `after` AttemptRef. |
| `launch` | `schema: 1`, `command: "launch"`. |
| `resume` | `schema: 1`, `command: "resume"`, `checkpoint` ArtifactRef equal to the original attempt's `resume_checkpoint`. |
| `pause` | `schema: 1`, `command: "pause"`, `run_id` and `attempt_id`. Writes a durable cooperative pause request for the named attempt. |

AttemptRef has `run_id` and `attempt_id` strings. ArtifactRef adds
`artifact_id`, `sha256` and `size_bytes`. Identifiers are canonical lower-case
UUID strings. A digest is a lower-case SHA-256 hex string. The size is an integer
number of bytes. Unknown or duplicate fields are rejected.

## Events

- `{"schema":1,"event":"accepted","attempt":<AttemptRef>}` identifies the new
  durable attempt before supervisor dispatch. Only launch and resume emit it.
- `{"schema":1,"event":"progress","attempt":<Attempt>}` records an observed
  sealed update while running. A short job can finish between observation ticks.
- `{"schema":1,"event":"result","result":<result>}` is the terminal response to
  the command. The attempt it describes can still be running for status or pause.
- `{"schema":1,"event":"error","code":<string>,"message":<string>}` is a terminal
  command error. Error strings omit private paths and exception diagnostics.

An Attempt has `run_id`, `attempt_id`, `job: "cpu_self_check"`,
`purpose: "software_test_only"`, `status`, `reason` (string or null),
`completed_updates` (integer or null), `artifacts` (array of verified
ArtifactRefs), and `resume_checkpoint` (ArtifactRef or null).

The attempt status is `prepared`, `running`, `passed`, `failed`, `paused`,
`interrupted` or `cancelled`. A null `resume_checkpoint` means this attempt
does not provide the approved first checkpoint for resume. A pause result
reports the current persisted state. Poll that attempt or read its original
launch stream for the terminal outcome.

Launch, resume, pause and attempt status return an Attempt. Status without
identifiers returns `{"ready":<boolean>,"job":"cpu_self_check","purpose":"software_test_only"}`.
List returns `{"attempts":[<Attempt>,...],"next":<AttemptRef or null>}`. Send
`next` as the next request's `after` cursor. Attempts are ordered by
`(run_id, attempt_id)`, ascending. Each event is at most 32 KiB excluding its
newline. A list page can contain fewer attempts than its requested limit when
the byte limit applies. Its cursor always identifies the last included attempt.

The CLI exits zero for a valid result even when the recorded attempt ends
paused or failed. A terminal command error exits nonzero.

## Operator setup

From the reviewed Training directory, prepare separate archives in an existing
private installation directory.

```powershell
python -B -m worker_api.prepare_bundle --output C:/operator/cpu-self-check.zip
python -B -m worker_api.prepare_bundle --cli --output C:/operator/studio-worker-cli.zip
```

The commands publish exclusively to fresh paths and print the two hashes.
Record the CLI ZIP hash in Studio's trusted settings. These settings also pin
the absolute Windows Python executable, its SHA-256 and the absolute operator
config path. Use a Python installation that starts without `._pth` overrides.
Verify the executable and CLI archive digests before dispatch. The CLI executes
from its approved ZIP with the isolated flags above.

The private operator JSON has `schema: 1`, absolute `artifact_root`,
`worker_bundle` and `python` paths, `worker_sha256` and `python_sha256` digests,
`dependency_roots` (absolute site-packages paths), `packages` (objects with
`distribution`, `module`, `version`, and zero-based `root_index`), and
`protected_process_images` (exact Windows executable basenames). Register
Torch and its applicable dependencies. Package contents come from the
operator-managed installations. Initialize `artifact_root` with the existing
`custody.ArtifactStore` before first use and keep it independent of the source
archives.

The supervisor owns the software check process from before dependency imports.
It applies a 45-second wall limit, a 4-GiB Job memory limit, a 10% Job CPU rate
cap at idle priority, one Torch thread and a 32-MiB attempt storage allowance.
It samples Windows available RAM, owned-process memory, free disk space and the
protected process list. CUDA starts masked and the worker records guard
receipts before and after the check. The attempt retains its original source,
output logs, resource configuration, control receipts and sealed checkpoints.

Pause records persist independently of supervisor-owned control records. A
pause before the first update can finish with zero checkpoints. The original
successful two-update attempt provides the eligible step-one resume reference.
