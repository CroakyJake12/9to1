# Development queue · revision 1

Source: owner continuation request, 30 October 2026; overnight handoff, 29–30 October.

1. **ACTIVE — BF-35, Boards block-actions menu.** Establish reproducible app.tests baseline; inspect
   actual CUI MenuItem types and trigger dispatch; repair popup/action lifetime without breaking grip
   dragging; validate open, real actions, stale-target refusal and dismissal/reopen. Native verification
   and ARM package remain separate gates. Required flow: `docs/development/boards-menu-repair-2026-10-30.md`.
2. **QUEUED — remaining reproduced Boards/Files defects.** Follow the handoff and reported-bug register;
   BF-37 insertion/replace hover previews remain open. Do not reopen working behaviour without a repro.
3. **HELD — ARM package.** Depends on genuinely working menu and PC build custody/native verification.
   The prior package and retained evidence remain preserved. No current PC access established.
4. **PRESERVED — Browse lane.** `733f35c3` contains group restoration and RED native ownership test.
   Keep independent evidence chains and respect the handoff's package exclusion. Diagnose/advance only
   with clear source ownership and after the owner's Boards/Files ordering is met.
5. **QUEUED — expanded specification continuation.** Continue dependency-ready canonical work after
   reproduced bugs; do not reinterpret implementation progress as independent acceptance.
6. **OWNER DECISION — BF-38 block links, Files root wording, orphan tab-group cleanup.** Keep existing
   decisions open. Do not invent links that cannot navigate or narrow unresolved semantics.

At each safe unit: inspect changes → compile/check → retain evidence → commit/push main → checkpoint.
