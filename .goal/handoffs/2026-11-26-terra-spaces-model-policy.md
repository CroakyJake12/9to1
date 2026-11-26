# Spaces model admission · 26 November 2026

- Owner: OpenCode session `ses_de8ff0ed5ffeJYSNxp34JqAHif`; base `698dfee71cfb524c39a6aafa12dfb54cf4b2459b` on local `main`.
- Narrow source scope: `9to1 Workspace/Spaces/Source/Chat/HavenChatSpaceBackend.cs` and `9to1 Workspace/Spaces/Tests/HavenChatSpaceBackendScopeTests.cs`. These files are identical at native candidate `91d36a7a29814f6153061c2260547baae4d97932`.
- Atlas `spaces-chat`; PICK-004/008, UIX-PICK-002, SPC-017/051. A model chosen before a Space policy change must not execute when its current provider, capability or category is ineligible. Explicit eligible alternatives remain usable.
- The native Build qualification owner retains `9to1 Workspace/Dev/Autonomous/**` and `9to1 Workspace/Dev/Tests/**`; no GPU, training, live inference, preview, shared CUI or profile changes.
- Coordination: published here for the existing workers; no receiving-session acknowledgement established. Avoid concurrent edits to these two paths until handoff.
- Status: scoped implementation complete on `main`; native candidate integration, independent review, and actual native acceptance remain open. Main has unrelated dirty work; stage only owned paths. The shared feature inventory is already dirty and is not owned by this session; its owner should incorporate this entry.

## What changed

Send and regenerate now resolve the same effective Space-filtered model inventory used by the picker and recheck policy after safety admission. Discovery re-resolves policy after awaiting the provider. Disabled categories, unsupported providers, missing/unknown capabilities and inherited category restrictions refuse before execution; a missing default does not prevent an explicitly selected eligible model. Regeneration rechecks before branch preparation and again before execution. If revocation occurs after branch preparation, that already-prepared branch is not rolled back.

These are action-time checks, not an atomic policy lease. `ChatSessionService` separately captures and revalidates original Space policy at its dispatch boundaries; this ticket's controlled backend tests do not establish that complete end-to-end path or revocation semantics for already-running work.

## Evidence

- Guarded CPU-only owning tests, on base `698dfee71` plus this patch and the pre-existing dirty checkout: `HavenChatSpaceBackendScopeTests|HavenChatSpaceBackendAcceptanceTests`, **70 passed, 0 failed, 0 skipped**; exit 0, 9.7 seconds, minimum available RAM 72,140 MiB, 12,000 MiB guard floor. Original log, guard receipt and copied TRX: `/home/jacob/.local/state/9to1-development/terra-20261126-spaces/green-2.*`.
- Corrected pre-fix regression: 8 failures, 1 positive control passed; `/home/jacob/.local/state/9to1-development/terra-20261126-spaces/red-valid.log` and guard receipt. Initial `red.log` used an invalid regeneration fixture; retain it but do not count its regeneration failures as proof. `green-1.log` exposed a changed archived-Space exception, fixed before `green-2`.
- `git diff --check` passed. No native launch, inference, profile changes or independent review was performed.
- The latest observed native branch `91d36a7a29814f6153061c2260547baae4d97932` had byte-identical versions of the two source/test files before this patch. Its owner must verify its current revision and integrate/retest deliberately; do not overwrite its active Dev work.
- A subsequent guarded `ChatSpaceControllerRoutingTests|ChatSpaceConcurrencyAcceptanceTests` run after source commit `e62fcd425` returned **30 passed, 3 failed, 0 skipped** (exit 1, 125.7 seconds, minimum RAM 66,411 MiB). The failures are `Cancelling_a_correction_restores_the_unsent_draft_without_changing_history`, `Navigating_away_and_back_keeps_the_correction_separate_from_the_saved_draft`, and `Failed_correction_retains_the_proposal_and_the_original_draft`. These tests are pre-existing uncommitted additions to `ChatSpaceConcurrencyAcceptanceTests.cs`, exercise their own controlled backend rather than the changed adapter, and expect draft restoration not present in this checkout's controller. A same-fixture baseline comparison was not run; do not classify them as a regression or pass. The native candidate has divergent controller code and a separately committed test expecting an empty draft on cancel; its owner must reconcile intent and verify. Raw evidence: `controllers.log`, `controllers.log.guard.json` and `controllers.trx` in the same evidence directory.

## Adapter and routing follow-up

The active ALORA evaluation script explicitly identifies its input as an ALORA custody checkpoint, **not** a PEFT adapter or merged GGUF. Dulche's existing `eng/dulche/service/model_formats.py` can register a matching PEFT LoRA directory, but marks its runtime backend unavailable; `controller.py` and `engines.py` refuse unsupported serving formats. Spaces' `HavenChatSpaceModelCatalog` currently reads `IOllamaClient`, and its Space policy filter identifies that catalogue as Ollama-only. Therefore neither registering a checkpoint nor selecting it in a picker establishes a usable Flash route.

After today's independent evaluation, the runtime/training owners need an exact checkpoint/base identity and verified compatible export or serving adapter, followed by CPU contract checks and a separately admitted real inference test. Preserve the verified fallback until then; do not guess conversion, load a model, change a service, or use ALORA's GPUs from this ticket.
