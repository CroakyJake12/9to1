# Half-hourly development emails

Product authority and seven acceptance criteria remain in the canonical specification and AGENTS.md. The direct owner format is recorded in [the latest reporting decision](../specification/owner-references/2026-10-23-progress-email-format/owner-correction.json). This updates presentation; it does not change scope, independent acceptance or release gates.

## 👀 Progress

One bullet per app: `Home Shell: N% Implemented | N% Accepted`, followed by Spaces, Build, Automations, Write, Present, Boards, Canvas, Data, Files and other active products. Keep implementation separate from independent acceptance.

The latest owner instruction is: “Also actually estimate progress. Not yet measured is meaningless.” This supersedes the earlier rule requiring a complete denominator before reporting a percentage. Give a rough engineering point estimate for both percentages whenever a measured value is unavailable; do not replace the estimate with `Not yet measured` or an unknown status. Label the section or values clearly as rough estimates, not audited completion.

`Implemented` estimates applicable specification and donor behaviour present in the current integrated candidate. `Accepted` estimates the portion of applicable app scope independently validated by retained relevant reviews and actual workflows; it is not a claim that the app has passed full acceptance. Base judgment on current source, observed behaviour, evidence and known gaps, with source/revision, date and a brief rationale recorded in reporting evidence. Private unintegrated fixes do not count. Use measured percentages when a complete, current applicable feature/donor manifest supplies auditable numerators, denominators, scope and stage evidence.

Keep implementation and acceptance separate and use consistent app scope between reports. Explain changed scope or recalibrated judgment. Tests/tasks passed and the seven quality gates are not feature denominators or app-completion ratios. `Not Started` requires verified absence of implementation work. Partial or estimated percentages never establish app readiness or compensate for failed or missing mandatory gates. The previous denominator-only owner policy is retained as explicitly superseded history in the linked owner-reference record.

## 9️⃣ 9to1 Progress

One concise bullet per app summarises changes since the previous successfully sent email. Use `No Changes` when accurate. Include a previous-to-new percentage when the values refer to the same app scope; label estimated movements as estimates and distinguish actual implementation progress from recalibration. Do not invent a measured gain. Prefer user-visible fixes and outcomes to implementation details.

Include useful actual screenshots under `🕵‍♂️ Evidence`, captured during existing verification where practical. Label source, date, platform and freshness. A reused headless image is not a new native/package/laptop capture.

## 🌩 Thunder

Keep the Thunder progress section and make measurements explicit: requests completed/eligible/failed, CPU checks, build or integration gates, hardware, model/quantisation and controlled before/after medians with variability. Include TTFT, peak memory or utilisation when genuinely captured. State configuration differences, noise, BLOCKED and NOT RUN. Do not report estimates or confounded endpoint comparisons as established speed gains.

Use the existing private recipient, 30-minute cadence, authenticated send path and concurrency-safe send journal. Preserve all prior receipts and the last successful send; do not create another scheduler or resend messages merely to change formatting. Read the latest durable coordinator checkpoint and reporting state after compaction/restart.
