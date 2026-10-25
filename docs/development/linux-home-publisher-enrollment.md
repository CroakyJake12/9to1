# Linux Home publisher enrollment

The maintained ARM64 package path is
`.github/workflows/havenos-linux-package.yml` and
`.github/scripts/native16-arm64/build-native-arm64.py`. Its current deb/hash
receipt is not a Home publisher enrollment or a signed Home package catalogue.

The installed Desktop executable now has an explicit administrative enrollment
entry. It runs before GUI initialization and writes the existing canonical
`HomePackageOriginalPublisherEnrollmentRecord` through `HomePackageDatabase`.
Ordinary startup does not invoke it. No elevation, key generation, private-key
read, package activation or service start occurs in this command.

## Required signer inputs

The installer must receive these genuine externally supplied inputs:

- A public DER code-signing certificate, with digital-signature usage, a current
  validity period and an RSA key of 3072–8192 bits. Its SHA256 is the SHA256 of
  the exact DER bytes. The certificate is selected explicitly by the administrator;
  supplying a certificate file does not silently enroll it.
- The independently confirmed certificate SHA256 and exact signed-catalogue-file
  SHA256. Do not calculate a hash from an untrusted download and treat that alone
  as publisher authorization.
- An existing-format installer catalogue, containing signed Home, Root and
  selected package descriptors, wrapped in a signed envelope. All descriptors
  must declare the selected Linux ABI and the same issuer.
- The explicit issuer ID, ABI, selected package IDs and an operation UUID.

The outer envelope uses the existing descriptor envelope fields:
`schemaVersion: 1`, `issuerKeyId`, base64 `payload` and base64 `signature`.
Its payload is the exact UTF8 JSON of the maintained installer `Catalogue` DTO:
`schemaVersion`, `catalogueRevision`, `issuerKeyId`, `homePackageId`,
`rootPackageId`, `mandatoryPackageIds` and `packages`. Each package contains
`signedDescriptorBase64` and `descriptorPayloadBase64`, with the maintained
`HomePackageArtifactDescriptor` payload. Both the outer catalogue and every
descriptor use RSA-PSS/SHA256 with a SHA256-sized salt and the same selected
public certificate. No signer or catalogue is bundled or fabricated by this entry.

## Installer invocation

The canonical state directory must already exist, be root-owned mode0700 and
have root-owned protected ancestors. Root-owned sticky ancestors such as `/tmp`
are accepted only with root-owned existing children. Existing state and lock
files must be root-owned mode0600 regular single-link files. Symlinks and
user-controlled writable ancestors are refused. The command does not adopt or
chmod existing state paths.

An administrator may prepare the intended state directory separately:

```sh
sudo install -d -o root -g root -m 0700 /var/lib/9to1/Home
```

Replace every uppercase operand below with its actual reviewed value. Package
IDs come from the signed catalogue; they are not inferred from display names.

```sh
sudo /opt/9to1-preview/Haven --home-enroll-publisher \
  --machine-state /var/lib/9to1/Home/home-core-state.json \
  --publisher-certificate /absolute/path/publisher.cer \
  --publisher-sha256 INDEPENDENTLY_CONFIRMED_CERTIFICATE_SHA256 \
  --signed-catalogue /absolute/path/signed-catalogue.json \
  --catalogue-sha256 INDEPENDENTLY_CONFIRMED_SIGNED_CATALOGUE_FILE_SHA256 \
  --issuer-key-id EXPLICIT_ISSUER_ID \
  --abi ABI_FROM_SIGNED_DESCRIPTORS \
  --operation-id OPERATION_UUID \
  --select HOME_PACKAGE_ID_FROM_SIGNED_CATALOGUE \
  --select ROOT_PACKAGE_ID_FROM_SIGNED_CATALOGUE
```

Repeat `--select` for optional selected packages. All mandatory packages must be
selected. The command verifies all signatures before initializing the machine
profile, uses the actual operating-system profile and observed installer image,
and performs the existing guarded database CAS. Repeating the same publisher,
catalogue and selection returns the retained enrollment without rewriting it.
A different existing enrollment is preserved for explicit owning recovery.
On a failed or lost acknowledgement, retain the original operation UUID and inspect
the canonical record before retrying. A failure does not establish that no profile
or enrollment write occurred. A known commit followed by failed final currentness
has the explicit `PublisherEnrollmentCommitUnconfirmed` error and exit code2;
the recorded operation remains available for recovery.

Success prints a JSON receipt with certificate/catalogue hashes, canonical
registry revision and original enrollment operation ID. It explicitly reports
`installedPeerAdmission: NOT_ESTABLISHED` and `packageActivation: NOT_PERFORMED`.
Retain the public certificate, exact signed input and receipt in the existing
private installer/evidence custody. Never place a private signing key in Git.

## Remaining user workflow

This supplies the initial Linux enrollment producer consumed by
`LinuxHomePackageOriginalArtifactProvider`; it does not complete Home Apps
Install, Update, Repair or Uninstall. Those actions still require the genuine
Linux authenticated Root mutation service, protected package activation and
recorded controlled application launches. A listening Home socket, enrollment
row, valid signature or compiled package does not independently establish native
caller or installed-application authority. Full independent/native acceptance
remains paused; this command has not been run against production state here.

## Explicit initial code activation after enrollment

`--home-activate-bootstrap` consumes the SAME existing canonical enrollment and
explicit public certificate/catalogue inputs. It cannot enroll a publisher or
initialize a missing machine profile. This command handles an initial empty
package/journal only. It does not perform updates, Root service registration,
authenticated peer enrollment, controlled launch or runtime Ready.

The administrative packager first prepares a root-owned, mode0700 stage below an
existing protected root-owned mode0755 code parent. All ancestors must be
root-owned and protected from unprivileged replacement. The code parent must be
disjoint from the private canonical state directory, public signing inputs and
plan. Nothing here extracts an archive, runs a script, adopts a directory or
changes the parent/state permissions. Existing `active` entries are preserved.

The explicit plan is bounded JSON, separate from the code parent:

```json
{
  "schemaVersion": 1,
  "packages": [
    { "packageId": "home.pkg", "relativeRoot": "home", "signedReceiptRelativePath": "receipt.json", "completePayloadRelativePath": "payload.bin" },
    { "packageId": "root.pkg", "relativeRoot": "root", "signedReceiptRelativePath": "receipt.json", "completePayloadRelativePath": "payload.bin" }
  ]
}
```

Its package IDs must equal the enrolled selection; every mandatory package and
signed dependency must be selected. Component classes use the existing lifecycle
values; Home and Root must be `mandatory-shared-core`. Versions and dependency
bounds use the existing strict SemVer policy (`1.0.0`, not opaque `1`). Cycles,
missing dependencies and unsupported classes are refused before effects.

Each descriptor's `signedInstallationReceiptSha256` hashes its exact receipt
envelope, signed with the already enrolled publisher's RSA-PSS/SHA256 key through
the same version1 envelope used for catalogue/descriptors. Its receipt payload is:

```json
{
  "schemaVersion": 1,
  "packageId": "home.pkg", "appId": "home", "version": "1.0.0",
  "platform": "linux", "abi": "arm64", "entrypointRelativePath": "app",
  "files": [ { "relativePath": "app", "bytes": 1234, "sha256": "EXACT_64_HEX_SHA256" } ]
}
```

`files` covers every extracted runtime file, excluding receipt and complete
payload themselves to avoid self-hash cycles. Descriptor `payloadSha256` and
`payloadBytes` bind the separate complete payload artifact. The complete physical
cohort is runtime files plus the receipt plus the full payload. Extra entries,
symlinks, hardlinks, foreign ownership and writable foreign permissions are
refused using retained descriptors/openat2/statx. Every executable runtime file
must be native ELF64 for the signed x64/arm64 ABI; scripts and unknown ABIs are
refused. Entrypoints must already have owner-execute permission. Receipt and full
payload artifacts must be non-executable. There are at most4096 complete files
and8GiB across the whole bundle.

After checking every signature and every actual byte, activation reserves one
entry per package in the existing canonical journal, retaining the original
operation ID, stage/parent/layout, plan hash and signed receipt/payload hashes.
Only this explicit verified machine-code mode makes code directories0755 and
signed artifacts0644 (native executable files0755). The ordinary physical store's
private0700 default is unchanged. No keys, state, profile data or credentials are
made public. One `renameat2(RENAME_NOREPLACE)` publishes the complete stage as
`active`; the SAME canonical metadata CAS then records actual activated paths and
settles the journal. Packages explicitly retain `HomeServiceUnavailable`.

Example invocation for an administrator/packager with real signing inputs (the
example does not create trust or provide a signer):

```sh
/absolute/path/to/Haven.Desktop --home-activate-bootstrap \
  --machine-state /absolute/private-machine-state/canonical.json \
  --publisher-certificate /absolute/inputs/publisher-public.cer \
  --publisher-sha256 EXACT_DER_CERTIFICATE_SHA256 \
  --signed-catalogue /absolute/inputs/catalogue.signed.json \
  --catalogue-sha256 EXACT_SIGNED_ENVELOPE_SHA256 \
  --issuer-key-id OWNER_CONFIGURED_ISSUER --abi arm64 \
  --operation-id ORIGINAL_OPERATION_UUID \
  --select home.pkg --select root.pkg \
  --activation-parent /absolute/root-owned-code-parent \
  --stage-leaf prepared --plan /absolute/inputs/plan.json
```

Exit0 reports `LinuxBootstrapCodeActivatedRuntimeUnavailable`, never Ready.
Exit2 (`LinuxBootstrapActivationUnconfirmed`) means a canonical journal write
was attempted or publication occurred but its complete outcome could not be
confirmed. Retain its original operation ID, journal, stage/active directories
and administrative inputs for owning recovery; do not delete artifacts or
redispatch it as a fresh install. Existing package/journal state is deliberately
refused by this initial-bootstrap command. Exit1 occurs before journal/effects.
Cross-store recovery/update/uninstall and actual Root service/installed-peer/
controlled-launch composition remain subsequent implementation work.

The isolated tests use genuine ELF files, real filesystem operations, actual
canonical store/OS profile and ephemeral TEST-ONLY signers in memory. They write
public DER only and never modify an OS trust store, start a service, execute the
copied binaries or establish production publisher/installed-peer authority.

## Recover an already published initial bundle

Use `--home-recover-bootstrap` with the same options, original operation UUID,
original stage leaf, exact retained plan and original signing inputs. Recovery
checks the CURRENT canonical enrollment and retained per-package journal binding,
then independently re-verifies every signed receipt, payload and runtime file in
`active`, including its exact published permissions. It never chmods or renames
anything and never starts a service/process. A complete exact bundle with the
original `Validated` journal can settle the canonical metadata CAS. A complete
already-settled bundle returns `alreadySettled: true` without a state rewrite;
this covers loss of an actual successful settlement acknowledgement.

An absent, altered or redirected bundle, changed plan/layout, different operation,
mixed journal state or changed canonical package row is refused while preserving
all existing state/artifacts. Recovery never adopts another bundle or repeats the
initial effect. Exit2 retains the original operation for further owning recovery.
Exit0 reports `LinuxBootstrapRecoverySettledRuntimeUnavailable`, with all runtime
and installed-peer limitations unchanged. Existing malformed/missing OS-profile
state is preserved, rather than being initialized as a new administrative profile.

Focused failure controls use the existing actual-store persisted-write observer
to lose a real successful settlement acknowledgement and a one-shot failing
principal reader that otherwise delegates to the actual OS source. The second
control fails the real profile commit guard after an actual filesystem publication,
leaving the actual canonical journal pending. Neither fixture invents a successful
actor, publisher, store result or controlled launch.

## Registered Linux Root cold service implementation

The explicit `--home-root-service` entry is separate from ordinary GUI startup.
It consumes the same activation options above, plus `--control-directory` for an
existing protected root-owned directory with user traversal (normally0711), and
requires the already-settled canonical bootstrap. Startup never performs recovery,
enrollment, installation or registration. It requires the actual process's
`sd_pid_get_unit` to be `9to1-root.service`, effectiveUID0, and its actual
`/proc/self/exe` inode to match the signed activated Root entrypoint.

The active local desktop user comes from actual logind session APIs, never HOME,
USER, SUDO_UID, service arguments or caller JSON. Exactly one active, non-remote
`user` graphical session (x11/wayland) is supported; absent/ambiguous/unsuitable
sessions remain unavailable. The administrative machine profile remains Root's
profile. Home runs under the real desktop login UID and keeps its own existing
OS-local profile/lease; installation metadata never impersonates that actor.

The Root signed receipt must include `home-root-launcher`, a native0755 executable
built from `shared/native/linux/home-root-launcher.c`. The maintained ARM build
compiles it with CMake and copies it into the actual publish tree before shape
verification/provenance/packaging. The packager must include its exact hash in the
real externally signed Root runtime cohort. Merely copying the helper into a
preview package does not satisfy publisher admission. It is never setuid and is
never granted file capabilities.

The managed runtime retains the original verified code cohort through its child
and Root control lifetime. It starts the exact signed native supervisor with an
empty environment and fixed arguments derived only from the current logind user
and held signed Home entrypoint. The native supervisor owns fork/namespace/group/
UID changes outside the CLR, verifies the held image inode/device/size/timestamps,
uses openat2/fexecve (no shell/PATH), and permanently drops all real/effective/saved
UID/GID IDs and supplementary groups to the actual OS account. It disables core
dumps and new privileges, closes inherited descriptors, provides a bounded private
supervisor protocol, retains a pidfd and keeps exited child PIDs unreaped until
Root retirement. Root rechecks actual kernel peer UID/PID and current executable
before accepting the controlled Home connection.

A fresh user namespace owned by Root isolates the child's credential namespace
from ordinary host same-UID ptrace/process_vm access, including across exec's
dumpability reset. Namespace support and mapping must actually succeed; no weaker
launch fallback exists. The child remains the real user's host filesystem UID.
The service monitors the original active login and controlled process, revoking
the command lifetime if either changes. The fixed Root protocol currently supports
controlled Home hello, current package inspection and Home-lifetime checks. It
refuses unsupported mutations and does not accept caller-selected UID/path/argv.

No service is installed or started by this source change. A real protected unit
and real signed activation are still deployment prerequisites. The Home-side Linux
Root client/listening publication and selected application launch producer are the
next composition slice; the cold service does not advertise that these exist.
`LinuxRootOriginalHomeControlAccepted` describes only the actual child control
handshake, never Home Core or Root runtime Ready. A missing helper/session/unit or
uncompleted child protocol remains a visible failure/unavailable result.

The native CMake refusal test runs the helper with no original protocol arguments,
which refuses before UID/group/namespace/fork operations. Native compilation and
that refusal run require a separately admitted actual Root check. The current
source work has not executed a helper, changed privileges, registered/started a
service or established a production signing/enrollment/controlled-launch result.

## Linux Home child client composition

The controlled native child receives only `--home-linux-root-control` and its
original endpoint locator. Program consumes this locator before GUI parsing;
ordinary Home startup supplies none and retains its existing local path. The
locator is not authority. The client requires the connected actualUID0 peer's
SO_PEERPIDFD lifetime and actual `sd_pid_get_unit(peerPid)==9to1-root.service`.
Cross-UID `/proc/RootPID/exe` access is unavailable to an ordinary user, so this
specifically named physical service-lifetime observation claims no executable or
installed proof. The full executable pin remains mandatory for existing Core
accepted app/Home peers. The registered Root independently cold-verifies its own
signed executable/cohort and controls the actual Home child; no unknown Root or
ordinary process gains authority merely by using the locator/UID/socket.

That same client is bound once to the actual Home/store/OS-profile/index graph.
It connects before Home starts, observes the canonical selected package inventory
through Root, and checks that this process executes the exact protected Home
entrypoint. Home then starts its existing Linux process bootstrap and Core host.
Only after the real host/listener/lease has independently succeeded does the
client borrow its source-issued listening observation, get the existing installed
registry's Home ID, and publish the same actor/profile/lease/socket tuple to Root.
Role verification checks the current actual actor, original process/listener,
maintained registry row and live Root acknowledgement before and after awaits.
No fake installed GUID, alternate profile, second listener or session issuer is
created. Root retires on unsolicited controlled Home loss, retaining actual child
status and all cleanup failures; explicit registered service retirement is distinct.

The implemented peer verifier admits this actual controlled Home only. Unknown
selected applications remain denied until Root owns their real controlled launch
producer and Home's original approval/write path is connected. Linux app launch,
service unit installation/deployment, real signing inputs and native integration
remain outstanding; these source changes do not claim runtime Ready or100%.
Focused negative controls use actual failed socket connections and omitted source
callbacks, preserving foreign endpoint files and creating no Home identity.
