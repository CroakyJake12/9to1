# ALORA03: CPU implementation qualification

Recorded 25 November 2026. The CPU training mechanism is implemented and qualified by 198 regression tests and an actual 48-layer native neural-model update with checkpoint reload. Real-corpus training, GPU qualification, independent evaluation and promotion remain open gates.

## Exact revisions and evidence boundary

| Checkpoint | Revision | Evidence |
|---|---|---|
| ALORA02 baseline | 11b71cadc28318b4c0dc4b968b47465643920ebf | Previous selected-expert/native loader implementation |
| ALORA03 core | 8069faedd1cf665d2bef969f307fc9d2ef1dc1bb | Permits, streaming loss, capacity pause and calibration |
| CPU entry points | e98cfd80af7979b4f59986fd5d60e3ab7a33a4cb | Calibration CLI, pinned workflow and continuation |
| Native arithmetic qualification | b9c86d405a6b2ef38be75a18bf8e9965d9200ac8 | Actual 48-layer forward/backward/update/checkpoint proof |
| Final code qualification | c6c135e8c047d510b35f91723a024fa3414cd3e1 | Conservative FP64 temporary bound; 198/198 tests |

The final change tightens memory admission at the loss, session and workflow boundaries. The complete streaming autograd class AST is unchanged from b9, with SHA-256 8e1a21455b0b06530ea415cf2b7845efffe0186e2615b327335f03fe471d3a81. All 21 production source pins in the native proof match b9. All 43 Python source pins in the final suite match c6.

The full native proof was not repeated after this validation-only change. Its actual two-token loss tile needs 4,096 bytes under the final rule, well below 16,777,216 bytes. The b9 checkpoint remains bound to the b9 backend source identity; cross-revision checkpoint restoration is not claimed.

The machine-readable companion, validation/ALORA03.json, lists every file, SHA-256, revision, primary receipt and remaining gate. This documentation has its own subsequent commit. The local integration result is recorded in the owner queue's ALORA03-integration.json.

## Implemented behavior

| Slice | Files in this package | Result |
|---|---|---|
| Selected-expert training | adapters.py, capture.py, profile.py, session.py | Select from observed dispatch; train expert gate/up/down adapters; retain frozen base, routers and shared experts. |
| Native quantized model | gguf_store.py, gguf_model.py, paging.py, paged_experts.py | Map all 1,224 text-model tensors; read and decode bounded frozen slices; preserve downstream gradients. |
| Streamed causal loss | paged_loss.py, session.py | Full-vocabulary causal loss, masked targets, native decoder and optional router auxiliary loss; recompute frozen-head gradients without retaining full training logits. |
| Compute permits | compute.py, paging.py, paged_experts.py, capture.py, gguf_model.py, session.py | Check permits at reads, transfers, compute and autograd boundaries; CUDA library admission remains unexecuted. |
| Capacity interruption | read_limits.py, gguf_store.py, session.py | Pause before the optimizer on fingerprint capacity; restore RNG, clear incomplete gradients and preserve cursor, adapters and optimizer history. |
| Calibration | calibration.py, capture.py | Category-balanced train-only routing observations; atomic partial/complete profiles with receipts; exact whole-row resume. |
| CPU calibration command | calibration_cli.py, __main__.py | Existing SDK flock and approved memory guard; verify actual parent/child lineage and kernel lock before corpus/model execution. |
| CPU service workflow | workflow.py, runner.py, worker.py, checkpoints.py | Pinned deployment, venv-preserving executable, actual job-context checks, one readiness handshake and verified content-addressed checkpoint import. |
| Operator instructions | README.md, RUNBOOK.md | Complete request schemas, commands, supervision, resume and evidence boundaries. |

The required training split is 5,000–10,000 examples: 25% autonomous recovery, 25% UI, 15% UX, 15% proactive reasoning and 20% coding. Calibration defaults to four train samples per category, 20 total; three per category gives 15. The independent 54-case computer-use evaluation is reserved for subsequent quality assessment.

## Final verification

### Regression suite

ALORA03-budget-final-1795627788/test-summary.json records 198 tests, zero failures, zero errors and zero skips. CUDA remained uninitialized with zero initialization attempts. The existing shared SDK lock and 8,192 MiB memory guard were used. Guard result: OK, exit 0, 23.1 seconds, minimum available RAM 30,160 MiB.

Installed runtime: Python 3.12.3, Torch 2.13.0+cu130 and Transformers 5.20.1. These are the versions actually exercised, not a general compatibility claim.

### Actual native neural-model proof

ALORA03-qualified-1795627289/native-graph-proof.json records:

| Measurement | Result |
|---|---:|
| Forward layers | 48 |
| Backward layers | 48 |
| Mapped GGUF tensors | 1,224 |
| Selected LoRA parameters | 675,840 |
| Completed optimizer updates | 1 |
| Adapter tensors with measured changes | 96 |
| Streamed loss | 12.876958847045898 |
| FP64 reference on identical native FP32 logits | 12.876957994436642 |
| Absolute loss difference | 0.000000852609256 |
| Unchanged acceptance tolerance | 0.00002 |
| Permit checks / renewals | 81,187 / 1,368 |
| Checkpoint round trip | Verified |
| CUDA initialization attempts | 0 |

The proof used numeric token IDs [1, 3, 9], one observed expert per layer, rank 2 and alpha 4. Its permit issuer was an isolated software fixture using the actual file-based TrainingControl client. It did not use the production supervisor, a real training corpus, held-out tasks or a GPU.

Checkpoint: 8,340,083 bytes; SHA-256 65eb75fd1c7c1fb303fd72ed43cf475a6514cdae32c13e91fc0988fa62832b87. The checkpoint was delivered, sealed, reloaded through custody and restored in the exact-source session.

The model execution took 214.625 seconds. The guard recorded 224.0 seconds and minimum available RAM 27,875 MiB. Process self-reported peak RSS was 7,265,728 KiB; the guard's sampled process-tree peak can miss brief peaks and must not replace that high-water measurement.

The loader used the existing three Flash GGUF shards, source identity 4a6a3aa5010d91cb55ba0cf7668cbc4375fe81eaabf869e226821fca8a5dfc56. Full shard hashes are external attestations; this run did not freshly hash all approximately 94 GB. Accessed slices were freshly fingerprinted. Text-native execution excludes vision and MTP.

### Guard and lock lineage

The actual calibration guard probe passed with kernel flock ownership and process lineage verified. Its receipt is ALORA03-guard-lineage-proof/receipt.json, SHA-256 a84f2825e2f0242c32d4de2a0b6ed5f72fce147982e69fa8a502e0a63ab43c2b. This probe did not import Torch, open a dataset, load a model or claim a production service launch.

## Numerical correction and preserved failures

The first two full-native attempts completed a numeric update but failed their native-FP32 loss comparison. Their failed statuses and artifacts remain unchanged. An actual-hidden/logit diagnostic established that native FP32 whole-vocabulary cross-entropy lost precision: its absolute error against FP64 reduction of identical FP32 scores was approximately 0.00013934.

Qualification therefore uses FP64 cross-entropy on those fixed FP32 scores, retaining the original 0.00002 acceptance threshold. This changes the reference accuracy, not the tolerance.

A separate 248,320-token synthetic case with a dominant score first or last then exposed real order-sensitive drift in the original streaming accumulator. FP64 normalization accumulation fixed it. Both positions now meet a 0.000002 loss/hidden-gradient gate; loss error is approximately 0.000000388 and hidden-gradient error approximately 0.000000434.

Projection scores, exponential scores, gradients and returned loss are FP32. PyTorch may allocate a bounded FP64 reduction temporary; retained normalization statistics scale with token count. The final admission rule budgets at least eight bytes per tile element and limits each compute/reduction buffer to 16 MiB. This is a per-buffer limit, not a total live-memory guarantee.

Retained failed runs:

- ALORA03-first-1795624505: 177 tests, two failures; corrected extreme-logit gradient calculation and callback assertion.
- ALORA03-third-1795624999: 186 tests, two failures; corrected calibration completion observation.
- ALORA03-native-1795625339: failed native-FP32 comparison.
- ALORA03-matched-1795626254: failed matched-hidden native-FP32 comparison.
- ALORA03-wide-1795627076: exposed streaming wide-vocabulary normalization drift.

All original logs, guard receipts, diagnostics, update records and checkpoints are retained. The final passing proof adds evidence; it does not relabel any earlier failure.

## Remaining inputs and execution gates

1. An independently admitted 5,000–10,000-row ALORA corpus with the exact five-category quotas, provenance and separated validation/final-heldout splits was not found in the bounded local audit. The older Atlas corpus and expanded 2,955-item pool were not substituted. No training corpus was generated here.
2. The requested independent 54-case evaluation was not located. Existing cycle metadata still records heldout_pending and awaiting-independent-sealed-final. Previously consumed evaluation data is development data.
3. Real-corpus calibration and a production supervised CPU training job have not run. The implemented calibration/workflow commands can proceed after the required inputs and resource admission are supplied.
4. CUDA library admission code exists, but CUDA execution is unqualified and there is no production GPU launcher. An admitted resource window, accelerator qualification and corresponding launcher remain future work.
5. Independent Qwen review and quality/promotion gates remain open. The numeric proof demonstrates software execution and persistence, not model quality, long-context capacity or production throughput.

## Ownership, integration and Qwen handoff

The only edited repository scope is 9to1 Models/Dulche Alpha/Training/alora/**. Preflight found no competing ownership or destination collision. Before integration, main had 26 unrelated tracked modifications and 65 untracked files; their hashes are retained for preservation checks. Dulche/inference configuration and the dirty feature inventory belong to other workers.

The owner queue is /home/jacob/9to1-swarm-harness-20261124/owner-queue-20261125. ALORA03-integration.json records the eventual local fast-forward revision and before/after owner-file checks. ALORA03-chatgpt-validation-handoff.json records the final review revision and actionable external gates.

Qwen should independently review the final source and evidence, verify production input admission, review CPU calibration/continuation behavior and perform any later accelerator qualification through the existing resource owner. The current feature-inventory owner should reconcile the proposed ALORA entry; that shared file was not edited.

Atlas routes and CUI native previews are not applicable: the owner steered this slice to the ALORA backend, and no Haven or Spaces surface changed. Native neural-model execution evidence is reported above and is not GUI preview evidence.
