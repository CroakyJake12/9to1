# Thunder Pass-12 — Niko1221/Strata v0.2.2 source foundation acquired & pinned (lane 6)

Date: 2026-10-21 (verifiedAt 2026-10-21T15:54:36Z, real observed UTC)
Scope: EXCLUSIVE pass-12 objective — actually import official v0.2.2 (engine 0.1.47) and produce
thin candidate integration/package pin/compatibility/licence updates. No probe/harness churn.
Mode: SOURCE/PYTHON ONLY. No g++/native/CMake/.NET/CUDA/full-engine execution, no PC inference,
no model download, no service/driver/security/power changes, no commits/push/deploy/global-config/cap
changes, no credential reads.

## Official pin (authenticated independently via GitHub API)
- Repo: https://github.com/Niko1221/Strata
- Tag: `v0.2.2` -> annotated tag object `e6fff4ad12908de74952abe53b2bade45a68aa0f` -> commit `011c9e600a93bbc9fd451c563501b5831c32bbca` -> tree `539755e6d6b799bd75a47225befc786c436f6d8b`
- Engine version confirmed in-tree: `project(strata VERSION 0.1.47 ...)` in root CMakeLists.txt.
- License: MIT (© 2026 Niko1221 and Strata contributors); Apache-2.0 legacy ro99 donor preserved separately.

## Archive acquisition (in-memory stream; disk reserve held)
- URL: https://codeload.github.com/Niko1221/Strata/tar.gz/011c9e600a93bbc9fd451c563501b5831c32bbca
- Bytes: 28,901,367 ; sha256 `ccfffeaabfb410b734b95d202576b98b3d2f2d28080a69ea8a3c49268d95c93a`
- The ~28MB tarball was streamed and hashed in memory and NOT persisted, so /tmp free stays >=256MiB
  (observed free after: 332,537,856 B ~ 317 MiB). The full archive is re-fetchable in the later CUDA slot.
- Member enumeration (bounded, tarfile from memory): 4625 regular source rows; single top-level root; no
  unsafe/duplicate/truncated members.

## Tree closure / candidate lock
- New tracked candidate lock: `9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prep/strata-v0.2.2-lock.candidate.json`
  (1,168,735 bytes), schema identical to `prepare-upstream-update.py generate` output.
- Cross-check vs authenticated tree blob map (`/tmp/opencode/v0222-tree-blobs.json`, treeSha 539755e6...):
  rows=4625 == treeBlobs=4625; missing=0 extra=0; mismatch=16 all explained as CRLF->LF EOL-export
  normalization (0 unexplained). sourceBytes=77,151,632.
- Functional validation (in-memory, mirrors prepare-upstream-update verify/verify-tree): archive bytes/sha256
  exact; all 4625 lock rows match archive members exactly; closure complete; rows sorted/unique.

## Pinned change set
1. `prep/strata-v0.2.2-lock.candidate.json` — NEW tracked candidate lock (v0.2.2, 4625 blobs).
2. `strata-version-receipt.json` — candidate bumped to v0.2.2 / 0.1.47 / commit 011c9e6 / tree 539755e6;
   real verifiedAt 2026-10-21T15:54:36Z (no future fiction); archive bytes/sha256 for; latestVerifiedUpstream
   status=VERIFIED_ACQUIRED, archiveStatus=ACQUIRED_IN_MEMORY_BYTES_SHA_VERIFIED; v0.2.1 candidate pin
   preserved in `previousCandidate` (rollback lock path recorded); confirmedModels unchanged.
3. `dulche_strata_candidate_worker.cpp` — candidate origin hello text `90767252...` -> `011c9e6...`;
   comments v0.2.1 -> v0.2.2.
4. `StrataNativeWorker.cs` — managed Origin constant `90767252...` -> `011c9e6...`; comment v0.2.1 -> v0.2.2.
5. `CMakeLists.txt` — candidate profile comment v0.2.1 (0.1.46) -> v0.2.2 (0.1.47).
6. `build-package.py` — candidate pin comment v0.2.1 -> v0.2.2.
7. `MAINTAINED-FORK.md` — added v0.2.2 candidate provenance/lock section; rollback references.
8. `llama.cpp-dependency-lock.json` — verified note: ggml `third_party/ggml/VERSION.txt` in v0.2.2 still
   `3cf03257f219afbe7334045ff7c6a06ac68c627d` (unchanged vs v0.2.1 pin); pin intact.
9. `strata-runtime-dependencies.json` — donor source refs v0.2.1 -> v0.2.2 (tools/strata_tokenizer.py,
   tools/gguf_reader.py still present in v0.2.2 tree); regex/jinja2 pins unchanged.
10. Test custody updated to the new candidate pin: `NativeStrataCandidateTransportTests.py` (VERIFIED_UPSTREAM
    candidate==latest v0.2.2; test_candidate_pinned_to_v0_2_2), `NativeStrataCandidateManagedWireTests.py`
    (CANDIDATE_ORIGIN), `NativeStrataCapabilityDispatchTests.py` (commit + archiveStatus/status/equality),
    `NativeStrataNativeFixtureTests.py` (hello origin).

## Wire contract
Serve-line contract verified identical v0.2.1 -> v0.2.2 (READY/GEN/STOP/QUIT/T/DONE/ERR, engine-version
0.1.47 in-tree). The bump changes origin/engine-version/lock, NOT the wire.

## Reproducible later CUDA/managed build (NOT run this pass)
```
# In an allocated heavy-build slot with CUDA 12.8+ (sm_120), a verified llama.cpp checkout:
python build-package.py --archive <v0.2.2-codeload-tarball> --work /new/source-work
cmake -S . -B build -DDULCHE_STRATA_PROFILE=candidate -DSTRATA_ENABLE_CUDA=ON \
      -DSTRATA_NATIVE_EXPERTS=ON -DSTRATA_GGML_DIR=<verified-llama.cpp-checkout>
cmake --build build -j
python build-package.py --archive <archive> --work /new/build-work --build --install /new/dulche-package
```
The engine binary target is `strata`; candidate relay worker is `dulche-strata-candidate-worker`.

## Rollback known-good paths
- Legacy donor (default) profile: `strata-source-lock.json` (ro99/strata 015b0750..., 303 blobs) — unchanged.
- Prior candidate v0.2.1 pin/lock: `work/pc-worker/thunder-benchmark/prep/strata-v0.2.1-lock.candidate.json`
  (commit 90767252..., tree f7adcc90..., archive 27912843 / 2601d5df..., 4498 blobs); receipt `previousCandidate`.

## Explicit NOT RUN / NOT CLAIMED
- NO native/CMake/.NET/CUDA compilation, build, or engine execution (compiler/SDK slot not allocated here).
- NO live PC probe/inference (owner/reviewer-run only inside 21 Oct 15–20 UTC pool).
- NO model weights download; tools/vision/MTP unsupported remain explicit; text-only truthfulness preserved for
  Ornith-122B/Sage-35B.
- NO claim that v0.2.2 fixes owner engine exit-6: upstream issue #3465 is whole-prompt batched retry after
  non-finite logits (reporter got 503, not abort-6); would need actual installed version + engine crash logs.
- NO "uncompiled engine accepted" claim: package/lock prep uses the real v0.2.2 source, but the candidate
  engine remains unbuilt until the CUDA slot runs the commands above.
- NO speed-gain claim; no config tweaks; no credential/provider/cap/global config changes; no commit/push/deploy.
