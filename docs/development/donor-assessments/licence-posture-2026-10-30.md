# Distribution and donor licence posture — owner decisions, 30 October 2026

Owner answers to `OPEN-2026-10-30-DONOR-LICENCES` and `OPEN-2026-10-30-ENGINE-DONOR-FORK`:

1. **9to1 is OPEN SOURCE BY DEFAULT.** A component is proprietary only if open-sourcing it creates
   one of these specific risks, and only the **minimum necessary portion** stays closed:
   - it lets a user bypass safeguards imposed by the service;
   - it lets a user make malicious account-service API/network requests (editing or accessing another
     user's account or storage without permission);
   - it poses a monetary risk (spoofing account details to get a paid service free, unauthorised
     payments/charges);
   - it exposes sensitive information (private API keys, credentials, personal user data, internal
     security secrets);
   - it creates a significant infrastructure security risk (attacks on 9to1 servers, bypassing abuse
     detection, exploiting shared compute, disrupting availability);
   - it violates third-party IP, licensing or legally binding confidentiality (e.g. redistributing a
     proprietary SDK's source without permission).
   **Explicit qualifier from the owner:** a component is proprietary **only where the risk cannot
   reasonably be mitigated** through secure design, server-side validation, removal of sensitive
   information or other safeguards. Default is open; closed is the exception and must be justified.
2. **Engines/donors: "embed what we aren't modifying, fork what we are."**

## Measured starting point (not inferred)

- The repository already carries a **GPL-3.0** `LICENSE` file at the root (verified by reading it).
  So the open-source question is not a fresh choice of licence; it confirms the existing posture, and
  any per-module relicensing would be a deliberate change to record.
- `Canvas/Source/Rnote` pin `8cac558fa73e` (v0.14.2) is **GPL-3.0-or-later** and is linked by
  `Canvas/rnote-poc`. Under the owner's open-distribution decision this exposure is **acceptable and
  intended**; it is consistent with the repo's own GPL-3.0 licence. No engine replacement needed.
- `Browse/Source/FirefoxGecko` pin `5836a062726f` measured as a **clean upstream checkout**: the pin's
  own commit is an upstream Mozilla changeset (author `florian@queze.net`), and there is **no patch
  file, no mozconfig and no carried modification** anywhere under `Browse` (verified by search). Under
  rule 2 that means Gecko is **embedded, not forked**. It stays tri-licensed MPL-2.0 / GPL-2.0 /
  LGPL-2.1 with our own code kept separate; if we ever patch it, rule 2 turns it into a fork.
- `Data/Source/DuckDB` pin `d8cdaa33fda8` (tag v1.5.5) is **MIT**, unmodified → **embed**.
- `Boards/Source/AppFlowyBoard` pin `804d7898ac0b` is **dual AGPL-3.0 OR MPL-2.0**. Boards currently
  links **no** donor code (its `AppFlowyBoardEventAdapter.cs` is pure C# command mapping), so nothing
  is elected yet. If donor code is ever used, elect **MPL-2.0** and record the election in notices.
- `Unsloth Studio` (`studio/`, including the model browser at `studio/backend/hub`) is
  **AGPL-3.0-only**. Because the main app is distributed under GPL-3.0, merging AGPL code would push
  the combined work towards AGPL obligations, including for the paid/gated server side. Therefore
  **Unsloth Studio is not adopted as a code donor**; its feature list remains a parity checklist only.
  Its own core package is Apache-2.0 if a permissive quantisation/download path is ever wanted.

## Boundary the decisions create (must be enforced in code, not just prose)

The paid/proprietary half — identity, subscription, entitlement, anti-spoofing, cloud model and
storage billing — **cannot link GPL-3.0 code**, so it must live in separate modules and separate
processes reached only through typed boundaries. The open apps may link GPL/ MPL/ MIT donors.
`OPEN-2026-10-30-DONOR-LICENCES` is therefore marked **answered**, with the module split recorded as
follow-up implementation work rather than an open product question.

## Corrections accepted 30 October 2026 (owner's research pack, `docs/development/research/2026-10-30-six-handoffs/README.md`)

Three claims I wrote earlier this session were wrong or too broad. Recorded here so the error is not
carried forward:

1. **My blanket rejection of Unsloth Studio was too broad.** I justified it as "AGPL would drag the
   paid/gated server side open". AGPL s.13 applies to users interacting remotely with a *modified
   covered program*; whether our code is a *combined work* is a fact-sensitive question about actual
   imports, linkage and shared data structures, not about "another private server exists on the
   network". A separate process is not automatic safety, and neither is automatic infection. Correct
   status: **adoption conditional on a concrete source/linkage/distribution review**, not excluded on
   principle. The AGPL obligation, if it applies, applies to the covered work, not to unrelated
   services.
2. **"Chromium = embed a pinned official artefact + digest" is not viable on our ARM target as
   written.** Measured from the pack: Google's Chrome for Testing platform list covers Linux x64,
   macOS arm64/x64 and Windows — **no Linux ARM64**. Meanwhile Mozilla candidate builds **do** publish
   `linux-aarch64` with SHA-256/SHA-512 manifests and detached signatures. So the ARM64 engine route
   is Gecko-first, and any Chromium/CEF path needs its own artifact availability check first.
3. **My "CDP or JWP" framing for unified DevTools was wrong.** Mozilla removed its experimental CDP
   implementation in Firefox 141; Gecko's own tooling speaks **RDP**, with WebDriver BiDi for
   automation. The Gecko adapter must be built on RDP/BiDi, capability-tested per operation against
   the pinned revision — not assumed CDP-compatible.

Also accepted from the pack: pinning the **vendored DuckDB C++ source** does not install the **Python
wheel** Data actually imports. Those are two separate supply-chain records, and the pack's proposed
`workers/requirements.in` + `locks/<platform>-<python>.txt` + `pip` hash-checking layout is the shape
to implement (plus a check that the lock agrees with `REQUIRED_DUCKDB_VERSION`).

## Security posture implied by the criteria (proposed, not audited)

Because the owner's test is "can secure design remove the risk", the default engineering answer is to
make the **service authoritative**: the server decides account, entitlement, permitted spend and
resource access from its own state, so a client request is a request and never proof of permission
(OWASP: authorisation checks on every request). Client-side `paid=true`, arbitrary account IDs or
caller-supplied prices must confer nothing; secrets and user records stay out of any distributed
binary and out of the source archive. Publishing source does not mean publishing credentials, and
"security-sensitive" is not a blanket licence to withhold corresponding source a licence requires.
