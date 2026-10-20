# Chat durable-draft gate — verified gap analysis and safe design constraints

Author: Chat/Spaces lane (OpenCode worker, branch `opencode/m3-shell-wiring-20261016`).
Date: 2026-10-18. **Analysis only — no source change in this file's scope.**
This is the "durable drafts/ConversationID restoration" gate the model-unavailable assignment
lists as an explicit independent gate (assignment line 11), distinct from the committed
`9fc57054` milestone.

## Verified current state (exact code)

Canonical durable facility already exists and is already injected into the replacement page:

- `ConversationDraft` row: `(ConversationId, BranchId, Content, AttachmentIdsJson, UpdatedAt)`.
- `IConversationProductionRepository.GetDraftAsync / SaveDraftAsync / DeleteDraftAsync`
  (`shared/src/Haven.Application/ConversationProductionAbstractions.cs:67` region).
- `NewChatPage` already receives it (`_conversationProduction`, `NewChatPage.cs:37`/`145`), and
  `NewChatPage.Attachments.cs:147-167` (`SavePendingAttachmentDraftOriginalBodyAsync`) already
  writes **the live composer text** into `ConversationDraft.Content` plus the pending attachment
  ids. So the storage shape and the writer are canonical; nothing fake is required.

Three concrete defects in the replacement page's use of it:

1. **No write is triggered by typing.** The only callers of
   `SavePendingAttachmentDraftAsync` are attachment-change paths
   (`NewChatPage.cs:813, 2173, 2224`, `NewChatPage.Attachments.cs:54`). A text-only draft typed and
   then closed is never persisted, which is the native "drafts remain empty" observation.
2. **Restore never restores the text.** `LoadPendingPersistedAttachmentsOriginalBodyAsync`
   (`NewChatPage.Attachments.cs:94-131`) reads the draft, then returns at line 106 when
   `draft.AttachmentIdsJson` is blank, and even on the attachment path it only restores ids,
   never `draft.Content` into the composer.
3. **A text-only dispatch does not delete the draft.** `AssociatePendingAttachmentsWithUserMessage-
   OriginalBodyAsync` (`NewChatPage.Attachments.cs:171-189`) returns early at line 175 when
   `_pendingAttachmentIds.Count == 0`, so its `DeleteDraftAsync` (line 187) never runs for a
   text-only send: once defect 1 is fixed, the just-sent instruction would resurrect as a draft on
   the next reopen.

## Why a naive page-local "autosave on edit" is unsafe here (verified, not assumed)

1. **A debounce that is admitted page work cannot be cancelled cleanly at close.** The lifetime
   cancels `_lifetime` first and then awaits every admitted original
   (`DesktopOriginalWorkLifetime.CloseCoreAsync`, lines 236-248); an `OperationCanceledException`
   observed from an original is *not* waived (`Throw`, lines 280-286: "Unknown OCE/empty aggregate
   is not waived"). A pending `Task.Delay` debounce original would therefore turn a close into a
   failure.
2. **A write that spans a conversation transition retains an invariant failure.** Every admitted
   original carries a publication guard `!_disposed && _conversation.Id == target.ConversationId
   && _originalPresentationGeneration == target.Generation`
   (`NewChatPage.OriginalLifetime.cs:23-24`), re-demanded after each awaited call
   (`AcceptOriginalAwaitedTransition`, line 64-69). `ResetToFreshConversation` bumps the generation
   (`NewChatPage.cs:2252`) and `AdmitOriginalConversationLoad` replaces the conversation
   (`NewChatPage.OriginalLifetime.cs:58-60`). A draft write in flight across either transition
   faults with `InvalidOperationException: The original conversation presentation was replaced.`
   and the close reports it — this is the same pre-existing boundary independently reproduced at
   20/20 failure rate (see `shared/artifacts/chat-unavailable/EVIDENCE-chat-model-availability.md`).
   Per-edit autosave would *add* a high-frequency trigger to that boundary.
3. **There is no post-retirement flush window.** `RequestRetirement` immediately calls
   `AcquireClose` (`DesktopOriginalWorkLifetime.cs:179`), which sets `_retiring = true` under the
   gate (line 220); new admissions then throw `ObjectDisposedException` (lines 51/77/102).
   `RequestRetirement` also sets `_disposed = true` (`NewChatPage.OriginalLifetime.cs:75-76`), which
   makes the publication guard false. A flush admitted "at retirement" would fault; a flush cannot
   be admitted after it.

## Recommended safe shape (needs the shared/Dulche ownership the ledger already lists as open)

- Put draft persistence behind the shared conversation-draft ownership with a
  **retirement-aware flush**: the tab/window owner requests a flush, awaits it, and only then
  retires the page (so the write is complete before the admission seal and before the publication
  guard flips). Do not implement the flush inside the page's admitted-original cohort.
- Trigger writes on a human-scale boundary that does not race transitions (for example the owned
  flush above, plus the existing attachment-change writes), and coalesce them in the shared owner.
- Restore `draft.Content` on Load/Restore **only when the composer is currently empty** (never
  clobber text the user has already typed), before the attachment-ids early return.
- Delete the durable draft on dispatch **regardless of attachments** (fix defect 3 in the same
  change as defect 1, otherwise the first fix creates resurrection).
- Conversation identity restoration (the route lacking `ConversationID`) belongs to the same
  owner/route change: the route must carry the existing conversation id so reopening re-admits the
  SAME conversation rather than creating a fresh one.

## Verification available once implemented

- The maintained real-page rig (`Browse/Host.Tests/OverlayChatInitializationTests.cs`) already has
  the construction-join seam (`OverlayChatSessionFactory.CreateNewWithConstructionJoinAsync`) and
  the admitted-original drain helper, so restore/flush cases can be driven with the real production
  page and a real draft proxy without racing the transition boundary.
- A text-only dispatch delete is only exercisable where the real send/stream path is proxied; the
  current rig deliberately refuses unproxied provider I/O, so that assertion needs the send-path
  fixture (or the shared draft owner's own contract tests), not a weakened page test.
