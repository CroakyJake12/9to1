# Windows personal Task recovery custody

The Windows64 backend belongs to the existing `NativePersonalTaskRecoveryStore` and
`SqliteTaskRunColdRecoveryJournal`. The environment selector remains explicit:
`HAVEN_NATIVE_PERSONAL_TASK_COLD_RECOVERY=1` requests configuration, `0` or absence
keeps ordinary Task behavior, and any other value refuses configuration. A request or
configured descriptor never certifies protection, recovery, readiness, or permission.

The store captures the genuine effective Windows token SID, retains every actual local
path ancestor and the original directory/database handles, and validates current SID,
full volume/128-bit file identity, namespace binding, owner, kind, single-link files,
and bounded handle-bound DACL observations. Remote, reparse, alternate-stream,
case-sensitive, unsupported filesystem/API, and changed-custody inputs fail closed.
Existing descriptors are observed and pinned. They are never adopted or repaired.

The current SID must own each protected object. Root, database and authentication key
require an actual protected DACL with no inherited ACEs. Explicit ACEs permit only
simple OI/CI inheritance flags, basic allow/deny type, complete counted binary SID,
and concrete file rights within `0x001f01ff`; generic, conditional, object, callback,
inherit-only and no-propagate forms refuse. Bounded supported DACL ACEs may name
that SID, SYSTEM (`S-1-5-18`), and BUILTIN Administrators (`S-1-5-32-544`) only. This
explicitly trusts privileged Windows administrators and SYSTEM; it does not promise
protection from those OS principals. Broad trustees, null/absent DACLs, malformed or
unsupported ACE layouts, and changed security observations refuse. OS file privacy
is neither a Home resource grant nor CAKE/account, Task, or execution authority.

The existing journal obtains its 32-byte authentication key from this same protected
store. Missing-key reads refuse without creating provenance. Authorized creation uses
a successful actual SAME-parent namespace/storage probe before generating secret bytes
or creating any stage, CSPRNG bytes immediately under owning try/finally custody, an
exclusive private stage under the held directory, actual data and
metadata/storage synchronization, parent-relative create-only publication, and actual
namespace/storage synchronization. The parent is synchronized after stage creation,
probed again before publication, synchronized after successful rename or owned-stage
delete request, and synchronized separately after final owning-stage close. Only the
genuine kernel name-collision result can
select a concurrent winner. An unsupported or failed synchronization remains a fault;
file-only flush or rename success is not a namespace durability receipt. The final
key remains held without write/delete sharing while exact size, identity, version,
security, namespace, and principal are rechecked. Transient buffers are zeroed and
cleanup addresses the owned stage handle. Stage-name/path/list initialization and all
later resource failures remain inside the generated buffer's owning finally. Original
fault and cleanup siblings remain inspectable; a post-publication synchronization
fault never deletes the published key or becomes a no-effect receipt. Keys never enter
receipts, logs, or snapshots, including owning-control failure messages.

SQLite WAL/SHM/journal companions may carry the same explicit protected policy or an
unprotected DACL whose entire ordered policy is the exact file inheritance projection
of the same retained protected private parent. Only parent OI-eligible ACEs project;
child flags must be exactly inherited (`0x10`), with identical concrete mask, basic
type, trustee and counted raw SID. Explicit/inherited mixtures, empty projections,
foreign/default-token/broad policy and unsupported flags refuse. The parent's actual
identity and whole protected security fingerprint are checked immediately before and
after each parent-relative companion open, including missing/refused observations;
the child is held without delete sharing during identity/path/security checks.

SQLite database/WAL/SHM/journal files use the same protected namespace. Content versions
may legitimately change, while database physical identity and privacy remain pinned.
The existing journal still owns capsule HMAC, revisions, fresh activation, canonical
Task actor/context, and current source reauthorization. Its Linux and journal bodies
remain preserved; this backend supplies no alternate recovery executor or claim issuer.

Source controls are separate from actual Windows evidence. Compiler, genuine Windows
ACL/sharing/SQLite/storage synchronization, fresh subprocess recovery, GUI/login,
manual permissions, publisher/signing/install, and clean-exit workflow verification
remain unrun until the owning host records those actual operations.


Successor02 corrects the pre-effect actual parent probe and generated-buffer custody.
Role03 adds the protected/non-inherited root/database/key split and exact supported
companion inheritance projection. This policy concerns current effective privacy
under the same held parent, not historical creation provenance. Defaulted is
observed and pinned metadata, neither a privacy substitute nor a blanket refusal.
Existing stores are never repaired, adopted, or reset to satisfy the stricter policy.
A NEW protected-root/explicit-database setup must precede SQLite; ordinary inherited
Windows startup paths cannot be treated as that setup observation.

`--create-native-private-store` explicitly creates only the CURRENT existing AppPaths
mapping (`HAVEN_DATA_DIR`, or ApplicationData/Haven) and its fixed direct `haven.db`.
It accepts exactly that one argument; adding `--help` is informational with no effects.
No alternative parent/path/name, environment mutation, existing-store adoption,
descriptor repair, reset, import, schema migration or key creation occurs. The public
Windows-only boundary is `WindowsNativePersonalTaskStoreSetup.CreateNewConfiguredPersonalStore()`;
its returned normalized path is a nonsecret observation, with no protection/readiness
or Home/CAKE/Task/Run/session/execution authority.

The actual current Windows64 capabilities, local parent/ancestor handles and current
SID are required. An actual SAME-parent Flags0 namespace/storage probe precedes all
create effects. The NEW root uses CreateNew and the frozen protected actual-SID OICI
descriptor; its actual kind/identity/path/owner/explicit private DACL are checked.
After parent synchronization, the SAME NEW root is probed before the explicit NEW
zero-length database is created. Database storage, root namespace after create and
separately after database close, and original parent after root close are synchronized.
Identity/security/principal/configured mapping are checked before and after effects;
original faults, owning closes and independent synchronization siblings stay inspectable.
A failed/unknown post-effect result retains observed partial state and never claims
rollback, manufactured durability or absence of outputs. Existing-name collisions
never open, truncate, adopt, repair or delete the existing object.

Any occurrence of the setup option in argv routes to the early exact single-option
validation; mixed or reordered arguments refuse before ordinary startup. The command
runs before Program's generic bootstrap try/catch and reports refusal or
partial/unknown outcomes to console only. Successful actual setup retains its exact
nonsecret path privately, removes the option and continues ordinary Avalonia startup
in the SAME process. Immediately before constructing AppPaths, the Windows Home owner
denies a changed configured mapping; immediately afterward, it checks the SAME actual
DataDirectory and DatabasePath before maintained Home/provider/SQLite registration.
Ordinary startup with no actual setup observation is unchanged. Receiving mismatch or
subsequent startup failure from this command uses console-only reporting, preventing
default-Haven bootstrap logging from creating an unrelated data root. These path
checks only deny mismatches; role03's actual current custody validation still owns
privacy admission. Setup changes neither the recovery selector nor its unverified
status, and a source or headless command observation does not prove GUI rendering.

Owning source controls include genuine NEW explicit descriptors before maintained
SQLite OpenAsync/WAL writes, root/database collision preservation, SAME readonly
parent pre-effect refusal, SAME readonly NEW root post-effect native sync refusal,
configuration drift before/after effects, same-process pre/post AppPaths mismatch,
and inspectable post-create/independent close-boundary fault siblings. Negative
callbacks never substitute a principal/native success or positive setup observation;
close-boundary callbacks do not qualify genuine native close-error reporting.
Compiler and all actual Windows controls, GUI/auth/install and clean workflow remain
UNRUN until the owning host records their actual execution.
