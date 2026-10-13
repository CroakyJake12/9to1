# Temporary Chat local save

The native `NewChatPage` retains temporary messages and compact summaries in its live session. Its ordinary `ChatSessionService.SendWithTransientHistoryAsync` call supplies a validated, detached snapshot of prior user/assistant turns. The original `SendAsync` signatures remain compatible. Temporary sends do not read durable conversation history. This input grants no task, tool, or provider execution authority.

Choosing Saved explicitly calls `ITemporaryConversationSaveRepository` on the maintained local repository. The SQLite implementation commits the original conversation, messages, branch/version records, and compact summaries in one transaction. Original IDs, content, provenance, timestamps, and Space membership are retained. A collision with different saved content is refused. An exact retry acknowledges the same committed state.

The page does not publish Saved until the store acknowledges success. It retains the exact attempted snapshot after an uncertain acknowledgement and refuses history mutations until retry resolves that save. The existing original-work lifetime owns the raw save task and joins it on page retirement; retirement cannot publish a late success into a replaced page. The usage-tracking repository forwards local saving directly, without replaying model responses or consuming a later turn's usage buffer. Saving does not enable Mesh or Cloud.

Normal conversations cannot be relabelled Temporary. An empty saved draft is left intact and a new temporary identity is created through the maintained fresh-conversation reset path. Attached saved files must be resolved before that transition. A temporary descriptor is not live-session custody and cannot reload history from persistent storage.

## Verification and limits

`ConversationProductionRepositoryTests.Temporary.cs` verifies reopen, exact retry, collisions, and transaction rollback for message and context failures. `ChatSessionToolLoopTests.TransientHistory.cs` verifies context continuity, repeated identical turns, snapshot validation, and absence of persistent history access. `TemporaryConversationPageTests.cs` operates the actual native page with controlled local boundary acknowledgements, including save/close races.

These are scoped regression tests, not full Spaces acceptance. The existing separate dual-model comparison path, temporary attachment lifecycle, retained branch/version UI, provider usage attribution, cross-device persistence, and account isolation require their own integration evidence. Temporary regeneration currently explains that the chat must be saved before creating a durable regeneration branch; this is an outstanding product capability, not a completed temporary workflow.
