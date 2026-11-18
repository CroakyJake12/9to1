# Spaces — conversations, workspaces and custom Space Builder — Usage; Local/Cloud Chats (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2202 · v2 lines 13048–13049

Usage
A usage bar should be displayed floating at the bottom of the chat list’s section of the screen. It should show the usage for 9to1 (daily/weekly/monthly, etc), plus any other measurable usage limits from other connected providers.

### V2-B2204 · v2 lines 13058–13058

Local/Cloud Chats

### V2-B2205 · v2 lines 13061–13062

9to1 MUST support Local, Mesh and Cloud chat storage. Local chats remain on the current device and MUST NOT enter account storage without an explicit eligible Send to Cloud action. Cloud chats use encrypted account storage and require a paid account with more than 10 GB allocated storage. Describe actual behaviour, for example “Your local chats live on this device”, without claiming absolute privacy. Storage controls, remote AI processing and replica lifecycle are governed by LE-07; restrained marketing wording never weakens these requirements.
Support sending local chat directly to another device within your mesh in addition to the cloud.

### V2-B2206 · v2 lines 13065–13068

Supported Chat Modes:
* Local - Current Device only
* Mesh - All Mesh Devices
* Cloud - All Mesh Devices + Backed up to the Cloud

### V2-B2207 · v2 lines 13071–13071

Temporary chats are ALWAYS local-only and cannot be transferred to the cloud, mesh, or another device without being saved first.

### V2-B2208 · v2 lines 13074–13082

LE-07 — Chat storage, transfer and privacy contract
Storage mode is a property of the existing canonical conversation and its managed attachments/derived records, not a separate Local Chats database. Local permits storage on the current device only; Mesh permits authorised replicas across devices in the selected personal/team namespace; Cloud adds encrypted account-storage backup to that Mesh scope. All Mesh Devices never means every device in an unrelated organisation. A newly created chat defaults to Local unless the user has explicitly chosen another eligible default. Existing chats retain their known placement on upgrade; do not migrate them silently.
Persist ConversationID, requested/effective storage mode, authorised namespace/destination DeviceIDs, storage-policy revision and transfer state through the existing Spaces/Den/Files/Home contracts. Transfers expose JobID and per-destination Pending, Transferring, Synced, Failed or Blocked state with revision acknowledgements and retry/cancel controls. Cloud access requires a server-authoritative paid entitlement and StorageAllocatedBytes greater than 10,000,000,000 bytes; available quota is a separate check. Exactly 10 GB does not qualify, and a client label or Send to Cloud action cannot bypass entitlement checks.
Send to Cloud is an explicit promotion. Send to Device previews one authorised Mesh destination and sends only there; it does not silently enable full Mesh or Cloud mode. The destination initially retains that received chat locally. Transfers preserve canonical history/branches and do not replay stored requests or tools. Independent edits reconcile through existing revision/branch conflict rules, never silent last-write-wins.
Before widening placement, show the destination/account, affected conversation and attachment scope, encryption/permission state and quota impact. Commit the widened policy only through authorised domain actions; pending transfers are not reported as complete. Cancellation or interruption retains a valid source copy and exposes any already completed destination copies. Retries are idempotent and recheck permissions, device revocation and entitlements.
Reducing placement stops new replication immediately. The UI MUST distinguish future sync policy from deletion of existing replicas. Show existing copies and offer an explicit, recoverable removal/reassignment flow under the owning storage policy. Offline cleanup remains pending until acknowledged; do not claim remote copies or external backups have been erased without evidence. Billing expiry/quota failure preserves local work and blocks newly unauthorised cloud writes rather than destroying the sole copy.
Temporary chats are always local-only. Their messages, attachments and derived chat state MUST NOT enter Mesh, account backup or durable cross-Space memory while temporary. Save creates a durable local conversation first; a separate authorised action is needed to widen placement. Direct UI/API/Automation transfer attempts on an unsaved temporary chat return TemporaryChatMustBeSaved.
Storage locality and AI/tool execution locality are separate controls. A Local chat MUST NOT be uploaded for account storage or silently exposed through sync, search indexes, telemetry, memory or logging. Remote model/tool processing is allowed only through the separately authorised privacy route, clearly disclosed; a configured cloud fallback alone is not permission. Enforce the global Privacy Ceiling, encryption and log-minimisation rules. User copy must describe verified behaviour, not promise absolute privacy or treat restrained wording as permission for accidental uploads.
Acceptance: inspect network/storage effects for Local and temporary chats; reject temporary transfers through UI and API; save then explicitly promote; send to one device without propagating to others; enforce paid, exact-threshold, quota and revoked-device failures; interrupt/retry transfers without duplicate history; handle offline conflicts; verify downshift/replica-cleanup status and denial of unauthorised cloud-model fallback.

### V2-B2209 · v2 lines 13085–13086

Study Space Update
Study will be updated to utilise the new custom sidebar system.

### V2-B2210 · v2 lines 13089–13091

New Sidebar:
| New Chat |
| Subjects |

### V2-B2211 · v2 lines 13094–13097

– Study Tools –
| My Progress | // RAG topic tracker
| Planner | // opens the study burrow* inside planner, embedded into the spaces app (*more on this later).
| Revision Bank** |

### V2-B2212 · v2 lines 13100–13101

– Revise These –
| [ 3 topics (the 3 slots shared across subjects, not 3 each), opens a new chat for revision. Pulls from your hardest topics or ones not reviewed for a while] |

### V2-B2213 · v2 lines 13104–13105

– Recent Chats –
| [Recent Chat List] |

### V2-B2214 · v2 lines 13108–13112

LE-08 — Study state and recommendations
The listed Study sidebar is its default layout under the shared custom-sidebar system, not a private second sidebar implementation. Planner opens the canonical Study burrow in an embedded Planner tab. My Progress opens the existing topic tracker, and Revision Bank opens the resource catalogue below.
Revise These contains at most three distinct topics across all subjects combined; show fewer when fewer eligible topics exist. Candidates come from the user's recorded difficulty/proficiency and review dates. Use a deterministic, versioned ranking: recorded weak topics first, then overdue or oldest-reviewed topics, with stable TopicID tie-breaking; do not invent a low proficiency for an unassessed topic. Explain the recommendation basis and open a new Study chat bound to the selected topic and authorised sources.
Cards self-ratings and quiz/answer results are recorded as attributed review evidence with topic links and timestamps. They feed the existing Study progress/review service, not an incompatible Cards-only mastery score. Displaying, opening or flipping a card alone is not proof of proficiency. Topic rename/move and resource reclassification preserve stable links.
Acceptance: populate several subjects and prove there are only three slots total; test fewer/no eligible topics; record a review and verify recommendation/progress updates; open the expected topic chat; confirm unreviewed content is not reported as mastered.

### V2-B2215 · v2 lines 13115–13116

Revision Bank
Revision Bank is the persistent catalogue of the user’s created revision materials, including flashcards, past papers, notes, quizzes and other supported resources. It retains canonical references to their owning artifacts rather than maintaining duplicate payload storage, as specified in LE-09.

### V2-B2216 · v2 lines 13119–13120

It is split into categories with a tabber (but not the same type as the horizontal tabs, just a navigation tabber)
Eg

### V2-B2217 · v2 lines 13125–13126

The split should be
| Recommended* | All | Flashcards | Notes | Papers | Quizzes | 

### V2-B2218 · v2 lines 13129–13129

*pulled from your progress tracker, resources for difficult and ignored topics.

### V2-B2219 · v2 lines 13132–13132

You can create custom sections and add them, like “Homework.” Sections can also be added automatically (if the option is enabled) if you have a certain type of material not covered (eg revision games).

### V2-B2220 · v2 lines 13135–13135

You can also create new materials directly from the screen, with a right-aligned button on the same row as the category switcher.

### V2-B2221 · v2 lines 13138–13146

| + Create New |
→ Describe a Resource
→ Flashcard
→ Quiz
→ Document
→ Presentation
→ Canvas
→ Board 
→ Other (Dev)

### V2-B2222 · v2 lines 13149–13149

Quizzes should be built using Forms.

### V2-B2223 · v2 lines 13152–13152

Clicking on a revision material should open it in its respective app (embedded into the spaces app, but the user can choose to open it directly in that app via either right clicking on the revision material and selecting it or right clicking the tab the embed opened in and clicking open in app) - usually as a viewer/interacter rather than an editor by default and then the user can switch to editing mode.

### V2-B2224 · v2 lines 13155–13161

LE-09 — Revision Bank ownership and operations
Revision Bank is a persistent Study resource catalogue over canonical artifacts, not a second file store. It includes created revision materials, existing materials explicitly enrolled/imported and future materials classified by an authorised rule; it MUST NOT silently expose unrelated private documents. Each ResourceID records owning app, canonical ArtifactID/FileID or source reference, resource type, subject/topic links, category membership, provenance, owner/scope and revision. Files/owning apps retain payload, sharing, history and deletion authority.
Recommended, All, Flashcards, Notes, Papers and Quizzes are the default category tabs. Custom categories such as Homework have stable IDs and configurable order. Automatic creation of a category for an uncovered material type is opt-in and idempotent. Categories are filters/membership views, not duplicated artifacts. Recommended draws from recorded difficult/neglected topics and authorised linked resources, with an honest empty state when no match exists.
+ Create New MUST remain right-aligned on the category row and expose Describe a Resource, Flashcard, Quiz, Document, Presentation, Canvas, Board and Other (Dev). Creation routes through Cards, Forms, Write, Present, Canvas, Boards and Dev respectively. Describe a Resource uses Dulche to select an appropriate existing owning type and perform authorised creation; it does not invent another resource app. A failed or cancelled creation must not leave a false completed Bank entry.
Resources open in the real owning-app viewer/interactor embedded in Spaces, with an explicit switch to permitted editing. Open in App is available from both resource and tab context menus and an accessible non-right-click route. Both views address the same artifact. Missing apps/modules follow LE-02; revoked sources cannot remain readable through cached previews or recommendations.
Users can add/enrol, search/filter, classify, reclassify, remove from Bank, open, create and recover supported resources through typed APIs. Remove from Bank removes catalogue membership only; deleting the source is a distinct confirmed owning-app operation. Source rename/move/delete/restore and permission changes update the catalogue incrementally while preserving identity. Persisting an inline-generated flashcard set or quiz uses Cards or Forms canonical data, not a screenshot-only copy.
Acceptance: create each named resource type through the menu; restart; find it in the correct category; edit it embedded and reopen the same ID in its app; rename/move its file; remove only its Bank membership; restore a source; revoke sharing; verify opt-in category creation and source-backed recommendations without duplicated content.

### V2-B2281 · v2 lines 13393–13394

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

V2 — 12 OCT 2026 SPACES UI NORMATIVE INCORPORATION BY REFERENCE (WORK IN PROGRESS)
The linked “9to1 V2 — Spaces UI Decisions & Working Specification (12 Oct 2026)” is incorporated into this V2 master for its clearly labelled OWNER-CONFIRMED decisions. It supersedes conflicting earlier V2 instructions only on those specific matters, including the current global Assistants/Specialists system, with canonical conversation views retained in Spaces. Its PROPOSED designs and OPEN decisions remain non-normative until further owner approval. Spaces interface review is NOT complete; no implementation or acceptance has been verified by this UI discussion. Read the companion document at https://docs.google.com/document/d/12AbDnWynZjVbKZ5XhkkiGMmJkINOSqMfnPuHuI6XDBE/edit . This incorporation does not remove unrelated requirements, including V2 privacy, CUI and acceptance rules. For stage-agnostic future-chat continuity consult https://docs.google.com/document/d/1BWekbHUO0BqUSoIubtBacOcu5ftGztRd-blCJcLyGW0/edit .

### V2-B2282 · v2 lines 13397–13398

STUDY (13 OCT 2026) — Owner-approved detailed UI specification STUDY-UI-201–235, STUDY-AC-201–219: https://docs.google.com/document/d/17Q_KpQjNgjEAtdVq9HwsnMYveFn6zfIbZBl3es5bptg/edit . Incorporated through the linked Spaces companion; supplements LE-08–12. Study remains in progress; no implementation verification is claimed.
<!-- UX27:BEGIN SECTION-13 -->

### V2-B2332 · v2 lines 13497–13497

Experiences retains its canonical structured state and visibility rules, separate from prose presentation. A narrative statement of a state change is not proof the domain transition committed. Preserve age-appropriate content controls and permission boundaries; this specification does not authorise bypasses or unsafe simulations.

### V2-B2706 · v2 lines 14606–14606

## Spaces

### V2-B2707 · v2 lines 14608–14608

### UX27-TEST-063 — Direct Space entry and retained draft

### V2-B2708 · v2 lines 14610–14610

**Requirements:** UX27-SPACES-001, UX27-SPACES-005 · **Status:** NOT RUN

### V2-B2709 · v2 lines 14612–14612

**Given:** Two Spaces each with a draft and stored conversation.

### V2-B2710 · v2 lines 14614–14616

**When:**
1. Open one from the chosen rail/dropdown.
2. Switch Space, create a chat, then return and restart.

### V2-B2711 · v2 lines 14618–14621

**Then:**
- No unwanted management interstitial.
- Both retained navigation structures and the correct scoped history remain.
- Drafts and attachments belong to their original conversations.

### V2-B2712 · v2 lines 14623–14623

### UX27-TEST-064 — Composer shelf state and browse versus activate

### V2-B2713 · v2 lines 14625–14625

**Requirements:** UX27-SPACES-002, UX27-SPACES-003 · **Status:** NOT RUN

### V2-B2714 · v2 lines 14627–14627

**Given:** Idle, working-with-draft and stopped states.

### V2-B2715 · v2 lines 14629–14631

**When:**
1. Scroll/swipe the action shelf without pressing its action.
2. Activate one action; change runtime state during another press.

### V2-B2716 · v2 lines 14633–14636

**Then:**
- At most five contextual actions with peek/dots follow the state table.
- Browsing sends nothing.
- State drift cancels/revalidates instead of invoking the wrong consequential action.

### V2-B2717 · v2 lines 14638–14638

### UX27-TEST-065 — Queue Stop and Resume are durable distinctions

### V2-B2718 · v2 lines 14640–14640

**Requirements:** UX27-SPACES-004 · **Status:** NOT RUN

### V2-B2719 · v2 lines 14642–14642

**Given:** Active eligible run with a queued draft and permission-controlled action.

### V2-B2720 · v2 lines 14644–14647

**When:**
1. Queue once with a retried transport receipt.
2. Stop and inspect settlement.
3. Resume where supported.

### V2-B2721 · v2 lines 14649–14652

**Then:**
- One durable queued item, not duplicate work.
- Stopping is distinct from settled Stop; previous effects are not falsely reversed.
- Resume retains identity and cannot answer a permission hold.

### V2-B2722 · v2 lines 14654–14654

### UX27-TEST-066 — Rich message rendering and read position

### V2-B2723 · v2 lines 14656–14656

**Requirements:** UX27-SPACES-006 · **Status:** NOT RUN

### V2-B2724 · v2 lines 14658–14658

**Given:** Streaming reply with Markdown, code, table and citations.

### V2-B2725 · v2 lines 14660–14662

**When:**
1. Scroll up while generation continues.
2. Copy code and invoke an applicable message action.

### V2-B2726 · v2 lines 14664–14667

**Then:**
- Streaming updates one message without hijacking deliberate scroll position.
- Copy preserves code whitespace.
- Actions target the selected message/revision and disabled states are truthful.

### V2-B2727 · v2 lines 14669–14669

### UX27-TEST-067 — Summary and task filters use real state

### V2-B2728 · v2 lines 14671–14671

**Requirements:** UX27-SPACES-007 · **Status:** NOT RUN

### V2-B2729 · v2 lines 14673–14673

**Given:** Task Space with current and other-Space tasks in several statuses.

### V2-B2730 · v2 lines 14675–14677

**When:**
1. Inspect Summary/Action Graph, default task filter and All Tasks.
2. Open a checkpoint then change filter.

### V2-B2731 · v2 lines 14679–14682

**Then:**
- The agreed surfaces and meaningful badges reflect actual canonical records.
- Filter changes do not duplicate or complete tasks.
- Checkpoint viewed state follows its actual contract.

### V2-B2732 · v2 lines 14684–14684

### UX27-TEST-068 — Sidebar activation rechecks permissions

### V2-B2733 · v2 lines 14686–14686

**Requirements:** UX27-SPACES-008 · **Status:** NOT RUN

### V2-B2734 · v2 lines 14688–14688

**Given:** Extension entries for URL, app, authored CUI and declared action.

### V2-B2735 · v2 lines 14690–14692

**When:**
1. Activate permitted targets.
2. Revoke/detach the extension while an entry is displayed, then activate it.

### V2-B2736 · v2 lines 14694–14697

**Then:**
- Typed owning routes/actions execute when authorised.
- Stale/revoked contribution cannot execute through cached visibility.
- Unrelated conversations remain intact and no string-execution fallback occurs.

### V2-B2737 · v2 lines 14699–14699

### UX27-TEST-069 — Model group selection loads only on Send

### V2-B2738 · v2 lines 14701–14701

**Requirements:** UX27-SPACES-009 · **Status:** NOT RUN

### V2-B2739 · v2 lines 14703–14703

**Given:** Group containing two compatible local members and a permission-restricted remote route.

### V2-B2740 · v2 lines 14705–14708

**When:**
1. Switch members repeatedly without sending.
2. Send with the selected member; inspect actual routing.
3. Change selection during the run.

### V2-B2741 · v2 lines 14710–14713

**Then:**
- Picker changes do not repeatedly load/unload models.
- Send resolves current capability/permission and records actual member/backend.
- Running request remains correctly pinned; no silent paid fallback.

### V2-B2742 · v2 lines 14715–14715

### UX27-TEST-070 — Effort and allowance are not conflated

### V2-B2743 · v2 lines 14717–14717

**Requirements:** UX27-SPACES-010, UX27-SPACES-011 · **Status:** NOT RUN
