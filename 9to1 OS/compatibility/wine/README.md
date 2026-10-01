# HavenOS / CakeOS Windows compatibility broker

This directory contains the current broker substrate for the required unified
9to1-OS Wine/WinBoat/APK Compatibility Framework. Its implemented and missing
capabilities are recorded below; it is not a complete implementation of that
framework. Historical HavenOS names remain in surrounding internal interfaces.

The shared Application compatibility routing port uses Home's canonical
`InstalledApplicationReference` Guid/revision and an owning runtime observation.
It produces an immutable read-only framework proposal, rejects a changed Home
session or installed entrypoint, and preserves an ineligible preferred backend
instead of silently switching frameworks. Policy denial and unverified package
trust never produce a proposed backend. Its observations and proposals are not
execution permission or human approval. A production canonical runtime owner,
permission admission, installation manager and execution adapter are still
required; this port is not registered by the native shell. The Python registry
below remains a development runtime substrate, not a second canonical Home
application database or production installed-app authority.

## Unified package inspection and routing

The shared broker now exposes `listBackends`, `inspectPackage` and
`setPackageBackend` through its authenticated same-user daemon. The development
CLI exposes corresponding `list-backends`, `inspect-package PATH` and
`set-package-backend PATH FRAMEWORK` commands (`reset` removes an association).
These inspect and configure routing; they never install or execute a package.

Inspection uses DOS/PE headers and machine type for Windows executables, the
compound-file root Windows Installer CLSID for MSI databases, and a validated
Android manifest inside ZIP packages for APKs. Android binary XML string pools
in UTF-8 and UTF-16 are supported. Filename extensions do not establish format.
DLLs, malformed/ambiguous manifests, encrypted/oversized manifests and arbitrary
renamed files fail explicitly. MSI architecture remains unresolved and blocks
backend selection until a real installer metadata adapter supplies it.

APK associations use the stable `android:<package>` manifest identity, which
survives installer moves, renames and version changes. Windows packages without
verified application metadata use a clearly labelled content SHA-256 package
identity; it survives moves/renames but **does not establish application identity
across different installer versions**. This is not a claim of signed publisher
identity. Inspection currently marks every package `unverified`, including APKs
with signing records, because cryptographic publisher verification is not yet
implemented. Such packages never receive a selected execution backend.

Eligibility filters format, architecture/ABI, backend availability and managed
policy before priority/default selection. An unavailable or ineligible preferred
framework is reported explicitly; the broker does not silently switch it.
Preferences persist atomically under the per-user compatibility directory with
private directory/file modes. `wine`, `winboat` and `android` are typed backend
descriptors. Wine availability depends on actual prerequisite audit; WinBoat
and Android remain unavailable with explicit missing implementation reasons.

Administrator compatibility policy is read on every routing/registration/launch
operation from `/etc/9to1/compatibility-policy.json`. It must be a root-owned
regular file without group/other write access. Missing policy means unmanaged
defaults; malformed or unsafe policy fails closed. There is no user-facing
policy mutation method. Schema 1 supports `enabled`, `allowedBackends`,
`allowedIdentities` and `allowBackendPreferences`. Disabling compatibility or a
backend also blocks legacy manifest registration and launch plans below those
entry points. Application allowlists fail closed for legacy user-authored
manifests, because a caller-chosen manifest ID is not a verified installed
package identity. Existing applications may still be stopped for recovery.

This increment does not provide the required unified CUI installation flow,
desktop double-click/file associations, a Windows/Android runtime installation,
trusted package acquisition or installed-app identity bindings. The complete
Wine/WinBoat Framework remains incomplete until those workflows and real guest
runtime acceptance are implemented and verified.

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
