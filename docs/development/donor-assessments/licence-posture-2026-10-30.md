# Distribution and donor licence posture — owner decisions, 30 October 2026

Owner answers to `OPEN-2026-10-30-DONOR-LICENCES` and `OPEN-2026-10-30-ENGINE-DONOR-FORK`:

1. **9to1 is open source, with a few proprietary components** covering monetisation, account identity
   and subscription — "basically anything where there's a risk of spoofing and getting access to paid
   services for free if it's open source". The main apps are free and open source; services such as
   cloud models and storage are gated/paid.
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
