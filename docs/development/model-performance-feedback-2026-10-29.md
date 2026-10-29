# Reviewer feedback on the OpenCode PC session — 29 October 2026

Preserved at the owner's request so it survives context compaction. The quoted block is the reviewer's
text verbatim; the rest is this worker's operational response, written so the behaviour changes rather
than just being acknowledged.

## The feedback (verbatim)

> First, it sometimes coded before fully resolving requirements. The saving behaviour is a good example.
> It implemented a technically coherent storage system, but not the exact creation flow you expected.
>
> Second, it repeatedly produced intermediate code that didn't compile. This isn't unusual for coding
> agents, and it frequently repaired the errors. However, it suggests the model benefits considerably
> from immediate compiler feedback rather than being trusted to generate large patches without testing.
>
> Third, it struggled to close the loop between source code and the application you actually use. The
> model successfully built ARM64 packages and checked their contents, but acknowledged that it hadn't
> executed those packages on your ARM laptop. That's a serious gap for UI-heavy development.

Owner's framing alongside it: "overall doing well, just a couple of areas to improve in."

## Context that makes each point concrete

1. **Requirements resolved too late.** The storage work (commits `8a36217d`, `1fa867b3`, `b5554d6b`,
   `8e0fa938`) implemented the auto-provisioning, inspect-never-delete, relocation and recovery rules the
   owner gave, but routed "create new board" straight to a default folder. The owner's model also requires
   destination selection at create time: "you click create new board - it opens files (inside of the boards
   window) and then you navigate through files, select a folder, and click 'create' - the board is saved to
   that folder". The later clarification about `Documents/9to1` defaults removed the Files *connect*
   requirement, not the *choice*. That distinction was not pinned down before coding, and the owner
   discovered it on the laptop.
2. **Non-compiling intermediate states.** Several cycles in this session ended with compile errors that
   were then repaired: a method name invented in a `catch` (`EmitTabDiagnostic`), two Python patch scripts
   that asserted and died mid-patch leaving a file half-edited, a `finally` block destroyed by a blind text
   replacement, an argument-order bug in a driver script, and a routing helper left with a nonsense
   condition. Each was caught, but only because a compile or test run followed - sometimes too late.
3. **Source-to-running-app loop.** The ARM64 package was built and its contents inspected (architecture,
   assembly symbols, embedded CUI, hashes, provenance), but no aarch64 execution or installation was ever
   performed, and the on-PC native probe repeatedly failed to get its window on-screen so no native
   screenshot evidence was produced either. The consequence is visible: the owner could test the delivered
   package on the laptop and report failures that this session could not see, and the Browse lag change
   still has no before/after CPU figure attributed to it.

## Operational countermeasures adopted from now

- **Pin the flow before coding it.** For any creation, save, navigation or permission flow, write the
  required sequence as a numbered flow (who acts, what is shown, what the user picks, what is written
  where, what is shown afterwards) in `.handoff/boards-first-v3/work/REQUIREMENTS.md` and get it against
  the owner's words, before touching code. Where the owner has given two instructions that could be read
  as replacing each other, record explicitly which one amends which, and what survives.
- **Smaller patches, compile immediately.** Build after every edit unit rather than after a batch of
  edits. When a patch is applied by script, never leave it conditional on an `assert` that can abort
  halfway: apply edits individually, verify, and re-read the touched region before moving on. No commit is
  made while the tree is in a state that does not compile.
- **Close the loop, and say plainly when it is not closed.** A package is not evidence that behaviour
  works. Every behaviour claim must name its evidence level: unit/integration test, on-screen capture of a
  running instance, owner-reported live result, or not yet observed. Native verification steps are worked
  on in the foreground when a reported bug depends on them, and any blocker is named early rather than
  discovered at delivery time.
- **Sequencing is a decision that must be justified.** The owner had to correct the order here: the block
  menus they had drawn ("the new menus are not present") were queued behind the Browse engine work.
  Visible owner-directed UI behaviour outranks measurement work on a change that cannot yet be verified.
  Any deprioritisation of owner-requested UI work gets stated explicitly at the time, not after.
- **Never let a fix reach the owner only as a package.** When a fix is meant to be retested by the owner,
  state which build they must be running and how to confirm it (installed version string, source pin). The
  confusion in this session about which build was under test on which machine is exactly this gap.

## Status of the third point at the time of writing

Not resolved. ARM execution, installation and first run remain NOT RUN for every package delivered in
this session, and the on-screen native retest of the storage fix is still outstanding. This is recorded as
an open gap, not claimed as closed.
## Owner instruction added during the same exchange (29 October, ~15:25)

> don't worry about context (pin this too)

Meaning, as the owner put it: do **not** ration effort or working context out of caution. Do not shorten a
task, stop early, skip a verification step, or leave work half-finished because context feels expensive.
State is preserved by compaction plus the committed handoff, so the correct response to a long task is to
keep working through it, committing and pushing as I go so nothing depends on me remembering it.

Practical consequences I follow from this:
- Keep going until the task is genuinely done or a real blocker is hit; do not wind down early.
- Never claim a smaller scope than the owner asked for in order to save effort.
- Commit and push each completed unit of work so compaction cannot lose it (this is also why every commit
  in this session now goes to `origin/main`).
- Context pressure is not a reason to skip native verification, skip tests, or leave a tree that does not
  compile. If something must be deferred, name it as deferred and say why.

This does not relax any guard: the serial SDK allocation, memory/disk limits, never stopping the resident
model host, never signalling the owner's live instance, and the honesty rules about evidence all still apply.
