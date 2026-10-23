# Half-hourly development emails

Product authority and seven acceptance criteria remain in the canonical specification and AGENTS.md. The direct owner format is recorded in [the latest reporting decision](../specification/owner-references/2026-10-23-progress-email-format/owner-correction.json). This updates presentation; it does not change scope, independent acceptance or release gates.

## 👀 Progress

One bullet per app: `Home Shell: N% Implemented | N% Accepted`, followed by Spaces, Build, Automations, Write, Present, Boards, Canvas, Data, Files and other active products. Keep implementation separate from independent acceptance.

Calculate percentages only from a complete, current applicable feature/donor manifest with recorded numerator, denominator, scope/revision and explicit implementation/independent acceptance evidence. Both percentages use consistent scope across successive reports; explain a scope change. Tests passed are not features completed. Unknown percentages read `Not yet measured`; `Not Started` requires verified absence of implementation work. Partial percentages never establish app readiness or compensate for failed mandatory gates.

## 9️⃣ 9to1 Progress

One concise bullet per app summarises changes since the previous successfully sent email. Use `No Changes` when accurate. Include a previous-to-new percentage only when both values are measured on the same scope. Prefer user-visible fixes and outcomes to implementation details.

Include useful actual screenshots under `🕵‍♂️ Evidence`, captured during existing verification where practical. Label source, date, platform and freshness. A reused headless image is not a new native/package/laptop capture.

## 🌩 Thunder

Keep the Thunder progress section and make measurements explicit: requests completed/eligible/failed, CPU checks, build or integration gates, hardware, model/quantisation and controlled before/after medians with variability. Include TTFT, peak memory or utilisation when genuinely captured. State configuration differences, noise, BLOCKED and NOT RUN. Do not report estimates or confounded endpoint comparisons as established speed gains.

Use the existing private recipient, 30-minute cadence, authenticated send path and concurrency-safe send journal. Preserve all prior receipts and the last successful send; do not create another scheduler or resend messages merely to change formatting. Read the latest durable coordinator checkpoint and reporting state after compaction/restart.
