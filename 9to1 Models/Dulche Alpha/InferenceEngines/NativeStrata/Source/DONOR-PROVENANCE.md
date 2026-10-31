# Strata donor provenance (Dulche maintained fork)

Donor: Strata, the embedded inference engine that Dulche/Thunder runs. Specification line 4515 requires a
maintained Strata fork rather than treating an installed Strata application as a user dependency.

- Upstream: https://github.com/ro99/strata
- Controlled fork: https://github.com/CroakyJake12/strata (measured 2026-10-31: the pinned commit is reachable there; that is object custody, not a patched fork or a submodule pin)
- Pinned commit: `015b075079c51a7aec670ee24924f920f5e7bb2b`
- Git commit tree: `491a4a74e538bd469f83811c8a907cc7d6118db9`
- Archive: 1,476,955 bytes, SHA-256 `5b89b7b61a21cb6bee50f3a15b0f904a3c9324fdacc05f105128db69ebb3a62e`
- Source lock: `strata-source-lock.json`, 303 original blobs / 7,805,688 source bytes, one row per blob with
  its sha256 and git blob id.
- Licence: Apache-2.0, upstream notice in `LICENSE.strata`; the additive native bridge is licensed Apache-2.0
  as well. Vendored CUDA quantisation subset notice in `MARLIN-UPSTREAM-README.md`.

Full provenance and the recovery procedure live in `../MAINTAINED-FORK.md`, which is the authoritative record;
this file is the donor-registry pointer the gate checks.

## What is verified here, and what is not (stated, not smoothed over)

- Verified by `eng/check-donor-sources.py`: the lock exists, its commit and origin agree with the registry,
  `blobCount` equals its row count, its `sourceBytes` equals the sum of its rows, every row carries a sha256
  and a git blob id, the provenance document names the pinned commit, and both licence notices are present.
- NOT verified, and not claimed: that all 303 upstream blobs are vendored inside this repository. The lock
  binds them; this checkout carries the fork's own additive bridge files. Recovering upstream requires the
  documented fetch route, and every recovery must re-verify the digests above.
- NOT established: the original `IST-DASLab/Marlin` commit behind the vLLM 2.3.8 Marlin subset (recorded as a
  provenance limit in `MARLIN-UPSTREAM-README.md`), and whether `ro99/strata` is itself an origin or a further
  fork of another Strata project.
- NOT RUN: whether the resident inference host binary on this PC was built from this pinned source. That check
  is owed before any Strata behaviour claim, and it cannot be settled by restarting the host (the host is this
  session's model server).

No modification has been applied to upstream bodies: `MAINTAINED-FORK.md` records the fork as upstream source
plus additive bridge files only. That claim is the fork's own; re-verify it against the lock on upgrade.
