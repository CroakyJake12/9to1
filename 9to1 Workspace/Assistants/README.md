# Assistants

Assistants is a standalone product for persistent configured personal AI
identities. Its dedicated home, configuration and conversation surfaces consume
the same Conversation, Task, model, tool, permission, project and resource owners
as Spaces. Opening an Assistant does not create or select a Space.

## Source ownership

`HavenOS.Assistants.Core.csproj` contains the product controller, public contracts
and canonical bridge. `NativeUI` contains the dedicated CUI presentation. An
executable host must compose these with the existing platform owners; Core never
references Desktop or owns another chat engine, Task database or permission broker.

Assistant identity is the stable Den/namespace/definition tuple issued through
the verified personal Home Den. Definition edits use that store's revision CAS
and operation identity. Conversation membership references the existing canonical
Conversation through a Den session record. Multiple conversations retain the
same Assistant identity. Resource IDs in saved configuration are preferences;
current permission and resource owners must authorize each actual operation.

Definitions explicitly classified as Assistant or Specialist use the new semantic
API. Unclassified legacy definitions remain preserved for explicit recoverable
migration; the product does not silently treat every saved Agent as an Assistant.
Specialists do not acquire personal proactive contact by classification.

## Presentation lifetime

The controller owns its presentation admissions and observed source subscriptions.
Retirement seals new presentation commands and joins their actual original Tasks.
Native resources detach only after the same retained close Task succeeds. Closing
a view does not cancel a durable business Task, dispose the shared Chat/Task
services or authorize restart from saved IDs. A failed or unknown original remains
visible as unresolved custody rather than a clean shutdown acknowledgment.

## Receiving status

The product source is being implemented. This file and its records/contracts do
not establish standalone application readiness. Windows composition, its actual
original Home/resource authority, supported package/publish entry point, rendered
create/edit/reopen/multiple-conversation workflows and canonical work controls
require owning runtime evidence. Browser additionally needs the actual browser
Home/Den owner and registered conversation/work route. Unavailable capabilities
must display their setup or availability reason and cannot report success.

The user-directed current-pass acceptance includes configured development work
on the same authorized Files/Spaces/Dev project; durable Tasks; checkpoints,
steering and recovery; Mini Computer; shared app/tool work; multimodal use; and
scoped proactive/scheduled behavior. None are discharged by a persisted setting
or by success in another product. Track Windows and web separately through
implemented, packageable, packaged, smoke-passed and extensively verified.

The relevant shared guidance is `shared/docs/architecture/state-and-persistence.md`,
`shared/docs/architecture/agent-observation-retirement.md`,
`shared/docs/ARCHITECTURE_RULES.md`, `shared/HAVEN_UI_RULES.md`, and
`shared/docs/VALIDATION_RULES.md`. The current user direction takes precedence
over the older retired Agents product description.
