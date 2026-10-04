# Original package dependency lifecycle

This proposal extends the canonical Home package registry and installed-session admission with one dependency planner and continuing lifecycle run. It depends on the Windows Home Foundation commit `0c014b5a0f552911ebd834c3fac78e0f3966bdc0`.

The planner uses the exact privately issued artifact selections and a detached snapshot of `home.packages.registry`. The supplied Home package and every mandatory shared core are dependency roots. Compatible installed dependencies can be reused only after original current installation observations; missing or incompatible selections refuse. Optional feature installation requires explicit consent, and cancellation leaves the original work unchanged.

The registered policy binds each selection to its descriptor's component class: `mandatory-shared-core`, `optional-app`, `app-required-dependency` or `optional-feature-dependency`. It detaches supported actions and enforces SemVer 2.0.0 precedence with inclusive minimum and exclusive maximum bounds. Ambiguous or unsupported version syntax refuses.

Install, update, repair, rollback and uninstall share the original admission, manual permission broker and device registry. Mandatory cores, dependencies still used by installed packages, and unresolved original operations cannot be removed. Rollback requires a retained valid predecessor. Each effect dispatches once; interrupted or ambiguous effects remain query-only, and known outcomes survive acknowledgement or independent audit failures.

The composition must supply one device-wide canonical store/database, the original installed Home session and broker, authenticated artifact/publisher evidence, supported platform/ABI and release identities, a root lifecycle adapter and non-reentrant current guards. Missing inputs refuse. The per-profile permission store is separate from the device registry. No broker or profile read may occur under the device writer.

When Home is absent on Windows, a separate genuine bootstrap owner must acquire and install the verified Home dependency before an ordinary installed Home session can be used. This change does not supply that owner, a protected Windows installed-peer verifier, an approved signing policy, an installer executable or a release declaration. It adds no default activation or parallel registry.

## Validation

The proposal contains 23 test methods representing 45 authored cases; they have not been compiled or run. All prior assertions are retained. The pure planner/policy suite contains 34 cases. The eleven original device-owner cases use genuine Windows named pipes or Linux Unix sockets, an isolated File store and manual approval, with explicitly scripted installation/artifact/root evidence. Windows acquisition retains the original pipe accept/connect tasks, observed PID/SID and same held Home lease; partial-failure cleanup independently drains those tasks before disposing the pipes.

Run the pure suite on a supported .NET SDK:

```sh
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --filter FullyQualifiedName~HomePackageOriginalDependencyLifecycleTests
```

Run the original device-owner suite on Windows or Linux:

```sh
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --filter FullyQualifiedName~HomePackageOriginalDeviceOwnerTests
```

These fixtures do not establish protected publisher verification, administrator consent, a native installed release or a clean-machine Windows journey. Full owning compilation, physical PE/PDB source mapping, complete restore/reference metadata, actual Windows bootstrap/install/update/recovery/rollback/uninstall journeys and independent original process/channel/task drains remain required.
