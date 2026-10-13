# Pending shared CUI / Chat review

Scope: read-only independent source and evidence review after P0 source checkpoint `0e5aa3b5`. The live checkpoint35 is still based on `0e24d109`; these pending UI changes are not attested as deployed. Exact reviewed hashes are in the adjacent JSON. No builds, data mutations, or provider calls were performed by this review.

## Concrete finding

**UIR-01 — completed copied callback context rejects original prompt cleanup (P2).** The original `CuiChoicePrompt.CloseAsync` compared only `ExecutingCallback.Value` with the prompt, so an `ExecutionContext` copied inside its callback remained prohibited even after the exact callback Task settled. This falsely refused real cleanup without admitting `OriginalClose`. The CUI owner reproduced this in `evidence/cui-prompt-context-before-2.log` and has bounded the guard to the actual retained incomplete `_operation`. The owner's corrected focused31/31 and full Runtime335/335 on the same binaries pass. This reviewer independently read the failing and passing logs, verified the bounded guard and checked every source/dependency/log/assembly hash in the manifest against disk. The tests were executed by the CUI owner, not rerun by this reviewer. Live callback self-join must continue to fail, and a completed copied context must join the same idempotent close.

## Source findings

The proposed RevealWith mechanism resolves a literal nearest authored ancestor within the same loader. Keyed repeated message instances receive separate helpers; retained authored containment supports inactive cells and later nested Repeat/conditional insertion. Independently owned original-surface slots are excluded. Removing a repeated subtree or disposing the loader detaches the helper and original root pointer listener. The helper changes CUI overlay opacity and hit eligibility, preserving layout, tab stops, control identity and real action dispatch. It does not add permission or re-enable a disabled product capability.

The product message owner now has an authored transparent background. This supplies the continuous body-to-gap-to-action pointer region that was missing in the rejected earlier visual run. The current helper maintains visibility while focus is inside the original message; touch latching never dispatches an action itself. Source inspection found no additional blocking defect in these boundaries. That is a bounded review result, not proof of all accessibility states.

Chat keeps the original message-ID action declarations and actual BrowserChatSession/Controller plus host availability projection. The new conversation icon is conditional on the actual conversation row kind; native canonical Chat uses the same shared icon resolver. Existing unsupported browser commands remain disabled. The typed IsIdle marker is set only after the original successful initialization read and is false for a model outage; presentation suppresses only that exact idle marker while preserving errors, recovery, dirty drafts, progress and model-selection acknowledgement.

The footer is authored once in the actual shared Chat document. Header, recent-row and icon changes use canonical CUI resources and preserve their existing owner commands. They do not supply reasoning, usage, Voice or question/answer data.

## Evidence and test critique

Historical visual10 records68/68 bounded checks through the real feature/session/controller and production host availability under deterministic test data and Skia. Its reviewed receipt includes physical pointer body/gap/action traversal, real hit target, keyboard Tab traversal, sibling isolation and unchanged geometry. It predates the mandatory labelled report/CLI isolation contract and is internal evidence only. Do not deliver its raw image as a live or accepted product. Fresh labelled visual11 now supplies8 cases/540 bounded worker checks, independently opened and inspected with the explicit outside-image banner. The current source and assemblies match; see the final appended register section and independent report.

The shared tests now use explicit `Dispatch<bool>` for asynchronous assertion bodies and join the original loader actions. The earlier Runtime10 async-overload evidence was insufficient; it is not carried forward as independent passing evidence. The prior26 and27 joined runs remain historical; final focused31 and full335 results now have a source/assembly-bound manifest and independent readback. The touch test raises routed touch events directly; it proves only the latching policy, not real device hit testing, scrolling, pen input, a screen reader or platform accessibility integration. Likewise Skia fixture checks do not prove the actual browser/WASM/native shell, account, storage, model or tool workflows.

## Authoritative requirements used

- Spaces companion, section1: Professional/Glow separation, coherent Montserrat and CUI ownership of keyboard/focus states. Section2: rail, contextual sidebar, collapse with preserved state, and distinct Space workflows.
- Companion section6: approximate70/280/80 proportions; user/assistant alignment; real lower-emphasis work; compact/expanded composer, Aa/emoji, canonical attachments and genuine question/answer/recovery. These are responsive reference proportions, not a fixed-window contract.
- Companion sections7–9 and SPUI-133–135: actual Voice/session integrations, floating Summary, real acceptance, reasoning effort and separate usage/context semantics. Later owner-approved decisions retain precedence.
- CUI Professional annex CUI-PRO-202–204: coherent vector icons, honest hit areas, visible interaction states, Montserrat/shared tokens, native-control exclusion, focus/caret preservation and variant/other-app regression gates.
- The footer wording and measured coordinates come from root's direct inspection of the owner's approved image1; exact image bytes are still unavailable. No new reference image was fabricated.

## Visual specification status

Owner-supplied original images remain visible to root but unavailable as original image bytes to this reviewer. Exact original side-by-side/pixel parity remains blocked. Present image2 is excluded. Comparisons use root's transcribed Chat reference geometry and the approved text architecture, never a reconstructed reference.

In historical visual10, rail70/sidebar280/header80 and workspace origin350,80 match the recorded reference geometry; composer434,785 at1013×108 remains close to434,786 at1013×107. Recent text begins around139, and the filled Chat icon/title are closer to the approved hierarchy. The footer is the exact approved wording,14px italic, below the composer without overlap; measured dark-theme contrast is10.58:1. The generic ready label is absent, and resting action strips are visually hidden. These scoped corrections are now supported by fresh labelled8-case evidence; actual immutable-product evidence remains a separate pending gate.

Full visual acceptance remains FAIL: final assistant result still precedes separate tool cards rather than the approved chronology; work duration, inline question, genuine choices and recorded answer are absent; reasoning effort/separate usage, Aa formatting and compact Voice controls remain absent. The model pill measures137×44 versus reference266×49 because required semantics are missing. Approval's real browser owner and reference hand icon are absent. Other viewport, contrast/theme, attachment, summary, error/recovery and original-reference states remain separate requirements.

**Scoped verdict:** no additional blocking source defect found in RevealWith/Chat owner preservation; prompt-lifetime finding is corrected within the independently reviewed31/335 evidence scope. No complete visual, functional, dependency or Spaces acceptance. Do not propagate this shell work to other Spaces as verified from the fixture results.

Final visual checkpoint: the current canonical discrepancy register now records all8 labelled cases and explicit remaining failures. No new production edits or .NET runs were performed by this reviewer.
