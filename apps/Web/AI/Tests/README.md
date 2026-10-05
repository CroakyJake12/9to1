# Separate ordinary Chat SAME10 source proposal

All tests are NOT_RUN. Root is the sole integrator. No shared CI driver, workflow,
owner source or production adapter was changed by this authoring step.

Root mapping:
- run-chat-native.py -> apps/Web/Tests/ci/run-chat-native.py
- team-b-chat-native.yml -> .github/workflows/team-b-chat-native.yml
- ChatRead.Tests.csproj, ChatReadControls10.cs, source-pins.json -> apps/Web/AI/Tests/
- Fixtures/Original/ChatSpaceBrowserReadAdapter.cs -> apps/Web/AI/Tests/Fixtures/Original/

The original snapshot is exactly 4736B/5262d6add2e0ee763b4bce2d4001c912f99a904440b7908c239009c58fa51689,
received directly from the then-current B-owned adapter before root adoption. Both variants
compile identical SAME10 controls and the actual Spaces/CUI owner project references.
Current expects the reviewed b34569d4 adapter. No owner DTO/backend/model is copied or substituted.
The default compile removal of AI/**/*.cs in the Web parent must remain, so controls/snapshot
cannot be silently compiled into the browser application. Root should inspect current parent exclusion.

Commands after root adoption, from exact clean repository cwd:

python3 apps/Web/Tests/ci/run-chat-native.py --variant original --expected-commit <actual40hex> --output <freshOutsideCheckout>
python3 apps/Web/Tests/ci/run-chat-native.py --variant current --expected-commit <sameActual40hex> --output <differentFreshOutsideCheckout>

This is an ordinary source-built SDK10.0.401/Linux-x64 compile, not the old reused-DLL probe.
The standalone driver imports the exact a57 maintained Commands class only at future execution;
no common suite registry/driver hash or other suite workflow changes are required. Both variants
restore/build a fresh actual graph under their own artifacts and NuGet/CLI/TMP directories.
Actual checkout HEAD is compared with independently supplied github.sha; full tracked tree and
recursive native gitlink status are logged, source pins and git diff checked after failure too.
Only pinned canonical XamlX and Avalonia.DBus gitlinks are initialized; no guessed dependency pins.
Each native operation uses Commands' real 120s wall bound, 8MiB full log bound, held pidfd/birth
custody and full EOF/drain checks, within its 26min overall command budget and workflow30min.
These CI runner bounds are not a local disk quota or proof of hardware/provider cancellation.

The original native exit1 is preserved in commands.json and native.log. A wrapper exit0 means
EXPECTED_RED_CONTROL only: exactly nine PASS plus one denied-refresh FAIL, all ten unique names,
no timeout/skips/unrun, exact intended InvalidOperationException assertion, normal original exit1,
zero forced signals, all held births gone and final ECHILD. Any other outcome is FAIL. Current
requires ten PASS and normal native exit0. Unexpected original success is a control-integrity failure.

The fixtures use scripted IChatSpaceBackend with genuine owner controller/CUI/records. They do not
prove real provider/access enforcement, inference, durable zero-effects, browser UI, specialized CUI
controls or full Chat parity. Inventory and attachment lists start empty; their nonempty denial case
is not independently exercised. The actual production backend may initialize root branches on reads;
its authorization/operation/receipt closure is a separate owner prerequisite. All historical nine-control
proposals and source/read reviews remain distinct and unexecuted, not relabeled as these ten.

Upload diagnostics only: full command logs, result/custody metadata and source/generated binary hashes.
Never upload NuGet, CLI/TMP caches, generated binaries, fixture-data or account/provider credentials.

Source02 adds exact actual embedded ChatSpace.cui, resource loader and parent Web project pins.
Source01 packet remains immutable; same ten controls/workflow/adapter bodies are unchanged.
