# HavenOS / CakeOS Windows compatibility broker

This directory contains the first implementation slice of the optional Windows compatibility layer for the current CakeOS repository. Historical HavenOS naming is retained where the surrounding platform interfaces have not yet been renamed.

## Implemented

### Wine isolation

- provider-based manifest model (`wine` and reserved `winboat` provider names)
- per-application Wine state/prefix locations
- fail-closed Bubblewrap requirement
- private network namespace only; network-enabled manifests are rejected in slice 1
- explicit host mount grants beneath `/mnt/haven-share/<name>` only
- broad and sensitive host filesystem grants rejected after host-path resolution
- explicit GPU render-node grant which fails if no render node exists
- Wayland-only display socket exposure for slice 1
- explicit refusal of clipboard/media permissions until enforceable mediation exists
- explicit refusal of WinBoat until its VM/container boundary is implemented and runtime-proven

### Lifecycle

- one transient `systemd --user` service per launched Windows application
- deterministic SHA-256-derived unit names rather than raw application IDs
- `KillMode=control-group` ownership of the full Wine process tree
- `TimeoutStopSec=10s`
- clean service environment through `env -i`; unrelated session/environment variables are not forwarded to Wine
- lifecycle status, stop and journal-log retrieval
- reset is refused while an application is running

### HUI app registry

- persistent per-user registry at `$XDG_DATA_HOME/haven/compat/registry/`
- records addressed by SHA-256 of application ID instead of user-controlled filenames
- registry directory forced to mode `0700`
- registry files written as mode `0600`
- temp-file + file `fsync` + atomic `os.replace` + directory `fsync`
- schema and identity validation on every read
- symlinked/corrupt registry records fail closed
- unregister and state deletion are separate operations

### HUI broker daemon

- unprivileged per-user Unix-domain socket broker
- default socket: `$XDG_RUNTIME_DIR/haven/compat.sock`
- daemon refuses to run as root
- Linux `SO_PEERCRED` verification requires the client UID to match the daemon UID
- socket mode `0600`; parent mode `0700`
- socket path must remain below the caller-owned `XDG_RUNTIME_DIR`
- existing non-socket/non-owned paths are never overwritten
- 4-byte network-order length-prefixed UTF-8 JSON messages
- maximum request/response payload: 1 MiB
- strict method-specific parameter validation
- malformed requests receive bounded structured errors after peer authentication
- no command/shell execution method is exposed to HUI

## Not implemented or claimed

- Wine installation or downloading
- a bundled Wine runtime
- WinBoat, Podman, Docker, QEMU/KVM, Windows installation, or FreeRDP orchestration
- X11 fallback
- PipeWire output-only mediation
- clipboard brokering
- Internet/LAN mediation
- USB/smartcard/camera/microphone passthrough
- application compatibility claims
- approved-VM runtime proof
- installed/package-managed `haven-compatd` systemd unit

## Manifest example

```json
{
  "id": "example.app",
  "displayName": "Example",
  "backend": "wine",
  "runtime": "wine-11.0",
  "entrypoint": "C:\\Program Files\\Example\\Example.exe",
  "network": "none",
  "clipboard": false,
  "audioOutput": false,
  "microphone": false,
  "gpu": "none",
  "mounts": []
}
```

The parser reserves `internet` and `lan` network values for the future HUI contract, but slice 1 rejects both. No network access is advertised or granted until the backend can enforce the requested network scope rather than sharing the host network namespace.

User-selected file/folder grants must target a child beneath `/mnt/haven-share/`. Host paths are resolved before launch, so a symlink cannot be used to bypass the sensitive-path checks.

## Runtime layout

Managed Wine runtimes:

`$XDG_DATA_HOME/haven/compat/wine/runtimes/<runtime>/bin/wine`

Per-app Wine state:

`$XDG_DATA_HOME/haven/compat/wine/apps/<app-id>/`

HUI registry:

`$XDG_DATA_HOME/haven/compat/registry/<sha256(app-id)>.json`

HUI daemon socket:

`$XDG_RUNTIME_DIR/haven/compat.sock`

The broker never installs a missing runtime and never falls back to an unsandboxed system Wine executable.

## Daemon methods

The daemon exposes only the following methods:

- `capabilities`
- `health`
- `listApps`
- `registerApp`
- `unregisterApp`
- `launch`
- `status`
- `stop`
- `logs`
- `reset`

Operational methods use a registered application ID. `registerApp` is the only normal HUI entry point accepting a full manifest object.

Example request payload before framing:

```json
{"method":"launch","params":{"id":"example.app"}}
```

Successful response:

```json
{"ok":true,"result":{"unit":"haven-compat-...service","running":true}}
```

Errors are returned as:

```json
{"ok":false,"error":{"code":"backend_error","message":"..."}}
```

The framing is a four-byte unsigned big-endian payload length followed by exactly that many UTF-8 JSON bytes.

## CLI / development boundary

From the repository root:

```sh
python3 -m compatibility.wine.haven_compat.cli audit
python3 -m compatibility.wine.haven_compat.cli capabilities
python3 -m compatibility.wine.haven_compat.cli health
python3 -m compatibility.wine.haven_compat.cli daemon
python3 -m compatibility.wine.haven_compat.cli register path/to/manifest.json
python3 -m compatibility.wine.haven_compat.cli list-apps
python3 -m compatibility.wine.haven_compat.cli launch-app example.app
python3 -m compatibility.wine.haven_compat.cli status-app example.app
python3 -m compatibility.wine.haven_compat.cli stop-app example.app
python3 -m compatibility.wine.haven_compat.cli logs-app example.app --lines 200
python3 -m compatibility.wine.haven_compat.cli reset-app example.app
python3 -m compatibility.wine.haven_compat.cli unregister example.app
python3 -m compatibility.wine.haven_compat.cli unregister example.app --delete-state
```

Manifest-path `plan`, `launch`, `status`, `stop`, `logs`, and `reset` commands remain development interfaces. Normal HUI operation should use the registry and daemon ID-based methods.

## Read-only environment audit

`audit` checks, without installing or enabling anything:

- Linux/x86-64 target
- Bubblewrap
- managed Wine runtimes, including whether each `bin/wine` is executable and
  the reported `wine --version`
- Wayland socket
- user systemd tools and manager reachability
- render nodes
- PipeWire presence
- user-namespace kernel settings
- KVM usability
- Podman/Docker and FreeRDP presence for future WinBoat work

Passing preflight is not runtime proof. A discovered but non-executable Wine
file does not satisfy the managed-runtime prerequisite and is rejected before
the broker builds a launch plan.

## Tests

The tests are dependency-free:

```sh
python3 -m unittest discover -s tests -v
```

They cover manifest policy, sandbox-plan construction, lifecycle command construction, registry persistence/integrity, capability/preflight evaluation and same-user daemon protocol behavior. Passing tests do not prove that Wine applications run on the approved HavenOS/CakeOS VM.
