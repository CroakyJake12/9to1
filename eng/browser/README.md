# Browser acceptance evidence validation

This tool checks the integrity and coverage of supplied acceptance records. It does
not execute product tests, certify a complete canonical inventory, judge whether an
assertion actually proves a requirement, or promote any requirement state. Exit 0
means **EVIDENCE-CONSISTENT**, never browser parity complete. An independent reviewer
must inspect runtime evidence and challenge each requirement-to-test mapping.

The current canonical document revision must be fetched again before final delivery.
Pass its manifest and exact paragraph records from that fetch. The manifest must
bind the exact paragraph-file bytes with `paragraphs_sha256`; matching revision
labels alone cannot establish snapshot integrity. The requirement
inventory must be derived and reviewed against all applicable canonical sections,
including default-required SHOULDs, donor obligations and cross-app examples.
Blocked and missing requirements remain in that inventory and its denominator.
The `source_coverage` array must retain an exact hashed disposition for every
nonempty canonical paragraph, including sentence-bearing headings. Each carries
`index`, `end`, `text_sha256`, named `reviewer`, `reason` and `classification`:
`applicable` with mapped `requirement_ids`, `excluded` with an exact hashed
`canonical_basis` paragraph, or reviewed `non-requirement`. This prevents unmapped
source from silently disappearing. A human reviewer must still challenge whether
the classifications and operation decomposition are truthful and complete.
Approved exclusions need canonical justification and independent review;
this tool does not invent or approve them. Synthetic fixtures in this tool's tests
validate rejection behavior only and must never appear in product acceptance records.

Run:

```sh
python eng/browser/validate-acceptance-evidence.py \
  --source-manifest SOURCE/manifest.json --paragraphs SOURCE/paragraphs.json \
  --requirements reviewed-requirements.json --candidate candidate.json \
  --evidence acceptance.json --evidence-root evidence-directory \
  --report evidence-directory/integrity-report.json
python -m unittest discover -s eng/browser/tests -p 'test_*.py' -v
```

Exit statuses: 0 consistent records; 1 incomplete or inconsistent records;
2 malformed, unsupported or unreadable input. No absent evidence is a pass.

All three record documents use `schema_version: 1`, `document_id` and
`source_revision`. Candidate and evidence pin the SHA-256 of the exact inventory
file bytes using `requirements_sha256`. Both identify `release_id` and full browser
source `commit`. Evidence must copy the exact candidate frontend/backend deployment
identities, URLs, source commits and artifact/configuration hashes. Backend source
may be a different owned service commit. Development URLs/builds cannot satisfy a
deployed staging or production gate. The approved supported browser matrix carries
unique IDs, exact names/versions and platforms; every requirement's gate must have
results for every declared supported browser.

Each requirement contains `id`, `app`, `operation`, `ui`, `domain_action`,
`expected`, `required: true`, canonical `state`, distinct named `implementer` and
`reviewer`, `test_ids`, `gate_types` and a `source` citation. Citations use an exact
paragraph's `index`, `end` and SHA-256 of its UTF-8 `text` under `text_sha256`.
Create separate rows for distinct operations/expected outcomes; quoting a large
paragraph does not prove that all its mandatory criteria have been decomposed.

Only real `browser-runtime`, `cross-client`, `provider-runtime`, `accessibility`,
`visual` and `performance` evidence belongs in this acceptance file. Unit/mock or
static checks remain separate supporting evidence. Each suite preserves every
discovered result, process exit status, exact command/interaction steps, fixture,
start/end timestamps and hashed durable log. Counts include `discovered`, `executed`
and every outcome: `PASS`, `FAIL`, `SKIPPED`, `CANCELLED`, `NOT-RUN`, `UNSTABLE`,
`BLOCKED`. Executed counts mean PASS+FAIL+UNSTABLE; interrupted/not-run cases retain
their distinct outcomes. Zero discovery/execution is rejected.

Each test specifies globally unique `id`, reciprocal `requirement_ids`, `outcome`,
`kind`, `real_production_path: true`, `mocks_used: false`, exact `browser_id`,
`expected`, `observed`, substantive `assertions`, and a hashed `log`. Visual tests
also need a hashed `trace` and `screenshot`; accessibility needs a runtime `trace`;
performance needs workload/results under `measurements`. Cross-client results retain
ordered browser → desktop → browser `client_observations`, asserting unchanged
`artifact_id` plus `revision`, `content_sha256`, `permissions`, `history` and exact
`runtime_identity` for each observation. Reviewers must additionally compare the
meaningful content, revision transitions and permission/history results against
the owning app's specified contract. Matching nonempty metadata alone is insufficient.

Artifact records are `{ "path": "relative/to/evidence/root", "sha256": "..." }`.
Files must exist, be nonempty and match their hashes; traversal and symlink escapes
are rejected. Records are claims that require independent scrutiny: copying an
identity or setting a boolean cannot establish that a real runtime was exercised.
