# Temporary 9to1 coordinator handoff

Jacob has assigned the **current RTX Flash-Next model** temporary responsibility for the UI and documentation pass while Astra works on Thunder. The reviewed starting revision is `2a9c4d980c4b16741f7d3880f1c982c0560eae88` on local main. Verify the actual checkout and read `pass-state.json` beside this file.

Acknowledge the handoff by writing `flash-ack.json` beside this file with your session ID if exposed, observed revision, the shared-file owner and the three work lanes. If the session ID is unavailable to the model, the caller will bind the tool receipt. Then act as coordinator. Read the current diff and reports, choose the next small batch, and record decisions in `flash-decisions.md`. Use the actual tool interface. The caller can relay an instruction to the Codex workers and bring you their results. Keep the task in your existing session for subsequent rounds.

- **Spaces Chat** is owned by the app worker. Review the floating Options panel against `spaces-chat` and `spaces-permissions`, including save/cancel, Escape, focus return, drafts and attachments.
- **Build Code document tabs** are the V100 specialist ticket. The V100 sends a candidate to you or the caller for application. Center internal tabs while keeping Pin and existing actions reachable. The source path is `9to1 Workspace/Dev/UI/DeveloperWorkbench.cui`.
- **First-party documentation** belongs to the documentation workers in their assigned groups from `docs-in-scope.json`. Review real component details and current links. Protect historical evidence, source copies, licence text and the four pre-existing dirty paths.
- **Shared CUI theme/shell** is under your exclusive coordination. No one else is currently editing it. Keep changes small. Never stage unrelated files. Validate via the existing SDK lock and memory guard.
- **Thunder and GPU administration** belong to Astra. Do not start, stop, reconfigure or reserve a model or device. Defer GPU dispatch while Astra announces a brief training window. Preserve your conversation and all worker ticket histories. The account testing cutoff is 15% remaining; the caller monitors it and signals a stop. Use local main. External release or publication is outside this handoff.

The live checkpoint is showing Home → Settings → Appearance, with original Glow/SuperDark/50/50 restored. Normal reload currently encounters a reproducible stale Write close lease. Escalate a subsequent native restart to the caller. Handoff ends when Astra explicitly resumes coordination.
