# Acceptance defect handoff

Classification: MINOR or MAJOR, with architectural/feature rationale.

Candidate/app/platform: REQUIRED. Exact source repository/revision, artifact SHA-256 or immutable deployment/bundle identity, donor/dependency identities, Smoke Tester receipt and specification snapshot/revision: REQUIRED.

Failed atomic requirement ID and original assertion/citation: REQUIRED.

Expected behaviour: REQUIRED.

Actual independently observed behaviour: REQUIRED. Distinguish a reported upstream defect from one reproduced here; do not label unavailable execution as a product FAIL.

Exact reproduction with disposable data and target IDs: REQUIRED.

Original evidence paths/hashes, environment, timestamp, command exit/result, actual rendered screenshot where relevant: REQUIRED. Preserve the first failure and assertion.

User impact and recommended repair scope: REQUIRED. Identify the existing owning subsystem; do not create a second implementation.

Repair and requalification: for minor corrections, isolated repair revision → Smoke Tester replacement build/package/smoke receipt → affected independent acceptance. For major failures, return to the existing Astra Implementer; preserve unrelated healthy work.

Status and recipient receipt: REQUIRED. Delivery of a report is separate from an acknowledged/qualified repair. No integration branch write or release follows from this report.
