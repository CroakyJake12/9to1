# Shared CUI interaction primitives: current qualification

Status: scoped implementation completed and component tests passed; independent review is separate. Full Spaces acceptance remains incomplete. Source is the preserved pending working tree on HEAD `0e5aa3b51eb290e98b520b221672473ebbd27932`, not a clean committed product artifact.

The retained RevealWith implementation uses the canonical CUI property cascade and exact authored ancestry. It preserves layout, controls, keyboard focus and action identity while removing the resting strip from pointer hit testing. Dynamic conditional/repeated insertion resolves against the attached authored root. Independent child surface slots retain their own loader and retirement authority. Disposal removes only this reveal's subscriptions and overlay; full-loader retirement also releases its authored projection.

This pass added an actual mouse trajectory through the authored body-to-action gap, followed by a physical click on the same retained Copy control. The shared contract requires a continuous authored hit region, such as Background="Transparent", on owners with padding/gaps. No application-specific selector, dispatch shortcut, duplicate action or sample product state was added. Product markup/captures remain owned by the separate Spaces lane.

Review also reproduced a ChoicePrompt lifetime defect: an ExecutionContext copied during a callback kept the ambient prompt marker after the original callback Task completed, so cleanup was incorrectly rejected as a live self-join. Close now tests both the actual retained operation's incomplete state and the ambient marker. One joined regression asserts live self-join refusal before admission, actual callback completion, then original/idempotent close from that completed captured context.

The existing prompt focus correction remains: selecting an option can disable its button and clear focus before close. Focus restoration is allowed only while this exact prompt still owns the overlay, and does not steal focus from a different surviving control or clear a replacement overlay.

Async test custody was inspected against the actual source-vendored HeadlessUnitTestSession overloads. Reveal/ChoicePrompt/OriginalSurface async cases already use explicit Dispatch<bool> and a returned result. One older ThemeRecovery worker-thread case lacked a result; it now uses the same fully awaited overload. Four other helpers already returned an integer and were not the same defect: their calls are now explicit Dispatch<int>, with bodies/assertions unchanged. Earlier evidence is not retrospectively reclassified as passing assertions it did not execute.

Evidence:

- `cui-reveal-joined-tests-4.log`: 27 passed, including the new real pointer-gap trajectory before the subsequent prompt correction.
- `cui-prompt-context-before.log`: test harness compile rejection for an incorrect Assert.Throws overload. This is a test authoring error, not product reproduction.
- `cui-prompt-context-before-2.log`: corrected test compiled, then failed on the actual stale-context CloseAsync refusal. One failed case retained.
- `cui-reveal-joined-tests-5.log`: 31 passed / 0 failed after the prompt correction and explicit test awaiting.
- `cui-runtime-full-joined-1.log`: all 335 Runtime cases passed / 0 failed / 0 skipped against those same binaries with --no-build. This includes the focused cases; counts are not additive.
- No warning/error diagnostics in the successful focused build log. Existing dependency assets were reused with --no-restore; no package updates or vulnerability-audit claim.

The adjacent `cui-runtime-joined-20261013-manifest.json` records source, project/package-assets, source-vendored headless API, all test-directory DLL and log hashes. The adjacent patch includes both tracked changes and the new Reveal implementation/test files. Build: Linux headless, .NET SDK10.0.401, Release/net10.0/x64, serialized MSBuild.

Rules consulted: shared AGENTS.md, HAVEN_UI_RULES.md, docs/ARCHITECTURE_RULES.md, docs/VALIDATION_RULES.md, docs/architecture/system-map.md, docs/development/testing.md and docs/ui/building-ui.md. Current explicit CUI specification governs the authoring and theme ownership over historical HavenUI host conventions. The canonical building-ui documentation now records exact hit-region, original-choice lifetime and fully awaited test contracts.

Limits: synthetic routed-touch exercises event semantics rather than real device hit testing. Screen-reader output, Windows/Android device operation, actual account/provider services and the current product's complete visual acceptance are not established by this component suite. No raw fixture was delivered, and this lane performed no commit, push, deployment or product capture. The serial .NET slot was released directly to the Spaces product-layout lane after final hashes were captured.
