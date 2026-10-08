# Spaces acceptance preparation

**Preparation only — all 41 families are NOT EXECUTED.** Delivery intake reports zero smoke-passed candidates. No product was launched or exercised and no PASS/FAIL judgment is recorded.

This is an initial source-backed test-family mapping, not exhaustive atomic traceability, executed acceptance coverage or completion proof. Full exact-candidate reconciliation is outstanding: map every current requirement to the candidate feature/action/platform/dependency inventory, expand the families into atomic checks, identify omissions and explicit exceptions/OPEN decisions, and bind execution evidence to exact Windows artifacts and web deployments.

The complete requirement text, exact source document filenames, one-based inclusive line ranges, verbatim quotes, platform scope, steps, expected behavior and evidence requirements are in [spaces.requirements.json](/workspace/9to1-acceptance/docs/acceptance/astra/spaces.requirements.json). This Markdown file is a readable workflow index; its citations reference the full quotations in that ledger.

## Authoritative snapshot

Citation aliases below are filenames, not separate authorities: **D** = `development-specification.txt`; **P** = `post-release-updates.txt`. Both are frozen local text under `/workspace/astra-acceptance-evidence/specs/`; their paired metadata/revision JSON identifies the fetched documents. Requirements were not inferred from implementation.

- **development-specification.txt**: 9to1 Development Specification; document `1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg`; modified `2026-10-08T09:47:47.491Z`; revision `ANLCKQlGV-4pjogbJ8KNKTIJh95FXpzVNyyGudznrAwCF5v8YUH9MKLF6v12_I7NO4kuQ_gFqKINQo8xWeCtthei_Muf-fvH8WeCfOWyJcE`; SHA-256 `6b20bca3064d599408de5b952de00aebc22fc4d7f72c33eb47796e25ced71b26`.
- **post-release-updates.txt**: 9to1 Post-Release Updates; document `1yZCIP-ogTPLBfqcnc5EFMsL2FO5DoLYBTbGGPTk7Aks`; modified `2026-10-07T16:29:09.962Z`; revision `ANLCKQkSVZrxkLC8ZwUuozCSw4u_AsSqWQ9sXB5T29hofuQzw1kGZBxQ7v4ZfYoLkG1efDNvwGpfSPG21OFw3KFYN4_zzDT4jqc_wBEWP0Q`; SHA-256 `2b3981f007c68c1519c45476d2ff408f603fc209397f312116f638d4240fdce2`.

D:59–65 controls normative levels: MUST is mandatory, SHOULD requires a documented concrete scoped exception if omitted, and MAY remains optional unless another clause requires it. Testing a claimed/exposed optional behavior does not promote MAY to MUST.

## Scope reconciliation

- D:36 and P:4–7 pull the Agentic Update forward, including task/widget/dashboard, workers, steering, recovery and authorised model continuity.
- D:920 incorporates all former Lunar Eclipse LE-01–LE-15 into current Pass 1. Applicable Pass 2/3 remains in scope. Older headings do not defer incorporated requirements.
- D:11714 explicitly supersedes the earlier fixed Spaces sidebar hierarchy with the mini rail/custom sidebar. Protected behavior, permission safeguards and destination discovery remain required.
- P:18–25 applies current generic-agent versus configured Assistant/Specialist terminology and renames the legacy Agents Space to Specialists; compatibility/migration does not revive the retired product.
- Later unrelated post-release increments, including the additional Research evidence-workspace and phone-capture proposals, are not silently added to Pass 1. D:920 preserves that boundary; reconcile any explicit inclusion before dependent execution.

## Execute after candidate qualification

1. Obtain the exact Windows package/web deployment, matched dependencies, manifest/hash/commit identity and valid Smoke Tester custody/result. Qualify the two platforms independently. Do not substitute a current checkout, source presence or shared framework result for candidate product evidence.
2. Reconcile this initial map against the full authoritative scope and actual capability/action inventory. Split grouped families into independently observable conditions. Preserve every mandatory omission, OPEN decision or approved scoped exception in the acceptance ledger.
3. Prepare populated authorised data and scoped principals. Use genuine conversation/model execution, real owning-app state, supported production routing and actual installed/deployed surfaces. Diagnostic fault fixtures remain separately labelled and cannot establish real model/app success.
4. Execute UI journeys and corresponding typed API actions. Capture rendered output, stable canonical IDs/revisions and direct actual-effect readback; check denied/revoked grants, persistence/restart and interruption/retry in the same candidate.
5. Attach exact build/deployment/platform/hardware/browser/profile identity, data size, evidence paths, action/effect receipts, logs with secrets redacted and limitations to each atomic result. Changes invalidate affected results. A grouped family cannot PASS while a required constituent is missing, stale, blocked or untested.
6. Keep live external effects, spending, production release and unresolved commercial/retention/budget decisions within their existing authorisation gates. This document grants no new execution or publication permission. An unavailable prerequisite stays explicit and isolates its affected verification.

## Prepared family index

Every row below has status **NOT EXECUTED** and the same reason: no smoke-qualified candidate exists. Each corresponding JSON record contains its exact platform restrictions and detailed steps/expected/evidence fields.

| ID | Family | Sources (inclusive lines) |
| --- | --- | --- |
| SP-001 | Scope, supersessions and current terminology | D:920–921; D:11713–11714; P:18–25 |
| SP-002 | Real multi-turn conversations and persistence | D:3223; D:3323; D:3632–3633; D:127–130 |
| SP-003 | Space lifecycle, nesting, sharing and inheritance | D:3280–3286; D:3320; D:3404–3410; D:3530 |
| SP-004 | Conversation organisation, branches and historical actions | D:3329–3332; D:3524–3527 |
| SP-005 | Model-picker policy, provenance and route continuity | D:3353–3356; D:36; P:7 |
| SP-006 | Typed context, retrieval, citations and closed-corpus policy | D:3362–3368; D:3371–3380; D:3515–3518 |
| SP-007 | Canonical file/project/artifact edits and review | D:3226; D:3386–3389 |
| SP-008 | Shared rich-object rendering and round trips | D:3395–3398 |
| SP-009 | Protected extensions, custom graphs and graph AI bar | D:3416–3419; D:3425–3437; D:3443–3464 |
| SP-010 | Plugin/sidebar contribution lifecycle and safety | D:3305–3308; D:3311–3314; D:3470–3479 |
| SP-011 | Durable Tasks/Runs and distinct execution identities | D:3241; D:3542–3545; P:27 |
| SP-012 | Outcome persistence and bounded user-directed stop | P:30–32; P:67–70; D:340; D:370 |
| SP-013 | Persistent task widget and dashboard linkage | P:139–142 |
| SP-014 | Dashboard progress, checkpoints, durations and actual diffs | P:145–156 |
| SP-015 | User-added tests, stale results and verified handoff | P:159–162; P:70–88 |
| SP-016 | Live steering, safe boundaries and worker propagation | P:35–37 |
| SP-017 | Local blockers, honest execution status and estimates | P:40–42; P:55–64; P:52 |
| SP-018 | Worker ownership, integration, limits and individual controls | P:45–47; D:3500; D:10262 |
| SP-019 | Task/checkpoint recovery and exactly-once effects | P:50–52; D:10689–10695; D:10316 |
| SP-020 | Stop/Resume versus Retry/Regenerate/Cancel | D:3338–3347 |
| SP-021 | Tool probing, manifests, batching and partial recovery | P:91–110; P:168 |
| SP-022 | Scoped memory, frequency, corrections and temporary chats | D:3506–3509; D:10702–10718 |
| SP-023 | Local/Mesh/Cloud placement, threshold and privacy | D:11859–11866; D:12044 |
| SP-024 | Mini rail/dropdown navigation and pinned Spaces | D:11714; D:11753–11755 |
| SP-025 | Canonical tabs, embedded surfaces and restoration | D:11769–11773 |
| SP-026 | Custom sidebar sections, precedence and bounded rules | D:11824–11829 |
| SP-027 | Provider-labelled usage and freshness | D:11837–11839 |
| SP-028 | Study learning evidence and topic recommendations | D:3238; D:3536; D:11893–11896 |
| SP-029 | Revision Bank resources and owning-app lifecycle | D:11940–11945 |
| SP-030 | Study/Cards/Planner and optional-app install journey | D:12043; D:11736–11739; D:12009–12010 |
| SP-031 | UI/API parity, structured errors and idempotency | D:3591–3597; D:104–110; D:12032–12036 |
| SP-032 | Computer-use target, persistent notice and local stop | P:118–136; P:169 |
| SP-033 | Mini Computer provider/session identity and capability scope | D:9029–9031; D:9045–9056; D:9144–9153; D:9194–9200; P:133–136; D:1468–1472 |
| SP-034 | Multimodal conversation continuity and capture privacy | D:3643; D:3658–3670; D:3709–3727 |
| SP-035 | Distinct Shopping/Research source-backed workflows | D:3247–3250; D:3515–3518 |
| SP-036 | Translate context, chips and independent variants | D:3253–3256; D:3605 |
| SP-037 | Experience state, actor visibility, branches and Play handoff | D:3257–3272; D:3606–3609 |
| SP-038 | Pass 2 canonical artifact and interactive embeddings | D:11361–11363; D:11373–11378; D:11437–11438; D:11467; D:11514 |
| SP-039 | CUI accessibility, visual quality and responsive states | D:1279–1288; D:1303–1316; D:402–438; D:12042 |
| SP-040 | Measured resources, bounded caches and inactive work | D:1308–1312; D:11773; D:12045 |
| SP-041 | Independent Windows/browser workflow qualification | D:1454–1462; D:11591–11607; D:11736–11739; D:12046–12049 |

## Representative end-to-end evidence

- **Ordinary chat to verified task:** use a real model, create a durable task, observe its automatic widget/dashboard, steer during a tool call, coordinate bounded workers, preserve progress around a local approval gate, repair a failed test and show a current-revision verified handoff with actual artifacts/diffs. Test navigation/closure/reconnection without cancelling an explicitly background-capable run or claiming disconnected execution is active.
- **Canonical artifact/project work:** open the same authorised Files/owning-app entity from Spaces, perform a bounded typed edit, inspect the actual diff, open it in the owner, restart and preserve the same IDs/revisions/checkpoints. Revoke access or induce a stale revision; forbidden effects must remain absent.
- **Incorporated Study journey:** install Spaces/core, add/rearrange a custom Study section, create a Cards set from Revision Bank, edit both sides, reopen in Cards and Spaces, record a linked personal review, inspect progress/recommendations and create a Study-burrow assignment visible in standalone Planner. Test a missing optional owner through the trusted install path and preserve context after restart (D:12043).
- **Privacy/transfer journey:** create a temporary Local chat, observe no managed off-device copy, reject unsaved transfer, save locally, send only to one authorised device, then separately enable eligible Cloud mode. Test exactly-10-GB denial, quota/device revocation, interrupted idempotent retry, conflict handling and truthful pending replica cleanup. Storage placement is separate from AI/tool execution authority (D:11859–11866; D:12044).
- **Recovery/computer-use journey:** reconcile actual target state after timeout before retrying a consequential action. Where supported/authorised, exercise a real target app, screenshots plus semantic inspection, save/reopen, persistent session notice and local Pause/Stop during model/batch stall; safe resumption must not replay queued input (P:118–136; P:169).

## Exact platform boundaries

Windows native qualification requires its exact installed package and compatible Home/shared runtime. Browser qualification requires its exact authenticated deployment and shared CAKE ID/entities/domain APIs/permission services, while preserving full useful Spaces workflows; it does not require local Home. Every restriction must name the missing capability, affected operation, user-visible substitute and validation evidence (D:11591–11607; D:11736–11739).

Computer observation/control, camera/audio/screen capture, local model routing, Mesh/device transfers and VM operations run only within the published actual source/target/runtime capability matrix. Browser UI presence does not imply access to the user’s native desktop, arbitrary local filesystem or local models. Mini Computer retains its web exemption until hosted virtualisation/server compute is explicitly supplied (D:1468–1472; D:11606). Test explicit unavailable states independently of the required Spaces web product.

## Mapping integrity

The companion ledger contains 41 sequential SP IDs and 104 precise source citations. Integrity validation checks preparation declarations, required fields, NOT EXECUTED status and quotations against the pinned text; it is not a product test. Run [validate-mappings.py](/workspace/9to1-acceptance/docs/acceptance/astra/validate-mappings.py) with `--spec-root /workspace/astra-acceptance-evidence/specs` after ledger edits.
