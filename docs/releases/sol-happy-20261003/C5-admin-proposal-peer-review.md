# C5 bounded Admin proposal peer review

C5 found no actionable bounded regression in key proposal `2ee0365b7175a8b8dcf8160bc74185c9c887a5d9`, including every regression addition in parent `3b9452e1`, or driver proposal `a93987b6d5286fed4e10bbdfda16f8f0cf763a6d`. This is source proposal acknowledgement; adoption and runtime acceptance remain separate.

The key change rejects `char.IsControl` before persistence through the shared lifecycle validator, matching persisted role/job read guards. The 31 added regression lines use the maintained actual Accounts service, real state-file bytes, original authenticated session and restart. Four controls across four operations exercise 16 refusals, asserting no canonical or audit-state changes and readable original organisation and pending job after restart. The original 12 role/job scenarios remain present.

Reviewed raw actual full Accounts Debug logs: fresh detached regression-only baseline builds successfully and fails with exit -6 at the first CR UpdateRole case expecting ArgumentException; the fixed build and entire Accounts console run exit 0, including original markers and the new cohort. Independently checked three retained physical DLL/PDB pairs for each baseline and fixed build, pair identities, and all 59 document hashes per build against immutable tracked source or retained generated files. Build/run original-session seals report drained true, with no primary or cleanup errors. The first baseline output overwrite is qualified in C3 evidence; fresh detached negative outputs are retained.

Read the complete original driver and independently established proposed bytes equal the original with only `os.execv` replaced by `os.execvp`. This resolves bare `dotnet` from configured PATH while preserving argv and the original handshake, creator pidfd, session, drains, tree/dependency, physical PDB and authentication assertions. The actual imported guard probe reaches SDK 10.0.401 and exits 0 with the original session drain true. The original full driver failure before SDK execution remains retained. This probe does not establish full corrected-driver success.

No runtime checks were rerun by C5. No source, root release or coordination edits were made. Root must issue the external new-cut pin before the corrected driver-only full successor; key-fix adoption stays separate. No broader provider, billing, production or full SDK acceptance is claimed.

[Machine-readable source bindings and hashed evidence](C5-admin-proposal-peer-review.json).
