# 9to1 Boards revision migration — NO duplicate board implementation

**Today:** Continue teaching inside the same OpenCode Qwen conversation. It already connects to 9to1. Existing OpenCode tasks/terminal are sufficient as a temporary interface. Do NOT build an extra standalone revision server, new orchestrator, replacement Boards format or worker model.

**Next destination:** Make 9to1 usable for teaching *after* the native app, Boards and saved-document flow have real runtime confirmation. Existing source of truth:
- `9to1 Workspace/Boards/contract/HavenBoardDocumentCodec.cs` — schema validation (currently v5).
- `9to1 Workspace/Boards/contract/JsonFileHavenBoardStore.cs` — safe physical storage, backups.
- `9to1 Workspace/Boards/contract/RichBoardSession.cs` — versioned rich notes, mutation and autosave.
- `9to1 Workspace/Boards/Host/Program.cs` — host entry.
- `docs/specification/9to1-development-specification-v2.md` and later owner amendments — product authority.

## Concrete first-user journey to verify
1. From **the running 9to1 app**, create a new Board; the owner chooses a destination (no account/Files connection required) or a clearly identified recoverable local draft.
2. Enter a multi-part Maths question, editable notation and diagram; preserve teacher question and learner answer as separate structures.
3. Save, close normally, and reopen from actual stored location. Confirm names, structure, formulas and original user strokes survive; do not infer success from a source-only test.
4. Qwen reads/updates the Board through a supported typed API/contract, without hand-editing `.9to1board` JSON or overwriting user's work. Reopen after a tool write and confirm exact content.
5. Switch between Maths, Law and CS without losing the active study state, while the same model conversation retains native tools.
6. Check edits, undo/redo, focus, accessibility and the agreed non-shiny Glow/Professional presentation. Full implementation/acceptance remain distinct.

## Coordination / safety
- Preserve source ownership on `main`, existing native coordinator/leases and SDK allocation. Do not create rival local background processes or spawn another model. An async shell job may run only if admitted by existing guards; do not assert it is running until its own receipt exists.
- Never modify the currently running profile or replace a user Board blindly. Read, back up, apply typed mutations, validate, save and reopen.
- Keep tldraw archives available during the transition. Visual history: https://www.tldraw.com/f/hzb0de2hUqcR4tOfqc7Z9 ; active: https://www.tldraw.com/f/QwMj0Vy3q4dHHda9V-9VF .

## Switch criterion
Transition primary revision UI to 9to1 only after an actual editable, persisted Board passes end-to-end on the owner's computer. Until then tutor directly in OpenCode.
