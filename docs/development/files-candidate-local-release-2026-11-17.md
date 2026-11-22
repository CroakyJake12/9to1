# Verify a Files web candidate before extraction

Use this gate for the sealed web candidate. Obtain the exact Actions ZIP and the 40-character commit to which its manifest should refer. Read and verify the package first. Extract into a fresh directory only after the manifest, source identity and content hashes match.

The tool accepts a ZIP path and `--expected-commit`. Add `--extract` to materialise the verified site. Never point that option at an existing served site. A successful gate verifies the packaged web files. Hosted deployment, native Files behavior and access to a real Drive account require their own observations.

```bash
python3 -B eng/development/files_candidate_release_gate.py "$CANDIDATE_ZIP" --expected-commit "$REVISION"
```

[Candidate gate](../../eng/development/files_candidate_release_gate.py) · [Web Files](../../apps/Web/Files/README.md)

## Dated delivery evidence

The 17 November 2026 record retains the archive identity, sealed web artifact, integrity checks and release boundaries. [Original record](evidence/files-delivery-before-atlas-v4/files-candidate-local-release-2026-11-17.md)
