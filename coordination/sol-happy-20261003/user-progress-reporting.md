# Jacob's reporting request — frequent progress checkpoints

Jacob requests that the existing workers document their progress and a rough percentage frequently in the shared Worker Logs document:
https://docs.google.com/document/d/1rdxd_icOVUpeu19PI-IJk-7oWYs99tVrtc7ITJV4_8s

The document is a progress log, NOT a replacement product specification. The live canonical product authority remains:
https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg

This is an operational reporting instruction for existing Teams A, B and C and all six subagents within each team. It does not authorise new implementation scope, weaker tests, spending, deployment, merging, additional agents, or changes to ownership.

## Start and cadence

At your next safe coordination boundary, acknowledge this instruction and provide the first checkpoint. Thereafter update approximately every 30 minutes while actively working, and after a major validated milestone, material blocker, handoff or session end. Do not stop a running test to meet a reporting timestamp. If a test spans an interval, report that it is still running, its actual start time and last observed stage without inferring a result.

Existing subagents send short updates to their existing coordinator. Teams A and B publish concise, pass-tagged checkpoints in their own established team records on this coordination branch. Team C collects A/B/C checkpoints and is the single writer of combined pass entries in Worker Logs. Please relay this instruction to all existing workers; sharing a chat transcript is not a delivery mechanism.

Record an acknowledgement in your own team record with this file path, log document ID and the real acknowledgement time. Do not mark another team acknowledged. Team C may add this directive to its owned index; this request does not edit that index or the accepted-head pointer. If a writer handover is needed, make it explicit before another team changes combined log entries.

## Required checkpoint — Jacob's exact format

Use the following layout under each relevant pass. This supersedes the former single-line progress / What Changed / Evidence-tested-revision / Blockers-next-action layout.

```text
Checkpoint 11:07
% Progress:
    Implementation - … ;
    Acceptance Verified - … ;
Change Summary: …
Evidence: …
Next Action: …
```

In Google Docs, use Heading 1 for `Checkpoint HH:MM`. Make every following line bold, with `Implementation` and `Acceptance Verified` on separate indented lines. Preserve the displayed labels, capitalisation, hyphens, semicolons and field order. Use the actual Europe/London checkpoint time instead of copying 11:07.

When there is a blocker, the value of `Next Action:` MUST begin `BLOCKED`, for example `Next Action: BLOCKED — [blocker]; [owner and action needed to unblock].` Do not add a separate Blockers field. Name the affected scope; do not imply independent work is also blocked. When unblocked, state the next implementation or validation action normally.

Keep supporting detail inside these fields, not extra headings: scope/confidence and a brief estimate basis alongside the two progress values; the actual date and reporting team/scope in Change Summary when needed; exact source/tested revisions, executed pass/fail/not-run results and evidence links in Evidence. Distinguish local/unpushed, pushed-but-unmerged, integrated, deployed and commercially enabled work wherever relevant. Do not paste large logs.

Aim for about 80–150 words per affected pass checkpoint. Preserve previous entries; correct mistakes in a new labelled correction rather than rewriting history. Existing ellipses and 11:07 blocks are templates, not past measurements. Use fresh document readback/revision guards and re-resolve insertion positions on conflict. No secrets, private test accounts, tokens or credentials belong in the log or Git.

## Make the rough percentages useful and honest

Do not wait for a perfect thousands-row global requirements inventory before offering a provisional implementation estimate for work you have actually inspected. Start with a small explicit set of meaningful feature/workflow groups from the current canonical scope, note the major unknowns, and estimate implemented behaviour in those groups. Use rounded figures or ranges, not decimal precision. Distinguish this engineering estimate from measured acceptance. When evidence genuinely cannot support a number, identify the missing scope and report the bounded estimate you can justify; never manufacture one.

A team is NOT a pass. Teams may contribute to several passes. Tag contributions to Astra la Vista, Terra-Form, Sol Happy and any separately authorised active scope explicitly; do not silently include later scope in an earlier pass percentage. Team C owns combined pass/global estimates, with stable group weights and no double-counting of shared components or overlapping team contributions. Do not simply average three team percentages. Explain changed scope/weights rather than presenting a denominator change as progress.

Acceptance progress requires executed applicable tests and the specified observable behaviour. Source presence, authored tests, compilation, commit volume, raw test pass rates, metadata/custody checks and worker claims are not substitutes. Break large groups into meaningful independently testable criteria where useful; do not claim a whole app passed because one journey passed. Missing, failed, skipped, cancelled, stale, blocked and not-run criteria remain outstanding. Preserve legitimate bounded passes, but never relabel older source/platform/test-mode evidence as current integrated or live production acceptance. A recorded zero fully accepted groups does not mean zero implementation.

## Keep building

This is lightweight reporting alongside implementation and validation, not another documentation-only workstream. Reuse the existing evidence and coordination records. No extra agents, new reporting-only services or artificial timer loops. If Drive or coordination access is unavailable, save the dated checkpoint in your existing owned record, expose the reporting limitation, and continue independent work. Do not pretend the document was updated or another worker was contacted when it was not.

Completion criteria, independent validation and existing safety/approval gates remain unchanged. Receipt and first actual worker checkpoint are still required; publication of this instruction alone is not worker acknowledgement.
