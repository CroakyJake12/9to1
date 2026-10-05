# Latest user direction — stage the full apps first, deep acceptance afterwards

Jacob clarified on Monday 5 October 2026: "acceptance isn't being dropped, it's just that staging the full app becomes the priority and deep acceptance happens after". The first deliverable is the full in-scope app set staged BOTH on gated web and Windows, targeted for tonight. This supersedes earlier acceptance-before-private-staging/download instructions, NOT final product requirements or truthful completion reporting. The missed 07:00 milestone remains historical; do not hold staging to the former full-acceptance prerequisite. No successful final RC or new guaranteed deadline is implied.

## Canonical scope

The live Development Specification now begins with **Delivery order update — 5 October 2026: staging first**. Its staging-first text was written with revision guards and read back; all original acceptance requirements remain below it:
https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg

Read the current **9to1 Post-Release Updates**, especially **2026 post-release scope**:
https://docs.google.com/document/d/1yZCIP-ogTPLBfqcnc5EFMsL2FO5DoLYBTbGGPTk7Aks

Do not implement the expressly post-release Assistants/Specialists migration, Community/Workspace Kits, additional Study assessments, Mesh capture/continuity increment, creative libraries/publishing sets or Research source-change extensions during this staging effort. The roadmap explicitly preserves existing launch requirements; mentions of an existing app do not defer that whole app or its baseline. Reconcile the actual app/platform inventory once, without another broad spec rewrite or invented exclusions. Preserve useful implemented work.

## Execution now

- **A:** finish usable Windows app packages and required shared Home/runtime/dependency integration; hand actual fetchable sources/packages to C.
- **B:** finish and publish the real browser app build and navigation for the same in-scope set; hand the actual deployable bundle to C.
- **C:** maintain one provisional integrated candidate, provide gated staging deployment and private versioned Windows downloads, and keep a compact per-app delivery list. Existing technical ownership/source privacy protections remain.

Deliver ready apps progressively. Do not hold a usable app behind unrelated remaining apps or deep whole-ecosystem tests. Continue completing the rest until every applicable app has both surfaces. Stage full real builds and integrated existing functionality, not substitute mock shells. Record app-specific defects and missing workflows honestly; launching a shell is not full implementation.

Before staging, do only proportionate checks needed to establish a usable private preview: build/package completeness, real launch/navigation, one principal workflow (including a basic save/reopen with disposable data for editors), the actual web access boundary, and isolated test configuration. A failed core smoke path remains an app staging blocker, not an excuse to halt all delivery. Do not expand this into the full acceptance programme.

Web access must be server/edge enforced for Jacob/explicit testers and cover app routes and private APIs; verify unauthorised access is denied. An existing supported external access gate can protect staging while full CAKE ID acceptance continues later; never fabricate in-app authentication or permissions. Keep tests/data/accounts/storage isolated, preserve Home/OS authorisation, and do not disable security or falsely claim production signing. Where an unsafe operation is unready, keep that operation unavailable with its actual reason. Do not connect production customer data, charges, mail, paid inference, or public DNS/WordPress cutover under this instruction. Use existing authorised hosting/resources; escalate only a specific missing deployment permission/cost, not generic acceptance status.

Use the existing private Drive route described in blocker-resolution-20261004.md. No remote access to Jacob's PC is required for download delivery. Keep staged builds explicitly STAGED — ACCEPTANCE PENDING, versioned and immutable, with source/hash/deployment identity and short known-issues/recovery notes. Available download, successful local installation and accepted product are distinct.

## Conserve execution capacity without removing acceptance

Stop launching redundant full-suite reruns, repeated inventories, unchanged artifact reconstructions or status-only worker loops. Reuse already verified unchanged artifacts; batch focused build/smoke checks and investigate a failed task only with a concrete next hypothesis. No more broad per-app chat rollout. Preserve healthy running tests and completed evidence; defer new deep-sweep work until both-surface staging is complete unless it is essential to safe usable staging. Keep sufficient remaining capacity for deployment, uploads and a resumable handoff; do not invent quota telemetry or change model settings silently.

After staging all applicable apps, focus on deep functional/donor, accessibility, security/privacy, resource/recovery/concurrency, provider and supported-platform acceptance against the exact versions. Retain required tests, failures, not-run states and release-signing/install gates. Do not mark staged builds accepted, weaken assertions or promote acceptedHead merely to meet a time target. If execution ends first, the stage and pending test commands must remain accessible for continuation; no claimed background work after a Goal stops.

## Reporting

At the next safe checkpoint acknowledge this sequencing change in your own bounded record. Posting this directive is not acknowledgement or proof of deployment. Team C maintains the combined Worker Logs.

Lead with **Web staged n/N; Windows packages available n/N; both applicable surfaces n/N**, then **Acceptance verified separately**. Explain app/platform applicability and any deliberate post-release exclusion. Use the existing Checkpoint / % Progress / Change Summary / Evidence / Next Action format; give real links and exact next engineering actions. Brief checkpoints, not repeated historical failure inventories. Final acceptance still happens AFTER the staging milestone; it has not been dropped.
